# Server Assist 2.1 API

Server Assist owns one XUi window. Companion plugins provide panels and actions
through the server plugin. The client is a renderer and does not need plugin
assemblies.

Protocol version: **2**

---

## 1. Plugin identity

```text
Plugin name:   ServerAssist
Plugin class:  Oxide.Plugins.ServerAssist
Client mod:    ServerAssistMod
Window group:  serverAssist
```

Companion plugins resolve it with:

```csharp
Plugin serverAssist = plugins.Find("ServerAssist");
```

---

## 2. Ready hook

Server Assist calls this after its initial load and after a reload:

```csharp
private void OnServerAssistReady()
{
    RegisterMyPanel();
}
```

Registering the same panel id again replaces the previous one, so this is safe
after either plugin reloads.

---

## 3. Home-row panels

### RegisterPanel

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

Example:

```csharp
serverAssist.Call(
    "RegisterPanel",
    "adminshop",
    "Admin Shop",
    "Buy items with Dukes.",
    "adminshop.use",
    "AdminShop",
    "HandleGuiRequest",
    "ui_game_symbol_hammer",
    10);
```

The panel appears on the Server Assist home grid with a `PLUGIN` badge.

### OpenPanel

```csharp
[HookMethod("OpenPanel")]
public bool OpenPanel(string playerId, string panelId, string action);
```

Example:

```csharp
object result = serverAssist.Call(
    "OpenPanel",
    player.Id,
    "adminshop",
    "a|open");
```

Pass `""` for the normal entry screen. Returns `false` when the client has not
sent its Server Assist hello or the panel no longer exists.

### UnregisterPanel

```csharp
[HookMethod("UnregisterPanel")]
public bool UnregisterPanel(string panelId);
```

Call from the companion plugin's `Unload`.

### SetPanelIcon

```csharp
[HookMethod("SetPanelIcon")]
public bool SetPanelIcon(string id, string icon);
```

Use after `RegisterGuiHook` or `RegisterPanel` to set or change a card icon
without re-registering the panel. The icon accepts `sprite:`, `item:`, `atlas:`
or a plain sprite name.

The bundled hand-painted icon set uses these sprite keys:

```text
sprite:sa_admin_kits
sprite:sa_admin_shop
sprite:sa_admin_suite
sprite:sa_auction_house
sprite:sa_daily_rewards
sprite:sa_dynamic_backpack
sprite:sa_permission_gui
sprite:sa_plugin_hot_reload
sprite:sa_vote_skip_night
sprite:sa_qln_pack
```

`sprite:sa_server_assist` is the optional Server Assist brand mark, not a plugin
card. The PNGs live in `UIAtlases/UIAtlas` inside the Server Assist mod folder,
or in a separate atlas mod such as `AAZ_ServerAssistArt`, so the game loads them
into the UI atlas automatically.

### GetPanelIcon and GetPluginIcon

```csharp
[HookMethod("GetPanelIcon")]
public string GetPanelIcon(string id);

[HookMethod("GetPluginIcon")]
public string GetPluginIcon(string pluginName);
```

`GetPanelIcon` returns the live icon for a registered panel id, or an empty
string when the id is unknown. `GetPluginIcon` searches panels and providers for
a matching `pluginName` (case-insensitive) and returns the first icon it finds.
Both are safe to call through `Plugin.Call`. `PermissionGui` uses
`GetPluginIcon` and falls back to its built-in sprite map.

---

## 4. Provider / tab panels

Use this API when the provider needs the three top-level tabs and its own title,
such as AuctionHouse.

```csharp
RegisterGuiProvider(
    providerId,
    pluginName,
    handlerMethod,
    title,
    tab1,
    tab2,
    tab3);

OpenGuiProvider(playerId, providerId, payload);
UnregisterGuiProvider(providerId);
SetProviderIcon(providerId, icon);
bool GetProviderHomeSupport();
```

`GetProviderHomeSupport` returns `true` when the home board lists registered
providers. A companion should call it before choosing between provider mode and
a normal home panel, so older ServerAssist builds still get a home card.

A provider handler returns the standard view protocol and is called with:

```text
a|open
a|<action>
t|<encoded text>
```

Provider mode keeps the sidebar/tab chrome visible. It can rename the tabs with
`tab1label`, `tab2label` and `tab3label`.

Registered providers are also listed on the Server Assist home board as
`[GUI] <title>` cards with the `PROVIDER` badge. Clicking one opens the provider
in tab mode. The home row action is:

```text
providerpanel|open|<providerId>
```

The provider icon set with `SetProviderIcon` is used on the home card, and is
used as the default background/banner when the provider view omits them.

---

## 5. Handler signature

```csharp
public string HandleGuiRequest(string playerId, string payload)
```

The player id comes from the server-side connection, not from the payload.

---

## 6. View protocol

Server Assist accepts the original `v1` line protocol. New fields are optional.

```text
v2
title=<text>
subtitle=<text>
status=<text>
tab=home|shared|mine
tab1label=<text>
tab2label=<text>
tab3label=<text>
chrome=normal|hook
input=<label>|<visible 0/1>|<value>
row=<index>|<action>|<label>|<sub>|<enabled>|<selected>|<icon>|<badge>|<tint>
btn=<index>|<action>|<label>|<enabled>|<tooltip>|<icon>|<tint>
background=<icon>
banner=<icon>
launch=<type>|<target>|<close 0/1>
```

Limits:

- 10 cards/rows
- 12 action buttons
- 1 text input

The last two `btn=` fields (`<icon>` and `<tint>`) are optional; the five-field
form is still valid. This keeps older providers working and lets newer ones put
an `item:`, `sprite:` or `atlas:` icon on an action button.

### Page navigation

A panel can use its 12 action buttons as a page switcher. Row cards and buttons
send their action to the active panel handler, so no special page API is
required:

```text
btn=0|page|1|Page 1|1|Open page 1
btn=8|page|9|Page 9|0|Locked
btn=9|deposit|Deposit Held|1|Store the held stack
btn=10|refresh|Refresh|1|Reload the view
btn=11|hook|back|Back|1|Return to Server Assist
```

The handler receives:

```text
a|page|1
a|deposit
a|refresh
```

`hook|back` is handled by Server Assist itself and returns to the home board.
Row cards can carry page-specific actions such as `withdraw|21`.

This is the same pattern DynamicBackpack uses for its nine pages of nine item
cards, and it is available to AuctionHouse and any other multi-page panel.

### Row fields

```text
row=0|hook|open|shop|Admin Shop|Buy items|1|0|ui_game_symbol_hammer|PLUGIN|
row=1|launch|shared|kit|Kits|Give a kit|1|0|item:gunPistol||#FFCC66
```

### Panel images

Any hook or provider view can add a custom submenu image:

```text
background=sprite:sa_admin_shop
banner=sprite:sa_admin_shop
```

- `background` is a large, low-opacity watermark behind the cards.
- `banner` is a 72x72 image in the content header.
- Both accept the same `item:`, `sprite:`, `atlas:` or plain-name syntax as row icons.
- Server Assist automatically fills both from the panel's registered icon
  (`RegisterPanel`, `SetPanelIcon`, or `SetProviderIcon`) when the view does not
  set them. A companion can override either field for its own artwork.

The old six-field row is still valid. Missing icon fields use the default.

Action buttons accept optional `icon` and `tint` fields after `tooltip`. The
bundled `Config/XUi_InGame/windows.xml` includes an `icon` child view for
`btn0` through `btn11`, so button icons render when supplied; a button without
an icon hides that child view.

---

## 7. Icons and art

### Icon syntax

```text
item:<itemName>
sprite:<spriteName>
atlas:<atlasName>:<spriteName>
<plainSpriteName>
```

Examples:

```text
item:gunPistol
item:resourceForgedSteel
sprite:ui_game_symbol_map
sprite:ui_game_symbol_backpack
atlas:ServerAssistAtlas:permissions
```

### Item icons

Server Assist resolves:

```csharp
ItemClass.GetItemClass(itemName, true)
ItemClass.GetIconName()
```

The client assigns the resolved sprite to the card icon. The item name is stored,
not a baked icon path, so it follows game updates.

Item sprites are drawn from `ItemIconAtlas`, which covers the base game's
`Data/ItemIcons` textures and any mod that adds PNGs under
`Mods/<mod>/UIAtlases/ItemIconAtlas`. If the sprite is not in that atlas, Server
Assist checks the other loaded atlas managers and uses the one that contains it.
Mod authors do not need to duplicate textures or register a second icon.

### Tint

Optional HTML color:

```text
#RRGGBB
#RRGGBBAA
```

### Badges

Optional short badge text, for example:

```text
PLUGIN
PERSONAL
ADMIN
LOCKED
NEW
OFF
```

### Fallback

If an icon is missing or invalid, the client falls back to a vanilla symbol. A
custom atlas can be added later without changing the button data.

---

## 8. Item icon picker

The built-in button editor includes:

- **Edit Icon** — type `item:name`, `sprite:name`, `atlas:name:sprite`, or leave
  blank for the default.
- **Find Item Icon** — search item names, preview the actual game item icons,
  and click a result to assign it.

The picker uses `ItemClass.ItemNames` and filters by item name or localized name.

---

## 9. Permissions

```text
serverassist.use       open the window and browse
serverassist.admin     manage shared buttons and admin commands
serverassist.custom    create personal buttons
serverassist.button.*  shared button permission nodes
serverassist.personal.* personal button permission nodes
```

Rename any old permission nodes using `MIGRATION.md`.

---

## 10. Data model

Button fields:

```text
Id
Label
Description
TargetType    window | command
Target
Permission
Icon          item: / sprite: / atlas: / blank
IconTint      #RRGGBB or blank
Enabled
Order
```

Data file:

```text
oxide/data/ServerAssist/data.json
```

Config file:

```text
oxide/config/ServerAssist.json
```

---

## 11. Client behavior

- F7 / F6 opens Server Assist.
- `/sa`, `/serverassist` opens it.
- Sidebar keeps Home / Shared / My Buttons.
- Known cursor windows open non-modal.
- A hook screen hides navigation and shows Back.
- A provider screen keeps the sidebar/tab labels supplied by the provider.
- Window group lookup falls back across name, id, controller type and the XUi
  controller index.

---

## 12. API example

```csharp
using System;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("ExamplePanel", "Author", "2.0.0")]
    public class ExamplePanel : CovalencePlugin
    {
        private Plugin serverAssist;

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
            Plugin assistant = plugins.Find("ServerAssist");
            if (assistant != null)
            {
                assistant.Call("UnregisterPanel", "examplepanel");
            }
        }

        private void Register()
        {
            serverAssist = plugins.Find("ServerAssist");
            if (serverAssist == null)
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
            if (payload == "a|open" || string.IsNullOrEmpty(payload))
            {
                return "v2\n" +
                       "title=" + Encode("Example Panel") + "\n" +
                       "subtitle=" + Encode("A minimal panel") + "\n" +
                       "status=" + Encode("Ready") + "\n" +
                       "input=||0|\n" +
                       "row=0|refresh|Refresh|Click to refresh|1|0|ui_game_symbol_hammer|PANEL|\n" +
                       "btn=0|refresh|Refresh|1|\n";
            }

            return "v2\ntitle=" + Encode("Example Panel") + "\n" +
                   "subtitle=" + Encode("A minimal panel") + "\n" +
                   "status=" + Encode("Action: " + payload) + "\n" +
                   "input=||0|\n" +
                   "btn=0|refresh|Refresh|1|\n";
        }

        private static string Encode(string value)
        {
            return value == null
                ? string.Empty
                : value.Replace("%", "%25").Replace("|", "%7C").Replace("\n", "%0A");
        }
    }
}
```


---

## 13. Optional Player Profiles extension (client mod 2.0.1+)

The client mod recognises payloads that start with `@pp|` before passing a
response to the normal view parser. The PlayerProfiles plugin uses this to
stream a `.ttp` character payload over the existing request/response packages.
No new NetPackage type is registered, so the server package-ID table is
unchanged and older clients remain compatible.

Client-to-server protocol payloads carry the target hook id so they can be
routed without an active Server Assist window session:

```text
@pp|playerprofiles|hello|1
```

Server Assist routes this to the `playerprofiles` GUI hook and passes it
`@pp|hello|1`. The handshake and chunk format are documented in
`PlayerProfiles/README.txt`.
