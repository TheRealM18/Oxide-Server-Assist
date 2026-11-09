# Writing a companion

Part of [Companion plugin integration](index.md).

## Generic checklist

- [ ] Resolve `plugins.Find("ServerAssist")` and check `IsLoaded`.
- [ ] Register from `Init` (delayed) and `OnServerAssistReady`.
- [ ] Use a stable lowercase id (max 48).
- [ ] Prefer `RegisterPanel` with an icon and order; fall back to `RegisterGuiHook` for older builds.
- [ ] Provide a permission node owned by your plugin.
- [ ] Implement `public string Handler(string playerId, string payload)`.
- [ ] Encode every dynamic field.
- [ ] Re-check permission and object ownership in every action.
- [ ] Return a valid view for `a|open`.
- [ ] Unregister in `Unload`.
- [ ] Test with Server Assist unloaded, loaded, reloaded and with the client mod absent.
- [ ] Test as an admin, a permitted player and a denied player.

---

## Shared registration helpers

Different plugins use slightly different call styles. All are valid.

### Direct `Plugin.Call` (marked methods)

```csharp
Plugin serverAssist = plugins.Find("ServerAssist");
if (serverAssist == null || !serverAssist.IsLoaded)
{
    return;
}

serverAssist.Call(
    "RegisterPanel",
    "myplugin",
    "My Plugin",
    "Does a useful thing.",
    "myplugin.use",
    "MyPlugin",
    "HandleGuiRequest",
    "ui_game_symbol_hammer",
    50);
```

### Reflection for legacy overloads

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

### Defensive registration

```csharp
private void RegisterWithServerAssist()
{
    try
    {
        Plugin serverAssist = plugins.Find("ServerAssist");
        if (serverAssist == null || !serverAssist.IsLoaded)
        {
            return;
        }

        object result = serverAssist.Call(
            "RegisterPanel",
            HookId,
            HookLabel,
            HookDescription,
            HookPermission,
            Name,
            nameof(HandleGuiRequest),
            "ui_game_symbol_hammer",
            50);

        if (result is bool accepted && accepted)
        {
            Interface.Oxide.LogInfo("[" + Name + "] registered ServerAssist panel.");
        }
    }
    catch (Exception exception)
    {
        Interface.Oxide.LogWarning("[" + Name + "] ServerAssist registration failed: " + exception.Message);
    }
}
```

---

## Writing a new companion

### 1. Plugin skeleton

```csharp
using System;
using System.Text;
using Oxide.Core;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("MyFeature", "Author", "1.0.0")]
    [Description("A Server Assist companion.")]
    public class MyFeature : CovalencePlugin
    {
        private const string HookId = "myfeature";
        private const string PermUse = "myfeature.use";

        private void Init()
        {
            permission.RegisterPermission(PermUse, this);
            timer.Once(2f, Register);
            timer.Once(5f, Register);
        }

        private void OnServerAssistReady()
        {
            Register();
        }

        private void Unload()
        {
            Plugin serverAssist = plugins.Find("ServerAssist");
            if (serverAssist != null)
            {
                serverAssist.Call("UnregisterPanel", HookId);
            }
        }

        private void Register()
        {
            Plugin serverAssist = plugins.Find("ServerAssist");
            if (serverAssist == null || !serverAssist.IsLoaded)
            {
                return;
            }

            serverAssist.Call(
                "RegisterPanel",
                HookId,
                "My Feature",
                "Does something useful.",
                PermUse,
                Name,
                nameof(HandleGuiRequest),
                "ui_game_symbol_hammer",
                50);
        }

        public string HandleGuiRequest(string playerId, string payload)
        {
            if (!permission.UserHasPermission(playerId, PermUse) &&
                !permission.UserHasPermission(playerId, "serverassist.admin"))
            {
                return View("You do not have permission.");
            }

            if (string.IsNullOrEmpty(payload) || payload == "a|open")
            {
                return View("Ready");
            }

            if (payload.StartsWith("t|", StringComparison.Ordinal))
            {
                string text = Decode(payload.Substring(2));
                return View("You typed: " + text);
            }

            return View("Unknown action.");
        }

        private string View(string status)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("v2\n");
            builder.Append("title=").Append(Encode("My Feature")).Append('\n');
            builder.Append("subtitle=").Append(Encode("A Server Assist companion")).Append('\n');
            builder.Append("status=").Append(Encode(status)).Append('\n');
            builder.Append("input=Type something|1|\n");
            builder.Append("row=0|refresh|Refresh|Rebuild this view|1|0|sprite:ui_game_symbol_hammer|PANEL|\n");
            builder.Append("btn=0|refresh|Refresh|1|Reload this view\n");
            builder.Append("btn=1|close|Close|1|Close the window\n");
            return builder.ToString();
        }

        private static string Encode(string value)
        {
            return value == null
                ? string.Empty
                : value.Replace("%", "%25").Replace("|", "%7C").Replace("\n", "%0A");
        }

        private static string Decode(string value)
        {
            return value == null
                ? string.Empty
                : value.Replace("%0A", "\n").Replace("%7C", "|").Replace("%25", "%");
        }
    }
}
```

### 2. Permission

Register a node owned by your plugin. Do not reuse `serverassist.*` unless the node is genuinely a Server Assist button node.

### 3. View

Return a valid `v2` view with:

- title;
- subtitle;
- status;
- optional input;
- at least one row or button;
- encoded text.

### 4. Actions

Every action is a fresh request. Do not rely on state that can disappear on a plugin reload. Rebuild the view from authoritative data.

### 5. Errors

Return an error view instead of throwing. Log the exception if something unexpected happens.

---

## Testing matrix

| Scenario | Expected |
|---|---|
| Server Assist not loaded | Companion logs a warning once; chat commands still work |
| Server Assist loaded after companion | `OnServerAssistReady` registers the panel |
| Companion loaded after Server Assist | Delayed `Register` registers the panel |
| Server Assist reloaded | Panel re-registers; no duplicate row |
| Companion reloaded | Old hook removed; new one registered |
| Provider mode on a 2.0+ build | `PROVIDER` card appears on Home; click opens the provider with `a\|open` |
| Provider mode on an older build | `GetProviderHomeSupport` returns false; companion registers a home panel fallback |
| `@pp\|` protocol with a 2.1 plugin and client | Routed to the named hook; response is not chrome-wrapped |
| `@pp\|` protocol with an older plugin or client | Request is ignored or the companion uses its reconnect/fallback path; no error window |
| Client mod absent | `OpenPanel` returns `false`; companion falls back to chat |
| Player lacks hook permission | Row hidden; direct open refused |
| Player loses permission inside hook | Next action exits the hook with a message |
| Handler throws | Server Assist logs and returns an error view |
| Handler returns empty | Server Assist leaves the hook or provider |

---

## Do and do not

### Do

- Use a stable lowercase id.
- Register in both `Init` and `OnServerAssistReady`.
- Unregister in `Unload`.
- Own your permission node.
- Re-check permissions on every action.
- Encode all dynamic text.
- Return a view for every payload.
- Keep labels short.
- Use icons and badges consistently.

### Do not

- Register a node owned by another plugin.
- Trust `playerId` from the payload; it is supplied by the server.
- Assume the client is trusted.
- Return HTML or raw newlines in a field.
- Throw for normal user errors.
- Open a window target that the client may not have.
- Keep a second client DLL or a duplicate XUi group.

---

## Related pages

- [API](../api/index.md)
- [Permissions](../../core/permissions.md)
- [Icons](../../core/icons.md)
- [Buttons](../../core/buttons.md)
- [Dynamic Backpack](../dynamic-backpack.md)
- [Troubleshooting](../../reference/troubleshooting/index.md)
