using System;
using System.IO;
using System.Linq;

using TaleWorlds.ModuleManager;
using TaleWorlds.MountAndBlade;

using EngineUtilities = TaleWorlds.Engine.Utilities;

namespace Bannerlord.ServerInfo;

public sealed class SubModule : MBSubModuleBase
{
    public const string ModuleId = "Bannerlord.ServerInfo";
    private const string HostingStartupAssembliesVariable = "ASPNETCORE_HOSTINGSTARTUPASSEMBLIES";

    /// <summary>
    /// Holds the active server information provider instance.
    /// Initialized once on the main game thread and read concurrently by incoming HTTP request threads.
    /// </summary>
    internal static ServerInfoProvider? Provider { get; private set; }

    protected override void OnSubModuleLoad()
    {
        base.OnSubModuleLoad();

        try
        {
            // Engine and module states are queried exclusively here on the main thread; HTTP requests read the immutable snapshot.
            var modules = ModuleSnapshot.Create(EngineUtilities.GetModulesNames());
            var configPath = Path.Combine(ModuleHelper.GetModuleFullPath(ModuleId), "server-info.json");
            Provider = new ServerInfoProvider(modules, new ServerInfoConfigFile(configPath, Log.Info, Log.Error));

            RegisterHostingStartup();
            Log.Info($"Publishing server information at {ServerInfoEndpoint.Path} ({modules.Modules.Count} modules loaded).");
        }
        catch (Exception e)
        {
            Log.Error($"Failed to start: {e}");
            return;
        }

        // Dedicated server console command registration is isolated so that endpoint hosting remains active even if command registration fails.
        try
        {
            ServerInfoCommands.Register();
            Log.Info($"After editing server-info.json, run '{ServerInfoCommands.Reload}' in the server console to apply changes.");
        }
        catch (Exception e)
        {
            Log.Error($"The '{ServerInfoCommands.Reload}' console command could not be registered: {e.Message}");
        }
    }

    /// <summary>
    /// Configures ASP.NET Core hosting startup integration.
    /// When the dedicated server initializes its web panel via <c>WebHost.CreateDefaultBuilder</c> after modules are loaded,
    /// the builder inspects the <c>ASPNETCORE_HOSTINGSTARTUPASSEMBLIES</c> environment variable and loads
    /// <see cref="ServerInfoHostingStartup"/>. This injects the info endpoint ahead of the panel's standard middleware pipeline
    /// without patching any game code.
    /// </summary>
    private static void RegisterHostingStartup()
    {
        var assemblyName = typeof(SubModule).Assembly.GetName().Name!;
        var current = Environment.GetEnvironmentVariable(HostingStartupAssembliesVariable);
        if (current is not null && current.Split(';').Contains(assemblyName, StringComparer.OrdinalIgnoreCase))
            return;

        Environment.SetEnvironmentVariable(
            HostingStartupAssembliesVariable,
            string.IsNullOrEmpty(current) ? assemblyName : $"{current};{assemblyName}");
    }
}
