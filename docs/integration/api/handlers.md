# Handlers and lifecycle

Part of the [Server Assist API](index.md).

## Handler signature

Every panel, hook and provider handler must be a **public instance** method with exactly this signature:

```csharp
public string HandleGuiRequest(string playerId, string payload)
```

- `playerId` comes from the server-side connection, not from the payload.
- `payload` is one of the forms below, or `@pp|<body>` when an optional companion protocol routes to this hook.
- Return a view string, or `null`/empty to signal failure.
- The handler runs on the game thread.

A null or empty return from a hook causes Server Assist to leave the hook and show:

```text
GUI hook '<label>' did not return a view.
```

A null or empty return from a provider clears the active provider and falls back to the normal board.

---

## Payloads

| Payload | When | Notes |
|---|---|---|
| `a\|open` | Player enters the panel/provider | Initial entry |
| `a\|<action>` | A row or action button was pressed | The action is whatever the handler returned in its row/button line |
| `t\|<encoded text>` | The player submitted the text input | Percent-encoded; decode before use |
| `a\|home` | `/sa` or `/serverassist` returns to Home | Consumed by Server Assist; clears the active hook/provider |
| `a\|hook\|back` | The injected hook Back button | Consumed by Server Assist; not forwarded to the hook |
| `@pp\|<body>` | An optional companion protocol request | Routed to the named hook; see [Optional companion protocols](companion-protocols.md#optional-companion-protocols-pp) |

Examples:

```text
a|open
a|buy|12
a|tab|users
a|search
t|hello world
```

The client encodes text with the same rules as the server:

| Character | Escape |
|---|---|
| `%` | `%25` |
| `\|` | `%7C` |
| newline | `%0A` |

Other characters are sent literally. Do not assume URL encoding beyond these three sequences.

Provider actions are forwarded verbatim, except `a|hook|back`, which Server Assist consumes.

---

## Reflection compatibility

The current API marks the preferred methods with `[HookMethod]`, so this works:

```csharp
serverAssist.Call("RegisterPanel", ...);
serverAssist.Call("OpenPanel", playerId, panelId, action);
serverAssist.Call("UnregisterPanel", panelId);
serverAssist.Call("RegisterGuiProvider", ...);
serverAssist.Call("OpenGuiProvider", playerId, providerId, payload);
serverAssist.Call("UnregisterGuiProvider", providerId);
```

Older companions may use a reflection helper for overloads that are public but not marked:

```csharp
private static object Invoke(
    Plugin plugin,
    string method,
    Type[] types,
    object[] args)
{
    if (plugin == null)
    {
        return null;
    }

    MethodInfo info = plugin.GetType().GetMethod(
        method,
        BindingFlags.Public | BindingFlags.Instance,
        null,
        types,
        null);

    return info != null ? info.Invoke(plugin, args) : null;
}
```

Use `Invoke` for the three-argument `OpenHookForPlayer` if a legacy companion depends on it. New code should use `OpenPanel` or `OpenHookForPlayerWithAction`.

---

## Lifecycle and cleanup

```csharp
private Plugin serverAssist;
private bool registered;

private void Init()
{
    timer.Once(2f, Register);
    timer.Once(5f, Register);
}

private void OnServerAssistReady()
{
    Register();
}

private void Unload()
{
    if (serverAssist != null && serverAssist.IsLoaded)
    {
        serverAssist.Call("UnregisterPanel", "examplepanel");
    }
}

private void Register()
{
    serverAssist = plugins.Find("ServerAssist");
    if (serverAssist == null || !serverAssist.IsLoaded)
    {
        return;
    }

    object result = serverAssist.Call(
        "RegisterPanel",
        "examplepanel",
        "Example Panel",
        "A minimal Server Assist panel.",
        "examplepanel.use",
        "ExamplePanel",
        "HandleGuiRequest",
        "ui_game_symbol_hammer",
        100);

    registered = result is bool && (bool)result;
}
```

Registration is safe to repeat. Unregistration is safe to repeat.

---
