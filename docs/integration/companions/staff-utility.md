# Staff and utility companions

Part of [Companion plugin integration](index.md).

## VoteSkipNight

VoteSkipNight adds a vote panel to the home screen.

| Item | Value |
|---|---|
| Hook id | `voteskip` |
| Label | `Vote: Skip Night` |
| Permission | `voteskip.use` |
| Plugin name | `VoteSkipNight` |
| Handler | `HandleServerAssistRequest` |
| Entry | `/voteskip gui`, Home row |
| Admin | `voteskip.admin` |

### Registration

```csharp
Plugin serverAssist = plugins.Find("ServerAssist");
if (serverAssist == null)
{
    return;
}

string pluginName = string.IsNullOrEmpty(Name) ? GetType().Name : Name;

serverAssist.Call(
    "RegisterGuiHook",
    "voteskip",
    "Vote: Skip Night",
    "Start or join the night-skip vote. Blood moons cannot be skipped.",
    "voteskip.use",
    pluginName,
    nameof(HandleServerAssistRequest));

serverAssist.Call("SetPanelIcon", "voteskip", "sprite:sa_vote_skip_night");
```

### Notes

- Chat voting works without the GUI.
- `voteskip.gui` requires `voteskip.use` and the client mod.
- Blood moons can never be skipped.
- Re-register on `OnServerAssistReady`; unregister on `Unload`.

---

## AdminKits

AdminKits manages item kits and collection.

| Item | Value |
|---|---|
| Hook id | `adminkits` |
| Label | `Admin Kits` (config `HookLabel`) |
| Permission | `adminkits.use` (config `HookPermission`) |
| Plugin name | `AdminKits` |
| Handler | `HandleGuiRequest` |
| Entry | `/kits`, `/kit <name>`, Home row |
| Admin | `adminkits.admin` |
| Give | `adminkits.give` |
| Per kit | `adminkits.kit.<id>` |

### Registration

```csharp
Plugin serverAssist = plugins.Find("ServerAssist");
if (serverAssist == null)
{
    return;
}

serverAssist.Call(
    "RegisterGuiHook",
    "adminkits",
    config.HookLabel,
    config.HookDescription,
    config.HookPermission,
    Name,
    nameof(HandleGuiRequest));

serverAssist.Call("SetPanelIcon", "adminkits", "sprite:sa_admin_kits");
```

### Notes

- Kits store the game's own serialized item data, so quality, durability and mods survive.
- `adminkits.give` controls giving a kit to another player.
- The plugin re-registers after `/kitadmin reload` and on `OnServerAssistReady`.

---

## AdminSuite

AdminSuite is the staff toolkit: players, moderation, inventory, world, logs and rollback.

| Item | Value |
|---|---|
| Hook id | `adminsuite` |
| Label | `Admin Suite` (config `HookLabel`) |
| Permission | `adminsuite.use` (config `HookPermission`) |
| Plugin name | `AdminSuite` |
| Handler | `HandleGuiRequest` |
| Entry | `/as`, Home row |
| Admin | `adminsuite.admin` |

### Registration

```csharp
object result = InvokePlugin(
    serverAssist,
    "RegisterGuiHook",
    new[]
    {
        typeof(string), typeof(string), typeof(string),
        typeof(string), typeof(string), typeof(string)
    },
    new object[]
    {
        "adminsuite",
        config.HookLabel,
        config.HookDescription,
        config.HookPermission,
        Name,
        nameof(HandleGuiRequest)
    });

bool registered = result is bool && (bool)result;
if (registered)
{
    serverAssist.Call("SetPanelIcon", "adminsuite", "sprite:sa_admin_suite");
}
```

### Notes

- AdminSuite has many granular permissions (`adminsuite.players.kick`, `adminsuite.players.ban`, `adminsuite.world`, …). Ox administrators bypass them.
- Destructive actions require confirmation.
- `ProtectAdminsFromStaff` and `ProtectedPlayers` are server-side safeguards.
- Snapshots are taken before destructive actions when configured.
- `/as inv <player>` can open the Server Assist GUI directly on the inventory inspector.

---

## DailyRewards

DailyRewards provides daily, weekly, monthly and seasonal playtime rewards.

| Item | Value |
|---|---|
| Hook id | `dailyrewards` |
| Label | `Daily Rewards` (config `HookLabel`) |
| Permission | `dailyrewards.use` (config `HookPermission`) |
| Plugin name | `DailyRewards` |
| Handler | `HandleGuiRequest` |
| Entry | `/rewards`, `/daily`, Home row |
| Icon | `sprite:sa_daily_rewards` (config `PanelIcon`) |
| Order | `30` (config `PanelOrder`) |
| Admin | `dailyrewards.admin` |
| Tier | optional `dailyrewards.*` or another plugin's node |

### Registration

```csharp
object result = serverAssist.Call(
    "RegisterPanel",
    "dailyrewards",
    config.HookLabel,
    config.HookDescription,
    config.HookPermission,
    "DailyRewards",
    nameof(HandleGuiRequest),
    config.PanelIcon,
    config.PanelOrder);

bool registered = result is bool && (bool)result;

if (!registered)
{
    // Server Assist 1.x compatibility.
    serverAssist.Call(
        "RegisterGuiHook",
        "dailyrewards",
        config.HookLabel,
        config.HookDescription,
        config.HookPermission,
        "DailyRewards",
        nameof(HandleGuiRequest));
}

serverAssist.Call("SetPanelIcon", "dailyrewards", config.PanelIcon);
```

### Notes

- The GUI is optional; chat commands work without Server Assist.
- The plugin tracks playtime per period and resets on the configured schedule.
- Admin grants do not consume the player's claim.
- Per-tier permission nodes owned by other plugins are not registered by DailyRewards.
- `/rewards` opens the panel through `OpenPanel` and falls back to the three-argument `OpenHookForPlayer` on older builds.

---

## DynamicBackpack

DynamicBackpack is a server-only panel. It stores 81 slots and renders nine pages of nine item cards through Server Assist.

| Item | Value |
|---|---|
| Panel id | `backpack` |
| Label | `Backpack` |
| Permission | `backpack.1` |
| Plugin name | `DynamicBackpack` |
| Handler | `HandleBackpackGui` |
| Entry | `/backpack`, Home row |
| Icon | `sprite:sa_dynamic_backpack` |
| Order | `30` |
| Storage | 81 slots, nine pages × nine cards |
| Data | `oxide/data/DynamicBackpack/<player-id>.json` |

### Registration

```csharp
Plugin serverAssist = plugins.Find("ServerAssist");
if (serverAssist == null)
{
    return;
}

serverAssist.Call(
    "RegisterPanel",
    "backpack",
    "Backpack",
    "Permission-based virtual storage pages.",
    "backpack.1",
    "DynamicBackpack",
    nameof(HandleBackpackGui),
    "sprite:sa_dynamic_backpack",
    30);
```

### Page actions

```text
btn=0|page|1|Page 1|1|Open page 1
...
btn=8|page|9|Page 9|0|Locked
btn=9|deposit|Deposit Held|1|Store the held stack
btn=10|refresh|Refresh|1|Reload the view
btn=11|hook|back|Back|1|Return to Server Assist
```

Item cards send `withdraw|<slot>`; the slot mapping is:

```text
slot = (page - 1) * 9 + cardIndex
```

### Notes

- No client DLL, XUi window, grid, net package or bridge is involved.
- The panel is registered with `backpack.1`; a player needs at least page 1 to see the card.
- Clicking a card withdraws the whole stack; a full inventory drops it at the player's feet.
- **Deposit Held** fills the current page first, then other unlocked pages, merging partial stacks.
- Empty and locked cards are disabled.
- The 81-slot data format is unchanged from the old backpack build.

Read: [Dynamic Backpack](../dynamic-backpack.md).

---

## PluginHotReload

PluginHotReload watches `oxide/plugins` for `.cs` changes and reloads only the affected Oxide plugins, with filtering, debounce and audit output.

| Item | Value |
|---|---|
| Hook id | `pluginhotreload` |
| Label | `Plugin Hot Reload` |
| Permission | `pluginhotreload.use` |
| Plugin name | `PluginHotReload` |
| Handler | `HandleServerAssistRequest` |
| Entry | `/hotreload`, `/phr`, `/plreload`, Home row |
| Icon | `sprite:sa_plugin_hot_reload` |
| Order | `80` |
| Admin | `pluginhotreload.admin` |

### Registration

```csharp
object result = serverAssist.Call(
    "RegisterPanel",
    "pluginhotreload",
    "Plugin Hot Reload",
    "Reload plugins, scan for new files and check the watcher.",
    "pluginhotreload.use",
    Name,
    nameof(HandleServerAssistRequest),
    "sprite:sa_plugin_hot_reload",
    80);

serverAssistHookRegistered = result is bool && (bool)result;

if (!serverAssistHookRegistered)
{
    // Older Server Assist builds only expose RegisterGuiHook.
    result = serverAssist.Call(
        "RegisterGuiHook",
        "pluginhotreload",
        "Plugin Hot Reload",
        "Reload plugins, scan for new files and check the watcher.",
        "pluginhotreload.use",
        Name,
        nameof(HandleServerAssistRequest));

    serverAssistHookRegistered = result is bool && (bool)result;
    if (serverAssistHookRegistered)
    {
        serverAssist.Call("SetPanelIcon", "pluginhotreload", "sprite:sa_plugin_hot_reload");
    }
}
```

### Notes

- The panel can list loaded plugins, reload one plugin, scan the plugin folder and inspect watcher state.
- Reload and scan actions are re-checked against `pluginhotreload.use` or `pluginhotreload.admin` inside the handler.
- It unregisters with `UnregisterPanel`; older builds use `UnregisterGuiHook`.
- The watcher and commands keep working when Server Assist is not installed; only the panel is unavailable.

---
