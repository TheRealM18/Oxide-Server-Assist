# Media and network companions

Part of [Companion plugin integration](index.md).

## TwitchIntegration

TwitchIntegration links Twitch accounts, exposes permission tiers and lets streamers choose reward shortlists that render through Server Assist.

| Item | Value |
|---|---|
| Panel id | `twitch` |
| Label | `Twitch` (config `HookLabel`) |
| Permission | `twitch.use` (config `HookPermission`) |
| Plugin name | `TwitchIntegration` |
| Handler | `HandleGuiRequest` |
| Entry | `/twitch`, Home row |
| Icon | `sprite:sa_stream_panel` (config `PanelIcon`) |
| Order | `40` (config `PanelOrder`) |
| Extra permissions | `twitch.streamer`, `twitch.admin` |

### Registration

```csharp
string panelIcon = Icon("Panel");

object result = serverAssist.Call(
    "RegisterPanel",
    "twitch",
    config.HookLabel,
    config.HookDescription,
    config.HookPermission,
    Name,
    nameof(HandleGuiRequest),
    panelIcon,
    config.PanelOrder);

bool registered = result is bool && (bool)result;

if (!registered)
{
    // Older Server Assist builds only expose RegisterGuiHook.
    result = serverAssist.Call(
        "RegisterGuiHook",
        "twitch",
        config.HookLabel,
        config.HookDescription,
        config.HookPermission,
        Name,
        nameof(HandleGuiRequest));
    registered = result is bool && (bool)result;
}

if (registered)
{
    serverAssist.Call("SetPanelIcon", "twitch", panelIcon);
}
```

### Notes

- The panel shows the device-code link flow, loadout tiers, the effect catalog and an admin audit view.
- `twitch.streamer` unlocks streamer tools; `twitch.admin` unlocks relay and audit controls.
- Chat commands and the relay continue to work without Server Assist; the panel needs the client mod.
- The companion sends optional icon and tint fields on action buttons. ServerAssistMod 2.1 renders them; update the client with the server. A 2.0 client shows the extra values as tooltip text.

---

## BuggeeNet

BuggeeNet (`BuggeeCore`) provides fleet status, shared player state and one-click travel over a Redis relay.

| Item | Value |
|---|---|
| Hook id | `buggee` |
| Label | `Buggee Network` |
| Permission | `buggeecore.use` |
| Plugin name | `BuggeeCore` |
| Handler | `HandleGuiRequest` |
| Entry | `/buggee`, `/net`, Home row |
| Icon | `sprite:sa_stream_link` |
| Order | `95` |
| Admin | `buggeecore.admin` |

### Registration

```csharp
object result = serverAssist.Call(
    "RegisterPanel",
    "buggee",
    "Buggee Network",
    "Fleet status and one-click travel.",
    "buggeecore.use",
    Name,
    nameof(HandleGuiRequest),
    "sprite:sa_stream_link",
    95);

bool registered = result is bool && (bool)result;

if (!registered)
{
    // Older Server Assist builds only expose RegisterGuiHook.
    result = serverAssist.Call(
        "RegisterGuiHook",
        "buggee",
        "Buggee Network",
        "Fleet status and one-click travel.",
        "buggeecore.use",
        Name,
        nameof(HandleGuiRequest));
    registered = result is bool && (bool)result;
    if (registered)
    {
        serverAssist.Call("SetPanelIcon", "buggee", "sprite:sa_stream_link");
    }
}
```

### Notes

- The plugin name is `BuggeeCore` even though the product and folder are BuggeeNet. Use `plugins.Find("BuggeeCore")` and pass `Name` to registration.
- The panel shows fleet and peer status, hub travel, horde status and diagnostics.
- Other commands include `/hub`, `/home`, `/horde`, `/goto`, `/netpeers`, `/netdiag` and `/netrelease`; admin commands require `buggeecore.admin`.
- It unregisters with `UnregisterPanel` and clears its panel sessions.

---

## PlayerProfiles

PlayerProfiles stores multiple full character profiles per account and switches them through Server Assist. It is the reference implementation of the optional `@pp|` companion protocol.

| Item | Value |
|---|---|
| Panel id | `playerprofiles` |
| Label | `Profiles` |
| Permission | `playerprofiles.use` |
| Plugin name | `PlayerProfiles` |
| Handler | `HandleGuiRequest` |
| Entry | `/profiles`, `/profile`, Home row |
| Icon | `sprite:sa_profile_panel` |
| Order | `25` |
| Slot nodes | `playerprofiles.slots.1` … `playerprofiles.slots.N`, `playerprofiles.slots.*` |
| Admin | `playerprofiles.admin` |
| Client protocol | `@pp\|` version `1` |

### Registration

```csharp
object result = serverAssist.Call(
    "RegisterPanel",
    "playerprofiles",
    "Profiles",
    "Save and switch full character profiles.",
    "playerprofiles.use",
    Name,
    nameof(HandleGuiRequest),
    "sprite:sa_profile_panel",
    25);

bool registered = result is bool && (bool)result;

if (!registered)
{
    // Older Server Assist builds only expose RegisterGuiHook.
    result = serverAssist.Call(
        "RegisterGuiHook",
        "playerprofiles",
        "Profiles",
        "Save and switch full character profiles.",
        "playerprofiles.use",
        Name,
        nameof(HandleGuiRequest));
    registered = result is bool && (bool)result;

    if (registered)
    {
        serverAssist.Call("SetPanelIcon", "playerprofiles", "sprite:sa_profile_panel");
    }
}
```

### In-place switch protocol

When the updated client mod is present, the plugin switches characters without a reconnect:

```text
server -> client   @pp|prepare|token|fromName|toName
client -> server   @pp|playerprofiles|ready|token
server -> client   @pp|begin|token|chunks|sha256|bytes|chunkBytes
server -> client   @pp|chunk|token|index|base64
client -> server   @pp|playerprofiles|applied|token|sha256
server -> client   @pp|done|token
```

Client-to-server requests carry the panel id so Server Assist can route them without an active window session. Server-to-client responses do not carry the id. See [Optional companion protocols](../api/companion-protocols.md#optional-companion-protocols-pp).

### Notes

- **Server-only plugin.** There is no separate PlayerProfiles client DLL and no new NetPackage type. The client half is built into ServerAssistMod 2.1 (or the companion-protocol 2.0.1 build).
- **Older combinations fall back safely.** An older client or server plugin switches by reconnect instead of in-place, without showing an error window.
- **New profiles** start at level 1 with an empty inventory by default; the first switch to a new profile uses the reconnect path so the game can run its normal new-character spawn.
- **Permissions stay account-wide.** Oxide groups, permissions, land claims, allies and world data do not switch with the character.
- **Other plugins can read the active profile** with `GetActiveProfile`, `GetActiveProfileName` and `GetProfileScopedId`. The hooks `OnPlayerProfileCreated`, `OnPlayerProfileRenamed`, `OnPlayerProfileDeleted` and `OnPlayerProfileSwitch` announce changes.
- **Data lives under** `oxide/data/PlayerProfiles/<safe-player-id>/`. The `.ttp` payloads use the game's own PlayerDataFile container.

Read the plugin's own `README.txt` for commands, config and recovery behaviour.

---
