using Bannerlord.ServerTools;

using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Bannerlord.ServerJoin;

/// <summary>
/// Module loader entry point. Automatically resolves and loads the implementation build tailored
/// for the running game client version. See <see cref="ImplementationLoader"/>.
/// </summary>
public sealed class SubModule : MBSubModuleBase
{
    private ImplementationSubModule? _implementation;

    protected override void OnSubModuleLoad()
    {
        base.OnSubModuleLoad();
        _implementation = ImplementationLoader.Load(typeof(SubModule).Assembly, "Bannerlord.ServerJoin", message => Debug.Print($"[ServerJoin] {message}"));
        _implementation?.OnSubModuleLoad();
    }

    protected override void OnSubModuleUnloaded()
    {
        _implementation?.OnSubModuleUnloaded();
        base.OnSubModuleUnloaded();
    }

    protected override void OnApplicationTick(float dt)
    {
        base.OnApplicationTick(dt);
        _implementation?.OnApplicationTick(dt);
    }
}
