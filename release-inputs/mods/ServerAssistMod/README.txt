Server Assist 2.1.0
===================

Server Assist is the single permission-aware GUI launcher for 7 Days to Die.
Other plugins can register optional panels into it instead of shipping their
own XUi windows.

Version 2.0 introduces:

  * A left-sidebar / content-pane layout.
  * Art cards with icons, labels, subtitles and badges.
  * Vanilla icons, live item icons, and custom atlas icon hooks.
  * An item icon picker inside the button editor.
  * A 12-action editor toolbar.
  * ServerAssist naming, permissions, commands and data paths.
  * API.md for plugin authors and MIGRATION.md for old installs.

Version 2.1 adds:

  * 10 home cards per page with Prev/Next paging.
  * Right-click favorites, sorted before the alphabetical home order.
  * Optional icons on action buttons.
  * GetPanelIcon and GetPluginIcon for live icon read-back.

DynamicBackpack is now a server-only page panel. The backpack plugin registers
a "Backpack" card with Server Assist and renders nine pages of nine item cards;
there is no separate backpack window, client DLL, bridge or 9x9 grid. If the
DynamicBackpack plugin is not installed, the Server Assist board works exactly
as before.

The idea is simple:

  * The Server Assist window is installed for everyone.
  * Shared buttons are created by admins and shown only to players whose
    permissions allow them.
  * Players can create their own private buttons if they have
    serverassist.custom.
  * Every button can open another XUi window group or run a client console
    command.
  * Optional GUI hooks (for example PermissionGui) appear as [GUI] rows and
    are rendered inside this same window.
  * Button and hook permissions are checked again on the server every time a
    player opens the board, clicks a button, or enters a hooked screen.

That means one universal GUI can expose many different mod GUIs, while each
button and hook keeps its own dynamic permission setup.


FILES
-----

Server:
  oxide/plugins/ServerAssist.cs

Server and every client:
  ServerAssist/                     (this whole folder)
    ServerAssistMod.dll
    ModInfo.xml
    Config/XUi_InGame/xui.xml
    Config/XUi_InGame/windows.xml

Optional server hook:
  oxide/plugins/PermissionGui.cs   (PermissionGui, if you use it)

The client DLL is required. A server cannot push XUi windows to a client.
PermissionGui no longer needs its own client mod; it registers a server-side
hook into this window.


INSTALL
-------

1. Stop the server and the game.
2. Copy ServerAssist/ into the server's Mods folder.
3. Copy ServerAssist/ into every client's Mods folder. The release zip uses the
   folder name AA0_ServerAssistMod; use the same folder name on the server and
   every client.
4. Copy oxide/plugins/ServerAssist.cs into the server's oxide/plugins folder.
5. Start the server, then join with a client. Press F7 or F6, or type /sa in chat.

EAC must be off. Server and clients must use the same ServerAssist files.


USING THE BOARD
---------------

HOME
  Shows the shared and personal buttons available to you. Click a button to
  launch it. Right-click a home card to add or remove it from your favorites.
  Favorites sort first, then alphabetical by the card label (not by plugin
  name). The board shows 10 cards per page and adds Prev/Next buttons when more
  entries exist.

SHARED
  Admin-only. Create, edit, reorder, disable and delete server-wide buttons.

MY BUTTONS
  Personal buttons for the current player. Requires serverassist.custom.

F7, F6, /sa and /serverassist toggle the window.

When you create a button through the GUI, the prompts are:

  Label
  Type: window or command
  Target: an XUi window group name, or a console command
  Permission: blank for everyone, auto for a per-button permission node

"auto" creates a per-button node: serverassist.button.<button-id> for a shared
button, or serverassist.personal.<button-id> for a personal button. The node is
registered with Oxide and appears in PermissionGui, so you can grant it to a
group or a user there. A personal auto node is granted to its creator
automatically so the button works immediately; an admin can revoke it later.
You can also type any existing permission node, such as permissiongui.use.

Button icons are optional. Use:

  item:gunPistol                       live game item icon
  sprite:ui_game_symbol_map            vanilla or loaded sprite
  atlas:ServerAssistAtlas:permissions  custom atlas sprite
  (leave blank)                        default vanilla symbol

The Edit screen has Edit Icon and Find Item Icon. Find Item Icon searches
ItemClass.ItemNames by item name or localized name and previews the actual item
icon. Icons are stored as keys, not baked paths, so item icon changes follow
game updates.

Item icons are resolved from the client's live ItemClass data and drawn from
the ItemIconAtlas manager. That includes the base game's Data/ItemIcons textures
and any mod that adds PNGs under Mods/<mod>/UIAtlases/ItemIconAtlas. You do
not need to copy, re-export or register those textures again. If a sprite lives
in a different loaded atlas, Server Assist checks the other atlas managers and
uses the one that actually contains the sprite.


BUTTON TYPES
------------

window
  Opens an XUi window group on the client. ServerAssist closes itself first,
  then opens the target group modally.

  Target must match an XUi window group name that is installed on the client.

  PermissionGui is not a window target anymore. It is an optional GUI hook
  and is rendered inside ServerAssist. The target mod and its XUi config must
  already be installed on the client for a custom window target.

command
  Runs a console command through the local client console. The leading "/" is
  optional. Server-authoritative commands still enforce their own permission
  checks; this does not grant admin rights.

  Command buttons are disabled for player-made personal buttons by default.
  Admins can enable AllowPlayerCommandButtons in the config if they want them.


PERMISSIONS
-----------

serverassist.use
  Open the Server Assist window. Granted to the default player group by default
  (GrantUseToDefaultGroup).

serverassist.custom
  Create and edit personal buttons. Granted to the default player group by
  default (GrantCustomToDefaultGroup). Set GrantCustomToDefaultGroup to false
  if only staff should create personal buttons.

serverassist.admin
  Manage shared buttons and use the /sa admin commands. Granted to the
  administrators group automatically.

serverassist.button.<id>
  One permission per shared button created with "auto". Registering these
  nodes is automatic. Grant them with PermissionGui, /perm, or the normal
  Oxide permission commands.

serverassist.personal.<id>
  One permission per personal button created with "auto". It is registered
  automatically and granted to the creator. Admins can revoke it.

If a button's Permission field is empty, everyone who can open ServerAssist can
see and launch it. If it is set, the button is hidden from players without the
permission (admins can still see shared buttons unless AdminSeesAllButtons is
false).


ADMIN COMMANDS
--------------

/sa
/sa open
  Open the GUI.

/sa list
  List shared buttons and their targets/permissions.

/sa add <label> <window|command> <target> [permission]
  Create a shared button without using the GUI. The label cannot contain
  spaces because it is one console argument. Use the GUI for normal editing.

/sa remove <id>
  Remove a shared button.

/sa reload
  Reload ServerAssist config and data, and re-register permissions.


CONFIG AND DATA
---------------

Oxide writes these under oxide/config and oxide/data:

  oxide/config/ServerAssist.json
  oxide/data/ServerAssist/data.json

Configuration options:

  ChatPrefix                 chat prefix used by ServerAssist replies
  GrantUseToDefaultGroup     grant serverassist.use to the players group
  GrantCustomToDefaultGroup  grant serverassist.custom to the players group
  AdminSeesAllButtons        admins can see disabled/shared buttons
  AllowPlayerCustomButtons   allow personal buttons at all
  AllowPlayerCommandButtons  allow personal command buttons
  MaxSharedButtons           maximum shared buttons
  MaxPersonalButtons         maximum personal buttons per player

Button data is saved by the plugin. Do not edit data.json while the server is
running; use the GUI or /sa commands instead.


GUI HOOKS AND PERMISSIONGUI
---------------------------

ServerAssist exposes a small server-side hook API. It is optional: if the
provider plugin is not loaded, no hook row appears.

A provider registers itself with:

    Plugin serverAssist = plugins.Find("ServerAssist");
    serverAssist.Call(
        "RegisterGuiHook",
        "permissions",                       // hook id
        "Permission Defaults",                // row label
        "Groups, users, tracks and more.",   // description
        "permissiongui.use",                 // extra permission (blank = all)
        "PermissionGui",                     // provider plugin name
        "HandleServerAssistRequest");         // method name

The provider method must have this signature:

    string HandleServerAssistRequest(string playerId, string payload)

It returns the same line protocol used by ServerAssist:
title, subtitle, status, input, row and btn entries.

When a player clicks the [GUI] row, ServerAssist enters hook mode. The static
Home/Shared/My Buttons tabs disappear and a Back button appears. All actions
and text submissions are forwarded to the provider until the player presses
Back. The provider can use all 10 rows and 12 action buttons; its view is rendered by
the existing ServerAssist client, so it needs no client DLL or XUi XML.

PermissionGui uses this hook automatically. Install ServerAssist first, then
PermissionGui.cs in oxide/plugins. PermissionGui should not ship a client mod
or a permGui window. If it registers successfully, a row labelled
"[GUI] Permission Defaults" appears on the ServerAssist home screen for players
with permissiongui.use.

ServerAssist does not need a "Permission Defaults" button in its data file. If
an older install still has the seeded permission-manager button targeting
permGui, ServerAssist removes it on load. Use the hook row instead.


PROVIDER API
------------

ServerAssist also supports a provider API for plugins that need the three top
tabs. AuctionHouse uses this API. Registered providers also receive a home-card
entry automatically.

Server-side methods on the ServerAssist plugin:

    RegisterGuiProvider(providerId, pluginName, handlerMethod,
                        title, tab1, tab2, tab3);
    OpenGuiProvider(playerId, providerId, payload);
    UnregisterGuiProvider(providerId);
    SetProviderIcon(providerId, icon);
    GetProviderHomeSupport();

GetProviderHomeSupport returns true when the home board lists providers. A
companion can use it to decide between provider mode and a normal home panel,
so older ServerAssist builds still get a home card.

The provider handler is a public method with this signature:

    string Handler(string playerId, string payload)

Registered providers appear on the Home board as:

    [GUI] <title>    with a PROVIDER badge and the provider icon

Clicking the home card opens the provider in tab mode. The home row action is
providerpanel|open|<providerId>. The provider icon is also used as the default
background and banner when the provider view omits them.

Provider mode is different from hook mode:

  * The ServerAssist Home / Shared / My Buttons tabs stay visible.
  * The provider can rename them by returning tab1label, tab2label and
    tab3label in its view.
  * There is no injected Back button. The provider can use an a|home action,
    or the player can use /sa and /serverassist, to return to the normal board;
    F7 resumes the active provider.
  * Provider requests are routed before ServerAssist's own serverassist.use
    check, because the provider owns its own permission checks.

AuctionHouse registers the provider id "auctionhouse", opens it from /auction or
its home card, and uses sprite:sa_auction_house for its icon. PermissionGui and
AdminShop use the home-row GUI hook API instead.


TROUBLESHOOTING
---------------

"window group ... was not found on this client"
  The target GUI is not installed on this client, or its window group name is
  different. Check the other mod's Config/XUi_InGame/xui.xml.

"ServerAssist window group was not found"
  The client log now lists the loaded XUiWindowGroup IDs/controllers. If no
  serverAssist entry is present, the client XUi did not parse
  Config/XUi_InGame/xui.xml. Make sure the file is directly under
  AA0_ServerAssistMod/Config/XUi_InGame/, not nested inside src/ or obj/.
  The current client uses the player-owned XUi (player.PlayerUI.xui) and also
  falls back to the group ID, window-manager name, and controller-type index.

"Style key ... not found" for windowServerAssist
  An old windows.xml is installed. Captions must not use square brackets in
  XML (for example caption="Home", not caption="[Home]"). Install the current
  Config/XUi_InGame/windows.xml.

"The ServerAssist Oxide plugin is not loaded on this server."
  ServerAssist.cs is missing from oxide/plugins or failed to compile. Check the
  server log.

"The ServerAssist client mod is not installed..."
  The client does not have ServerAssistMod.dll and its Config folder, or the
  client has not sent its hello yet.

Button is missing
  Check Enabled, its Permission field, and whether the player has that
  permission. Admins can open Shared to see disabled buttons.

The window opens but is empty
  The client mod may not have received a response from the plugin. Check the
  server log for [ServerAssist] errors and the client log for net package
  registration errors.





PLAYER PROFILES EXTENSION (client mod 2.0.1)
--------------------------------------------

The 2.0.1 client mod adds an optional @pp protocol used by the PlayerProfiles
Oxide plugin. It is carried inside the existing Server Assist request/response
packages, so no new NetPackage type is registered and clients with older mods
still join normally. Older clients simply use PlayerProfiles' reconnect path.

See the PlayerProfiles README for installation, commands and caveats.
