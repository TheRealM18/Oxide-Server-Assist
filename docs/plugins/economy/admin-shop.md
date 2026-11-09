# Admin Shop

> **Version:** 1.2.0 · **Type:** Server-side Oxide GUI hook (no client DLL, no XUi) · **Source:** [AdminShop.cs](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/AdminShop/oxide/plugins/AdminShop.cs) · [README](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/AdminShop/README.txt)

Admin Shop is a server-side Oxide plugin whose entire interface is rendered through the Server Assist client window. It sells valued game items for dukes and can also sell configured permission products either permanently or as a temporary rental. It registers as a **GUI hook**, not a provider, so it appears as a normal `[GUI] Admin Shop` card on the Server Assist Home board.

---

## Overview

The shop has two product types:

- **Items** — any item class with a trader economic value and a computed unit price at or above `MinItemPrice`. The price is derived from the vanilla trader buy price and multiplied by a configurable amount (`PriceMultiplier`, 10 by default).
- **Permission products** — explicitly configured bundles of registered Oxide permission nodes. Each product can be sold as a permanent grant at an exact duke price and as a temporary rental at its own price and duration.

Payment is taken in **Duke's Casino Tokens** (`casinoCoin` by default). Item prices intentionally carry a high convenience markup, while permission products let an admin sell access to a perk, command or node bundle without editing Oxide permissions by hand.

Purchases always go into the buyer's own inventory, and permission purchases always grant the permission directly to the buyer's Oxide user record. A player can buy an item and hand it to someone else, but permission products are intended for the buyer's own account.

!!! note
    Admin Shop does not ship a client DLL or its own XUi window. Clients only need the normal Server Assist client mod. Admin Shop itself is installed only on the server.

### What players see

The Home screen shows item category cards, item counts and, when enabled products exist, a **Permissions** button. Category and search screens show ten items per page with price, max stack, quality and bundle size. Item detail adds trader value, current dukes, affordable count and **Buy 1 / Buy 10 / Buy 100 / Custom Qty**.

The Permissions screens split products into **Permanent Permissions** and **Temporary Permissions**. Permanent rows show the permanent price and owned state; temporary rows show the rental price, duration and active state. Temporary detail screens add an expiry and a **Rent** or **Renew** button. Admins with `adminshop.admin` also see a **Manage Prices** button on the permission categories screen.

---

## Requirements

Server:

- `Mods/AA0_ServerAssistMod` folder containing `ServerAssistMod.dll`, `ModInfo.xml` and `Config`
- `oxide/plugins/ServerAssist.cs`
- `oxide/plugins/AdminShop.cs`

Every client that should see the shop:

- `Mods/AA0_ServerAssistMod` folder

For the custom card and button icons:

- `ServerAssistArt` `UIAtlases/UIAtlas` on the server and every client, or the separate `AAZ_ServerAssistArt` atlas mod

Server Assist **2.1.0 or newer is required** for the GUI hook API, icon fields and the current `RegisterGuiHook` / `SetPanelIcon` integration. EAC must be off, and the server and clients must use the same Server Assist files.

`PermissionGui` is optional. If it is installed, `adminshop.use`, `adminshop.admin` and any permission products you grant appear in PermissionGui's permission lists.

---

## Installation

1. Stop the server.
2. Make sure Server Assist is installed on the server and on each client under `Mods/AA0_ServerAssistMod`.
3. Copy `oxide/plugins/AdminShop.cs` into the server's `oxide/plugins` folder. Oxide compiles it on startup.
4. Start the server.
5. Join with a client, press **F7**, then open the **Admin Shop** button on the Server Assist Home screen. You can also type `/shop` in chat.

!!! warning
    Do not put `AdminShop.cs` in the game's `Mods` folder. The mod loader does not compile `.cs` files.

On first load the plugin creates `oxide/config/AdminShop.json` and registers:

| Node | Default grant |
|---|---|
| `adminshop.use` | Oxide default player group (controlled by `GrantUseToDefaultGroup`); also granted to the Oxide administrators group |
| `adminshop.admin` | Oxide administrators group |

The plugin writes `oxide/data/AdminShop/permissions.json` when temporary grant data is saved. The file may not exist until the first save or temporary purchase.

---

## Commands

All `/adminshop` subcommands first pass the shop access check, which accepts `adminshop.use` or `adminshop.admin`. Admin-only subcommands then require `adminshop.admin`.

| Command | Permission | Behaviour |
|---|---|---|
| `/shop` | `adminshop.use` or `adminshop.admin` | Open the Admin Shop through Server Assist |
| `/adminshop` | `adminshop.use` or `adminshop.admin` | Same as `/shop` |
| `/adminshop open` | `adminshop.use` or `adminshop.admin` | Explicitly open the shop |
| `/adminshop reload` | `adminshop.admin` | Reload config, rebuild the item catalog from current game data, reload permission products and re-register the Server Assist hook. The reply reports item count, items skipped by `MinItemPrice` and permission product count |
| `/adminshop price <item name>` | `adminshop.admin` | Print unit price, trader value, bundle size and max stack for an item |
| `/adminshop catalog` | `adminshop.admin` | Print catalog size, how many items were skipped by `MinItemPrice`, permission product count and configured currency |
| `/adminshop perms` | `adminshop.admin` | List permission products with id, permanent price, temporary price/duration, enabled state, node registration state and permission nodes. Also reports active temporary grants |
| `/adminshop permissions` | `adminshop.admin` | Alias for `/adminshop perms` |
| `/adminshop help` | `adminshop.use` or `adminshop.admin` | Show command help |

Server console cannot open the GUI. If the player's client does not have the Server Assist client mod, `/shop` replies in chat instead of opening the window:

```text
The Server Assist client mod is not installed or has not sent its hello yet.
```

---

## Permissions

| Permission | Default | Purpose |
|---|---|---|
| `adminshop.use` | Default player group when `GrantUseToDefaultGroup` is `true`; also granted to administrators | Open the hook, buy items and buy permission products |
| `adminshop.admin` | Administrators group | Run `/adminshop reload`, `price`, `catalog` and `perms`; also satisfies the shop access check and unlocks **Manage Prices** |

`adminshop.admin` is treated as full shop access by the plugin, so administrators can open the shop even if `adminshop.use` was not granted separately.

Manual grant examples:

```text
oxide.grant group players adminshop.use
oxide.grant user 76561198000000000 adminshop.use
oxide.grant user 76561198000000000 adminshop.admin
```

If PermissionGui is installed, both nodes appear in its permission lists and can be granted there.

---

## Configuration

File: `oxide/config/AdminShop.json`

| Key | Default | Notes |
|---|---|---|
| `ChatPrefix` | `[AdminShop]` | Chat reply prefix; blank means no prefix |
| `PriceMultiplier` | `10.0` | Multiplies the trader-derived item price; invalid or non-positive values reset to `10` |
| `UseTraderBuyMarkup` | `true` | Apply the game's trader buy markup |
| `UseTraderQualityMod` | `true` | Apply the trader quality modifier to items that have quality |
| `ItemQuality` | `6` | Quality given to items that have quality; valid range `1`–`6`, out-of-range values reset to `6` |
| `CurrencyItem` | `casinoCoin` | Internal item name used as money |
| `MaxQuantityPerPurchase` | `1000` | Caps one item purchase; clamped to `1`–`1000000` |
| `MinItemPrice` | `500` | Skip items whose computed unit price is below this value; `0` lists every item with an economic value. Negative values reset to `0` |
| `TemporaryPermissionHours` | `24` | Default temporary rental duration in hours; clamped to `1`–`8760`. Non-positive values reset to `24` |
| `IncludeBlocks` | `true` | Include placeable block items |
| `IncludeCreativeItems` | `false` | Include dev/console/test-only items |
| `IncludeHiddenItems` | `false` | Include items marked hidden |
| `MinEconomicValue` | `0.0` | Items must have an economic value above this to be listed; invalid values reset to `0` |
| `GrantUseToDefaultGroup` | `true` | Grant `adminshop.use` to the default player group on startup |
| `LogPurchases` | `true` | Write item and permission purchases to the server log |
| `HookLabel` | `Admin Shop` | Label on the Server Assist Home card; blank resets to the default |
| `HookDescription` | `Buy items and permissions with dukes.` | Description shown on the Home card; blank resets to the default |
| `HookPermission` | `adminshop.use` | Permission Server Assist checks when it builds the Home screen |
| `PermissionProducts` | `[]` | List of permission products; empty by default |

Default config:

```json
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
```

!!! warning
    `HookPermission` is checked by Server Assist when it builds the Home screen, but Admin Shop independently checks `adminshop.use` or `adminshop.admin` on every request. Changing `HookPermission` does not remove that second check.

After editing the config, run `/adminshop reload` or `oxide.reload AdminShop`.

### Permission product fields

`PermissionProducts` is a list. The order in the config is the order shown to players. Example:

```json
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
```

| Field | Meaning |
|---|---|
| `Id` | Optional stable id. If blank, generated from `Name`; letters, numbers, spaces, `_`, `-` and `.` are converted to a lowercase id, duplicates get a numeric suffix, and an empty result becomes `permission` |
| `Name` | Display name on the Permissions screens |
| `Description` | Optional description on the detail screen |
| `Permission` | One permission node; can be blank when `Permissions` is used |
| `Permissions` | Optional list of additional permission nodes; duplicates are removed |
| `Price` | Exact permanent duke price; `0` makes the permanent grant free |
| `TemporaryPrice` | Exact temporary duke price; `0` (default) uses one tenth of `Price`, rounded down and never below `1`. A free product also has a free temporary price |
| `TemporaryHours` | Temporary duration in hours, `1`–`8760`; `0` (default) uses the global `TemporaryPermissionHours` |
| `Enabled` | `false` hides the product from players; `/adminshop perms` still lists it |

If both `Permission` and `Permissions` are set, all nodes are used. A product priced at `5000` always costs `5000` dukes permanently; `PriceMultiplier` does not apply to permission products.

Permission nodes must already be registered by a loaded plugin. Admin Shop does not register arbitrary nodes because doing so can take ownership away from the plugin that owns the permission. A product that references an unregistered node appears unavailable and cannot be bought. Install the plugin that owns the node, or use an existing registered node.

!!! warning
    Wildcard nodes such as `*` or `plugin.*` are refused. A product can grant any registered node, including `adminshop.admin` or another plugin's admin node, so only list nodes you are comfortable selling.

### Permission pricing

- **Permanent price** is the product's `Price`. It is exact and is not affected by `PriceMultiplier`.
- **Temporary price** is `TemporaryPrice` when set above `0`. Otherwise it is `Price / 10`, rounded down, with a minimum of `1` duke. If `Price` is `0`, the temporary price is also `0`.
- **Temporary duration** is `TemporaryHours` when set above `0`. Otherwise it is the global `TemporaryPermissionHours` (24 hours by default). Effective durations are clamped to `1`–`8760` hours.

If the player already owns every node permanently, both buy buttons are disabled. If the player owns only some nodes in a bundle, the product can still be bought at the full price and only the missing nodes are granted. If any node cannot be granted, the nodes already granted by that purchase are revoked and the dukes are refunded.

A permanent purchase grants the missing nodes permanently and removes any temporary grant records for the product's nodes, so a later temporary expiry cannot revoke the permanent grant.

### Temporary permission rentals

Temporary purchases grant the same nodes as normal direct Oxide user permissions, but the plugin also records an expiry in `oxide/data/AdminShop/permissions.json`. A timer checks expiries every minute; when an expiry passes, Admin Shop revokes the direct permission automatically and removes the record.

- Buying the same temporary product again while it is active **extends the existing expiry** by the product duration instead of creating a duplicate grant.
- Resetting a node to permanent through the shop clears the temporary record for that node.
- A node that only has a temporary record is treated as temporary-only, so the player can still buy the permanent product to upgrade.
- If an admin grants the same node permanently outside the shop, the existing temporary record is not cleared, and a later expiry can still revoke the direct permission. Buy the permanent product through the shop, or re-grant the node after expiry.

### Admin Manage Prices

Admins with `adminshop.admin` see a **Manage Prices** button on the permission categories screen. The management list includes every configured product, including disabled ones, ten per page.

Selecting a product opens an edit screen with:

| Action | Behaviour |
|---|---|
| Set Permanent Price | Accepts a whole duke amount of `0` or more |
| Set Temporary Price | Accepts a whole duke amount of `0` or more; `0` restores automatic one-tenth pricing |
| Set Temporary Duration | Accepts `0` for the global default or `1`–`8760` hours; values above `8760` are clamped |
| Reset Temporary Price To Auto | Clears `TemporaryPrice` so the product uses one-tenth pricing |
| Enable Product / Disable Product | Toggles the product's `Enabled` state |

Every change is written to `oxide/config/AdminShop.json` immediately and takes effect without a reload. Disabled products are hidden from players, but admins still see the **Permissions** button when products exist so they can re-enable them from **Manage Prices**.

---

## Item pricing

The default item price is:

```text
economic value per bundle / bundle size
x trader buy markup (normally 3x)
x quality modifier (quality 6 is normally 2x)
x PriceMultiplier (10)
```

For most items this is about 30x the item's base economic value, and 10x what a vanilla trader would charge for the same quality. That is intentional: the shop is a convenience and a dukes sink, not a cheaper trader.

Prices use the game's trader buy markup and quality modifiers. Sandbox trader price multipliers are **not** applied; adjust `PriceMultiplier` if your server uses a custom sandbox trader economy.

To charge exactly 10x the item's base economic value:

```json
"UseTraderBuyMarkup": false,
"UseTraderQualityMod": false
```

To change the convenience tax, change `PriceMultiplier`, for example:

```json
"PriceMultiplier": 5
```

All item prices are recalculated on the server. The client never sends a price or a currency amount.

### Minimum item price

`MinItemPrice` keeps ultra-cheap entries out of the shop. After an item's unit price is computed, any item below `MinItemPrice` is skipped. The default is **500 dukes**, which removes near-free junk that does not fit the "normal items at heavily inflated rates" goal.

```json
"MinItemPrice": 500
```

Set it to `0` to list every item with an economic value. The number of skipped items is written to the server log when the catalog is built and is also reported by `/adminshop catalog` and `/adminshop reload`. Run `/adminshop reload` after changing it.

### What is for sale

The item catalog includes every item class with an economic value above `MinEconomicValue` and a computed unit price of at least `MinItemPrice`. By default that includes weapons, ammo, armor, mods, food, medical items, resources, books, schematics, vehicles and block items. The catalog is built from the live `ItemClass` list after the world starts, so items added by other mods appear automatically when they have an economic value.

Excluded by default:

- Quest items
- Items marked hidden
- Creative/dev/console/test-only items
- Items with no economic value
- Items whose computed unit price is below `MinItemPrice`

The configured currency item itself is always excluded. Run `/adminshop reload` after adding or changing mod items at runtime. Categories are heuristics based on item tags; an item that does not match a category still appears under **Other** and is searchable by name.

---

## Data files

| Path | Contents |
|---|---|
| `oxide/config/AdminShop.json` | All plugin settings and permission products |
| `oxide/data/AdminShop/permissions.json` | Temporary permission grants and their expiry timestamps |

The temporary data file stores one record per player and permission node with the player id, node, product id and expiry time. It is written when a temporary grant is added, renewed or expired, on server save, and when the plugin unloads.

Admin Shop keeps no other persistent player data. Item prices are recomputed from live game data. Permission purchases are normal Oxide user permissions and are saved immediately with `permission.SaveData()`. PermissionGui sees product grants because they are normal user permissions; no server restart is needed after a purchase.

Purchases are written to the server log when `LogPurchases` is `true`.

---

## Server Assist integration

Admin Shop registers a GUI hook through the Server Assist Oxide plugin:

```csharp
ServerAssist.RegisterGuiHook(
    "adminshop",
    HookLabel,
    HookDescription,
    HookPermission,
    "AdminShop",
    "HandleGuiRequest");

ServerAssist.SetPanelIcon("adminshop", "sprite:sa_admin_shop");
```

The icon is set only after the hook registration succeeds. `/shop` opens the hook with `ServerAssist.OpenHookForPlayer(playerId, "adminshop", subAction)`.

| Integration field | Value |
|---|---|
| Integration model | GUI hook (`RegisterGuiHook`), not a provider |
| Hook id | `adminshop` |
| Label | `Admin Shop` (`HookLabel`) |
| Description | `Buy items and permissions with dukes.` (`HookDescription`) |
| Permission | `adminshop.use` (`HookPermission`), checked by Server Assist for the Home card |
| Icon sprite | `sprite:sa_admin_shop`, set with `SetPanelIcon` |
| Order | No order config key; Server Assist assigns the hook order internally |
| Handler method | `public string HandleGuiRequest(string playerId, string payload)` |
| UI limits | 10 rows and 10 action buttons per page, plus an optional text input |
| Home row | `[GUI] Admin Shop` with the `PLUGIN` badge |
| Requirement | Server Assist 2.1.0 or newer |
| Registration lifecycle | `Init` (direct call and timers), `OnServerInitialized`, `OnServerAssistReady`; unregistered in `Unload` |

!!! note
    Server Assist 2.1 sorts the Home board by favorite then label, so the internally assigned hook order does not pin Admin Shop to a fixed position. Use favorites on the board if you want it at the top.

### Hook behaviour

Sidebar tabs are hidden; the shop renders its own screens inside the hook chrome. Server Assist injects a Back button that returns to the Home screen, appends `chrome=hook` and `tab=hook`, checks `HookPermission` before routing, and Admin Shop checks shop access again on every request.

The handler receives `a|...` action payloads and `t|...` text-input payloads. The player id comes from Server Assist's server-side net-package sender, not from anything the client can spoof. Item ids, quantities, prices, permission product ids, permission nodes, temporary durations and currency checks are all validated again on the server.

### Fallback behaviour

- If Server Assist is not loaded, `TryRegisterHook` returns without registering a card.
- If Server Assist is loaded but the client is not capable, `/shop` replies with the "client mod is not installed or has not sent its hello yet" chat message.
- Admin Shop has no chat-only purchase fallback; the GUI hook is the only shop interface.
- If Server Assist is reloaded, `OnServerAssistReady` re-registers the hook automatically.
- When Admin Shop is unloaded, it calls `UnregisterGuiHook("adminshop")` and saves temporary grant data.

### Icons

The card, category and action icons come from the Server Assist art pack (`sprite:sa_admin_shop`, `sprite:sa_nav_items`, `sprite:sa_permission_gui`, `sprite:sa_act_time`, `sprite:sa_act_give`, and so on). Item rows use the live game icon through `item:<internal item name>`. Permission rows use short badges such as `PERM`, `TEMP`, `BUNDLE`, `OWNED`, `ACTIVE`, `LOCKED`, `NONE` and `ERROR`; the admin management list uses `EDIT` and `OFF`.

The icons are client-side. Without the Server Assist art atlas, Server Assist falls back to its default hammer symbol; Admin Shop does not bundle the PNGs. Older Server Assist client configs may skip the small action-button icons, but card and banner icons still work.

The planned `sa_shop_categories`, `sa_shop_search`, `sa_shop_buy` and `sa_shop_permissions` sprites are not in the finished ServerAssistArt atlas yet. This build uses the closest finished icons.

---

## Typical workflows

### Buy an item

1. Run `/shop` or open Server Assist and select **Admin Shop**.
2. Pick a category, or use **Search** to filter the whole catalog by name.
3. Open an item and choose **Buy 1**, **Buy 10**, **Buy 100** or **Custom Qty**.
4. Dukes are taken from the toolbelt and backpack. The item goes to the backpack first, then the toolbelt.

If there is not enough inventory space, the purchase is refused and any dukes already removed are refunded. Partial deliveries refund the unused dukes and report the result on the status line. The buyer must be online when the purchase is processed.

### Buy a permanent permission

1. Open the shop and select **Permissions**.
2. Open **Permanent Permissions** and select a product.
3. Select **Buy Permanent**. The full permanent price is charged, dukes are taken from the toolbelt and backpack, and every missing node is granted to the buyer's Oxide user record permanently.

If the player already owns every node, the buy button is disabled. If the player owns only some nodes in a bundle, the product can still be bought at the full price and only the missing nodes are granted.

### Rent or renew a temporary permission

1. Open the shop and select **Permissions**.
2. Open **Temporary Permissions** and select a product.
3. Select **Rent** to start a rental, or **Renew** while the rental is active.

The rental price and duration are shown on the detail screen. Payment is taken from the toolbelt and backpack. Each node is granted directly to the buyer's Oxide user record and recorded in `oxide/data/AdminShop/permissions.json`; the plugin revokes it automatically when the expiry passes. Renewing adds another full duration to each node's existing expiry, or starts from the current time if that node has already expired.

### Edit permission prices (admin)

1. Open the shop with an account that has `adminshop.admin`.
2. Select **Permissions**, then **Manage Prices**.
3. Select a product and use **Set Permanent Price**, **Set Temporary Price**, **Set Temporary Duration**, **Reset Temporary Price To Auto** or **Enable/Disable Product**.
4. Changes are saved to `AdminShop.json` immediately.

### Admin maintenance

1. Edit `PermissionProducts`, `PriceMultiplier`, `UseTraderBuyMarkup`, `UseTraderQualityMod`, `MinItemPrice`, `TemporaryPermissionHours` or the include filters in `oxide/config/AdminShop.json`.
2. Run `/adminshop reload`.
3. Verify products with `/adminshop perms` and confirm their nodes are registered by a loaded plugin.
4. Check a specific item with `/adminshop price <item name>` and the catalog size plus skipped-item count with `/adminshop catalog`.

---

## Troubleshooting

| Symptom | Check |
|---|---|
| The Admin Shop button is missing | Confirm both `ServerAssist.cs` and `AdminShop.cs` compiled. Look for `[AdminShop] registered ServerAssist GUI hook 'adminshop'` in the server log. Confirm the player has `adminshop.use` or `adminshop.admin`. |
| The Permissions button is missing | There are no enabled permission products. Add at least one with `"Enabled": true` and run `/adminshop reload`. Admins with `adminshop.admin` still see the button when products exist but are all disabled. |
| Manage Prices is missing | The account does not have `adminshop.admin`. |
| A permission product says unavailable | One or more nodes is not registered by a loaded plugin, or the product uses a wildcard. Run `/adminshop perms` for the exact reason. |
| A buy button is disabled | The player owns every node permanently, is offline, cannot afford the price, or the product is disabled/unavailable. Temporary products stay buyable while active so they can be renewed. |
| `Could not grant ... No dukes were taken` | The node was registered but the grant failed. Check the server log for the Admin Shop error and confirm the permission library is loaded. The purchase is rolled back and the dukes are refunded. |
| A temporary permission expired early or was removed | Expiry records live in `oxide/data/AdminShop/permissions.json`. If an admin grants the same node permanently outside the shop, a later temporary expiry can still revoke the direct permission. Buy the permanent product through the shop or re-grant the node after expiry. |
| A rental did not extend | Renewals add the product duration to the later of the current time and the existing expiry. Confirm the purchase completed and check the expiry shown on the detail screen. |
| Cheap items are missing from the shop | Items below `MinItemPrice` (default 500 dukes) are skipped on purpose. Lower `MinItemPrice` in `AdminShop.json` and run `/adminshop reload`. `/adminshop catalog` shows how many items were skipped. |
| `GUI hook 'Admin Shop' did not return a view` | Check the server log for an Admin Shop exception. The usual cause is a catalog that has not finished loading or a missing currency item. |
| `The configured currency item ... was not found` | Check `CurrencyItem` in `AdminShop.json`. The default is `casinoCoin`. |
| The item catalog is empty | Check for `[AdminShop] catalog built` in the log. If it reports 0 items, check `MinEconomicValue`, `MinItemPrice` and the include filters, then run `/adminshop reload` after the world is fully started. Permission products still work with an empty item catalog. |
| Not enough dukes | Dukes must be in the player's toolbelt or backpack. The shop does not read trader inventories or bank balances from other mods. |
| The item is not in the shop | Only items with an economic value and a unit price of at least `MinItemPrice` are listed. Quest, hidden and creative-only items are filtered by default. Use `/adminshop price <name>` to check. |

Log location:

```text
%APPDATA%\7DaysToDie\logs\output_log_server*.txt
```

---

## Related pages

- [Server Assist product guide](../../core/server-assist.md)
- [Panels and hooks](../../integration/api/panels.md)
- [Provider API](../../integration/api/providers.md)
- [View protocol](../../integration/api/view-protocol.md)
- [Permissions](../../core/permissions.md)
