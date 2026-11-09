# MediaIntegration

Status: **Phase 0 implemented**. `MediaIntegration` 0.3.0 and `MediaRelay` now run Twitch plus a per-streamer custom webhook. YouTube and Kick adapters are the next implementation phase. The web admin is designed but not exposed yet.

The goal is one shared reward/tier/loadout system for Twitch, YouTube, Kick, Trovo, Facebook Gaming, CHZZK, SOOP, VK Play, Bilibili, TikTok Live, Restream, StreamElements, Streamlabs, Ko-fi, Patreon, custom webhooks, and any future provider that can produce a normalized event.

## What works now

- `oxide/plugins/MediaIntegration.cs` (0.3.0) with `/media` and `/twitch` alias.
- Shared tiers, loadouts, permissions, cooldowns, effects, and audit.
- Optional `Tiers[].Platforms` restrictions.
- Platform-aware triggers and `CurrencyConversion` / `MinUsd`.
- Twitch device-code linking.
- Per-streamer custom webhook linking with HMAC, timestamp, nonce, per-link rate limiting, and a 64 KiB body cap.
- Automatic migration from `TwitchIntegration` config/data and `twitch.*` permissions.
- Server Assist **Media** panel with a platform links page.
- `MediaRelay` with `/api/providers`, `/webhook/custom`, and the existing signed server API.

## Not implemented yet

- YouTube Live adapter (next).
- Kick adapter (next).
- Trovo/CHZZK/SOOP/VK Play/Bilibili adapters.
- Restream/StreamElements/Streamlabs/Ko-fi/Patreon adapters.
- The hardened web admin from `ADMIN.md` (design only; no admin listener is exposed).
- Any unofficial adapter; mobile/regional platforms remain manual/webhook only.

## Documents

| File | Purpose |
|---|---|
| `DESIGN.md` | Core architecture, event model, provider interface, relay/plugin design, phases |
| `PLATFORMS.md` | Platform-by-platform capability matrix, confidence levels, research checklist |
| `MIGRATION.md` | TwitchIntegration → MediaIntegration rename, data/config/permission migration, rollout |
| `ADMIN.md` | Hardened relay web admin: loopback/Tailscale, mTLS, TOTP, CSRF, systemd lockdown |
| `UNOFFICIAL_ADAPTERS.md` | Audit/isolation policy for TikTok, regional, and other undocumented APIs |
| `ICONS.md` | Icon policy: shared roles, platform text badges, no platform logo art |
| `MEDIA_EVENT.schema.json` | Normalized event contract shared by every provider |

## The short version

- The **core stays shared**: one set of tiers, one loadout per tier, one set of buffs/debuffs/rewards, one permission model, one Server Assist panel.
- Each platform becomes a **provider adapter** in the relay. An adapter only has to emit normalized `MediaEvent` objects.
- The plugin never talks to a platform API directly. It polls the relay exactly as it does now, with a `platform` field added.
- Platforms differ wildly. Some have rich official event APIs, some only have chat, some have no public API at all. The design uses **capability flags** and **link modes** instead of pretending every platform is Twitch.
- Restream and similar services are treated as **aggregators/event sources**, not as a replacement for native provider events. They get a provider adapter with honest capabilities.
- No stream keys, for any platform. Providers use OAuth, device codes, webhooks with signatures, or a manual verification challenge.
- Tiers/loadouts are shared; a tier can optionally restrict which platforms may arm it. Permissions stay shared.
- Staff-set USD conversion is opt-in for cross-platform `MinUsd` thresholds; native units are always preserved.
- YouTube and Kick are the first post-Twitch providers.
- Unofficial adapters are manual/webhook by default. Automatic rewards require the audit and isolation in `UNOFFICIAL_ADAPTERS.md`.
- The web admin is allowed only under the lockdown checklist in `ADMIN.md`.
- The rename is implemented. Keep `TwitchIntegration` only as a rollback copy until the new build has run cleanly for a week.

## Quick start

1. Build/publish the relay:
   - Linux: `cd MediaIntegration/relay && ./publish.sh`
   - Windows cross-publish: `.\MediaIntegration\relay\publish.ps1 -Runtime linux-x64`
2. Run `MediaRelay` once to create `relay.json`, then set the Twitch `clientId` / `clientSecret`.
3. Remove the old `oxide/plugins/TwitchIntegration.cs` (do not run both; both would poll the same relay and can double-apply rewards), then install `oxide/plugins/MediaIntegration.cs`. Copy the relay `servers[0].apiKey` into `oxide/config/MediaIntegration.json` as `ServerKey`.
4. In game:
   - `/media link twitch`
   - `/media link custom` for a signed webhook token
   - `/media providers` to see configured providers and capabilities
   - `/media status` to see links and loadouts
5. Existing TwitchIntegration config/data/permissions migrate on first load. The old files are left in place for rollback.

## Next step

Implement the YouTube and Kick adapters behind the same `MediaEvent` contract, then the `ADMIN.md` web admin after the lockdown checklist is met.
