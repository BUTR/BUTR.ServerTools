using TaleWorlds.MountAndBlade;

namespace Bannerlord.ServerInfo;

/// <summary>
/// Dedicated server console commands.
/// <c>DedicatedServerConsoleCommandManager</c> discovers registered commands by finding methods decorated with
/// <see cref="ConsoleCommandMethodAttribute"/>. Type <c>list</c> in the server console to display available commands,
/// or <c>reload_server_info ?</c> for usage information.
/// </summary>
internal static class ServerInfoCommands
{
    public const string Reload = "reload_server_info";

    /// <summary>
    /// Registers console commands with the server command manager.
    /// (In Bannerlord v1.2+, the manager moved to the Multiplayer module while retaining the same type name and namespace).
    /// </summary>
    public static void Register() => DedicatedServerConsoleCommandManager.AddType(typeof(ServerInfoCommands));

    [ConsoleCommandMethod(Reload, "Reloads Modules/Bannerlord.ServerInfo/server-info.json from disk and updates the published endpoint")]
    private static void ReloadServerInfo()
    {
        if (SubModule.Provider is not { } provider)
            Log.Error("Server information provider is not active. Please inspect the startup logs for errors.");
        else if (provider.Reload())
            Log.Info("Successfully reloaded server-info.json and updated the published endpoint.");
        else
            Log.Error("Failed to reload server-info.json. Continuing to serve the previously loaded configuration. Please fix any syntax errors and try again.");
    }
}
