using System;
using System.Linq;
using System.Threading.Tasks;

using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Diamond;
#if GAME_1_2_9_OR_GREATER
using TaleWorlds.MountAndBlade.Multiplayer;
#endif

namespace Bannerlord.ServerJoin;

/// <summary>
/// Executes the connection sequence to join a custom server specified via command-line arguments,
/// replicating the actions performed by the custom server browser UI (<c>MPCustomGameVM.JoinCustomGame</c>).
/// Network responses and scene transitions are handled natively by the game's <c>LobbyGameClientHandler</c>.
/// </summary>
internal static class ServerJoiner
{
    public static async Task JoinAsync(LobbyState lobbyState, LobbyClient client, JoinRequest request)
    {
        Message($"Searching for server {request}...");

        var serverList = await client.GetCustomGameServerList();
        var servers = serverList?.CustomGameServerInfos ?? new System.Collections.Generic.List<GameServerEntry>();
        var server =
            servers.FirstOrDefault(entry => request.LobbyId is { } id && entry.Id.Guid == id) ??
            servers.FirstOrDefault(entry => request.Address is not null && entry.Port == request.Port &&
                                            string.Equals(entry.Address, request.Address, StringComparison.OrdinalIgnoreCase));
        if (server is null)
        {
            ShowError($"The server {request} was not found in the custom server list. It may be offline, full, or restarting.");
            return;
        }

#if GAME_1_2_9_OR_GREATER
        // Triggering client refusal opens the map download interface in DedicatedCustomServer.ClientHelper,
        // identical to manually joining a server with missing maps. Older game versions did not perform map checks.
        var (isRefusedToJoin, notExistingMap) = await MapCheckHelpers.CheckMaps(server);
        if (isRefusedToJoin)
        {
            lobbyState.OnClientRefusedToJoinCustomServer(server);
            ShowError($"The map '{notExistingMap}' is missing or differs from the server version. Please download the required map and try joining again.");
            return;
        }
#endif

        if (server.PasswordProtected)
        {
            // Prompt for the password via the in-game UI rather than passing it via the command line where other processes could inspect it.
            InformationManager.ShowTextInquiry(new TextInquiryData(
                server.ServerName,
                "This server is password protected. Please enter the password to join:",
                isAffirmativeOptionShown: true,
                isNegativeOptionShown: true,
                GameTexts.FindText("str_ok").ToString(),
                GameTexts.FindText("str_cancel").ToString(),
                password => RequestJoinAsync(client, server, password).ContinueWith(ReportFailure, TaskContinuationOptions.OnlyOnFaulted),
                () => { },
                shouldInputBeObfuscated: true));
            return;
        }

        await RequestJoinAsync(client, server, null);
    }

    private static async Task RequestJoinAsync(LobbyClient client, GameServerEntry server, string? password)
    {
        Message($"Connecting to {server.ServerName}...");
#if GAME_1_5_2_OR_GREATER
        var joined = await client.RequestJoinCustomGame(server.Id, CustomGameJoinType.Player, password ?? "");
#elif GAME_1_2_0_OR_GREATER
        var joined = await client.RequestJoinCustomGame(server.Id, password ?? "", isJoinAsAdmin: false);
#else
        var joined = await client.RequestJoinCustomGame(server.Id, password ?? "");
#endif
        if (!joined)
            ShowError(GameTexts.FindText("str_couldnt_join_server").ToString());
    }

    public static void ReportFailure(Task task)
    {
        var error = task.Exception?.GetBaseException();
        if (error is null)
            return;
        Debug.Print($"[ServerJoin] {error}");
        ShowError($"Failed to join the server: {error.Message}");
    }

    public static void Message(string text)
    {
        Debug.Print($"[ServerJoin] {text}");
        InformationManager.DisplayMessage(new InformationMessage($"[Server Join] {text}"));
    }

    private static void ShowError(string text)
    {
        Debug.Print($"[ServerJoin] {text}");
        InformationManager.ShowInquiry(new InquiryData(
            "Server Join", text,
            isAffirmativeOptionShown: true, isNegativeOptionShown: false,
            GameTexts.FindText("str_ok").ToString(), "", null, null));
    }
}
