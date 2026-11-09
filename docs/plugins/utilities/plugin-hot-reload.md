# Plugin Hot Reload

> **Version:** 1.0.1 · **Type:** Server-side Oxide plugin · **Source:** [PluginHotReload.cs](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/PluginHotReload/oxide/plugins/PluginHotReload.cs)

PluginHotReload watches `oxide/plugins` for C# source changes and hot-reloads only the matching Oxide plugins. It does not reload game code mods, DLLs, XUi files, configs, data files, or the game itself.

It also provides manual reload commands, a small public API, optional Server Assist panel actions, per-plugin row icons, and hooks that other plugins can listen to.

---

## Overview

Oxide ships a built-in C# plugin watcher, but it has no filtering, no per-server ignore list, no reload history, no manual command surface, and no API for other plugins. PluginHotReload adds:

- A debounced watcher for `.cs` files under `oxide/plugins`.
- Optional subdirectory watching.
- Include-file handling through Oxide's own dependency reload path.
- Per-file ignore and include-only filters with `*` and `?` wildcards.
- Reload rate limiting and a timeout warning when a compile never completes.
- SHA-256 content-change tracking, so touching a file without changing it is ignored.
- Manual `/hotreload` commands.
- A public API and hooks for Server Assist or other plugins.
- Per-plugin Server Assist row icons from the ServerAssistArt icon pack.

By default the plugin takes over Oxide's built-in `FileSystemWatcher` at runtime, so a single file save does not produce two reloads. The built-in watcher is restored when this plugin unloads or if this plugin's own watcher fails to start. Nothing in `oxide.config.json` is edited permanently.

!!! note
    PluginHotReload is a C# plugin hot-reloader only. Changing a game mod DLL still requires a server restart.

---

## What it reloads

A file is watched only when all of these are true:

- It is under `oxide/plugins`, or under a subfolder when `WatchSubdirectories` is on.
- Its extension is exactly `.cs` (case-insensitive).
- It is not inside a hidden folder, `obj`, or `bin`.
- It is not a hidden file or an editor temp file such as `Foo.cs~` or `~Foo.cs`.
- It is not matched by `Ignore`.
- It is matched by `IncludeOnly` when `IncludeOnly` is not empty.

It never touches:

- Mod DLLs or `ModInfo.xml` files.
- `oxide/config` or `oxide/data`.
- XUi XML files.
- The game's own assemblies.
- Anything outside `oxide/plugins`.

Files under `oxide/plugins/include` are passed to `Interface.Oxide.ReloadPlugin` with their include path, so Oxide reloads every plugin that references the changed include. PluginHotReload does not maintain its own dependency graph.

---

## Requirements

| Requirement | Detail |
|---|---|
| Game server | 7 Days to Die with Oxide/uMod installed |
| EAC | Must be off, because this is an Oxide plugin |
| Plugin file | `oxide/plugins/PluginHotReload.cs` |
| Client install | None; the plugin is server-side only |
| Optional | Server Assist for the panel; ServerAssistArt for the card and row icons |

No client DLL and no XUi change are required. Oxide compiles the `.cs` file on the server at startup.

---

## Installation

1. Stop the server.
2. Copy `PluginHotReload.cs` into the server's `oxide/plugins` folder.
3. Start the server. Oxide compiles it and creates `oxide/config/PluginHotReload.json`.
4. Join the server or use the server console and run `/hotreload status`.

You can also copy the whole `PluginHotReload` folder from the package and move its `oxide/plugins/PluginHotReload.cs` into the server's `oxide/plugins` folder. That is the whole install.

If Server Assist is installed, the plugin registers a **Plugin Hot Reload** panel automatically. The optional card and row icons come from the ServerAssistArt UIAtlases pack on the server and every client. Without that art pack the panel still works and falls back to the vanilla hammer sprite.

---

## Commands

Aliases: `/hotreload`, `/phr`, `/plreload`.

| Command | Permission | Purpose |
|---|---|---|
| `/hotreload` | `pluginhotreload.use` | Show status; same as `/hotreload status` |
| `/hotreload status` | `pluginhotreload.use` | Show watcher state, tracked files, pending changes, debounce, last reload, and session reload count |
| `/hotreload list [filter]` | `pluginhotreload.use` | List `.cs` plugin files with loaded/not-loaded state; optional case-insensitive substring filter; up to 20 lines |
| `/hotreload reload <plugin>` | `pluginhotreload.admin` | Reload one plugin by file name, relative path, or loaded plugin name/title |
| `/hotreload reloadall confirm` | `pluginhotreload.admin` | Reload every loaded watched `.cs` plugin; the `confirm` argument is required |
| `/hotreload scan` | `pluginhotreload.admin` | Load `.cs` plugin files that are not currently loaded; existing plugins are not unloaded |
| `/hotreload enable` | `pluginhotreload.admin` | Turn automatic watching on and save the config |
| `/hotreload disable` | `pluginhotreload.admin` | Turn automatic watching off; manual commands still work |
| `/hotreload ignore [name]` | `pluginhotreload.admin` | Add a pattern to `Ignore`, or print the current list when no name is given |
| `/hotreload unignore <name>` | `pluginhotreload.admin` | Remove a pattern from `Ignore` |
| `/hotreload reloadconfig` | `pluginhotreload.admin` | Re-read `oxide/config/PluginHotReload.json` and restart the watcher |
| `/hotreload help` | `pluginhotreload.use` | Show the command list |

`/hotreload reload` accepts a file name with or without `.cs`, a relative path such as `subfolder/PluginName`, or the loaded plugin's `[Info]` name or title. `/hotreload reloadall` reloads this plugin last so the command can finish cleanly.

The `Ignore` and `IncludeOnly` patterns support `*` and `?` and match either the full relative path or the file name.

---

## Permissions

| Permission | Grants |
|---|---|
| `pluginhotreload.use` | `status`, `list`, and `help` |
| `pluginhotreload.admin` | `reload`, `reloadall`, `scan`, `enable`, `disable`, `ignore`, `unignore`, and `reloadconfig` |

The server console is always allowed. Both permissions are granted to the default administrators group on startup when `GrantAdminPermission` is true.

---

## Configuration

File: `oxide/config/PluginHotReload.json`

| Key | Default | Notes |
|---|---|---|
| `ChatPrefix` | `"[HotReload]"` | Prefix for command replies; use `""` for no prefix |
| `Enabled` | `true` | Master switch for automatic file watching; manual commands still work when false |
| `TakeOverCoreWatcher` | `true` | Disable Oxide's underlying watcher at runtime and let this plugin watch instead |
| `WatchSubdirectories` | `true` | Also watch `.cs` files in subfolders of `oxide/plugins` |
| `DebounceSeconds` | `1.5` | How long a file must stay quiet before reload; clamped to 0.2–10.0 |
| `ReloadOnCreate` | `true` | Load a newly created `.cs` file automatically |
| `UnloadOnDelete` | `false` | Unload a plugin when its `.cs` file is deleted |
| `ReloadSelf` | `true` | Allow `PluginHotReload.cs` itself to be reloaded |
| `AllowManualIncludeReload` | `false` | Allow explicit single-file reloads of files under `oxide/plugins/include` |
| `LogReloads` | `true` | Log when a reload starts, succeeds, or times out |
| `GrantAdminPermission` | `true` | Grant both permissions to the default administrators group |
| `MaxReloadsPerMinute` | `60` | Safety limit for reload requests in a rolling minute; clamped to 1–600 |
| `ReloadTimeoutSeconds` | `90` | Warn and fire `OnPluginHotReloadFailed` when a reload does not complete; clamped to 10–600 |
| `Ignore` | `[]` | File patterns that are never watched or reloaded |
| `IncludeOnly` | `[]` | When not empty, only matching files are watched or reloaded |

Example patterns:

```json
{
  "Ignore": ["ServerAssist", "subfolder\\Plugin", "*Test*"],
  "IncludeOnly": []
}
```

Delete the config file and run `/hotreload reloadconfig` to regenerate the defaults.

### Watcher takeover

Oxide's CSharp extension normally registers its own `FileSystemWatcher`. With `TakeOverCoreWatcher` true, PluginHotReload finds that watcher through the public `CSharpExtension.Watcher` property and disables its `EnableRaisingEvents` flag. That is a runtime-only change. It is re-enabled when this plugin unloads or if this plugin's own watcher fails to start.

`/hotreload disable` intentionally does not re-enable Oxide's watcher, so disabling automatic reload really stops all automatic reloading. With `TakeOverCoreWatcher` false, this plugin provides manual commands and its API only and does not start a second watcher.

!!! warning
    If the takeover fails, the plugin refuses to start a second watcher because that would reload every changed file twice. Either update the plugin and set `TakeOverCoreWatcher` to true again, or set `"PluginWatchers"` to false in `oxide/oxide.config.json`, restart the server, and let PluginHotReload own the watcher.

---

## Data and files

| Path | Purpose |
|---|---|
| `oxide/plugins/PluginHotReload.cs` | The entire plugin install |
| `oxide/config/PluginHotReload.json` | Configuration, created on first load |

PluginHotReload does not create an `oxide/data` file. Reload history and content hashes are kept in memory for the current session. `oxide.config.json` is not modified on disk.

---

## Server Assist integration

When Server Assist is loaded, PluginHotReload registers a panel:

| Item | Value |
|---|---|
| Panel id | `pluginhotreload` |
| Label | Plugin Hot Reload |
| Permission | `pluginhotreload.use` |
| Actions permission | `pluginhotreload.admin` |
| Icon | `sprite:sa_plugin_hot_reload` |
| Handler | `HandleServerAssistRequest` |
| Entry | `/hotreload` |

The panel shows the watcher status and every plugin file that is present, and gives admins Reload, Reload All, Scan, Enable Watch and Disable Watch actions. Players need `serverassist.use` plus `pluginhotreload.use`; reload actions need `pluginhotreload.admin`.

Single-plugin rows use a dedicated `reloadone` action and only ever reload the selected file. They cannot fall through to Reload All. The bulk Reload All button is kept last and requires a second click to confirm. Include files are excluded from the panel, and manual include reloads are refused unless `AllowManualIncludeReload` is true.

The chat commands and API work without Server Assist.

### Row icons

Known plugin files use matching ServerAssistArt sprites. The client needs the art pack (`AAZ_ServerAssistArt` or the ServerAssistArt UIAtlases folder) for the sprites to resolve.

| Plugin row | Icon |
|---|---|
| Panel card / generic plugin | `sprite:sa_plugin_hot_reload` |
| AdminKits | `sprite:sa_admin_kits` |
| AdminShop | `sprite:sa_admin_shop` |
| AdminSuite | `sprite:sa_admin_suite` |
| AuctionHouse | `sprite:sa_auction_house` |
| DailyRewards | `sprite:sa_daily_rewards` |
| DynamicBackpack | `sprite:sa_dynamic_backpack` |
| PermissionGui | `sprite:sa_permission_gui` |
| PluginHotReload | `sprite:sa_plugin_hot_reload` |
| VoteSkipNight | `sprite:sa_vote_skip_night` |
| QLNPack / QLNPackServer / QLNPackBootstrap | `sprite:sa_qln_pack` |
| ServerAssist | `sprite:sa_server_assist` |
| Empty / status row | `sprite:sa_nav_server` |

Server Assist fills the panel background and banner from the panel icon automatically. The current Server Assist action buttons are text-only, so this plugin does not put art on them.

---

## Public API and hooks

Another Oxide plugin can find the plugin and call its methods:

```csharp
private Plugin hotReload;

private void OnServerInitialized()
{
    hotReload = plugins.Find("PluginHotReload");
}

private void ReloadOne(string pluginFile)
{
    if (hotReload != null)
    {
        hotReload.Call("ReloadPlugin", pluginFile);
    }
}
```

| Method | Returns | Purpose |
|---|---|---|
| `ReloadPlugin(string name)` | `bool` | Reload one plugin by file name, relative path, or loaded plugin name; returns false when nothing matched |
| `ReloadAll()` | `int` | Reload every loaded plugin, skipping include files; returns the number of reload requests |
| `GetPluginFiles()` | `string[]` | Relative names of every `.cs` plugin file in `oxide/plugins` |
| `GetStatus()` | `string` | Compact status string for a GUI or console line |
| `IsWatching()` | `bool` | True when automatic watching is active |

These methods carry Oxide's `[HookMethod]` attribute, so another plugin can call them with `Plugin.Call("ReloadPlugin", name)`, `Plugin.Call<int>("ReloadAll")`, and so on without a compile-time reference.

Hooks fired by this plugin:

| Hook | Arguments |
|---|---|
| `OnPluginHotReloadRequested` | `string pluginFile, string reason` |
| `OnPluginHotReloaded` | `string pluginFile, bool success` |
| `OnPluginHotReloadFailed` | `string pluginFile, string error` |
| `OnPluginHotReloadUnloaded` | `string pluginFile` |

Example listener:

```csharp
private void OnPluginHotReloaded(string pluginFile, bool success)
{
    if (success &&
        string.Equals(pluginFile, "ServerAssist", StringComparison.OrdinalIgnoreCase))
    {
        // Re-register anything that ServerAssist owns, or refresh a GUI.
    }
}
```

Use `/hotreload scan` to load new, unloaded plugin files. `ReloadAll` is deliberately behind the `confirm` argument in chat.

---

## Typical workflows

### First install

1. Copy `PluginHotReload.cs` to `oxide/plugins`.
2. Start the server and confirm `oxide/config/PluginHotReload.json` exists.
3. Run `/hotreload status`.
4. Confirm `Auto-watch: on` and that the tracked `.cs` count matches the server.

### Edit and reload a single plugin

1. Save the changed `.cs` file.
2. Wait for the debounce (default 1.5 seconds).
3. Check the server log for the reload line, or run `/hotreload status`.
4. Use `/hotreload reload <plugin>` when you do not want to wait for the watcher.

### Load new plugin files

1. Copy the new `.cs` file into `oxide/plugins`.
2. With `ReloadOnCreate` true, the watcher loads it automatically.
3. If the file was added while watching was off, run `/hotreload scan`.

### Silence a noisy file

```text
/hotreload ignore *Test*
/hotreload ignore subfolder\Plugin
```

`/hotreload ignore` with no argument prints the current list. `/hotreload reloadconfig` re-reads the JSON if you prefer to edit it by hand.

### Reload from the Server Assist board

1. Open Server Assist and select **Plugin Hot Reload**.
2. Use a single row's Reload action for one plugin.
3. Use **Reload All** only when you intend to reload every loaded watched plugin, then confirm the second click.

---

## Troubleshooting

### Automatic reload is not happening

1. Run `/hotreload status`.
2. If `Auto-watch` is off, run `/hotreload enable` and check the server log.
3. If the status says the built-in watcher is on, set `TakeOverCoreWatcher` to true (the default), or set `PluginWatchers` to false in `oxide/oxide.config.json` and restart.
4. Check that the file is a `.cs` file under `oxide/plugins`, is not in `Ignore`, and is not blocked by `IncludeOnly`.
5. Check the server log for a compile error. A plugin that does not compile cannot be reloaded.

!!! note
    A failed compile is handled by Oxide. The old plugin normally keeps running when the loader can compile before unloading, and the timeout warning is logged.

### Reload did not complete within N seconds

Oxide waited for a compile or load and got neither. Check the server log for the compiler line number, fix the source, and save again. The old plugin normally keeps running.

### Reloading one plugin still reloads several plugins

The target is probably a dependency. Oxide's C# loader reloads every plugin that declares `// Requires` for the target, and every plugin that references a changed include. PluginHotReload does not request Reload All from a single row. Check the log for `Reloading X because it references updated include file` and check which plugins use `// Requires` on the target.

### An icon shows as a hammer instead of Server Assist art

Install the ServerAssistArt UIAtlases (or `AAZ_ServerAssistArt`) on the server and on every client. Sprite names have no `.png` extension. The panel uses `sprite:sa_plugin_hot_reload` and known plugin rows use the matching `sa_*` card icon.

### The file watcher error appears in the log

The OS notification buffer overflowed, usually because a tool wrote hundreds of files at once. The plugin restarts the watcher after two seconds.

### A changed DLL or game mod did nothing

That is intentional. This plugin only reloads `.cs` Oxide plugins. Mod DLLs are locked by the running game and need a server restart.

### `TakeOverCoreWatcher could not disable the built-in watcher`

The reflection path did not match this Oxide build. Set `"PluginWatchers"` to false in `oxide/oxide.config.json`, restart the server, and let PluginHotReload watch instead.

### Permission denied

Grant `pluginhotreload.use` for status, list, and help, and `pluginhotreload.admin` for reload operations. The default administrators group receives both when `GrantAdminPermission` is true.

### Server Assist data is not what I edited

Server Assist saves its own data. Do not edit `oxide/data/ServerAssist/data.json` while the server is running. Reloading `ServerAssist.cs` runs its normal `Init`/`OnServerInitialized` path and reads its config and data again.

---

## Related pages

- [Server Assist API](../../integration/api/index.md)
- [Companion plugin integration](../../integration/companions/index.md)
- [Server Assist product guide](../../core/server-assist.md)
- [Icons](../../core/icons.md)
- [Troubleshooting](../../reference/troubleshooting/index.md)
