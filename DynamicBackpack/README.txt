Dynamic Backpack for 7 Days to Die (V3.4)
==========================================
Plugin version 2.2.0

A permission-driven virtual backpack that runs entirely inside Server Assist.

This is a server-only Oxide plugin. There is no DynamicBackpack client DLL, no
custom XUi window, and no backpack net packages. Server Assist owns the UI,
the cards, the buttons, the page input and the item search. The plugin owns the
stored items, permission checks and item movement.

Each page holds 10 items. The number of pages is permission-based and can be
far more than the number of action buttons Server Assist shows: page selection
and item search handle large backpacks without changing Server Assist.


CAPACITY AND PAGES
------------------

  Page 1   -> 10 cards -> slots   1-10
  Page 2   -> 10 cards -> slots  11-20
  Page 3   -> 10 cards -> slots  21-30
  ...
  Page 10  -> 10 cards -> slots  91-100
  Page 100 -> 10 cards -> slots 991-1000

MaxPages controls the highest page that can be registered and granted. The
default is 100 and the hard cap is 1000. The player's backpack is only as large
as their own permission requires; a player with one page gets 10 slots, a
player with 100 pages gets 1000 slots.

Every card shows the real game item icon, the localized item name, the amount
and the quality when the item has quality. Clicking a card withdraws that stack
into your inventory (or drops it at your feet if your inventory is full).
"Deposit Held" stores the item in your hand.


REQUIREMENTS
------------

  * 7 Days to Die V3.4 (built against the 3.4 managed assemblies).
  * Oxide / uMod.
  * ServerAssist 2.0.0 or newer: the Server Assist plugin on the server and the
    ServerAssistMod client mod on the server and every client.
  * The ServerAssistArt UI atlas that contains sa_dynamic_backpack.png on every
    client. Without it the panel falls back to the default hammer icon.
  * EAC must be off.


FILES
-----

Server:

  oxide/plugins/DynamicBackpack.cs
  oxide/plugins/ServerAssist.cs
  Mods/AA0_ServerAssistMod/        (Server Assist client mod, server-side copy)

Server and every client:

  Mods/AA0_ServerAssistMod/
    ServerAssistMod.dll
    ModInfo.xml
    Config/XUi_InGame/xui.xml
    Config/XUi_InGame/windows.xml
    README.txt

Do not install the old DynamicBackpackMod.dll/client folder. Remove any old
DynamicBackpack client mod folder if you installed an earlier build.


INSTALL - SERVER
----------------

1. Stop the server.
2. Install the Server Assist plugin and mod folder (see the Server Assist
   README or Wiki).
3. Copy DynamicBackpack.cs into:
       <server>/oxide/plugins/DynamicBackpack.cs
4. Remove any old Mods/DynamicBackpack client-mod folder if it exists.
5. Start the server.
6. Check the server log for:
       [DynamicBackpack] loaded. Pages: backpack.1 - backpack.100. UI is provided by Server Assist.
       [DynamicBackpack] registered the backpack panel with Server Assist (sprite:sa_dynamic_backpack).

The plugin registers a "Backpack" panel with Server Assist. It appears on the
Server Assist home board with a PLUGIN badge and the
sprite:sa_dynamic_backpack icon from the ServerAssistArt pack.

If the log says the icon could not be applied, check that the ServerAssistArt
UI atlas is installed on clients and that the Server Assist version supports
SetPanelIcon.


INSTALL - CLIENT
----------------

1. Stop the game.
2. Install the same AA0_ServerAssistMod folder used by the server.
3. Do not install a separate DynamicBackpack client mod. There is none.
4. Start the game and join. EAC must be off.
5. Press F7 (or F6), or type /sa in chat, then click the Backpack card.
   You can also type /backpack in chat.


USING IT
--------

  F7 / F6        Open or close Server Assist.
  /sa            Open Server Assist.
  /backpack      Open the backpack page directly in Server Assist.
  /backpack info Show unlocked pages, slots and usage.
  /backpack save Force-save your backpack.
  /backpack reload Reload config and re-register permissions (admin only).
  /backpack help Show the command list.

Inside the backpack panel:

  10 item cards    One page at a time. Locked pages show "Locked".

  Prev / Next      Move one page. While searching, move one results page.
  First / Last     Jump to the first or last page/result page.
  -10 / +10        Jump ten pages/results at a time.
  Go To Page       Opens the page input. Enter a backpack page number.
  Search           Opens the search input. Enter an item name.
  Clear Search     Removes the current search and returns to the page view.
  Deposit Held     Store the item in your hand on the current page.
  Refresh          Rebuild the page view.
  Back             Server Assist's toolbar Back button.

PAGE SELECTION
  Click "Go To Page", type a number and Submit. The backpack supports many
  pages; you are not limited to the number of action buttons. The input range
  is shown in the label, for example "Backpack page (1-100)".

ITEM SEARCH
  Click "Search", type part of an item name and Submit. The search covers every
  unlocked page and shows 10 matching stacks per results page. Use Prev/Next,
  First/Last or -10/+10 to move through results. Click a result to withdraw it.
  While a search is active the search bar stays visible so you can refine it;
  Cancel hides the bar without clearing the results. Clear Search returns to
  the normal page view.

"Deposit Held" always uses the current backpack page. It is not affected by
the search result page.

The backpack is server-authoritative. The client never edits the bag directly
and there is no drag-and-drop grid. Every withdraw and deposit is validated and
applied by the server.


PERMISSIONS
-----------

  backpack.1 ... backpack.N
      One page per level, 10 slots per page. N is MaxPages (default 100,
      hard cap 1000). The highest granted level is used.
      Examples: backpack.1 = 10 slots, backpack.10 = 100 slots,
                backpack.100 = 1000 slots.

  backpack.*
      Gives all configured pages (MaxPages).

  backpack.admin
      Gives AdminPages (default 10). Also required for /backpack reload.
      Server admins (IPlayer.IsAdmin) also receive AdminPages even if the node
      was not granted explicitly. An admin with backpack.100 still gets 100
      pages; the highest value wins.

By default the plugin grants backpack.1 to the players group and backpack.10
plus backpack.admin to the administrators group. Change this in the config.

To offer 100 pages:
  1. Keep MaxPages at 100 (the default), or raise it up to 1000.
  2. Grant backpack.100 to the player or group.
  3. Reload the plugin or wait for the permission refresh.
The player's bag grows to 1000 slots the next time the panel opens.

Existing installs that already granted backpack.9 keep nine pages. Grant
backpack.10 or higher, or backpack.*, for more pages.

Permission changes are picked up immediately for online players and checked
again every 30 seconds as a fallback. The panel itself is registered with
backpack.1, so a player needs at least one page to see the Backpack card.


CONFIG
------

The plugin writes oxide/config/DynamicBackpack.json on first start:

  ChatPrefix                 chat prefix for command replies
  PermissionPrefix           base node, default "backpack"
  MaxPages                   maximum pages, default 100 (hard cap 1000)
  DefaultPages               pages for players with no backpack permission
  GrantFirstPageToPlayers    grant <prefix>.1 to the default players group
  AdminPermission            admin override node, default "backpack.admin"
  AdminPages                 pages for AdminPermission, default 10
  GrantAdminToAdministrators grant AdminPages to the administrators group
  SaveIntervalSeconds        how often dirty backpacks are written to disk
  DropOverflowOnDowngrade    move items above the page limit to a dropped loot
                             bag when permission is reduced. Default is false,
                             which keeps those items saved in locked pages until
                             the player upgrades again. Set true only if you
                             want overflow dropped at the player's feet.

Important:
  * MaxPages must be at least as high as the highest backpack.N permission you
    want to grant. The plugin registers backpack.1 through backpack.MaxPages.
  * If you upgrade from a build whose config had MaxPages: 10, set MaxPages to
    the new desired highest page (for example 100) and reload. Old config keys
    are otherwise left as they are.
  * An admin with a higher backpack.N permission gets that higher value even if
    AdminPages is lower.

Setting GrantFirstPageToPlayers to false does not revoke an existing players
group grant. Use oxide.revoke group players backpack.1 if you want to remove
the free first page from players who already have it.

Changing PermissionPrefix also changes the permission nodes. For example,
"backpack" gives backpack.1 through backpack.MaxPages. Reload the plugin after
changing it: /backpack reload (or oxide.reload DynamicBackpack).


DATA
----

Backpacks are stored under:

  oxide/data/DynamicBackpack/<player-id>.json

Each file contains a base64 ItemStackGrid payload plus the player name, last
known page count and update time. The game's own ItemStack format is used, so
quality, durability, ammo, mods and other item data are preserved.

The physical bag is sized to the player's page permission, not to MaxPages.
It grows when the player earns more pages and is never shrunk automatically;
if permission is reduced, the extra slots stay saved and locked. This means a
player with one page does not carry 100 pages of empty slots.

Existing data from the old 81-slot build is preserved. Slot indexes do not
move, but page boundaries changed from 9 items to 10 items per page, so an old
item may appear on a different page than before. Capacity grows when the
player's page permission increases.

The server writes dirty backpacks every SaveIntervalSeconds (default 15), on
server save, on player disconnect and on plugin unload.


HOW PERMISSION CHANGES WORK
---------------------------

  * When a player joins, the plugin loads the stored bag and caches the
    highest backpack page their permissions allow.
  * The plugin scans the player's direct and inherited group permissions for
    backpack.N values, so it stays fast even with hundreds of pages.
  * When a backpack.N permission is granted or revoked, the server recomputes
    the pages. The next time the player opens or refreshes the panel, the new
    pages are visible.
  * Group permission changes and group membership changes also trigger a
    refresh. A 30-second check catches anything else.
  * If the new page count is larger, the bag grows automatically.
  * If the new page count is smaller, items in the now-locked pages stay in
    the backpack and are simply hidden. Set DropOverflowOnDowngrade to true if
    you want them moved into a DroppedLootContainer at the player's feet.
  * The server rejects withdraw and deposit requests for locked pages.


PAGE UI AND SERVER ASSIST
-------------------------

The backpack uses Server Assist's existing card/action system. It does not
change or patch Server Assist.

  10 item cards     item icon, localized name, amount, quality badge
  11 action buttons Prev, Next, First, Last, -10, +10, Go To Page, Search,
                    Clear Search, Deposit Held, Refresh
  Back              Server Assist's hook toolbar button

Server Assist allows 12 action buttons, so the page selection is done with the
input field and the navigation buttons instead of one button per page. This is
what makes 100+ pages possible without changing Server Assist.

Icons use Server Assist's item icon syntax, so the real game item icon is shown
for every stack:

  item:<itemName>

Item icons follow game updates automatically because the item name is stored,
not a baked icon path.


REMOVING THE OLD BACKPACK BUILD
-------------------------------

If you previously installed the 9x9 backpack build:

1. Stop the server and every client.
2. Delete Mods/ServerAssist and Mods/AA0_ServerAssistMod from the server and
   every client, then install the current Server Assist folder.
3. Delete any old Mods/DynamicBackpack client folder.
4. Replace oxide/plugins/DynamicBackpack.cs with this version.
5. Start the server. The old dynamicBackpack window group is gone, so no stale
   XUi window will open.


TROUBLESHOOTING
---------------

The Backpack card is not on the Server Assist home board.
  The player has no backpack page permission. Grant backpack.1 or higher.
  Also confirm oxide/plugins/DynamicBackpack.cs loaded successfully and the log
  shows the panel registration line.

The Backpack card opens but all pages are locked.
  The player has no backpack.N permission. Check /backpack info.

I granted backpack.100 but the player only has 10 pages.
  MaxPages is probably still 10 in oxide/config/DynamicBackpack.json. Set it to
  100 (or higher), reload the plugin, then grant/reload the permission.

The icon is a hammer instead of the backpack.
  The ServerAssistArt UI atlas is missing on the client, the sprite name is
  wrong, or the Server Assist build does not expose SetPanelIcon. The plugin
  logs a warning when the icon cannot be applied.

The page or search input does not appear.
  The Server Assist build must support the view input line. Use Server Assist
  2.0.0 or newer.

"Server Assist is required to open the backpack."
  The ServerAssist plugin is not loaded, or the client mod is missing. Install
  the Server Assist plugin and the AA0_ServerAssistMod folder.

"The Server Assist client mod is not installed or has not sent its hello yet."
  Server Assist is not installed on the client, or the client is still loading.

A search returns no items although items are stored.
  Search only covers unlocked pages. Check that the item page is unlocked and
  that the query matches the localized item name or internal item name.

The window is too tall/small for the display.
  The window is defined by Server Assist, not by this plugin. Adjust the Server
  Assist XUi window settings if needed.
