# Media Integration

> **Version:** 0.3.0 · **Type:** Server-side Oxide plugin plus the standalone MediaRelay · **Source:** [MediaIntegration.cs](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/MediaIntegration/oxide/plugins/MediaIntegration.cs)

!!! note "Phase 0 status"
    Phase 0 is implemented: `MediaIntegration` 0.3.0 plus `MediaRelay` run Twitch and a per-streamer custom webhook. YouTube and Kick adapters are the next implementation phase. The hardened web admin from `ADMIN.md` is designed but **not exposed** in this build.

!!! warning "Do not run TwitchIntegration and MediaIntegration together"
    `TwitchIntegration` and `MediaIntegration` must not be loaded at the same time. Both poll the same relay and can register a Server Assist panel, which can duplicate links and apply the same reward twice. Remove one plugin and reload or restart the server before installing the other.

Media Integration is the platform-neutral successor to [Twitch Integration](twitch-integration.md). The core reward system is shared: one tier list, one loadout per tier, one permission model, one Server Assist panel, and one audit trail. A platform is a **provider** behind the relay; the plugin never talks to a platform API directly. It polls the relay for normalized events, then handles links, loadouts, cooldowns, effects, and audit locally.

The design keeps Twitch at parity while leaving room for YouTube, Kick, Trovo, CHZZK, SOOP, VK Play, Bilibili, Restream, StreamElements, Streamlabs, Ko-fi, Patreon, custom webhooks, and future providers. Only Twitch and the generic custom webhook are implemented today.

---

## What works now

| Area | Status in 0.3.0 |
|---|---|
| Plugin and relay | `MediaIntegration.cs` 0.3.0 and `MediaRelay` 0.3.0 |
| Commands | `/media`, with `/twitch` kept as an alias |
| Twitch | Device-code linking, EventSub events, token refresh, scoped subscriptions |
| Custom webhook | Per-streamer token, HMAC signature, timestamp, nonce, rate limit, 64 KiB body cap |
| Shared reward core | Tiers, loadouts, permissions, cooldowns, effects, audit |
| Platform awareness | `Platform` on events and triggers; optional `Tiers[].Platforms` |
| Currency | Opt-in staff rate table plus optional `MinUsd` triggers |
| Migration | TwitchIntegration config, data, permissions, and `twitch` group |
| Server Assist | `media` panel with a platform links page |
| Relay API | `/api/providers`, `/webhook/custom`, and the signed server API |

## Not implemented yet

- YouTube Live and Kick adapters (the next phase).
- Trovo, CHZZK, SOOP, VK Play, and Bilibili adapters.
- Restream, StreamElements, Streamlabs, Ko-fi, and Patreon adapters.
- Any unofficial adapter. Mobile and regional platforms remain manual/webhook only.
- The hardened web admin. There is no admin listener or web UI in this build.
- Automatic conversion between bits, points, Super Chats, gifts, and fiat without staff-set rates.

---

## Requirements and installation

| Requirement | Detail |
|---|---|
| Game server | 7 Days to Die with Oxide/uMod; EAC off |
| Relay | .NET 8 service, published self-contained for `linux-x64` or Windows for testing |
| Twitch application | One developer application with a Client ID; Client Secret recommended for refresh |
| Twitch status | Affiliate or Partner for subs, gift subs, Bits, and channel points; follows, raids, and chat work for any channel |
| Server Assist | Server plugin plus the Server Assist client mod for the panel |
| Art pack | ServerAssistArt for `sa_stream_*` sprites; missing sprites fall back to vanilla |
| Network | Outbound game server to relay; outbound relay to Twitch |
| Clock sync | NTP on both hosts; signed requests allow a five-minute window |

### 1. Build and run the relay

The relay is a separate service. See [MediaRelay](../programs/media-relay.md) for the full configuration and deployment reference.

```bash
# On the Linux relay host
cd MediaIntegration/relay && chmod +x publish.sh && ./publish.sh

# Or cross-publish from Windows
.\MediaIntegration\relay\publish.ps1 -Runtime linux-x64
```

Run `MediaRelay` once so it creates `relay.json`, then set the Twitch `clientId` and `clientSecret`, confirm the `servers[]` entry, and start it again. Copy that server's `apiKey` into the plugin config as `ServerKey`.

### 2. Install the plugin

1. Remove `oxide/plugins/TwitchIntegration.cs`. Do not run both plugins.
2. Copy `oxide/plugins/MediaIntegration.cs` to `oxide/plugins/`.
3. Load it once with `oxide.reload MediaIntegration` or a server restart.
4. Edit `oxide/config/MediaIntegration.json` and set `RelayUrl`, `ServerId`, and `ServerKey`.
5. Reload the plugin and run `/media relaytest`.
6. Make sure Server Assist is installed and every client has the Server Assist mod.

### 3. Link and verify

```text
/media link twitch      # device code at twitch.tv/activate
/media link custom      # generate a signed webhook URL and token
/media providers        # configured providers and link state
/media status           # links and loadout summary
```

Existing TwitchIntegration config, player data, and `twitch.*` permissions migrate on first load. The old files are left in place for rollback.

---

## Commands

`/media` is the documented command. `/twitch` is an alias for one release and forwards to `/media`. Omitting the subcommand opens the panel.

| Command | Permission | Purpose |
|---|---|---|
| `/media` or `/media open` / `/media panel` | `media.use` | Open the Media panel |
| `/media link [platform]` | `media.use` | Start a link; defaults to `twitch` |
| `/media unlink [platform]` | `media.use` | Remove a link; defaults to `twitch` |
| `/media providers` | `media.use` | List configured platforms and link state |
| `/media on` / `/media off` | `media.use` | Opt in or out of effects |
| `/media pause` / `/media resume` | `media.use` | Pause or resume effects |
| `/media status` | `media.use` | Show links, opt-in state, and loadouts |
| `/media loadout` | `media.use` | Open the tier loadout page |
| `/media catalog` | `media.use` | Open the reward catalog |
| `/media help` | `media.use` | Show command help |
| `/media reload` | `media.admin` | Reload config and permissions |
| `/media reconcile` | `media.admin` | Ask the relay to recreate EventSub subscriptions |
| `/media relaytest` | `media.admin` | Test the relay URL and key |
| `/media connect <relayUrl> <serverKey> [serverId]` | `media.admin` | Set the relay destination from chat |
| `/media test <platform> <type> [amount]` | `media.admin` | Fire a test event; `/media test <type> [amount]` still defaults to Twitch |
| `/media list` | `media.admin` | List linked accounts |
| `/media who <player>` | `media.admin` | Inspect a player profile |
| `/media tier <player> <tierId>` | `media.admin` | Grant a tier permission |
| `/media untier <player> <tierId>` | `media.admin` | Revoke a tier permission |
| `/media icons [page]` | `media.admin` | List icon roles and current mappings |
| `/media iconset <role> <value>` | `media.admin` | Remap one icon role |
| `/media forcelink <player> <twitchLogin>` | `media.admin` | Create a local-only Twitch link for testing |

!!! warning "Prefer the panel for the relay key"
    `/media connect` writes the server key into chat and command logs. Use the panel's Admin page in production; it saves the value to config and does not display it again.

!!! info "Migration is automatic"
    First load imports TwitchIntegration data and config. `/media migrate` appears in the migration design, but 0.3.0 does not register a separate `migrate` subcommand; migration runs during load and the result is written to the log.

---

## Permissions

| Permission | Purpose |
|---|---|
| `media.use` | Open the panel and use player commands |
| `media.streamer` | Granted automatically when a channel is linked, through the `media` group by default |
| `media.admin` | Relay status, audit, test events, tier grants, icon remapping, relay destination edits |
| `media.tier.<id>` | Staff-controlled tier eligibility, for example `media.tier.tier1` |
| `twitch.use` | Legacy alias, registered and mirrored to `media.use` |
| `twitch.streamer` | Legacy alias, registered and mirrored to `media.streamer` |
| `twitch.admin` | Legacy alias, registered and mirrored to `media.admin` |
| `twitch.tier.<id>` | Legacy alias, registered and mirrored to `media.tier.<id>` |

A tier is eligible when the player has the tier permission; admins bypass the check. An empty tier `Permission` is filled with `TierPermissionPrefix` plus the tier id. Permissions are shared across platforms, so there are no per-platform tier nodes.

Migration behaviour:

- `twitch.use`, `twitch.admin`, and `twitch.tier.X` assignments are copied to the matching `media.*` nodes.
- Members of the legacy `twitch` group are added to the configured `media` group.
- The mirror runs once, tracked by `StoredData.LegacyPermissionsMigrated`, so later staff revocations are not undone on restart.
- Legacy nodes are registered but not automatically revoked in the first release.

---

## Server Assist panel integration

The plugin registers a normal Server Assist **panel**, not a separate panel per platform and not the three-tab GUI provider API in 0.3.0.

| Item | Value |
|---|---|
| Panel/provider id | `media` |
| Legacy panel id | `twitch`, unregistered on upgrade |
| Title / label | `Media` (`HookLabel`) |
| Description | `HookDescription`, editable in config |
| Permission | `media.use` (`HookPermission`) |
| Handler | `HandleGuiRequest` |
| Icon | `Icons.Panel`, default `sprite:sa_stream_panel` |
| Order | `40` (`PanelOrder`) |
| Entry | `/media`, plus the Server Assist Home card |

Registration is idempotent. The plugin tries `RegisterPanel` and falls back to `RegisterGuiHook` on older Server Assist builds, then calls `SetPanelIcon` when registration succeeds. It also retries shortly after startup and listens for `OnServerAssistReady`, because Server Assist may load after this plugin. The panel is unregistered on unload.

Panel pages (tabs):

| Page | Contents |
|---|---|
| **Home** | Link summary, opt-in, pause, category toggles, loadout and catalog shortcuts |
| **Links** | One row per enabled or already-linked platform, with link/unlink and pending state |
| **My tier loadouts** | One row per tier; locked tiers show the required permission |
| **Tier** and **Category** | Choose buffs, debuffs, or rewards; tap an effect to add or remove it |
| **Reward catalog** | Read-only view of every effect staff defined, grouped by tier |
| **Admin** | Relay URL, server ID and key editor, Test Relay, status, pending events, recent audit, reload, reconcile, and test buttons |

Platforms are rows and text badges inside the shared panel. The UI does not grow one tab per platform. If Server Assist is absent, chat commands and relay polling still work, but the panel cannot open.

Every icon resolves through a logical role under `Icons`:

| Form | Behaviour |
|---|---|
| `sprite:name` | Game or mod UI sprite |
| `item:name` | Base-game or mod item icon |
| `atlas:atlasName:sprite` | Explicit custom atlas sprite |
| `plugin:PluginName` | Live icon from another Server Assist panel or provider |
| `panel:panelId` | Live icon from a registered panel |
| `none` | Vanilla fallback symbol |

Resolution order is `Icons[role]`, then a live `plugin:`/`panel:` lookup, then the built-in default. AdminSuite, PluginHotReload, and PermissionGui are referenced live by default but do not require new Media-specific textures. See [Icons](../../core/icons.md) and [Server Assist Art](../art/server-assist-art.md).

---

## Configuration

File: `oxide/config/MediaIntegration.json`. The full sample is [MediaIntegration.example.json](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/MediaIntegration/oxide/config/MediaIntegration.example.json).

| Key | Default | Notes |
|---|---|---|
| `ConfigVersion` | `4` | Migrations run on load |
| `ChatPrefix` | `"[Media]"` | Prefix for chat replies |
| `GrantUseToDefaultGroup` | `true` | Grant `media.use` to the default players group |
| `GrantAdminToAdminGroup` | `true` | Grant `media.admin` and `media.use` to the default admin group |
| `GrantStreamerOnLink` / `RevokeStreamerOnUnlink` | `true` / `true` | Grant or remove the streamer group with the link |
| `StreamerGroup` / `StreamerGroupTitle` / `StreamerGroupRank` | `"media"` / `"Media Streamer"` / `0` | Linked-streamer group |
| `HookLabel` / `HookDescription` | `"Media"` / link text | Server Assist panel text |
| `HookPermission` | `"media.use"` | Panel permission |
| `PanelIcon` / `PanelOrder` | `"sprite:sa_stream_panel"` / `40` | Legacy panel icon; `Icons.Panel` is authoritative |
| `Icons` | built-in defaults | Logical role to icon value mapping |
| `RelayUrl` | `"http://127.0.0.1:8090"` | Scheme is added if missing; trailing slash trimmed |
| `ServerId` / `ServerKey` | `"main"` / `""` | Must match the relay `servers[]` entry |
| `EnabledPlatforms` | `["twitch", "custom"]` | Platforms shown in the UI; only implemented providers can link |
| `PlatformLabels` | built-in map | Display names for rows and badges |
| `CurrencyConversion` | disabled, empty rates | Staff-set USD conversion table |
| `PollIntervalSeconds` / `LinkPollIntervalSeconds` / `RequestTimeoutSeconds` | `3` / `3` / `20` | Clamped to 1–60, 2–60, and 5–120 |
| `TierPermissionPrefix` | `"media.tier."` | Used when a tier has no explicit permission |
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
| `AllowedCommandPrefixes` | `buffplayer`, `debuffplayer`, `give`, `chaos`, `weather`, `spawn`, `say` | First-token allowlist for `command` actions |
| `Tiers` | default tiers | Pools, weights, icons, pick limits, optional platform restrictions |
| `Triggers` | default triggers | Thresholds, filters, priorities, cooldowns |

### Data files

| Path | Purpose |
|---|---|
| `oxide/plugins/MediaIntegration.cs` | The Oxide plugin |
| `oxide/config/MediaIntegration.json` | Configuration, created on first load |
| `oxide/data/MediaIntegration/players.json` | Links, loadouts, cooldowns, pending effects, processed event IDs, audit |
| `oxide/data/TwitchIntegration/players.json` | Legacy data, read once if the new file is absent and kept for rollback |
| `relay.json` | Twitch credentials, listen address, per-server API keys |
| `data/secret.key` | AES key for the relay token vault |
| `data/tokens.enc` | Encrypted Twitch OAuth tokens |
| `data/customlinks.json` | Custom webhook link records; only SHA-256 token hashes are stored |
| `data/events/<serverId>.json` | Pending normalized events for one game server |

A player profile stores the Steam ID, player name, a `Links` dictionary keyed by platform, opt-in and pause state, category toggles, shared loadouts by tier, viewer and trigger cooldowns, and pending effects. `ProcessedEventIds` is a bounded ring of 2,000 IDs; `Audit` is a bounded list of 200 lines.

---

## Linking and identity

### Twitch device-code linking

1. The player runs `/media link twitch`.
2. The plugin posts `{platform, steamId, playerName}` to `/api/link/start` with HMAC.
3. The relay starts Twitch device-code OAuth and returns a session id, user code, verification URI, and expiry.
4. The player enters the code at `twitch.tv/activate`.
5. The relay polls Twitch, validates the token, fetches the Helix user, and binds the Twitch user ID to the server and Steam ID.
6. The relay creates EventSub subscriptions; the plugin polls `/api/link/status`, saves the link, grants the streamer group, and opts the player in.

The Steam ID comes from the game server, not the client. One Twitch account maps to one character per server, and one character maps to one Twitch account per server. No stream key or password is involved. The streamer can revoke access at <https://www.twitch.tv/settings/connections>.

### Custom webhook linking and signing

`/media link custom` creates a per-streamer link and returns a URL such as `https://relay.example.com/webhook/custom` plus a one-time token. The panel shows the token once; unlink and relink to rotate it. The relay stores only the SHA-256 hash of the token in `data/customlinks.json`.

Send an HTTP `POST` with these headers:

| Header | Value |
|---|---|
| `X-Media-Token` | The plaintext custom webhook token |
| `X-Media-Timestamp` | Unix seconds; accepted within ±300 seconds |
| `X-Media-Nonce` | One-time value, 8–128 characters |
| `X-Media-Signature` | Base64 HMAC-SHA256 of the canonical string, keyed with the token |

The canonical string is:

```text
timestamp + "\n" + nonce + "\n" + rawBody
```

Current limits:

- Request body cap: 64 KiB.
- Rate limit: 120 requests per 60 seconds per link id.
- Nonce replay cache: 10 minutes.
- Invalid, stale, replayed, oversized, or badly signed requests are rejected.
- `type` is required; a missing `id` is replaced with a generated ID.

The JSON body can set `id`, `type`, `viewerId`, `viewerLogin`, `viewerName`, `amount`, `amountMinor`, `unit`, `currency`, `normalizedUsd`, `tier`, `months`, `giftCount`, `rewardId`, `rewardTitle`, `redemptionId`, `status`, `viewerCount`, `message`, `isGift`, `isAnonymous`, `occurredAt`, and free-form `extra`. The normalized event gets `platform = "custom"` and `broadcasterId =` the link id, which maps it back to the linked character.

See [MediaRelay](../programs/media-relay.md#custom-webhook-endpoint) for the endpoint contract and a signing example.

### Provider capability model

The relay's `/api/providers` route returns `id`, `displayName`, `linkMode`, `capabilities`, and a link count for each configured provider. In 0.3.0 it reports two providers:

| Provider | Link mode | Capabilities |
|---|---|---|
| `twitch` | `OAuthDeviceCode` | chat, follow, subscription, gift_subscription, donation, reward, raid, momentum, stream_status, viewer_count |
| `custom` | `WebhookToken` | donation, subscription, gift_subscription, resub, reward, raid, follow, momentum, chat, custom |

The design uses capability flags so the UI can hide unsupported triggers and label providers honestly. The full flag set is `Chat`, `Follow`, `Subscription`, `GiftSubscription`, `Donation`, `Reward`, `Raid`, `Momentum`, `StreamStatus`, `ViewerCount`, and `Custom`. Link modes are `OAuthDeviceCode`, `OAuthRedirect`, `ManualChallenge`, `WebhookToken`, `StaffApproval`, and `None`; only the first and `WebhookToken` are used today.

---

## Tiers, loadouts, platforms, and currency

- Tiers and loadouts are shared by default. A streamer has one loadout per tier, used by every linked platform.
- A tier can set `Platforms` to restrict which platforms may arm it. An empty or omitted list means every provider. This is data, not a permission node.
- A trigger can set `Platform`; blank means any platform. Legacy triggers that had no platform were migrated to `platform: "twitch"` so a custom webhook cannot fire them by accident.
- `RequireAllCategories` requires `MinPicks` valid selections in each non-empty pool before the tier is considered armed. An empty pool is treated as "not part of this tier".
- Loadout IDs are re-validated on every trigger, so removed effects clean up stale picks.
- Cooldowns are shared: global, per-viewer, per-trigger, and a per-minute effect cap.

Example tier restriction:

```json
{
  "Id": "tier2",
  "DisplayName": "Tier 2 - Surge",
  "Platforms": ["twitch", "custom"],
  "MinPicks": 1,
  "MaxPicks": 3
}
```

### Currency conversion and `MinUsd`

Native amounts are always preserved. A staff-set table can add a USD equivalent for cross-platform thresholds and audit:

```json
{
  "CurrencyConversion": {
    "Enabled": true,
    "Rates": {
      "twitch.bits": 0.01,
      "custom.points": 0.01,
      "youtube.USD": 1.0,
      "KRW": 0.00072,
      "JPY": 0.0067,
      "EUR": 1.08
    },
    "EffectiveDate": "2026-11-09"
  }
}
```

Lookup order is `platform.unit`, then bare `unit`, then ISO currency. USD converts 1:1 even without a row. Fiat events with `amountMinor` are divided by 100 before applying the rate. If a trigger has `MinUsd > 0` and no rate exists, that trigger does not match and the plugin logs a one-time warning; native-unit triggers still work. The plugin never invents exchange rates.

---

## Platform support summary

| Provider | Status | Transport / link | Notes |
|---|---|---|---|
| Twitch | Implemented (P0) | EventSub WebSocket, Helix, device code | Parity target |
| Custom webhook | Implemented (P0) | HTTP POST + HMAC | Covers IFTTT, Zapier, Streamer.bot, custom scripts |
| YouTube Live | Next (P1) | Data API v3, OAuth redirect | Super Chat, memberships, chat, stream status, viewer count; quota research |
| Kick | Next (P1) | Public API, OAuth, chat socket | Event coverage still being verified |
| Trovo | Research (P2) | Developer API, OAuth | Chat/stream status first if events are limited |
| CHZZK / SOOP / VK Play / Bilibili | Research (P2) | Official APIs | Regional platforms; test account required |
| Restream / StreamElements / Streamlabs | Research (P2) | Aggregator APIs/webhooks | Counted separately from native providers; dedupe required |
| Ko-fi / Patreon | Research (P2) | Webhooks/API | Memberships and donations |
| TikTok and other long-tail platforms | Not approved | Manual challenge + custom webhook | No automatic rewards |

The full matrix and research checklist are in the repository's [PLATFORMS.md](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/MediaIntegration/PLATFORMS.md).

### Unofficial adapter policy

No unofficial adapters are implemented. The default path for a platform without a documented official event API is:

1. Manual link or staff approval.
2. A per-streamer custom webhook from the streamer's own tooling.
3. No automatic reward granting.

Automatic rewards from an unofficial source require the full audit, dependency review, isolation, contract tests, live smoke test, explicit opt-in with a warning, and a named maintainer described in [UNOFFICIAL_ADAPTERS.md](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/MediaIntegration/UNOFFICIAL_ADAPTERS.md). If any item is missing, the platform stays on manual/webhook.

---

## Security notes

- No stream keys or platform passwords, ever. Twitch uses OAuth device codes; custom webhooks use a per-streamer token.
- Twitch tokens live only on the relay, encrypted with AES-GCM; the game server receives events, never tokens.
- The game server only makes outbound requests. The relay makes outbound requests to Twitch. No inbound game-server port is required.
- Custom webhook requests are signed, timestamped, nonce-checked, rate-limited, and body-capped. Only the token hash is stored at rest.
- Every effect is audited with platform, provider event ID, viewer, native amount, and the staff USD equivalent when available.
- Unofficial adapters are opt-in and must be isolated; the default remains manual linking plus custom webhooks.
- The web admin is not exposed in 0.3.0. Do not expose a new admin listener until the lockdown checklist is complete.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Every relay request returns 401 | `ServerId`/`ServerKey` mismatch, clock skew, or reused nonce | Match the `servers[]` entry; check NTP; restart the relay after editing `relay.json` |
| `/media link` says the relay is not configured | Empty `RelayUrl` or `ServerKey` | Set both and run `/media reload` |
| `Platform 'youtube' is not enabled` or "not implemented" | The platform is not in `EnabledPlatforms`, or no adapter exists | Use `twitch` or `custom`; add adapters in a later phase |
| Link code expires | Twitch device codes are short-lived | Run `/media link twitch` again |
| No subscriptions in the relay log | Not Affiliate/Partner, missing scope, or optional event disabled | Re-link; check `enableFollows`, `enableChat`, `enableHypeTrain` in `relay.json` |
| Custom webhook returns 404 | Unknown or rotated token | Relink `/media link custom` and update the sender |
| Custom webhook returns 401 | Bad signature, stale timestamp, reused nonce, or unknown token | Recompute HMAC over the exact body; sync clocks; use a fresh nonce |
| Custom webhook returns 413 | Body over 64 KiB | Send a smaller normalized event |
| Custom webhook returns 429 | More than 120 requests in 60 seconds for one link | Slow the sender; the limit is per link id |
| Events arrive but nothing applies | Platform disabled, no matching trigger, tier locked, loadout not armed, streamer paused/offline, or reward canceled | Check `/media status`, `/media who`, and the audit page |
| A `MinUsd` trigger never fires | No conversion rate for the event's platform/unit | Enable `CurrencyConversion` and add the key; native-unit triggers still work |
| Icons show vanilla symbols | ServerAssistArt is missing or the role was remapped | Check `/media icons`, install the art pack, or remap with `/media iconset` |
| Panel does not open | Server Assist or the client mod is missing | Install both; chat commands and relay polling still work without the panel |
| Both integrations apply rewards twice | TwitchIntegration and MediaIntegration are both loaded | Remove one plugin and restart |

More: [Troubleshooting](../../reference/troubleshooting/index.md) and [Rollout and rollback](../../reference/migration/rollout-rollback.md).

---

## Migrating from TwitchIntegration

The rename map and compatibility rules are in the repository's [MIGRATION.md](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/MediaIntegration/MIGRATION.md).

1. Back up `oxide/config/TwitchIntegration.json` and `oxide/data/TwitchIntegration/players.json`.
2. Stop the server and remove `oxide/plugins/TwitchIntegration.cs`.
3. Install `oxide/plugins/MediaIntegration.cs`.
4. Start the server. First load imports the old config and player data, moves Twitch fields into `Links["twitch"]`, sets `ConfigVersion = 4`, defaults unqualified legacy triggers to `twitch`, and mirrors `twitch.*` permissions to `media.*`.
5. Check the migration log, run `/media relaytest`, and test a Twitch redemption.
6. Keep the old plugin, config, and data untouched for at least a week as the rollback copy.

To roll back, stop the server, remove `MediaIntegration.cs`, restore `TwitchIntegration.cs` and the old config/data if the new plugin modified them, then start the server. The relay API is backward compatible: old event records and old plugins default a missing `platform` to `twitch`.

---

## Related pages

- [MediaRelay](../programs/media-relay.md)
- [Twitch Integration](twitch-integration.md)
- [Server Assist API](../../integration/api/index.md)
- [Panels and hooks](../../integration/api/panels.md)
- [Providers](../../integration/api/providers.md)
- [Server Assist product guide](../../core/server-assist.md)
- [Icons](../../core/icons.md)
- [Permissions](../../core/permissions.md)
- [Troubleshooting](../../reference/troubleshooting/index.md)
