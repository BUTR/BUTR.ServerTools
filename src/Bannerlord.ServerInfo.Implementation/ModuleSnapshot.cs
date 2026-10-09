using System.Collections.Generic;
using System.Linq;

using TaleWorlds.ModuleManager;
#if GAME_1_0_2_OR_GREATER
using TaleWorlds.MountAndBlade.Diamond;
#endif

namespace Bannerlord.ServerInfo;

/// <summary>
/// Represents a module loaded by the server and its classification for client matchmaking sessions.
/// </summary>
/// <param name="Id">The unique module identifier.</param>
/// <param name="Name">The display name of the module.</param>
/// <param name="Version">The module version string.</param>
/// <param name="IsOfficial">Indicates whether this is an official TaleWorlds module.</param>
/// <param name="IsClientModule">Indicates whether the module is included in session metadata reported to the lobby.</param>
/// <param name="IsClientRequired">Indicates whether connecting clients are strictly required to have this module installed. Other client modules are optional.</param>
internal sealed record LoadedModule(string Id, string Name, string Version, bool IsOfficial, bool IsClientModule, bool IsClientRequired);

/// <summary>
/// An immutable snapshot of loaded server modules captured during module initialization.
/// </summary>
internal sealed class ModuleSnapshot
{
    public IReadOnlyList<LoadedModule> Modules { get; }

    internal ModuleSnapshot(IReadOnlyList<LoadedModule> modules) => Modules = modules;

#if GAME_1_0_2_OR_GREATER
    /// <summary>
    /// Classifies loaded modules using the matchmaking lobby's session model (<c>ModuleInfoModel.TryCreateForSession</c>).
    /// Modules included in session metadata are published as client modules. Those not flagged as <c>IsOptional</c>
    /// are strictly required for connecting players (<c>ModuleInfoModelExtensions.IsCompatibleWith</c>), ensuring the published list
    /// accurately reflects game enforcement.
    /// </summary>
    public static ModuleSnapshot Create(string[] loadedModuleIds) => new(
        ModuleHelper.GetSortedModules(loadedModuleIds)
            .Select(module =>
            {
                var isClientModule = ModuleInfoModel.TryCreateForSession(module, out var model);
                return new LoadedModule(
                    module.Id,
                    module.Name,
                    module.Version.ToString(),
                    module.IsOfficial,
                    isClientModule,
                    isClientModule && !model.IsOptional);
            })
            .ToList());
#else
    /// <summary>
    /// Classifies modules for Bannerlord versions prior to v1.0.2 (before <c>ModuleInfoModel</c> and <c>MultiplayerOptional</c> were introduced).
    /// In these legacy versions, connecting clients are required to have all modules in the <see cref="ModuleCategory.Multiplayer"/> category,
    /// with no optional client modules supported.
    /// </summary>
    public static ModuleSnapshot Create(string[] loadedModuleIds)
    {
        var modules = ModuleHelper.GetModules().ToList();
        return new ModuleSnapshot(loadedModuleIds
            .Select(id => modules.FirstOrDefault(module => module.Id == id))
            .Where(module => module is not null)
            .Select(module => new LoadedModule(
                module.Id,
                module.Name,
                module.Version.ToString(),
                module.IsOfficial,
                module.Category == ModuleCategory.Multiplayer,
                module.Category == ModuleCategory.Multiplayer))
            .ToList());
    }
#endif
}
