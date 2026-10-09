using System;

using Bannerlord.ServerTools;

using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Bannerlord.ServerInfo;

/// <summary>
/// Module loader entry point. Automatically resolves and loads the implementation build tailored
/// for the running dedicated server version. See <see cref="ImplementationLoader"/>.
/// </summary>
public sealed class SubModule : MBSubModuleBase
{
    private ImplementationSubModule? _implementation;

    protected override void OnSubModuleLoad()
    {
        base.OnSubModuleLoad();
        _implementation = ImplementationLoader.Load(typeof(SubModule).Assembly, "Bannerlord.ServerInfo", Log);
        _implementation?.OnSubModuleLoad();
    }

    protected override void OnSubModuleUnloaded()
    {
        _implementation?.OnSubModuleUnloaded();
        base.OnSubModuleUnloaded();
    }

    private static void Log(string message)
    {
        Console.WriteLine($"[ServerInfo] {message}");
        Debug.Print($"[ServerInfo] {message}");
    }
}
