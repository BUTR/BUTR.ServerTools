using System.Threading.Tasks;

using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Diamond;

namespace Bannerlord.ServerJoin;

/// <summary>
/// Automatically connects to the server specified via the <c>/joinserver</c> command-line parameter
/// once the multiplayer lobby is ready. Launch the game with <c>/multiplayer</c> to navigate directly to the lobby.
/// Interacts solely via standard game APIs without patching any game code.
/// </summary>
public sealed class SubModule : MBSubModuleBase
{
    // The lobby interface needs a brief moment to settle after entering AtLobby; connecting immediately can race the initial server list refresh.
    private const float SettleSeconds = 1.5f;
    private const float GiveUpSeconds = 180f;

    private JoinRequest? _request;
    private Task? _login;
    private float _waited;
    private float _atLobby;

    protected override void OnSubModuleLoad()
    {
        base.OnSubModuleLoad();
        // Emulate how Module.ProcessApplicationArguments reads arguments: retrieve engine command line and split by whitespace.
        var commandLine = TaleWorlds.Engine.Utilities.GetFullCommandLineString() ?? "";
        _request = JoinRequest.FromCommandLine(commandLine.Split(' '));
    }

    protected override void OnApplicationTick(float dt)
    {
        base.OnApplicationTick(dt);
        if (_request is null)
            return;

        _waited += dt;
        if (_waited > GiveUpSeconds)
        {
            Stop($"Timed out waiting to join {_request}: the multiplayer lobby did not become ready. Ensure the game was launched with '/multiplayer'.");
            return;
        }

        if (Game.Current?.GameStateManager?.ActiveState is not LobbyState lobbyState || NetworkMain.GameClient is not { } client)
        {
            _atLobby = 0;
            return;
        }

        // LobbyState.OnInitialize only signs in automatically if multiplayer privileges have already been verified (normally performed by the main menu).
        // When launched directly with /multiplayer, the client pauses at "Not Logged In". Emulate clicking the Login button:
        if (_login is null && client.IsIdle && !lobbyState.IsLoggingIn)
        {
            ServerJoiner.Message("Signing in to the multiplayer lobby...");
            _login = lobbyState.TryLogin();
            return;
        }

        if (client.CurrentState != LobbyClient.State.AtLobby)
        {
            _atLobby = 0;
            // When sign-in fails, the game displays its native "Login Failed" dialog.
            if (_login is { IsCompleted: true } && !lobbyState.IsLoggingIn && client.IsIdle)
                Stop("Unable to sign in to the multiplayer lobby.");
            return;
        }

        _atLobby += dt;
        if (_atLobby < SettleSeconds)
            return;

        // Perform only one join attempt per launch session so returning to the lobby later does not trigger a rejoin.
        var request = _request;
        _request = null;
        ServerJoiner.JoinAsync(lobbyState, client, request).ContinueWith(ServerJoiner.ReportFailure, TaskContinuationOptions.OnlyOnFaulted);
    }

    private void Stop(string message)
    {
        _request = null;
        ServerJoiner.Message(message);
    }
}
