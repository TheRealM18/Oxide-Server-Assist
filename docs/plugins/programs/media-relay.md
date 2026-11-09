# MediaRelay

> **Version:** 0.3.0 · **Type:** standalone .NET 8 ASP.NET Core service · **Source:** [MediaIntegration/relay](https://github.com/TheRealM18/Oxide-Server-Assist/tree/main/MediaIntegration/relay)

MediaRelay is the standalone service behind the Server Assist **Media** panel. It owns the Twitch application credentials, OAuth tokens, EventSub connection, custom webhook tokens, normalization, deduplication, and per-server event queues. The Oxide plugin polls the relay over HTTP with HMAC authentication; the game server never holds platform tokens and never opens an inbound port.

!!! note "Phase 0 status"
    MediaRelay 0.3.0 runs Twitch EventSub plus a per-streamer custom webhook. YouTube and Kick adapters are next. The hardened web admin from `ADMIN.md` is design-only; there is no admin listener in this build.

```text
Twitch EventSub  ──────────────┐
Custom webhook (signed POST) ──┤
                               v
                         MediaRelay (.NET 8)
                           - device-code OAuth, encrypted token vault
                           - EventSub subscriptions + reconnect
                           - custom webhook tokens, HMAC, rate limits
                           - normalization, dedupe, per-server queues
                               │  outbound HTTPS + HMAC (plugin polls)
                               v
                         MediaIntegration.cs (Oxide)
                           - Media panel, links, shared tiers/loadouts
                           - cooldowns, effects, audit
```

!!! important "No stream keys"
    Linking uses Twitch's official device-code flow on `twitch.tv/activate`. The relay stores OAuth tokens only. The streamer never shares a stream key or password with the relay or the game server.

---

## Architecture and components

`Program.cs` hosts Kestrel, routes, and the `/api` HMAC middleware. `RelayConfig` loads and clamps `relay.json`; `ServerAuth` and `Crypto` handle server signatures and replay; `LinkService`, `TokenStore`, and `TwitchApi` own Twitch linking and tokens; `EventSubService` normalizes Twitch events; `EventStore` keeps per-server queues; `CustomWebhookStore` and `CustomWebhookLimiter` handle the public webhook.

The relay is deliberately a single-process design with no Redis or external database.

---

## Requirements

| Requirement | Detail |
|---|---|
| Runtime / SDK | .NET 8; build host needs the .NET 8 SDK, published Linux build is self-contained |
| Twitch application | One developer application with a Client ID; Client Secret recommended for refresh |
| Game server | Outbound HTTP(S) access to the relay |
| Relay host | Outbound HTTPS/WSS access to Twitch |
| Server Assist | Installed on the server and every client that uses the Media panel |
| TLS | Reverse proxy or ASP.NET Core HTTPS when the relay is remote |

Follows, raids, and chat work for any channel. Subscriptions, gift subs, Bits, and channel points only exist for Affiliate or Partner channels. The self-contained Linux build still needs system OpenSSL (`libssl3` on current Debian/Ubuntu).

---

## Build and publish

```bash
# Linux relay host
cd MediaIntegration/relay && chmod +x publish.sh && ./publish.sh
# output: relay/bin/Release/net8.0/linux-x64/publish/MediaRelay

# Or cross-publish from Windows
.\MediaIntegration\relay\publish.ps1 -Runtime linux-x64
```

The default scripts publish self-contained, single-file builds with native libraries included for self-extraction. For a framework-dependent build, add `-FrameworkDependent`. For local Windows testing use `-Runtime win-x64`; for Alpine/musl use `linux-musl-x64`. The scripts copy `relay.example.json` next to the binary.

```bash
mkdir -p /opt/mediarelay
# copy the publish folder contents to /opt/mediarelay
cd /opt/mediarelay
chmod +x MediaRelay
./MediaRelay
```

First run creates `relay.json`, generates a random `apiKey` for the `main` server, and creates `data/secret.key`. Stop it, edit `relay.json`, then start it again. Lock down the state:

```bash
chmod 600 relay.json
chmod 700 data
```

---

## Configuration

File: `relay.json` next to the executable. The path can also be the first argument or `MEDIA_RELAY_CONFIG`; the legacy `TWITCH_RELAY_CONFIG` variable is still read. Otherwise the service uses `relay.json` beside the binary, with `appsettings.json` as a fallback.

```json
{
  "clientId": "YOUR_TWITCH_CLIENT_ID",
  "clientSecret": "YOUR_TWITCH_CLIENT_SECRET",
  "eventSubUrl": "wss://eventsub.wss.twitch.tv/ws",
  "listen": "http://127.0.0.1:8090",
  "publicBaseUrl": "http://127.0.0.1:8090",
  "dataDir": "data",
  "linkSessionMinutes": 10,
  "eventRetentionHours": 72,
  "maxEventsPerServer": 2000,
  "httpTimeoutSeconds": 20,
  "reconcileMinutes": 5,
  "enableChat": false,
  "enableFollows": true,
  "enableHypeTrain": true,
  "requestManageRedemptions": false,
  "servers": [
    {
      "id": "main",
      "name": "Main Server",
      "apiKey": "GENERATE_A_LONG_RANDOM_KEY"
    }
  ]
}
```

| Key | Default | Notes |
|---|---|---|
| `clientId` | empty | Twitch application Client ID; required for Twitch linking |
| `clientSecret` | empty | Optional; enables token refresh and is sent only to Twitch |
| `eventSubUrl` | `wss://eventsub.wss.twitch.tv/ws` | Current Twitch EventSub endpoint; keep configurable |
| `listen` | `http://127.0.0.1:8090` | Kestrel listen URL |
| `publicBaseUrl` | same as `listen` | Public URL used in the custom webhook link response |
| `dataDir` | `data` | Resolved relative to the config file |
| `linkSessionMinutes` | `10` | Clamped to 1–60; Twitch's device-code `expires_in` governs the actual approval window |
| `eventRetentionHours` | `72` | Clamped to 1–720; older queued events are pruned |
| `maxEventsPerServer` | `2000` | Clamped to 50–20,000; oldest events are dropped first |
| `httpTimeoutSeconds` | `20` | Clamped to 5–120; outbound Twitch HTTP timeout |
| `reconcileMinutes` | `5` | Clamped to 1–60; EventSub subscription reconcile interval |
| `enableChat` / `enableFollows` / `enableHypeTrain` | `false` / `true` / `true` | Optional subscriptions; add their scopes when enabled |
| `requestManageRedemptions` | `false` | Adds `channel:manage:redemptions`; only needed for auto-fulfill |
| `servers` | one generated `main` entry | One entry per game server |

`servers[]` entries have `id`, `name`, and `apiKey`. IDs are trimmed and lowercased; duplicate IDs are logged and the first entry wins. An empty list gets a generated `main` entry, and empty API keys are generated on startup. Each game server needs its own entry and matching plugin `ServerId`/`ServerKey`.

!!! warning "Keep credentials on the relay"
    `clientSecret` and every `apiKey` belong on the relay only. Do not copy them into the Oxide plugin or a client mod. The plugin gets only its own per-server key.

---

## Server API authentication

Every `/api` route requires HMAC authentication. `/health` is open so a load balancer or operator can check liveness.

| Header | Value |
|---|---|
| `X-Server-Id` | A `servers[].id` from `relay.json` |
| `X-Timestamp` | Unix seconds |
| `X-Nonce` | One-time value, 8–128 characters |
| `X-Signature` | Base64 HMAC-SHA256 of the canonical string, keyed with that server's `apiKey` |

The canonical string is:

```text
timestamp + "\n" + nonce + "\n" + rawBody
```

Rules enforced by `ServerAuth`:

- Timestamp must be within 300 seconds of the relay clock.
- Nonce must be 8–128 characters and is single-use.
- Signature comparison is constant-time.
- A missing header, unknown server, stale timestamp, bad signature, or replayed nonce returns `401`.
- Changing `relay.json` requires a relay restart so the new key is loaded.

The plugin sends these headers for link, status, event polling, acknowledgement, reconcile, and test calls. The relay handles all responses as JSON.

---

## Twitch application setup

1. Sign in at <https://dev.twitch.tv/console/apps>.
2. Create one application and copy its **Client ID**.
3. Copy the **Client Secret** if you want refresh-token support.
4. Put both values in `relay.json`.
5. Use `http://localhost` as a placeholder redirect URL; the device-code flow does not use a redirect.
6. Start the relay and link a streamer with `/media link twitch`.

Each streamer authorizes the same application individually. No streamer password, stream key, or account sharing is involved. A separate bot account is only needed if you later add Twitch chat output; this version notifies in game chat.

### Scopes requested by the relay

| Scope | Why |
|---|---|
| `channel:read:redemptions` | Channel point redemptions and automatic rewards |
| `bits:read` | Cheers |
| `channel:read:subscriptions` | Subs, gift subs, and resub messages |
| `channel:read:hype_train` | Hype train events, when `enableHypeTrain` is true |
| `moderator:read:followers` | Follow events, when `enableFollows` is true |
| `user:read:chat` | Chat messages, when `enableChat` is true |
| `channel:manage:redemptions` | Optional, when `requestManageRedemptions` is true |

If a token is missing a scope, the relay logs a re-link instruction and skips that subscription. Re-linking requests the scopes again. Revoked tokens are reported in the log and require a re-link.

---

## Custom webhook endpoint

`/media link custom` creates a per-streamer webhook. The relay returns the public URL from `publicBaseUrl` plus `/webhook/custom`, and the plugin shows the one-time token.

```text
POST https://relay.example.com/webhook/custom
```

| Header | Value |
|---|---|
| `X-Media-Token` | Plaintext custom webhook token |
| `X-Media-Timestamp` | Unix seconds; accepted within ±300 seconds |
| `X-Media-Nonce` | One-time value, 8–128 characters |
| `X-Media-Signature` | Base64 HMAC-SHA256 over the canonical string, keyed with the token |

Signing:

```text
canonical = timestamp + "\n" + nonce + "\n" + rawBody
signature = base64(hmac_sha256(webhook_token, canonical))
```

Minimal body:

```json
{
  "id": "my-tool-1700000000",
  "type": "donation",
  "viewerName": "Viewer",
  "amount": 250,
  "unit": "points",
  "occurredAt": "2026-11-09T12:15:00Z"
}
```

Supported body fields are `id`, `type`, `viewerId`, `viewerLogin`, `viewerName`, `amount`, `amountMinor`, `unit`, `currency`, `normalizedUsd`, `tier`, `months`, `giftCount`, `rewardId`, `rewardTitle`, `redemptionId`, `status`, `viewerCount`, `message`, `isGift`, `isAnonymous`, `occurredAt`, `rawType`, and `extra`. `type` is required. If `id` is empty, the relay generates one.

The relay sets `platform = "custom"`, `broadcasterId =` the link id, and `broadcasterLogin =` the stored player name. It then queues the normalized event for that link's server. The response is `{ "queued": true, "id": "..." }`.

Limits and failure responses:

| Condition | Result |
|---|---|
| Unknown token | `404 Unknown custom webhook token.` |
| More than 120 requests in 60 seconds for one link id | `429 Too many requests. Slow down.` |
| Timestamp missing or outside ±300 seconds | `401 Invalid or stale timestamp.` |
| Nonce outside 8–128 characters | `401 Invalid nonce.` |
| Body over 64 KiB | `413 Request body is too large.` |
| Signature mismatch or reused nonce | `401 Invalid signature.` or `401 Replayed nonce.` |
| Missing `type` or invalid JSON | `400` with the parse/validation error |

Only the SHA-256 hash of the token is persisted. Unlink and relink to rotate the token. The rate-limit key is the non-secret link id, so rotation starts a fresh window.

---

## Token and secret storage

```text
data/
  secret.key       32 random bytes, AES-256 key
  tokens.enc       AES-GCM encrypted Twitch token JSON
  customlinks.json custom webhook records; SHA-256 token hashes only
  events/
    main.json      pending normalized events for server "main"
```

- `secret.key` is generated with `RandomNumberGenerator.GetBytes(32)` when missing or invalid.
- `tokens.enc` uses AES-GCM with a 12-byte nonce and 16-byte tag; writes go to a temporary file, are permission-restricted, then moved into place.
- If `secret.key` is replaced or is not 32 bytes, existing tokens cannot be read and each streamer must re-link.
- Twitch tokens refresh when they expire within two minutes and a refresh token exists.
- The game server never receives an OAuth token. The streamer can revoke access at <https://www.twitch.tv/settings/connections>.
- Back up `relay.json`, `data/secret.key`, and `data/tokens.enc` together and encrypt the backup. A backup without the key is useless; a backup with the key is sensitive.

---

## EventSub and queues

`EventSubService` maintains **one** outbound WebSocket for all linked Twitch broadcasters. It handles `session_welcome` (store the session and reconcile), `session_keepalive`, `session_reconnect`, `notification`, and `revocation`. Reconcile runs on welcome and every `reconcileMinutes`. Reconnect uses backoff.

| EventSub type | Normalized `type` | Scope |
|---|---|---|
| `channel.channel_points_custom_reward_redemption.add` | `channel_points` | `channel:read:redemptions` |
| `channel.channel_points_custom_reward_redemption.update` | `channel_points_update` | `channel:read:redemptions` |
| `channel.channel_points_automatic_reward_redemption.add` | `automatic_reward` | `channel:read:redemptions` |
| `channel.cheer` | `bits` | `bits:read` |
| `channel.subscribe` | `sub` | `channel:read:subscriptions` |
| `channel.subscription.gift` | `gift_sub` | `channel:read:subscriptions` |
| `channel.subscription.message` | `resub` | `channel:read:subscriptions` |
| `channel.raid` | `raid` | none |
| `channel.follow` | `follow` | `moderator:read:followers` |
| `channel.chat.message` | `chat` | `user:read:chat` |
| `channel.hype_train.begin/progress/end` | `hype_train` | `channel:read:hype_train` |

Normalization also carries broadcaster and viewer IDs, native amount and unit, reward fields, sub tier, months, gift count, message, timestamps, and `extra` values such as hype phase or streak months.

### Deduplication and delivery

- Every event needs a provider-stable `id`. The relay dedupes per game server by `platform:id`.
- A seen-id ring of 2,000 entries drops duplicates after a restart or reconnect.
- Events are written to `data/events/<serverId>.json` when queued, so a game-server restart does not lose a redemption.
- `eventRetentionHours` prunes old events; `maxEventsPerServer` caps the queue and drops the oldest first.
- The plugin fetches pending events, processes them, then acknowledges IDs. Acknowledgements accept `platform:id` (preferred) and plain ids for compatibility.
- Unacknowledged events remain after a plugin restart.
- Shutdown calls `SaveAll()` to flush every queue.
- Custom webhook events use `platform = "custom"` and follow the same queue and dedupe rules.

This is intentionally a single-relay design. For a very large network, shard by relay rather than adding a shared database.

---

## HTTP API

All `/api` routes require HMAC authentication. `/health` is open. `/webhook/custom` uses the custom webhook token instead of server HMAC.

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/health` | Liveness, version, EventSub state, subscription count, pending total |
| `POST` | `/api/link/start` | Start a Twitch device-code link or create a custom webhook link |
| `GET` | `/api/link/status?sessionId=` | Poll a Twitch link session |
| `POST` | `/api/link/cancel?sessionId=` | Cancel a pending link |
| `GET` | `/api/links` | List links for the authenticated server |
| `POST` | `/api/unlink` | Remove a link by platform/user/Steam ID |
| `GET` | `/api/events/pending?limit=` | Fetch pending events; limit clamped to 1–500 |
| `POST` | `/api/events/ack` | Acknowledge event IDs |
| `POST` | `/api/test-event` | Queue a local test event |
| `POST` | `/api/reconcile` | Recreate EventSub subscriptions |
| `GET` | `/api/status` | EventSub state, session, subscriptions, pending events, link count |
| `GET` | `/api/providers` | Configured providers, link modes, capabilities, link counts |
| `POST` | `/webhook/custom` | Public signed custom webhook endpoint |

`/api/link/start` accepts `{ "platform": "twitch" | "custom", "steamId": "...", "playerName": "..." }`. For Twitch it returns a session id, user code, verification URI, expiry, and poll interval. For custom it returns `status: "linked"`, the webhook URL, and the one-time token.

`/api/providers` currently returns the two implemented providers. It is the contract the plugin uses for capability-aware UI; adding a provider means adding an adapter and a row here, not changing the plugin's tier model.

---

## Deployment

Use `listen: http://127.0.0.1:8090` and plugin `RelayUrl: http://127.0.0.1:8090` when the relay and game server share a host. If the game server runs in a container, `127.0.0.1` means the container itself; bind to the Docker bridge or host address and firewall it.

### Remote relay with TLS

```caddyfile
relay.example.com {
    reverse_proxy 127.0.0.1:8090
}
```

Open 443 to the game server, ideally allowlisting its IP. Set the plugin `RelayUrl` to `https://relay.example.com`. The service account must own `relay.json` and `data/`.

For a private network, bind to the Tailscale or WireGuard address and use `http://100.x.y.z:8090`; plain HTTP is acceptable inside the tunnel. A Cloudflare Tunnel (`cloudflared tunnel --url http://127.0.0.1:8090`) needs no inbound port.

!!! danger "Never expose plain HTTP to the internet"
    HMAC protects integrity and authenticity, not confidentiality. Terminate TLS at a reverse proxy or bind only to a private network. Keep NTP running on both hosts; signed requests are accepted only within five minutes.

### systemd

The repository includes `MediaRelay.service`. Adjust `User=`, `Group=`, and the `/opt/mediarelay` paths for your host.

```ini
[Unit]
Description=Media Relay for 7 Days to Die
After=network-online.target

[Service]
Type=simple
User=sdtd
Group=sdtd
WorkingDirectory=/opt/mediarelay
ExecStart=/opt/mediarelay/MediaRelay
Restart=always
Environment=MEDIA_RELAY_CONFIG=/opt/mediarelay/relay.json
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true
PrivateTmp=true
RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX
CapabilityBoundingSet=
UMask=0077
ReadWritePaths=/opt/mediarelay/data /opt/mediarelay/relay.json

[Install]
WantedBy=multi-user.target
```

The shipped unit also enables `PrivateDevices`, kernel/clock protection, `MemoryDenyWriteExecute`, `SystemCallFilter=@system-service`, `LimitNOFILE=65536`, and `TasksMax=512`. Keep those unless your host requires an exception.

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now mediarelay
sudo journalctl -u mediarelay -f
```

Check health from the game server:

```bash
curl -fsS https://relay.example.com/health
```

---

## Security hardening

- Twitch OAuth tokens are encrypted at rest; only the relay can decrypt them.
- Custom webhook tokens are stored as SHA-256 hashes and shown once.
- Every server API request is HMAC-signed with a timestamp and one-time nonce.
- Custom webhook requests use a separate HMAC, timestamp window, nonce cache, per-link rate limit, and 64 KiB body cap.
- Run as a dedicated unprivileged user; keep `relay.json` at `600` and `data/` at `700`.
- Default-deny inbound firewall; allow only 443 from the game server or the private tailnet address, and restrict outbound egress to Twitch and future enabled provider hosts.
- Keep the OS and .NET runtime patched. Do not install a shell, compiler, or package manager on the relay host.
- The `ADMIN.md` design requires loopback/tailnet binding, mTLS, TOTP, CSRF, an audit log, and systemd lockdown before any web admin is exposed. None of that UI exists in this build.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `/health` works but `/api` returns 401 | Server ID/key mismatch, clock skew, or a reused nonce | Match `servers[].id`/`apiKey` in the plugin; check NTP; restart the relay after config edits |
| `Request timestamp is outside the allowed window` | Clocks differ by more than five minutes | Enable NTP on both hosts |
| `Invalid request signature` or `nonce was already used` | Wrong key, wrong body, or a reused nonce | Sign `timestamp\nnonce\nbody` with the exact server `apiKey`; use a fresh nonce per request |
| `Twitch ClientId is not configured on the relay` | Empty `clientId` | Create the Twitch app and set the Client ID |
| `/media link custom` returns a localhost URL | `publicBaseUrl` still points at loopback | Set it to the relay's public HTTPS URL and restart |
| Custom webhook returns 404 | Unknown or rotated token | Unlink/relink `custom` and update the sender |
| Custom webhook returns 429 | Sender exceeds 120 requests per minute for one link | Slow down or batch events; the limit is per link id |
| No subscriptions in the log | Not Affiliate/Partner, missing scope, or optional event disabled | Re-link; check `enableFollows`, `enableChat`, `enableHypeTrain` |
| `could not decrypt token store` | `secret.key` was replaced or is not 32 bytes | Restore the matching key; otherwise every streamer must re-link |
| Linux OpenSSL/libssl error | Missing system OpenSSL | Install `libssl3` or the package for your distribution |
| Events queue but do not apply | Plugin not polling, platform disabled, or no matching trigger | Run `/media relaytest`, check `EnabledPlatforms`, trigger, tier, and loadout |

More: [Troubleshooting](../../reference/troubleshooting/index.md).

---

## Current limitations and admin status

- **Twitch and custom webhook only.** YouTube and Kick are the next adapters; every other platform is research or manual/webhook.
- **No web admin.** The design and lockdown checklist exist, but no admin listener, UI, TOTP, mTLS, or CSRF surface is exposed in 0.3.0.
- **One relay process.** It holds one Twitch application, one EventSub WebSocket, and all per-server queues. Shard by relay for a very large network.
- **In-memory link and replay state.** Pending Twitch device-code sessions, nonce caches, and custom webhook windows reset on restart; completed links survive in `tokens.enc`.
- **Custom webhook token is shown once.** Only its hash is stored; unlink and relink to rotate it.
- **No refund reversal and no default chat trigger.** Effects apply on redemption `add`; canceled/refunded redemptions are not automatically reversed. Chat subscriptions are optional and off by default.
- **No external database.** Queues are per-server JSON files; there is no Redis dependency.
- **Provider health is limited.** `/api/status` reports relay and EventSub state; `/api/providers` reports static capability rows and link counts. The full per-provider health design is not implemented.

---

## Related pages

- [Media Integration](../utilities/media-integration.md)
- [Twitch Integration](../utilities/twitch-integration.md)
- [TwitchRelay](twitch-relay.md)
- [Server Assist API](../../integration/api/index.md)
- [Panels and hooks](../../integration/api/panels.md)
- [Providers](../../integration/api/providers.md)
- [Troubleshooting](../../reference/troubleshooting/index.md)
