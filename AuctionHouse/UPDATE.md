# ServerAssist Integration Notes for AuctionHouse

Status: ServerAssist 2.0.0 and newer already provide the provider API used by
AuctionHouse. **No ServerAssist source change is required.** This document records
the exact calls, protocol fields and icon keys AuctionHouse uses so both plugins
stay in sync.

ServerAssist 2.0.1 in this workspace implements all of it.

---

## 1. What AuctionHouse calls

AuctionHouse uses the public ServerAssist provider methods. They are marked with
`[HookMethod]`, so `Plugin.Call` reaches them without reflection.

```csharp
object RegisterGuiProvider(
    string providerId,
    string pluginName,
    string handlerMethod,
    string title,
    string tab1,
    string tab2,
    string tab3);

bool SetProviderIcon(string providerId, string icon);
bool OpenGuiProvider(string playerId, string providerId, string payload);
bool UnregisterGuiProvider(string providerId);
```

The exact registration AuctionHouse performs:

```csharp
Plugin serverAssist = plugins.Find("ServerAssist");

serverAssist.Call(
    "RegisterGuiProvider",
    "auctionhouse",
    "AuctionHouse",
    "HandleAuctionGuiRequest",
    "Auction House",
    "Browse",
    "My Listings",
    "Sell");

serverAssist.Call(
    "SetProviderIcon",
    "auctionhouse",
    "sprite:sa_auction_house");
```

Opening the UI:

```csharp
serverAssist.Call(
    "OpenGuiProvider",
    player.Id,
    "auctionhouse",
    "a|open");
```

Unloading:

```csharp
serverAssist.Call("UnregisterGuiProvider", "auctionhouse");
```

Registered providers appear on the Server Assist home board as `[GUI] Auction
House` with the `sprite:sa_auction_house` icon and a `PROVIDER` badge. Clicking
the card opens the tabbed provider screen. `/auction` also opens it directly.

The registration checks `GetProviderHomeSupport` first:

- `true` — register the tabbed provider and set its icon.
- missing/false — register a normal home panel with the same id, label, icon and
  permission. The panel opens in hook mode; the top tabs are replaced by the
  Browse / My Listings / Sell action buttons.

---

## 2. Lifecycle

AuctionHouse registers in all of these paths:

- `Init()` (a short delayed timer)
- `OnServerInitialized()`
- The ServerAssist `OnServerAssistReady` hook
- `OnPluginLoaded` when ServerAssist loads
- `/auction reload`

It unregisters in `Unload()`. Registration is idempotent because ServerAssist
replaces the provider with the same id.

`OnServerAssistReady` is the normal recovery path after ServerAssist is reloaded:

```csharp
private void OnServerAssistReady()
{
    timer.Once(0.5f, TryRegisterServerAssist);
}
```

---

## 3. Provider handler

ServerAssist resolves the handler by reflection and requires exactly:

```csharp
public string HandleAuctionGuiRequest(string playerId, string payload)
```

ServerAssist calls it with:

| Payload | Meaning |
| --- | --- |
| `a\|open` | Player entered the provider. |
| `a\|<action>` | A row card or action button was pressed. |
| `t\|<encoded text>` | The player submitted the text input. |

AuctionHouse validates `auctionhouse.use` on every request. The ServerAssist
provider permission is only for showing/opening the card.

---

## 4. Response protocol used by AuctionHouse

AuctionHouse returns the ServerAssist v2 line protocol:

```text
v2
protocol=2
title=Auction House
tab=home|shared|mine
tab1label=Browse
tab2label=My Listings
tab3label=Sell
subtitle=...
status=...
background=sprite:sa_auction_house
banner=sprite:...
input=<label>|<visible 0/1>|<value>
row=<index>|<action>|<label>|<sub>|<enabled>|<selected>|<icon>|<badge>|<tint>
btn=<index>|<action>|<label>|<enabled>|<tooltip>|<icon>|<tint>
```

Limits:

- 10 rows (`row=0` through `row=9`)
- 12 action buttons (`btn=0` through `btn=11`)
- One text input

The ServerAssist 2.0 client accepts the two optional `btn` icon/tint fields.
AuctionHouse uses them for the Refresh, Browse, My Listings, Sell and SA Home
buttons. Older clients that only read the first five fields ignore the extras.

The three tabs map as:

| ServerAssist tab slot | AuctionHouse screen |
| --- | --- |
| `home` | Browse |
| `shared` | My Listings |
| `mine` | Sell |

The `SA Home` button sends `a|home`, which ServerAssist consumes to leave the
provider and return to its own board. The `Close` button is handled locally by
the ServerAssist client.

---

## 5. Icon map

AuctionHouse uses the Server Assist art pack (AAZ_ServerAssistArt). The provider
card icon is set with `SetProviderIcon`; row cards carry their own `icon` field.

| UI element | Icon key | Art subject |
| --- | --- | --- |
| Provider card, background, detail banner | `sprite:sa_auction_house` | gavel, crate and tag |
| Browse banner | `sprite:sa_nav_items` | open supply crate |
| My Listings banner | `sprite:sa_nav_logs` | logbook and magnifier |
| Sell banner | `sprite:sa_act_give` | hand offering a parcel |
| Item rows and held item | `item:<itemShortName>` | real game item art |
| Price, fee, tax, balance, Buy Now | `sprite:sa_admin_shop` | cash register and tokens |
| Quantity, backpack, Set Quantity | `sprite:sa_dynamic_backpack` | survival backpack |
| Duration and expiry | `sprite:sa_act_time` | alarm clock |
| Cancel Listing | `sprite:sa_act_ban` | gavel and prohibition stamp |
| Expired or broken state | `sprite:sa_act_warn` | warning triangle |
| Seller | `sprite:sa_nav_players` | survivor dog tags |
| Refresh button | `sprite:sa_plugin_hot_reload` | chip and refresh arrows |
| SA Home button | `sprite:sa_nav_server` | server rack |
| Browse button | `sprite:sa_nav_items` | open supply crate |
| My Listings button | `sprite:sa_nav_logs` | logbook and magnifier |
| Sell button | `sprite:sa_act_give` | hand offering a parcel |

Badges used on cards:

```text
YOURS    the listing belongs to the viewer
SOON     expires within one hour
BUY      a purchase can be made
HELD     the item currently in the selected toolbelt slot
x<N>     stack count
<S>      listing state: ACTIVE, SOLD, EXPIRED, CANCELLED, BROKEN
```

The art pack is client-side. Copy `UIAtlases/UIAtlas` from
`AAZ_ServerAssistArt-1.1.0.zip` into the Server Assist mod folder on the server
and on every client. The game bakes the PNGs into the global UI atlas at launch.
If a sprite is missing, ServerAssist falls back to its default hammer sprite.

### Pending art request

Four AuctionHouse-specific icons are requested but not yet rendered:

- `sa_auction_browse`
- `sa_auction_mine`
- `sa_auction_sell`
- `sa_auction_cancel`

Until they arrive, the UI uses the existing `sa_nav_items`, `sa_nav_logs`,
`sa_act_give` and `sa_act_ban` sprites. The work order is
`ServerAssistArt/REQUESTS_AUCTIONHOUSE.md`; no other icon batches are requested.

---

## 6. Permission behavior

- `auctionhouse.use` is checked by AuctionHouse for every provider request.
- `auctionhouse.sell` is checked for listing and cancelling.
- `auctionhouse.admin` is checked for reload, expiry, remove and status.
- ServerAssist routes provider requests before its own `serverassist.use` check,
  so a player can use the auction with only `auctionhouse.use`.
- The ServerAssist provider registration itself does not grant any permission.

---

## 7. Fallback behavior

- If ServerAssist is missing or older than 2.0.0, registration returns false.
- AuctionHouse logs one warning and keeps every chat command working.
- `/auction`, `/auction browse`, `/auction sell`, `/auction buy`, `/auction
  cancel`, `/auction list`, `/auction fee` and the admin commands do not need the
  ServerAssist client mod.

---

## 8. Verification checklist

1. Install ServerAssist 2.0.0+, AuctionHouse and the art pack on server + client.
2. Run `/auction status`; it should report the provider as registered.
3. Run `/auction`; the Server Assist window should show Auction House with the
   Browse tab and the gavel/crate card icon.
4. List an item from the Sell tab; the held-item card should show the real item
   icon and the HELD badge.
5. Open Browse; listing cards should show real item icons and YOURS / SOON /
   xN badges.
6. Open a listing; the Buy Now card should use the cash-register icon.
7. Open My Listings; the Cancel Listing card should use the prohibition-gavel
   icon.
8. Click SA Home; Server Assist should return to its own board.
9. Unload AuctionHouse; the provider should disappear and the chat fallback
   should still work.
10. If all cards show a hammer, the client is missing the art pack.
