# Companion plugin integration

Server Assist is the shell; companion plugins provide the features. This page is the integration guide for the plugins in this repository and for new plugins that follow the same patterns.

All companion plugins are **server-only**. They ship no client DLL and no XUi XML. They return the Server Assist line protocol through the existing client mod.

---

## Choose the right model

| If the feature... | Use | Entry |
|---|---|---|
| Should appear as a card on Home | Panel / hook | Player clicks `[GUI] <label>` |
| Needs the three tabs and its own destination | Provider | Home `PROVIDER` card or `OpenGuiProvider` |
| Is an existing XUi window | Button target | Shared or personal button |
| Is a client-side console command | Button target | Shared or personal button |

### Panel / hook

- Appears on Home with a `[GUI] ` prefix and a `PLUGIN` badge.
- Hides the board tabs while open.
- Gets an injected **Back** button.
- Permission is checked by Server Assist when building Home, opening and on every forwarded action.
- Best for PermissionGui, AdminShop, VoteSkipNight, AdminKits, AdminSuite, DailyRewards, DynamicBackpack, PluginHotReload, TwitchIntegration, BuggeeNet and PlayerProfiles.

### Provider

- Appears on Home on current builds as a `[GUI] <title>` card with a `PROVIDER` badge.
- Keeps the three tabs and can rename them.
- Has no injected Back button.
- Is routed before `serverassist.use`; the provider owns its own permission checks.
- `GetProviderHomeSupport()` returns `true`; a companion can fall back to a normal panel on older builds.
- Best for AuctionHouse.

### Button

- Uses a `window` or `command` target.
- Is ideal for opening a third-party XUi window or a client command.
- Cannot render a custom feature by itself.

---

## Companion pages

| Page | Covers |
|---|---|
| [Core companions](core.md) | PermissionGui, AdminShop and AuctionHouse integration recipes |
| [Staff and utility companions](staff-utility.md) | VoteSkipNight, AdminKits, AdminSuite, DailyRewards, DynamicBackpack and PluginHotReload integration recipes |
| [Media and network companions](media-network.md) | TwitchIntegration, BuggeeNet and PlayerProfiles integration recipes |
| [Writing a companion](authoring.md) | Checklist, registration helpers, full example, testing and rules |
| [Plugin reference](../../plugins/index.md) | Full install, command, permission and configuration pages for every bundled plugin |


---

## Integration matrix

| Plugin | Model | Id | Permission | Handler | Entry |
|---|---|---|---|---|---|
| PermissionGui | Panel/hook | `permissions` | `permissiongui.use` | `HandleServerAssistRequest` | `/permgui` |
| AdminShop | Panel/hook | `adminshop` | `adminshop.use` | `HandleGuiRequest` | `/shop` |
| AuctionHouse | Provider | `auctionhouse` | `auctionhouse.use` | `HandleAuctionGuiRequest` | `/auction` |
| VoteSkipNight | Panel/hook | `voteskip` | `voteskip.use` | `HandleServerAssistRequest` | `/voteskip gui` |
| AdminKits | Panel/hook | `adminkits` | `adminkits.use` | `HandleGuiRequest` | `/kits` |
| AdminSuite | Panel/hook | `adminsuite` | `adminsuite.use` | `HandleGuiRequest` | `/as` |
| DailyRewards | Panel/hook | `dailyrewards` | `dailyrewards.use` | `HandleGuiRequest` | `/rewards` |
| DynamicBackpack | Panel/hook | `backpack` | `backpack.1` | `HandleBackpackGui` | `/backpack` |
| PluginHotReload | Panel/hook | `pluginhotreload` | `pluginhotreload.use` | `HandleServerAssistRequest` | `/hotreload` |
| TwitchIntegration | Panel/hook | `twitch` | `twitch.use` | `HandleGuiRequest` | `/twitch` |
| BuggeeNet | Panel/hook | `buggee` | `buggeecore.use` | `HandleGuiRequest` | `/buggee` |
| PlayerProfiles | Panel/hook + `@pp\|` | `playerprofiles` | `playerprofiles.use` | `HandleGuiRequest` | `/profiles` |

---

## Registration order and reload

```text
Server start
  Server Assist loads -> OnServerAssistReady at ~3s
  Companions load -> Init timers at 2s/5s
  Companions that are already loaded receive OnServerAssistReady
  Late companions register from their own timers

Oxide reload ServerAssist
  Init -> new hook registry
  OnServerAssistReady -> every loaded companion re-registers

Oxide reload a companion
  Companion Init -> register again (replace by id)

Companion unload
  Companion Unload -> Unregister...
  Server Assist OnPluginUnloaded -> safety removal
```

Use both delayed registration and `OnServerAssistReady`. Do not assume the companion or Server Assist loads first.

---

## Related pages

- [API](../api/index.md)
- [Permissions](../../core/permissions.md)
- [Icons](../../core/icons.md)
- [Buttons](../../core/buttons.md)
- [Dynamic Backpack](../dynamic-backpack.md)
- [Troubleshooting](../../reference/troubleshooting/index.md)
