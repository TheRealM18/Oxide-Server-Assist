# Providers

Part of the [Server Assist API](index.md).

## Provider API

Use a provider when the feature owns the three tabs and has its own entry command.

Current builds list every registered provider on Home as `[GUI] <title>` with the `PROVIDER` badge. The card uses the action `providerpanel|open|<providerId>` and is shown to everyone who can open the board; the provider must still check its own permission when the card is clicked. The provider view is opened with `a|open` exactly like a command entry.

### `GetProviderHomeSupport`

```csharp
[HookMethod("GetProviderHomeSupport")]
public bool GetProviderHomeSupport();
```

Returns `true` when the home board lists registered providers. A companion should call this before choosing provider mode or a normal home panel so it still gets a card on older ServerAssist builds:

```csharp
object support = serverAssist.Call("GetProviderHomeSupport");
bool providerHomeSupported = support is bool && (bool)support;
```

### `RegisterGuiProvider`

```csharp
[HookMethod("RegisterGuiProvider")]
public bool RegisterGuiProvider(
    string providerId,
    string pluginName,
    string handlerMethod,
    string title,
    string tab1,
    string tab2,
    string tab3);
```

| Parameter | Rules |
|---|---|
| `providerId` | Lowercased, max 48 |
| `pluginName` | Max 64 |
| `handlerMethod` | Max 64 |
| `title` | Max 64; defaults to `Server Assist` |
| `tab1` | Max 32; defaults to `Home` |
| `tab2` | Max 32; defaults to `Shared` |
| `tab3` | Max 32; defaults to `My Buttons` |

Returns `true` when stored. Registering the same id replaces the previous provider.

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
```

### `OpenGuiProvider`

```csharp
[HookMethod("OpenGuiProvider")]
public bool OpenGuiProvider(
    string playerId,
    string providerId,
    string payload = "a|open");
```

Sends an internal server request:

```text
a|provider|<providerId>|<encoded payload>
```

The server attaches the provider to the player's session and forwards the decoded payload. Returns `false` when the player is offline, the provider is unknown or the client is not capable.

```csharp
object result = serverAssist.Call(
    "OpenGuiProvider",
    player.Id,
    "auctionhouse",
    "a|open");
```

### `SetProviderIcon`

```csharp
[HookMethod("SetProviderIcon")]
public bool SetProviderIcon(string providerId, string icon);
```

Sets or replaces the provider card icon after registration. The icon also becomes the default `background` and `banner` for views that do not set their own. Accepts `item:`, `sprite:`, `atlas:` or a plain sprite name.

### `UnregisterGuiProvider`

```csharp
[HookMethod("UnregisterGuiProvider")]
public bool UnregisterGuiProvider(string providerId);
```

Removes the provider and clears sessions using it. Call from `Unload`.

---

## Provider mode vs hook mode

| Behaviour | Hook / panel | Provider |
|---|---|---|
| Home row | Yes (`PLUGIN`) | Yes on 2.0+ (`PROVIDER`); `GetProviderHomeSupport` reports it |
| Opens from a board click | Yes | Yes, via the home card, or the provider command calls `OpenGuiProvider` |
| Sidebar tabs | Hidden | Visible |
| Tab labels | N/A | Provider may rename |
| Back button | Yes, injected | No |
| `chrome=hook` | Appended by Server Assist | Not appended |
| `serverassist.use` check | Yes, before routing | Provider routed first |
| Permission owner | Server Assist checks hook node | Provider checks its own |
| `/sa` | Leaves the hook | Returns to the board |
| `F7` | Resumes the hook | Resumes the provider |

---
