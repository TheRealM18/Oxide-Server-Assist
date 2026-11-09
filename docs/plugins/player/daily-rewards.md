# Daily Rewards

> **Version:** 1.1.0 · **Type:** Server-side Oxide plugin with a Server Assist panel (no client files) · **Source:** [DailyRewards.cs](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/DailyRewards/oxide/plugins/DailyRewards.cs)

Daily Rewards adds daily, weekly, monthly and seasonal playtime rewards to a 7 Days to Die server. The player-facing interface is a Server Assist panel, so the plugin ships no client DLL and no XUi XML. Chat commands work even when Server Assist is unavailable.

The plugin tracks real connected playtime per tier period. A daily reward is locked until the player has played the configured amount of time in the current daily period. Weekly, monthly and seasonal tiers default to no additional playtime requirement, but each tier has its own `RequiredPlaytimeMinutes` setting.

---

## Requirements

| Requirement | Detail |
|---|---|
| Game | 7 Days to Die with Oxide / uMod |
| Server | `oxide/plugins/DailyRewards.cs` compiled by Oxide |
| EAC | Off, as with every Oxide plugin |
| Server Assist | Optional for gameplay, required for the GUI panel |
| Server Assist client mod | Required on each client that should see the panel |
| Art | Optional `ServerAssistArt/UIAtlases/UIAtlas` sprites for card and row icons |

!!! note
    The plugin still works through chat without Server Assist. Server Assist only supplies the panel, its navigation and its icons.

---

## Installation

1. Stop the server.
2. Copy `DailyRewards.cs` into `oxide/plugins/`.
3. Install the Server Assist plugin on the server and the Server Assist client mod on every client if you want the GUI.
4. Optionally copy `ServerAssistArt/UIAtlases/UIAtlas` into the Server Assist mod folder on the server and every client. Missing sprites fall back to the hammer symbol.
5. Start the server.
6. Edit `oxide/config/DailyRewards.json` if you want different rewards or playtime requirements, then run `/rewards reload` or restart.

On a successful panel registration the server log contains:

```text
[DailyRewards] registered Server Assist panel 'dailyrewards' with icon sprite:sa_daily_rewards.
```

A complete example configuration is included in the package at `Config/DailyRewards.example.json`.

---

## How rewards and periods work

Each player has one independent claim per tier per period:

| Tier | Period reset |
|---|---|
| Daily | Every day at `DailyResetHour` |
| Weekly | Every week on `WeeklyResetDay` at `DailyResetHour` |
| Monthly | The 1st of each month at `DailyResetHour` |
| Seasonal | Every three calendar months, or when manually rotated |

Playtime is counted only while the player is connected. The time is added to all four tier periods. A player can disconnect and reconnect; time accumulates until the relevant period resets. When a period rolls over, that tier's playtime counter starts at zero again.

The requirement is period-based, not lifetime-based. Set `Daily.RequiredPlaytimeMinutes` to `0` to make the daily reward instantly claimable, or raise it for a longer gate. The plugin flushes active sessions every 60 seconds and saves player data every `SaveIntervalSeconds`, on disconnect, on claim, on server save, on reload and on shutdown.

Reset timing uses the server machine's local time zone. If the machine runs in UTC, the resets are UTC.

---

## Commands

The command is registered as both `/rewards` and `/daily`.

| Command | Permission | Behaviour |
|---|---|---|
| `/rewards` | `dailyrewards.use` or configured hook permission | Open the Server Assist GUI, or show chat status if Server Assist is unavailable |
| `/daily` | Same as `/rewards` | Alias of `/rewards` |
| `/rewards claim <daily\|weekly\|monthly\|seasonal>` | Use permission plus the tier's optional `Permission` | Claim a tier directly from chat |
| `/rewards status [player]` | Use permission; `dailyrewards.admin` to inspect another player | Show total playtime, daily streak and every tier's status |
| `/rewards playtime [player]` | Use permission; `dailyrewards.admin` to inspect another player | Show tracked playtime and current period counters |
| `/rewards grant <player name or SteamID> <tier>` | `dailyrewards.admin` | Grant a tier without consuming the target's claim; target must be online and spawned |
| `/rewards grantall <tier>` | `dailyrewards.admin` | Grant a tier to every online, spawned player |
| `/rewards reset <player name or SteamID> <tier>` | `dailyrewards.admin` | Clear one player's current claim; works for offline players with stored data |
| `/rewards resetall <tier>` | `dailyrewards.admin` | Clear the current claim for every stored player |
| `/rewards season` | `dailyrewards.admin` | Show the current season mode and season name |
| `/rewards season <name>` | `dailyrewards.admin` | Start a manual season and immediately create a new seasonal period |
| `/rewards season auto` | `dailyrewards.admin` | Return to calendar seasons |
| `/rewards reload` | `dailyrewards.admin` | Flush playtime, save data, reload config and data, and re-register permissions and the panel |
| `/rewards help` | None | Show the command list |

`/rewards info` is an alias of `status`, and `/rewards time` is an alias of `playtime`.

Commands also work from the server console where applicable. Admin grants still require an online, spawned player because reward items need an inventory.

---

## Permissions

| Permission | Purpose |
|---|---|
| `dailyrewards.use` | Use the Daily Rewards GUI and claim normal rewards. Granted to the default `players` group when `GrantUseToDefaultGroup` is true |
| `dailyrewards.admin` | Use Admin Tools and all admin commands. Granted to the default `administrators` group automatically |
| `dailyrewards.<anything>` | Any tier `Permission` or reward `Permissions` entry that starts with `dailyrewards.` is registered by this plugin |
| Tier `Permission` | Optional per-tier node. A player must have the use permission (or configured `HookPermission`) and the tier node to claim it |

Permission nodes owned by other plugins are not registered by Daily Rewards. Grant those with their owner plugin, PermissionGui or the normal Oxide permission commands. Administrators bypass the use and tier checks for admin grants.

---

## Configuration

File: `oxide/config/DailyRewards.json`

| Key | Default | Notes |
|---|---|---|
| `ChatPrefix` | `[Rewards]` | Prefix for chat replies |
| `GrantUseToDefaultGroup` | `true` | Grant `dailyrewards.use` to the default `players` group |
| `HookLabel` | `Daily Rewards` | Server Assist card label |
| `HookDescription` | `Claim daily, weekly, monthly and seasonal rewards.` | Server Assist card description |
| `HookPermission` | `dailyrewards.use` | Card permission; may be blank to show the card to everyone who can open Server Assist |
| `PanelIcon` | `sprite:sa_daily_rewards` | Server Assist icon, background and banner for the card |
| `PanelOrder` | `30` | Sort order on the Server Assist home screen. Lower numbers appear first; clamped to 0–1000 |
| `NotifyWhenReady` | `true` | Send a chat message when the daily reward becomes claimable |
| `SaveIntervalSeconds` | `300` | Player-data write interval; clamped to 30–3600 |
| `MaxTickAccrualSeconds` | `300` | Safety cap for one playtime tick; clamped to 60–86400. The normal tick is 60 seconds |
| `DailyResetHour` | `0` | Hour of the server's local day when periods roll over; normalized to 0–23 |
| `WeeklyResetDay` | `Monday` | Day-of-week name for weekly resets |
| `SouthernHemisphere` | `false` | Swap calendar season names for the southern hemisphere; boundary months stay the same |
| `SeasonMode` | `calendar` | `calendar` or `manual` |
| `ManualSeason` | `Season 1` | Season name used while `SeasonMode` is `manual` |

Each of the four tier objects (`Daily`, `Weekly`, `Monthly`, `Seasonal`) has the same shape:

| Tier key | Default | Notes |
|---|---|---|
| `Enabled` | `true` | Turn the tier on or off |
| `DisplayName` | `Daily`, `Weekly`, `Monthly`, `Seasonal` | Name shown in the GUI and messages |
| `Icon` | Tier-specific `sprite:` key | Server Assist row icon |
| `RequiredPlaytimeMinutes` | `60` for Daily, `0` for the others | Playtime required in the current period; clamped to 0–10080 |
| `Permission` | blank | Optional extra permission node for the tier |
| `Items` | Tier-specific item list | Each entry has `Item`, `Count` and `Quality` |
| `Commands` | empty | Server console commands run when the reward is granted |
| `Permissions` | empty | Permission nodes granted to the player as part of the reward |

Default tier icons:

| Tier | Icon |
|---|---|
| Daily | `sprite:sa_daily_rewards` |
| Weekly | `sprite:sa_act_time` |
| Monthly | `sprite:sa_nav_snapshots` |
| Seasonal | `sprite:sa_act_weather` |

Default item rewards use `casinoCoin` (dukes) and `medicalFirstAidBandage`:

| Tier | `casinoCoin` | `medicalFirstAidBandage` |
|---|---|---|
| Daily | 10 | 1 |
| Weekly | 75 | 3 |
| Monthly | 300 | 10 |
| Seasonal | 1500 | 20 |

Reward item fields:

| Field | Meaning |
|---|---|
| `Item` | Internal item name, display name, or a recognizable partial name |
| `Count` | Number of items, at least 1; clamped to 1–100000 |
| `Quality` | Quality for items that support it, 0–6; 0 means the item's default 6 |

Reward commands support these placeholders:

| Placeholder | Value |
|---|---|
| `{playerid}` | Player's platform/Steam ID |
| `{steamid}` | Same as `playerid` |
| `{entityid}` | Current game entity ID |
| `{name}` | Player display name |
| `{player}` | Same as `name` |
| `{tier}` | `daily`, `weekly`, `monthly` or `seasonal` |

Wrap placeholders in quotes when a player name may contain spaces, for example `say "Reward claimed by {name}"`.

If a reward item cannot be found, the plugin logs one warning per item name and skips that item. If every item is invalid and there are no commands or permissions, the claim is not consumed. If the inventory is full, existing stacks are filled first and the remainder drops at the player's feet; the claim is still consumed because the reward was delivered.

---

## Data files

| Path | Purpose |
|---|---|
| `oxide/config/DailyRewards.json` | Runtime configuration |
| `oxide/data/DailyRewards/players.json` | Stored playtime, period counters and claim state |
| `Config/DailyRewards.example.json` | Optional reference copy shipped in the package |

!!! warning
    Do not edit `players.json` while the server is running. Use the admin commands instead.

---

## Server Assist integration

When Server Assist is loaded, Daily Rewards registers the panel id `dailyrewards` through `RegisterPanel` with the configured label, description, permission, icon and order. The card appears on the Server Assist home screen for players with the configured `HookPermission`.

The panel handler is:

```csharp
public string HandleGuiRequest(string playerId, string payload)
```

It accepts the standard `a|open`, `a|<action>` and `t|<encoded text>` payloads and returns the Server Assist v2 view protocol with 9-field rows:

```text
row=index|action|label|sub|enabled|selected|icon|badge|tint
```

The overview shows all four tiers with status, reward contents, playtime progress and next reset. Clicking a tier row opens details, and claim buttons grant the tier when the player is eligible. Administrators also get an **Admin Tools** row for granting a tier without using a claim or resetting a player's current claim.

`RegisterPanel` and the v2 icon, badge and tint fields require Server Assist 2.0 or newer. On older 1.x installs the plugin falls back to `RegisterGuiHook` followed by `SetPanelIcon`, but the per-row icon fields are ignored by that client. The plugin re-registers on `OnServerAssistReady`, opens through `OpenPanel` with an `OpenHookForPlayer` fallback, and unregisters on unload. It adds no XUi windows of its own.

See [Panels and hooks](../../integration/api/panels.md) and the [Server Assist product guide](../../core/server-assist.md) for the registration and view protocol.

---

## Typical workflows

### A player claims the daily reward

1. The player connects and the plugin starts a playtime session.
2. The player checks `/rewards playtime` or opens the panel to see progress.
3. Once the current daily period reaches the configured playtime, the row shows **Ready**.
4. The player clicks the claim button or runs `/rewards claim daily`.
5. Items are added to the inventory, commands and permissions run, and the claim is recorded for that period.

### An administrator hands out a reward

1. Make sure the target is online and spawned.
2. Run `/rewards grant <player> weekly`.
3. The grant does not consume the player's normal weekly claim.
4. To let the player claim again normally, run `/rewards reset <player> weekly`.

### Rotating a manual season

1. Set `SeasonMode` to `manual`, or run `/rewards season Season 1`.
2. Later run `/rewards season Season 2`.
3. Every name change creates a fresh seasonal period, so everyone can claim the new seasonal reward.
4. Run `/rewards season auto` to return to calendar seasons.

### Restricting a tier to a group

1. Add a permission such as `dailyrewards.vip` to the tier's `Permission` field.
2. Reload with `/rewards reload`.
3. Grant `dailyrewards.vip` through PermissionGui or the Oxide permission commands.
4. Members now meet the extra tier check in addition to the use permission.

### Calling Daily Rewards from another plugin

```csharp
object result = plugins.Find("DailyRewards").Call(
    "GrantReward", playerId, "weekly");
```

---

## Public API

| Method | Behaviour |
|---|---|
| `GrantReward(string playerId, string tierKey)` | Grant a tier without using the player's claim; the player must be online |
| `ClaimReward(string playerId, string tierKey)` | Run normal claim checks, including permission, claim state and playtime |
| `ResetReward(string playerId, string tierKey)` | Clear the player's current claim for one tier |
| `GetPlaytimeSeconds(string playerId)` | Return total tracked playtime in seconds |
| `GetPeriodPlaytimeSeconds(string playerId, string tierKey)` | Return playtime in the current tier period in seconds |
| `GetRewardStatus(string playerId, string tierKey)` | Return a short status string such as `Ready to claim` or `Locked \| 12m 00s / 1h 00m played this period` |

---

## Troubleshooting

| Symptom | Check |
|---|---|
| The `[GUI] Daily Rewards` row does not appear | Confirm Server Assist is loaded, the player has `dailyrewards.use` or the configured `HookPermission`, and the plugin compiled. Reload Server Assist or run `/rewards reload` |
| The card or rows show the default hammer | The client is missing the sprite. Copy `ServerAssistArt/UIAtlases/UIAtlas` into the Server Assist mod folder on that client, then restart the game. Confirm the sprite key matches a PNG name without `.png` |
| The GUI says the player has no permission | Grant `dailyrewards.use` to the default player group, or change `HookPermission` to an existing node. Admins get access through `dailyrewards.admin` |
| The daily reward stays locked | Playtime is counted in the current daily period only. Check `/rewards playtime`. Time from before the plugin was installed is not credited retroactively |
| Items did not appear | Check the server log for `reward item ... was not found`. Use the exact item name from `listitems`. If the inventory was full, check the player's feet |
| The daily reward was claimed but the player wants it again | Run `/rewards reset <player> daily`. Admin grants through `/rewards grant` do not consume the normal claim |
| Weekly, monthly or seasonal rewards reset at the wrong time | These use the server's local time zone and `DailyResetHour`. Check the machine's time zone |
| A reward command did not run | Check the server log for the failed command and its message. Verify the placeholders and quoting in the config |

---

## Related pages

- [Server Assist product guide](../../core/server-assist.md)
- [Panels and hooks](../../integration/api/panels.md)
- [Companion protocols](../../integration/api/companion-protocols.md)
- [Companion problems](../../reference/troubleshooting/companions.md)
- [Icons](../../core/icons.md)
