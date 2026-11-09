AdminSuite 1.0.4
================

AdminSuite is a server-side Oxide plugin that turns ServerAssist into a complete
staff toolkit for 7 Days to Die. It is modelled after the "one menu, many tools"
feel of CMI: players, moderation, teleports, inventory management, world control,
audit logs, snapshots, rollback, server information and more, all inside the same
ServerAssist window.

AdminSuite does not ship a client DLL and does not add XUi XML. It registers a GUI
hook with ServerAssist, so the existing ServerAssist client mod renders it. The
server enforces every permission and every action; the client is only a view.

IMPORTANT: AdminSuite needs BOTH of these permission nodes for staff:

    serverassist.use      (to open ServerAssist)
    adminsuite.use       (to open AdminSuite inside ServerAssist)

ServerAssist normally grants serverassist.use to the default player group. If you
disabled that, grant it to your staff group manually. adminsuite.use is not
granted to players by default; grant it to moderators/admins.


REQUIREMENTS
------------

Server:
  ServerAssist/Mods folder (ServerAssistMod.dll, ModInfo.xml and Config)
  oxide/plugins/ServerAssist.cs        version 2.0.2 or newer
  oxide/plugins/AdminSuite.cs         this plugin

Every client that should see the window:
  ServerAssist/Mods folder

AdminSuite itself is server-only. Clients do not install an AdminSuite mod.
EAC must be off, and the server and clients must use the same ServerAssist files.


INSTALL
-------

1. Stop the server and the game.
2. Make sure ServerAssist is installed on the server and on every client.
3. Copy oxide/plugins/AdminSuite.cs into the server's oxide/plugins folder.
   Oxide compiles it on startup. Do not put the .cs file in the game Mods
   folder; the mod loader does not compile .cs files.
4. Start the server.
5. Join with a client, press F7/F6 or type /as.
6. If you are not an Oxide administrator, grant the permissions below.
7. Open the "[GUI] Admin Suite" row or use /as.

On first load the plugin creates:

  oxide/config/AdminSuite.json
  oxide/data/AdminSuite/data.json
  oxide/data/AdminSuite/snapshots.json
  oxide/logs/AdminSuite-YYYY-MM-DD.log

The plugin registers adminsuite.use and adminsuite.admin with Oxide. The
administrators group receives adminsuite.admin and every granular permission
automatically. The players group receives adminsuite.use only if
GrantUseToDefaultGroup is true (default false).


FIRST-TIME PERMISSIONS
----------------------

Grant the main permission to a staff group:

  oxide.grant group moderators adminsuite.use
  oxide.grant group moderators serverassist.use
  oxide.grant group moderators adminsuite.players.view
  oxide.grant group moderators adminsuite.players.teleport
  oxide.grant group moderators adminsuite.players.bring
  oxide.grant group moderators adminsuite.players.kick
  oxide.grant group moderators adminsuite.players.ban
  oxide.grant group moderators adminsuite.players.mute
  oxide.grant group moderators adminsuite.logs

Administrators in Oxide already receive adminsuite.admin, which bypasses every
granular check. If PermissionGui is installed, all of these nodes appear in its
permission browser and can be granted there.


OPENING THE MENU
----------------

  /as
  /adminsuite
  F7 or F6 (ServerAssist's keys)
  The "[GUI] Admin Suite" row on the ServerAssist home screen

Only players with adminsuite.use (or Oxide administrators) see the hook row.


WHAT IS IN THE MENU
-------------------

HOME
  The main categories: Players, Moderation, Teleport, Items, World, Logs,
  Snapshots, Server, Warps and Help.

PLAYERS
  A paginated online player list with ping, health, distance and staff markers.
  Select a player to open their profile: identity, connection, position, biome,
  vitals, game stage, kills, inventory summary, moderation state, staff note,
  snapshot count and flags.

PLAYER ACTIONS
  Teleport To, Bring, Spawn On Top, Spectate, Heal, Feed, God Mode, Buffs and a
  More menu. The More menu contains Kick, Ban, Mute, Timeout, Freeze/Unfreeze,
  Kill, Inventory, Give Item and Staff Tools.

STAFF TOOLS
  Warn, Note, Snapshot, Rollback, Clear Inventory, Take All Items, Repair Items,
  Jail (if configured), Unmute and Unfreeze.

BUFFS
  Apply or remove a buff with the vanilla buffplayer/debuffplayer commands, set
  current health, set food and water, or review the player's vitals.

MODERATION
  Active mutes, active freezes, stored warnings, the server ban list, the server
  admin list and the whitelist. Includes Kick All, Clear Mutes, Clear Warnings,
  Heal All and Feed All.

TELEPORT
  Teleport to a player, bring a player, spawn on top of a player, teleport to
  coordinates, send the selected player to coordinates or to another player, set
  and use a personal home, and use saved warps. Also opens the vanilla player
  list and the POI teleporter on your client.

WARPS
  Saved staff teleport points. Warp names may use letters, numbers, underscore
  and dash. The list is shared by all staff.

ITEMS
  Give an item to the selected player or to yourself, search the live item
  catalog, spawn an entity, open the vanilla creative menu, inspect the selected
  player's inventory, clear it, take every item, or repair every item.

INVENTORY INSPECTOR
  The server-side inventory view has three sections: Toolbelt, Backpack and
  Equipment. Each item shows its slot, count, quality and durability. Click an
  item to Take it to your inventory, Repair it, or Delete it. Take All, Repair All
  and Clear All work on the whole player. This is a live server-side inspector;
  it does not use a remote drag-and-drop client window. Every destructive action
  snapshots the player first.

WORLD
  Set day, set night, set an exact time, clear weather, turn rain on, start a
  storm, send an airdrop, spawn a wandering horde, spawn scouts, save the world,
  reset the chunks around you, start a full region reset, open the debug menu on
  your client and set a game preference (admin only).

LOGS
  A live audit log of chat, joins, leaves, deaths, console commands and staff
  actions. Filter by chat, join/leave, deaths, admin actions or commands. Click
  an entry to see the full record. Logs are also written to oxide/logs. Clear
  removes the in-memory list; the file is kept for evidence.

SNAPSHOTS AND ROLLBACK
  Snapshots store the player's toolbelt, backpack, equipment, position, health,
  stamina, water and food. Capture one manually or let the plugin capture one
  automatically before destructive actions (kick, ban, clear, take all, delete
  item, rollback). Restore a snapshot from the snapshot list. Snapshots are
  trimmed by retention days and per-player limit.

SERVER
  Server name, world, game mode, difficulty, player count, world time, blood moon
  frequency, XP and loot rates, ban/whitelist state, uptime and managed memory.
  Buttons: Save, Reload, Announce, Restart Warning and Shutdown. Shutdown asks for
  confirmation and gives players a 5 second warning.

HELP
  An in-window quick reference for the main permissions and commands.


COMMANDS
--------

/as
/as open
  Open the AdminSuite window.

/as help
  Show the command list.

/as list
  List online players with ping and health. Requires adminsuite.players.view.

/as who <player>
  Show detailed information about a player. Requires adminsuite.players.view.

/as nearby [radius]
  List players near you. Requires adminsuite.players.view.

/as find <item>
  Search the live item catalog. Example: /as find ak47

/as tp <player | x y z | x z>
  Teleport yourself to a player or to coordinates.

/as bring <player>
  Teleport a player to you. Requires adminsuite.players.bring.

/as ontop <player>
/as spawnontop <player>
  Spawn on top of a player. Requires adminsuite.players.spawnontop.

/as spectate <player>
/as look <player>
  Teleport near a player and toggle spectator mode. Requires
  adminsuite.players.spectate.

/as inv <player>
/as inventory <player>
/as openinventory <player>
  Open the ServerAssist GUI directly on that player's inventory inspector.
  Requires adminsuite.players.inventory.

/as jail <player>
  Teleport the player to the configured jail and freeze them. Requires
  adminsuite.players.freeze and EnableJail = true.

/as kick <player> [reason]
  Kick a player. Requires adminsuite.players.kick.

/as kickall <reason> confirm
  Kick every connected player. Requires adminsuite.moderation.kickall.

/as ban <player> <duration> [reason] confirm
  Ban a player. Durations: 30m, 1h, 7d, 2w, 1mo, 1y, permanent.
  Example: /as ban Griefer 7d Cheating confirm
  Requires adminsuite.players.ban.

/as unban <player name or id>
  Remove a ban entry. Requires adminsuite.players.ban.

/as banlist
  List ban entries. Requires adminsuite.moderation.banlist or adminsuite.players.ban.

/as adminlist
  List the server admin file users and groups. Requires adminsuite.moderation.adminlist.

/as admin add <player> [level]
/as admin remove <player>
  Add or remove a server admin entry. Requires adminsuite.moderation.adminlist.

/as whitelist
  List whitelist users and groups. Requires adminsuite.moderation.whitelist.

/as whitelist add <player>
/as whitelist remove <player>
  Add or remove a whitelist entry. Requires adminsuite.moderation.whitelist.

/as clearmutes
/as clearwarnings
  Clear every active mute or every stored warning.

/as mute <player> [duration] [reason]
  Mute chat. Duration examples: 30m, 1h, 1d, permanent.

/as unmute <player>
  Remove a mute.

/as timeout <player> [duration] [reason]
  Mute and freeze a player for a finite duration. Requires adminsuite.players.mute and
  adminsuite.players.freeze.

/as freeze <player> [duration] [reason]
  Freeze a player at their current position. No duration or perm = indefinite.

/as unfreeze <player>
  Release a frozen player.

/as god <player>
  Toggle god mode for a player. Requires adminsuite.players.god.

/as fly [player]
  Toggle fly mode. In single-player/listen-server this toggles the local
  EntityPlayerLocal directly. On a dedicated server fly is a client-side debug
  toggle, so the command opens the debug menu on your client (or explains that
  another player must use their own debug menu). Requires
  adminsuite.players.fly.

/as heal <player>
/as feed <player>
/as kill <player>
  Heal, feed or kill a player.

/as healall
/as feedall
  Heal or feed every online player.

/as buff <player> <buff>
/as debuff <player> <buff>
  Apply or remove a buff with the vanilla buffplayer/debuffplayer commands.

/as give <player> <item> [amount] [quality]
  Give an item directly to a player's inventory. Item names may be internal
  names such as 9mmBullet, foodCanChili or medicalFirstAidBandage, or the
  in-game display name such as "9mm Bullet". Use /as find or the GUI item search
  if you are unsure. Examples:
    /as give Griefer 9mmBullet 100
    /as give Griefer 9mm Bullet 100 6
    /as give Griefer foodCanChili 10

  Items are split into valid stack sizes and added to the backpack first, then
  the toolbelt. If the inventory is full the reply says how many were actually
  given.

/as clear <player> confirm
  Clear every item from the player. A snapshot is taken first.

/as take <player> confirm
  Move every item from the player to your inventory. A snapshot is taken first.

/as repair <player>
  Reset every item's durability to full.

/as warn <player> <message>
  Store and send a warning.

/as note <player> [message]
  Set or clear a private staff note.

/as time <day|night|hour|day hour minute>
  Set world time. Examples: /as time day, /as time night, /as time 1 12 0.

/as weather <clear|rain|storm> [hours]
  Control weather. /as weather storm 1 starts a 1 hour storm in your biome.

/as airdrop
/as horde
/as scouts
  Spawn an airdrop, a wandering horde or scouts.

/as spawn <entity> [count]
  Spawn an entity near you. Example: /as spawn zombieBoe 3

/as save
  Force a world save.

/as say <message>
  Broadcast a message. Requires adminsuite.server.

/as shutdown confirm
  Shut down the server after a 5 second warning. Admin only.

/as warps
/as warp <name>
/as setwarp <name>
/as delwarp <name>
  Manage shared staff warps.

/as snapshot <player> [reason]
  Capture a snapshot. Requires adminsuite.players.snapshot.

/as snapshots [player]
  List snapshots. Requires adminsuite.snapshots, adminsuite.players.snapshot or
  adminsuite.players.rollback.

/as rollback <player> [snapshotId] confirm
  Restore a snapshot. Without an id the latest snapshot is used. A pre-rollback
  snapshot is captured first. Requires adminsuite.players.rollback.

/as logs [count] [filter]
  Show recent audit logs. Filters: all, chat, join, death, admin, command.
  Requires adminsuite.logs.

/as creative
/as cm
/as creativemenu
  Toggle the vanilla creative menu on your own client. Open your inventory to
  use it. Requires adminsuite.players.give. The toggle is a client setting; if
  your server has creative mode disabled it may still be refused by the client.

/as debug
/as debugmenu
  Toggle the vanilla debug menu on your own client. Requires adminsuite.world or
  administrator access.

/as spectator
  Toggle spectator mode on your own client. Requires adminsuite.players.spectate.

/as playerlist
  Open the vanilla player list on your own client. Requires
  adminsuite.players.view.

/as tppoi
  Open the vanilla POI teleporter on your own client. Requires
  adminsuite.players.teleport.

/as killall confirm
  Kill all non-player entities. Requires adminsuite.players.kill.

/as reload
  Reload config, data, snapshots and the ServerAssist hook. Requires adminsuite.reload.

Durations in commands and prompts use a compact form: 30m, 1h, 6h, 1d, 7d, 2w,
1mo, 1y or permanent.


PERMISSIONS
-----------

adminsuite.use
  Open AdminSuite. Grant this to staff. Not granted to players by default.

adminsuite.admin
  Master permission. Bypasses every granular check. Granted to the Oxide
  administrators group automatically.

adminsuite.players.view
  See the player list and player profiles.

adminsuite.players.teleport
  Teleport yourself and send players to coordinates.

adminsuite.players.bring
  Bring a player to you.

adminsuite.players.spawnontop
  Spawn on top of a player.

adminsuite.players.spectate
  Teleport near a player and toggle spectator mode.

adminsuite.players.kick
  Kick players and use the kick commands.

adminsuite.players.ban
  Ban and unban players.

adminsuite.players.mute
  Mute and unmute players.

adminsuite.players.freeze
  Freeze, unfreeze, jail and timeout players.

adminsuite.players.kill
  Kill a player.

adminsuite.players.heal
  Heal a player, set health, heal all.

adminsuite.players.feed
  Feed a player, set food/water, feed all.

adminsuite.players.god
  Toggle god mode.

adminsuite.players.fly
  Toggle fly mode in single-player/listen-server, or open the debug menu on a
  dedicated server. Fly itself is a client-side debug toggle.

adminsuite.players.inventory
  Inspect inventories, repair items and change item durability.

adminsuite.players.give
  Give items and open the creative menu.

adminsuite.players.clearinventory
  Clear a player's inventory or delete individual items.

adminsuite.players.takeinventory
  Take all items from a player.

adminsuite.players.snapshot
  Capture snapshots.

adminsuite.players.rollback
  Restore snapshots.

adminsuite.players.warn
  Warn players and manage warnings.

adminsuite.players.notes
  Manage private staff notes.

adminsuite.players.buffs
  Apply or remove buffs.

adminsuite.world
  Time, weather, airdrops, hordes, scouts, entity spawning, chunk/region resets
  and game preferences.

adminsuite.logs
  View and clear the in-memory audit log.

adminsuite.snapshots
  Browse, delete and clean up snapshots.

adminsuite.server
  Server information, save, announce, restart warning and shutdown.

adminsuite.reload
  Reload config, data, snapshots and the ServerAssist hook.

adminsuite.warps
  Create, delete and use warps.

adminsuite.moderation.adminlist
  View and edit the server admin file from the GUI.

adminsuite.moderation.whitelist
  View and edit the whitelist from the GUI.

adminsuite.moderation.banlist
  View the ban list.

adminsuite.moderation.kickall
  Kick every player.

Oxide administrators always pass every check.


CONFIGURATION
-------------

oxide/config/AdminSuite.json is created on first run. Values are clamped to safe
ranges on load.

  ChatPrefix                  Chat prefix used by AdminSuite replies.
  HookLabel                   ServerAssist home-row label. Default "Admin Suite".
  HookDescription             ServerAssist home-row description.
  HookPermission              Permission required to see the hook. Default
                              adminsuite.use.
  GrantUseToDefaultGroup      Grant adminsuite.use to the default player group.
                              Default false. Keep false for a staff-only tool.
  ProtectAdminsFromStaff      Stop non-admin staff from targeting admins.
                              Default true.
  AllowSelfTarget             Allow harmful actions against yourself.
                              Default false.
  NotifyStaffOnJoin           Tell online staff when a non-admin joins.
                              Default true.
  LogChat                     Record chat in the audit log. Default true.
  LogCommands                 Record console commands in the audit log.
                              Default false.
  LogDeaths                   Record player deaths. Default true.
  WriteLogFile                Also write entries to oxide/logs.
                              Default true.
  MaxLogEntries               In-memory log cap. Default 1000.
  SnapshotBeforeDestructive   Automatically snapshot before kick, ban, clear,
                              take, delete item and rollback. Default true.
  SnapshotRetentionDays       Delete snapshots older than this many days.
                              Default 14.
  MaxSnapshotsPerPlayer       Keep at most this many snapshots per player.
                              Default 20.
  DefaultBanDays              Reserved default. Default 1.
  DefaultTimeoutMinutes       Reserved default. Default 30.
  DefaultFreezeMinutes        Duration used by the Freeze button. 0 = indefinite.
                              Default 60.
  FreezeCheckSeconds          How often frozen players are pulled back.
                              Default 1.
  DefaultItemQuality          Default quality for /as give and GUI give.
                              Default 1.
  DefaultSpawnCount           Default count for /as spawn. Default 1.
  MaxWarps                    Maximum saved warps. Default 50.
  EnableJail                  Enable the Jail action. Default false.
  JailX, JailY, JailZ         Jail position. Configure these before enabling jail.
  ProtectedPlayers            Player ids or names that staff cannot target.

After editing the config, run /as reload or oxide.reload AdminSuite.


DATA AND LOGS
-------------

oxide/data/AdminSuite/data.json
  Warps, mutes, freezes, warnings, notes and personal homes.

oxide/data/AdminSuite/snapshots.json
  Snapshot metadata and serialized item data. Items are stored with the game's
  own ItemValue binary format, base64 encoded, so mods and quality survive.

oxide/logs/AdminSuite-YYYY-MM-DD.log
  One tab-separated line per audit entry. The plugin loads the most recent 200
  lines of the current day's file on startup so the log view is useful after a
  restart. The Clear button only clears the in-memory view; the file is kept.

Do not edit data.json or snapshots.json while the server is running. Use the GUI
or commands instead.


SNAPSHOTS AND ROLLBACK - DETAILS
--------------------------------

What is captured:
  - toolbelt, backpack and equipment items (including quality, durability,
    mods and metadata through ItemValue serialization)
  - position and rotation
  - health, stamina, water and food

What is not captured:
  - quest progress, skill points, permanent progression, buffs, active quests,
    vehicle ownership and land claims

A rollback clears the current toolbelt, backpack and equipment and writes the
snapshot items back. If the player's inventory size changed, slots outside the
current size are ignored. The player is teleported to the snapshot position.
A pre-rollback snapshot is captured first when SnapshotBeforeDestructive is true.

For a full save-game rollback, use your normal world backups. AdminSuite's
rollback is designed for item loss, griefing, accidental clears and bad trades.


LOGS - WHAT IS RECORDED
-----------------------

  join, leave, chat, death, command
  admin, kick, ban, unban, mute, freeze, warn, snapshot, rollback, world, server

The audit log is an operations aid, not a legal evidence system. If you need
guaranteed retention, ship oxide/logs to external storage and keep server
backups.


SERVER ASSIST INTEGRATION
-----------------------

AdminSuite registers a GUI hook with ServerAssist:

  ServerAssist.RegisterGuiHook(
      "adminsuite",
      HookLabel,
      HookDescription,
      HookPermission,
      "AdminSuite",
      "HandleGuiRequest")

ServerAssist calls:

  string HandleGuiRequest(string playerId, string payload)

The player id comes from ServerAssist's server-side net-package sender, not from
anything the client can spoof. Every action re-checks the Oxide permission on
the server. The hook is re-registered on OnServerAssistReady, so reloading either
plugin is safe. Unload unregisters the hook.

AdminSuite uses only the standard ServerAssist line protocol (rows, buttons, one
text input, title, subtitle, status) and does not require a client update.


CLIENT-AUTHORITATIVE TOOLS
--------------------------

Some values in 7 Days to Die live on the client, not the server: toolbelt and
backpack contents, fly mode, no-collision and invisibility. A server-only plugin
cannot force those. AdminSuite does the following:

  give                 uses the client's own giveself command, so it works
  heal / feed          sends NetPackageEntityStatChanged to the client
  set health / food    sends NetPackageEntityStatChanged to the client
  god                  sends NetPackageEntityAliveFlags to the client
  teleport / freeze    sends NetPackageTeleportPlayer to the client
  clear / take / repair inventory
                       applied on the server; the client may need to reopen the
                       inventory or relog before it shows the change
  fly                  toggles directly in single-player/listen-server; on a
                       dedicated server it opens the debug menu

This build is intentionally server-only. If you later want instant client-side
inventory clear/take/repair or true remote fly/no-clip/invisible, a small
AdminSuiteClient companion mod can be added later.


TROUBLESHOOTING
---------------

The "Admin Suite" row is missing from ServerAssist
  - Make sure oxide/plugins/ServerAssist.cs and AdminSuite.cs both compiled.
  - Look for "[AdminSuite] Registered ServerAssist GUI hook 'adminsuite'".
  - Confirm the player has adminsuite.use or is an Oxide administrator.
  - ServerAssist also hides the whole window from players without
    serverassist.use. Grant both nodes to staff.

"AdminSuite" opens but every row says no permission
  Grant the granular permissions. Oxide administrators bypass them.

"The ServerAssist client mod did not respond"
  The client does not have ServerAssistMod.dll and its Config folder, or the
  client has not sent its hello yet. Install ServerAssist on the client.

"Window group 'x' was not found"
  That is an ServerAssist/XUi issue for a different button, not AdminSuite.

Items in the inventory inspector show "Unknown item"
  The item class is from a mod that is not loaded on the server, or the item
  data is from an older save. Check the server log.

Give says the item was not found
  Use /as find <name> or the GUI item search. AdminSuite accepts both internal
  names such as 9mmBullet and display names with spaces such as 9mm Bullet.

Give says the inventory is full
  The remaining items are dropped at the player's feet with the vanilla give
  command. Look around the player; the reply says how many were given directly
  and how many were dropped.

Creative does nothing visible
  /as creative toggles the client's creative menu setting. It does not open a new
  window. Open your inventory to use it. Some servers disable creative mode in
  their game settings, in which case the client may ignore the toggle.

God mode says ON but damage still applies
  The server flag is authoritative and is set before the client sync. If the
  status says the client sync failed, the local flag may not update until the
  player relogs. Check that the player is connected and that no other mod blocks
  NetPackageEntityAliveFlags.

Fly does nothing on a dedicated server
  Fly is a client-side debug toggle in 7 Days to Die. /as fly opens the debug
  menu on your own client; use the Fly switch there. A true remote fly toggle
  needs a client-side companion mod. In single-player / listen-server, /as fly
  toggles EntityPlayerLocal.IsFlyMode directly.

Rollback did not restore quests/skills
  By design. See "What is not captured".

Weather commands do nothing
  Some weather commands are client or biome specific. Use the vanilla weather
  command in the server console to confirm the names available on your build.

Timed mutes or freezes seem to stay after a restart
  Expired entries are cleaned on server save and when the player chats/reconnects.
  If a timed entry looks stuck, run /as reload or clear it from the mute/freeze
  list.

Log file is not created
  Check WriteLogFile and the server's oxide/logs permissions. The in-memory log
  still works.

Why is there no drag-and-drop remote inventory?
  A true remote inventory window needs a client-side mod and custom net packages.
  AdminSuite is server-only and deliberately uses a live inspector with Take,
  Repair and Delete actions instead. That keeps installation to one .cs file and
  avoids client version mismatches.


SAFETY NOTES
------------

- AdminSuite is a staff tool. Do not grant adminsuite.use to normal players.
- ProtectAdminsFromStaff is on by default. Add owner ids to ProtectedPlayers for
  an extra layer.
- Destructive GUI actions require a confirmation press. Destructive chat commands
  require the word confirm at the end.
- Snapshots are taken before destructive actions when SnapshotBeforeDestructive is
  true. Do not disable that on a production server unless you have another backup
  workflow.
- Region Reset and world chunk resets permanently delete saved chunk data. They
  are intentionally behind a confirmation and adminsuite.world.
- The plugin writes item data from the game's own serializer. Do not hand-edit
  snapshots.json.


FILES IN THIS PROJECT
---------------------

AdminSuite/
  oxide/plugins/AdminSuite.cs     the whole plugin, compiled by Oxide
  Config/AdminSuite.example.json  a reference config
  README.txt                      this file
  docs/                           optional notes
  release/package.ps1             builds AdminSuite.zip

Build a release zip:

  powershell -ExecutionPolicy Bypass -File release/package.ps1

The zip contains the Oxide plugin, the README, the example config and the
quickstart guide. AdminSuite has no client mod.


VERSION HISTORY
---------------

1.0.4
  - Beautified every AdminSuite screen with art from the ServerAssist icon pack.
    Each screen now sets its own banner and background art (sa_nav_*, sa_act_*
    and sa_admin_suite), and every row can carry an icon, a short badge and a
    tint.
  - Home, Players, Player detail, Staff Tools, Buffs, Moderation, Mutes,
    Freezes, Warnings, Ban List, Admin List, Whitelist, Teleport, Warps, Items,
    World, Logs, Snapshots, Server, Help and Confirm all use themed row icons.
  - Item search results and inventory rows use live item icons through
    item:<internalName>, so the art follows the item and the game version.
    Items that have no icon of their own fall back to category art, and every
    item row gets a colour-coded badge: WEAPON, AMMO, FOOD, MED, ARMOR, TOOL,
    MOD, VEHICLE, BOOK, RES, BLOCK, DECOR or ITEM.
  - The item detail screen shows the item icon, category, internal name, slot,
    count, quality and durability at a glance.
  - Log rows use category-specific icons and tints for chat, joins, deaths,
    admin actions and commands.
  - The action button row now uses all 12 ServerAssist buttons, which makes room
    for the extra navigation, paging and refresh actions already used by the
    menus.

1.0.3
  - Heal, feed, set-health and set-food/water now send
    NetPackageEntityStatChanged to the target client so the client sees the new
    health, stamina, food and water immediately.
  - Heal All and Feed All sync every affected client.
  - Snapshot rollback now syncs the restored vitals.
  - Kill now uses the game's kill command for a server-authoritative death.
  - Teleports and the freeze pull-back now use NetPackageTeleportPlayer so the
    client actually moves, with the teleportplayer console command as fallback.
  - Updated the plugin for the ServerAssist 2.0 GUI name and icon API.

1.0.2
  - Give now uses the target client's own giveself command, so the item appears
    immediately and syncs back to the server. The server-side add path remains
    as a fallback and the vanilla give command drops leftovers.
  - God mode now sends NetPackageEntityAliveFlags to the target client so the
    client's local god flag matches the server flag. The status reports if the
    client sync could not be sent.
  - Added /as fly and a Toggle Fly button. Single-player/listen-server toggles
    EntityPlayerLocal.IsFlyMode directly. Dedicated servers open the debug menu
    because fly is a client-side debug toggle in 7 Days to Die.
  - Added adminsuite.players.fly.

1.0.1
  - Fixed /as give and the GUI give action: items are now split into valid
    stacks, added with the same bag/toolbelt path AdminShop uses, and leftovers
    are dropped at the player's feet instead of silently failing.
  - Item lookup now accepts internal names and display names with spaces, with
    separator-insensitive matching.
  - Added /as creative, /as cm, /as creativemenu, /as debug, /as debugmenu,
    /as spectator, /as playerlist and /as tppoi client commands.
  - Added /as ontop, /as spectate, /as inv, /as jail, /as clearmutes,
    /as clearwarnings, /as admin add/remove and /as whitelist add/remove.
  - Teleports now use the game's teleportplayer command as the primary path,
    with a direct teleport fallback.
  - Game admins are now recognized even if the Oxide administrators group was
    renamed or the permission grant was removed.
  - Better client-command feedback when the ServerAssist client cannot receive
    a command.

1.0.0
  - Initial release: ServerAssist GUI hook, player management, moderation,
    teleports, warps, items, inventory inspector, buffs, world control, weather,
    hordes, airdrops, audit logs, snapshots, rollback, server information,
    admin/whitelist/ban lists, warnings, notes and a full chat command set.


