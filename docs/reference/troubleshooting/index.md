# Troubleshooting

Start with the three questions that explain most failures:

1. **Is the client mod installed and parsed?** Look for `[ServerAssist] XUi window initialized`.
2. **Is the server plugin loaded?** Look for the plugin in `oxide/plugins` and a compile error in the server log.
3. **Do the server and client use the same build?** Replace both, not one.

---

## Quick symptom table

| Symptom | Most likely cause | Jump |
|---|---|---|
| Nothing happens on `F7` | Client mod missing or XUi not parsed | [Window will not open](#window-will-not-open) |
| `window group ... was not found on this client` | Button target not installed | [Window group errors](#window-group-errors) |
| `ServerAssist window group was not found` | Client XUi XML missing or nested | [XUi group diagnostics](#xui-group-diagnostics) |
| `Style key ... not found` | Old `windows.xml` with square brackets | [Style key errors](#style-key-errors) |
| `The ServerAssist Oxide plugin is not loaded` | Plugin missing or failed to compile | [Plugin not loaded](runtime.md#plugin-not-loaded) |
| `The ServerAssist client mod is not installed` | Client folder missing or no hello | [Client mod not installed](runtime.md#client-mod-not-installed) |
| Window opens empty | No response, hook error, net package issue | [Empty window](runtime.md#empty-window) |
| Button missing | Disabled, permission, or hidden by config | [Button missing](runtime.md#button-missing) |
| Every action says no permission | Missing node or wrong player id | [Permission denied](runtime.md#permission-denied) |
| Icon is a hammer | Item/atlas/sprite not resolvable | [Icons and item icons](companions.md#icons-and-item-icons) |
| Item search is empty | Item not on the server or query too narrow | [Item icons](companions.md#item-icons) |
| `[GUI]` row missing | Companion not registered or permission missing | [Hook row missing](companions.md#hook-row-missing) |
| Permission Manager row missing | Old standalone button or hook not registered | [Permission Manager row missing](companions.md#permission-manager-row-missing) |
| Provider not found | Not registered or opened too early | [Provider errors](companions.md#provider-errors) |
| Backpack card or pages missing | Panel registration, `backpack.N` permission, Server Assist | [Dynamic Backpack](companions.md#dynamic-backpack) |
| Config/data did not load | Invalid JSON or hand edit | [Config and data](data-reports.md#config-and-data) |

---

## Window will not open

### Check the client log

Expected:

```text
[ServerAssist] loading companion mod from ...
[ServerAssist] XUi window initialized
[ServerAssist] window opened
```

| Missing line | Meaning |
|---|---|
| `loading companion mod` | `ServerAssistMod.dll` is not in `Mods/<folder>/` or EAC is on |
| `XUi window initialized` | `Config/XUi_InGame/windows.xml` was not parsed or the controller failed |
| `window opened` | The window group was not found or the open request failed |

### Check the folder layout

The most common mistake is a nested folder:

```text
Mods/AA0_ServerAssistMod/ServerAssist/Config/XUi_InGame/xui.xml   <- wrong
Mods/AA0_ServerAssistMod/Config/XUi_InGame/xui.xml                <- correct
```

`ServerAssistMod.dll` and `Config` must be siblings directly under the mod folder.

### Check EAC

EAC must be off on the server and every client. A code mod does not load with EAC enabled.

### Check the game restart

XUi XML is parsed at startup. Replacing `windows.xml` and reconnecting is not enough. Fully exit the game and start it again.

### Check the client is capable

The client sends a `hello` after the local player spawns. If the server never receives it, `/sa` reports that the client mod is not installed or has not sent its hello. Rejoin after a full restart.

---

## Window group errors

### `Window group '<name>' was not found on this client.`

This is a **button launch** error. It means the button's `window` target is not a loaded XUi window group on that client.

Check:

1. The target name is spelled exactly as the other mod's `xui.xml` group name.
2. The target mod is installed on the client.
3. The target mod's `Config/XUi_InGame/xui.xml` is directly under its mod folder.
4. The client restarted after installing the target mod.
5. The group is not the old name from a previous release.

The error is local: the player stays on the Server Assist board.

### `ServerAssist window group was not found`

This is a **Server Assist window** error. The client assembly is loaded but its XUi group is missing.

Check:

1. `Config/XUi_InGame/xui.xml` exists and contains:

   ```xml
   <window_group name="serverAssist"
                 controller="ServerAssistWindowGroup, ServerAssistMod">
     <window name="windowServerAssist" />
   </window_group>
   ```

2. The file is directly under the mod folder.
3. `windows.xml` contains `windowServerAssist` with controller `ServerAssistWindow, ServerAssistMod`.
4. The client is not running an old DLL with a new XML, or a new DLL with an old XML.
5. No duplicate Server Assist folder is installed.

### Target `serverAssist` itself

A button that targets `serverAssist` is refused on purpose:

```text
That button points at the Server Assist window itself.
```

Use a hook or a different target.

---

## XUi group diagnostics

When the client cannot find the group, it writes a diagnostic line:

```text
[ServerAssist] window group was not found. Is Config/XUi_InGame/xui.xml installed?
count=... | <id> -> <controller> | <id> -> <controller>
```

How to read it:

| Part | Meaning |
|---|---|
| `count=` | Number of XUi window groups the client parsed |
| `<id> -> <controller>` | A matching group id and its controller type |

The diagnostic filters to ids containing `admin` or `helper` and controllers containing `ServerAssist`, so it highlights old and new launcher groups without dumping the entire XUi.

### What to do with the diagnostic

1. **If `serverAssist -> XUiC_ServerAssistWindowGroup` is present**, the group parsed but the lookup still failed. Restart the game; check for a duplicate group with the same id; check for a second `ServerAssistMod.dll`.
2. **If an old group is present** (for example an old launcher group), remove the old client folder. Duplicate groups confuse name and controller lookups.
3. **If `count=0` or no `ServerAssist` controller is listed**, the XML was not parsed. Fix the folder layout and restart.
4. **If the group is listed under a different id**, the XML was edited or another mod renamed it. Restore the supplied `xui.xml`.

### Lookup fallbacks

The client tries, in order:

1. `player.PlayerUI.xui` — the XUi that actually parsed the in-game config.
2. A scan of `XUi.WindowGroups` for id `serverAssist`.
3. A scan for the `XUiC_ServerAssistWindowGroup` controller type.
4. `FindWindowGroupByName("serverAssist")` and lowercase.
5. `GetWindowGroupById("serverAssist")`.
6. The window manager name map.
7. The internal `windowsAndGroupsByControllerType` index.

A failure after all seven means the group is genuinely not in the client's XUi.

!!! tip
    The diagnostic is intentionally written at warning level. It is the fastest way to tell "XML not installed" apart from "lookup table weirdness".

---

## Style key errors

Example:

```text
Style key ... not found for windowServerAssist
```

Cause: an old `windows.xml` is installed. Captions must not use square brackets in XML. For example:

```xml
<!-- wrong -->
<simplebutton caption="[Home]" />

<!-- correct -->
<simplebutton caption="Home" />
```

The client adds `[ ]` around the active tab at runtime. The XML stores plain captions.

Fix: install the current `Config/XUi_InGame/windows.xml` from the same build as the DLL and restart the game.

---

## Troubleshooting pages

| Page | Covers |
|---|---|
| [Runtime problems](runtime.md) | Plugin load, client hello, empty window, missing buttons and permission failures |
| [Companion problems](companions.md) | Icons, hooks, providers, Dynamic Backpack and Player Profiles |
| [Data and reports](data-reports.md) | Config/data load, server-side errors, collecting diagnostics and repair |
