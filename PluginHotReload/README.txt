PluginHotReload 1.0.1
=====================

A server-side Oxide plugin for 7 Days to Die. It watches oxide/plugins for C#
source changes and hot-reloads only those Oxide plugins. It never reloads game
code mods, DLLs, XUi files, configs, data, or the game itself.

It also provides manual reload commands, a small public API, and an optional
panel inside ServerAssist so admins can reload plugins from the board.


WHY THIS EXISTS
---------------

Oxide ships a built-in C# plugin watcher, but it only knows about files it has
already mapped and it has no filtering, no per-server ignore list, no reload
history, no manual command surface, and no API for other plugins.

PluginHotReload adds:

  * A debounced watcher for .cs files under oxide/plugins.
  * Optional subdirectory watching.
  * Include-file handling through Oxide's own dependency reload path.
  * Per-file ignore and include-only filters with * and ? wildcards.
  * Reload rate limiting and a timeout warning when a compile never completes.
  * Content-change tracking, so touching a file without changing it is ignored.
  * Manual /hotreload commands.
  * A public API and hooks for ServerAssist or other plugins.
  * Per-plugin ServerAssist row icons from the ServerAssistArt icon pack.

By default the plugin takes over Oxide's built-in FileSystemWatcher at runtime
so you do not get two reloads for one file save. The built-in watcher is
restored when this plugin unloads or if this plugin's own watcher fails to
start. Nothing in oxide.config.json is edited permanently.


FILES
-----

  oxide/plugins/PluginHotReload.cs

That is the whole install. There is no client DLL and no XUi change. Oxide
compiles the .cs file on the server at startup. If ServerAssist is installed,
the plugin registers a "Plugin Hot Reload" panel automatically; no extra files
are needed. The optional card and row icons come from the ServerAssistArt
UIAtlases pack on the server and every client. Without that art pack the panel
still works and falls back to the vanilla hammer sprite.


INSTALL
-------

1. Stop the server.
2. Copy PluginHotReload.cs into the server's oxide/plugins folder.
3. Start the server. Oxide compiles it and creates:
     oxide/config/PluginHotReload.json
4. Join the server or use the server console and run:
     /hotreload status

You can also copy the whole PluginHotReload folder from this package and move
its oxide/plugins/PluginHotReload.cs into the server's oxide/plugins folder.

EAC must be off because this is an Oxide plugin. The plugin is server-side
only; clients do not need it.


WHAT IT RELOADS
---------------

Only files that match all of these:

  * Located under oxide/plugins (or a subfolder when WatchSubdirectories is on).
  * Extension is exactly .cs (case-insensitive).
  * Not inside a hidden folder, obj, or bin.
  * Not a hidden file or an editor temp file such as Foo.cs~ or ~Foo.cs.
  * Not matched by the Ignore list.
  * Matched by IncludeOnly when IncludeOnly is not empty.

It never touches:

  * Mod DLLs and ModInfo.xml files.
  * oxide/config or oxide/data.
  * XUi XML files.
  * The game's own assemblies.
  * Anything outside oxide/plugins.

Changing a game mod DLL still requires a server restart. This plugin is not a
DLL hot-reloader.


COMMANDS
--------

Aliases: /hotreload, /phr, /plreload

  /hotreload
  /hotreload status
      Show whether the watcher is on, what the built-in watcher is doing, how
      many .cs files are tracked, how many changes are pending, the debounce,
      the last reload, and the session reload count.

  /hotreload list [filter]
      List .cs plugin files with loaded/not-loaded state. The optional filter
      is a case-insensitive substring. Up to 20 lines are shown.

  /hotreload reload <plugin>
      Reload one plugin. <plugin> can be:
        - the file name, with or without .cs
        - a relative path such as subfolder/PluginName
        - the plugin's [Info] name or title when it is currently loaded

  /hotreload reloadall confirm
      Reload every loaded .cs plugin that is watched. The "confirm" argument
      is required. This plugin reloads itself last so the command can finish
      cleanly. Use /hotreload scan to load new, unloaded plugin files.

  /hotreload scan
      Load any .cs plugin files that are not currently loaded. Existing
      plugins are not unloaded.

  /hotreload enable
      Turn automatic watching on and save the config.

  /hotreload disable
      Turn automatic watching off. When TakeOverCoreWatcher is true, Oxide's
      built-in watcher stays disabled too, so nothing auto-reloads until you
      enable watching again, reload this plugin, or unload it. Manual reload
      commands still work. With TakeOverCoreWatcher false, Oxide's watcher is
      left alone.

  /hotreload ignore [name]
  /hotreload unignore <name>
      Change the Ignore list. With no name, ignore prints the current list.
      Patterns support * and ? wildcards and match either the full relative
      path or the file name.

  /hotreload reloadconfig
      Re-read oxide/config/PluginHotReload.json and restart the watcher.

  /hotreload help
      Show the command list.


PERMISSIONS
-----------

pluginhotreload.use
  status, list, and help. Granted to the default administrators group on
  startup when GrantAdminPermission is true.

pluginhotreload.admin
  reload, reloadall, scan, enable, disable, ignore, unignore, and
  reloadconfig. Granted to the default administrators group on startup when
  GrantAdminPermission is true.

The server console is always allowed.


CONFIGURATION
-------------

Oxide creates oxide/config/PluginHotReload.json. Delete it and run
/hotreload reloadconfig to regenerate the defaults.

  ChatPrefix                 "[HotReload]"
      Prefix for command replies. Use "" for no prefix.

  Enabled                    true
      Master switch for automatic file watching. When false and
      TakeOverCoreWatcher is true, the built-in watcher stays disabled too, so
      nothing auto-reloads until you enable watching again or unload this
      plugin. With TakeOverCoreWatcher false, Oxide's watcher is left alone.
      Manual commands still work.

  TakeOverCoreWatcher        true
      When Oxide's built-in plugin watcher is active, disable its underlying
      FileSystemWatcher at runtime and let this plugin watch instead. The
      built-in watcher is restored when this plugin unloads or if this
      plugin's own watcher fails to start. It stays disabled across plugin
      reloads while Enabled is false. Set this to false if you prefer Oxide's
      watcher; this plugin will then provide manual commands and its API only,
      and will not start a second watcher.

  WatchSubdirectories        true
      Watch .cs files in subfolders of oxide/plugins too.

  DebounceSeconds            1.5
      How long a file must stay quiet before it is reloaded. Clamped to
      0.2-10.0. Editors that write a file in several steps need this.

  ReloadOnCreate             true
      Load a newly created .cs file automatically.

  UnloadOnDelete             false
      Unload a plugin when its .cs file is deleted. Off by default because a
      delete can be accidental; turn it on if you delete plugins while the
      server runs.

  ReloadSelf                 true
      Allow PluginHotReload.cs itself to be reloaded. The new instance starts
      the watcher again.

  AllowManualIncludeReload   false
      Allow /hotreload reload and the ServerAssist panel to manually reload a
      file under oxide/plugins/include. Off by default because reloading an
      include makes Oxide reload every plugin that references it. Automatic
      include changes are still handled by the watcher; this option only
      controls the explicit single-file actions.

  LogReloads                 true
      Write a log line when a reload starts, succeeds, or times out.

  GrantAdminPermission       true
      Grant pluginhotreload.use and pluginhotreload.admin to the default
      administrators group. Set false if you manage permissions yourself.

  MaxReloadsPerMinute        60
      Safety limit for reload requests in a rolling minute. Clamped to 1-600.

  ReloadTimeoutSeconds       90
      If a requested reload has not produced a successful plugin load within
      this time, log a warning and fire OnPluginHotReloadFailed. This usually
      means a compile error. Clamped to 10-600.

  Ignore                     []
      File patterns that are never watched or reloaded. Examples:
        "ServerAssist"           ignore that file name
        "subfolder\\Plugin"     ignore one relative path
        "*Test*"                ignore any file containing Test

  IncludeOnly                []
      When not empty, only matching files are watched or reloaded. Uses the
      same wildcard rules as Ignore.


HOW DOUBLE RELOADS ARE AVOIDED
------------------------------

Oxide's CSharp extension normally registers its own FileSystemWatcher. With
TakeOverCoreWatcher true, PluginHotReload finds that watcher through the public
CSharpExtension.Watcher property and disables its EnableRaisingEvents flag.
That is a runtime-only change. It is re-enabled when this plugin is unloaded
or if this plugin's own watcher fails to start. It is intentionally not
re-enabled by /hotreload disable, so disabling automatic reload really stops
all automatic reloading. If Enabled is false, a reloaded instance disables the
built-in watcher again instead of leaving it on.

If the takeover fails (for example because a future Oxide build renames the
private field), the plugin refuses to start a second watcher, because that
would reload every changed file twice. In that case either:

  1. Set TakeOverCoreWatcher to true again after updating the plugin, or
  2. Set "PluginWatchers" to false in oxide/oxide.config.json and restart, then
     let PluginHotReload own the watcher.

Oxide's built-in watcher and PluginHotReload both use Oxide's own
Interface.Oxide.ReloadPlugin path. Neither one recompiles the game or touches
mod DLLs.


INCLUDE FILES
-------------

Files under oxide/plugins/include are passed to Interface.Oxide.ReloadPlugin
with their include path. Oxide's CSharp plugin loader then reloads every plugin
that references the changed include. PluginHotReload does not need its own
dependency graph.

Automatic include changes still work that way. Manual include reloads are
refused by default because a single click would reload every dependent plugin.
Set AllowManualIncludeReload to true only if that is what you want. Include
files are not shown as plugin rows in the ServerAssist panel.


SERVER ASSIST PANEL AND API
---------------------------

When ServerAssist is loaded, PluginHotReload registers a "Plugin Hot Reload"
panel with the icon sprite:sa_plugin_hot_reload. The panel shows the watcher
status and every plugin file that is present, and gives admins Reload, Reload
All, Scan, Enable Watch and Disable Watch actions. Players need serverassist.use
plus pluginhotreload.use; reload actions need pluginhotreload.admin. The panel
is optional and the chat commands and API work without ServerAssist.

Single-plugin rows use a dedicated reloadone action and only ever reload the
selected file. They cannot fall through to Reload All. The bulk Reload ALL
button is kept last and requires a second click to confirm. Include files are
excluded from the panel, and manual include reloads are refused unless
AllowManualIncludeReload is true, because one include reload would reload every
dependent plugin.

Server Assist icon map
----------------------

These are the best matches from the ServerAssistArt pack. The client needs the
art pack (AAZ_ServerAssistArt or the ServerAssistArt UIAtlases folder) for the
sprites to resolve.

  Panel card / generic plugin   sprite:sa_plugin_hot_reload
  AdminKits                     sprite:sa_admin_kits
  AdminShop                     sprite:sa_admin_shop
  AdminSuite                    sprite:sa_admin_suite
  AuctionHouse                  sprite:sa_auction_house
  DailyRewards                  sprite:sa_daily_rewards
  DynamicBackpack               sprite:sa_dynamic_backpack
  PermissionGui                 sprite:sa_permission_gui
  PluginHotReload               sprite:sa_plugin_hot_reload
  VoteSkipNight                 sprite:sa_vote_skip_night
  QLNPack / QLNPackServer       sprite:sa_qln_pack
  QLNPackBootstrap              sprite:sa_qln_pack
  ServerAssist                  sprite:sa_server_assist
  Empty / status row            sprite:sa_nav_server

The current ServerAssist action buttons are text-only, so this plugin does not
try to put art on them. The sa_act_* and sa_nav_* icons are still useful on
shared ServerAssist buttons or on future icon fields; this panel uses them for
the empty/status row only.

Server Assist fills the panel background and banner from the panel icon
automatically, so the chip-and-refresh art is used behind the card grid and in
the header without extra fields.

The public API is also available to any other Oxide plugin. From another
plugin:

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

Public API:

  bool ReloadPlugin(string name)
      Reload one plugin. Accepts a file name, relative path, or loaded plugin
      name. Returns false when nothing matched.

  int ReloadAll()
      Reload every loaded plugin (include files are skipped). Returns the
      number of reload requests. Use /hotreload scan to load new files.

  string[] GetPluginFiles()
      Relative names of every .cs plugin file in oxide/plugins.

  string GetStatus()
      Compact status string for a GUI or console line.

  bool IsWatching()
      True when automatic watching is active.

These methods carry Oxide's [HookMethod] attribute, so another plugin can call
them with Plugin.Call("ReloadPlugin", name), Plugin.Call<int>("ReloadAll"),
and so on, without a compile-time reference to this plugin.

Hooks this plugin fires:

  OnPluginHotReloadRequested(string pluginFile, string reason)
  OnPluginHotReloaded(string pluginFile, bool success)
  OnPluginHotReloadFailed(string pluginFile, string error)
  OnPluginHotReloadUnloaded(string pluginFile)

Example listener in ServerAssist or another plugin:

    private void OnPluginHotReloaded(string pluginFile, bool success)
    {
        if (success &&
            string.Equals(pluginFile, "ServerAssist", StringComparison.OrdinalIgnoreCase))
        {
            // Re-register anything that ServerAssist owns, or refresh a GUI.
        }
    }

Optional ServerAssist command
----------------------------

If you later want a /sa shortcut, the smallest change in ServerAssist.cs is to
add a case in CommandServerAssist. This is not applied by this package; it is
shown so the hook is obvious:

    case "reloadplugins":
        if (!RequireAdmin(player))
        {
            return;
        }

        Plugin hotReload = plugins.Find("PluginHotReload");
        int count = hotReload == null ? 0 : hotReload.Call<int>("ReloadAll");
        Reply(player, "Reload requested for " + count + " plugin(s).");
        return;

Why this is better than a client-side command button: ServerAssist command
buttons run in the local client console. A server-side plugin reload must go
through the server plugin manager, which is what the API above does.


SAFETY NOTES
------------

  * The plugin never reloads a file outside oxide/plugins.
  * The plugin never reloads a DLL or a game mod.
  * Reloads are debounced, so a single save does not produce a burst.
  * A SHA-256 content check ignores file-touch events where the source text did
    not change, so editors or build tools that touch the whole folder do not
    reload every plugin.
  * The ServerAssist per-plugin rows use reloadone and can only target one file.
    Include files are not listed as rows, and manual include reloads are refused
    unless AllowManualIncludeReload is true.
  * Failed compiles are handled by Oxide: the old plugin stays loaded when the
    loader can compile before unloading, and the timeout warning is logged.
  * ReloadAll is deliberately behind a "confirm" argument.
  * Oxide itself may reload plugins that explicitly declare a // Requires
    dependency on the target. That is dependency behaviour in Oxide's C# loader,
    not a Reload All request from this plugin.
  * The built-in watcher is restored when this plugin unloads or if its own
    watcher fails to start, so you are never left without a watcher after a
    normal unload.
  * This plugin is server-side only. It does not send anything to clients.


TROUBLESHOOTING
---------------

"Automatic reload is not happening"

  1. Run /hotreload status.
  2. If "Auto-watch" is off, run /hotreload enable and check the server log.
  3. If the status says the built-in watcher is on, set TakeOverCoreWatcher to
     true (default) or set PluginWatchers to false in oxide/oxide.config.json
     and restart.
  4. Check that the file is a .cs file under oxide/plugins and is not in the
     Ignore list or blocked by IncludeOnly.
  5. Check the server log for a compile error. A plugin with a compile error
     cannot be reloaded until it compiles.

"Reload did not complete within N seconds"

  Oxide waited for a compile or load and got neither. Check the server log for
  the compiler line number. Fix the source and save again. The old plugin
  normally keeps running.

"Reloading one plugin still reloads several plugins"

  The target is probably a dependency. Oxide's C# loader reloads every plugin
  that declares // Requires for the target, and every plugin that references a
  changed include. PluginHotReload does not request Reload All from a single
  row. Include files are excluded from the panel and manual include reloads are
  refused by default. If the target is a normal plugin, check the log for
  "Reloading X because it references updated include file" and check which
  plugins use // Requires on it.

"An icon shows as a hammer instead of the Server Assist art"

  Install the ServerAssistArt UIAtlases (or AAZ_ServerAssistArt) on the server
  and on every client. The sprite names have no .png extension. The plugin uses
  sprite:sa_plugin_hot_reload for the panel and the matching sa_* card icon for
  known plugins.

"The file watcher error appears in the log"

  The OS notification buffer overflowed, usually because a tool wrote hundreds
  of files at once. The plugin restarts the watcher after two seconds.

"I changed a DLL or a game mod and nothing happened"

  That is intentional. This plugin only reloads .cs Oxide plugins. Mod DLLs
  are locked by the running game and need a server restart.

"TakeOverCoreWatcher could not disable the built-in watcher"

  The reflection path did not match this Oxide build. Set "PluginWatchers" to
  false in oxide/oxide.config.json, restart the server, and let
  PluginHotReload watch instead.

"Permission denied"

  Grant pluginhotreload.use for status/list/help, and
  pluginhotreload.admin for reload operations. The default administrators
  group receives both when GrantAdminPermission is true.

"ServerAssist's data is not what I edited"

  ServerAssist saves its own data. Do not edit oxide/data/ServerAssist/data.json
  while the server is running. Reloading ServerAssist.cs runs its normal
  Init/OnServerInitialized path and reads its config and data again.



