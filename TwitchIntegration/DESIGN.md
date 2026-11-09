# Twitch Integration — Design

This document records the decisions behind the implementation so staff and future contributors can change it safely.

## 1. Goals

- Server owners define the reward pools, not the streamer.
- Streamers self-link a Twitch channel. No stream key, no password, no account sharing.
- Oxide permissions define who can use which tier.
- Each tier requires a buff, a debuff, and a reward slot.
- Streamers shortlist multiple candidates per slot.
- The server rolls one candidate per slot when a viewer event fires.
- Every event type the Twitch API exposes should be available: Bits, subs, gift subs, resubs, channel points, automatic rewards, raids, follows, and hype trains.
- Other mods (Chaos Tricks, weather, spawns, etc.) can be wired in through whitelisted console commands or Oxide hooks.

## 2. Non-goals

- Replacing the game's native Twitch extension.
- Letting viewers choose which effect fires.
- Letting the streamer or viewer edit staff pools from the game client.
- Holding Twitch credentials inside the game server.

## 3. Why a relay

Oxide's `webrequest` library is request/response. It can poll an HTTP API, but it is not a good place for:

- long-lived EventSub WebSockets,
- token refresh and reconnect loops,
- a Twitch Client Secret,
- multiple game servers sharing one Twitch app.

The relay is a small ASP.NET Core service. It can run on the same machine as the game server (bind to `127.0.0.1`) or remotely (HTTPS + firewall). The game plugin only needs outbound HTTP.

## 4. Linking flow

```text
Player: /twitch link
  -> plugin POST /api/link/start {steamId, playerName}   (HMAC)
  -> relay POST https://id.twitch.tv/oauth2/device       (client_id + scopes)
  <- relay returns {sessionId, userCode, verificationUri, expiresIn}
  -> plugin shows "go to twitch.tv/activate and enter CODE"

Streamer approves the code on Twitch
  -> relay polls /oauth2/token until authorized
  -> relay validates the token, fetches the Helix user, binds twitchUserId -> serverId+steamId
  -> relay creates EventSub subscriptions for the broadcaster
  <- plugin polls /api/link/status and receives {status:"linked", twitchUserId, login}
  -> plugin saves the link, grants the `twitch` group, opts in
```

Security properties:

- The device code is bound to a Steam ID chosen by the game server, not by the client.
- The relay only reveals status to the server that created the session.
- The link expires; the code is single-use.
- One Twitch account maps to one character per server. One character maps to one Twitch account per server.
- The streamer can revoke the app at Twitch at any time.

## 5. Event pipeline

```text
EventSub notification
  -> relay verifies the WebSocket session and metadata.message_id
  -> normalizes to RewardEvent
  -> for each server linked to broadcaster_user_id: queue the event
  -> plugin GET /api/events/pending
  -> plugin processes, then POST /api/events/ack {ids}
```

`RewardEvent` fields:

```text
id, type, rawType, broadcasterUserId, broadcasterLogin,
viewerUserId, viewerLogin, viewerName, amount,
rewardId, rewardTitle, redemptionId, status, subTier,
isGift, isAnonymous, occurredAt, receivedAt, extra
```

Normalized types:

| EventSub type | `type` | `amount` |
|---|---|---|
| `channel.cheer` | `bits` | Bits cheered |
| `channel.subscribe` | `sub` | 1 |
| `channel.subscription.gift` | `gift_sub` | Number gifted |
| `channel.subscription.message` | `resub` | Cumulative months |
| `channel.channel_points_custom_reward_redemption.add` | `channel_points` | Reward cost |
| `.update` | `channel_points_update` | Reward cost |
| `channel.channel_points_automatic_reward_redemption.add` | `automatic_reward` | Reward cost |
| `channel.raid` | `raid` | Viewer count |
| `channel.follow` | `follow` | 0 |
| `channel.hype_train.begin/progress/end` | `hype_train` | Level; `extra.phase` |
| `channel.chat.message` | `chat` | 0; optional |

Dedupe and reliability:

- The relay drops duplicate `metadata.message_id` per server.
- The relay persists unacknowledged events and replays them after restart.
- The plugin keeps a bounded history of processed IDs (2,000) and drops stale events (`MaxEventAgeMinutes`).
- Events are acknowledged even when ignored, so a misconfigured trigger cannot create an infinite loop.

## 6. Data model

Config (`oxide/config/TwitchIntegration.json`):

```text
TierDefinition
  Id, DisplayName, Description, Permission, Enabled, Order, Icon
  MinPicks, MaxPicks
  Buffs[], Debuffs[], Rewards[]

EffectDefinition
  Id, Label, Description, Icon, Weight
  Actions[] { Type, Value, Count, Quality, DelaySeconds }

TriggerDefinition
  Id, Event, Enabled, TierId, MinAmount
  RewardId, RewardTitle, MatchAnyReward
  SubTier, RequireGift, RequireNotGift
  Phase, Priority, CooldownSeconds, Description
```

Player data (`oxide/data/TwitchIntegration/players.json`):

```text
PlayerProfile
  SteamId, PlayerName
  TwitchUserId, TwitchLogin, TwitchDisplayName, LinkedAtUtc
  OptedIn, Paused, AllowBuffs, AllowDebuffs, AllowRewards
  Loadouts[tierId] { Buffs[], Debuffs[], Rewards[], UpdatedAtUtc }
  ViewerCooldowns[], TriggerCooldowns[]
  PendingEffects[]
  LastEffectUtc
```

`LinkSessions` are keyed by Steam ID and expire. `ProcessedEventIds` is a bounded ring. `Audit` is a bounded list rendered in the admin panel.

## 7. Tier and loadout semantics

- A tier is eligible when the player has `tier.Permission` (admins bypass).
- `IsTierArmed` requires at least `MinPicks` valid selections in each non-empty pool when `RequireAllCategories` is true.
- An empty pool is treated as "not part of this tier", so staff can define a tier with only rewards if they want.
- `MaxPicks` caps the shortlist per category. Default 3.
- At trigger time, `PickEffect` uses weighted random over the shortlist. Staff set `Weight` per effect.
- If a category is disabled globally or by the streamer, it is skipped and does not block the roll.
- Loadout IDs are re-validated against the current config on every trigger, so removing an effect from staff config cleans up stale picks.

Recommended staff pattern:

```text
Tier 1: 2 buffs, 2 debuffs, 2 rewards in the pool; streamer picks 1-2 per category.
Tier 2: stronger pools; streamer picks 1-3 per category.
Tier 3: high-impact pools; keep MaxPicks low so the roll stays readable.
```

## 8. Trigger resolution

`FindTrigger` walks enabled triggers and picks the best match by:

1. higher `Priority` wins,
2. then higher `MinAmount` wins.

Event matching:

- `bits`: `amount >= MinAmount`.
- `sub`: `SubTier` (0 = any), `RequireGift`/`RequireNotGift`.
- `resub`: `amount` = cumulative months.
- `gift_sub`: `amount` = number gifted.
- `channel_points` / `automatic_reward`: `MatchAnyReward`, or exact `RewardId`, or exact `RewardTitle`.
- `raid`: `amount` = viewers.
- `follow`: no amount.
- `hype_train`: `Phase` (`begin`/`progress`/`end`), `amount` = level.

Fallback: if no trigger matches and the event type is in `FallbackEventTypes`, the `DefaultTierId` is used. This keeps Bits/subs/gifts/raids working even if staff forget to map a threshold. Channel points are intentionally not in the fallback list so an unrelated redemption ("hydrate") does not fire a reward until staff map it.

## 9. Security threat model

| Threat | Control |
|---|---|
| Stream key theft | Never requested; OAuth only |
| Twitch password phishing | Streamer types the code on Twitch's own domain |
| Token exfiltration from game server | Tokens never leave the relay; plugin has only a per-server HMAC key |
| Relay impersonation | HMAC-SHA256 over timestamp + nonce + body |
| Replay | 5-minute clock window + nonce cache |
| Link hijack | Device code bound to server-side Steam ID; session status restricted to the creating server |
| Fake EventSub events | WebSocket session + Twitch metadata; broadcaster ID must match the linked account |
| Duplicate delivery | `message_id` dedupe on relay and plugin |
| Command injection | Staff-only templates; whitelisted first token; sanitized `{viewer}`/`{reward}`/`{player}`; no newlines/semicolons |
| Effect spam | Global cooldown, per-viewer cooldown, per-minute cap, trigger cooldown |
| Permission escalation | Tier permission re-checked at trigger time |
| Redemption refund | Known gap; see below |
| Malicious panel payload | Effect IDs/picks are validated against the tier pool and permission |
| Relay exposure | Bind to loopback or use TLS + firewall; rotate API keys |

What the streamer is told:

> Linking uses Twitch's official authorization page. We never see your password or stream key. You can revoke access at twitch.tv/settings/connections.

## 10. Known gaps

1. **Channel point refund/cancel.** The relay subscribes to the `.update` event, but the plugin ignores it. If a viewer refunds a redemption, the effect has already fired. To close this:
   - request `channel:manage:redemptions`,
   - on `.add`, mark the redemption `FULFILLED` only after the effect applies,
   - on `.update` with `CANCELED`, revert or compensate.
2. **No per-effect undo.** Buffs/debuffs are applied by console command. Reversing them would need paired "remove" actions or a native buff duration.
3. **No Twitch chat output.** In-game chat only. Add a bot account if you want chat replies.
4. **No web admin UI.** Staff edit JSON and use `/twitch reload`.
5. **Single relay process.** EventSub WebSocket cost/limit is fine for a normal community; shard for a large network.
6. **Native Twitch integration conflict.** The game's own `twitch.xml` system may also apply effects. Use different buffs/actions or disable one system.

## 11. Extension points

- `OnTwitchEffect` — fired for every effect that is applied.
- `OnTwitchEffectAction` — fired for `hook` actions with a staff-defined value.
- `command` actions — use any server console command whose first token is in `AllowedCommandPrefixes`.

For Chaos Tricks:

```json
{
  "Type": "command",
  "Value": "chaos.trick {effect} {steamid}"
}
```

Add `chaos.trick` to `AllowedCommandPrefixes`. If Chaos Tricks exposes an Oxide hook instead, use a `hook` action and listen for `OnTwitchEffectAction`.

## 12. Icons

Every UI icon resolves through a logical role in `Configuration.Icons`, for example `Buff`, `Reward`, `RelayOnline`, or `LinkPending`.

Supported role values:

- `sprite:name` — game/mod UI sprite.
- `item:name` — base-game or mod item icon; Server Assist resolves it through the item atlas, so no new texture is required.
- `atlas:atlasName:sprite` — explicit custom atlas sprite.
- `plugin:PluginName` — live icon from another Server Assist panel/provider via `GetPluginIcon`.
- `panel:panelId` — live icon from a registered panel via `GetPanelIcon`.
- `none` — vanilla fallback.

Resolution order:

1. `Icons[role]` from config.
2. If it is `plugin:`/`panel:`, ask Server Assist for the live icon.
3. If that fails, fall back to `DefaultIconMap[role]`, which uses existing mod art where possible (`sa_admin_suite`, `sa_permission_gui`, `sa_nav_players`, `sa_nav_logs`, `sa_nav_server`, `sa_plugin_hot_reload`).
4. Streaming-specific roles only use the `sa_stream_*` set from `ServerAssistArt`.

`DefaultLiveIconMap` seeds the mod-related defaults:

- `Admin` → `plugin:AdminSuite`
- `AdminReload` → `plugin:PluginHotReload`
- `AdminLinks` → `plugin:PermissionGui`

Reward effects default to `item:` icons so base-game and mod items use the existing item atlas instead of new hand-painted textures.

Missing roles are filled on load, so a future plugin update can add a role without breaking old configs. `ConfigVersion 3` migrates the 0.2.0 streaming admin/reward defaults to the live hooks, existing mod art, and item icons, while preserving custom mappings. `/twitch iconset <role> <value>` remaps a role without a rebuild. `PanelIcon` is kept for older configs; `Icons.Panel` is authoritative.

The streaming sprite names are stable even while the art is being replaced. `PLANNED_STREAMING_ICONS.md` in `ServerAssistArt` documents the final painted art and the interim aliases.

## 13. Roadmap

- Auto-fulfill/cancel channel point redemptions.
- Per-tier trigger cooldown editor in the panel.
- Relay web admin page for token status and subscription health.
- Per-effect "remove" pairs for clean reversibility.
- Twitch chat bot output.
- More EventSub topics (predictions, charity, goals).
- Multi-relay sharding.
- Metrics endpoint for the relay.
