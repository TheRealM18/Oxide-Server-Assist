# MediaIntegration — Design

> **Implementation status:** Phase 0 is implemented for Twitch plus the custom webhook provider. YouTube and Kick are the next adapters. The web admin is design-only and is not exposed.

## 1. Goals

- One shared reward system for every supported platform.
- Server owners define tiers, reward pools, triggers, and cooldowns once.
- Streamers link one or more platform accounts to the same game character.
- The streamer picks one loadout per tier; it works across platforms unless a trigger is platform-restricted.
- Each platform is an adapter behind a normalized event contract.
- Add as many platforms as practical, including regional and long-tail services, without changing the core plugin or the Server Assist UI.
- Keep the existing Twitch behavior and data as the first provider parity target.

## 2. Non-goals

- Pretending every platform has the same feature set. Capability flags are explicit.
- Scraping private APIs as the default path. Unofficial adapters are isolated and opt-in.
- Asking for stream keys or platform passwords. Never.
- Automatic currency conversion between bits, points, Super Chats, gifts, and fiat without staff-set rates.
- Replacing OBS, Restream, or the platforms themselves.
- Making a new texture for every platform logo. Platform names use text badges; platform-brand assets follow each platform's brand rules.

## 3. Design principles

1. **Normalize the event, not the platform.** Adapters translate into one event model.
2. **Capabilities are data.** The UI and trigger system ask what a provider can do; they do not assume.
3. **Link modes are data.** OAuth device code, OAuth redirect, manual challenge, webhook token, or staff approval.
4. **Platform adapters are replaceable.** An adapter can break without taking down the relay or the plugin.
5. **One core, many providers.** Tiers, loadouts, effects, permissions, audit, and cooldowns stay shared.
6. **No silent double counting.** Aggregators and multi-platform links need dedupe keys and explicit platform attribution.
7. **Official APIs first.** Unofficial APIs are quarantined, opt-in, and documented as fragile.

## 4. Terminology

| Term | Meaning |
|---|---|
| Provider | A platform adapter (Twitch, YouTube, Kick, custom webhook, etc.) |
| Broadcaster | The streamer account on a provider |
| Viewer | The person who triggered an event |
| Event | A normalized `MediaEvent` |
| Trigger | A staff rule that maps an event to a tier |
| Tier | A permission-gated reward level |
| Loadout | The streamer's shortlist for buffs, debuffs, and rewards at a tier |
| Aggregator | A service that combines platforms (Restream, StreamElements, Streamlabs) |
| Link | A provider account bound to a game character |

## 5. Architecture

```text
Twitch EventSub ─┐
YouTube PubSub/API ─┤
Kick webhook/WS ─┤
Trovo / CHZZK / ... ─┤
Restream / StreamElements / Ko-fi ─┤
Custom webhook ─┘
        │
        ▼
MediaRelay
  ├─ ProviderRegistry
  ├─ Provider adapters (one per platform)
  ├─ LinkService (OAuth / device code / manual challenge / webhook token)
  ├─ TokenStore (encrypted, keyed by platform:user)
  ├─ Normalizer -> MediaEvent
  ├─ EventStore (per game server, durable, deduped)
  └─ HMAC API for game servers
        │  outbound HTTPS + HMAC
        ▼
MediaIntegration.cs (Oxide)
  ├─ Server Assist Media panel
  ├─ Links per platform
  ├─ Shared tiers / loadouts / triggers
  ├─ Platform-aware event matching
  ├─ Cooldowns, rate limits, audit
  └─ Effect adapters (buff/debuff/item/command/hook)
```

The game server still only makes outbound requests. The relay still makes outbound requests to providers. No inbound game-server ports.

## 6. Normalized event model

The contract is `MEDIA_EVENT.schema.json`.

```json
{
  "id": "provider-event-id",
  "platform": "twitch",
  "type": "donation",
  "broadcasterId": "123",
  "broadcasterLogin": "streamer",
  "viewerId": "456",
  "viewerLogin": "viewer",
  "viewerName": "Viewer",
  "amount": 500,
  "unit": "bits",
  "currency": "",
  "tier": "",
  "months": 0,
  "giftCount": 0,
  "rewardId": "",
  "rewardTitle": "",
  "redemptionId": "",
  "status": "",
  "viewerCount": 0,
  "message": "",
  "isGift": false,
  "isAnonymous": false,
  "occurredAt": "2026-11-09T12:00:00Z",
  "receivedAt": "2026-11-09T12:00:01Z",
  "rawType": "channel.cheer",
  "raw": {}
}
```

### Event types

```text
donation          bits, Super Chat, tip, gift, star balloon, cheer, etc.
subscription      sub, membership, guard, Patreon membership
gift_subscription gift sub / gift membership batch
resub             resub / renewal with months
reward            channel point redemption or platform reward
raid              incoming raid
follow            new follower
momentum          hype train / momentum / goal
chat              chat message, optionally keyword-triggered
stream_online     broadcaster went live
stream_offline    broadcaster went offline
viewer_count      periodic viewer count
custom            provider-specific event for a custom adapter
```

### Amount and unit

`amount` is always the native value. `unit` explains it:

```text
bits, points, coins, gifts, stars, balloons, viewers, months, USD, EUR, KRW, JPY, ...
```

`currency` is the ISO 4217 code for fiat. `amountMinor` is optional for fiat to avoid floating point.

### Staff-set USD conversion

The core never invents exchange rates. Staff define a conversion table in the plugin config:

```json
{
  "CurrencyConversion": {
    "Enabled": true,
    "Rates": {
      "twitch.bits": 0.01,
      "kick.points": 0.01,
      "youtube.USD": 1.0,
      "bilibili.coins": 0.0015,
      "KRW": 0.00072,
      "JPY": 0.0067,
      "EUR": 1.08
    },
    "EffectiveDate": "2026-11-09"
  }
}
```

- `unit` and `currency` identify the native value.
- The plugin computes `normalizedUsd = amount * rate` (or `amountMinor / 100 * rate`) for triggers that use `MinUsd` and for audit.
- The event can carry an optional `normalizedUsd` when the provider already supplies a USD value (for example YouTube Super Chat).
- If no rate exists, `MinUsd` triggers do not match and the plugin logs a one-time warning. Native-unit triggers still work.
- Rates are server-local, visible in the Admin panel, and versioned by `EffectiveDate`.
- Audit shows both values: `500 bits (~5.00 USD by staff rate)`.

This avoids comparing 100 bits to 100 KRW as if they were the same thing, while still allowing a staff-approved cross-platform threshold.

### Dedupe

Every event needs a provider-stable `id`. The relay dedupes per server by `platform + id`. The plugin dedupes again and keeps a bounded history. For webhook-only providers, `broadcasterId` is the provider link key/token and the adapter maps it to the linked streamer.

Cross-platform dedupe is intentionally not automatic: the same display name on Twitch and YouTube is not the same person. Aggregator adapters must set `platform` to the original platform when they can; if they cannot, they emit `platform = restream` and the trigger rules must treat it separately.

## 7. Provider interface

```csharp
public interface IMediaProvider
{
    string Id { get; }                 // twitch, youtube, kick, ...
    string DisplayName { get; }
    MediaCapabilities Capabilities { get; }
    MediaLinkMode LinkMode { get; }

    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);

    Task<LinkStartResult> StartLinkAsync(LinkRequest request, CancellationToken ct);
    Task<LinkStatusResult> GetLinkStatusAsync(string sessionId, CancellationToken ct);
    Task UnlinkAsync(MediaLink link, CancellationToken ct);

    Task<ProviderHealth> GetHealthAsync(CancellationToken ct);
}
```

Providers emit events through a callback or channel owned by the relay:

```csharp
public interface IMediaEventSink
{
    Task PublishAsync(MediaEvent evt, CancellationToken ct);
}
```

### Capability flags

```csharp
[Flags]
public enum MediaCapabilities
{
    None = 0,
    Chat = 1 << 0,
    Follow = 1 << 1,
    Subscription = 1 << 2,
    GiftSubscription = 1 << 3,
    Donation = 1 << 4,
    Reward = 1 << 5,
    Raid = 1 << 6,
    Momentum = 1 << 7,
    StreamStatus = 1 << 8,
    ViewerCount = 1 << 9,
    Custom = 1 << 10
}
```

The plugin uses these flags to hide unsupported triggers and to label providers honestly.

### Link modes

```csharp
public enum MediaLinkMode
{
    OAuthDeviceCode,   // Twitch-style code on the platform
    OAuthRedirect,     // Google/YouTube-style browser redirect
    ManualChallenge,   // put a code in the channel title/chat/description
    WebhookToken,      // per-streamer webhook URL/token
    StaffApproval,     // staff confirms the account
    None               // read-only/public event source
}
```

## 8. Relay

Rename `TwitchRelay` to `MediaRelay`.

```text
MediaRelay/
  Providers/
    Twitch/
    YouTube/
    Kick/
    Trovo/
    ...
    CustomWebhook/
  Core/
    IMediaProvider.cs
    MediaEvent.cs
    MediaCapabilities.cs
    LinkService.cs
    TokenStore.cs
    EventStore.cs
    ProviderRegistry.cs
  Api/
    /health
    /api/link/start
    /api/link/status
    /api/links
    /api/unlink
    /api/events/pending
    /api/events/ack
    /api/providers
    /webhook/{provider}
    /oauth/{provider}/callback
```

### Provider registry

`relay.json` gains one section per provider:

```json
{
  "providers": {
    "twitch": {
      "enabled": true,
      "clientId": "...",
      "clientSecret": "...",
      "eventSubUrl": "wss://eventsub.wss.twitch.tv/ws"
    },
    "youtube": {
      "enabled": false,
      "clientId": "...",
      "clientSecret": "...",
      "apiKey": "..."
    },
    "kick": {
      "enabled": false,
      "clientId": "...",
      "clientSecret": "..."
    },
    "custom": {
      "enabled": true,
      "webhookSecret": "..."
    }
  }
}
```

A disabled provider is not instantiated and does not appear in the plugin's link list.

### Provider health

Each adapter reports:

```text
connected, lastEventAt, lastError, subscriptionCount, linkCount
```

The plugin Admin panel shows this per provider. A broken TikTok adapter should not make Twitch look offline.

### Webhooks

Webhook providers use `POST /webhook/{provider}`. The adapter verifies the provider-specific signature/HMAC and rejects unsigned requests. The generic custom webhook uses a per-streamer secret and a documented JSON mapping.

### OAuth redirects

Providers that require browser redirects (YouTube, some regional platforms) use:

```text
GET /oauth/{provider}/start?session=...
GET /oauth/{provider}/callback
```

The link session is bound to the server ID and Steam ID exactly like the Twitch device-code flow.

## 9. Plugin

Rename the plugin, class, files, config, data, and commands:

```text
MediaIntegration.cs
[Info("MediaIntegration", ...)]
oxide/config/MediaIntegration.json
oxide/data/MediaIntegration/players.json
/media
```

Keep `/twitch` as an alias for one release.

### Data model

```csharp
PlayerProfile
  SteamId, PlayerName
  OptedIn, Paused
  Links: Dictionary<string, MediaLink>       // platform -> account
  AllowBuffs, AllowDebuffs, AllowRewards
  PlatformToggles: Dictionary<string, bool>  // optional per-platform opt-in
  Loadouts: Dictionary<string, TierLoadout>  // shared across platforms
  ViewerCooldowns, TriggerCooldowns
  PendingEffects
```

`MediaLink`:

```text
Platform, UserId, Login, DisplayName, LinkedAtUtc
```

A player can link Twitch + YouTube + Kick to one character. Tier loadouts are shared. A platform can be individually opted out.

### Trigger model

Add platform and unit awareness:

```text
TriggerDefinition
  Id, Event, Enabled, TierId
  Platform        // blank = any
  MinAmount
  Unit            // blank = provider-native default
  MinUsd          // optional, requires staff conversion rate
  RewardId, RewardTitle, MatchAnyReward
  SubTier, RequireGift, RequireNotGift
  Phase, Priority, CooldownSeconds, Keyword
```

Examples:

```text
Twitch, donation, 100 bits       -> tier1
YouTube, donation, 5 USD         -> tier2
Kick, subscription, tier 1       -> tier1
Any platform, gift_subscription 5 -> tier2
TikTok, donation, 100 gifts      -> tier1
custom, donation, 500 points     -> tier2
```

### Panel

The panel becomes **Media**:

- **Links** — one row per supported provider, with link/unlink and status.
- **Home** — opt-in, pause, per-platform toggles, shared loadout summary.
- **Loadouts** — unchanged shared tiers.
- **Catalog** — unchanged, grouped by tier; trigger/platform info can be shown.
- **Admin** — relay URL/key, provider health, per-provider link/event counts, recent audit, test events per provider.

The UI does not grow one tab per platform. Platforms are rows/badges inside the shared UI.

### Notifications and audit

Include the platform name and native amount:

```text
[Media] YouTube: ViewerName sent 5.00 USD -> Tier 2: buff X, debuff Y, reward Z
[Media] Twitch: ViewerName cheered 500 bits -> Tier 2: ...
```

## 10. Config and data

### Plugin config

```json
{
  "ConfigVersion": 4,
  "RelayUrl": "https://relay.example.com",
  "ServerId": "main",
  "ServerKey": "...",
  "Providers": {
    "twitch": { "DisplayName": "Twitch", "Enabled": true },
    "youtube": { "DisplayName": "YouTube", "Enabled": true },
    "kick": { "DisplayName": "Kick", "Enabled": true }
  },
  "CurrencyConversion": {
    "Enabled": true,
    "Rates": {
      "twitch.bits": 0.01,
      "kick.points": 0.01,
      "youtube.USD": 1.0,
      "KRW": 0.00072,
      "JPY": 0.0067,
      "EUR": 1.08
    },
    "EffectiveDate": "2026-11-09"
  },
  "Tiers": [
    {
      "Id": "tier1",
      "Platforms": [],
      "...": "blank Platforms means every provider"
    },
    {
      "Id": "tier2",
      "Platforms": ["twitch", "youtube", "kick"],
      "...": "optional per-platform restriction"
    }
  ],
  "Triggers": [ ... ],
  "Icons": { ... }
}
```

Provider credentials stay on the relay. The plugin only knows display preferences, conversion rates, and tier restrictions.

### Relay data

```text
data/
  secret.key
  tokens.enc                 // keyed by platform:userId
  links.json                 // optional index for fast lookup
  events/{serverId}.json     // normalized MediaEvent queues
```

## 11. Tiers, loadouts, and cross-platform behavior

- Tiers and loadouts are shared by default.
- A tier can optionally list `Platforms: ["twitch", "youtube"]` to restrict which platforms can arm it. Empty/omitted means every provider.
- A trigger can specify `Platform`; blank means any platform.
- A streamer can have one loadout per tier, used by every linked platform.
- Per-platform toggles let the streamer pause Twitch rewards while leaving YouTube active.
- Permissions are shared: `media.tier.<id>`. There are no per-platform tier permission nodes; platform restrictions are data on the tier, not permission nodes.

## 12. Linking and identity

| Link mode | Example | Flow |
|---|---|---|
| OAuth device code | Twitch | `/media link twitch` -> code -> twitch.tv/activate |
| OAuth redirect | YouTube | `/media link youtube` -> URL -> Google consent -> relay callback |
| Manual challenge | TikTok, regional services | `/media link tiktok <username>` -> code shown -> put code in channel title/chat -> relay verifies by API/chat |
| Webhook token | Ko-fi, StreamElements, custom | `/media link kofi` -> per-streamer webhook URL/token -> donor includes code, or staff approves |
| Staff approval | Any platform without automation | Staff runs `/media approve <player> <platform> <account>` |

Every link is bound to the server-side Steam ID. The client never supplies the identity.

## 13. Platform strategy

The detailed matrix is in `PLATFORMS.md`.

Three adapter classes:

1. **Official event-rich**: Twitch, YouTube, Kick (verify), Trovo (verify), CHZZK, VK Play, Bilibili, SOOP.
2. **Official but limited / webhook-first**: Facebook Gaming, DLive, Rumble, Restream, StreamElements, Streamlabs, Ko-fi, Patreon.
3. **Unofficial / partner-only / long-tail**: TikTok, Douyu, Huya, Kuaishou, Nimo, Bigo, Nonolive, YY, and similar. These are opt-in, clearly labeled, and maintained separately.

Every provider must document:

- Official API docs and scopes
- Transport (WebSocket, webhook, polling, chat)
- Link mode
- Event types and payloads
- Rate limits/quotas
- Signature verification
- Token refresh/revocation
- Terms-of-service constraints

If a platform has no public event API, the honest options are a generic webhook adapter, a manual challenge, or an opt-in unofficial adapter. The core does not pretend.

Unofficial adapters are governed by `UNOFFICIAL_ADAPTERS.md`: default to manual linking plus custom webhooks; automatic reward granting is allowed only after a full code/dependency audit and isolation. The relay web admin is governed by `ADMIN.md` and is loopback/Tailscale-only with the hardening checklist in that document.

## 14. Rename and migration

See `MIGRATION.md`.

Highlights:

- `TwitchIntegration` -> `MediaIntegration` (plugin, config, data, commands, permissions).
- `/twitch` remains an alias for one release.
- Existing Twitch links become `Links["twitch"]`.
- Existing loadouts and cooldowns carry over.
- Existing triggers get `Platform = "twitch"` if they were Twitch-specific; global triggers stay blank.
- Legacy `twitch.*` permissions remain registered while `media.*` is introduced.
- The relay becomes `MediaRelay`; the old event JSON is accepted with `platform` defaulting to `twitch`.

## 15. Phases

| Phase | Scope |
|---|---|
| 0 | Design approval |
| 1 | Rename + provider-neutral core + Twitch parity + generic webhook + migration |
| 2 | YouTube + Kick |
| 3 | Trovo + CHZZK + SOOP + VK Play + Bilibili |
| 4 | Restream + StreamElements + Streamlabs + Ko-fi + Patreon |
| 5 | TikTok + regional/long-tail adapters (opt-in) |
| 6 | Hardening, observability, load testing, docs |

Each phase must keep Twitch at parity. A new provider cannot regress the existing Twitch flow.

## 16. Security, privacy, and ToS

- No stream keys or platform passwords, ever.
- OAuth tokens encrypted at rest; per-provider scopes minimized.
- Webhook signatures verified; replay protection.
- Link sessions bound to server-side Steam IDs and short-lived.
- Per-provider rate limits, backoff, and circuit breakers.
- Provider errors isolated; one broken adapter cannot take down the relay.
- Audit every effect with platform, provider event ID, and native amount.
- Document retention for viewer names/messages; allow unlink/delete.
- Official APIs first. Unofficial adapters are opt-in and may violate platform terms; make that explicit.
- Platform brand assets follow each platform's brand guidelines. The default UI uses text badges, not painted logos.

## 17. Testing

- **Provider contract tests**: recorded fixtures for each event type.
- **Normalization tests**: payload -> `MediaEvent` snapshots.
- **Trigger tests**: platform/event/unit matching, priority, cooldowns.
- **Replay harness**: feed recorded provider traffic into the relay.
- **Mock provider**: emits every event type for UI and end-to-end tests.
- **Webhook signature tests**: valid, invalid, replayed.
- **Token refresh/revocation tests**.
- **Rate-limit/backoff tests**.
- **Migration tests**: old Twitch config/data -> Media config/data.
- **Multi-platform dedupe tests**: Twitch + Restream chat, gift batches, duplicate webhooks.

## 18. Decisions

1. **Tiers:** shared by default; a tier can optionally restrict `Platforms`. Not a separate tier per platform.
2. **Currency:** staff-set USD conversion table; native units always stored and shown; `MinUsd` is opt-in.
3. **First expansion:** YouTube and Kick, after Twitch parity and the generic webhook foundation.
4. **Unofficial adapters:** default to manual linking + custom webhooks. Automatic rewards are allowed only after the full audit and isolation described in `UNOFFICIAL_ADAPTERS.md`.
5. **Permissions:** one shared `media.tier.<id>` set. No per-platform permission nodes.
6. **Web admin:** allowed only with the hardened deployment in `ADMIN.md` (loopback/Tailscale, TLS/mTLS, TOTP, CSRF, audit, systemd lockdown, firewall).

## 19. Remaining research questions

1. Does Restream expose native gift/sub event data, or only unified chat? Design assumes chat-only until proven otherwise.
2. Exact YouTube quota strategy for `liveChatMessages` polling and PubSubHubbub renewal.
3. Exact Kick official event/webhook coverage versus chat-only.
4. Should tier `Platforms` be editable from the panel, or JSON/admin command only?
5. Web admin auth choice: password + TOTP, mTLS client certificates, or both?
6. Who approves/updates the USD conversion rates, and how often?
7. Which regional platform is the first P2 target after CHZZK/SOOP/VK Play/Bilibili research?
