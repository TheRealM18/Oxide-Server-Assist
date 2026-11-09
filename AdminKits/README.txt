AdminKits 1.1.0
===============

AdminKits is a server-side Oxide plugin that adds item kits to the ServerAssist
GUI. Admins can "make" kits (empty kits, or kits collected from their own
inventory), and players or staff can "collect" a kit straight into an inventory.

The whole kit editor and collection browser lives inside the existing
ServerAssist window. AdminKits does not ship a client DLL, XUi XML or custom net
package. ServerAssist renders it through its GUI hook API, exactly like AdminShop
and AdminSuite.

What "make" and "collect" mean here:

  MAKE
    Create Kit              create an empty kit and add items by name.
    Create From Inventory   copy your toolbelt, backpack and (optionally)
                            equipment into a new kit. Set
                            ClearInventoryOnCapture to true if the source
                            inventory should be emptied after the copy.
    Add Item                add one item stack with an exact amount, quality
                            and the game's own serialized item data.
    Give To                 give a kit to another online player.

  COLLECT
    Collect                 move the kit's items into your inventory.
    /kit <name>              the chat shortcut for Collect.
    Give To                 staff can collect a kit into another player's
                            inventory without using that player's cooldown.

Items are stored with the game's own ItemValue serializer, base64 encoded, so
quality, durability and mods survive a server restart. A kit is not a text list
of item names that is rebuilt from scratch on every claim.


REQUIREMENTS
------------

Server:
  ServerAssist/Mods folder with ServerAssistMod.dll, ModInfo.xml and Config
  oxide/plugins/ServerAssist.cs      version 1.1.0 or newer
  oxide/plugins/AdminKits.cs        this plugin

Every client that should see the window:
  ServerAssist/Mods folder
  AAZ_ServerAssistArt/Mods folder (the sa_* icon atlas)

AdminKits is server-only. Clients do not install an AdminKits mod. EAC must be
off, and the server and clients must use the same ServerAssist files. The icon
pack is optional but recommended: without it the cards still work and fall back
to the vanilla hammer symbol.


INSTALL
-------

1. Stop the server and the game.
2. Make sure ServerAssist is installed on the server and every client.
3. Copy oxide/plugins/AdminKits.cs into the server's oxide/plugins folder.
   Oxide compiles it on startup. Do not put the .cs file in the game Mods
   folder; the mod loader does not compile .cs files.
4. Start the server.
5. Join and press F7/F6, or type /kits, /kit or /ah and open "Admin Kits".
6. If you are not an Oxide administrator, grant the permissions below.

On first load the plugin creates:

  oxide/config/AdminKits.json
  oxide/data/AdminKits/kits.json

The administrators group receives adminkits.admin, adminkits.use and
adminkits.give automatically. The players group receives adminkits.use when
GrantUseToDefaultGroup is true (the default), so public kits work immediately.


FIRST-TIME PERMISSIONS
----------------------

Players who should collect public kits:

  oxide.grant group players adminkits.use

Staff who should create and edit kits:

  oxide.grant group moderators adminkits.use
  oxide.grant group moderators adminkits.admin
  oxide.grant group moderators adminkits.give
  oxide.grant group moderators serverassist.use

Per-kit permissions are optional. A kit with a blank permission is available to
everyone who has adminkits.use. A kit with a permission node is only visible to
players who have that node. If you type "auto" in the kit's Permission field,
AdminKits creates:

  adminkits.kit.<kit-id>

and registers it with Oxide so it appears in PermissionGui. You can then grant
that node to a group or a single player.


OPENING THE KIT WINDOW
----------------------

  /kits
  /kit
  F7 or F6 (ServerAssist's keys)
  The "[GUI] Admin Kits" row on the ServerAssist home screen

ServerAssist itself also requires serverassist.use. AdminKits registers its hook
with the permission in HookPermission (default adminkits.use).

When ServerAssist opens a GUI hook, its Home / Shared / My Buttons tabs are
replaced by the hook view and a Back button. Every action stays inside the same
ServerAssist window. The Back button returns to the ServerAssist board.


ICONS AND ART
-------------

AdminKits uses the finished Server Assist Art set. The full mapping is in
ICONS.md. Highlights:

  Panel card              sprite:sa_admin_kits
  Collect a Kit           sprite:sa_nav_items
  Manage Kits             sprite:sa_nav_logs
  Create From Inventory   sprite:sa_dynamic_backpack
  Kit item rows           item:<internalName> (the real game item icon)
  Permission              sprite:sa_permission_gui
  Cooldown                sprite:sa_act_time
  Edit                    sprite:sa_act_repair
  Delete / clear          sprite:sa_act_ban
  Warning / limit         sprite:sa_act_warn

Kit rows in the collect and manage lists change icon with their state:
permission-locked, cooldown, limit reached, disabled, or hidden. Screen banners
and the large background watermark follow the same art.

The icons are client-side and are provided by the AAZ_ServerAssistArt pack.
Copy its UIAtlases folder into the Server Assist mod folder on the server and
on every client:

  Mods/AA0_ServerAssistMod/UIAtlases/UIAtlas/sa_*.png

The planned sa_kits_* action icons in ServerAssistArt/PLANNED_ICONS.md are not
rendered yet. AdminKits uses the closest finished icons and keeps every sprite
key in the KitIcons class, so they can be swapped later without touching the
screen logic. The focused seven-icon art request is
ServerAssistArt/REQUESTS_AdminKits.md; the twelve kit-category icons are not
needed.


MAKING A KIT - GUI
------------------

1. Open Admin Kits -> Manage Kits -> Create Kit.
2. Type a name. The kit editor opens.
3. Press Items -> Add Item. Type an item spec:

     item [amount] [quality]

   Examples:
     9mmBullet 100 6
     foodCanChili 10
     762mmBullet 200
     medicalFirstAidBandage 5

   You can type the internal item name (9mmBullet) or the display name
   ("9mm Bullet"). Surrounding spaces are ignored.
   Alternatively, hold an item in your hand and press Items -> Add Held to copy
   the exact stack, including quality, durability and mods.
4. Optional: Permission, Cooldown, Limits, Description.
5. Press Back to see the detail screen, then Give To to send the kit to a
   player, or leave it available for players to Collect.

Create From Inventory:

1. Manage Kits -> From Inventory.
2. Type a name.
3. AdminKits copies every non-empty toolbelt, backpack and equipment slot into
   the new kit, preserving item data.
4. ClearInventoryOnCapture is false by default, so the copy does not remove the
   original items. Set it to true if you want a true "collect my inventory into
   a kit and empty it" workflow.

Kit editor buttons:

  Name          rename the kit
  Permission    blank = everyone, auto = adminkits.kit.<id>, or any node
  Cooldown      minutes between collections (0 = none)
  Limits        total uses and uses per player (0 = unlimited)
  Description   one-line description shown in the detail screen
  Items         add, inspect, remove or clear item stacks
  Add Held      copy the exact item stack currently in your hand
  Enable/Hide   control availability without deleting the kit
  Delete        permanent delete after a confirmation screen


COLLECTING A KIT
----------------

GUI:
  Collect a Kit -> choose a kit -> Collect

Chat:
  /kit <name>
  /kit list

A collection is refused when:

  - the kit is disabled,
  - you do not have the kit's permission,
  - the kit reached its total use limit,
  - you reached your per-player limit,
  - the cooldown is still active,
  - you do not have enough bag/toolbelt space for the whole kit,
  - you are not spawned in the world.

AdminKits checks inventory space before it gives anything, so a full inventory
does not consume the cooldown or silently eat half a kit.

Staff with adminkits.admin bypass the permission, use limits and cooldown when
they collect a kit themselves. Staff with adminkits.give can use Give To on
another online player. Give To still checks that the target has enough space.


COMMANDS
--------

/kits
/kit
  Open the Admin Kits window.

/kit <name>
  Collect a kit. The name may contain spaces.

/kit list
  List the kits you can collect.

/kit help
  Short in-chat help.

/kitadmin
/kitadmin help
  Admin command reference.

/kitadmin list
  List every kit with its id, name, stack count, state, uses and permission.

/kitadmin create <name>
  Create an empty kit. Names may contain spaces.

/kitadmin frominv <name>
  Create a kit from your toolbelt, backpack and equipment.

/kitadmin delete <name> confirm
  Delete a kit and all of its claim records.

/kitadmin enable <id or name>
/kitadmin disable <id or name>
  Enable or disable a kit.

/kitadmin additem <id or name> <item> [amount] [quality]
  Add an item stack. Examples:
    /kitadmin additem starter 9mmBullet 100 6
    /kitadmin additem Starter Kit foodCanChili 10

/kitadmin removeitem <id or name> <index>
  Remove one item stack. Use the index shown in the GUI item list.

/kitadmin give <player> <id or name>
  Give a kit to an online player.

/kitadmin reset <player> <id or name>
  Clear that player's claim count and cooldown for the kit.

/kitadmin reload
  Reload config, kit data, per-kit permissions and the ServerAssist hook.

Kit ids are generated from the name. They are only used internally and in
commands; the GUI always shows friendly names.


PERMISSIONS
-----------

adminkits.use
  Open the kit window and collect public kits. Granted to the players group when
  GrantUseToDefaultGroup is true.

adminkits.admin
  Create, edit, enable, hide and delete kits; use the admin GUI screens and
  commands. Granted to the Oxide administrators group automatically.

adminkits.give
  Give kits to other players. Admins receive this automatically.

adminkits.kit.<id>
  Optional per-kit permission created with "auto". Registering the node is
  automatic. Grant it with PermissionGui, /perm or the normal Oxide commands.


CONFIGURATION
-------------

oxide/config/AdminKits.json is created on first run. Values are clamped to safe
ranges on load.

  ChatPrefix                 Chat prefix for command replies. Default "[Kits]".
  HookLabel                  ServerAssist home row label. Default "Admin Kits".
  HookDescription            ServerAssist home row description.
  HookPermission             Permission needed to see/open the hook.
                             Default "adminkits.use".
  GrantUseToDefaultGroup     Grant adminkits.use to the players group.
                             Default true.
  DefaultCooldownMinutes     Cooldown applied to new kits. Default 0.
  DefaultMaxUses             Total uses applied to new kits. 0 = unlimited.
  DefaultMaxPerPlayer        Per-player uses applied to new kits. 0 = unlimited.
  DefaultItemQuality         Quality used when Add Item omits a quality. Default 1.
  ClearInventoryOnCapture    Empty the source inventory after Create From
                              Inventory. Default false (copy only).
  CaptureEquipment           Include equipment slots in Create From Inventory.
                             Default true.
  LogClaims                  Write successful collections to the server log.
                             Default true.
  MaxKits                    Maximum number of defined kits. Default 100.
  MaxItemsPerKit             Maximum item stacks in one kit. Default 50.

After editing the config, run /kitadmin reload or oxide.reload AdminKits.


DATA
----

oxide/data/AdminKits/kits.json
  Kit definitions and per-player claim records.

  Each kit item stores:
    Item   internal item name (for display and fallback)
    Count  stack amount
    Data   base64-encoded ItemValue written by the game's serializer

  Because the item value is serialized by the game, quality, durability and
  installed mods are preserved exactly.

Do not hand-edit kits.json while the server is running. Use the GUI or commands.


SERVER ASSIST INTEGRATION
-----------------------

AdminKits registers a GUI hook with ServerAssist:

  ServerAssist.RegisterGuiHook(
      "adminkits",
      HookLabel,
      HookDescription,
      HookPermission,
      "AdminKits",
      "HandleGuiRequest")

ServerAssist calls:

  string HandleGuiRequest(string playerId, string payload)

The player id is supplied by ServerAssist's server-side net-package sender, not
by anything the client can spoof. Every action re-checks the Oxide permission on
the server. AdminKits re-registers on OnServerAssistReady, so reloading either
plugin is safe, and Unload unregisters the hook.

The view uses only the standard ServerAssist line protocol: title, subtitle,
status, one text input, up to 10 rows and up to 10 buttons. No client update is
needed when this plugin changes.


TROUBLESHOOTING
---------------

The "Admin Kits" row is missing from ServerAssist
  - Make sure ServerAssist.cs and AdminKits.cs both compiled.
  - Look for "[AdminKits] registered ServerAssist GUI hook 'adminkits'".
  - Confirm the player has adminkits.use (and serverassist.use).
  - Check HookPermission in the config.

"Open the ServerAssist window and choose Admin Kits"
  ServerAssist is not loaded, so /kits cannot open its window. Install
  oxide/plugins/ServerAssist.cs and the ServerAssist client mod.

"The ServerAssist client mod did not respond"
  The client does not have ServerAssistMod.dll and its Config folder, or it has
  not sent its hello yet. Install ServerAssist on the client.

Add Item says the item was not found
  Use the exact internal name (9mmBullet) or the in-game display name
  ("9mm Bullet"). Mods that add items work as long as they are loaded on the
  server. If the name ends in a number, use the internal name so the amount is
  not confused with part of the name.

A kit is missing from the collect list
  A hidden kit is not listed, but it still works with /kit <name> unless it is
  disabled or you lack its permission. Check that the kit is enabled, not
  hidden, and that the player has the kit's permission. Admins see hidden
  enabled kits in the collect list and all kits in Manage Kits.

Collect says there is not enough inventory space
  AdminKits requires room for the whole kit. Free bag or toolbelt slots and try
  again. Nothing is removed from the kit and no cooldown is consumed on a failed
  space check.

Give To says the player is not online
  Give To targets a spawned, connected player. Type the exact name or platform
  id.

Items show "Unknown item"
  The item class comes from a mod that is not loaded on the server, or the kit
  data came from an older save. Check the server log.

Clear Inventory On Capture did not empty my inventory
  Set ClearInventoryOnCapture to true in oxide/config/AdminKits.json and run
  /kitadmin reload. The default is copy-only on purpose.


FILES IN THIS PROJECT
---------------------

  oxide/plugins/AdminKits.cs          the whole plugin, compiled by Oxide
  Config/AdminKits.example.json       a reference config
  README.txt                          this file
  ICONS.md                            the Server Assist Art icon map
  tests/PluginCompile/                build check against the game assemblies
  tests/LogicHarness/                 reflection checks for the parser, ids, icons and data normalization
  release/package.ps1                 builds AdminKits.zip

Build a release zip:

  powershell -ExecutionPolicy Bypass -File release/package.ps1

AdminKits has no client mod and no XUi files.


VERSION HISTORY
---------------

1.1.0
  - Added Server Assist Art icons throughout the panel: card art, per-screen
    banner/background art, state-aware kit rows, real game item icons on kit item
    rows, and action-button icons.
  - Kit rows now show LOCKED, WAIT, LIMIT, OFF and HIDDEN states with the
    matching art and tints.
  - Documented the icon map in ICONS.md.
  - Extended the logic harness to check the 9-field row and 7-field button
    protocol, item icon resolution and kit status art.

1.0.0
  - Initial release.
  - ServerAssist GUI hook with home, collect, manage, detail, editor, limits,
    item inspector, delete and clear confirmations.
  - Create empty kits, create from inventory, add items by internal or display
    name, exact ItemValue storage for quality, durability and mods.
  - Collect into the player's bag and toolbelt with a full-inventory pre-check.
  - Per-kit permissions with "auto" node creation, cooldowns, total use limits,
    per-player limits and admin bypass.
  - Give To for staff, chat claim commands and a full /kitadmin command set.
  - Config and data persistence under oxide/config and oxide/data.
  - Source compiles against the installed Assembly-CSharp and Oxide assemblies.
    The logic harness checks the item-spec parser, kit id generation, protocol
    encoding and data normalization.


