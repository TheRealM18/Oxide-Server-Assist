# Player Profiles

> **Version:** 1.0.0 · **Type:** Server-only Oxide plugin with a Server Assist panel and optional `@pp|` companion protocol · **Source:** [PlayerProfiles.cs](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/PlayerProfiles/oxide/plugins/PlayerProfiles.cs)

Player Profiles gives one Steam account several saved characters. A profile stores the game's own `PlayerDataFile`: inventory, backpack, equipment, held item, level, XP, perks, books, quests, challenges, buffs, health, food, water, waypoints, bedroll spawn points, position, kills, deaths, score, playtime, crafting queue and owned entities.

Permissions and groups stay on the account by design. With an updated Server Assist client mod, switching happens in place: no disconnect and no re-login. Without that client mod, or when starting a brand-new profile, the plugin falls back to a safe reconnect.

---

## What this is and is not

This is one Steam account with several characters, not a second account.

| Shared across all profiles | Stored per profile |
|---|---|
| Oxide permissions, groups and admin status | Inventory, backpack, equipment and held item |
| Land claims, allies, parties and other `PersistentPlayerData` | Level, XP, perks, books, quests and challenges |
| World chests, bases, vehicles, traders and loot | Buffs, health, food, water and waypoints |
| Map exploration in a separate per-player map database | Bedroll spawn points, position and owned-entity references |
| Data other plugins store by raw Steam ID | Kills, deaths, score and playtime |
| Steam name and login identity | Crafting queue |

Anything another plugin stores by raw player ID is shared unless that plugin is changed to use the profile-scoped ID API described below.

Character appearance is not changed by an in-place switch. On a reconnect it follows the saved character profile when the server has `PersistentPlayerProfiles` enabled. Inventory, armor and cosmetics do change.

!!! warning
    Vehicles and drones are world entities. Exit a vehicle or drone before switching. The owned-entity references are stored per profile, but the actual objects remain in the world. Land claims, allies and bedroll ownership stay account/world-level.

---

## Requirements

| Requirement | Detail |
|---|---|
| Game | 7 Days to Die V3.4, built against the 3.4 managed assemblies |
| Server | Oxide / uMod |
| Server Assist plugin | 2.1.0 or newer on the server |
| Server Assist client mod | 2.1.0 or newer on every client that should switch |
| In-place switching | Server Assist plugin and `ServerAssistMod` builds with the companion protocol (current 2.1.x builds) |
| EAC | Off |
| Art | Optional `ServerAssistArt` UI atlas for the `sa_profile_*` panel icons |

Older Server Assist combinations still join and use the reconnect path. An updated client mod stays silent when the server plugin is older.

---

## Installation

### Server

1. Stop the server.
2. Install the Server Assist plugin and the updated `AA0_ServerAssistMod` folder.
3. Copy `oxide/plugins/PlayerProfiles.cs` into `oxide/plugins/`.
4. Start the server.
5. Check the log for:

   ```text
   [PlayerProfiles] loaded. Permissions: playerprofiles.slots.1 - 10. UI is provided by Server Assist.
   [PlayerProfiles] registered the profiles panel with Server Assist (sprite:sa_profile_panel).
   ```

The plugin writes `oxide/config/PlayerProfiles.json` on first start and creates the default `players` and `administrators` grants described below.

### Clients

Install the same updated `AA0_ServerAssistMod` folder used by the server. There is no separate PlayerProfiles client mod and no extra net-package type.

If a client keeps the old Server Assist mod, it can still join. The Profiles panel shows "reconnect required" for switches, and the server kicks the player with the reconnect message when they switch.

---

## Using the panel

Open Server Assist with `F7` or `F6`, then open the **Profiles** card. `/profiles` opens the panel directly, or lists profiles when Server Assist is unavailable. `/profile ...` is the command form.

| Panel control | Behaviour |
|---|---|
| Profile cards | One card per profile. The active profile has an `ACTIVE` badge and a gold tint; a new profile has a `NEW` badge |
| New | Create a profile. New profiles start at level 1 with an empty inventory. The first switch to a new profile reconnects once so 7DTD can run its normal new-character spawn |
| Rename | Give a profile a new name. The saved file is not moved |
| Delete | Delete an inactive profile. The payload is archived first |
| Save Current | Capture the active profile immediately |
| Switch | Load the selected profile. With the updated client mod this happens in place |
| Refresh | Rebuild the list |
| Reload | Admin only: reload config and permissions |
| Back | Return to Server Assist |

The panel uses one text input. Type a name in **New** or **Rename** and press Submit, or press Create/Save after submitting.

---

## Commands

| Command | Permission | Behaviour |
|---|---|---|
| `/profiles` | `playerprofiles.use` | Open the Profiles panel, or list profiles if Server Assist is unavailable |
| `/profile` | `playerprofiles.use` | Open the panel, or list profiles when the panel cannot open |
| `/profile list` | `playerprofiles.use` | List profiles and the active one |
| `/profile use <name>` | `playerprofiles.use` | Switch to a profile; alias `/profile switch` |
| `/profile new <name>` | `playerprofiles.use` and an available slot | Create a profile; alias `/profile create` |
| `/profile rename <old> <new>` | `playerprofiles.use` | Rename a profile |
| `/profile delete <name> confirm` | `playerprofiles.use` | Delete an inactive profile; alias `/profile remove` |
| `/profile save` | `playerprofiles.use` | Capture the active profile immediately |
| `/profile reload` | `playerprofiles.admin` | Reload config and permissions |
| `/profile help` | `playerprofiles.use` | Show the command list |

---

## Permissions

| Permission | Purpose |
|---|---|
| `playerprofiles.use` | Open the panel and use commands. Granted to the `players` group by default |
| `playerprofiles.slots.1` … `playerprofiles.slots.N` | Maximum number of profiles the player may create. `N` is `MaxSlots` (default 10, hard cap 50). The highest granted node wins |
| `playerprofiles.slots.*` | All configured slots |
| `playerprofiles.admin` | Admin override. Grants `MaxSlots`, the Reload action and `/profile reload`. Granted to the `administrators` group by default |

Default grants:

| Group | Permissions |
|---|---|
| `players` | `playerprofiles.use`, `playerprofiles.slots.2` |
| `administrators` | `playerprofiles.use`, `playerprofiles.admin`, `playerprofiles.slots.10` |

Examples:

| Node | Meaning |
|---|---|
| `playerprofiles.slots.1` | One profile |
| `playerprofiles.slots.2` | Two profiles (default for players) |
| `playerprofiles.slots.10` | Ten profiles |
| `playerprofiles.slots.*` | Every configured slot |

Existing profiles are never deleted when a slot permission is reduced. New profiles cannot be created until the player is under the limit again.

---

## Configuration

File: `oxide/config/PlayerProfiles.json`

| Key | Default | Notes |
|---|---|---|
| `ChatPrefix` | `[Profiles]` | Chat prefix for command replies |
| `PermissionPrefix` | `playerprofiles` | Base permission node |
| `UsePermission` | `playerprofiles.use` | Panel and command permission |
| `AdminPermission` | `playerprofiles.admin` | Admin override node |
| `MaxSlots` | `10` | Highest slot node registered; clamped to 1–50 |
| `DefaultSlots` | `2` | Slots for players with no slot node; clamped to 0–`MaxSlots` |
| `GrantDefaultSlotsToPlayers` | `true` | Grant `slots.DefaultSlots` to the `players` group |
| `GrantAdminToAdministrators` | `true` | Grant admin and `slots.MaxSlots` to the `administrators` group |
| `MaxNameLength` | `24` | Profile-name limit; clamped to 1–48 |
| `BootstrapProfileName` | `Main` | Name for the automatically created first profile |
| `NewProfileStartsFresh` | `true` | `true`: new profiles start fresh. `false`: copy the current character into the new profile |
| `AllowSeamlessSwitch` | `true` | Use the in-place client-mod protocol when the client supports it |
| `ReconnectMessage` | `Profile '{profile}' is ready.\n\nReconnect to the server to load it.` | Kick message for the reconnect path. Supports `{profile}` and `{player}` |
| `SwitchCooldownSeconds` | `5` | Minimum time between switch attempts; clamped to 0–3600 |
| `DisallowWhileDead` | `true` | Block switching while dead |
| `DisallowWhileInVehicle` | `true` | Block switching while attached to a vehicle or drone |
| `KeepPositionOnSwitch` | `false` | `false`: restore the profile's saved position. `true`: keep the current position |
| `AutosaveSeconds` | `60` | Capture online active profiles this often; clamped to 10–3600. Also captures on server save and disconnect |
| `SwitchTimeoutSeconds` | `20` | In-place handshake timeout; clamped to 5–120 |
| `ChunkBytes` | `24576` | Profile payload chunk size; clamped to 4096–65536 |
| `BackupCount` | `2` | Rotating backups per profile file; clamped to 0–2 |
| `ArchiveDeletedProfiles` | `true` | Copy deleted payloads to `deleted/` before removing them |
| `RequireSameModSet` | `true` | Refuse to switch to a profile saved with a different mod set |
| `RequireSameGameVersion` | `false` | Refuse to switch across game versions. 7DTD can usually read older files |
| `Debug` | `false` | Extra log lines for captures and switches |

---

## Data files

| Path | Purpose |
|---|---|
| `oxide/data/PlayerProfiles/<safe-player-id>/state.json` | Profile names, IDs, active profile, pending switch, metadata and player name |
| `oxide/data/PlayerProfiles/<safe-player-id>/<profile-id>.ttp` | Saved `PlayerDataFile` payload |
| `oxide/data/PlayerProfiles/<safe-player-id>/<profile-id>.ttp.bak1` | First rotating backup |
| `oxide/data/PlayerProfiles/<safe-player-id>/<profile-id>.ttp.bak2` | Second rotating backup |
| `oxide/data/PlayerProfiles/<safe-player-id>/deleted/...` | Archived payloads when `ArchiveDeletedProfiles` is true |

The `.ttp` files use the game's own save container:

```text
"ttp\0" | version byte | PlayerDataFile.Write(..., Persistency)
```

The game's `ItemStack` format is used, so quality, durability, ammo, mods, cosmetic slots and other item data survive automatically.

`state.json` holds profile names, IDs, active profile, pending switch, metadata such as level, deaths, playtime, bytes, saved time, game version and mod-set hash, and the player name. It does not hold permissions.

!!! warning
    The plugin never edits the live vanilla `.ttp` in place. It writes a temporary file and replaces the target atomically. The vanilla save is the fallback copy.

---

## Server Assist integration

Player Profiles registers the panel id `playerprofiles` with Server Assist:

| Item | Value |
|---|---|
| Panel id | `playerprofiles` |
| Label | `Profiles` |
| Description | `Save and switch full character profiles.` |
| Permission | `playerprofiles.use` |
| Plugin name | `PlayerProfiles` |
| Handler | `HandleGuiRequest(string playerId, string payload)` |
| Icon | `sprite:sa_profile_panel` |
| Order | `25` |

It calls `RegisterPanel` first and falls back to `RegisterGuiHook` plus `SetPanelIcon` on older Server Assist builds. It re-registers on `OnServerAssistReady` and unregisters on unload. There is no PlayerProfiles XUi XML, no client DLL and no extra net-package type.

The panel renders profile cards, action buttons and one shared text input through the normal Server Assist view protocol. It owns the `playerprofiles` hook registration and the `playerprofiles.use` permission; Server Assist only routes to the hook.

---

## Optional `@pp|` companion protocol

The in-place switch uses the optional companion protocol described in [Companion protocols](../../integration/api/companion-protocols.md). It is optional: when the plugin, server hook or client build is missing, the plugin uses the reconnect path.

### Compatibility contract

| Item | Value |
|---|---|
| Panel / hook id | `playerprofiles` |
| Panel permission | `playerprofiles.use` |
| Plugin name | `PlayerProfiles` |
| Handler method | `HandleGuiRequest(string playerId, string payload)` |
| Protocol prefix | `@pp\|` |
| Protocol version | `1` |
| Client request shape | `@pp\|playerprofiles\|<protocol body>` |
| Server response shape | `@pp\|<protocol body>` |

No new NetPackage type is registered. The protocol travels inside the existing `NetPackageServerAssistRequest` and `NetPackageServerAssistResponse` packets, so the server package-ID table does not change and older clients are not disconnected with "Unknown package type". The client half lives in [PlayerProfilesClient.cs](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/ServerAssist/src/PlayerProfilesClient.cs).

### Messages

Client-to-server requests carry the panel id so Server Assist can route them without window state:

```text
@pp|playerprofiles|hello|1
@pp|playerprofiles|ready|token
@pp|playerprofiles|chunk|token|index
@pp|playerprofiles|applied|token|sha256
@pp|playerprofiles|failed|token|reason
@pp|playerprofiles|recover|token|reason
@pp|playerprofiles|abort|token|reason
```

Server-to-client responses do not carry the panel id:

```text
@pp|prepare|token|fromName|toName
@pp|begin|token|chunks|sha256|bytes|chunkBytes
@pp|chunk|token|index|base64
@pp|done|token
@pp|abort|token|reason
@pp|ok
```

Server Assist strips the panel id and passes the hook `@pp|<protocol body>`. A handler response that starts with `@pp|` is returned verbatim; Server Assist does not append hook chrome, `tab=hook`, `background=` or `banner=` to it. The routing branch must run before the normal `a|` / `t|` dispatcher, and the companion handler performs its own permission check.

### In-place switch flow

1. The player confirms a switch in the Profiles panel or by command.
2. With the updated client mod:

   ```text
   server -> client   @pp|prepare
   client             freezes input, locks outgoing player-data syncs, and
                      sends a full PlayerDataFile
   client -> server   @pp|ready
   server             captures the old profile, validates the target, and
                      streams the new .ttp payload in chunks
   client             applies the payload locally and refreshes the HUD,
                      inventory, quest tracker and crafting queue, while
                      staying frozen and not syncing
   client -> server   @pp|applied
   server             applies the same payload to the server entity, writes
                      the new vanilla .ttp, commits state, and refreshes the
                      active profile badge
   server -> client   @pp|done
   client             unfreezes and reopens the Profiles panel
   ```

3. On any failure the old profile is kept. A partially applied client is kicked back to a reconnect so both sides resync.

The client locks `countdownSendPlayerDataFileToServer` and `countdownSendPlayerInventoryToServer`, suppresses outgoing sync until commit, closes cursor windows, clears old buffs, waypoints, owned entities, spawn point, recipes and the quest journal before applying, and applies the payload with `StreamModeRead.Persistency`. It stays frozen until `@pp|done` or a 10-second fallback.

### Reconnect fallback

Used for a new profile, an old client mod, or when seamless switching is disabled:

1. The server captures the current profile.
2. The server records a pending reconnect target.
3. The server kicks the player with `ReconnectMessage`.
4. On the next connect, `OnUserApprove` writes the target `.ttp` or deletes the vanilla file for a new profile before the vanilla spawn load.
5. The game loads the character normally.

A new profile needs this once. After its first spawn it has data, and later switches can be in place.

### Compatibility

| Server Assist plugin | ServerAssistMod | PlayerProfiles | Result |
|---|---|---|---|
| Patched 2.1.x | Patched 2.1.x | Installed | In-place switch |
| Patched | Unpatched | Installed | Reconnect switch, no errors |
| Unpatched | Patched | Installed | Reconnect switch, no errors |
| Patched | Patched | Not installed | `@pp` requests are ignored; no error window |
| Unpatched | Unpatched | Installed | Reconnect switch |

The bridge probes for `RouteCompanionProtocol` by name. If the method is absent, the bridge stays silent for `@pp` requests, which is the correct old-plugin behaviour.

---

## Crash and disconnect safety

| Behaviour | Detail |
|---|---|
| Autosave | Captures online active profiles every `AutosaveSeconds` |
| Server save | `OnServerSave` captures active profiles |
| Disconnect | `OnPlayerDisconnected` captures the departing profile |
| Restart | If the vanilla `.ttp` is newer than the profile file, it is adopted into the active profile |
| Newer profile file | Materialised over the vanilla `.ttp` before spawn |
| Interrupted in-place switch | Marked pending. On the next login the server reverts to the profile the player came from, so a half-applied switch cannot become the active character |
| Unreadable profile | Falls back to `.bak1` / `.bak2`; a corrupt vanilla save is never allowed to lock a player out |

---

## Profile-scoped ID API and hooks

Other plugins can call Player Profiles through `Plugin.Call`:

| Method | Return value |
|---|---|
| `GetActiveProfile(string playerId)` | Active profile id, or `null` |
| `GetActiveProfileName(string playerId)` | Active display name, or `null` |
| `GetProfileScopedId(string playerId)` | `"<playerId>#<profileId>"`, or the raw `playerId` when there is no active profile |

Example:

```csharp
string scopedId = plugins.Find("PlayerProfiles")
    .Call("GetProfileScopedId", playerId) as string;

// Store economy, cooldowns or other per-character data under scopedId.
```

Hooks:

| Hook | Arguments |
|---|---|
| `OnPlayerProfileCreated` | `IPlayer player, string profileId, string name` |
| `OnPlayerProfileRenamed` | `IPlayer player, string profileId, string oldName, string newName` |
| `OnPlayerProfileDeleted` | `IPlayer player, string profileId, string name` |
| `OnPlayerProfileSwitch` | `IPlayer player, string fromProfileId, string toProfileId, bool inPlace` |

To store per-profile data in another plugin, key it by `GetProfileScopedId` instead of the raw player ID. Without that change, the other plugin's data is shared across profiles. Permissions and groups deliberately remain account-wide.

---

## Typical workflows

### Create and switch to a second character

1. Run `/profile new Scout` or use **New** in the panel.
2. The first switch to the new profile reconnects once and spawns a level 1 character.
3. Switch back to the original profile afterwards. That switch can happen in place.
4. Use `/profile save` before a risky switch if you want an immediate capture.

### Give a player more slots

1. Grant `playerprofiles.slots.3` with PermissionGui or the Oxide permission commands.
2. The player can create a third profile on the next attempt.
3. If the node is later revoked, existing profiles remain readable but new creation is blocked until the player is under the limit.

### Keep another plugin's data per character

1. Call `GetProfileScopedId(playerId)` when the player connects or switches.
2. Store the plugin's data under that scoped ID.
3. Listen for `OnPlayerProfileSwitch` to move the active data pointer when the player switches.

---

## Troubleshooting

| Symptom | Check |
|---|---|
| The Profiles card is not on the Server Assist board | Confirm `playerprofiles.use`, that `PlayerProfiles.cs` compiled, and that the log shows the panel registration line |
| The card opens but says "reconnect required" for every switch | The server plugin, client mod or both are older than the companion-protocol build. Install the current Server Assist plugin and `ServerAssistMod` on the server and client. Switches still work, but they reconnect |
| `Your current profile could not be saved.` | The server could not read the live `PlayerDataFile`. Check the log for capture errors. The switch is cancelled so no progress is lost |
| `That profile is dead. Reconnect to respawn it.` | A dead profile cannot be applied in place. Select it and use the reconnect path, or let it respawn |
| `That profile was saved with a different mod set.` | Item IDs are load-order integers. Restore the original mod set, or set `RequireSameModSet` to `false` if you understand the risk |
| A new profile reset to level 1 | Expected when `NewProfileStartsFresh` is true. Set it to `false` if new profiles should copy the current character |
| The quest tracker or inventory looked stale for a moment | The client closes cursor windows and rebuilds the quest tracker, toolbelt, backpack and equipment. Reopen any window that was open during the switch |
| Vehicles moved or ownership changed | Owned entities are stored per profile and replaced on switch. World vehicles remain world objects. Exit vehicles before switching. Land claims and allies never switch |
| Profiles show the wrong slot count | Check the highest granted `playerprofiles.slots.N` node, `DefaultSlots`, `MaxSlots` and `playerprofiles.admin` |
