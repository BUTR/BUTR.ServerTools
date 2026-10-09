# BUTR Server Tools

Two standalone modules designed for Mount & Blade II: Bannerlord custom servers, each released independently:

| Module | Runs on | Nexus Mods |
|---|---|---|
| [Bannerlord.ServerInfo](#bannerlordserverinfo) | Dedicated server | [mods/13895](https://www.nexusmods.com/mountandblade2bannerlord/mods/13895) |
| [Bannerlord.ServerJoin](#bannerlordserverjoin-joining-from-the-command-line) | Game client | [mods/13892](https://www.nexusmods.com/mountandblade2bannerlord/mods/13892) |

Both modules support every game version from v1.0.0 through the current beta with a single download. See
[Game versions](#game-versions).

## Bannerlord.ServerInfo

A dedicated server module that publishes which mods the server is running and which mods players need in order to join.
Community tools such as the Vortex server browser read this endpoint so players can see required mods and download links before attempting to connect.

Data is served via HTTP at `GET /butr/v1/server` on the server's game port over TCP. This uses the same embedded ASP.NET Core web host that
already serves the vanilla `/maps/list` endpoint. The endpoint specification and JSON schema are detailed in
[docs/server-info-endpoint.md](docs/server-info-endpoint.md).

### For server owners

1. Copy `Bannerlord.ServerInfo` into the server's `Modules` directory.
2. Add it to your module startup list: `_MODULES_*Native*Multiplayer*Bannerlord.ServerInfo*_MODULES_`.
3. Ensure the server's game port is open for TCP traffic in addition to UDP. While players connect via UDP, server browsers and external tools require TCP to query server info.

Because the module uses the `Server` module category, the game lobby never requires connecting clients to install it.

Loaded server modules are discovered and published automatically, with requirement status determined directly by the game:
- Modules enforced by the lobby upon connection are marked with `required: true`.
- Modules in the `MultiplayerOptional` category (which players can optionally install) are marked with `required: false`.
- Server-side community modules not required by clients are listed under `serverModules` for informational purposes.
- Client-only mods declared exclusively in `server-info.json` are not loaded on the server (and therefore never validated by the lobby), so they are always marked with `required: false`.

To provide download links, hide private admin tools, or recommend optional client mods, edit `Modules/Bannerlord.ServerInfo/server-info.json` and run `reload_server_info` in the dedicated server console to apply changes without restarting:

```jsonc
{
  "description": "Siege every evening, EU timezone.",
  "links": [ { "label": "Discord", "url": "https://discord.gg/..." } ],
  "clientModules": [
    // Provide download links for mods hosted on the server
    { "id": "MyArmorMod", "nexusMods": { "modId": 1234, "fileId": 5678 } },
    // Recommend client-only mods not installed on the server (always optional)
    { "id": "MyUiMod", "name": "My UI Mod", "version": "v1.0.0", "url": "https://..." }
  ],
  // Specify a Nexus Mods collection allowing players to install all mods at once.
  // The slug is found in the collection URL (nexusmods.com/games/mountandblade2bannerlord/collections/<slug>).
  // Omit "revision" to always target the latest revision.
  "nexusMods": { "collection": { "slug": "abc123", "revision": 4 } },
  // Server-only modules to omit from public listings (e.g. administrative tools)
  "hiddenServerModules": [ "MyPrivateAdminTools" ]
}
```

Using a collection is the most convenient way for players to install required mods. The Vortex server browser can install an entire collection with a single click. The collection should match the exact versions running on the server, as the game lobby strictly validates mod compatibility when players connect.

The configuration file is read once at server startup and re-read whenever `reload_server_info` is executed; incoming HTTP requests are served directly from an in-memory cached response. If the configuration file contains invalid syntax, an error is logged. At startup, fallback automatic module info is served; on reload, the existing valid configuration remains active until errors are resolved and reloaded.

### How it works

The module does not patch game code or use Harmony. During `OnSubModuleLoad`, it registers its assembly in the
`ASPNETCORE_HOSTINGSTARTUPASSEMBLIES` environment variable. When the dedicated server initializes its web host via `WebHost.CreateDefaultBuilder`,
ASP.NET Core automatically loads `ServerInfoHostingStartup`. This injects an `IStartupFilter` that maps `/butr/v1/server` ahead of
the web panel's middleware and authentication pipeline, keeping administrative endpoints fully secure.

Loaded modules are inspected once on the main game thread and classified using the lobby's session model
(`ModuleInfoModel.TryCreateForSession` and `IsOptional`). As a result, the published requirement flags always accurately reflect the rules enforced by the game when players connect.

## Bannerlord.ServerJoin: joining from the command line

A lightweight client module enabling direct connection to a custom server via the command line, allowing launchers and server browsers like Vortex to provide a one-click Join button:

```text
Bannerlord.exe /multiplayer _MODULES_*Native*Multiplayer*<server's modules>*Bannerlord.ServerJoin*_MODULES_ /joinserver <lobby id> <address>:<port>
```

Launching with `/multiplayer` starts Bannerlord directly in the multiplayer lobby. Normally, the lobby only signs in automatically if multiplayer privileges have already been verified by the main menu, causing direct launches to pause at "Not Logged In". `Bannerlord.ServerJoin` automates this process:

1. Signs into the multiplayer lobby using the same logic as the Login button (`LobbyState.TryLogin`).
2. Fetches the active custom server list.
3. Locates the server by lobby ID, falling back to IP address and port if the server has restarted and re-registered.
4. Triggers the game's map check, automatically displaying the native map download dialog if custom maps are missing.
5. Displays an in-game password prompt if the server is password protected.
6. Connects using the native join request used by the custom server browser.

Connection is attempted only once per game launch session. Either `<lobby id>` or `<address>:<port>` may be omitted.

The module is categorized under `MultiplayerOptional`, the game's native category for client-side mods. When connecting,
the lobby compares only core `Multiplayer` modules against the server, ignoring optional client modules so any server permitting optional mods will accept the connection. Servers configured with `/dedicatedcustomserverDontAllowOptionalModules` disallow optional modules; Vortex does not offer direct join for those servers. (Players can still join manually through the in-game browser if launched without this module).

While the module is loaded, the game session is flagged as modded. Consequently, quick play, matchmade games, and premade clan matches are disabled for that session (`MPMatchmakingVM.RefreshSubPageStates`). The custom server browser remains completely unaffected.

## Game versions

The game's API changes between versions, and a module built against one version can fail to load on another. Each module
ships its implementation built several times, once per range of game versions where the API it uses stays the same, plus
a small loader:

```text
Modules/Bannerlord.ServerJoin/bin/Win64_Shipping_Client/
  Bannerlord.ServerJoin.dll          the loader, the DLL SubModule.xml names
  Bannerlord.ServerJoin.v1.0.0.dll   v1.0.0 to v1.0.3
  Bannerlord.ServerJoin.v1.1.0.dll   v1.1.0 to v1.1.6
  Bannerlord.ServerJoin.v1.2.6.dll   v1.2.6 to v1.2.8
  Bannerlord.ServerJoin.v1.2.9.dll   v1.2.9 to v1.5.1
  Bannerlord.ServerJoin.v1.5.2.dll   v1.5.2 and later
```

The loader uses exclusively APIs that have been part of the game since v1.0.0. At startup, it determines the active game version
(via `ApplicationVersion`, or the Native module's manifest on dedicated servers), dynamically loads the build with the highest baseline
version that is less than or equal to the current game version, and forwards `SubModule` lifecycle callbacks.
No game code is patched, and neither module requires Harmony.

| Module | Build | Baseline version | Changes requiring a separate build |
|---|---|---|---|
| ServerJoin | v1.0.0 | v1.0.0 | Multiplayer lobby resided in Native's `TaleWorlds.MountAndBlade`; map checks were not yet implemented |
| | v1.1.0 | v1.1.0 | `InquiryData` constructor received two additional optional parameters |
| | v1.2.6 | v1.2.6 | Multiplayer extracted into its own module; `RequestJoinCustomGame` gained `isJoinAsAdmin` parameter |
| | v1.2.9 | v1.2.9 | Introduced `MapCheckHelpers` and the dedicated map download panel |
| | v1.5.2 | v1.5.2 beta | `RequestJoinCustomGame` signature changed to accept `CustomGameJoinType` |
| ServerInfo | v1.0.0 | v1.0.0 | Pre-dates `ModuleInfoModel`: required modules were determined strictly by the `Multiplayer` category |
| | v1.0.2 | v1.0.2 | Introduced `ModuleInfoModel.ShouldIncludeInSession` and the `MultiplayerOptional` category |
| | v1.2.7 | v1.2.7 | `DedicatedServerConsoleCommandManager` (used by `reload_server_info`) moved to the Multiplayer module |

Bannerlord dedicated servers originally ran on .NET Core 2.1, updated to .NET Core 3.1 in v1.0.2, and upgraded to .NET 6 in v1.2.7.
`Bannerlord.ServerInfo` targets `netstandard2.0` and runs seamlessly across all of them. It compiles against baseline abstractions that remain backward-compatible with later runtimes: ASP.NET Core 2.1.0 abstractions, and the `Newtonsoft.Json` dependency shipped with `TaleWorlds.Library` (11.0.2 for older builds, and 13.0.1 since v1.0.2). Neither dependency is packaged directly into the module.

Target builds are declared as `Implementation` items in each module's `.csproj`. Preprocessor symbols (`#if GAME_x_y_z_OR_GREATER`) isolate version-specific API differences. When a future game update introduces breaking API changes, simply declare a new implementation item for that baseline version and add the necessary conditional code.

Continuous Integration validates ABI compatibility using [tools/AbiCheck](tools/AbiCheck). The tool downloads the official
[reference assemblies](https://github.com/BUTR/Bannerlord.ReferenceAssemblies) published for each game release. For every build, it
verifies that the loader and the implementation selected by `ImplementationSelector` can successfully resolve every TaleWorlds type and member they reference, including method and field signatures. `ServerJoin` is verified against every build actively served across Steam branches (v1.0.0 through the current beta). `ServerInfo` is verified against every published dedicated server release. Any game update that breaks binary compatibility fails the Test workflow immediately.

> [!NOTE]
> ABI checks verify assembly references and runtime binding signatures; they do not simulate live gameplay behavior. Builds for archived versions that are no longer downloadable from Steam (game v1.0.0 to v1.2.8, and dedicated server builds prior to v1.4.7) cannot be tested against a live running game instance.

## Building

```shell
# Build release binaries and stage into artifacts/<ModuleId>/
dotnet build src/Bannerlord.ServerInfo -c Release
dotnet build src/Bannerlord.ServerJoin -c Release

# Build and deploy directly to a local game or dedicated server folder
dotnet build src/Bannerlord.ServerInfo -c Release -p:ModuleDeployPath="C:\...\Mount & Blade II Dedicated Server"
dotnet build src/Bannerlord.ServerJoin -c Release -p:ModuleDeployPath="C:\...\Mount & Blade II Bannerlord"
```

Each build command compiles the module loader and every implementation variant, staging the module layout into `artifacts/<ModuleId>/`. Providing `ModuleDeployPath` additionally copies the files directly into the target game or server directory. `ServerJoin` deploys to the client binary folder (Win64_Shipping_Client), while `ServerInfo` deploys to both Windows and Linux server binary directories. Module versions are defined by the latest entry in each module's `changelog.txt`.

To run tests and ABI checks locally:

```shell
dotnet test tests/Bannerlord.ServerTools.Tests -c Release
dotnet run --project tools/AbiCheck -- --module artifacts/Bannerlord.ServerJoin/bin/Win64_Shipping_Client --id Bannerlord.ServerJoin --package Bannerlord.ReferenceAssemblies.Core --package Bannerlord.ReferenceAssemblies.Multiplayer --steam-app 261550
dotnet run --project tools/AbiCheck -- --module artifacts/Bannerlord.ServerInfo/bin/Win64_Shipping_Server --id Bannerlord.ServerInfo --package Bannerlord.ReferenceAssemblies.Server.Core
```

When building an implementation project directly in an IDE, it builds against the current stable baseline by default. You can build against an alternate baseline by passing `-p:GameVersion=<version>` (e.g. `-p:GameVersion=1.4.8`).

## Releasing

Pushing to the `master` branch executes tests and automated checks, then automatically publishes any module whose `changelog.txt` contains a version that does not yet have an existing GitHub release.
Release artifacts are deployed to Nexus Mods and published as GitHub releases tagged `<ModuleId>-v<version>`. Pull requests and pushes to the `dev` branch run test suites and verification checks only.
