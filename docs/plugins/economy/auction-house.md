# Auction House

> **Version:** 1.1.1 · **Type:** Server-side Oxide GUI provider with panel fallback (no client DLL, no XUi) · **Source:** [AuctionHouse.cs](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/AuctionHouse/oxide/plugins/AuctionHouse.cs) · [README](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/AuctionHouse/README.txt) · [Integration notes](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/AuctionHouse/UPDATE.md)

Auction House is a server-side auction house for 7 Days to Die. Players list items from their toolbelt, browse other players' lots through the Server Assist window, and buy with a configurable currency item.

This version is a **fixed-price (buyout)** auction house. A listing has one asking price, and the first buyer who pays it gets the item. There is no bidding timer.

---

## Overview

Auction House is an Oxide plugin only. It does not ship a client DLL and does not add or change XUi files. The GUI is rendered by the existing Server Assist client mod through the Server Assist Oxide plugin.

It registers as a **GUI provider** with three tabs: **Browse** for active listings, **My Listings** for cancelling or inspecting your own listings, and **Sell** for setting a price/quantity and listing the held item. The tab labels are supplied by the provider registration, so no client edit is needed.

!!! note
    Auction House does not spawn a physical auction block or NPC. The entry points are `/auction` and the Server Assist window. Only the server needs the plugin file; players do not install Auction House. The Server Assist client mod is still required for the GUI.

---

## Requirements

- 7 Days to Die dedicated server with Oxide.
- For the GUI: **Server Assist 2.1.0 or newer** (server plugin and client mod) and the **Server Assist art pack `AAZ_ServerAssistArt` 1.1.0 or newer** on the server and on every client. The art pack supplies `sprite:sa_auction_house`, `sprite:sa_admin_shop` and the other `sa_*` sprites used by the UI.
- For chat-only use: nothing else.
- EAC must be off when client or server code mods are used.

!!! note
    Server Assist 2.1.0 and newer already implement the provider API, tab-label support and provider routing used by Auction House. No Server Assist source edit is required. If an older Server Assist is installed, Auction House still works through chat commands.

---

## Installation

1. Stop the server.
2. Copy `oxide/plugins/AuctionHouse.cs` into the server's `oxide/plugins` folder.
3. Make sure Server Assist 2.1.0+ and the `AAZ_ServerAssistArt` art pack are installed on the server and on every client.
4. Start the server.
5. Edit `oxide/config/AuctionHouse.json` if needed.
6. Type `/auction` in chat, or `/auction help`.

Copy only `oxide/plugins/AuctionHouse.cs` to the server. `UPDATE.md` documents the Server Assist provider calls and icon map; it is not installed on the server. Oxide creates these files at runtime:

```text
oxide/config/AuctionHouse.json
oxide/data/AuctionHouse/listings.json
```

If Server Assist 2.1.0+ is installed, `/auction` opens the Auction House window. Otherwise the plugin tells the player to use the chat commands.

---

## GUI layout

| Tab | Purpose |
|---|---|
| Browse | Active listings with item icon, price, seller, time left and `YOURS` / `SOON` / `x<N>` badges |
| My Listings | The player's own listings, including cancel and inspect actions |
| Sell | Held-item card, **Set Price**, optional **Set Quantity**, listing fee and **List Item** |

### Selling and buying through the GUI

To sell, put the item in the selected toolbelt slot, open `/auction` and switch to **Sell**, click **Set Price**, optionally click **Set Quantity**, then click **List Item**. The listing fee is taken from your inventory and the item is moved into escrow.

To buy, open `/auction`, click a lot to open its detail page, then click **Buy Now**. The price is taken from your inventory and the item is delivered to your backpack, toolbelt, or dropped at your feet if it cannot be placed.

The GUI uses 10 row cards and up to 12 action buttons. Listing rows carry the real game item icon through `item:<itemShortName>`; the rest of the cards use the Server Assist art pack.

---

## Commands

| Command | Permission | Behaviour |
|---|---|---|
| `/auction` | `auctionhouse.use` | Open the Auction House GUI |
| `/auction browse [page]` | `auctionhouse.use` | Open the GUI, or list active lots in chat when the GUI is unavailable |
| `/auction sell <price> [quantity]` | `auctionhouse.sell` | List the item in your selected toolbelt slot; without quantity, the whole held stack is listed. The listing fee is charged immediately. |
| `/auction mine` | `auctionhouse.use` to open; `auctionhouse.sell` for listing actions | Open My Listings, or print your active listings in chat |
| `/auction buy <id>` | `auctionhouse.use` | Buy a listing directly by its id |
| `/auction cancel <id>` | `auctionhouse.sell` | Cancel one of your own active listings; the item is returned |
| `/auction list [page]` | `auctionhouse.use` | Print active lots in chat |
| `/auction fee [price]` | `auctionhouse.sell` | Show the listing fee for the held item and, optionally, a price |
| `/auction status` | `auctionhouse.admin` | Show provider registration, currency status, active listing count, fee rule and expiry interval |
| `/auction help` | None | Show the command list |
| `/auction reload` | `auctionhouse.admin` | Reload config and listings data, restart the expiry timer and re-register the Server Assist provider |
| `/auction expire` | `auctionhouse.admin` | Run an expiry check immediately |
| `/auction remove <id>` | `auctionhouse.admin` | Remove any listing record; active listings return the item to the seller, non-active records are deleted from history |

Aliases: `/auc` and `/market`. The source also accepts `/auction open` as an alias for `/auction browse` and `/auction listings` as an alias for `/auction mine`.

---

## Permissions

| Permission | Default | Purpose |
|---|---|---|
| `auctionhouse.use` | Default players group when `GrantUseToDefaultGroup` is `true`; also granted to administrators | Browse listings, buy and open the GUI |
| `auctionhouse.sell` | Default players group when `GrantSellToDefaultGroup` is `true`; also granted to administrators | List, cancel and inspect your own listings |
| `auctionhouse.admin` | Administrators group | `/auction reload`, `/auction expire`, `/auction remove` and `/auction status` |

A player with `auctionhouse.admin` also passes the `auctionhouse.use` and `auctionhouse.sell` checks. Registration of the Server Assist provider itself does not grant any permission.

Manual grant examples:

```text
oxide.grant group players auctionhouse.use
oxide.grant group players auctionhouse.sell
oxide.grant user 76561198000000000 auctionhouse.admin
```

---

## Configuration

File: `oxide/config/AuctionHouse.json`

| Key | Default | Notes |
|---|---|---|
| `ChatPrefix` | `[Auction]` | Prefix for chat messages; use `""` for no prefix |
| `ListingFeeMode` | `Flat` | `Flat`, `Percent` or `FlatAndPercent`; invalid values fall back to `Flat` |
| `ListingFee` | `100` | Flat listing fee; clamped to `0`–`1000000000` |
| `ListingFeePercent` | `2.5` | Percentage component used by `Percent` and `FlatAndPercent`; clamped to `0`–`1000` |
| `ListingFeesByItem` | `{}` | Per-item flat-fee overrides keyed by the item's internal short name; an override replaces `ListingFee` for that item |
| `MinimumPrice` | `1` | Minimum asking price; clamped to at least `1` |
| `MaximumPrice` | `100000000` | Maximum asking price; clamped to at least `MinimumPrice` |
| `ListingDurationHours` | `24` | How long a listing stays active; clamped to `1`–`8760` |
| `MaxListingsPerPlayer` | `10` | Active listings per player; `0` means unlimited; clamped to `0`–`1000` |
| `MaxActiveListings` | `500` | Global cap on active listings; `0` means unlimited; clamped to `0`–`100000` |
| `BrowseSort` | `Newest` | `Newest`, `PriceLow`, `PriceHigh` or `Name` |
| `CurrencyItem` | `casinoCoin` | Internal item name used as money. `casinoCoin` is Duke's Casino Coin. |
| `CurrencyDisplayName` | `Casino Coins` | Name shown in the GUI and chat |
| `SalesTaxPercent` | `5.0` | Percentage removed from the seller's proceeds; clamped to `0`–`100` |
| `ExpireCheckSeconds` | `60` | How often the expiry timer runs; clamped to `10`–`3600` |
| `RefundExpiredItems` | `true` | Return the item when a listing expires; `false` destroys it |
| `RefundFeeOnCancel` | `false` | Refund the listing fee when the seller cancels |
| `AllowBuyOwnListings` | `false` | Allow a seller to buy their own listing with `/auction buy` |
| `AllowListingCurrency` | `false` | Allow the currency item itself to be listed |
| `BlockedItems` | `[]` | Internal short names or display names that cannot be listed |
| `NotifyOnSale` | `true` | Chat notice to the seller when an item sells |
| `NotifyOnExpire` | `true` | Chat notice to the seller when a listing expires |
| `BroadcastSales` | `false` | Announce every sale to all connected players |
| `GrantUseToDefaultGroup` | `true` | Grant `auctionhouse.use` to the default players group on startup |
| `GrantSellToDefaultGroup` | `true` | Grant `auctionhouse.sell` to the default players group on startup |
| `KeepHistoryEntries` | `200` | Number of sold/expired/cancelled records kept in the data file; `0` removes all history immediately; clamped to `0`–`10000` |

Delete the config and reload the plugin to regenerate the defaults.

### Fee and tax calculation

```text
flat    = ListingFeesByItem[item short name] if present, otherwise ListingFee
percent = ceil(asking price * ListingFeePercent / 100)

Flat            -> flat
Percent         -> percent
FlatAndPercent  -> flat + percent
```

The seller's proceeds are the asking price minus the sales tax:

```text
tax     = ceil(asking price * SalesTaxPercent / 100), capped at the asking price
proceeds = asking price - tax
```

The listing fee is charged when the listing is created and is not returned when the item sells. `RefundFeeOnCancel` controls the cancel case only.

---

## Data files

| Path | Contents |
|---|---|
| `oxide/config/AuctionHouse.json` | Plugin configuration |
| `oxide/data/AuctionHouse/listings.json` | `NextId`, `Listings` and `Pending` records |

`listings.json` contains:

| Field | Contents |
|---|---|
| `NextId` | Next listing id |
| `Listings` | Active and recent listing records |
| `Pending` | Offline returns and sale proceeds, keyed by player id |

Listing records store the escrowed item as a serialized item value, so quality, mods, durability, ammo and metadata are preserved. Listing states are `Active`, `Sold`, `Expired`, `Cancelled` and `Broken`.

!!! warning
    Back up `oxide/data/AuctionHouse/listings.json` together with the world save. It contains the escrowed items. Do not edit it while the server is running.

If a mod that owned a listed item is removed, the item cannot be restored. The listing is marked `Broken`, the server log records the listing id, and an admin can remove the record with `/auction remove <id>`.

---

## Server Assist integration

Auction House registers a GUI provider with Server Assist:

```csharp
serverAssist.Call(
    "RegisterGuiProvider",
    "auctionhouse",
    "AuctionHouse",
    "HandleAuctionGuiRequest",
    "Auction House",
    "Browse",
    "My Listings",
    "Sell");

serverAssist.Call("SetProviderIcon", "auctionhouse", "sprite:sa_auction_house");
```

| Integration field | Value |
|---|---|
| Integration model | GUI provider (`RegisterGuiProvider`) with a normal panel fallback |
| Provider id | `auctionhouse` |
| Title / label | `Auction House` |
| Permission | The provider registration takes no permission parameter; the handler checks `auctionhouse.use` on every request. The panel fallback registers `auctionhouse.use`. |
| Icon sprite | `sprite:sa_auction_house`, set with `SetProviderIcon` |
| Order | Provider Home entries use Server Assist's internal order `50`; the panel fallback passes order `20` to `RegisterPanel` |
| Handler method | `public string HandleAuctionGuiRequest(string playerId, string payload)` |
| Tabs | `Browse`, `My Listings`, `Sell` |
| UI limits | 10 rows and 12 action buttons per page |
| Home card | `[GUI] Auction House` with the `PROVIDER` badge when provider Home support is available |
| Registration lifecycle | `Init` delayed timers, `OnServerInitialized`, `OnServerAssistReady`, `OnPluginLoaded` when Server Assist loads, and `/auction reload`; unregistered in `Unload` |

!!! note
    Server Assist 2.1 sorts the Home board by favorite then label, so these order values do not pin Auction House to a fixed position. Use favorites on the board if you want it at the top.

### Provider behaviour

Sidebar tabs are visible and renamed to Browse / My Listings / Sell. No Back button is injected; the provider supplies an **SA Home** button, and Server Assist does not append hook chrome. Provider requests are routed before Server Assist's own `serverassist.use` check, and Auction House checks `auctionhouse.use` on every request. `/sa` returns to the board; `F7` resumes the provider.

A player who has `auctionhouse.use` can open the provider even without `serverassist.use`, because Server Assist routes provider requests before its own board permission check.

### Provider vs panel fallback

Auction House checks `GetProviderHomeSupport` before registering:

- **Support present** — registers the tabbed provider and sets its icon.
- **Support missing or `false`** — registers a normal Home panel with the same id, label, permission and icon. The card is still `[GUI] Auction House`, but the three top tabs are replaced by Browse / My Listings / Sell action buttons.
- **Provider registration returns `false`** — registers the same normal panel fallback.
- **Server Assist missing or older than 2.1.0** — registration fails cleanly, one warning is logged and every chat command remains available.

The panel fallback call is:

```csharp
serverAssist.Call(
    "RegisterPanel",
    "auctionhouse",
    "Auction House",
    "Buy and sell items in the Auction House.",
    "auctionhouse.use",
    "AuctionHouse",
    "HandleAuctionGuiRequest",
    "sprite:sa_auction_house",
    20);
```

Registration is idempotent because Server Assist replaces the provider with the same id. On unload, Auction House unregisters both the provider and the panel. If Server Assist is reloaded, the `OnServerAssistReady` hook re-registers after a short delay.

### Icons

The provider card, background and detail banner use `sprite:sa_auction_house`. Banners and actions use `sprite:sa_nav_items` (Browse), `sprite:sa_nav_logs` (My Listings), `sprite:sa_act_give` (Sell), `sprite:sa_admin_shop` (price/fee/tax/balance/Buy Now), `sprite:sa_dynamic_backpack` (quantity), `sprite:sa_act_time` (expiry), `sprite:sa_act_ban` (cancel), `sprite:sa_act_warn` (expired/broken), `sprite:sa_nav_players` (seller), `sprite:sa_plugin_hot_reload` (refresh) and `sprite:sa_nav_server` (SA Home). Listing rows and the held item use `item:<itemShortName>`.

The art pack installs the PNGs into `UIAtlases/UIAtlas`. The game bakes them into the global UI atlas at launch. If the art pack is missing, Server Assist falls back to its default hammer sprite, but every action still works.

---

## How listings work

- The item is removed from the seller's selected toolbelt slot when the listing is created. It is stored in the plugin data as a serialized item value, preserving quality, mods, durability, ammo and metadata.
- The listing fee is charged when the listing is created and is not returned when the item sells. `RefundFeeOnCancel` controls the cancel case.
- A buyer must have enough `CurrencyItem` in the backpack and toolbelt. Space is checked before payment; currency is removed first, then the item is delivered.
- On a sale, `SalesTaxPercent` is removed from the seller's proceeds. The rest is given to the seller immediately, or queued if the seller is offline.
- When a listing expires or is cancelled and the seller is offline, the item or currency is queued in the data file and delivered the next time that player connects.
- Expiry uses the server's UTC wall clock, so it continues while the server is offline. Expired listings are processed on startup.
- Listing item data uses the game's own `ItemValue` binary format plus Base64. If a mod that owned an item is removed, the item cannot be restored; the listing is marked `Broken` and the server log records the id.

---

## Typical workflows

### Sell an item

Put the item in the selected toolbelt slot, run `/auction` and use the **Sell** tab, or list it from chat:

```text
/auction sell 500
/auction sell 500 1
```

The first form lists the whole held stack; the second lists one item at 500 dukes. The fee is taken immediately and the item moves into escrow.

### Browse, buy and manage

```text
/auction
/auction browse
/auction buy 42
/auction mine
/auction cancel 42
/auction fee 500
```

Click a lot in the GUI and choose **Buy Now**, or buy by listing id from chat. `/auction mine` opens My Listings or prints your active listings. `/auction cancel` returns the item. `/auction fee` shows the fee for the held item at the supplied price.

### Admin checks and backups

```text
/auction status
/auction reload
/auction expire
/auction remove 42
```

`/auction status` reports whether the provider is registered, whether the currency item loaded, the active listing count, the fee rule and the expiry interval. Back up `oxide/data/AuctionHouse/listings.json` with the world save; it contains escrowed items and pending deliveries, so losing it loses player property.

---

## Troubleshooting

| Symptom | Check |
|---|---|
| `The configured currency item is not available on this server.` | `CurrencyItem` does not match an item loaded by the game. The default is `casinoCoin`; check the item name in `items.xml`. |
| `The listing fee is X, but you only have Y.` | The seller does not have enough currency in the backpack or toolbelt. |
| `You do not have enough free inventory space.` | Make room in the backpack or toolbelt before buying. |
| `The held item changed before it could be listed.` | The selected toolbelt slot changed during the listing flow. Select the item again and retry. |
| `ServerAssist does not expose the provider API yet.` | The server has an old Server Assist plugin. Install Server Assist 2.1.0 or newer. Chat commands still work without it. |
| `The cards all show a hammer.` | The client is missing the Server Assist art pack. Copy `AAZ_ServerAssistArt`'s `UIAtlases/UIAtlas` folder into the Server Assist mod folder on that client and restart the game. |
| The window opens but shows the Server Assist board. | The player has an active Server Assist session, or provider registration did not happen. Run `/auction reload` as an admin, then reopen `/auction`. Use **SA Home** or `/sa` to return to the board. |
| `An item could not be restored.` | The item's mod or item class no longer exists. Check the server log for the listing id and the missing item name. |
| No config file appears. | Oxide writes the config after the plugin loads. Check the server log for Auction House compile or load errors. |

Log location:

```text
%APPDATA%\7DaysToDie\logs\output_log_server*.txt
```

---

## Related pages

- [Server Assist product guide](../../core/server-assist.md)
- [Provider API](../../integration/api/providers.md)
- [View protocol](../../integration/api/view-protocol.md)
- [Handlers and lifecycle](../../integration/api/handlers.md)
- [Permissions](../../core/permissions.md)
