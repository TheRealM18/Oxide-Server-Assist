# Core companions

Part of [Companion plugin integration](index.md).

## PermissionGui

PermissionGui is the permission manager. Its GUI is a Server Assist hook; the `/perm` commands work without Server Assist.

| Item | Value |
|---|---|
| Hook id | `permissions` |
| Label | `Permission Manager` |
| Permission | `permissiongui.use` |
| Plugin name | `PermissionGui` |
| Handler | `HandleServerAssistRequest` |
| Entry | `F7` → Permission Manager, `/permgui`, `/perm gui` |
| Admin | `permissiongui.admin` **and** `oxide.*` |

### Registration

```csharp
private void RegisterServerAssistHook()
{
    Plugin serverAssist = plugins.Find("ServerAssist");
    if (serverAssist == null)
    {
        return;
    }

    serverAssist.Call(
        "RegisterGuiHook",
        "permissions",
        "Permission Manager",
        "Groups, users, tracks, defaults and a searchable permission browser.",
        "permissiongui.use",
        "PermissionGui",
        nameof(HandleServerAssistRequest));

    serverAssist.Call("SetPanelIcon", "permissions", "sprite:sa_permission_gui");
}

private void UnregisterServerAssistHook()
{
    Plugin serverAssist = plugins.Find("ServerAssist");
    if (serverAssist != null)
    {
        serverAssist.Call("UnregisterGuiHook", "permissions");
    }
}

public string HandleServerAssistRequest(string playerId, string payload)
{
    return HandleGuiRequest(playerId, payload);
}
```

### Notes

- PermissionGui no longer needs its own client mod or `permGui` window.
- Server Assist removes a legacy seeded button targeting the old standalone window on load.
- Use the `[GUI] Permission Manager` row instead.
- The permission search sees Server Assist's per-button nodes because they are registered with Oxide.

Read: [Permissions](../../core/permissions.md) and [Troubleshooting](../../reference/troubleshooting/companions.md#permission-manager-row-missing).

---

## AdminShop

AdminShop sells items and permission products for dukes.

| Item | Value |
|---|---|
| Hook id | `adminshop` |
| Label | `Admin Shop` (config `HookLabel`) |
| Permission | `adminshop.use` (config `HookPermission`) |
| Plugin name | `AdminShop` |
| Handler | `HandleGuiRequest` |
| Entry | `/shop`, Home row |
| Admin | `adminshop.admin` |

### Registration

```csharp
Plugin serverAssist = plugins.Find("ServerAssist");
if (serverAssist == null)
{
    return;
}

serverAssist.Call(
    "RegisterGuiHook",
    "adminshop",
    config.HookLabel,
    config.HookDescription,
    config.HookPermission,
    "AdminShop",
    nameof(HandleGuiRequest));

serverAssist.Call("SetPanelIcon", "adminshop", "sprite:sa_admin_shop");
```

### Notes

- AdminShop validates item id, quantity, price, currency count and inventory space on the server for every request.
- The hook permission is visibility convenience, not the only security check.
- `/shop` calls `OpenHookForPlayer(playerId, "adminshop")`.
- Register in `Init`, `OnServerInitialized` and `OnServerAssistReady`; unregister in `Unload`.

---

## AuctionHouse

AuctionHouse is the reference **provider**. It keeps the three tabs and owns its own entry command.

| Item | Value |
|---|---|
| Provider id | `auctionhouse` |
| Title | `Auction House` |
| Tabs | `Browse`, `My Listings`, `Sell` |
| Plugin name | `AuctionHouse` |
| Handler | `HandleAuctionGuiRequest` |
| Entry | `/auction`, Home `PROVIDER` card |
| Icon | `sprite:sa_auction_house` |
| View | 10 rows / 12 action buttons |
| Row icons | `item:<itemShortName>` plus `x<count>` badge |
| Permissions | `auctionhouse.use`, `auctionhouse.sell`, `auctionhouse.admin` |

### Registration

```csharp
object support = ServerAssist.Call("GetProviderHomeSupport");
bool providerHomeSupported = support is bool && (bool)support;

if (providerHomeSupported)
{
    object result = ServerAssist.Call(
        "RegisterGuiProvider",
        "auctionhouse",
        Name,
        nameof(HandleAuctionGuiRequest),
        "Auction House",
        "Browse",
        "My Listings",
        "Sell");

    serverAssistGuiReady = result is bool && (bool)result;
    if (serverAssistGuiReady)
    {
        ServerAssist.Call("SetProviderIcon", "auctionhouse", IconAuction);
        ServerAssist.Call("UnregisterPanel", "auctionhouse");
    }
}
else
{
    // Older Server Assist: register a normal home panel fallback.
    object result = ServerAssist.Call(
        "RegisterPanel",
        "auctionhouse",
        "Auction House",
        "Buy and sell items in the Auction House.",
        "auctionhouse.use",
        Name,
        nameof(HandleAuctionGuiRequest),
        IconAuction,
        20);

    serverAssistGuiReady = result is bool && (bool)result;
    if (serverAssistGuiReady)
    {
        ServerAssist.Call("UnregisterGuiProvider", "auctionhouse");
    }
}
```

### Opening

```csharp
object result = ServerAssist.Call(
    "OpenGuiProvider",
    playerId,
    "auctionhouse",
    "a|open");
```

### Notes

- The provider keeps the sidebar/tab chrome visible.
- It can rename the tabs through `tab1label`, `tab2label`, `tab3label` in its view.
- On current builds it appears on Home as a `PROVIDER` card. Clicking the card sends `providerpanel|open|auctionhouse`; the provider must check `auctionhouse.use` before returning its view.
- There is no injected Back button. `/sa` returns to the board.
- If `GetProviderHomeSupport()` is false, AuctionHouse registers a normal home panel instead and opens it with `OpenPanel`.
- Provider requests are routed before `serverassist.use`. AuctionHouse must enforce `auctionhouse.use` itself.
- Register from `Init` and retry on `OnServerInitialized` / `OnServerAssistReady`; unregister in `Unload`.
- It renders the normal **10-row / 12-button** view model.
- Listing rows and the held-item preview use optional row icon fields:

  ```text
  item:<itemShortName>     real game item icon
  x<count>                 amount badge and subtitle
  ```

  Example:

  ```text
  row=0|view%7C12|AK-47|x1 / $500 / Seller / 2h|1|0|item:gunPistol|x1|
  row=1||Held item|AK-47 x1|0|0|item:gunPistol|x1|
  ```

- The same optional fields work in hook and provider rows. AuctionHouse can also use the generic [Page navigation](../api/view-protocol.md#page-navigation) pattern for listing pages, alongside its current `page|prev` / `page|next` actions.

---
