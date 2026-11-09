# Admin Shop

> **Version:** 1.1.1 · **Type:** Server-side Oxide GUI hook (no client DLL, no XUi) · **Source:** [AdminShop.cs](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/AdminShop/oxide/plugins/AdminShop.cs) · [README](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/AdminShop/README.txt)

Admin Shop is a server-side Oxide plugin whose entire interface is rendered through the Server Assist client window. It sells valued game items for dukes and can also sell configured permission products for an exact duke price. It registers as a **GUI hook**, not a provider, so it appears as a normal `[GUI] Admin Shop` card on the Server Assist Home board.

---

## Overview

The shop has two product types:

- **Items** — any item class with a trader economic value. The price is derived from the vanilla trader buy price and multiplied by a configurable amount (`PriceMultiplier`, 10 by default).
- **Permission products** — explicitly configured bundles of registered Oxide permission nodes sold at an exact duke price.

Payment is taken in **Duke's Casino Tokens** (`casinoCoin` by default). Item prices intentionally carry a high convenience markup, while permission products let an admin sell access to a perk, command or node bundle without editing Oxide permissions by hand.

Purchases always go into the buyer's own inventory, and permission purchases always grant the permission directly to the buyer's Oxide user record. A player can buy an item and hand it to someone else, but permission products are intended for the buyer's own account.

!!! note
    Admin Shop does not ship a client DLL or its own XUi window. Clients only need the normal Server Assist client mod. Admin Shop itself is installed only on the server.

### What players see

The Home screen shows item category cards, item counts and, when enabled products exist, a **Permissions** button. Category and search screens show ten items per page with price, max stack, quality and bundle size. Item detail adds trader value, current dukes, affordable count and **Buy 1 / Buy 10 / Buy 100 / Custom Qty**. The Permissions screens list up to ten products per page and show price, ownership, nodes and **Buy Permission**.

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

Server Assist **2.1.0 or newer is recommended** for the GUI hook API and icon fields. EAC must be off, and the server and clients must use the same Server Assist files.

`PermissionGui` is optional. If it is installed, `adminshop.use`, `adminshop.admin` and any permission products you grant appear in PermissionGui's permission lists.

---

## Installation

1. Stop the server.
2. Make sure Server Assist is installed on the server and on each client.
3. Copy `oxide/plugins/AdminShop.cs` into the server's `oxide/plugins` folder. Oxide compiles it on startup.
4. Start the server.
5. Join with a client, press **F7**, then open the **Admin Shop** button on the Server Assist Home screen. You can also type `/shop` in chat.

!!! warning
    Do not put `AdminShop.cs` in the game's `Mods` folder. The mod loader does not compile `.cs` files.

On first load the plugin creates `oxide/config/AdminShop.json` and registers:

| Node | Default grant |
|---|---|
| `adminshop.use` | Oxide default player group (controlled by `GrantUseToDefaultGroup`) |
| `adminshop.admin` | Oxide administrators group |

---

## Commands

All `/adminshop` subcommands first pass the `adminshop.use` check. Admin-only subcommands then require `adminshop.admin`.

| Command | Permission | Behaviour |
|---|---|---|
| `/shop` | `adminshop.use` | Open the Admin Shop through Server Assist |
| `/adminshop` | `adminshop.use` | Same as `/shop` |
| `/adminshop open` | `adminshop.use` | Explicitly open the shop |
| `/adminshop reload` | `adminshop.admin` | Reload config, rebuild the item catalog from current game data, reload permission products and re-register the Server Assist hook |
| `/adminshop price <item name>` | `adminshop.admin` | Print unit price, trader value, bundle size and max stack for an item |
| `/adminshop catalog` | `adminshop.admin` | Print catalog size, permission product count and configured currency |
| `/adminshop perms` | `adminshop.admin` | List permission products with id, price, enabled state, node registration state and permission nodes |
| `/adminshop permissions` | `adminshop.admin` | Alias for `/adminshop perms` |
| `/adminshop help` | `adminshop.use` | Show command help |

If the player's client does not have the Server Assist client mod, `/shop` replies in chat instead of opening the window:

```text
The Server Assist client mod is not installed or has not sent its hello yet.
```

---

## Permissions

| Permission | Default | Purpose |
|---|---|---|
| `adminshop.use` | Default player group when `GrantUseToDefaultGroup` is `true`; also granted to administrators | Open the hook, buy items and buy permission products |
| `adminshop.admin` | Administrators group | Run `/adminshop reload`, `price`, `catalog` and `perms` |

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
| `ItemQuality` | `6` | Quality given to items that have quality; valid range `1`–`6` |
| `CurrencyItem` | `casinoCoin` | Internal item name used as money |
| `MaxQuantityPerPurchase` | `1000` | Caps one item purchase; clamped to `1`–`1000000` |
| `IncludeBlocks` | `true` | Include placeable block items |
| `IncludeCreativeItems` | `false` | Include dev/console/test-only items |
| `IncludeHiddenItems` | `false` | Include items marked hidden |
| `MinEconomicValue` | `0.0` | Items must have an economic value above this to be listed |
| `GrantUseToDefaultGroup` | `true` | Grant `adminshop.use` to the default player group on startup |
| `LogPurchases` | `true` | Write item and permission purchases to the server log |
| `HookLabel` | `Admin Shop` | Label on the Server Assist Home card |
| `HookDescription` | `Buy items and permissions with dukes.` | Description shown on the Home card |
| `HookPermission` | `adminshop.use` | Permission Server Assist checks when it builds the Home screen |
| `PermissionProducts` | `[]` | List of permission products; empty by default |

!!! warning
    `HookPermission` is checked by Server Assist when it builds the Home screen, but Admin Shop independently checks `adminshop.use` on every request. Changing `HookPermission` does not remove that second check.

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
    "Enabled": true
  }
]
```

| Field | Meaning |
|---|---|
| `Id` | Optional stable id. If blank, generated from `Name`; letters, numbers, spaces, `_`, `-` and `.` are converted to a lowercase id, and duplicates get a numeric suffix |
| `Name` | Display name on the Permissions screen |
| `Description` | Optional description on the detail screen |
| `Permission` | One permission node; can be blank when `Permissions` is used |
| `Permissions` | Optional list of additional permission nodes; duplicates are removed |
| `Price` | Exact duke price; `0` makes the product free |
| `Enabled` | `false` hides the product from players; `/adminshop perms` still lists it |

If both `Permission` and `Permissions` are set, all nodes are used. A product priced at `5000` always costs `5000` dukes; `PriceMultiplier` does not apply to permission products. Granted nodes are permanent user grants; group permissions are not changed.

Permission nodes must already be registered by a loaded plugin. Admin Shop does not register arbitrary nodes because doing so can take ownership away from the plugin that owns the permission. A product that references an unregistered node appears unavailable and cannot be bought.

!!! warning
    Wildcard nodes such as `*` or `plugin.*` are refused. A product can grant any registered node, including `adminshop.admin` or another plugin's admin node, so only list nodes you are comfortable selling.

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

### What is for sale

The item catalog includes every item class with an economic value above `MinEconomicValue`. By default that includes weapons, ammo, armor, mods, food, medical items, resources, books, schematics, vehicles and block items. The catalog is built from the live `ItemClass` list after the world starts, so items added by other mods appear automatically when they have an economic value.

Excluded by default:

- Quest items
- Items marked hidden
- Creative/dev/console/test-only items
- Items with no economic value

The configured currency item itself is always excluded. Run `/adminshop reload` after adding or changing mod items at runtime. Categories are heuristics based on item tags; an item that does not match a category still appears under **Other** and is searchable by name.

---

## Data files

| Path | Contents |
|---|---|
| `oxide/config/AdminShop.json` | All plugin settings and permission products |

Admin Shop does **not** create an `oxide/data` file. It keeps no persistent player data; item prices are recomputed from live game data. Permission purchases are normal Oxide user permissions and are saved immediately with `permission.SaveData()`.

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

| Integration field | Value |
|---|---|
| Integration model | GUI hook (`RegisterGuiHook`), not a provider |
| Hook id | `adminshop` |
| Label | `Admin Shop` (`HookLabel`) |
| Description | `Buy items and permissions with dukes.` (`HookDescription`) |
| Permission | `adminshop.use` (`HookPermission`), checked by Server Assist for the Home card |
| Icon sprite | `sprite:sa_admin_shop`, set with `SetPanelIcon` |
| Order | Assigned by Server Assist as `guiHooks.Count * 10`; Admin Shop has no order config key |
| Handler method | `public string HandleGuiRequest(string playerId, string payload)` |
| UI limits | 10 rows and 10 action buttons per page |
| Home row | `[GUI] Admin Shop` with the `PLUGIN` badge |
| Registration lifecycle | `Init` (direct call and timers), `OnServerInitialized`, `OnServerAssistReady`; unregistered in `Unload` |

!!! note
    Server Assist 2.1 sorts the Home board by favorite then label, so the internally assigned hook order does not pin Admin Shop to a fixed position. Use favorites on the board if you want it at the top.

### Hook behaviour

Sidebar tabs are hidden; the shop renders its own screens inside the hook chrome. Server Assist injects a Back button that returns to the Home screen, appends `chrome=hook` and `tab=hook`, checks `HookPermission` before routing, and calls `adminshop.use` again on every request. `/sa` leaves the hook; `F7` resumes it.

The handler receives `a|...` action payloads and `t|...` text-input payloads. The player id comes from Server Assist's server-side net-package sender, not from anything the client can spoof. Item ids, quantities, prices, product ids, permission nodes and currency checks are all validated again on the server.

### Fallback behaviour

- If Server Assist is not loaded, `TryRegisterHook` returns without registering a card.
- If Server Assist is loaded but the client is not capable, `/shop` replies with the "client mod is not installed or has not sent its hello yet" chat message.
- Admin Shop has no chat-only purchase fallback; the GUI hook is the only shop interface.
- If Server Assist is reloaded, `OnServerAssistReady` re-registers the hook automatically.
- When Admin Shop is unloaded, it calls `UnregisterGuiHook("adminshop")`.

### Icons

The card, category and action icons come from the Server Assist art pack (`sprite:sa_admin_shop`, `sprite:sa_nav_items`, `sprite:sa_permission_gui`, `sprite:sa_act_give`, and so on). Item rows use the live game icon through `item:<internal item name>`. Permission rows use short badges such as `PERM`, `BUNDLE`, `OWNED`, `LOCKED`, `NONE` and `ERROR`.

The icons are client-side. Without the Server Assist art atlas, Server Assist falls back to its default hammer symbol; Admin Shop does not bundle the PNGs. Older Server Assist client configs may skip the small action-button icons, but card and banner icons still work.

---

## Typical workflows

### Buy an item

1. Run `/shop` or open Server Assist and select **Admin Shop**.
2. Pick a category, or use **Search** to filter the whole catalog by name.
3. Open an item and choose **Buy 1**, **Buy 10**, **Buy 100** or **Custom Qty**.
4. Dukes are taken from the toolbelt and backpack. The item goes to the backpack first, then the toolbelt.

If there is not enough inventory space, the purchase is refused and any dukes already removed are refunded. Partial deliveries refund the unused dukes and report the result on the status line. The buyer must be online when the purchase is processed.

### Buy a permission product

1. Open the shop and select **Permissions**.
2. Open a product and select **Buy Permission**.
3. The full product price is charged and every missing node is granted to the buyer's Oxide user record.

If the player already owns every node, the Buy button is disabled. If the player owns only some nodes in a bundle, the product can still be bought at the full price and only the missing nodes are granted. If any node cannot be granted, the nodes already granted by that purchase are revoked and the dukes are refunded.

### Admin maintenance

1. Edit `PermissionProducts`, `PriceMultiplier`, `UseTraderBuyMarkup`, `UseTraderQualityMod` or the include filters in `oxide/config/AdminShop.json`.
2. Run `/adminshop reload`.
3. Verify products with `/adminshop perms` and confirm their nodes are registered by a loaded plugin.
4. Check a specific item with `/adminshop price <item name>` and the catalog size with `/adminshop catalog`.

---

## Troubleshooting

| Symptom | Check |
|---|---|
| The Admin Shop button is missing | Confirm both `ServerAssist.cs` and `AdminShop.cs` compiled. Look for `[AdminShop] registered ServerAssist GUI hook 'adminshop'` in the server log. Confirm the player has `adminshop.use` or is an admin. |
| The Permissions button is missing | There are no enabled permission products. Add at least one with `"Enabled": true` and run `/adminshop reload`. |
| A permission product says unavailable | One or more nodes is not registered by a loaded plugin, or the product uses a wildcard. Run `/adminshop perms` for the exact reason. |
| Buy Permission is disabled | The player owns every node, is offline, cannot afford the price, or the product is disabled/unavailable. |
| `Could not grant ... No dukes were taken` | The node was registered but the grant failed. Check the server log for the Admin Shop error and confirm the permission library is loaded. The purchase is rolled back and the dukes are refunded. |
| `GUI hook 'Admin Shop' did not return a view` | Check the server log for an Admin Shop exception. The usual cause is a catalog that has not finished loading or a missing currency item. |
| `The configured currency item ... was not found` | Check `CurrencyItem` in `AdminShop.json`. The default is `casinoCoin`. |
| The item catalog is empty | Check for `[AdminShop] catalog built` in the log. If it reports 0 items, run `/adminshop reload` after the world is fully started and check `MinEconomicValue` and the include filters. Permission products still work with an empty item catalog. |
| Not enough dukes | Dukes must be in the player's toolbelt or backpack. The shop does not read trader inventories or bank balances from other mods. |
| The item is not in the shop | Only items with an economic value are listed. Quest, hidden and creative-only items are filtered by default. Use `/adminshop price <name>` to check. |

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
