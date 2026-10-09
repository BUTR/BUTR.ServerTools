using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

using TaleWorlds.Library;
using TaleWorlds.ModuleManager;
using TaleWorlds.MountAndBlade;

namespace Bannerlord.ServerTools;

/// <summary>
/// Discovers and loads the appropriate implementation assembly for the currently running game version
/// (via <see cref="ImplementationSelector"/>) and instantiates its <c>&lt;ModuleId&gt;.SubModule</c>.
/// This loader relies exclusively on APIs available since Bannerlord v1.0.0 without patching any game code;
/// the loader's own <c>SubModule</c> simply forwards lifecycle callbacks to the loaded implementation.
/// </summary>
internal static class ImplementationLoader
{
    public static ImplementationSubModule? Load(Assembly loader, string moduleId, Action<string> log)
    {
        try
        {
            var directory = Path.GetDirectoryName(loader.Location);
            var implementations = string.IsNullOrEmpty(directory) ? [] : ImplementationSelector.Find(directory!, moduleId);
            if (implementations.Count == 0)
            {
                log($"No implementations found in '{directory}'.");
                return null;
            }

            var gameVersion = GetGameVersion();
            var selected = ImplementationSelector.Select(implementations, gameVersion);
            if (selected is null)
            {
                log($"The running game version (v{gameVersion}) is older than v{implementations[0].GameVersion}, which is the minimum version supported by this module.");
                return null;
            }
            if (gameVersion is null)
                log("Could not determine the game version; falling back to the newest implementation.");

            // Resolve the SubModule type by name (matching how the game resolves SubModuleClassType).
            // This prevents premature loading of dependencies that might not yet be available
            // (e.g. ASP.NET Core assemblies in ServerInfo before the web panel initializes).
            var assembly = Assembly.LoadFrom(selected.Path);
            if (assembly.GetType($"{moduleId}.SubModule") is not { } type || !typeof(MBSubModuleBase).IsAssignableFrom(type))
            {
                log($"Implementation '{selected}' does not contain a valid SubModule class '{moduleId}.SubModule'.");
                return null;
            }

            log($"Loading implementation '{selected}' for game version v{gameVersion?.ToString() ?? "unknown"}.");
            return new ImplementationSubModule((MBSubModuleBase) Activator.CreateInstance(type));
        }
        catch (Exception e)
        {
            log($"Failed to load implementation: {e}");
            return null;
        }
    }

    private static Version? GetGameVersion() => GetApplicationVersion() ?? GetNativeVersion();

    /// <summary>
    /// Reads the game version via <c>ApplicationVersion.FromParametersFile</c>, matching the game's native version discovery.
    /// Invoked via reflection to remain resilient against parameter changes across game updates.
    /// Dedicated servers lack <c>Parameters/Version.xml</c> and return <c>ApplicationVersion.Empty</c>, falling back to Native.
    /// </summary>
    private static Version? GetApplicationVersion()
    {
        try
        {
            var method = typeof(Debug).Assembly.GetType("TaleWorlds.Library.ApplicationVersion")?
                .GetMethod("FromParametersFile", BindingFlags.Public | BindingFlags.Static);
            var arguments = method?.GetParameters()
                .Select(parameter => parameter.HasDefaultValue ? parameter.DefaultValue : parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null)
                .ToArray();
            return ImplementationSelector.ParseVersion(method?.Invoke(null, arguments)?.ToString());
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the version attribute from Native's <c>SubModule.xml</c>, which reflects the game version on both clients and dedicated servers.
    /// </summary>
    private static Version? GetNativeVersion()
    {
        try
        {
            var subModule = Path.Combine(ModuleHelper.GetModuleFullPath("Native"), "SubModule.xml");
            var version = XDocument.Load(subModule).Root?.Element("Version")?.Attribute("value")?.Value;
            return ImplementationSelector.ParseVersion(version);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>
/// Wraps the loaded implementation's SubModule instance.
/// Binds protected lifecycle methods via reflection delegates upon creation for low-overhead callback forwarding.
/// </summary>
internal sealed class ImplementationSubModule
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly Action? _onSubModuleLoad;
    private readonly Action? _onSubModuleUnloaded;
    private readonly Action<float>? _onApplicationTick;

    public ImplementationSubModule(MBSubModuleBase subModule)
    {
        _onSubModuleLoad = Bind<Action>(subModule, "OnSubModuleLoad");
        _onSubModuleUnloaded = Bind<Action>(subModule, "OnSubModuleUnloaded");
        _onApplicationTick = Bind<Action<float>>(subModule, "OnApplicationTick", typeof(float));
    }

    public void OnSubModuleLoad() => _onSubModuleLoad?.Invoke();

    public void OnSubModuleUnloaded() => _onSubModuleUnloaded?.Invoke();

    public void OnApplicationTick(float dt) => _onApplicationTick?.Invoke(dt);

    private static TDelegate? Bind<TDelegate>(MBSubModuleBase subModule, string name, params Type[] parameters) where TDelegate : Delegate =>
        subModule.GetType().GetMethod(name, Instance, null, parameters, null) is { } method
            ? (TDelegate) Delegate.CreateDelegate(typeof(TDelegate), subModule, method)
            : null;
}
