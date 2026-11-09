# Panels and hooks

Part of the [Server Assist API](index.md).

## Panel API

### `RegisterPanel`

```csharp
[HookMethod("RegisterPanel")]
public bool RegisterPanel(
    string id,
    string label,
    string description,
    string permissionName,
    string pluginName,
    string methodName,
    string icon,
    int order);
```

| Parameter | Rules |
|---|---|
| `id` | Lowercased, max 48 characters, stable |
| `label` | Cleaned, max 48 characters; rendered with a `[GUI] ` prefix on Home |
| `description` | Cleaned, max 160 characters |
| `permissionName` | Max 96 characters; blank means everyone who can open the board |
| `pluginName` | The Oxide plugin name passed to `plugins.Find` |
| `methodName` | Public instance method with the handler signature |
| `icon` | `item:`, `sprite:`, `atlas:`, plain sprite, or blank for the hammer |
| `order` | Sort order on Home; lower first |

Returns `true` when the panel was stored. `RegisterPanel` calls `RegisterGuiHook` internally and then applies the icon and order.

```csharp
serverAssist.Call(
    "RegisterPanel",
    "adminshop",
    "Admin Shop",
    "Buy items with dukes.",
    "adminshop.use",
    "AdminShop",
    "HandleGuiRequest",
    "ui_game_symbol_hammer",
    10);
```

The panel appears as:

```text
[GUI] Admin Shop   PLUGIN
Buy items with dukes.  |  adminshop.use
```

### `OpenPanel`

```csharp
[HookMethod("OpenPanel")]
public bool OpenPanel(string playerId, string panelId, string action);
```

Opens the panel on the client and optionally sends one action after the initial `a|open`.

```csharp
object result = serverAssist.Call(
    "OpenPanel",
    player.Id,
    "adminshop",
    "a|open");
```

Pass `""` or `null` for the normal entry screen. Returns `false` when:

- the player is offline;
- the client has not sent its capability hello;
- the client bridge cannot be reached.

An unknown panel id is reported by the board as `GUI hook not found.` on the next request.

`OpenPanel` delegates to `OpenHookForPlayerWithAction`.

### `UnregisterPanel`

```csharp
[HookMethod("UnregisterPanel")]
public bool UnregisterPanel(string panelId);
```

Removes the panel and clears any session currently inside it. Call from the companion's `Unload`.

```csharp
private void Unload()
{
    Plugin serverAssist = plugins.Find("ServerAssist");
    if (serverAssist != null)
    {
        serverAssist.Call("UnregisterPanel", "adminshop");
    }
}
```

Server Assist also removes hooks and providers whose owning plugin unloaded, as a safety net.

### `SetPanelIcon`

```csharp
[HookMethod("SetPanelIcon")]
public bool SetPanelIcon(string id, string icon);
```

Sets or replaces a panel or hook card icon after registration. Use it when the icon comes from configuration or when a companion registers with `RegisterGuiHook` for compatibility. The icon becomes the default `background` and `banner` for views that do not set their own.

```csharp
serverAssist.Call("SetPanelIcon", "adminshop", "sprite:sa_admin_shop");
```

### `GetPanelIcon` and `GetPluginIcon`

```csharp
[HookMethod("GetPanelIcon")]
public string GetPanelIcon(string id);

[HookMethod("GetPluginIcon")]
public string GetPluginIcon(string pluginName);
```

`GetPanelIcon` returns the live icon registered for a panel id. `GetPluginIcon` searches panels first, then providers, for a matching `pluginName` (case-insensitive) and returns the first icon it finds. Both return an empty string when nothing matches.

```csharp
string panelIcon = serverAssist.Call("GetPanelIcon", "adminshop") as string;
string pluginIcon = serverAssist.Call("GetPluginIcon", "AdminShop") as string;
```

TwitchIntegration uses these hooks through its `plugin:<name>` and `panel:<id>` icon references:

```text
plugin:AdminShop
panel:twitch
```

That lets a companion reuse the live card icon without duplicating the sprite key. Treat an empty result as "use your own fallback icon".

---

## GUI hook API

The hook API is the compatibility surface and the implementation behind `RegisterPanel`.

### `RegisterGuiHook`

```csharp
[HookMethod("RegisterGuiHook")]
public bool RegisterGuiHook(
    string id,
    string label,
    string description,
    string permissionName,
    string pluginName,
    string methodName);
```

Same as `RegisterPanel` without the icon and order. The default icon is `ui_game_symbol_hammer`; the order is the registration order times ten.

### `UnregisterGuiHook`

```csharp
[HookMethod("UnregisterGuiHook")]
public bool UnregisterGuiHook(string id);
```

### `OpenHookForPlayer`

```csharp
[HookMethod("OpenHookForPlayer")]
public bool OpenHookForPlayer(string playerId, string hookId);

public bool OpenHookForPlayer(string playerId, string hookId, string subAction);
```

The two-argument overload opens at the normal screen. The three-argument overload is kept for reflection-era companions and sends one provider action after entry.

### `OpenHookForPlayerWithAction`

```csharp
[HookMethod("OpenHookForPlayerWithAction")]
public bool OpenHookForPlayerWithAction(
    string playerId,
    string hookId,
    string subAction);
```

Sends:

```text
a|hook|open|<hookId>
```

and, if `subAction` is not empty, that payload immediately afterwards.

Returns `false` if the client has not sent its capability hello or the player is offline. Fall back to chat in that case.

---
