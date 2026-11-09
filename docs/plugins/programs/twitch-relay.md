# TwitchRelay

> **Version:** 0.1.1 · **Type:** standalone .NET 8 ASP.NET Core service · **Source:** [TwitchIntegration/relay](https://github.com/TheRealM18/Oxide-Server-Assist/tree/main/TwitchIntegration/relay)

TwitchRelay is the standalone service behind the Server Assist **Twitch** panel.
It holds the Twitch application secret and OAuth tokens, maintains one Twitch
EventSub WebSocket, normalizes events, and queues them per game server. The
Oxide plugin polls the relay over HTTPS with HMAC authentication; the game server
never holds Twitch tokens and never opens an inbound port.

```text
Twitch EventSub
      |  outbound WebSocket (relay connects out)
      v
TwitchRelay (.NET 8)
  - device-code OAuth, encrypted token vault
  - EventSub subscriptions + reconnect
  - normalization, dedupe, per-server queues
      |  HTTPS + HMAC (plugin polls out)
      v
TwitchIntegration.cs (Oxide plugin)
  - Server Assist "Twitch" panel, linking, tiers
  - cooldowns, randomization, effect adapters, audit
```

!!! important
    The streamer never shares a stream key. Linking uses Twitch's official
    device-code flow on `twitch.tv/activate`; the relay stores only OAuth tokens.

---

## Requirements

| Requirement | Detail |
|---|---|
| Runtime | .NET 8; the published Linux build is self-contained |
| Twitch application | One developer application with Client ID and optional Client Secret |
| Twitch account type | Affiliate/Partner for subs, gift subs, Bits and channel points |
| Game server | Outbound HTTPS access to the relay |
| Relay host | Outbound access to Twitch over HTTPS/WSS |
| Server Assist | Installed on the server and every client that uses the Twitch panel |
| Clock sync | NTP on both hosts; signed requests allow a 5-minute window |
| TLS | Reverse proxy or ASP.NET Core HTTPS when the relay is remote |

Follows, raids and chat work for any channel. Subscriptions, gift subs, Bits and
channel points only exist for Affiliate or Partner channels.

---

## Build, publish, install

Linux relay host:

```bash
cd TwitchIntegration/relay
chmod +x publish.sh && ./publish.sh
# output: relay/bin/Release/net8.0/linux-x64/publish/TwitchRelay
```

Cross-publish from Windows: `.\relay\publish.ps1 -Runtime linux-x64`. For local
Windows testing use `-Runtime win-x64`; for Alpine/musl use `linux-musl-x64`.

```bash
mkdir -p /opt/twitchrelay
# copy the publish folder contents to /opt/twitchrelay
cd /opt/twitchrelay
chmod +x TwitchRelay
./TwitchRelay
```

First run creates `relay.json`, generates a random `ApiKey` for the `main`
server, and creates `data/secret.key`. Stop it, edit `relay.json`, then start
again. Lock down the state with `chmod 600 relay.json` and `chmod 700 data`.

The self-contained Linux build still uses the system OpenSSL for HTTPS to
Twitch. Install `libssl3` on Ubuntu 22.04+/Debian 12, or `libssl1.1` on older
distributions.

---

## Configuration

File: `relay.json` next to the executable. The path can also be the first
argument or `TWITCH_RELAY_CONFIG`; otherwise the service checks
`appsettings.json`, then `relay.json`.

```json
{
  "clientId": "YOUR_TWITCH_CLIENT_ID",
  "clientSecret": "YOUR_TWITCH_CLIENT_SECRET",
  "eventSubUrl": "wss://eventsub.wss.twitch.tv/ws",
  "listen": "http://127.0.0.1:8090",
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
    { "id": "main", "name": "Main Server", "apiKey": "GENERATED_KEY" }
  ]
}
```

| Key | Default | Notes |
|---|---|---|
| `clientId` | empty | Twitch application Client ID; required for linking |
| `clientSecret` | empty | Optional; enables token refresh |
| `eventSubUrl` | `wss://eventsub.wss.twitch.tv/ws` | Current Twitch EventSub endpoint |
| `listen` | `http://127.0.0.1:8090` | Kestrel listen URL |
| `dataDir` | `data` | Resolved relative to the config file |
| `linkSessionMinutes` | `10` | Clamped to `1`–`60` |
| `eventRetentionHours` | `72` | Clamped to `1`–`720` |
| `maxEventsPerServer` | `2000` | Clamped to `50`–`20000` |
| `httpTimeoutSeconds` | `20` | Clamped to `5`–`120` |
| `reconcileMinutes` | `5` | Clamped to `1`–`60` |
| `enableChat` | `false` | Adds `user:read:chat`; plugin has no default chat trigger |
| `enableFollows` | `true` | Adds `moderator:read:followers` |
| `enableHypeTrain` | `true` | Adds `channel:read:hype_train` |
| `requestManageRedemptions` | `false` | Adds `channel:manage:redemptions` |
| `servers` | one generated `main` entry | One entry per game server |

Each `servers[]` entry has `id`, `name` and `apiKey`. IDs are lowercased and
deduplicated; duplicate IDs keep the first entry. Empty API keys are generated
on startup.

!!! warning
    Keep `clientSecret` and `apiKey` on the relay only. Do not copy them into the
    Oxide plugin or a client mod. The plugin gets only its own per-server key.

---

## EventSub

`EventSubService` maintains **one** outbound WebSocket for all linked
broadcasters. It handles `session_welcome` (store session and reconcile),
`session_keepalive`, `session_reconnect`, `notification` and `revocation`.
Keep-alive is 20 seconds; reconnect backoff starts at 1 second and doubles to 60.
Reconcile runs on welcome and every `reconcileMinutes`.

| EventSub type | Normalized `type` | Scope |
|---|---|---|
| `channel.channel_points_custom_reward_redemption.add` | `channel_points` | `channel:read:redemptions` |
| `.update` | `channel_points_update` | `channel:read:redemptions` |
| `channel.channel_points_automatic_reward_redemption.add` | `automatic_reward` | `channel:read:redemptions` |
| `channel.cheer` | `bits` | `bits:read` |
| `channel.subscribe` | `sub` | `channel:read:subscriptions` |
| `channel.subscription.gift` | `gift_sub` | `channel:read:subscriptions` |
| `channel.subscription.message` | `resub` | `channel:read:subscriptions` |
| `channel.raid` | `raid` | none |
| `channel.follow` | `follow` | `moderator:read:followers` |
| `channel.chat.message` | `chat` | `user:read:chat` |
| `channel.hype_train.begin/progress/end` | `hype_train` | `channel:read:hype_train` |

A token missing the required scope skips that subscription and logs a re-link
instruction. `requestManageRedemptions` only adds the OAuth scope; effects still
apply on redemption `add`, so canceled/refunded redemptions are not
automatically reversed.

The relay checks the WebSocket session and Twitch metadata, verifies the
broadcaster ID against the linked account, and drops duplicate
`metadata.message_id` values per server before queueing.

---

## Linking

```text
Player: /twitch link
  -> plugin POST /api/link/start {steamId, playerName}   (HMAC)
  -> relay starts device-code OAuth and returns {sessionId, userCode, verificationUri}
  -> plugin shows "go to twitch.tv/activate and enter CODE"

Streamer approves the code
  -> relay validates the token, fetches the Helix user
  -> binds twitchUserId -> serverId+steamId, creates EventSub subscriptions
  <- plugin polls /api/link/status, saves the link, grants the `twitch` group
```

| Property | Detail |
|---|---|
| Device-code endpoint | `https://id.twitch.tv/oauth2/device` |
| Token endpoint | `https://id.twitch.tv/oauth2/token` |
| Verification page | `twitch.tv/activate` |
| Config link session | `linkSessionMinutes`, default 10 |
| Device-code expiry | Twitch returns `expires_in`; it governs the approval window |
| Pending poll | `authorization_pending`; `slow_down` adds 5 seconds |
| Steam binding | Server-side Steam ID from `/api/link/start` |
| Account mapping | One Twitch account per character per server; one character per Twitch account per server |

Unlinking removes the per-server link. When no server links remain, the relay
revokes the Twitch token, deletes the record and removes the broadcaster's
EventSub subscriptions.

---

## Token storage

Tokens live only on the relay:

```text
data/
  secret.key     32 random bytes, AES-256 key
  tokens.enc     AES-GCM encrypted token JSON
```

- `secret.key` is generated with `RandomNumberGenerator.GetBytes(32)` when
  missing.
- `tokens.enc` uses AES-GCM with a 12-byte nonce and 16-byte tag.
- Writes go to a temporary file, are permission-restricted, then moved into
  place. The README recommends `chmod 600 relay.json` and `chmod 700 data`.
- Tokens refresh when they expire within two minutes and a refresh token exists.
  If `secret.key` is replaced or is not 32 bytes, existing tokens cannot be read
  and the streamer must re-link.

The game server never receives an OAuth token. The streamer can revoke access at
`https://www.twitch.tv/settings/connections`.

---

## Event storage

There is **no Redis dependency** and no external database. The relay writes a
durable per-server JSON queue:

```text
data/events/main.json
```

- Queue file names are derived from the server ID, with unsafe characters
  replaced by `_`.
- Events are written on enqueue and after acknowledgement; the queue survives a
  relay restart. `eventRetentionHours` prunes old events,
  `maxEventsPerServer` caps the queue, and a seen-id ring of 2,000 entries drops
  duplicates.
- The plugin fetches pending events, processes them, then acknowledges IDs.
  Unacknowledged events remain after a plugin restart.
- Shutdown calls `SaveAll()` to flush every queue.

This is intentionally a single-relay design. For a very large network, shard by
relay rather than adding a shared database.

---

## HTTP API

All `/api` routes require HMAC authentication. `/health` is open.

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/health` | Liveness, version, EventSub state, subscription count, pending total |
| `POST` | `/api/link/start` | Start a device-code link |
| `GET` | `/api/link/status?sessionId=` | Poll a link session |
| `POST` | `/api/link/cancel?sessionId=` | Cancel a pending link |
| `GET` | `/api/links` | List links for the authenticated server |
| `POST` | `/api/unlink` | Remove a link by Twitch user or Steam ID |
| `GET` | `/api/events/pending?limit=` | Fetch pending events (limit 1–500) |
| `POST` | `/api/events/ack` | Acknowledge event IDs |
| `POST` | `/api/test-event` | Queue a local test event |
| `POST` | `/api/reconcile` | Recreate EventSub subscriptions |
| `GET` | `/api/status` | EventSub state, session, subscriptions, pending events, link count |

Every `/api` request sends:

```text
X-Server-Id: main
X-Timestamp: <unix seconds>
X-Nonce: <8-128 character one-time value>
X-Signature: <HMAC-SHA256 hex>
```

The signature is HMAC-SHA256 over the canonical timestamp, nonce and request
body, keyed with the server's `apiKey`. The relay enforces a 300-second clock
window, a nonce length of 8–128, one-time nonce use, and a constant-time
signature comparison. A failed check returns `401`. A changed `relay.json`
requires a relay restart so the new key is loaded.

---

## Oxide plugin connection

The plugin config is `oxide/config/TwitchIntegration.json`. Set `RelayUrl` to
the relay URL, `ServerId` to a `servers[].id`, and `ServerKey` to that server's
`apiKey`. `PollIntervalSeconds` (default `3.0`) controls event polling;
`LinkPollIntervalSeconds` (default `3.0`) and `RequestTimeoutSeconds` (default
`20.0`) control linking and HTTP. Cooldowns default to 15 seconds globally, 60
seconds per viewer, and 10 effects per minute.

The plugin calls the relay only outbound. It registers the **Twitch** panel for
players with `twitch.use`; Server Assist must be installed and every client needs
the Server Assist mod.

| Command | Purpose |
|---|---|
| `/twitch link`, `/twitch unlink` | Start or remove a link |
| `/twitch relaytest` | Admin: test the relay URL and key |
| `/twitch connect <url> <key> [serverId]` | Admin: set the relay destination |
| `/twitch reconcile` | Admin: recreate EventSub subscriptions |
| `/twitch reload` | Admin: reload config and permissions |

Prefer the in-game **Twitch → Admin** panel over `/twitch connect` for the key:
the command writes the key into chat/command logs, while the panel saves each
value immediately and does not display the key again.

---

## Deployment

Local relay: `listen: http://127.0.0.1:8090` and plugin `RelayUrl:
http://127.0.0.1:8090`. If the game server runs in a container, `127.0.0.1`
means the container itself; bind to the Docker bridge/host and firewall it.

Remote relay with TLS:

```caddyfile
relay.example.com {
    reverse_proxy 127.0.0.1:8090
}
```

Open 443 to the game server (ideally allowlist its IP) and set the plugin
`RelayUrl` to `https://relay.example.com`. The service account must own
`relay.json` and `data/`; run it under systemd, NSSM or your host's process
manager. For a private network, bind to the Tailscale/WireGuard address and use
`http://100.x.y.z:8090`; no TLS is needed inside the tunnel. A Cloudflare Tunnel
needs no inbound port (`cloudflared tunnel --url http://127.0.0.1:8090`).

Never expose plain HTTP to the internet. HMAC protects integrity, not
confidentiality.

---

## Server Assist integration

TwitchRelay itself has no Server Assist API calls. The **Oxide plugin** is the
Server Assist companion:

- It registers a home-row GUI hook labelled **Twitch** with `twitch.use` and
  `sprite:sa_stream_panel`.
- The panel uses Server Assist's normal view protocol; all actions are
  re-validated server-side.
- Icon roles can resolve live icons through `plugin:AdminSuite`,
  `plugin:PluginHotReload` and `plugin:PermissionGui`, or through
  `panel:<id>` with `GetPanelIcon`.
- The `sa_stream_*` sprites come from
  [Server Assist Art](../art/server-assist-art.md).
- Players need both the server plugin and the Server Assist client mod to see the
  panel. The relay is independent of the game client.

---

## Typical workflows

**First setup.** Create the Twitch application, put the Client ID and Secret in
`relay.json`, start once to generate keys, restart, set `RelayUrl`, `ServerId`
and `ServerKey` in the plugin, run `/twitch relaytest`, then `/twitch link`.

**Link a streamer.** They open `twitch.tv/activate` and enter the code. The relay
validates the token, binds the account, and the plugin grants the streamer group
and opts them in.

**Recover or verify.** Run `/twitch reconcile` to recreate subscriptions, or
`curl -fsS https://relay.example.com/health` to check `connected`,
`subscriptions` and `pendingEvents`.
---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Every plugin request gets 401 | Server ID/key mismatch, clock skew, reused nonce | Match `ServerId`/`ServerKey`; check NTP; restart relay after config edits |
| `/twitch link` says relay not configured | Empty `RelayUrl` or `ServerKey` | Set both and run `/twitch reload` |
| `Twitch ClientId is not configured` | Missing `clientId` | Create the Twitch app and set the Client ID |
| Link code expires | Device codes are short-lived | Run `/twitch link` again |
| No subscriptions in the relay log | Not Affiliate/Partner, missing scope, or optional event disabled | Re-link; check `enableFollows`, `enableChat`, `enableHypeTrain` |
| EventSub host error for `eventsub.wttv.tv` | Old relay build | Use the current build / `wss://eventsub.wss.twitch.tv/ws` |
| Token refresh fails | Revoked token or missing refresh token | Re-link the streamer |
| Linux relay OpenSSL/libssl error | Missing system OpenSSL | Install `libssl3` or `libssl1.1` |
| Permission denied starting relay | Not executable or data ownership | `chmod +x TwitchRelay`; fix ownership of `relay.json` and `data/` |
| HTTP warning in production | Plain HTTP exposed | Terminate TLS at a reverse proxy and firewall the relay |

More: [Troubleshooting](../../reference/troubleshooting/index.md).
