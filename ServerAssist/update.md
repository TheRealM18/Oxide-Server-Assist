# ServerAssist GUI Hook API

Status: the hooks below are implemented in the ServerAssist 2.1.0 plugin and in
the ServerAssistMod client DLL. This file documents them so AdminShop,
PermissionGui and future companion plugins can use the same integration.

## What the hook system does

ServerAssist owns one universal XUi window. A companion Oxide plugin can register
a **GUI hook**. The hook appears as a home-row button on the ServerAssist home
screen. When the player opens it, ServerAssist forwards every row click, button
click, and text submission to the companion plugin and renders the string that
the companion returns.

The companion plugin never sends a net package and never adds XUi XML. It only
returns the same line protocol ServerAssist already uses. The ServerAssist client
mod is therefore the only client-side dependency.

## Hook entry points on ServerAssist

These are public instance methods on the `ServerAssist` Oxide plugin:

```csharp
bool RegisterGuiHook(
    string id,
    string label,
    string description,
    string permissionName,
    string pluginName,
    string methodName);

bool UnregisterGuiHook(string id);

bool OpenHookForPlayer(string playerId, string hookId);
bool OpenHookForPlayer(string playerId, string hookId, string subAction);
```

### RegisterPanel, OpenPanel and page panels

Server Assist 2.0 adds a richer alias:

```csharp
bool RegisterPanel(
    string id,
    string label,
    string description,
    string permissionName,
    string pluginName,
    string methodName,
    string icon,
    int order);

bool OpenPanel(string playerId, string panelId, string action);
bool UnregisterPanel(string panelId);
```

`RegisterPanel` registers the same hook as `RegisterGuiHook` but also sets a
card icon (`item:`, `sprite:` or a plain sprite name) and an explicit sort order.

A panel can expose pages with its 12 action buttons. The client sends
`a|page|N` and the hook handler receives it:

```text
btn=0|page|1|Page 1|1|Open page 1
btn=8|page|9|Page 9|0|Locked
btn=9|deposit|Deposit Held|1|Store the held item
btn=10|refresh|Refresh|1|Reload the view
btn=11|hook|back|Back|1|Return to Server Assist
```

`hook|back` is handled by Server Assist itself. Row cards can carry page-specific
actions such as `withdraw|21`. DynamicBackpack uses this pattern for nine pages
of nine item cards; AuctionHouse can use it for its listing pages.

### RegisterGuiHook

Registers or replaces a hook by id.

- `id` is lowercased and limited to 48 characters. Use a short stable id such as
  `adminshop`.
- `label` is the text shown on the ServerAssist home screen. ServerAssist prefixes
  it with `[GUI] `.
- `description` is shown on the home row and in tooltips. It is limited to 160
  characters.
- `permissionName` is an Oxide permission node. ServerAssist hides the hook from
  players who do not have it. Leave it blank to show the hook to everyone who can
  open ServerAssist. ServerAssist always allows Oxide administrators.
- `pluginName` is the Oxide plugin name passed to `plugins.Find`.
- `methodName` is the public method on that plugin with this exact signature:

```csharp
string MethodName(string playerId, string payload);
```

Returns `true` when the hook was stored. Registering the same id again replaces
the old definition, which makes reloads safe.

### UnregisterGuiHook

Removes a hook by id. Call this from the companion plugin's `Unload` so a
reloaded or removed plugin does not leave a dead home-row button.

### OpenHookForPlayer

Opens ServerAssist on the client, enters the hook, and optionally sends one
provider action after the initial `a|open` call.

```csharp
// Using the reflection helper shown below:
// Open the hook at its normal entry screen.
Invoke(
    serverAssist,
    "OpenHookForPlayer",
    new[] { typeof(string), typeof(string), typeof(string) },
    new object[] { playerId, "adminshop", null });

// Open the hook and immediately ask the provider to show its search input.
Invoke(
    serverAssist,
    "OpenHookForPlayer",
    new[] { typeof(string), typeof(string), typeof(string) },
    new object[] { playerId, "adminshop", "a|search" });
```

Returns `false` when the client has not sent its ServerAssist capability hello or
when the player is not online. In that case the companion should fall back to a
chat message explaining that the ServerAssist client mod is required.

### OnServerAssistReady

ServerAssist calls the Oxide hook `OnServerAssistReady` after its initial load and
after Oxide reloads ServerAssist. A companion plugin should define:

```csharp
private void OnServerAssistReady()
{
    RegisterMyHookAgain();
}
```

This is how a companion recovers its hook after ServerAssist is reloaded. The
registration is idempotent because `RegisterGuiHook` replaces by id.

### Panel and provider icons

Panels and providers can carry an icon for their home card and their opened
banner. Pass `item:`, `sprite:` or a plain sprite name:

```csharp
bool SetPanelIcon(string id, string icon);
bool SetProviderIcon(string providerId, string icon);
```

Two read-back hooks let another plugin reuse a live icon without hard-coding a
sprite map:

```csharp
string GetPanelIcon(string id);
string GetPluginIcon(string pluginName);
```

`GetPanelIcon` returns the icon registered for a panel id. `GetPluginIcon`
matches the owning `PluginName` of a panel or provider, case-insensitively, and
returns the first icon it finds. Both return an empty string when the panel or
plugin is unknown. `PermissionGui` prefers `GetPluginIcon` and falls back to its
built-in plugin-to-sprite map.

## Provider method and payloads

Registered providers are listed on the home board alongside GUI hooks. The home
card shows `[GUI] <title>`, the provider icon and a `PROVIDER` badge; clicking it
opens the provider in tab mode. Internally that home row uses the action
`providerpanel|open|<providerId>`.

`GetProviderHomeSupport()` returns `true`. Companion plugins call it before
choosing between provider mode and a normal home panel, so they can keep a home
card on builds that predate provider home entries.

ServerAssist resolves the provider method with reflection:

```csharp
MethodInfo method = plugin.GetType().GetMethod(
    methodName,
    BindingFlags.Public | BindingFlags.Instance,
    null,
    new[] { typeof(string), typeof(string) },
    null);
```

The provider must therefore expose exactly:

```csharp
public string HandleGuiRequest(string playerId, string payload)
```

`playerId` is derived on the server from the sending client's platform id. It is
not taken from the payload, so a client cannot claim another player's identity.

ServerAssist calls the provider with these payloads:

| Payload | When |
| --- | --- |
| `a\|open` | The player enters the hook. |
| `a\|<action>` | A row or action button with that action was pressed. |
| `t\|<encoded text>` | The player submitted the text input. |

Everything after `a|` is the action string the provider put in its row or button
line. Everything after `t|` is percent-encoded text. ServerAssist decodes the
text before re-encoding it for the provider, so the provider should decode the
value found after `t|`.

Provider actions are forwarded verbatim, except for `a|hook|back`, which
ServerAssist handles itself to leave the hook and return home.

## Response protocol

The provider returns the standard ServerAssist line protocol. All dynamic text
must be percent-escaped with the same rules as ServerAssist:

| Character | Escape |
| --- | --- |
| `%` | `%25` |
| `\|` | `%7C` |
| newline | `%0A` |

Supported keys:

```text
v2
protocol=2
title=<text>
subtitle=<text>
status=<text>
input=<label>|<visible 0/1>|<value>
row=<index>|<action>|<label>|<sub>|<enabled 0/1>|<selected 0/1>|<icon>|<badge>|<tint>
btn=<index>|<action>|<label>|<enabled 0/1>|<tooltip>|<icon>|<tint>
```

The last two `btn=` fields are optional. The five-field form remains valid, so an
older client or provider keeps working. `icon` accepts the same `item:`,
`sprite:` and `atlas:` prefixes as a row icon; `tint` is an HTML colour such as
`#FFFFFF`.

Limits:

- 10 rows (`row=0` through `row=9`)
- 12 action buttons (`btn=0` through `btn=11`)
- One text input
- Title, subtitle, status, and labels are plain text after encoding; the client
  renders them in the ServerAssist window.
- The static footer text in ServerAssist's `Config/XUi_InGame/windows.xml` is not
  part of the provider protocol. It stays whatever the client XML says.

ServerAssist appends these keys after the provider's response:

```text
chrome=hook
tab=hook
```

The `chrome=hook` value tells the client to hide the Home/Shared/My Buttons tabs
and the ServerAssist refresh button, and to show the hook Back button instead.
The Back button sends `a|hook|back`, which ServerAssist consumes. The provider
does not receive it.

The provider should not output `launch=` lines unless it intends to launch an
XUi window or run a local console command. AdminShop does not use launches.

## Calling ServerAssist from another plugin

ServerAssist's public integration methods are normal public methods, not Oxide
hooks. `Plugin.Call` only reaches private hook methods and methods marked with
`[HookMethod]`, so a companion plugin should use reflection for these three
calls. AdminShop does exactly that.

```csharp
private static object Invoke(Plugin plugin, string method, Type[] types, object[] args)
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

`OnServerAssistReady` is different: it is a private method on the companion
plugin, so it is a real Oxide hook and ServerAssist can call it normally.

## Minimal example

```csharp
using System;
using System.Reflection;
using System.Text;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("ExampleHook", "Author", "1.0.0")]
    public class ExampleHook : CovalencePlugin
    {
        private const string HookId = "examplehook";

        private void Init()
        {
            timer.Once(2f, Register);
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
                Invoke(
                    serverAssist,
                    "UnregisterGuiHook",
                    new[] { typeof(string) },
                    new object[] { HookId });
            }
        }

        private void Register()
        {
            Plugin serverAssist = plugins.Find("ServerAssist");
            if (serverAssist == null)
            {
                return;
            }

            Invoke(
                serverAssist,
                "RegisterGuiHook",
                new[]
                {
                    typeof(string), typeof(string), typeof(string),
                    typeof(string), typeof(string), typeof(string)
                },
                new object[]
                {
                    HookId,
                    "Example",
                    "An example ServerAssist hook.",
                    "examplehook.use",
                    "ExampleHook",
                    "HandleGuiRequest"
                });
        }

        private static object Invoke(Plugin plugin, string method, Type[] types, object[] args)
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

        public string HandleGuiRequest(string playerId, string payload)
        {
            if (payload == "a|open" || string.IsNullOrEmpty(payload))
            {
                return RenderHome();
            }

            if (payload.StartsWith("t|", StringComparison.Ordinal))
            {
                string text = Decode(payload.Substring(2));
                return RenderHome("You typed: " + text);
            }

            return RenderHome("Unknown action: " + payload);
        }

        private string RenderHome(string status = "")
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("v2\n");
            builder.Append("protocol=2\n");
            builder.Append("title=").Append(Encode("Example")).Append('\n');
            builder.Append("subtitle=").Append(Encode("ServerAssist GUI hook demo")).Append('\n');
            builder.Append("status=").Append(Encode(status)).Append('\n');
            builder.Append("input=Type something|1|\n");
            builder.Append("row=0|say|Say hello|This row sends an action|1|0\n");
            builder.Append("btn=0|refresh|Refresh||1\n");
            return builder.ToString();
        }

        private static string Encode(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("%", "%25")
                .Replace("|", "%7C")
                .Replace("\n", "%0A")
                .Replace("\r", string.Empty);
        }

        private static string Decode(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("%0A", "\n")
                .Replace("%7C", "|")
                .Replace("%25", "%");
        }
    }
}
```

## AdminShop integration notes

AdminShop uses this API as follows:

- Hook id: `adminshop`
- Hook label: `Admin Shop`
- Hook permission: `adminshop.use`
- Provider method: `AdminShop.HandleGuiRequest(string, string)`
- It registers in `Init`, `OnServerInitialized`, and `OnServerAssistReady`.
- It unregisters in `Unload`.
- It never adds an XUi window and never sends a net package.
- `/shop` calls `ServerAssist.OpenHookForPlayer(playerId, "adminshop")`.

The shop validates permission, item id, quantity, price, currency count, and
inventory space again on the server for every request. The ServerAssist hook
permission is convenience visibility; it is not the only security check.

## Future changes

If a future companion needs a new capability, prefer extending the provider
method or adding a new public method on ServerAssist rather than changing the
existing protocol. The current API is sufficient for:

- multi-screen provider UIs
- search and pagination
- text prompts
- per-hook permissions
- re-registration after either plugin reloads
- server-authoritative actions

`RegisterGuiHook`, `UnregisterGuiHook` and `OpenHookForPlayerWithAction` are
marked with `[HookMethod]`, so `Plugin.Call` works directly. The two
`OpenHookForPlayer` overloads remain available through reflection for AdminShop
and older companions.

No update to the ServerAssist client DLL is needed to use these hooks.



