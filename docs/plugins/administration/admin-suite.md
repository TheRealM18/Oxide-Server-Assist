# AdminSuite

> **Version:** 1.0.4 · **Type:** Server-side Oxide plugin exposed as a Server Assist GUI panel (no client files) · **Source:** [AdminSuite.cs](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/AdminSuite/oxide/plugins/AdminSuite.cs)

AdminSuite turns Server Assist into a complete staff toolkit for 7 Days to Die: players, moderation, teleports, inventory management, world control, audit logs, snapshots, rollback, server information and more, all inside the same Server Assist window. It follows the "one menu, many tools" pattern.

AdminSuite ships no client DLL and no XUi XML. It registers a GUI hook with Server Assist, so the existing Server Assist client mod renders it. The server enforces every permission and every action; the client is only a view.

!!! warning
    Staff need **both** `serverassist.use` (to open Server Assist) and `adminsuite.use` (to open AdminSuite). Server Assist normally grants `serverassist.use` to the default player group. `adminsuite.use` is not granted to players by default; grant it to moderators and admins.

---

## Requirements

| Requirement | Detail |
|---|---|
| Server | `Mods/AA0_ServerAssistMod` folder with `ServerAssistMod.dll`, `ModInfo.xml` and `Config` |
| Server Assist plugin | `oxide/plugins/ServerAssist.cs` version 2.1.0 or newer |
| AdminSuite plugin | `oxide/plugins/AdminSuite.cs` |
| Every client | The `Mods/AA0_ServerAssistMod` folder |
| EAC | Off |
| Client files for AdminSuite | None |

AdminSuite is server-only. Clients do not install an AdminSuite mod. The server and clients must use the same Server Assist files.

---

## Installation

1. Stop the server and the game.
2. Make sure Server Assist is installed on the server and on every client.
3. Copy `oxide/plugins/AdminSuite.cs` into the server's `oxide/plugins` folder. Oxide compiles it on startup.
4. Start the server.
5. Join with a client, press F7/F6 or type `/as`.
6. If you are not an Oxide administrator, grant the permissions below.
7. Open the `[GUI] Admin Suite` row or use `/as`.

!!! warning
    Do not put the `.cs` file in the game's `Mods` folder. The mod loader does not compile `.cs` files; Oxide must compile the plugin from `oxide/plugins`.

On first load the plugin creates:

```text
oxide/config/AdminSuite.json
oxide/data/AdminSuite/data.json
oxide/data/AdminSuite/snapshots.json
oxide/logs/AdminSuite-YYYY-MM-DD.log
```

The plugin registers `adminsuite.use` and `adminsuite.admin` with Oxide. Oxide's administrators group receives `adminsuite.admin` and every granular permission automatically. The players group receives `adminsuite.use` only when `GrantUseToDefaultGroup` is true, which defaults to false.

A successful registration logs:

```text
[AdminSuite] Registered ServerAssist GUI hook 'adminsuite'.
```

---

## First-time permissions

Grant the main permission and the common tools to a staff group:

```text
oxide.grant group moderators adminsuite.use
oxide.grant group moderators serverassist.use
oxide.grant group moderators adminsuite.players.view
oxide.grant group moderators adminsuite.players.teleport
oxide.grant group moderators adminsuite.players.bring
oxide.grant group moderators adminsuite.players.kick
oxide.grant group moderators adminsuite.players.ban
oxide.grant group moderators adminsuite.players.mute
oxide.grant group moderators adminsuite.logs
```

Oxide administrators already receive `adminsuite.admin`, which bypasses every granular check. If PermissionGui is installed, all AdminSuite nodes appear in its permission browser and can be granted there.

---

## Opening the menu

Run `/as`, `/adminsuite` or `/as open`, press F7/F6, or click the `[GUI] Admin Suite` home row. All of these require `adminsuite.use` or Oxide administrator access. Only players with that access see the hook row.

The window's main categories are Players, Moderation, Teleport, Items, World, Logs, Snapshots, Server, Warps and Help. The inventory inspector is a live server-side view, not a remote drag-and-drop window. Every destructive action snapshots the player first when `SnapshotBeforeDestructive` is true.

---

## Commands

AdminSuite registers the command as both `/as` and `/adminsuite`. Every command also requires `adminsuite.use` or administrator access at the base level.

### Opening and player information

| Command | Permission | Behaviour |
|---|---|---|
| `/as`, `/as open`, `/as help` | `adminsuite.use` | Open the AdminSuite window or show the command list |
| `/as list`, `/as who <player>`, `/as nearby [radius]` | `adminsuite.players.view` | List online players, show detailed player information, or list players near you |
| `/as find <item>` | `adminsuite.use` | Search the live item catalog, for example `/as find ak47` |

### Player actions

| Command | Permission | Behaviour |
|---|---|---|
| `/as tp <player\|x y z\|x z>` | `adminsuite.players.teleport` | Teleport yourself to a player or coordinates |
| `/as bring <player>` | `adminsuite.players.bring` | Teleport a player to you |
| `/as ontop <player>`, `/as spawnontop <player>` | `adminsuite.players.spawnontop` | Spawn on top of a player |
| `/as spectate <player>`, `/as look <player>` | `adminsuite.players.spectate` | Teleport near a player and toggle spectator mode |
| `/as inv <player>`, `/as inventory <player>`, `/as openinventory <player>` | `adminsuite.players.inventory` | Open the inventory inspector for that player |
| `/as jail <player>` | `adminsuite.players.freeze` and `EnableJail = true` | Teleport the player to the jail and freeze them |
| `/as god <player>` | `adminsuite.players.god` | Toggle god mode |
| `/as fly [player]` | `adminsuite.players.fly` | Toggle fly in single-player/listen-server; on a dedicated server it opens the debug menu |
| `/as heal <player>`, `/as feed <player>`, `/as kill <player>` | `adminsuite.players.heal` / `adminsuite.players.feed` / `adminsuite.players.kill` | Heal, feed or kill a player |
| `/as healall`, `/as feedall` | `adminsuite.players.heal` / `adminsuite.players.feed` | Heal or feed every online player |
| `/as buff <player> <buff>`, `/as debuff <player> <buff>` | `adminsuite.players.buffs` | Apply or remove a buff |
| `/as give <player> <item> [amount] [quality]` | `adminsuite.players.give` | Give an item; internal and display names are accepted |
| `/as clear <player> confirm`, `/as take <player> confirm`, `/as repair <player>` | `adminsuite.players.clearinventory` / `adminsuite.players.takeinventory` / `adminsuite.players.inventory` | Clear, take or repair every item; clear and take snapshot first |
| `/as warn <player> <message>`, `/as note <player> [message]` | `adminsuite.players.warn` / `adminsuite.players.notes` | Store and send a warning, or set/clear a private staff note |
| `/as snapshot <player> [reason]`, `/as snapshots [player]` | `adminsuite.players.snapshot`; snapshots also accepts `adminsuite.snapshots` or `adminsuite.players.rollback` | Capture a snapshot or list snapshots |
| `/as rollback <player> [snapshotId] confirm` | `adminsuite.players.rollback` | Restore a snapshot; without an id the latest is used |

### Moderation

| Command | Permission | Behaviour |
|---|---|---|
| `/as kick <player> [reason]` | `adminsuite.players.kick` | Kick a player |
| `/as kickall <reason> confirm` | `adminsuite.moderation.kickall` | Kick every connected player |
| `/as ban <player> <duration> [reason] confirm` | `adminsuite.players.ban` | Ban a player |
| `/as unban <player name or id>` | `adminsuite.players.ban` | Remove a ban entry |
| `/as banlist` | `adminsuite.moderation.banlist` or `adminsuite.players.ban` | List ban entries |
| `/as adminlist`, `/as admin add <player> [level]`, `/as admin remove <player>` | `adminsuite.moderation.adminlist` | List, add or remove server admin entries |
| `/as whitelist`, `/as whitelist add <player>`, `/as whitelist remove <player>` | `adminsuite.moderation.whitelist` | List, add or remove whitelist entries |
| `/as clearmutes`, `/as clearwarnings` | `adminsuite.players.mute` / `adminsuite.players.warn` | Clear every active mute or stored warning |
| `/as mute <player> [duration] [reason]`, `/as unmute <player>` | `adminsuite.players.mute` | Mute chat or remove a mute |
| `/as timeout <player> [duration] [reason]` | `adminsuite.players.mute` and `adminsuite.players.freeze` | Mute and freeze for a finite duration |
| `/as freeze <player> [duration] [reason]`, `/as unfreeze <player>` | `adminsuite.players.freeze` | Freeze at the current position, or release a frozen player |

Ban durations accept `30m`, `1h`, `7d`, `2w`, `1mo`, `1y` or `permanent`. Timed command durations use the same compact form: `30m`, `1h`, `6h`, `1d`, `7d`, `2w`, `1mo`, `1y`, `permanent`.

### World, server and maintenance

| Command | Permission | Behaviour |
|---|---|---|
| `/as time <day\|night\|hour\|day hour minute>` | `adminsuite.world` | Set world time, for example `/as time 1 12 0` |
| `/as weather <clear\|rain\|storm> [hours]` | `adminsuite.world` | Control weather; `/as weather storm 1` starts a one-hour storm in your biome |
| `/as airdrop`, `/as horde`, `/as scouts` | `adminsuite.world` | Spawn an airdrop, wandering horde or scouts |
| `/as spawn <entity> [count]` | `adminsuite.world` | Spawn an entity near you, for example `/as spawn zombieBoe 3` |
| `/as save`, `/as say <message>` | `adminsuite.server` (save also accepts `adminsuite.world`) | Force a world save or broadcast a message |
| `/as shutdown confirm` | Administrator only | Shut down after a five-second warning |
| `/as warps`, `/as warp <name>`, `/as setwarp <name>`, `/as delwarp <name>` | `adminsuite.warps`; warps and warp also accept `adminsuite.players.teleport` | List, use, create or delete shared staff warps |
| `/as logs [count] [filter]` | `adminsuite.logs` | Show recent audit logs; filters are `all`, `chat`, `join`, `death`, `admin` and `command` |
| `/as creative`, `/as cm`, `/as creativemenu` | `adminsuite.players.give` | Toggle the client's vanilla creative menu |
| `/as debug`, `/as debugmenu` | `adminsuite.world` or administrator | Toggle the client's vanilla debug menu |
| `/as spectator` | `adminsuite.players.spectate` | Toggle spectator mode on your own client |
| `/as playerlist` | `adminsuite.players.view` | Open the vanilla player list |
| `/as tppoi` | `adminsuite.players.teleport` | Open the vanilla POI teleporter |
| `/as killall confirm` | `adminsuite.players.kill` | Kill all non-player entities |
| `/as reload` | `adminsuite.reload` | Reload config, data, snapshots and the ServerAssist hook |

---

## Permissions

| Permission | Purpose |
|---|---|
| `adminsuite.use` | Open AdminSuite; not granted to players by default |
| `adminsuite.admin` | Master permission; bypasses every granular check and is granted to Oxide's administrators group |
| `adminsuite.players.view`, `adminsuite.players.teleport`, `adminsuite.players.bring`, `adminsuite.players.spawnontop`, `adminsuite.players.spectate` | See the player list and profiles; teleport yourself or send players, bring a player, spawn on top and spectate |
| `adminsuite.players.kick`, `adminsuite.players.ban`, `adminsuite.players.mute`, `adminsuite.players.freeze`, `adminsuite.players.kill` | Kick, ban/unban, mute/unmute, freeze/unfreeze, jail/timeout and kill |
| `adminsuite.players.heal`, `adminsuite.players.feed`, `adminsuite.players.god`, `adminsuite.players.fly` | Heal/feed players, set health and food/water, toggle god mode and fly or open the debug menu |
| `adminsuite.players.inventory`, `adminsuite.players.give`, `adminsuite.players.clearinventory`, `adminsuite.players.takeinventory` | Inspect and repair inventories, give items, open the creative menu, clear or delete items and take all items |
| `adminsuite.players.snapshot`, `adminsuite.players.rollback`, `adminsuite.players.warn`, `adminsuite.players.notes`, `adminsuite.players.buffs` | Capture and restore snapshots, manage warnings, private notes and buffs |
| `adminsuite.world` | Time, weather, airdrops, hordes, scouts, entity spawning, chunk/region resets and game preferences |
| `adminsuite.logs` | View and clear the in-memory audit log |
| `adminsuite.snapshots` | Browse, delete and clean up snapshots |
| `adminsuite.server` | Server information, save, announce, restart warning and shutdown |
| `adminsuite.reload` | Reload config, data, snapshots and the ServerAssist hook |
| `adminsuite.warps` | Create, delete and use warps |
| `adminsuite.moderation.adminlist` | View and edit the server admin file |
| `adminsuite.moderation.whitelist` | View and edit the whitelist |
| `adminsuite.moderation.banlist` | View the ban list |
| `adminsuite.moderation.kickall` | Kick every player |

Oxide administrators always pass every check.

---

## Configuration

File: `oxide/config/AdminSuite.json`. Values are clamped to safe ranges on load.

| Key | Default | Notes |
|---|---|---|
| `ChatPrefix` | `[AdminSuite]` | Prefix used by AdminSuite replies |
| `HookLabel` | `Admin Suite` | Server Assist home-row label |
| `HookDescription` | `The complete staff toolkit: players, moderation, teleports, inventory, world, logs and rollback.` | Home-row description |
| `HookPermission` | `adminsuite.use` | Permission required to see the hook |
| `GrantUseToDefaultGroup` | `false` | Grant `adminsuite.use` to the default player group; keep false for a staff-only tool |
| `ProtectAdminsFromStaff` | `true` | Stop non-admin staff from targeting admins |
| `AllowSelfTarget` | `false` | Allow harmful actions against yourself |
| `NotifyStaffOnJoin` | `true` | Tell online staff when a non-admin joins |
| `LogChat` | `true` | Record chat in the audit log |
| `LogCommands` | `false` | Record console commands in the audit log |
| `LogDeaths` | `true` | Record player deaths |
| `WriteLogFile` | `true` | Also write entries to `oxide/logs` |
| `MaxLogEntries` | `1000` | In-memory log cap |
| `SnapshotBeforeDestructive` | `true` | Snapshot before kick, ban, clear, take, delete item and rollback |
| `SnapshotRetentionDays` | `14` | Delete snapshots older than this many days |
| `MaxSnapshotsPerPlayer` | `20` | Keep at most this many snapshots per player |
| `DefaultBanDays` | `1` | Reserved default |
| `DefaultTimeoutMinutes` | `30` | Reserved default |
| `DefaultFreezeMinutes` | `60` | Duration used by the Freeze button; `0` means indefinite |
| `FreezeCheckSeconds` | `1` | How often frozen players are pulled back |
| `DefaultItemQuality` | `1` | Default quality for `/as give` and GUI give |
| `DefaultSpawnCount` | `1` | Default count for `/as spawn` |
| `MaxWarps` | `50` | Maximum saved warps |
| `EnableJail` | `false` | Enable the Jail action |
| `JailX`, `JailY`, `JailZ` | `0.0` | Jail position; configure these before enabling jail |
| `ProtectedPlayers` | `[]` | Player ids or names that staff cannot target |

After editing the config, run `/as reload` or `oxide.reload AdminSuite`.

---

## Data and logs

| Path | Contents |
|---|---|
| `oxide/data/AdminSuite/data.json` | Warps, mutes, freezes, warnings, notes and personal homes |
| `oxide/data/AdminSuite/snapshots.json` | Snapshot metadata and serialized item data |
| `oxide/logs/AdminSuite-YYYY-MM-DD.log` | One tab-separated line per audit entry |

Snapshots store items with the game's own `ItemValue` binary format, base64 encoded, so mods and quality survive. The plugin loads the most recent 200 lines of the current day's log file on startup so the log view is useful after a restart. The Clear button only clears the in-memory view; the file is kept.

!!! warning
    Do not edit `data.json` or `snapshots.json` while the server is running. Use the GUI or commands instead. Do not hand-edit `snapshots.json`; it contains data written by the game's serializer.

### Snapshots and rollback

| Captured | Not captured |
|---|---|
| Toolbelt, backpack and equipment items, including quality, durability, mods and metadata | Quest progress, skill points, permanent progression, buffs and active quests |
| Position and rotation | Vehicle ownership and land claims |
| Health, stamina, water and food | — |

A rollback clears the current toolbelt, backpack and equipment and writes the snapshot items back. If the player's inventory size changed, slots outside the current size are ignored. The player is teleported to the snapshot position. A pre-rollback snapshot is captured first when `SnapshotBeforeDestructive` is true.

AdminSuite's rollback is designed for item loss, griefing, accidental clears and bad trades. For a full save-game rollback, use your normal world backups.

The audit log records `join`, `leave`, `chat`, `death`, `command`, `admin`, `kick`, `ban`, `unban`, `mute`, `freeze`, `warn`, `snapshot`, `rollback`, `world` and `server` entries. It is an operations aid, not a legal evidence system. Ship `oxide/logs` to external storage and keep server backups if you need guaranteed retention.

---

## Client-authoritative tools

Some values live on the client. AdminSuite uses the client's own `giveself` for give, sends `NetPackageEntityStatChanged` for stats, `NetPackageEntityAliveFlags` for god and `NetPackageTeleportPlayer` for teleport/freeze. Clear, take and repair are applied on the server; the client may need to reopen inventory or relog. Fly toggles directly in single-player/listen-server and opens the debug menu on a dedicated server.

---

## Server Assist integration

| Item | Value |
|---|---|
| Model | Panel / hook; home row shows `[GUI] Admin Suite` |
| Hook id | `adminsuite` |
| Label | Config `HookLabel`, default `Admin Suite` |
| Description | Config `HookDescription`, default `The complete staff toolkit: players, moderation, teleports, inventory, world, logs and rollback.` |
| Permission | Config `HookPermission`, default `adminsuite.use` |
| Icon | `sprite:sa_admin_suite` via `SetPanelIcon` |
| Plugin name | `AdminSuite` |
| Handler | `HandleGuiRequest` |
| Order | Not specified by the plugin; the six-field `RegisterGuiHook` call carries no order argument |
| Entry | `/as`, `/adminsuite`, F7/F6, `[GUI] Admin Suite` row |

The plugin registers through the six-string hook signature. The call is made through its reflection helper so the exact overload is selected:

```csharp
object result = InvokePlugin(serverAssist, "RegisterGuiHook",
    new[] { typeof(string), typeof(string), typeof(string),
            typeof(string), typeof(string), typeof(string) },
    new object[] { HookId, config.HookLabel, config.HookDescription,
                   config.HookPermission, Name, nameof(HandleGuiRequest) });

if (result is bool && (bool)result)
{
    serverAssist.Call("SetPanelIcon", HookId, "sprite:sa_admin_suite");
}
```

Registration runs from three-second and six-second timers in `Init`, from `OnServerInitialized`, and again from `OnServerAssistReady`, so reloading either plugin is safe. `/as reload` saves data, reloads config, data and snapshots and re-registers the hook. `Unload` unregisters with `UnregisterGuiHook("adminsuite")`. `OnPluginUnloaded` resets the local registration flag when Server Assist itself unloads.

The GUI opens through `OpenHookForPlayer`, and `/as inv` opens directly on the inventory screen with `OpenHookForPlayerWithAction`. Every action re-checks the Oxide permission on the server. The player id comes from Server Assist's server-side net-package sender, not from anything the client can spoof.

See [Staff and utility companions](../../integration/companions/staff-utility.md) and [Handlers and lifecycle](../../integration/api/handlers.md).

---

## Typical workflows

### Onboard a moderator

1. Grant `serverassist.use` and `adminsuite.use` to the moderator group.
2. Grant only the granular tools the role needs, such as `adminsuite.players.view`, `adminsuite.players.kick`, `adminsuite.players.mute`, `adminsuite.players.freeze` and `adminsuite.logs`.
3. Ask the moderator to reconnect or use PermissionGui to apply permissions.
4. Confirm the `[GUI] Admin Suite` row is visible and the unavailable actions report the missing nodes.

### Handle a griefing report

1. Open Players and select the suspect to review position, inventory summary, moderation state and notes.
2. Use `/as snapshot <player> grief report` or the Staff Tools snapshot before making changes.
3. Open the Inventory Inspector to review, take or repair specific items.
4. If items were lost, use `/as rollback <player> confirm`; a pre-rollback snapshot is captured first.
5. Use `/as ban <player> 7d <reason> confirm` or the More menu.

### Run a world event

1. Use World or `/as time day`, `/as weather clear`, `/as airdrop`, `/as horde` or `/as scouts`.
2. Announce the event with `/as say <message>`.
3. Save afterwards with `/as save`.
4. Region Reset and chunk resets permanently delete saved chunk data, so use them only with a confirmation and administrator approval.

---

## Troubleshooting

| Symptom | Check |
|---|---|
| The `Admin Suite` row is missing | Confirm both plugins compiled, look for the registration log line, confirm `adminsuite.use` or administrator status, and confirm the player also has `serverassist.use` |
| AdminSuite opens but every row says no permission | Grant the granular permissions; Oxide administrators bypass them |
| `The ServerAssist client mod did not respond` | The client is missing `ServerAssistMod.dll` and its `Config` folder, or has not sent its hello yet |
| `Window group 'x' was not found` | That is a Server Assist/XUi issue for a different button, not AdminSuite |
| Inventory items show `Unknown item` | The item class comes from a mod not loaded on the server, or the data is from an older save |
| Give says the item was not found | Use `/as find <name>` or the GUI search; internal names such as `9mmBullet` and display names such as `9mm Bullet` are accepted |
| Give says the inventory is full | Remaining items are dropped at the player's feet with the vanilla give command; the reply reports how many were given and dropped |
| Creative does nothing visible | `/as creative` toggles the client setting; open your inventory to use it, and check whether the server disables creative mode |
| God mode says ON but damage still applies | The server flag is authoritative. If the client sync failed, the local flag may not update until the player relogs |
| Fly does nothing on a dedicated server | Fly is a client-side debug toggle. `/as fly` opens the debug menu on your own client |
| Rollback did not restore quests or skills | By design; see the captured/not-captured table |
| Timed mutes or freezes seem to stay after a restart | Expired entries are cleaned on save, chat and reconnect; run `/as reload` or clear the entry |
| The log file is not created | Check `WriteLogFile` and `oxide/logs` permissions; the in-memory log still works |

---

## Safety notes

- AdminSuite is a staff tool. Do not grant `adminsuite.use` to normal players. `ProtectAdminsFromStaff` is on by default; add owner ids to `ProtectedPlayers` for an extra layer.
- Destructive GUI actions require confirmation. Destructive chat commands require the word `confirm` at the end. `SnapshotBeforeDestructive` is on by default.
- Region Reset and world chunk resets permanently delete saved chunk data. They are intentionally behind a confirmation and `adminsuite.world`.
- Do not hand-edit `snapshots.json`; it uses the game's item serializer.

---

## Related pages

- [Server Assist product guide](../../core/server-assist.md)
- [Server Assist API](../../integration/api/index.md)
- [Staff and utility companions](../../integration/companions/staff-utility.md)
- [Handlers and lifecycle](../../integration/api/handlers.md)
- [Permissions](../../core/permissions.md)
- [Companion problems](../../reference/troubleshooting/companions.md)
- [PermissionGui](permission-gui.md)
