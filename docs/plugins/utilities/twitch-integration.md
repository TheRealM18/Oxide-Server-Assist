# Twitch Integration

> **Version:** 0.2.1 · **Type:** Server-side Oxide plugin plus a standalone .NET 8 relay · **Source:** [TwitchIntegration.cs](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/TwitchIntegration/oxide/plugins/TwitchIntegration.cs)

Twitch Integration links a streamer's Twitch channel to their server character, exposes permission tiers, and lets the streamer shortlist buffs, debuffs, and rewards for each tier. When a viewer event fires, the server rolls one effect from each category and applies it.

Bits, subscriptions, gift subs, resubs, channel point redemptions, automatic rewards, raids, follows, and hype trains are supported.

!!! warning
    The standalone relay is a hard dependency for linking and live events. Without it the Oxide plugin still loads and its panel opens, but it cannot start a link, receive events, or test the relay connection.

---

## Architecture

The relay owns the Twitch connection: device-code OAuth, an encrypted token vault, EventSub subscriptions, deduplication, and per-server queues. The Oxide plugin polls the relay over HTTPS with HMAC, then handles links, tiers, loadouts, cooldowns, effects, and audit. Effects run through `buffplayer`, `debuffplayer`, `give`, whitelisted console commands, or the `OnTwitchEffect` / `OnTwitchEffectAction` hooks.

The game server never holds Twitch tokens and never opens an inbound port. The relay connects outbound to Twitch; the game server connects outbound to the relay. Staff define reward pools, weights, and trigger thresholds. Oxide permissions decide tier eligibility. Streamers shortlist candidates; the server rolls one per category.

---

## Requirements

| Requirement | Detail |
|---|---|
| Game server | 7 Days to Die with Oxide/uMod; EAC off |
| Relay | .NET 8 service, published self-contained for Linux x86_64 or Windows for testing |
| Twitch application | One developer application with a Client ID; Client Secret recommended for refresh |
| Twitch status | Affiliate or Partner for subs, gift subs, Bits, and channel points; follows, raids, and chat work for any channel |
| Server Assist | Server plugin plus the Server Assist client mod for the panel |
| Art pack | ServerAssistArt for `sa_stream_*` sprites; missing sprites fall back to vanilla |
| Network | Outbound game server → relay; outbound relay → Twitch |

### Twitch application setup

Sign in at <https://dev.twitch.tv/console/apps>, create an application, and copy its Client ID (and Client Secret for refresh-token support) into `relay.json`. The device-code flow does not use a redirect URL, so `http://localhost` is a fine placeholder. Each streamer authorizes that one application individually; no streamer password, stream key, or account sharing is involved. A separate bot account is only needed for Twitch chat output; this version notifies in game chat.

---

## Installation

### 1. Build and run the relay

The published build is self-contained, so the Linux server does not need .NET installed.

```bash
# On the Linux server (recommended)
cd TwitchIntegration/relay && chmod +x publish.sh && ./publish.sh

# Or cross-publish from Windows
cd TwitchIntegration
.\relay\publish.ps1 -Runtime linux-x64
```

Output: `relay/bin/Release/net8.0/linux-x64/publish/TwitchRelay`. For Windows testing, `.\relay\publish.ps1 -Runtime win-x64` produces `TwitchRelay.exe`; for Alpine/musl, publish `linux-musl-x64`.

Copy the publish folder to the Linux server, for example `/opt/twitchrelay`:

```bash
cd /opt/twitchrelay && chmod +x TwitchRelay && ./TwitchRelay
```

First run creates `relay.json` next to the binary, generates a random `ApiKey` for the `main` server, and creates `data/secret.key`. Stop it, edit `relay.json` with the Client ID, Client Secret, and at least one `Servers` entry, then start it again. The full key list is in [Relay configuration](#relay-configuration).

```bash
chmod 600 relay.json && chmod 700 data
```

Run it with the supplied `TwitchRelay.service`; adjust `User=`, `Group=`, and the `/opt/twitchrelay` paths in the unit file for your account. The service account must own `relay.json` and `data/`.

### 2. Install the Oxide plugin

1. Copy `oxide/plugins/TwitchIntegration.cs` to `oxide/plugins/`.
2. Load it once with `oxide.reload TwitchIntegration` or a restart.
3. Edit `oxide/config/TwitchIntegration.json`: set `RelayUrl` (for example `http://127.0.0.1:8090`), `ServerId` (must match `Servers[].Id`), and `ServerKey` (the same `ApiKey`).
4. Reload the plugin.
5. Make sure Server Assist is installed and each client has the Server Assist mod.

The plugin registers a **Twitch** panel for players with `twitch.use`.

### 3. Link a streamer

```text
/twitch link
```

The panel or chat shows a code and `twitch.tv/activate`. After approval, the plugin binds the Twitch user ID to the Steam ID, grants the `twitch` group (configurable), and opts the streamer in.

### 4. Point a remote server at the relay

The game server needs outbound access to the relay only. The relay needs outbound access to Twitch. No inbound game-server ports are required.

Use `/twitch` → **Admin** and set **Relay URL**, **Server ID**, and **Server Key**, then **Test Relay**. Values save immediately, and the key is not displayed again. The command alternative is:

```text
/twitch connect https://relay.example.com <apiKey> main
/twitch relaytest
```

The command puts the key in chat and command logs, so prefer the panel in production. Recommended topologies:

- **VPS plus HTTPS** — keep `Listen` on `http://127.0.0.1:8090`, put Caddy or nginx in front, open 443 to the game server.
- **Tailscale / WireGuard** — bind the relay to the tailnet address; plain HTTP is fine inside the tunnel.
- **Cloudflare Tunnel** — run `cloudflared tunnel --url http://127.0.0.1:8090` and use the HTTPS URL.

Check `https://relay.example.com/health` from the game server, then run `/twitch relaytest`.

!!! warning
    Never expose plain HTTP to the internet. HMAC protects integrity, not confidentiality. Keep NTP running on both hosts; signed requests are accepted within a five-minute window.

---

## Commands

| Command | Permission | Purpose |
|---|---|---|
| `/twitch` | `twitch.use` | Open the panel |
| `/twitch link` | `twitch.use` | Start a Twitch link |
| `/twitch unlink` | `twitch.use` | Remove the link |
| `/twitch on` / `/twitch off` | `twitch.use` | Opt in or out of effects |
| `/twitch pause` / `/twitch resume` | `twitch.use` | Pause or resume effects |
| `/twitch status` | `twitch.use` | Show link and loadout summary |
| `/twitch loadout` | `twitch.use` | Open the tier loadout page |
| `/twitch catalog` | `twitch.use` | Open the reward catalog |
| `/twitch help` | `twitch.use` | Show command help |
| `/twitch connect <url> <key> [serverId]` | `twitch.admin` | Set the remote relay destination |
| `/twitch relaytest` | `twitch.admin` | Test the relay URL and key |
| `/twitch reload` | `twitch.admin` | Reload config and permissions |
| `/twitch reconcile` | `twitch.admin` | Ask the relay to recreate EventSub subscriptions |
| `/twitch test <type> [amt]` | `twitch.admin` | Fire a local test event |
| `/twitch list` | `twitch.admin` | List linked accounts |
| `/twitch who <player>` | `twitch.admin` | Inspect a profile |
| `/twitch tier <player> <id>` | `twitch.admin` | Grant a tier permission |
| `/twitch untier <player> <id>` | `twitch.admin` | Revoke a tier permission |
| `/twitch icons [page]` | `twitch.admin` | List icon roles and mappings |
| `/twitch iconset <role> <value>` | `twitch.admin` | Remap one icon role |
| `/twitch forcelink <player> <twitchLogin>` | `twitch.admin` | Create a local-only test link |

---

## Permissions

| Permission | Purpose |
|---|---|
| `twitch.use` | Open the panel and use player commands |
| `twitch.streamer` | Granted automatically when a channel is linked, through the `twitch` group by default |
| `twitch.tier.<id>` | Staff-controlled tier eligibility, for example `twitch.tier.tier1` |
| `twitch.admin` | Relay status, audit, test events, tier grants, icon remapping, relay destination edits |

A tier is eligible when the player has the tier permission; admins bypass the check. An empty tier `Permission` is filled with `TierPermissionPrefix` plus the tier id. Staff can grant tiers with PermissionGui or `/twitch tier`.

---

## Configuration

File: `oxide/config/TwitchIntegration.json`

| Key | Default | Notes |
|---|---|---|
| `ConfigVersion` | `0` | Migrated to `3` on load; controls icon/default migrations |
| `ChatPrefix` | `"[Twitch]"` | Prefix for chat replies |
| `GrantUseToDefaultGroup` | `true` | Grant `twitch.use` to the default players group |
| `GrantAdminToAdminGroup` | `true` | Grant `twitch.admin` to the default admin group |
| `GrantStreamerOnLink` / `RevokeStreamerOnUnlink` | `true` / `true` | Grant or remove the streamer group with the link |
| `StreamerGroup` / `StreamerGroupTitle` / `StreamerGroupRank` | `"twitch"` / `"Twitch Streamer"` / `0` | Linked-streamer group |
| `HookLabel` / `HookDescription` | `"Twitch"` / `"Link Twitch, choose reward shortlists, and see what can trigger."` | Server Assist panel text |
| `HookPermission` | `"twitch.use"` | Panel permission |
| `PanelIcon` / `PanelOrder` | `"sprite:sa_stream_panel"` / `40` | Legacy panel icon; `Icons.Panel` is authoritative |
| `Icons` | built-in defaults | Logical role to icon value mapping |
| `RelayUrl` | `"http://127.0.0.1:8090"` | Scheme is added if missing; trailing slash trimmed |
| `ServerId` / `ServerKey` | `"main"` / `""` | Must match the relay `Servers` entry |
| `PollIntervalSeconds` / `LinkPollIntervalSeconds` / `RequestTimeoutSeconds` | `3` / `3` / `20` | Clamped to 1–60, 2–60, and 5–120 |
| `TierPermissionPrefix` | `"twitch.tier."` | Used when a tier has no explicit permission |
| `DefaultTierId` | `"tier1"` | Tier used by fallback triggers |
| `RequireAllCategories` | `true` | Require `MinPicks` in every non-empty pool before arming |
| `AllowBuffs` / `AllowDebuffs` / `AllowRewards` | `true` / `true` / `true` | Global category switches |
| `AllowAnonymousCheers` | `true` | Accept anonymous Bits cheers |
| `MaxEventAgeMinutes` | `15` | Drop stale events; clamped to 1–1440 |
| `GlobalCooldownSeconds` / `PerViewerCooldownSeconds` | `15` / `60` | Clamped to 0–3600 |
| `MaxEffectsPerMinute` | `10` | Clamped to 1–120 |
| `QueueWhileOffline` | `false` | Queue effects while the streamer is offline |
| `PendingEffectTtlMinutes` | `30` | Clamped to 1–1440 |
| `AnnounceEffects` | `true` | Announce applied effects in game chat |
| `FallbackToDefaultTier` | `true` | Use `DefaultTierId` when no trigger matches a fallback event |
| `FallbackEventTypes` | `["bits", "sub", "gift_sub", "resub", "raid"]` | Event types eligible for fallback |
| `AllowedCommandPrefixes` | `["buffplayer", "debuffplayer", "give", "chaos", "weather", "spawn", "say"]` | First-token whitelist for `command` actions |
| `Tiers` | three default tiers | Pools, weights, icons, and pick limits |
| `Triggers` | default trigger set | Thresholds, filters, priorities, cooldowns |

### Relay configuration

`relay.json` is created next to the relay binary. The example file uses lower-case keys:

| Key | Default | Notes |
|---|---|---|
| `clientId` | required | Twitch application Client ID |
| `clientSecret` | optional | Recommended for token refresh; keep it on the relay |
| `eventSubUrl` | `wss://eventsub.wss.twitch.tv/ws` | Do not change unless Twitch announces a new endpoint |
| `listen` | `http://127.0.0.1:8090` | Bind loopback when relay and game server share a host |
| `dataDir` | `data` | Token vault and event queue directory |
| `linkSessionMinutes` / `eventRetentionHours` / `maxEventsPerServer` | `10` / `72` / `2000` | Link lifetime and event retention |
| `httpTimeoutSeconds` / `reconcileMinutes` | `20` / `5` | Outbound timeout and subscription reconcile interval |
| `enableChat` / `enableFollows` / `enableHypeTrain` | `false` / `true` / `true` | Optional event topics |
| `requestManageRedemptions` | `false` | Adds `channel:manage:redemptions`; only for auto-fulfill |
| `servers` | required | One entry per game server with `id`, `name`, and `apiKey` |

Add one `servers` entry per game server. `ClientSecret` is optional for device-code linking but recommended for refresh support. If the 7DtD server runs in a container, `127.0.0.1` means the container itself; bind to the Docker bridge or host and firewall it.

---

## Data files

| Path | Purpose |
|---|---|
| `oxide/plugins/TwitchIntegration.cs` | The Oxide plugin |
| `oxide/config/TwitchIntegration.json` | Configuration, created on first load |
| `oxide/data/TwitchIntegration/players.json` | Links, loadouts, cooldowns, pending effects, processed event IDs, audit |
| `relay.json` | Twitch credentials, listen address, per-server API keys |
| `data/secret.key` | AES key for the token vault; protect it |
| `data/tokens.enc` | Encrypted Twitch OAuth tokens |
| `data/events/*.json` | Pending events per server |

A player profile stores the Steam ID, player name, Twitch user ID and login, link time, opt-in and pause state, category toggles, loadouts by tier, viewer and trigger cooldowns, pending effects, and last effect time. Link sessions are keyed by Steam ID and expire. `ProcessedEventIds` is a bounded ring of 2,000 IDs; `Audit` is a bounded list of 200 lines.

The relay encrypts tokens at rest with AES-GCM in `data/tokens.enc` and keeps the key in `data/secret.key`.

---

## Server Assist integration

| Item | Value |
|---|---|
| Panel id | `twitch` |
| Label | `Twitch` (`HookLabel`) |
| Permission | `twitch.use` (`HookPermission`) |
| Handler | `HandleGuiRequest` |
| Entry | `/twitch`, Home row |
| Icon | `sprite:sa_stream_panel` (`Icons.Panel`) |
| Order | `40` (`PanelOrder`) |

Panel screens:

- **Home** — link/unlink, opt-in, pause, category toggles, loadout and catalog buttons.
- **My tier loadouts** — one row per tier; locked tiers show the required permission.
- **Tier** and **Category** — choose buffs, debuffs, or rewards; tap an effect to add or remove it.
- **Reward catalog** — read-only view of every effect staff defined, with descriptions and weights.
- **Admin** — relay URL/server ID/key editor, Test Relay, relay status, link count, pending events, recent audit, reload/reconcile/test buttons.

Registration is idempotent and falls back to `RegisterGuiHook` on older Server Assist builds. The panel unregisters on plugin unload. Chat commands and the relay work without Server Assist; the panel needs the client mod.

---

## Twitch linking

1. The player runs `/twitch link`.
2. The plugin posts `{steamId, playerName}` to the relay's `/api/link/start` with an HMAC signature.
3. The relay requests a Twitch device code and returns a session id, user code, verification URI, and expiry.
4. The player enters the code at `twitch.tv/activate`.
5. The relay polls Twitch, validates the token, fetches the user, and binds the Twitch user ID to the server and Steam ID.
6. The relay creates EventSub subscriptions; the plugin polls `/api/link/status`, saves the link, grants the group, and opts in.

The device code is bound to a Steam ID chosen by the game server, not by the client. The relay only reveals status to the creating server. The code expires and is single-use. One Twitch account maps to one character per server, and one character maps to one Twitch account per server. The streamer can revoke the app at <https://www.twitch.tv/settings/connections> at any time.

When a token is revoked or refresh fails, the relay logs it and the streamer must re-link. Relinking requests the scopes again.

---

## Tiers, loadouts, and rolls

The default tiers use 7 Days to Die's native `twitch_*` buffs:

| Tier | Name | Buffs | Debuffs | Rewards |
|---|---|---|---|---|
| `tier1` | Spark | Quick Feet, Regeneration, Looter's Luck | Heavy Legs, Blurred Vision, No Running | 100 Dukes, 3 First Aid Bandages, 30 9mm Rounds |
| `tier2` | Surge | Melee Surge, Ranged Surge, Infinite Ammo | No Melee, No Ranged, Encumbered | 50 Forged Steel, 40 7.62mm Rounds, 2 Mega Crush |
| `tier3` | Overload | Toughness, XP Gain, Pack Mule | Tougher Enemies, Double Spawns, No Healing | 200 Forged Steel, 60 7.62mm AP Rounds, 3 First Aid Kits |

- `MinPicks` is `1` and `MaxPicks` is `3` for each default tier.
- Each tier permission defaults to `twitch.tier.<id>`.
- `RequireAllCategories` is true, so a loadout needs `MinPicks` valid selections in each non-empty pool.
- An empty pool is treated as "not part of this tier", so a tier can define only rewards.
- Loadout IDs are re-validated on every trigger, so removed effects clean up stale picks.
- If a category is disabled globally or by the streamer, it is skipped and does not block the roll.

When a viewer event fires, the plugin resolves the tier, re-checks `twitch.tier.<id>`, checks cooldowns and rate limits, rolls one effect from each shortlist using `Weight`, applies the effects, and writes an audit line.

Default triggers include Bits at 100/500/1000, sub tiers, resub milestones at 1/6/12 months, gift sub totals at 1/5/20, raids at 10/50/100 viewers, hype train end levels 1/3/5, and catch-all channel point and automatic reward triggers. Follows are disabled by default because they are easy to spam. The catch-all `cp_any` trigger has priority `-10`, so a specific reward trigger at priority `0` or higher wins. Create the actual channel point rewards in the Twitch dashboard; the relay only sees redemptions for rewards that exist.

Chat events are off by default. If `EnableChat` is on at the relay, a chat trigger only matches when it has a `Keyword` and the message contains it. A chat trigger with an empty `Keyword` never fires.

---

## Effect catalog

Each effect has one or more actions:

| Type | Example | Behavior |
|---|---|---|
| `buff` | `twitch_buffFast` | Runs `buffplayer <entityId> "<buff>"` |
| `debuff` | `twitch_buffSlow` | Runs `debuffplayer <entityId> "<buff>"` |
| `item` | `resourceForgedSteel` | Runs `give <entityId> "<item>" <count> [quality]` |
| `command` | `chaos.trick {effect} {steamid}` | Runs a console command whose first token is in `AllowedCommandPrefixes` |
| `hook` | `some_hook_value` | Calls `OnTwitchEffectAction(playerId, effectId, value, category, tierId, viewer, amount)` |

Action fields are `Type`, `Value`, `Count`, `Quality`, and `DelaySeconds`. Supported command tokens: `{player}`, `{steamid}`, `{twitch}`, `{viewer}`, `{amount}`, `{tier}`, `{effect}`, `{reward}`, and `{category}`. `{category}` expands to `buff`, `debuff`, or `reward`.

The plugin also calls this hook for every applied effect:

```csharp
OnTwitchEffect(string playerId, string effectId, string category, string tierId,
               string eventType, string viewerLogin, long amount)
```

That is the integration point for Chaos Tricks or another mod. For Chaos Tricks, a `command` action can use `chaos.trick {effect} {steamid}` after adding `chaos.trick` to `AllowedCommandPrefixes`.

---

## Icons

Every icon resolves through a logical role under `Icons` in `oxide/config/TwitchIntegration.json`:

| Form | Behavior |
|---|---|
| `sprite:name` | Game or mod UI sprite |
| `item:name` | Base-game or mod item icon, resolved by Server Assist's item atlas |
| `atlas:atlasName:sprite` | Explicit custom atlas sprite |
| `plugin:PluginName` | Live icon from another Server Assist panel/provider via `GetPluginIcon` |
| `panel:panelId` | Live icon from a registered panel via `GetPanelIcon` |
| `none` | Vanilla fallback symbol |

Resolution order: `Icons[role]`, then a live `plugin:`/`panel:` lookup, then `DefaultIconMap[role]`. Missing roles are filled on load. If the target plugin or panel is not loaded, the role falls back to its static sprite, so AdminSuite, PermissionGui, and PluginHotReload do not need new Twitch-specific textures.

Key defaults:

| Role | Default |
|---|---|
| Panel / Home | `sprite:sa_stream_panel`, `sprite:sa_stream_home` |
| Tier1 / Tier2 / Tier3 / fallback | `sprite:sa_stream_tier_1`, `_2`, `_3`, `ui_game_symbol_star` |
| Buff / Debuff / Reward | `ui_game_symbol_twitch_buffs`, `ui_game_symbol_twitch_debuffs`, `sprite:sa_stream_reward` |
| Loadout group | `sprite:sa_stream_loadout`, `_armed`, `ui_game_symbol_lock` |
| Link group | `sprite:sa_stream_link`, `_pending`, `_linked`, `sa_stream_unlink`, `_cancel` |
| Admin / Reload / Links | `plugin:AdminSuite` → `sprite:sa_admin_suite`; `plugin:PluginHotReload` → `sprite:sa_plugin_hot_reload`; `plugin:PermissionGui` → `sprite:sa_permission_gui` |
| Players / Audit / Relay | `sprite:sa_nav_players`, `sprite:sa_nav_logs`, `sprite:sa_nav_server` with status tint |

Other roles use `sprite:sa_announce_test`, `item:casinoCoin`, `ui_game_symbol_star`, `ui_game_symbol_check`, `ui_game_symbol_x`, `ui_game_symbol_clock`, and `sprite:sa_nav_help`.

Rewards default to item icons, so no new reward textures are required:

```json
{ "Id": "dukes_100", "Icon": "item:casinoCoin" }
{ "Id": "medkit_3", "Icon": "item:medicalFirstAidKit" }
```

Per-tier and per-effect icons override the role. `PanelIcon` is kept for older configs; `Icons.Panel` is authoritative. `ConfigVersion 3` migrates the 0.2.0 streaming defaults to live hooks, mod art, and item icons while preserving custom mappings. `sa_stream_*` sprites come from `ServerAssistArt/UIAtlases/UIAtlas/`; install the pack as `AAZ_ServerAssistArt` or copy the PNGs into the Server Assist mod's `UIAtlases/UIAtlas/`.

---

## Admin tools

`/twitch connect`, `/twitch relaytest`, `/twitch reload`, `/twitch reconcile`, `/twitch test`, `/twitch list`, `/twitch who`, `/twitch tier`, `/twitch untier`, `/twitch icons`, `/twitch iconset`, and `/twitch forcelink` cover relay setup, diagnostics, links, tier grants, icon remapping, and local test links.

The panel's **Admin** screen also exposes relay URL/server ID/key editing, Test Relay, relay status, link count, pending events, recent audit, and reload, reconcile, and test buttons. The key is written straight to config and is not shown again after submission.

---

## Typical workflows

- **First-time setup:** create the Twitch application; publish the relay for `linux-x64` or `linux-musl-x64`; run it once to generate `relay.json` and `data/secret.key`; add the credentials and a `servers` entry; install the plugin and set `RelayUrl`, `ServerId`, and `ServerKey`; run `/twitch relaytest`.
- **Link and arm a streamer:** the streamer runs `/twitch link`, approves the code at `twitch.tv/activate`, then opens `/twitch loadout` and shortlists effects for each unlocked tier. The plugin grants the `twitch` group and opts them in.
- **Staff define and grant a tier:** edit `Tiers`, add effects with IDs, labels, descriptions, icons, weights, and actions, set `MinPicks` and `MaxPicks`, run `/twitch reload`, then grant `twitch.tier.<id>` with PermissionGui, `/twitch tier`, or your permission manager.
- **Viewer triggers a tier:** the relay normalizes, deduplicates, and queues the event; the plugin polls it, picks the best trigger by priority then minimum amount, re-checks permission and cooldowns, rolls the shortlists, applies the effects, and writes an audit line.

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| 401 from the relay | `ServerId` and `ServerKey` must match the relay `Servers` entry exactly; keep clocks within five minutes; restart the relay after editing `relay.json` |
| `/twitch link` says the relay is not configured | Set `RelayUrl` and `ServerKey`, then run `/twitch reload` |
| Relay says `Twitch ClientId is not configured` | Create a Twitch application and put its Client ID in `relay.json` |
| `Name or service not known (eventsub.wttv.tv:443)` | Older relay build; the current host is `wss://eventsub.wss.twitch.tv/ws` |
| The link code expires | Device codes are short-lived by design; run `/twitch link` again |
| No subscriptions in the relay log | The channel may not be Affiliate/Partner; re-link to request scopes; check optional features in `relay.json` |
| Linux OpenSSL/libssl error | Install `libssl3` on Ubuntu 22.04+/Debian 12, or `libssl1.1` on older distributions |
| Icons show as vanilla symbols | Install ServerAssistArt or copy the `sa_stream_*` PNGs; check `/twitch icons` and remap with `/twitch iconset` |
| Refunded redemption kept its effect | Effects apply on `add`; `channel_points_update` is ignored, so refunds are not reversed |
| Effects fire twice | The game's own `Data/Config/twitch.xml` system may also apply effects; use different buffs or disable one system |

Known limitations: channel point refunds are not reversed; the native 7DtD Twitch system may double-apply; one relay process handles all broadcasters; there is no web admin page; there is no Twitch chat output.

---

## Related pages

- [Server Assist API](../../integration/api/index.md)
- [Companion plugin integration](../../integration/companions/index.md)
- [Server Assist product guide](../../core/server-assist.md)
- [Icons](../../core/icons.md)
- [Permissions](../../core/permissions.md)
- [Troubleshooting](../../reference/troubleshooting/index.md)
