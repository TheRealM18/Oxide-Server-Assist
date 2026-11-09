AuctionHouse 1.1.1
==================

A server-side auction house for 7 Days to Die. Players list items from their
toolbelt, browse other players' lots through the ServerAssist window, and buy
with a configurable currency item. The listing fee is fully configurable.

This version is a fixed-price (buyout) auction house. A listing has one asking
price and the first buyer who pays it gets the item; there is no bidding timer.

This package is an Oxide plugin only. It does not ship a client DLL and it does
not add or change XUi files. The GUI is rendered by the existing ServerAssist
client mod through the ServerAssist Oxide plugin.

ServerAssist 2.0.0 and newer already include the provider API, tab-label support
and provider routing used by this plugin. If an older ServerAssist is installed,
the plugin still works through chat commands.

The plugin uses the Server Assist art pack (AAZ_ServerAssistArt) for its card,
banner and row icons. The art is client-side: install the same art pack on the
server and on every client, as described in REQUIREMENTS.


FILES
-----

  oxide/plugins/AuctionHouse.cs
  README.txt
  UPDATE.md

Copy only oxide/plugins/AuctionHouse.cs to the server's oxide/plugins folder.
UPDATE.md documents the ServerAssist provider calls and the icon map; it is not
installed on the server. Oxide creates these files at runtime:

  oxide/config/AuctionHouse.json
  oxide/data/AuctionHouse/listings.json


REQUIREMENTS
------------

* 7 Days to Die dedicated server with Oxide.
* For the GUI: ServerAssist 2.0.0 or newer (server plugin and client mod) and
  the Server Assist art pack AAZ_ServerAssistArt 1.1.0 or newer on the server
  and on every client. The art pack supplies sprite:sa_auction_house,
  sprite:sa_admin_shop and the other sa_* sprites used by the UI.
* For chat-only use: nothing else.

EAC must be off when client or server code mods are used.


INSTALL
-------

1. Stop the server.
2. Copy oxide/plugins/AuctionHouse.cs into the server's oxide/plugins folder.
3. Make sure ServerAssist 2.0.0+ and the AAZ_ServerAssistArt art pack are
   installed on the server and on every client.
4. Start the server.
5. Edit oxide/config/AuctionHouse.json if needed.
6. Type /auction in chat, or /auction help.

If ServerAssist 2.0.0+ is installed, /auction opens the Auction House window.
Otherwise the plugin tells the player to use the chat commands.


GUI LAYOUT
----------

The ServerAssist window has three tabs. AuctionHouse uses them as:

  Browse      -> Browse active listings
  My Listings -> Cancel or inspect your own listings
  Sell        -> Set price/quantity and list the held item

The tabs are renamed through the provider registration, so no client edit is
needed.

Selling through the GUI:

1. Put the item in the selected toolbelt slot.
2. Open /auction and switch to the Sell tab.
3. Click Set Price and type a whole-number price.
4. Optionally click Set Quantity.
5. Click List Item. The listing fee is taken from your inventory and the item
   is moved into escrow.

Buying through the GUI:

1. Open /auction.
2. Click a lot to open its detail page.
3. Click Buy Now. The price is taken from your inventory and the item is put in
   your backpack, toolbelt, or dropped at your feet if both are full.


COMMANDS
--------

/auction
/auction browse [page]
  Open the GUI, or list active lots in chat when the GUI is unavailable.

/auction sell <price> [quantity]
  List the item in your selected toolbelt slot. Without quantity, the whole
  held stack is listed. The listing fee is charged immediately.

/auction mine
  Open My Listings, or print your active listings in chat.

/auction buy <id>
  Buy a listing directly by its id.

/auction cancel <id>
  Cancel one of your own active listings. The item is returned.

/auction list [page]
  Print active lots in chat.

/auction fee [price]
  Show the listing fee for the held item and, optionally, a price.

/auction status          (admin)
  Show whether the ServerAssist provider is registered, whether the currency item
  loaded, the active listing count, the fee rule and the expiry interval.

/auction help
  Show the command list.

/auction reload          (admin)
  Reload oxide/config/AuctionHouse.json and oxide/data/AuctionHouse/listings.json,
  restart the expiry timer and re-register the ServerAssist provider.

/auction expire          (admin)
  Run an expiry check immediately.

/auction remove <id>     (admin)
  Remove any listing record. If it is still active, the item is returned to the
  seller. Non-active records are deleted from history.

Aliases: /auc and /market.


PERMISSIONS
-----------

auctionhouse.use
  Browse listings, buy, and open the GUI. Granted to the default players group
  by default (GrantUseToDefaultGroup).

auctionhouse.sell
  List, cancel and inspect your own listings. Granted to the default players
  group by default (GrantSellToDefaultGroup).

auctionhouse.admin
  /auction reload, /auction expire and /auction remove. Granted to the default
  administrators group automatically.

A player with auctionhouse.admin also passes the use and sell checks.


CONFIGURATION
-------------

Oxide creates oxide/config/AuctionHouse.json on first start. Delete it and
reload the plugin to regenerate the defaults.

  ChatPrefix                 "[Auction]"
      Prefix for chat messages. Use "" for no prefix.

  ListingFeeMode             "Flat"
      "Flat"            charge ListingFee once per listing
      "Percent"         charge ListingFeePercent of the asking price
      "FlatAndPercent"  charge both

  ListingFee                 100
      Flat listing fee. This is the base "cost to put it up".

  ListingFeePercent          2.5
      Percentage component, used by Percent and FlatAndPercent.

  ListingFeesByItem          {}
      Per-item flat-fee overrides, keyed by the item's internal short name.
      Example:
        "ListingFeesByItem": {
          "casinoCoin": 0,
          "gunPistol": 250
        }

  MinimumPrice               1
  MaximumPrice               100000000
      Allowed asking-price range.

  ListingDurationHours       24
      How long a listing stays active before it expires.

  MaxListingsPerPlayer      10
      Active listings per player. 0 means unlimited.

  MaxActiveListings          500
      Global cap on active listings. 0 means unlimited.

  BrowseSort                 "Newest"
      "Newest", "PriceLow", "PriceHigh" or "Name".

  CurrencyItem               "casinoCoin"
      Internal item name used as money. casinoCoin is Duke's Casino Coin.

  CurrencyDisplayName        "Casino Coins"
      Name shown in the GUI and chat.

  SalesTaxPercent            5.0
      Percentage removed from the seller's proceeds. The tax is a money sink.

  ExpireCheckSeconds         60
      How often the expiry timer runs. Clamped to 10-3600.

  RefundExpiredItems         true
      Return the item when a listing expires. false destroys it.

  RefundFeeOnCancel          false
      Refund the listing fee when the seller cancels.

  AllowBuyOwnListings        false
      Allow a seller to buy their own listing with /auction buy.

  AllowListingCurrency       false
      Allow the currency item itself to be listed.

  BlockedItems               []
      Internal short names or display names that cannot be listed.

  NotifyOnSale               true
  NotifyOnExpire             true
      Chat notices for the seller.

  BroadcastSales             false
      Announce every sale to all connected players.

  GrantUseToDefaultGroup     true
  GrantSellToDefaultGroup    true
      Grant the player permissions to the default players group on startup.

  KeepHistoryEntries         200
      Number of sold/expired/cancelled records kept in the data file.
      0 removes all history immediately.


HOW LISTINGS WORK
-----------------

* The item is removed from the seller's selected toolbelt slot when the listing
  is created. It is stored in the plugin's data as a serialized item value, so
  quality, mods, durability, ammo and metadata are preserved.
* The listing fee is charged when the listing is created and is not returned
  when the item sells. RefundFeeOnCancel controls the cancel case.
* A buyer must have enough of CurrencyItem in their backpack and toolbelt.
  Currency is removed first, then the item is delivered. If the buyer has no
  space, the buy is refused before any currency is taken.
* On a sale, SalesTaxPercent is removed from the seller's proceeds. The rest is
  given to the seller immediately, or queued if the seller is offline.
* When a listing expires or is cancelled and the seller is offline, the item (or
  currency) is queued in the data file and delivered the next time that player
  connects.
* Expiry is based on the server's UTC wall clock, so it continues while the
  server is offline. Expired listings are processed on startup.
* Listing item data uses the game's own ItemValue binary format plus Base64.
  If a mod that owned an item is removed, the item cannot be restored; the
  listing is marked Broken and the server log records the id. Admins can remove
  broken records with /auction remove <id>.


DATA AND BACKUPS
----------------

  oxide/data/AuctionHouse/listings.json

contains:

  NextId          next listing id
  Listings        active and recent listing records
  Pending         offline returns and sale proceeds, by player id

Back up this file together with the world save. It contains the escrowed items.
Do not edit it while the server is running.


SERVER ASSIST GUI INTEGRATION
---------------------------

AuctionHouse registers itself as a GUI provider with ServerAssist:

  RegisterGuiProvider("auctionhouse", "AuctionHouse",
                      "HandleAuctionGuiRequest",
                      "Auction House", "Browse", "My Listings", "Sell")
  SetProviderIcon("auctionhouse", "sprite:sa_auction_house")

ServerAssist 2.0.0+ already implements this API. No ServerAssist source edit is
required. The plugin also implements the OnServerAssistReady hook, so it
re-registers automatically when ServerAssist is reloaded.

Providers are listed on the Server Assist home board as [GUI] Auction House with
the sprite:sa_auction_house icon and a PROVIDER badge. Clicking that card opens
the tabbed Auction House screen. /auction, /auc and /market open it directly as
well. The provider icon is also used for the background/banner and can be read
by other panels through GetPluginIcon.

On ServerAssist builds that do not implement provider home cards (the
GetProviderHomeSupport API is missing), AuctionHouse automatically registers a
normal home panel instead. The card is still [GUI] Auction House with the same
icon, but the three top tabs are replaced by the Browse / My Listings / Sell
action buttons.

The plugin renders the ServerAssist v2 view protocol with 10 row cards and up to
12 action buttons. Every row carries an icon key, so the cards use the icon set
from ServerAssistArt:

  sprite:sa_auction_house     provider card, background and detail banner
  sprite:sa_nav_items         Browse banner and the "no listings" card
  sprite:sa_nav_logs          My Listings banner and empty-card icon
  sprite:sa_act_give          Sell banner and List Item action card
  sprite:sa_admin_shop        price, fee, tax, balance and Buy Now cards
  sprite:sa_dynamic_backpack  held item, quantity and Set Quantity cards
  sprite:sa_act_time          duration and expiry cards
  sprite:sa_act_ban           Cancel Listing action card
  sprite:sa_act_warn          expired or broken-state cards
  sprite:sa_nav_players       seller card
  sprite:sa_plugin_hot_reload Refresh button
  sprite:sa_nav_server        SA Home button

Listing rows use the real game item icon instead of a generic sprite:

  item:<itemShortName>        real game item icon
  YOURS / SOON / x<count>     card badges

The art pack installs those PNGs into UIAtlases/UIAtlas. The game bakes them
into the global UI atlas at launch. If the art pack is missing, ServerAssist
falls back to its default hammer sprite, but every action still works.

A player who has auctionhouse.use can open the provider even without
serverassist.use, because ServerAssist routes provider requests before its own
board permission check. The AuctionHouse handler checks auctionhouse.use again
on every request.

If ServerAssist 2.0.0+ is unavailable, registration fails cleanly and the chat
commands remain available. An outdated ServerAssist provider API is logged once.


SERVER ASSIST ART PACK
----------------------

The icon keys above come from the Server Assist art pack. Install the same files
on the server and every client:

  Mods/AA0_ServerAssistMod/UIAtlases/UIAtlas/
    sa_auction_house.png
    sa_admin_shop.png
    sa_nav_items.png
    sa_nav_logs.png
    sa_act_give.png
    sa_dynamic_backpack.png
    sa_act_time.png
    sa_act_ban.png
    sa_act_warn.png
    sa_nav_players.png
    sa_plugin_hot_reload.png
    sa_nav_server.png

The ready-made package is AAZ_ServerAssistArt-1.1.0.zip. Copy its UIAtlases
folder into the Server Assist mod folder. The game bakes the PNGs into UIAtlas
at launch; there is no XML edit and no registration step.


TROUBLESHOOTING
---------------

"The configured currency item is not available on this server."
  CurrencyItem does not match an item loaded by the game. The default is
  "casinoCoin". Check the item name in items.xml.

"The listing fee is X, but you only have Y."
  The seller does not have enough currency in the backpack or toolbelt.

"You do not have enough free inventory space."
  Make room in the backpack or toolbelt before buying.

"The held item changed before it could be listed."
  The selected toolbelt slot changed during the listing flow. Select the item
  again and retry.

"ServerAssist does not expose the provider API yet."
  The server has an old ServerAssist plugin. Install ServerAssist 2.0.0 or newer.
  The chat commands still work without it.

"The cards all show a hammer."
  The client does not have the Server Assist art pack. Copy AAZ_ServerAssistArt's
  UIAtlases/UIAtlas folder into the Server Assist mod folder on that client.
  Restart the game so the atlas is baked again.

"The window opens but shows the Server Assist board."
  The player has an active ServerAssist session, or the provider registration
  did not happen. Run /auction reload as an admin, then reopen /auction. Use the
  SA Home button or /sa to return to the board.

"An item could not be restored."
  The item's mod or item class no longer exists. Check the server log for the
  listing id and the missing item name.

No config file appears
  Oxide writes the config after the plugin loads. Check the server log for
  AuctionHouse compile or load errors.


NOTES
-----

* This plugin does not modify the world, blocks, recipes, loot or XUi.
* It does not spawn a physical auction block or NPC. The entry point is the
  /auction command and the ServerAssist window.
* Only the server needs this file. Players do not install AuctionHouse.
* The ServerAssist client mod is still required for the GUI, exactly as it is
  for the normal Server Assist board.


