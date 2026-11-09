# API example and reference

Part of the [Server Assist API](index.md).

## Complete example

```csharp
using System;
using System.Text;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("ExamplePanel", "Author", "2.1.0")]
    [Description("A minimal Server Assist panel.")]
    public class ExamplePanel : CovalencePlugin
    {
        private Plugin serverAssist;

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
            Plugin assistant = plugins.Find("ServerAssist");
            if (assistant != null)
            {
                assistant.Call("UnregisterPanel", "examplepanel");
            }
        }

        private void Register()
        {
            serverAssist = plugins.Find("ServerAssist");
            if (serverAssist == null || !serverAssist.IsLoaded)
            {
                return;
            }

            serverAssist.Call(
                "RegisterPanel",
                "examplepanel",
                "Example Panel",
                "A minimal Server Assist panel.",
                "examplepanel.use",
                "ExamplePanel",
                "HandleGuiRequest",
                "ui_game_symbol_hammer",
                100);
        }

        public string HandleGuiRequest(string playerId, string payload)
        {
            if (!permission.UserHasPermission(playerId, "examplepanel.use") &&
                !permission.UserHasPermission(playerId, "serverassist.admin"))
            {
                return "v2\n" +
                       "title=" + Encode("Example Panel") + "\n" +
                       "status=" + Encode("You do not have permission.") + "\n" +
                       "row=0||" + Encode("Locked") + "|" + Encode("Ask an admin.") + "|0|0\n" +
                       "btn=0|close|" + Encode("Close") + "|1\n";
            }

            if (string.IsNullOrEmpty(payload) || payload == "a|open")
            {
                return RenderHome("Ready");
            }

            if (payload.StartsWith("t|", StringComparison.Ordinal))
            {
                string text = Decode(payload.Substring(2));
                return RenderHome("You typed: " + text);
            }

            if (payload == "a|refresh")
            {
                return RenderHome("Refreshed");
            }

            return RenderHome("Unknown action: " + payload);
        }

        private string RenderHome(string status)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("v2\n");
            builder.Append("title=").Append(Encode("Example Panel")).Append('\n');
            builder.Append("subtitle=").Append(Encode("A minimal panel")).Append('\n');
            builder.Append("status=").Append(Encode(status)).Append('\n');
            builder.Append("tab=hook\n");
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

---

## API quick reference

| Method | Marked | Purpose |
|---|---|---|
| `OnServerAssistReady()` | Hook | Re-register after Server Assist loads |
| `RegisterPanel` | `[HookMethod]` | Add or replace a home-row panel |
| `OpenPanel` | `[HookMethod]` | Open a panel for a player |
| `UnregisterPanel` | `[HookMethod]` | Remove a panel |
| `SetPanelIcon` | `[HookMethod]` | Set or replace a panel card icon |
| `GetPanelIcon` | `[HookMethod]` | Read a panel card icon |
| `GetPluginIcon` | `[HookMethod]` | Read a plugin's panel/provider icon |
| `RegisterGuiHook` | `[HookMethod]` | Compatibility panel registration |
| `UnregisterGuiHook` | `[HookMethod]` | Compatibility removal |
| `OpenHookForPlayer` (2) | `[HookMethod]` | Open a hook |
| `OpenHookForPlayer` (3) | Public | Legacy reflection overload |
| `OpenHookForPlayerWithAction` | `[HookMethod]` | Open a hook and send one action |
| `GetProviderHomeSupport` | `[HookMethod]` | Reports whether providers appear on Home |
| `RegisterGuiProvider` | `[HookMethod]` | Add or replace a provider |
| `OpenGuiProvider` | `[HookMethod]` | Open a provider |
| `SetProviderIcon` | `[HookMethod]` | Set or replace a provider card icon |
| `UnregisterGuiProvider` | `[HookMethod]` | Remove a provider |

Companion protocols are not a method call. Send `@pp|<hook-id>|<body>` through the existing request path; see [Optional companion protocols](companion-protocols.md#optional-companion-protocols-pp).

---

## Versioning

- View protocol **2** adds optional row icon, badge and tint fields, tab labels, hook chrome, panel images and optional action-button icon/tint fields.
- Protocol **1** rows are still parsed.
- New optional fields are ignored by older clients. A 2.0 client still parses the first five `btn` fields, but not the 2.1 icon/tint fields.
- Server Assist 2.1 adds `GetPanelIcon`, `GetPluginIcon`, action-button icons and the `@pp|` companion protocol.
- Companion protocol version **1** is independent of view protocol **2**.
- A future capability should extend the view protocol or add a new public method; do not break existing keys.
- Client and server builds should be updated together.

---
