# Server Assist 2.1

!!! important
    **Server Assist** is the current name of the universal, permission-aware GUI launcher for 7 Days to Die. The runtime identity is `ServerAssist` / `serverassist`. This wiki documents version **2.1.0**, view protocol **2**, and the companion client mod **ServerAssistMod**.

Server Assist gives a server one polished in-game board for every admin tool, player menu and server feature. Instead of shipping a separate XUi window for every plugin, companion plugins register a panel or a GUI hook and Server Assist renders it inside a single window that every player already has installed.

The result is one installation, one keybind, one visual language, and a per-button permission model that stays server-authoritative.

---

## Why servers use it

| Problem | Server Assist approach |
|---|---|
| Every plugin ships its own XUi window, client DLL and keybind | One client mod owns the window; plugins are server-only |
| Players see admin tools they cannot use | Buttons and hooks are hidden or disabled by permission |
| Staff need different tools on different servers | Shared buttons, personal buttons and per-button nodes |
| A custom UI takes days of XUi work | A companion plugin returns a simple line protocol |
| A plugin reload leaves dead menu rows | Idempotent registration plus `OnServerAssistReady` |
| Installations drift between server and clients | One `ServerAssist` mod folder on the server and every client |

---

## At a glance

| Item | Value |
|---|---|
| Product name | **Server Assist** |
| Version | `2.1.0` |
| Oxide plugin | `ServerAssist` (`Oxide.Plugins.ServerAssist`) |
| Client mod | `ServerAssistMod` |
| Window group | `serverAssist` |
| Window | `windowServerAssist` |
| View protocol | `2` (accepts `v1` rows and five-field buttons) |
| Companion protocol | `1` (`@pp\|`, optional) |
| Cards per page | `10` |
| Action buttons | `12`, optional icon and tint |
| Text inputs | `1` |
| Open keys | `F7`, `F6` |
| Chat commands | `/sa`, `/serverassist` |
| Client console | `sa`, `serverassist` |
| Config | `oxide/config/ServerAssist.json` |
| Data | `oxide/data/ServerAssist/data.json` |

---

## What 2.1 adds

| Addition | What it does |
|---|---|
| Action-button icons | Optional `icon` and `tint` fields on `btn=` lines; the client draws a 24×24 icon and shifts the label. |
| Icon read-back | `GetPanelIcon` and `GetPluginIcon` return the live card icon for another plugin or UI. |
| Companion protocol | `@pp\|` payloads route to a named hook without a window session; PlayerProfiles uses it for in-place character switching. |

---

## Feature map

| Area | What you get | Read |
|---|---|---|
| Window | Left sidebar, content pane, 10 art cards, 12-action toolbar, status line, one input | [Interface](core/interface.md) |
| Navigation | Home, Shared, My Buttons, hook Back mode, provider tab labels | [Interface](core/interface.md) |
| Buttons | Shared and personal scopes, window and command targets, per-button permissions | [Buttons](core/buttons.md) |
| Icons | Live item icons, vanilla sprites, atlas sprites, action-button icons, tint, badges, fallback | [Icons](core/icons.md) |
| Icon picker | Search internal and localized item names, preview, click to assign | [Item Search](core/item-search.md) |
| Permissions | `serverassist.use`, `.admin`, `.custom`, `.button.*`, `.personal.*` | [Permissions](core/permissions.md) |
| API | Panels, hooks, providers, icon read-back, `@pp\|` companion protocols, view protocol | [API](integration/api/index.md) |
| Plugins | Full reference pages for every plugin, client mod, program and art pack | [Plugins](plugins/index.md) |
| Companions | Integration recipes and the companion registration matrix | [Companion Integration](integration/companions/index.md) |
| Backpack | Server-only panel with nine pages of nine item cards | [Dynamic Backpack](integration/dynamic-backpack.md) |
| Upgrades | Command, permission, path and API mappings | [Migration](reference/migration/index.md) |
| Support | Diagnostics, logs, common failures | [Troubleshooting](reference/troubleshooting/index.md) |
| Questions | Short answers to the things server owners ask first | [FAQ](reference/faq/index.md) |

---

## The 60-second version

1. Stop the server and the game.
2. Copy the `ServerAssist` mod folder into the server's `Mods` folder and into every client's `Mods` folder. Use the same folder name everywhere. The release zip uses `AA0_ServerAssistMod`.
3. Copy `oxide/plugins/ServerAssist.cs` into the server's `oxide/plugins` folder.
4. Start the server, join with a client, and press `F7` or `F6`, or type `/sa`.
5. Open **Shared** as an admin, create a button, choose a target, and grant its permission to the right group.

!!! warning
    EAC must be off. The server cannot push an XUi window to a client. A player without the Server Assist client mod cannot see the board, even if the Oxide plugin is running.

Full instructions: [Install](start/install.md).

---

## Architecture

```text
┌──────────────────────────────┐
│ Client                        │
│                               │
│  ServerAssistMod.dll          │
│   ├─ windowServerAssist       │
│   ├─ view parser              │
│   ├─ net packages             │
│   └─ local console sa/serverassist
│                               │
│  F7 / F6  ───────────────┐    │
└───────────────────────────┼───┘
                            │ request / response
                            ▼
┌──────────────────────────────┐
│ Server                        │
│                               │
│  ServerAssist.cs              │
│   ├─ button data              │
│   ├─ permissions              │
│   ├─ hook registry            │
│   ├─ provider registry        │
│   └─ view protocol builder    │
│                               │
│  Companion plugins            │
│   ├─ PermissionGui            │
│   ├─ AdminShop                │
│   ├─ AuctionHouse             │
│   ├─ VoteSkipNight            │
│   ├─ AdminKits                │
│   ├─ AdminSuite               │
│   ├─ DailyRewards             │
│   ├─ DynamicBackpack          │
│   ├─ PluginHotReload          │
│   ├─ TwitchIntegration        │
│   ├─ BuggeeCore               │
│   └─ PlayerProfiles           │
└──────────────────────────────┘
```

The client is deliberately dumb. It draws what the server returns and sends named actions back. It never decides whether a player may see or launch a button. That keeps every permission check on the server and makes the client assembly safe to share across servers.

---

## Two window models

Server Assist supports two ways for another plugin to take over the content pane, plus an optional companion protocol for client-side features.

### Panel / GUI hook

A companion plugin registers a home-row entry. The player clicks the row and Server Assist enters **hook mode**: the Home / Shared / My Buttons tabs disappear, a **Back** button appears, and every action is forwarded to the companion until the player leaves.

Use this for a self-contained feature that should look like one more tool on the board: PermissionGui, AdminShop, VoteSkipNight, AdminKits, AdminSuite, DailyRewards, DynamicBackpack, PluginHotReload, MediaIntegration, TwitchIntegration, BuggeeNet, PlayerProfiles and SentinelAC.

```csharp
Plugin serverAssist = plugins.Find("ServerAssist");
serverAssist.Call(
    "RegisterPanel",
    "adminshop",
    "Admin Shop",
    "Buy items with dukes.",
    "adminshop.use",
    "AdminShop",
    "HandleGuiRequest",
    "ui_game_symbol_hammer",
    10);
```

### Provider / tabs

A provider plugin keeps the three tab buttons and can rename them. It owns its own screen, permission checks and entry command. Current Server Assist builds also list each provider on Home as `[GUI] <title>` with a `PROVIDER` badge; `GetProviderHomeSupport()` tells a companion whether that card exists. There is no injected Back button; `/sa` returns to the normal board, while `F7` resumes the active provider.

Use this when the feature is a destination rather than a menu row. AuctionHouse is the reference provider, and it falls back to a normal home panel when it detects an older Server Assist build without provider home support.

```csharp
serverAssist.Call(
    "RegisterGuiProvider",
    "auctionhouse",
    "AuctionHouse",
    "HandleAuctionGuiRequest",
    "Auction House",
    "Browse",
    "My Listings",
    "Sell");
```

Both models use the same handler signature and the same view protocol:

```csharp
public string HandleGuiRequest(string playerId, string payload)
```

### Optional companion protocol

Server Assist 2.1 also routes `@pp|` payloads to a registered hook without opening a window session. This is for client-side work that cannot be expressed as a normal view; PlayerProfiles uses it for an in-place character switch. The protocol travels inside the existing request/response packages and registers no NetPackage type. See [Optional companion protocols](integration/api/companion-protocols.md#optional-companion-protocols-pp).

Details: [API](integration/api/index.md) and [Companion Integration](integration/companions/index.md).

---

## Security model

Server Assist is a launcher, not a permission bypass.

- The player id is derived from the sending client's server-side connection, never from the payload.
- `serverassist.use` is checked when a normal board request is handled.
- Every button launch re-checks `Enabled`, the button permission and the personal-command restriction.
- Every hook open and every hook action re-checks the hook permission.
- A provider home card is listed for everyone who can open the board; the provider must check its own permission when the card is clicked.
- Provider requests are routed before `serverassist.use` because the provider owns its own checks. The provider must enforce them.
- A client-launched console command runs through the local console. Server-authoritative commands still enforce their own permissions; a command button does not grant admin rights.
- The client cannot synthesize a `launch=` line. Launch instructions come from a server-built view.

!!! caution
    Treat a hook or provider permission as **visibility**, not as the only security boundary. Re-check the acting player's permission inside every action handler, especially for economy, inventory, moderation and world-changing operations.

---

## Runtime identity

Use these exact strings in configs, scripts and companion plugins.

| Purpose | Value |
|---|---|
| Oxide plugin name | `ServerAssist` |
| Oxide plugin class | `Oxide.Plugins.ServerAssist` |
| Client assembly | `ServerAssistMod.dll` |
| Client namespace | `ServerAssistMod` |
| Window group | `serverAssist` |
| Window controller | `ServerAssistWindow, ServerAssistMod` |
| Window group controller | `ServerAssistWindowGroup, ServerAssistMod` |
| Chat command | `/sa`, `/serverassist` |
| Client console | `sa`, `serverassist` |
| Backpack panel | `backpack` |
| Backpack handler | `HandleBackpackGui` |

!!! note
    `serverassist`, `ServerAssist`, `ServerAssistMod` and `serverAssist` are all intentional. The casing is part of the public identity. Do not normalize one into another.

---

## Compatibility

| Component | Supported |
|---|---|
| 7 Days to Die | V3.4.x managed assemblies |
| Oxide / uMod | Current build |
| EAC | Must be off |
| Existing `v1` view rows | Accepted; icon fields are optional |
| Action-button icons | Require a 2.1 client; a 2.0 client treats trailing fields as tooltip text |
| `@pp\|` companion protocols | Require the 2.1 plugin and client; older combinations fall back to the feature's own safe path |
| Older companion plugins that call legacy hook names | Still accepted through compatibility methods |
| Old client DLL next to the new one | **Not supported** — remove it |
| Server without the client mod | Plugin loads; GUI cannot open |

The server plugin and the client mod are versioned together. A client with an older `ServerAssistMod.dll` can fail to send its hello or miss new protocol fields. Update the server and every client at the same time.

---

## Documentation

This documentation is built with MkDocs Material and published through Read the Docs. It supports search, nested navigation, code copying, light/dark mode and per-page tables of contents. The source lives in this repository under `docs/`, and Read the Docs rebuilds the site whenever the repository changes.

---

## Quick answers

**Do players need the client mod?**
Yes. XUi windows cannot be pushed from the server. Every player who should see the board needs the same `ServerAssist` folder.

**Does a companion plugin need a client DLL?**
No. PermissionGui, AdminShop, AuctionHouse, VoteSkipNight, AdminKits, AdminSuite, DailyRewards, DynamicBackpack, PluginHotReload, MediaIntegration, TwitchIntegration, BuggeeNet, PlayerProfiles and SentinelAC are server-only. They return the line protocol through the Server Assist client. PlayerProfiles can additionally use the optional `@pp|` companion protocol for in-place character switching.

**Can players make their own buttons?**
Yes, with `serverassist.custom` and `AllowPlayerCustomButtons`. Player-made command buttons are disabled unless `AllowPlayerCommandButtons` is enabled.

**Where do buttons live?**
`oxide/data/ServerAssist/data.json`. Do not edit it while the server is running.

**How do I upgrade an existing install?**
Follow [Migration](reference/migration/index.md). The migration page is the only page that intentionally contains the previous product name.

**Where do I start when the window will not open?**
Read [Troubleshooting](reference/troubleshooting/index.md), then check the client log for `[ServerAssist] window group was not found` and the server log for `[ServerAssist] registered GUI hook`.
