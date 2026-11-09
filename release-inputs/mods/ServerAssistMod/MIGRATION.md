# MIGRATION.md - AdminHelper to Server Assist 2.0

This is the migration sheet for the old **AdminHelper** release. The product
name is now **Server Assist**, and the runtime identifiers are now
`ServerAssist` / `serverassist`.

This file intentionally keeps the old name so server owners and companion
plugin authors can find and replace it. Do not install a second AdminHelper
client next to Server Assist.

---

## 1. Fast search-and-replace table

Use these replacements in companion plugins, configs, scripts and docs:

| Old | New |
|---|---|
| `AdminHelper` | `ServerAssist` |
| `AdminHelperMod` | `ServerAssistMod` |
| `Admin Helper` | `Server Assist` |
| `adminHelper` | `serverAssist` |
| `adminhelper` | `serverassist` |
| `AdminHelperWindowGroup` | `ServerAssistWindowGroup` |
| `AdminHelperWindow` | `ServerAssistWindow` |
| `AdminHelperClient` | `ServerAssistClient` |
| `AdminHelperServer` | `ServerAssistServer` |
| `AdminHelperBridge` | `ServerAssistBridge` |
| `AdminHelperProtocol` | `ServerAssistProtocol` |
| `AdminHelperView` | `ServerAssistView` |

The same replacements apply to file names and to the XUi controller strings.

---

## 2. Folder and file mapping

| Old | New |
|---|---|
| `Mods/AdminHelper/` | `Mods/AA0_ServerAssistMod/` |
| `AdminHelperMod.dll` | `ServerAssistMod.dll` |
| `Config/XUi_InGame/xui.xml` | `Config/XUi_InGame/xui.xml` |
| `Config/XUi_InGame/windows.xml` | `Config/XUi_InGame/windows.xml` |
| `oxide/plugins/AdminHelper.cs` | `oxide/plugins/ServerAssist.cs` |
| `oxide/config/AdminHelper.json` | `oxide/config/ServerAssist.json` |
| `oxide/data/AdminHelper/data.json` | `oxide/data/ServerAssist/data.json` |

The old XUi group `adminHelper` is replaced by `serverAssist`. The old window
`windowAdminHelper` is replaced by `windowServerAssist`.

A client with the old DLL cannot use the new server. Remove the old folder from
every client before installing Server Assist.

---

## 3. Command mapping

| Old | New |
|---|---|
| `/ah` | `/sa` |
| `/ah open` | `/sa open` |
| `/ah list` | `/sa list` |
| `/ah add ...` | `/sa add ...` |
| `/ah remove ...` | `/sa remove ...` |
| `/ah reload` | `/sa reload` |
| `/adminhelper` | `/serverassist` |
| `!ah` | `!sa` |
| `!adminhelper` | `!serverassist` |

The client console command is now `serverassist` / `sa`.

---

## 4. Permission mapping

| Old | New |
|---|---|
| `adminhelper.use` | `serverassist.use` |
| `adminhelper.admin` | `serverassist.admin` |
| `adminhelper.custom` | `serverassist.custom` |
| `adminhelper.button.<id>` | `serverassist.button.<id>` |
| `adminhelper.personal.<id>` | `serverassist.personal.<id>` |

Server Assist 2.0 does not grant the old permission nodes. Re-grant the new
nodes to your groups and users, or use the PermissionGui migration helpers if
available.

Example:

```text
oxide.grant group admin serverassist.use
oxide.grant group admin serverassist.admin
oxide.grant group admin serverassist.custom

oxide.grant user 76561198000000000 serverassist.use
oxide.grant user 76561198000000000 serverassist.admin
```

If you used per-button permissions, map them one-for-one:

```text
adminhelper.button.shop  ->  serverassist.button.shop
```

---

## 5. API mapping for companion plugins

### Plugin lookup

Old:

```csharp
Plugin plugin = plugins.Find("AdminHelper");
```

New:

```csharp
Plugin plugin = plugins.Find("ServerAssist");
```

### Ready hook

Old:

```csharp
private void OnAdminHelperReady()
{
    RegisterMyHook();
}
```

New:

```csharp
private void OnServerAssistReady()
{
    RegisterMyPanel();
}
```

### Home-row hooks

The method names below are retained for compatibility, but new plugins should
prefer the richer `RegisterPanel` API.

| Old call | New call |
|---|---|
| `RegisterGuiHook(...)` | `RegisterPanel(...)` or `RegisterGuiHook(...)` |
| `UnregisterGuiHook(id)` | `UnregisterPanel(id)` or `UnregisterGuiHook(id)` |
| `OpenHookForPlayer(playerId, id)` | `OpenPanel(playerId, id, "")` |
| `OpenHookForPlayer(playerId, id, action)` | `OpenPanel(playerId, id, action)` |

`RegisterPanel` adds an icon and an explicit order:

```csharp
serverAssist.Call(
    "RegisterPanel",
    "voteskip",                         // id
    "Vote: Skip Night",                 // label
    "Start or join the night-skip vote",// description
    "voteskip.use",                     // permission
    "VoteSkipNight",                    // provider plugin
    "HandleVoteSkipRequest",            // handler method
    "ui_game_symbol_map",               // icon
    20);                                // order
```

### Provider/tab API

These remain valid under Server Assist:

```csharp
RegisterGuiProvider(providerId, pluginName, handlerMethod,
                    title, tab1, tab2, tab3);
OpenGuiProvider(playerId, providerId, payload);
UnregisterGuiProvider(providerId);
```

Provider handlers receive the original payload:

```text
a|open
a|buy|12
t|<encoded text>
```

### Handler signature

Unchanged:

```csharp
public string HandleGuiRequest(string playerId, string payload)
```

The return protocol is the same line protocol, now with optional icon fields.

---

## 6. View protocol additions

The new client understands these optional row fields:

```text
row=index|action|label|sub|enabled|selected|icon|badge|tint
```

Examples:

```text
row=0|launch|shared|shop|Shop|Buy and sell items|1|0|ui_game_symbol_hammer|PLUGIN|
row=1|launch|shared|kit|Admin Kits|Give a kit|1|0|item:gunPistol||#FFCC66
```

Icon values:

```text
item:gunPistol
sprite:ui_game_symbol_map
atlas:ServerAssistAtlas:permissions
```

Plain sprite names are also accepted and treated as `sprite:<name>`.

The AdminHelper-era six-field row is still accepted. Missing icon fields fall
back to `ui_game_symbol_hammer`.

---

## 7. Data and config migration

1. Stop the server and all clients.
2. Keep a backup of `oxide/config/AdminHelper.json` and
   `oxide/data/AdminHelper/data.json`.
3. Rename them:

   ```text
   AdminHelper.json       -> ServerAssist.json
   AdminHelper/data.json  -> ServerAssist/data.json
   ```

4. Change permission nodes inside the data file if you used per-button
   permissions:

   ```text
   adminhelper.button.*    -> serverassist.button.*
   adminhelper.personal.*  -> serverassist.personal.*
   ```

5. Start the server. Server Assist writes a normalized config and data file.

If you prefer a clean install, delete the old files and recreate the buttons
through the Server Assist GUI.

---

## 8. Dynamic Backpack migration

The backpack is now a server-only Server Assist panel. There is no backpack
window, client DLL, bridge type or custom net package. DynamicBackpack
registers a `backpack` panel with `RegisterPanel` and returns pages through
`HandleBackpackGui`.

Old builds used a bridge build id and a separate window:

```text
AdminHelper-1.2.0-backpack-1
ServerAssist-2.0.0-backpack-1
dynamicBackpack / windowDynamicBackpack
```

Those are all removed in 2.0. Delete the old client backpack folder and use the
current DynamicBackpack plugin and Server Assist mod together. The 81-slot
storage format is unchanged; slot index mapping is:

```text
slot = (page - 1) * 9 + card
```

Permissions now unlock pages instead of rows:

```text
backpack.1  -> page 1
backpack.2  -> pages 1-2
...
backpack.9  -> pages 1-9
backpack.*  -> every page
```

---

## 9. Companion plugin checklist

Update these plugins to look for `ServerAssist` and to use
`OnServerAssistReady`:

- PermissionGui
- AdminShop
- AuctionHouse
- VoteSkipNight
- AdminKits
- AdminSuite
- DailyRewards
- DynamicBackpack
- any custom plugin that called `plugins.Find("AdminHelper")`

Search each plugin for the old name and replace it using the table in section 1.

Recommended compatibility order while rolling out:

```csharp
Plugin serverAssist = plugins.Find("ServerAssist");
if (serverAssist == null)
{
    serverAssist = plugins.Find("AdminHelper"); // temporary only
}
```

Remove the fallback after every client and plugin has been migrated.

---

## 10. Client rollout checklist

1. Remove the old `Mods/AdminHelper` folder from every client.
2. Install `AA0_ServerAssistMod`.
3. Delete old XUi caches or restart the game completely.
4. Verify the log contains:

   ```text
   [ServerAssist] loading companion mod ...
   [ServerAssist] XUi window initialized
   ```

5. Press F7 or F6, or type `/sa`.
6. Confirm the window title is **SERVER ASSIST** and the left navigation opens
   Home / Shared / My Buttons.

Do not run an old and new client DLL side by side. The duplicate XUi groups and
net packages will cause confusing window-group failures.

---

## 11. Verification commands

```text
/sa
/sa list
/sa reload
```

Server log checks:

```text
[ServerAssist] registered GUI hook ...
[ServerAssist] registered GUI provider ...
```

Client log checks:

```text
[ServerAssist] XUi window initialized
[ServerAssist] window opened
```

If the window group is not found, the client logs the loaded XUi group IDs and
controllers so the install can be checked.
