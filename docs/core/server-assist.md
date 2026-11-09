# Server Assist — product guide

Server Assist is a permission-aware launcher board for 7 Days to Die. It owns one XUi window and one client assembly. Companion plugins register panels, hooks or providers; Server Assist renders them, routes their actions and enforces the board's own permissions.

This page describes the product as a whole: lifecycle, configuration, data model, commands, limits and behaviour. For the API surface, read [API](../integration/api/index.md).

---

## Design principles

| Principle | Consequence |
|---|---|
| **One window** | Every feature is rendered by `windowServerAssist`; companion plugins ship no client XUi XML |
| **Server owns authority** | The client draws and forwards actions; the server decides visibility, launch and hook access |
| **Idempotent registration** | Registering the same id again replaces the definition, so reloads are safe |
| **Dynamic permissions** | Per-button nodes are created on demand and can be granted in PermissionGui |
| **Lazy failure** | A missing companion produces no row; a missing target produces a local error, not a crash |
| **Stable identifiers** | `ServerAssist`, `serverassist`, `ServerAssistMod`, `serverAssist` are public API |
| **Server-only companions** | Panels such as DynamicBackpack ship only server code; Server Assist renders their cards |

---

## Components

| Component | Responsibility |
|---|---|
| `ServerAssist.cs` | Button data, permissions, sessions, hooks, providers, view protocol, commands |
| `ServerAssistMod.dll` | XUi window, view parser, net packages and client bridge |
| `Config/XUi_InGame/xui.xml` | Declares the `serverAssist` window group |
| `oxide/plugins/DynamicBackpack.cs` | Optional server-only backpack panel; no client files |
| `Config/XUi_InGame/windows.xml` | Declares the window layout and controllers |
| `ServerAssistBridge` | Server-side reflection bridge from the client assembly to the Oxide plugin |
| `ServerAssistServer` | Tracks capable clients and routes client requests |
| `ServerAssistView` | Parses the line protocol on the client |
| `ServerAssistClient` | Keys, window open/close, launches, hello and requests |

---

## Window identity

```text
Plugin name:    ServerAssist
Plugin class:   Oxide.Plugins.ServerAssist
Client mod:     ServerAssistMod
Window group:   serverAssist
Window:         windowServerAssist
Group ctrl:     ServerAssistWindowGroup, ServerAssistMod
Window ctrl:    ServerAssistWindow, ServerAssistMod
Protocol:       2
```

The client resolves the window group through several fallbacks:

1. The player-owned XUi (`player.PlayerUI.xui`).
2. A direct scan of `XUi.WindowGroups` for id `serverAssist`.
3. A scan for the `XUiC_ServerAssistWindowGroup` controller type.
4. `FindWindowGroupByName("serverAssist")` and the lowercase variant.
5. `GetWindowGroupById("serverAssist")`.
6. The window manager's name map.
7. The internal `windowsAndGroupsByControllerType` index.

This matters when another mod or a game update changes XUi lookup tables. See [Troubleshooting](../reference/troubleshooting/index.md#xui-group-diagnostics).

---

## Lifecycle

```text
Oxide loads ServerAssist.cs
  Init()
    register serverassist.use / .admin / .custom
    LoadConfig()
    LoadData()
    SeedExampleButtons()
    RegisterButtonPermissions()
    add /sa and /serverassist commands
    timer 1s  EnsureDefaultPermissions()
    timer 2s  SeedExampleButtons()
    timer 3s  NotifyServerAssistReady()  -> OnServerAssistReady

OnServerInitialized(initial)
  EnsureDefaultPermissions()
  SeedExampleButtons()
  RegisterButtonPermissions()
  NotifyServerAssistReady()

OnServerSave()
  SaveData()

OnPluginUnloaded(plugin)
  remove hooks/providers owned by that plugin

OnUserDisconnected(player)
  drop that player's session

Unload()
  SaveData()
  clear sessions, hooks and providers
```

### `OnServerAssistReady`

A companion plugin defines:

```csharp
private void OnServerAssistReady()
{
    RegisterMyPanel();
}
```

Server Assist calls the Oxide hook after its own load and after Oxide reloads Server Assist. Registering the same id again replaces the previous definition, so the companion does not need to guard against duplicates.

!!! note
    `/sa reload` reloads config and data and re-registers button permissions. It does not re-broadcast `OnServerAssistReady`; hooks that are already registered remain in memory. Use `oxide.reload ServerAssist` when you want the full ready cycle.

---

## Commands

### Player and admin commands

| Command | Permission | Behaviour |
|---|---|---|
| `/sa` | `serverassist.use` | Open the board |
| `/serverassist` | `serverassist.use` | Alias |
| `/sa open` | `serverassist.use` | Open the board |
| `/sa gui` | `serverassist.use` | Open the board |
| `/sa list` | `serverassist.admin` | List shared buttons with targets, permissions and state |
| `/sa add <label> <window\|command> <target> [permission]` | `serverassist.admin` | Create a shared button without the GUI |
| `/sa remove <id>` | `serverassist.admin` | Remove a shared button |
| `/sa reload` | `serverassist.admin` | Reload config/data and re-register permissions |
| `/sa help` | `serverassist.use` | Show usage |

`/sa add` accepts one label argument, so the label cannot contain spaces. Use the GUI for normal editing.

### Client console

| Command | Effect |
|---|---|
| `sa` | Open the board at Home |
| `serverassist` | Open the board at Home |

The client console command always leaves an active provider and returns to the board, unlike `F7`, which resumes an active provider.

### Chat shortcuts

The companion mod intercepts these before Oxide:

| Message | Effect |
|---|---|
| `/sa`, `!sa`, `/serverassist`, `!serverassist` | Home |
| `/sa mine`, `!sa mine` | My Buttons |
| `/sa shared`, `!sa shared` | Shared |

If the client is not capable, the chat handler passes the message through so the Oxide command can answer in chat.

---

## Configuration

File: `oxide/config/ServerAssist.json`

| Key | Default | Range / notes |
|---|---|---|
| `ChatPrefix` | `[ServerAssist]` | Prefix for chat replies; blank means no prefix |
| `GrantUseToDefaultGroup` | `true` | Grant `serverassist.use` to the default players group |
| `GrantCustomToDefaultGroup` | `true` | Grant `serverassist.custom` to the default players group |
| `AdminSeesAllButtons` | `true` | Admins see disabled buttons and buttons they cannot launch |
| `AllowPlayerCustomButtons` | `true` | Allow the My Buttons tab for players with `.custom` |
| `AllowPlayerCommandButtons` | `false` | Allow personal command buttons; shared command buttons are admin-created |
| `MaxSharedButtons` | `60` | Clamped to 1–200 |
| `MaxPersonalButtons` | `20` | Clamped to 0–100 |

Admins always pass `CanManage` and `CanLaunch` checks. `AllowPlayerCommandButtons` does not apply to admins.

---

## Data model

File: `oxide/data/ServerAssist/data.json`

```json
{
  "Version": 1,
  "SeededExamples": true,
  "Shared": {
    "admin-shop": {
      "Id": "admin-shop",
      "Label": "Admin Shop",
      "Description": "Buy items with dukes.",
      "TargetType": "window",
      "Target": "adminshop",
      "Permission": "adminshop.use",
      "Icon": "item:casinoCoin",
      "IconTint": "#FFD166",
      "Enabled": true,
      "Order": 20
    }
  },
  "Personal": {
    "76561198000000000": {
      "my-shop": {
        "Id": "my-shop",
        "Label": "My Shop",
        "Description": "Open my usual shop.",
        "TargetType": "window",
        "Target": "character",
        "Permission": "serverassist.personal.my-shop",
        "Icon": "item:casinoCoin",
        "IconTint": "#FFCC66",
        "Enabled": true,
        "Order": 10
      }
    }
  }
}
```

### Button fields

| Field | Type | Rules |
|---|---|---|
| `Id` | string | Stable, lowercased, generated from the label, max 40 base characters plus suffix |
| `Label` | string | Required, max 48 characters |
| `Description` | string | Max 160 characters |
| `TargetType` | string | `window` or `command` |
| `Target` | string | Max 160 characters |
| `Permission` | string | Blank = everyone; `auto` resolved at create time; otherwise a node |
| `Icon` | string | `item:`, `sprite:`, `atlas:` or blank |
| `IconTint` | string | `#RRGGBB` or `#RRGGBBAA` |
| `Enabled` | bool | Disabled buttons are hidden from normal players |
| `Order` | int | Ascending; reorder writes multiples of 10 |

!!! warning
    Do not edit `data.json` while the server is running. The plugin keeps the data in memory and writes it on save, toggle, move, create, edit and delete. Use the GUI or `/sa` commands.

The loader normalizes every button: it fills missing ids, trims and truncates strings, coerces unknown target types to `window`, and removes a legacy seeded permission-manager button whose window target no longer exists.

---

## Sessions

Each player gets an in-memory session:

| Field | Meaning |
|---|---|
| `Tab` | `home`, `shared`, `mine`, `hook` |
| `Scope` | `shared` or `personal` |
| `SelectedId` | Button being edited |
| `HookId` | Active GUI hook |
| `ActiveProvider` | Active provider |
| `Page` | Current page |
| `InputAction` | Pending text prompt action |
| `InputLabel`, `InputValue` | Pending text prompt state |
| `Status` | Status line |
| `IconSearchActive`, `IconSearchQuery` | Item picker state |
| `Draft` | New-button draft |

Sessions are removed on disconnect and cleared on unload. They are not persisted, so a reconnect starts at Home.

---

## Limits

| Limit | Value | Behaviour |
|---|---|---|
| Cards per page | 10 | Paging buttons appear above 10 |
| Action buttons | 12 | Two rows of six; optional icon and tint per button |
| Text input | 1 | Shared by button editing, hook providers and item search |
| Shared buttons | 60 default | Config clamp 1–200 |
| Personal buttons | 20 default | Config clamp 0–100 |
| Hook id | 48 chars | Lowercased |
| Hook label | 48 chars | Cleaned |
| Hook description | 160 chars | Cleaned |
| Hook permission | 96 chars | Cleaned |
| Provider id | 48 chars | Lowercased |
| Provider title | 64 chars | Cleaned |
| Provider tab label | 32 chars | Cleaned |
| Button target | 160 chars | Window names restricted to letters, digits, `_`, `-`, `.` |
| Command target | 160 chars | Must not contain `|`; leading `/` stripped |

---

## Visibility and launch checks

```text
CanUse(player)        = admin OR serverassist.use
CanManage(player,s)   = admin OR (s == personal AND AllowPlayerCustomButtons AND serverassist.custom)
CanLaunch(player,b)   = b.Enabled AND (admin OR b.Permission blank OR player has b.Permission)
IsVisibleOnHome       = admin ? (AdminSeesAllButtons OR CanLaunch) : CanLaunch
```

A non-admin sees only enabled buttons they can launch. A button with a blank permission is visible to everyone who can open the board.

A command button created by a player is refused at launch unless an admin created it or `AllowPlayerCommandButtons` is true. This check applies again when the command button is created or its type is edited.

---

## Default icons

When a button has no explicit icon, the server chooses a sensible default:

| Target type / target | Default sprite |
|---|---|
| `command` | `ui_game_symbol_hammer` |
| window `map` | `ui_game_symbol_map` |
| anything else | `ui_game_symbol_hammer` |

The client falls back to `ui_game_symbol_hammer` when an icon cannot be resolved.

---

## Compatibility surface

The plugin keeps the earlier integration methods available:

| Method | Status |
|---|---|
| `RegisterGuiHook` | Current, compatibility name retained |
| `UnregisterGuiHook` | Current |
| `OpenHookForPlayer` (2 args) | Current, `[HookMethod]` |
| `OpenHookForPlayer` (3 args) | Public method for reflection-era companions |
| `OpenHookForPlayerWithAction` | Current `[HookMethod]` |
| `RegisterPanel` | Preferred panel API |
| `OpenPanel` | Preferred panel open API |
| `UnregisterPanel` | Preferred panel unregister API |
| `SetPanelIcon` | Set or replace a panel card icon |
| `GetPanelIcon` | Read a panel card icon |
| `GetPluginIcon` | Read a plugin's panel/provider icon |
| `@pp\|<hook-id>\|<body>` | Optional companion protocol routed to a hook |
| `RegisterGuiProvider` / `OpenGuiProvider` / `UnregisterGuiProvider` | Provider API |
| `SetProviderIcon` | Set or replace a provider card icon |
| `GetProviderHomeSupport` | Reports whether providers appear on Home |

The old six-field row and the old hook names remain valid. New plugins should use `RegisterPanel` for home rows and the provider API only when they need the tabs. On current builds registered providers also appear on Home with a `PROVIDER` badge; `GetProviderHomeSupport` lets a companion fall back to a normal panel on older builds.

Server Assist 2.1 adds optional `icon` and `tint` fields to `btn=` lines. The 2.1 client draws them; a 2.0 client parses only the five-field button and shows the trailing values as tooltip text. The companion protocol uses the existing request/response path under an `@pp|` prefix and registers no new NetPackage type.

---

## Operational notes

- **Main thread only.** Registration, routing and handler calls happen on the game thread. A handler that blocks stalls the GUI for that player.
- **No client plugin assembly.** Companion plugins are resolved by name on the server with `plugins.Find`. The client never loads them.
- **No automatic unregister on unload for companions.** Server Assist removes hooks and providers whose owning plugin unloaded, but a well-behaved companion should also unregister in its own `Unload`.
- **No HTML in the view.** Titles, labels and status are plain text after percent-decoding. The client renders text, not markup.
- **No arbitrary client code.** The server returns a whitelisted set of keys and a `launch=` instruction. Unknown keys are ignored by the parser.
- **Companion protocols reuse the existing packets.** The `@pp|` path does not add a NetPackage type, so it cannot change the server package-ID table or disconnect clients with an unknown package type. Older clients simply use the companion's fallback path.

---

## Related pages

- [Interface](interface.md)
- [Buttons](buttons.md)
- [Permissions](permissions.md)
- [API](../integration/api/index.md)
- [Plugins](../plugins/index.md)
- [Dynamic Backpack](../integration/dynamic-backpack.md)
- [Migration](../reference/migration/index.md)
