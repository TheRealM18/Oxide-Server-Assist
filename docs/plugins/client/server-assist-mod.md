# Server Assist Mod (client)

> **Version:** 2.1.0 · **Type:** client-side 7 Days to Die code mod · **Source:** [ServerAssist](https://github.com/TheRealM18/Oxide-Server-Assist/tree/main/ServerAssist)

`ServerAssistMod.dll` is the client half of Server Assist. It owns the
`windowServerAssist` XUi window, parses the line protocol sent by the Oxide
plugin, registers the two Server Assist net packages, and forwards player
actions back to the server. The server plugin remains authoritative: the client
draws the board and executes only the `launch=` instruction the server approved.

The mod is required on the server and on **every client** that should see the
board. A server cannot push an XUi window to a client.

!!! important
    This page covers the client assembly and its protocol. For the product as a
    whole, read the [Server Assist product guide](../../core/server-assist.md).
    For first-time setup, read [Install Server Assist](../../start/install.md).

---

## Requirements

| Requirement | Detail |
|---|---|
| Game version | 7 Days to Die **V3.4.x**, built against the 3.4 managed assemblies |
| Anti-cheat | **EAC off** on the server and every client; `ModInfo.xml` sets `SkipWithAntiCheat` |
| Server | Oxide / uMod loading `ServerAssist.cs` 2.1.0 |
| Client files | `ServerAssistMod.dll`, `ModInfo.xml`, `Config/XUi_InGame/xui.xml`, `Config/XUi_InGame/windows.xml` |
| Shared styles | `Config/XUi_Common/styles.xml` is part of the current deploy set |
| Folder location | Directly under `Mods/`, for example `Mods/AA0_ServerAssistMod/` |
| Matching builds | Use the same Server Assist build on the server and every client |
| .NET | The game's .NET Framework 4.8 runtime; no separate install |

!!! warning
    Do not install two Server Assist client folders side by side. Duplicate XUi
    groups and duplicate net package names cause registration and window lookup
    failures. Remove the old folder before copying the new one.

---

## Files and deployment

The current client deploy set is defined by `deploy-to-pack.ps1`:

```text
AA0_ServerAssistMod/
  ServerAssistMod.dll
  ModInfo.xml
  Config/
    XUi_InGame/
      xui.xml
      windows.xml
    XUi_Common/
      styles.xml
  UIAtlases/            optional, from ServerAssistArt
    UIAtlas/
      sa_*.png
```

| File | Destination | Notes |
|---|---|---|
| `ServerAssistMod.dll` | `Mods/<folder>/` | Client assembly; must match the server build |
| `ModInfo.xml` | `Mods/<folder>/` | `Name=ServerAssist`, `Version=2.1.0`, `SkipWithAntiCheat=true` |
| `Config/XUi_InGame/xui.xml` | `Mods/<folder>/Config/XUi_InGame/` | Declares the `serverAssist` window group |
| `Config/XUi_InGame/windows.xml` | `Mods/<folder>/Config/XUi_InGame/` | Window layout, rows, buttons, controllers |
| `Config/XUi_Common/styles.xml` | `Mods/<folder>/Config/XUi_Common/` | Shared style keys used by the window |
| `UIAtlases/UIAtlas/sa_*.png` | `Mods/<folder>/UIAtlases/UIAtlas/` | Optional hand-painted card icons |

The release zip uses `AA0_ServerAssistMod`. The QLN pack staging instructions in
`PACK-UPDATE.md` use `AAY_ServerAssistMod`. Either name works as long as the
**same folder name** is used on the server and every client, and the pack
manifest installs to that folder. The art folder (`AAZ_ServerAssistArt`) is
managed separately and is not copied by `deploy-to-pack.ps1`.

!!! note
    `xui.xml` is parsed at client startup. If it is nested one level too deep,
    the log will not contain `[ServerAssist] XUi window initialized` and the
    window group lookup will fail. Keep `Config/XUi_InGame` directly under the
    mod folder.

### Deployment script

From the `ServerAssist` build folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy-to-pack.ps1
```

The script copies only the client files to the configured pack staging folder,
then verifies each copy with SHA-256. It intentionally leaves
`AAZ_ServerAssistArt` untouched. The default destination is documented in
[PACK-UPDATE.md](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/ServerAssist/PACK-UPDATE.md).

---

## Client UI

The window is a left-sidebar board with a content pane. The header is the static
**SERVER ASSIST** brand; the content title comes from the server view.

| Area | Behaviour |
|---|---|
| Sidebar | **Home**, **Shared**, **My Buttons** |
| Content header | Title, subtitle, banner image |
| Card grid | 10 cards per page; Prev/Next actions when more entries exist |
| Action bar | Up to 12 action buttons; optional icon and tint per button |
| Input row | One shared text input with Submit and Cancel |
| Hook chrome | Hides the three tabs and Refresh; shows Back |
| Provider chrome | Keeps the sidebar and tab labels supplied by the provider |
| Footer | `F7 or F6 toggles Server Assist` |

Home cards sort favorites first, then alphabetically by label. Right-clicking a
home card sends `a|favorite|<action>`; this is suppressed while a hook or
provider owns the screen. Selected rows are highlighted gold.

The card icon is drawn from the client's live item or UI atlases. `item:` keys
use `ItemClass.GetIconName()` and prefer `ItemIconAtlas`; `sprite:` and `atlas:`
keys use the loaded UI atlases. If an icon cannot be resolved, the client falls
back to `ui_game_symbol_hammer`.

### Keys and commands

| Input | Where | Effect |
|---|---|---|
| `F7` | In game | Toggle the board; resumes an active provider |
| `F6` | In game | Alternate toggle key |
| `sa` | Client console (F1) | Open the board at Home, always leaving a provider |
| `serverassist` | Client console (F1) | Alias for `sa` |
| `/sa`, `!sa` | Chat | Home |
| `/serverassist`, `!serverassist` | Chat | Home |
| `/sa mine`, `!sa mine` | Chat | My Buttons |
| `/sa shared`, `!sa shared` | Chat | Shared |

`ConsoleCmdServerAssist` is client-only (`IsExecuteOnClient = true`) and has
default permission level `0`. Chat interception runs in the client mod's
`ModEvents.ChatMessage` handler. If the client has not been marked capable, the
message is passed through so the Oxide command can answer in chat.

---

## Hello and capability handshake

There is no version-negotiation field. Capability is proven by traffic.

```text
Client spawn/world join
  ServerAssistClient.Update()
    if !helloSent -> SendHello()
      NetPackageServerAssistRequest.Setup("hello")
      helloSent = true
      PlayerProfilesClient.SendHello()

Server receives request
  ServerAssistServer.HandleRequest()
    MarkCapable(sender)
    payload == "hello" -> return (no response)
```

`ServerAssistServer` keeps an in-memory `HashSet<int>` keyed by
`client.ClientNumber` (falling back to `client.entityId`). Any incoming request
marks the client capable, not only the literal `hello`. The set is not
persisted; a reconnect sends `hello` again from `PlayerSpawnedInWorld` and
`GameUpdate`. `PlayerDisconnected` removes the entry.

`ServerAssistServer.OpenForPlayer` refuses to open a panel when the client is
not capable. This is why a player who has not yet sent a request can see a
"client mod is not installed" response even though the DLL is present.

!!! note
    A 2.0 client still completes the hello and renders the board. It simply
    lacks the 2.1 action-button icon fields and the `@pp|` PlayerProfiles
    client support.

---

## Net packages

Both packages are registered at `InitMod` by `ServerAssistNetPackages.RegisterAll`.
The game fills `NetPackageManager.knownPackageTypes` before mods load, so the
mod injects its types by reflection into that dictionary. Existing names are
skipped, and the log reports the number added.

| Package | Direction | Payload | Receiver |
|---|---|---|---|
| `NetPackageServerAssistRequest` | `ToServer` | One string | `ServerAssistServer.HandleRequest(Sender, payload)` |
| `NetPackageServerAssistResponse` | `ToClient` | One string | `ServerAssistClient.OnResponse(payload)` |

```text
[ServerAssist] loading companion mod from ...
[ServerAssist] registered 2 net package(s)
[ServerAssist] XUi window initialized (cards=10, iconViews=10, textFallback=False)
```

`NetPackageServerAssistResponse.ProcessPackage` ignores the package on a server
process. The client dispatches a response to `PlayerProfilesClient.TryHandle`
first, then to the open window or stores it as a pending payload and opens the
group.

---

## View protocol and server authority

The server returns newline-separated `key=value` lines. The client parser accepts
the original `v1` form and the current `v2` form; it ignores unknown keys.

| Key | Client effect |
|---|---|
| `title`, `subtitle`, `status` | Content header and status line |
| `tab` | Active sidebar tab (`home`, `shared`, `mine`) |
| `tab1label`, `tab2label`, `tab3label` | Rename sidebar tabs |
| `chrome` | `normal` or `hook`; hook hides navigation and shows Back |
| `provider` | Marks a provider-owned view |
| `background`, `banner` | Panel watermark and header image |
| `input` | `label\|visible\|value` |
| `row` | Card: index, action, label, sub, enabled, selected, icon, badge, tint |
| `btn` | Action: index, action, label, enabled, tooltip, optional icon, optional tint |
| `launch` | `type\|target\|close`; only the server can issue this |

Limits are 10 rows, 12 action buttons and one text input. Dynamic fields are
percent-escaped for `%`, `|` and newline. Titles and labels are plain text after
decoding; the client does not render markup.

The client never derives a launch target from local button state. When the
server sends `launch=window|<group>|<close>`, the client:

1. Refuses a launch that targets `serverAssist` itself.
2. Looks up the window group by name.
3. Closes Server Assist first when `close` is not `0`.
4. Opens the group modally, except `players`, which is opened non-modal and made
   Escape-closable.

For `launch=command|<command>|<close>`, the client strips a leading `/`, closes
the board when requested, and runs the command through `SdtdConsole`. The
server-side command still enforces its own permissions.

---

## Player Profiles client support

Client mod 2.0.1 added an optional `@pp|` protocol used by the PlayerProfiles
Oxide plugin. It is carried inside the existing
`NetPackageServerAssistRequest` / `NetPackageServerAssistResponse` pair, so it
registers **no new NetPackage type** and does not change the server package-ID
table.

| Property | Value |
|---|---|
| Prefix | `@pp\|` |
| Panel id | `playerprofiles` |
| Protocol version | `1` |
| Capability hello | `@pp\|playerprofiles\|hello\|1` |
| Hello interval | 60 seconds |
| Switch timeout | 20 seconds |
| Chunk retry/ack | Client requests each chunk by index |
| Integrity | SHA-256 over the assembled payload |
| Payload format | `.ttp` character data (`ttp\0`, version 38+) |

The client checks `@pp|` before normal view parsing, so protocol traffic can
never render as a broken window. Client-to-server messages include the panel id
so Server Assist can route them to the `playerprofiles` GUI hook without an
active window session. The plugin receives `@pp|<body>`.

The switch flow is:

```text
Server -> @pp|prepare|token|fromName|toName
Client -> @pp|playerprofiles|ready|token
Server -> @pp|begin|token|chunks|sha256|bytes|chunkBytes
Client -> @pp|playerprofiles|chunk|token|index
Server -> @pp|chunk|token|index|base64
Client -> @pp|playerprofiles|applied|token|sha256
Server -> @pp|done|token
```

The client refuses to start while dead or attached to a vehicle/drone, freezes
send timers during the switch, closes open windows, applies the profile locally,
and waits for the server's `done` before releasing the freeze. If the server
does not confirm, the client has a 10-second done deadline and a recovery
unlock window. Older clients without the protocol simply fall back to
PlayerProfiles' reconnect path.

!!! warning
    `@pp` traffic is intentionally silent when the PlayerProfiles plugin is not
    loaded or the server plugin does not expose `RouteCompanionProtocol`. This
    prevents an updated client mod from showing an error window on an older
    server.

---

## Permissions

The client assembly has no permission checks of its own. The server plugin
checks every request.

| Permission | Purpose |
|---|---|
| `serverassist.use` | Open the board and browse |
| `serverassist.admin` | Manage shared buttons and run admin commands |
| `serverassist.custom` | Create and edit personal buttons |
| `serverassist.button.<id>` | Per-shared-button node created by `auto` |
| `serverassist.personal.<id>` | Per-personal-button node created by `auto` |

See [Permissions](../../core/permissions.md) for grant examples and the
visibility rules.

---

## Version alignment

| Component | Current value |
|---|---|
| Client `ModInfo.xml` | `2.1.0` |
| Client assembly namespace | `ServerAssistMod` |
| Server plugin `[Info]` | `2.1.0` |
| View protocol | `2` |
| Window group | `serverAssist` |
| Window controller | `XUiC_ServerAssistWindow` |

Keep the server plugin and every client on the same build:

- A **2.0 client** still loads and opens the board, but it does not draw
  action-button icons and `@pp|` features fall back to reconnect paths.
- A **2.1 client with a 2.0 server** may not receive fields it expects; keep the
  server plugin current first.
- A mismatched `windows.xml` can produce `Style key ... not found` or a missing
  window group. Replace the whole client folder, not only the DLL.

There is no version field in the hello. The server treats any request as proof
of capability; version-sensitive features are optional protocol fields that an
older client ignores or a newer client detects by a missing route.

---

## Typical workflows

### First client install

1. Stop the game completely.
2. Remove any older Server Assist folder from `Mods/`.
3. Copy the whole `AA0_ServerAssistMod` folder into `Mods/`.
4. Start the game without EAC and join the server.
5. Confirm the client log contains `[ServerAssist] XUi window initialized`.
6. Press `F7`/`F6` or run `sa` in the client console.

### Deploy through the QLN pack

1. Build `ServerAssistMod.dll`.
2. Run `deploy-to-pack.ps1` to copy the five client files to the staging folder.
3. Have the pack agent zip the folder, upload it, and update the manifest
   `sha256` and `size`.
4. Let the bootstrap install the new folder at the next restart.

See [QLN Pack Bootstrap](qln-pack-bootstrap.md) and
[QLN Pack Server](../programs/qln-pack-server.md).

### Add a companion panel icon

Register the panel with `sprite:sa_*` from
[Server Assist Art](../art/server-assist-art.md). No client DLL or XUi XML is
needed for a companion; the existing board renders the view.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `ServerAssist window group was not found` | `Config/XUi_InGame/xui.xml` missing or nested | Put `Config/XUi_InGame` directly under the mod folder |
| `The ServerAssist client mod is not installed` | Client has no DLL/config, or hello not sent yet | Install the folder; rejoin; check net package log |
| `The ServerAssist Oxide plugin is not loaded` | `ServerAssist.cs` missing or failed to compile | Check `oxide/plugins` and the server log |
| Board opens but is empty | Server response not received | Check both logs for net package or plugin errors |
| Action buttons have no icons | Client is 2.0 or server omits icon fields | Update the client to the same 2.1 build |
| `Style key ... not found` | Old `windows.xml` with bracketed captions | Install the current `windows.xml` |
| Old layout after replacing files | XUi cache / game not fully restarted | Quit the game completely and relaunch |
| Duplicate window or package errors | Two client builds installed | Remove both, install one |
| EAC enabled | Mod skipped | Disable EAC on server and client |

More: [Troubleshooting](../../reference/troubleshooting/index.md).
