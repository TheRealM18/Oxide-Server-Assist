# Server Assist 2.1 API

Server Assist exposes a small server-side integration surface for Oxide plugins. A companion plugin registers a panel, hook or provider; Server Assist calls a public handler with the acting player and a payload, and renders the string that the handler returns.

The client is a renderer. It does not load companion assemblies and it does not need a new net package for each feature. Server Assist 2.1 also adds optional icon read-back, action-button icons and an `@pp|` companion protocol for client-side features such as PlayerProfiles.

```text
Plugin name:   ServerAssist
Plugin class:  Oxide.Plugins.ServerAssist
Client mod:    ServerAssistMod
Window group:  serverAssist
View protocol: 2
```

---

## Integration models

| Model | Best for | Home row | Tabs | Back button | Entry |
|---|---|---|---|---|---|
| **Panel / hook** | A feature that should appear as a card on Home | Yes, `[GUI] <label>` (`PLUGIN` badge) | Hidden while open | Yes, injected | Click the card or `OpenPanel` |
| **Provider** | A destination with its own three tabs | Yes on 2.0+ builds, `[GUI] <title>` (`PROVIDER` badge); check `GetProviderHomeSupport` | Visible, renameable | No | Home card or provider command calls `OpenGuiProvider` |
| **Launch target** | An existing XUi window or client command | Button | N/A | N/A | Server Assist button |

This page documents all three. For per-plugin recipes, see [Companion Integration](../companions/index.md).

---

## Resolve the plugin

```csharp
Plugin serverAssist = plugins.Find("ServerAssist");
if (serverAssist == null || !serverAssist.IsLoaded)
{
    return;
}
```

`Plugin.Call` reaches private hook methods and public methods marked `[HookMethod]`. The preferred panel API methods are marked, so `Call` works. Some legacy overloads are public but not marked; see [Reflection compatibility](handlers.md#reflection-compatibility).

---

## Ready hook

Server Assist calls the Oxide hook after its initial load and after Oxide reloads Server Assist:

```csharp
private void OnServerAssistReady()
{
    RegisterMyPanel();
}
```

Registration is idempotent: the same id replaces the previous definition. Define this method in the companion plugin and register from both `Init` (delayed) and `OnServerAssistReady` for the most robust startup.

```csharp
private void Init()
{
    timer.Once(2f, RegisterMyPanel);
    timer.Once(5f, RegisterMyPanel);
}

private void OnServerAssistReady()
{
    RegisterMyPanel();
}
```

!!! note
    `OnServerAssistReady` is a normal Oxide hook on the companion plugin. Server Assist invokes it with `plugins.CallHook("OnServerAssistReady")`. It is not a method on Server Assist.

---

## API pages

| Page | Covers |
|---|---|
| [Panels and hooks](panels.md) | `RegisterPanel`, `OpenPanel`, panel icons, `RegisterGuiHook` and hook entry points |
| [Providers](providers.md) | Provider registration, opening, provider home cards and the hook/provider comparison |
| [View protocol](view-protocol.md) | Top-level keys, limits, images, page navigation, rows, buttons, launches, errors and encoding |
| [Handlers and lifecycle](handlers.md) | Handler signature, payloads, reflection compatibility and registration lifecycle |
| [Companion protocols](companion-protocols.md) | The optional `@pp\|` protocol, routing, responses and compatibility |
| [Example and reference](example-reference.md) | A complete companion example, the API quick reference and versioning |
