AdminShop 1.2.0
===============

AdminShop is a server-side Oxide plugin. It does not ship a client DLL and does
not add an XUi window of its own. Its entire interface is rendered through the
ServerAssist client window, using ServerAssist's GUI hook API.

The shop lets a player buy:

  * any item that has a trader economic value, priced from the vanilla trader
    buy price and multiplied by a configurable amount (10x by default), and
  * configured permission products, either permanently or as a temporary rental.

Payment is taken in Duke's Casino Tokens (casinoCoin). Item prices keep the
"rush tax" high, while permission products let you sell access to a perk, a
command, or a bundle of nodes without editing Oxide permissions by hand.

Only items at or above MinItemPrice are listed, so the shop focuses on normal,
useful items at heavily inflated rates instead of near-free junk.

Purchases always go into the buyer's own inventory, and permission purchases
always grant the permission directly to the buyer. A rich player can help a
newer player by buying items and handing them over; permission products are
intended for the buyer's own account.


REQUIREMENTS
------------

Server:
  ServerAssist/Mods folder (ServerAssistMod.dll, ModInfo.xml and Config)
  oxide/plugins/ServerAssist.cs
  oxide/plugins/AdminShop.cs

Every client that should see the shop:
  ServerAssist/Mods folder

For the custom card and button icons:
  ServerAssistArt UIAtlases/UIAtlas on the server and every client
  (or the separate AAZ_ServerAssistArt atlas mod)

ServerAssist 2.0.1 or newer is recommended for the GUI hook API and icon fields. AdminShop itself
is server-only. Clients do not install an AdminShop mod. EAC must be off, and the
server and clients must use the same ServerAssist files.

PermissionGui is optional. If it is installed, adminshop.use, adminshop.admin and
any permission products you grant appear in PermissionGui's permission lists.


INSTALL
-------

1. Stop the server.
2. Make sure ServerAssist is installed on the server and on each client.
3. Copy oxide/plugins/AdminShop.cs into the server's oxide/plugins folder.
   Oxide compiles it on startup. Do not put AdminShop.cs in the game's Mods
   folder; the mod loader does not compile .cs files.
4. Start the server.
5. Join with a client, press F7, and open the "Admin Shop" button on the
   ServerAssist home screen. Or type /shop in chat.

On first load the plugin creates:

  oxide/config/AdminShop.json

The plugin also registers adminshop.use and adminshop.admin with Oxide.
adminshop.use is granted to Oxide's default player group by default.
adminshop.admin is granted to Oxide's administrators group.


PERMISSIONS
-----------

adminshop.use
  Open the Admin Shop hook, buy items, and buy permission products. Granted to
  the default player group by default. Set GrantUseToDefaultGroup to false if
  you want to grant it manually.

adminshop.admin
  Run /adminshop reload, price, catalog and perms. Granted to the administrators
  group automatically.

Manual grant examples:

  oxide.grant group players adminshop.use
  oxide.grant user 76561198000000000 adminshop.use
  oxide.grant user 76561198000000000 adminshop.admin

If PermissionGui is installed, adminshop.use and adminshop.admin appear in its
permission lists and can be granted there.


COMMANDS
--------

/shop
/adminshop
/adminshop open
  Open the Admin Shop through ServerAssist. If the client does not have the
  ServerAssist client mod, the command replies with a chat message instead.

/adminshop reload
  Reload config, rebuild the item catalog from the current game data, reload
  permission products, and re-register the ServerAssist hook. Admin only.

/adminshop price <item name>
  Print the unit price, trader value, bundle size, and max stack for an item.
  Admin only.

/adminshop catalog
  Print the item catalog size, how many items were skipped by MinItemPrice, the
  permission product count and the configured currency. Admin only.

/adminshop perms
/adminshop permissions
  List configured permission products with their id, permanent price, temporary
  price/duration, enabled state, node registration state, and permission nodes.
  Also reports how many temporary grants are currently active. Admin only.

/adminshop help
  Show command help.


ITEM PRICING
------------

The default item price is:

  economic value per bundle / bundle size
  x trader buy markup (normally 3x)
  x quality modifier (quality 6 is normally 2x)
  x PriceMultiplier (10)

For most items this is about 30x the item's base economic value, and 10x what a
vanilla trader would charge for the same quality. That is intentional: the shop
is a convenience and a dukes sink, not a cheaper trader.

Prices use the game's trader buy markup and quality modifiers. Sandbox trader
price multipliers are not applied; adjust PriceMultiplier if your server uses a
custom sandbox trader economy.

If you want exactly 10x the item's base economic value, set:

  "UseTraderBuyMarkup": false
  "UseTraderQualityMod": false

If you want a different convenience tax, change PriceMultiplier. For example:

  "PriceMultiplier": 5

All item prices are recalculated on the server. The client never sends a price or
a currency amount.

MINIMUM ITEM PRICE
------------------

MinItemPrice keeps ultra-cheap entries out of the shop. After an item's unit
price is computed, any item below MinItemPrice is skipped. The default is 500
dukes. This removes entries such as a 300-duke vehicle part that do not fit the
"normal items at heavily inflated rates" goal.

  "MinItemPrice": 500

Set it to 0 to list every item with an economic value. The number of skipped
items is written to the server log when the catalog is built and is also shown
by /adminshop catalog. Run /adminshop reload after changing it.


PERMISSION PRODUCT PRICING
--------------------------

A permission product has its own exact Price in dukes. PriceMultiplier does not
apply to permission products. A product priced at 5000 always costs 5000 dukes.

The shop presents two permission categories:

  Permanent Permissions
    Price is the product's Price field. Buying it grants each configured node to
    the buyer's Oxide user record permanently.

  Temporary Permissions
    Price defaults to one tenth of the permanent price, rounded down and never
    below 1 duke. A product can override that with TemporaryPrice. Buying it
    grants the same nodes as a direct Oxide permission, but records an expiry in
    oxide/data/AdminShop/permissions.json. When the expiry passes, AdminShop
    revokes the direct permission automatically.

Temporary duration defaults to TemporaryPermissionHours (24 hours). A product can
override it with TemporaryHours. Values are clamped to 1-8760 hours. Buying the
same temporary product again while it is active extends the expiry instead of
creating a duplicate grant.

If the player already has every node permanently, both buy buttons are disabled.
If the player has some nodes in a bundle but not all, the product can be bought
and only the missing nodes are granted for the full product price. Buying a
permanent product removes any temporary grant records for the same nodes so a
later expiry cannot revoke the permanent grant.

Admin price editing
  Admins with adminshop.admin see a Manage Prices button on the permission
  categories screen. Pick a product to edit its permanent price, temporary
  price, temporary duration, or enabled state. Changes are saved to
  oxide/config/AdminShop.json immediately. Setting the temporary price to 0
  restores automatic 1/10 pricing, and setting the duration to 0 restores the
  global default.


WHAT IS FOR SALE
----------------

The item catalog includes every item class that has an economic value above
MinEconomicValue and a computed unit price of at least MinItemPrice. By default
that includes weapons, ammo, armor, mods, food, medical items, resources, books,
schematics, vehicles, and block items.

The catalog is built from the live ItemClass list after the world starts, so
items added by other mods appear automatically when they have an economic value.
Run /adminshop reload after adding or changing mod items at runtime.

Excluded by default:

  - quest items
  - items marked as hidden
  - creative/dev/console/test-only items
  - items with no economic value
  - items whose computed unit price is below MinItemPrice

You can change the filters in oxide/config/AdminShop.json:

  IncludeBlocks         include placeable block items (default true)
  IncludeCreativeItems  include dev/console/test items (default false)
  IncludeHiddenItems    include hidden items (default false)
  MinEconomicValue      minimum economic value to list (default 0)
  MinItemPrice          minimum computed unit price to list (default 500)

The item categories are heuristics based on item tags. An item that does not
match a category still appears under "Other" and is always searchable by name.


PERMISSION PRODUCTS
-------------------

Permission products are configured in the PermissionProducts list in
oxide/config/AdminShop.json. They are not auto-generated from registered
permissions; you choose exactly which nodes are for sale and at what price.

Example:

  "PermissionProducts": [
    {
      "Id": "vip",
      "Name": "VIP Access",
      "Description": "Permanent VIP permission.",
      "Permission": "vip.use",
      "Permissions": [],
      "Price": 5000,
      "TemporaryPrice": 0,
      "TemporaryHours": 0,
      "Enabled": true
    },
    {
      "Id": "kit-master",
      "Name": "Master Kit",
      "Description": "Two permissions in one product.",
      "Permission": "",
      "Permissions": [ "kit.basic", "kit.advanced" ],
      "Price": 12000,
      "TemporaryPrice": 900,
      "TemporaryHours": 12,
      "Enabled": true
    }
  ]

Fields:

  Id              optional stable id. If blank, it is generated from Name.
                  Letters, numbers, spaces, '_', '-' and '.' are converted to a
                  lowercase id. Duplicate ids get a numeric suffix.
  Name            display name on the Permissions screen.
  Description     optional description shown on the detail screen.
  Permission      one permission node. Can be blank if Permissions is used.
  Permissions     optional list of additional permission nodes.
  Price           exact permanent duke price. 0 makes the permanent grant free.
  TemporaryPrice  exact temporary duke price. 0 (default) means automatic
                  one tenth of Price, rounded down, minimum 1.
  TemporaryHours  temporary duration in hours, 1-8760. 0 (default) uses the
                  global TemporaryPermissionHours.
  Enabled         false hides the product from players. Admin /adminshop perms
                  still lists it.

If both Permission and Permissions are set, all of the nodes are used. Duplicate
nodes are removed. Product order in the config is the order shown in the shop.

Permission nodes must already be registered by a loaded plugin. AdminShop does
not register arbitrary nodes because doing so can take ownership away from the
plugin that actually owns the permission. Most Oxide plugins register their own
permission nodes automatically when they load. If a product references an
unregistered node, it appears as unavailable and cannot be bought. Install the
plugin that owns the node, or use an existing registered node.

Wildcard nodes such as "*" or "plugin.*" are refused. Selling a wildcard would
grant every matching permission and is not supported.

Be careful with staff or admin permission nodes. A product can grant any
registered node, including adminshop.admin or another plugin's admin node, so
only list nodes you are comfortable selling.

PermissionGui sees granted product permissions because they are normal Oxide
user permissions. You do not need to restart the server after a purchase.


GUI
---

ServerAssist's hook chrome shows the shop title, subtitle, status line, ten rows,
ten action buttons, and an optional text input.

Home
  Item category buttons and item counts. If permission products are enabled, a
  Permissions button is also shown.

Permission categories
  Two rows: Permanent Permissions and Temporary Permissions. Each row shows how
  many nodes the player owns or how many temporary grants are active. Admins
  with adminshop.admin also see a Manage Prices button.

Permissions list
  Up to ten permission products per page. The title and prices follow the chosen
  category. Permanent rows show the permanent price and owned state; temporary
  rows show the rental price, duration, and active state. Disabled products are
  hidden from players.

Permission detail
  Shows the product name, price, duration (temporary only), current dukes,
  ownership or active state, permission nodes, product type, description, and
  product id. The buy button is enabled only when the product is enabled, the
  nodes are registered, the player can afford it, and the player is online.
  Temporary products can be renewed to extend their expiry.

Manage Prices (admin)
  Lists every permission product, including disabled ones. Pick a product to
  edit its permanent price, temporary price, temporary duration, or enabled
  state. Text prompts accept whole numbers. Changes save to the config file
  immediately.

Category / search results
  Ten items per page. Each row shows the price per item, max stack, quality, and
  bundle size. Use Search to filter the whole item catalog by name.

Item detail
  Shows unit price, trader value, quality, bundle size, your current dukes, how
  many you can afford, and the maximum quantity. Buy 1, Buy 10, Buy 100, or
  Custom Qty.

The Back button in ServerAssist's hook chrome returns to the ServerAssist home
screen. The shop's own Back and Categories buttons move around inside the shop.


ICONS
-----

AdminShop uses the hand-painted ServerAssistArt icon set for its category cards,
action buttons, permission screens and detail rows. Item rows use the live game
item icon through `item:<internal item name>`, so each item shows its real
inventory icon instead of a generic symbol.

The plugin card icon is:

  sprite:sa_admin_shop

Selected ServerAssistArt sprite map:

  Home / shop / categories       sprite:sa_admin_shop
  All Items                      sprite:sa_nav_items
  Weapons                        sprite:sa_admin_kits
  Ammo                           sprite:sa_act_airdrop
  Armor                          sprite:sa_act_godmode
  Mods                           sprite:sa_admin_suite
  Food & Medical                 sprite:sa_act_heal
  Resources                      sprite:sa_qln_pack
  Blocks                         sprite:sa_nav_world
  Books & Schematics             sprite:sa_nav_logs
  Other                          sprite:sa_nav_help
  Permissions                    sprite:sa_permission_gui
  Search                         sprite:sa_nav_logs
  Refresh                        sprite:sa_plugin_hot_reload
  Back / Prev / Next             sprite:sa_nav_teleport
  Buy / give                     sprite:sa_act_give
  Price / dukes                  sprite:sa_admin_shop
  Trader value                   sprite:sa_auction_house
  Quality                        sprite:sa_act_repair
  Bundle                         sprite:sa_qln_pack
  Warning / locked               sprite:sa_act_warn
  Owned                          sprite:sa_act_godmode

Permission rows also use short badges: PERM, TEMP, BUNDLE, OWNED, ACTIVE,
LOCKED, NONE, EDIT, OFF and ERROR. Item rows use the item's own icon and no
badge.

The planned `sa_shop_categories`, `sa_shop_search`, `sa_shop_buy` and
`sa_shop_permissions` sprites are not in the finished ServerAssistArt atlas yet.
This build uses the closest finished icons from the 34-icon set listed above.
The exact missing-icon request for the ServerAssistArt artist is in
`ServerAssistArt/REQUESTS_AdminShop.md`.

Action-button icons use the `icon` child view in ServerAssist's
`Config/XUi_InGame/windows.xml`. The source config in this workspace has been
updated to include that view for all 12 action buttons. If you deploy an older
ServerAssist client config, the card and banner icons still work; only the small
button icons are skipped.

The icons are client-side. Copy the ServerAssistArt `UIAtlases/UIAtlas` PNGs
into the Server Assist mod folder on the server and on every client, or install
the separate AAZ_ServerAssistArt atlas mod. Without the atlas, Server Assist
falls back to the default hammer symbol. AdminShop does not bundle the PNGs; it
only references the sprite keys above.


PAYMENT AND DELIVERY
--------------------

Items:

- Payment is taken from the player's toolbelt and backpack.
- The item goes to the backpack first, then the toolbelt.
- If there is not enough inventory space, the purchase is refused and any dukes
  already removed are refunded. If a partial delivery happens, the unused dukes
  are refunded and the status line reports it.
- Purchases are logged when LogPurchases is true.
- The buyer must be online when the purchase is processed.

Permissions:

- Payment is taken from the player's toolbelt and backpack.
- The permission is granted directly to the buyer's Oxide user record.
- Permanent purchases grant the nodes permanently and remove any temporary grant
  records for the same nodes.
- Temporary purchases also grant the nodes directly, but record an expiry in
  oxide/data/AdminShop/permissions.json. A timer checks expiries every minute
  and revokes expired direct permissions. Buying an active temporary product
  again extends its expiry.
- If any node in a bundle cannot be granted, the nodes already granted by that
  purchase are revoked and the dukes are refunded.
- Permission data is saved immediately with permission.SaveData(). Temporary
  grant data is saved to its own data file.
- Purchases are logged when LogPurchases is true.
- The buyer must be online when the purchase is processed.

The default currency item is casinoCoin. If a mod changes the currency, set
CurrencyItem to the new item's internal name.


CONFIG
------

Default oxide/config/AdminShop.json:

{
  "ChatPrefix": "[AdminShop]",
  "PriceMultiplier": 10.0,
  "UseTraderBuyMarkup": true,
  "UseTraderQualityMod": true,
  "ItemQuality": 6,
  "CurrencyItem": "casinoCoin",
  "MaxQuantityPerPurchase": 1000,
  "MinItemPrice": 500,
  "TemporaryPermissionHours": 24,
  "IncludeBlocks": true,
  "IncludeCreativeItems": false,
  "IncludeHiddenItems": false,
  "MinEconomicValue": 0.0,
  "GrantUseToDefaultGroup": true,
  "LogPurchases": true,
  "HookLabel": "Admin Shop",
  "HookDescription": "Buy items and permissions with dukes.",
  "HookPermission": "adminshop.use",
  "PermissionProducts": []
}

ItemQuality is the quality given to items that have quality. Valid values are 1
through 6. The price uses the same quality modifier the trader uses.

MaxQuantityPerPurchase caps one item purchase. The inventory space check still
applies after this cap.

MinItemPrice hides items whose computed unit price is below the value. Default
500. Set it to 0 to list everything.

TemporaryPermissionHours is the default duration for temporary permission
purchases. Valid values are 1 through 8760. Individual products can override it
with TemporaryHours.

HookLabel, HookDescription and HookPermission control the ServerAssist home-row
button. HookPermission is checked by ServerAssist when it builds the home screen.
It should be a valid Oxide permission node. AdminShop also checks adminshop.use
on every request.

PermissionProducts is the permission product list described above. It is empty
by default. Add products, then run /adminshop reload or oxide.reload AdminShop.

After editing the config, run /adminshop reload or oxide.reload AdminShop.


SERVER ASSIST INTEGRATION
-----------------------

AdminShop registers itself through:

  ServerAssist.RegisterGuiHook(
      "adminshop",
      HookLabel,
      HookDescription,
      HookPermission,
      "AdminShop",
      "HandleGuiRequest")

After registering, it calls:

  ServerAssist.SetPanelIcon("adminshop", "sprite:sa_admin_shop")

ServerAssist calls:

  string HandleGuiRequest(string playerId, string payload)

The player id comes from ServerAssist's server-side net-package sender, not from
anything the client can spoof. All item ids, quantities, prices, permission
product ids, permission nodes, and currency checks are validated again on the
server.

AdminShop also listens for OnServerAssistReady, so reloading ServerAssist
re-registers the shop automatically. When AdminShop is unloaded it calls
ServerAssist.UnregisterGuiHook("adminshop").

The hook API is documented in ServerAssist/update.md.


TROUBLESHOOTING
---------------

The Admin Shop button is missing from ServerAssist
  Check that oxide/plugins/ServerAssist.cs and oxide/plugins/AdminShop.cs both
  compiled. Look for "[AdminShop] registered ServerAssist GUI hook 'adminshop'"
  in the server log. Confirm the player has adminshop.use or is an admin.

The Permissions button is missing
  There are no enabled PermissionProducts. Add at least one product with
  "Enabled": true and run /adminshop reload. Admins with adminshop.admin still
  see the button when products exist but are all disabled, so they can re-enable
  them from Manage Prices.

A permission product says unavailable
  One or more of its permission nodes is not registered by a loaded plugin, or
  the product uses a wildcard. Run /adminshop perms for the exact reason. Install
  the plugin that owns the node, or use a registered node.

The permission buy button is disabled
  The player already owns every node in the product permanently, is offline,
  cannot afford the price, or the product is disabled/unavailable. Temporary
  products stay buyable while active so they can be renewed.

A temporary permission expired early or was removed
  Expiry records live in oxide/data/AdminShop/permissions.json. If an admin
  grants the same node permanently outside the shop, a later temporary expiry can
  still revoke the direct permission. Buy the permanent product through the shop
  (which clears the temporary record) or re-grant the node after expiry.

Cheap items are missing from the shop
  Items below MinItemPrice (default 500 dukes) are skipped on purpose. Lower
  MinItemPrice in AdminShop.json and run /adminshop reload. /adminshop catalog
  shows how many items were skipped.

"Could not grant ... No dukes were taken"
  The node was registered but the grant failed. Check the server log for the
  AdminShop error and confirm the permission library is loaded. The purchase is
  rolled back and the dukes are refunded.

"GUI hook 'Admin Shop' did not return a view"
  Check the server log for an [AdminShop] exception. The usual cause is an item
  catalog that has not finished loading, or a missing currency item.

"The configured currency item ... was not found"
  Check CurrencyItem in AdminShop.json. The default is casinoCoin.

The item catalog is empty
  Check the server log for "[AdminShop] catalog built". If it says 0 items, the
  item economic values may not have loaded yet, or MinItemPrice may be too high.
  Run /adminshop reload after the world is fully started. Also check
  MinEconomicValue, MinItemPrice and the include filters. Permission products
  still work when the item catalog is empty.

Not enough dukes
  Dukes must be in the player's toolbelt or backpack. The shop does not read
  trader inventories or bank balances from other mods.

The item is not in the shop
  Only items with an economic value are listed. Quest, hidden, and creative-only
  items are filtered out by default. Use /adminshop price <name> to check.

Log location
  %APPDATA%\7DaysToDie\logs\output_log_server*.txt


