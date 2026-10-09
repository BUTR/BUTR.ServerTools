# Server Information Endpoint (Schema Version 1)

A dedicated server running [Bannerlord.ServerInfo](../README.md#bannerlordserverinfo) publishes information about the mods it hosts and the client mods players need in order to join.
This document defines the protocol contract: the HTTP interface served by the module and the data guarantees client applications can rely on.
The Vortex server browser extension serves as the reference client.

## Where it is served

Every Bannerlord dedicated server runs an embedded ASP.NET Core web host on its game port over TCP
(`DedicatedCustomServerWebPanelSubModule`, `http://*:{port}`). The vanilla server already serves its map list there,
without authentication, at `GET /maps/list`. `Bannerlord.ServerInfo` adds a dedicated anonymous endpoint to the same host:

```http
GET http://{address}:{port}/butr/v1/server
Accept: application/json
```

| Response | Condition |
| --- | --- |
| `200 OK`<br>`Content-Type: application/json; charset=utf-8`<br>`Cache-Control: no-cache` | Success. The module is running and serving server info. `HEAD` requests return the same headers without a response body. |
| `404 Not Found` | The server is not running the module, or the module failed during initialization. Check dedicated server console logs for details. |
| `405 Method Not Allowed`<br>`Allow: GET, HEAD` | An unsupported HTTP method was used. |

The endpoint is public and requires no authentication. Sensitive details—such as game server passwords, administrator credentials, or player session data—are never included. The response payload changes only when an administrator executes `reload_server_info` in the server console; because it is served directly from an in-memory cache, queries have negligible performance overhead on the server.

The `v1` path segment denotes the major schema version. Any future breaking schema changes will be introduced under a distinct endpoint path alongside `v1`.

## Body

```json
{
  "schemaVersion": 1,
  "description": "Siege every evening, EU timezone.\nRules: no team killing.",
  "links": [
    { "label": "Discord", "url": "https://discord.gg/example" },
    { "label": "Website", "url": "https://example.com" }
  ],
  "serverModules": [
    { "id": "Bannerlord.Harmony", "name": "Harmony", "version": "v2.3.3.207" },
    { "id": "MyServerAdminTools", "name": "My Server Admin Tools", "version": "v1.2.0.0" }
  ],
  "clientModules": [
    {
      "id": "MyVlandiaArmor",
      "name": "My Vlandia Armor",
      "version": "v1.0.4.0",
      "required": true,
      "nexusMods": { "modId": 1234, "fileId": 56789 }
    },
    {
      "id": "BetterMultiplayerHUD",
      "name": "Better Multiplayer HUD",
      "version": "v2.1.0",
      "required": false,
      "url": "https://example.com/better-hud"
    }
  ],
  "nexusMods": {
    "collection": { "slug": "abc123", "revision": 4, "required": true }
  }
}
```

Optional fields with no assigned value are omitted entirely rather than serialized as `null`.

| Field | Type | Description |
| --- | --- | --- |
| `schemaVersion` | number, always present | Integer version identifier (`1`). Schema 1 evolves solely via non-breaking additive fields. |
| `description` | string | Optional description text provided by the server administrator. Preserves meaningful line breaks. |
| `links` | array, always present | Array of `{ label, url }` objects, preserving the administrator's ordering. Empty array if none configured. |
| `serverModules` | array, always present | Modules active on the server that do not require client installation (e.g. server-side utilities, admin tools). Listed for informational purposes. |
| `clientModules` | array, always present | Modules that players need or can optionally install to connect to the server. |
| `nexusMods` | object | Server-wide Nexus Mods metadata (such as a shared collection). Omitted if not configured. |

### Modules

| Field | Type | Description |
| --- | --- | --- |
| `id` | string, always present | The module identifier matching the `Id` attribute in `SubModule.xml`. |
| `name` | string | Human-readable display name. Clients should fall back to `id` if omitted. |
| `version` | string | The version string from `SubModule.xml` (e.g. `v1.0.4` or `v1.0.4.0`). Omitting a changeset implies `0` (matching game convention). If omitted, any version is accepted. |
| `required` | boolean, client modules only | `true` if players must install the mod to join; `false` if the mod is optional. Determined strictly by the game engine, not by server configuration. |
| `nexusMods` | object, client modules only | `{ "modId": number, "fileId": number }` on the `mountandblade2bannerlord` game domain. `fileId` is optional and specifies an exact file release. |
| `url` | string, client modules only | Direct download or informational webpage for mods not hosted on Nexus Mods. |

#### Module categorization rules:

- **Official modules** (e.g. `Native`, `Multiplayer`, and official DLCs) are excluded from all listings.
- **`clientModules` from the server:** Community modules loaded by the dedicated server and reported in session metadata (`ModuleInfoModel.TryCreateForSession`, or the `Multiplayer` category on servers prior to v1.0.2). These entries reflect each module's actual loaded name and version. The `required` property is determined natively by the game: modules in the `MultiplayerOptional` category (`ModuleInfoModel.IsOptional`) are marked `false` (as the lobby permits players to join without them), while all other session modules are marked `true`. (Servers prior to v1.0.2 had no optional category). Administrator configuration may augment these entries with `nexusMods` IDs and download `url` links, but cannot alter `required`.
- **`clientModules` from configuration:** Additional modules defined in `server-info.json` that are not loaded by the server itself (such as recommended client-side UI mods). These entries adopt metadata supplied by the administrator and are always assigned `required: false` because the lobby does not validate modules the server does not host.
- **`serverModules`:** All other community modules loaded on the server that are not designated as client modules, excluding any listed in `hiddenServerModules`.

### Nexus Mods Collection

The server-wide counterpart to per-module `nexusMods` metadata. Future iterations of schema 1 may introduce additional properties.

| Field | Type | Description |
| --- | --- | --- |
| `collection` | object | A curated Nexus Mods collection for Bannerlord that players can install with one click. |

Collection properties:

| Field | Type | Description |
| --- | --- | --- |
| `slug` | string, always present | Lowercase collection identifier from the collection URL (`nexusmods.com/games/mountandblade2bannerlord/collections/<slug>`). |
| `revision` | number | Optional specific revision number to pin. If omitted, refers to the latest published revision. |
| `required` | boolean, always present | `true` if the collection is required; `false` indicates a recommended collection. |

Configuring a collection does not bypass the lobby's compatibility verification. Because the game still validates required modules when connecting, collections should contain all required server mods matching the versions running on the server.

## Recommended limits

Client applications may reject or truncate entries exceeding these suggested constraints. Server administrators should ensure their configuration conforms to these limits:

- `description`: Up to 4,000 characters.
- `links`: Up to 10 entries. Must use `http` or `https` URL schemes.
- `serverModules` and `clientModules`: Up to 256 entries each.
- `url`: Must use `http` or `https` schemes.
- `slug`: 4 to 32 lowercase alphanumeric characters.
- Overall JSON payload: Maximum of 1 MiB.

## Client implementation guidelines

- **Forward compatibility:** Ignore unrecognized fields. Schema 1 maintains backward compatibility by introducing additive fields.
- **Resilience:** Discard individual malformed entries rather than rejecting the entire response payload.
- **Fallback handling:** Treat HTTP `404`, connection timeouts, or non-200 status codes as an indication that the server does not publish metadata.
- **Security:** Construct Nexus Mods URLs directly within the client application using `modId` and `fileId` rather than relying on unverified third-party links.
