# Twitch Integration for 7 Days to Die

A Server Assist panel plus a standalone Twitch relay that lets a streamer:

- link their Twitch channel to their server character without ever sharing a stream key,
- opt in or out of Twitch effects,
- see every buff, debuff, and reward staff have defined for their permission tiers,
- shortlist several candidates per category, and
- let the server roll one buff, one debuff, and one reward when a viewer triggers the tier.

Bits, subscriptions, gift subs, resubs, channel point redemptions, automatic rewards, raids, follows, and hype trains are supported.

> **Status:** working MVP. The relay and Oxide plugin compile, and the relay's event pipeline has been smoke-tested. Live Twitch linking requires a Twitch developer application (see [Setup](#setup)).

---

## Architecture

```text
Twitch EventSub
      |
      |  outbound WebSocket (the relay connects out)
      v
TwitchRelay (standalone .NET service)
  - Twitch device-code OAuth
  - encrypted token vault
  - EventSub subscriptions + reconnect
  - normalization, dedupe, per-server event queues
      |
      |  HTTPS + HMAC (the game plugin polls out)
      v
TwitchIntegration.cs (Oxide plugin)
  - Server Assist "Twitch" panel
  - Steam ID <-> Twitch user linking
  - permission tiers + loadout shortlists
  - cooldowns, randomization, effect adapters
  - audit trail
      |
      +-- buffplayer / debuffplayer
      +-- give
      +-- whitelisted console commands (Chaos Tricks, weather, etc.)
      +-- OnTwitchEffect / OnTwitchEffectAction hooks
```

The game server never holds Twitch tokens and never opens an inbound port. The relay holds the Twitch Client Secret and the OAuth tokens.

---

## Security model

**The streamer never gives you their stream key.** That is the point of the design.

| Concern | How it is handled |
|---|---|
| Stream key | Never requested, never stored, not needed by any Twitch API |
| Streamer credentials | Twitch OAuth device-code flow; the streamer approves a short code on `twitch.tv/activate` |
| Twitch app secret | Lives only in the relay's `relay.json`; never in the Oxide plugin |
| Tokens at rest | AES-GCM encrypted in `data/tokens.enc`; key in `data/secret.key` |
| Token scope | Minimal read scopes; write scope only if you enable auto-fulfill |
| Game server <-> relay | Per-server API key + HMAC-SHA256 signature + timestamp + one-time nonce |
| Account linking | One-time device code bound server-side to the authenticated Steam ID; expires in minutes |
| Fake events | EventSub WebSocket session; broadcaster ID checked against the linked account; duplicate message IDs dropped |
| Command injection | Viewers never supply commands; staff define templates; command prefixes are whitelisted; values are sanitized |
| Spam / griefing | Global cooldown, per-viewer cooldown, effects-per-minute cap, trigger cooldowns, minimum Bits |
| Refunded redemptions | `channel_points_update` events are currently ignored; effects apply on `add`. Auto-cancel/refund is a documented next step |
| Permission escalation | Tier permission is re-checked at trigger time, not only when the loadout was saved |
| Client tampering | All decisions are server-side; the panel only sends action names/IDs, which are re-validated |

The streamer can revoke access at any time at <https://www.twitch.tv/settings/connections>. When a token is revoked or refresh fails, the relay logs it and the streamer must re-link.

### Do you need a Twitch account?

You need **one Twitch developer application**, not a per-streamer account:

1. Sign in to <https://dev.twitch.tv/console/apps> with the operator's Twitch account.
2. Create an application. A redirect URL is not used by the device-code flow; `http://localhost` is fine as a placeholder.
3. Copy the **Client ID** (and **Client Secret** if you want refresh-token support) into `relay.json`.

Each streamer then authorizes that application individually. No streamer password, stream key, or account sharing is involved.

A separate **bot account** is only needed if you want the relay to send Twitch chat messages. This MVP sends notifications in game chat, not Twitch chat.

### Affiliate / Partner requirement

Subscriptions, gift subs, Bits, and channel points only exist for **Affiliate or Partner** channels. Follows, raids, and chat work for any channel. If a streamer is not Affiliate, the relay will simply never receive those event types for them.

---

## Setup

### 1. Build and run the relay (Linux x86_64)

The relay is a normal .NET 8 service. The published build is self-contained, so the Linux server does not need .NET installed.

**Option A — build on the Linux server** (recommended if the .NET 8 SDK is installed):

```bash
cd TwitchIntegration/relay
chmod +x publish.sh
./publish.sh
```

Output: `relay/bin/Release/net8.0/linux-x64/publish/TwitchRelay` — a Linux ELF executable, not a `.exe`.

**Option B — cross-publish from Windows:**

```powershell
cd TwitchIntegration
.\relay\publish.ps1 -Runtime linux-x64
```

Output folder: `relay\bin\Release\net8.0\linux-x64\publish\`.

For local Windows testing only, use `.\relay\publish.ps1 -Runtime win-x64`, which produces `TwitchRelay.exe`.

Copy the publish folder to the Linux server, for example `/opt/twitchrelay`, then:

```bash
cd /opt/twitchrelay
chmod +x TwitchRelay
./TwitchRelay
```

First run creates `relay.json` next to the binary, generates a random `ApiKey` for the `main` server, and creates `data/secret.key`. Stop it, edit `relay.json`, then start it again.

```json
{
  "ClientId": "YOUR_TWITCH_CLIENT_ID",
  "ClientSecret": "YOUR_TWITCH_CLIENT_SECRET",
  "Listen": "http://127.0.0.1:8090",
  "DataDir": "data",
  "EnableChat": false,
  "EnableFollows": true,
  "EnableHypeTrain": true,
  "RequestManageRedemptions": false,
  "Servers": [
    {
      "Id": "main",
      "Name": "Main Server",
      "ApiKey": "the-generated-key"
    }
  ]
}
```

Then lock down the state files:

```bash
chmod 600 relay.json
chmod 700 data
```

**Run it as a systemd service:**

```bash
sudo chown -R sdtd:sdtd /opt/twitchrelay
sudo cp TwitchRelay.service /etc/systemd/system/TwitchRelay.service
sudo systemctl daemon-reload
sudo systemctl enable --now TwitchRelay
sudo journalctl -u TwitchRelay -f
```

Adjust `User=`, `Group=`, and the `/opt/twitchrelay` paths in `TwitchRelay.service` if your server uses a different account or directory. The service account must own `relay.json` and `data/` so it can write the token vault and event queues.

The default publish target is glibc (`linux-x64`), which covers Debian, Ubuntu, and most game-server images. For Alpine/musl, publish `linux-musl-x64` instead:

```bash
./publish.sh linux-musl-x64
```

- `ClientSecret` is optional for the device-code flow but recommended so the relay can refresh tokens. Keep it on the relay only.
- `EventSubUrl` defaults to `wss://eventsub.wss.twitch.tv/ws`. Twitch retired the old `eventsub.wttv.tv` host, so do not change this unless Twitch announces a new endpoint.
- `Listen` can stay on `127.0.0.1` when the relay and game server are on the same machine. If the 7DtD server runs in a container, `127.0.0.1` means the container itself; bind the relay to the Docker bridge/host address and firewall it. If the relay is remote, put TLS in front of it (Caddy/nginx) or configure ASP.NET Core HTTPS; never expose plain HTTP to the internet.
- Add one `Servers` entry per game server. Each gets its own `Id` and `ApiKey`.
- `EnableChat` is off by default. Chat events are delivered but the default plugin has no chat trigger.
- `RequestManageRedemptions` adds `channel:manage:redemptions`; leave it off unless you implement auto-fulfill.

The relay only makes outbound connections to Twitch. The game server only makes outbound connections to the relay. No inbound game-server ports are required.

### 2. Install the Oxide plugin

1. Copy `oxide/plugins/TwitchIntegration.cs` to the server's `oxide/plugins/`.
2. Load the plugin once (`oxide.reload TwitchIntegration` or restart).
3. Edit `oxide/config/TwitchIntegration.json`:
   - `RelayUrl`: `http://127.0.0.1:8090` if local.
   - `ServerId`: must match the relay's `Servers[].Id`.
   - `ServerKey`: the same `ApiKey` from `relay.json`.
4. Reload the plugin.
5. Make sure Server Assist is installed and each client has the Server Assist mod.

The plugin registers a **Twitch** panel for players with `twitch.use`.

### 3. Link a streamer

In game:

```text
/twitch link
```

The panel/chat shows a code and `twitch.tv/activate`. After the streamer approves it, the plugin binds their Twitch user ID to their Steam ID, grants the `twitch` group (configurable), and turns opt-in on.

### 4. Set the relay destination (remote game server)

If the 7DtD server is in a different location or country than the relay, the game server only needs **outbound** access to the relay. The relay needs **outbound** access to Twitch. No inbound game-server ports are required.

**In-game admin panel (recommended):**

```text
/twitch  ->  Admin
```

- Tap **Relay URL** and enter `https://relay.example.com`
- Tap **Server ID** and enter `main`
- Tap **Server Key** and paste `servers[0].apiKey` from `relay.json`
- Tap **Test Relay**

Each value is saved immediately. The key is never displayed again after you submit it.

**Command alternative:**

```text
/twitch connect https://relay.example.com <apiKey> main
/twitch relaytest
```

The command puts the key in chat/command logs, so prefer the panel for production.

**Recommended remote topology:**

1. **VPS + HTTPS (best)** — run the relay on a small VPS, keep `listen` on `http://127.0.0.1:8090`, and put Caddy in front:

   ```text
   relay.example.com {
       reverse_proxy 127.0.0.1:8090
   }
   ```

   Open 443 to the game server (ideally allowlist its IP). Plugin `RelayUrl` is `https://relay.example.com`.

2. **Tailscale / WireGuard (no public exposure)** — bind the relay to the tailnet address (or `0.0.0.0:8090` with a firewall), and set the plugin `RelayUrl` to `http://100.x.y.z:8090`. No TLS is needed inside the tunnel.

3. **Cloudflare Tunnel** — run `cloudflared tunnel --url http://127.0.0.1:8090` on the relay host and use the HTTPS URL it gives you.

**Checks:**

```bash
# from the 7DtD server
curl -fsS https://relay.example.com/health
```

Then in game:

```text
/twitch relaytest
```

Never expose plain HTTP to the internet. HMAC protects integrity, not confidentiality. Keep NTP running on both hosts; signed requests are accepted within a 5-minute window.

---

## Permissions

| Permission | Purpose |
|---|---|
| `twitch.use` | Open the Twitch panel |
| `twitch.streamer` | Granted automatically when a channel is linked (through the `twitch` group by default) |
| `twitch.tier.<id>` | Staff-controlled tier eligibility, e.g. `twitch.tier.tier1`, `twitch.tier.tier2` |
| `twitch.admin` | Relay status, audit, test events, tier grants |

Staff can grant tiers with PermissionGui or:

```text
/twitch tier <player> <tierId>
/twitch untier <player> <tierId>
```

The default groups created by the plugin are registered so they appear in PermissionGui.

---

## Streamer panel

- **Home** — link/unlink, opt-in, pause, per-category toggles (buffs/debuffs/rewards), loadout and catalog buttons.
- **My tier loadouts** — one row per tier. Locked tiers show the permission you need.
- **Tier** — open Buffs, Debuffs, or Rewards and pick a shortlist.
- **Category** — tap an effect to add/remove it. The server stores the shortlist; at trigger time it rolls one effect from each category, weighted by the staff-defined `Weight`.
- **Reward catalog** — read-only view of every effect staff defined for that tier, including descriptions and weights.
- **Admin** — relay URL/server ID/key editor, Test Relay, relay status, link count, pending events, recent audit, reload/reconcile/test buttons.

### How the roll works

For each tier, the streamer picks a shortlist in **all three** categories (unless staff set `RequireAllCategories` to false or left a pool empty). When a matching viewer event fires:

1. The plugin resolves the tier from the trigger config.
2. It checks `twitch.tier.<id>` again.
3. It checks cooldowns and rate limits.
4. It rolls **one** effect from the buff shortlist, **one** from the debuff shortlist, and **one** from the reward shortlist.
5. It applies the effects and writes an audit line.

This is the requested "forced buff, debuff, and reward, but the streamer shortlists multiple per category" model.

---

## Default tiers and triggers

The plugin ships example tiers using 7 Days to Die's native `twitch_*` buffs:

- **Tier 1 — Spark:** speed/regen/looter buffs, slow/blur/no-running debuffs, small items.
- **Tier 2 — Surge:** melee/ranged/infinite-ammo buffs, no-melee/no-ranged/encumbered debuffs, steel/ammo/drinks.
- **Tier 3 — Overload:** toughness/XP/pack-mule buffs, tougher-enemies/double-spawns/no-healing debuffs, premium materials.

Default triggers include Bits at 100/500/1000, sub tiers, resub milestones, gift sub totals, raids by viewer count, hype train end levels, and catch-all channel point/automatic rewards. **Follows are disabled by default** because they are easy to spam. Edit `oxide/config/TwitchIntegration.json` to change any of it.

Channel point redemptions can be matched by `RewardId` or `RewardTitle`. The catch-all `cp_any` trigger has priority `-10`, so any specific reward trigger you add (priority `0` or higher) wins.

Create the actual channel point rewards in the Twitch dashboard. The relay can only see redemptions for rewards that exist on the channel.

Chat events are off by default. If you enable `EnableChat` on the relay, a chat trigger only matches when it has a `Keyword` and the message contains it. A chat trigger with an empty `Keyword` never fires, so a misconfiguration cannot turn every chat line into a reward.

---

## Commands

```text
/twitch                     Open the panel
/twitch link                Start a Twitch link
/twitch unlink              Remove the link
/twitch on | off            Opt in / out
/twitch pause | resume      Pause / resume effects
/twitch status              Show link and loadout summary
/twitch loadout             Open the tier loadout page
/twitch catalog             Open the reward catalog

/twitch connect <url> <key> [serverId]  Admin: set the remote relay destination
/twitch relaytest           Admin: test the relay URL and key
/twitch reload              Admin: reload config/permissions
/twitch reconcile           Admin: ask the relay to recreate EventSub subscriptions
/twitch test <type> [amt]   Admin: fire a local test event
/twitch list                Admin: list linked accounts
/twitch who <player>        Admin: inspect a profile
/twitch tier <player> <id>  Admin: grant a tier permission
/twitch untier <player> <id> Admin: revoke a tier permission
/twitch icons [page]        Admin: list icon roles and current mappings
/twitch iconset <role> <value>  Admin: remap one icon role
/twitch forcelink <player> <twitchLogin>  Admin: local-only test link
```

---

## Icon mapping

Every icon is resolved through a logical role in `oxide/config/TwitchIntegration.json` under `Icons`. Roles use one of these forms:

| Form | Behavior |
|---|---|
| `sprite:name` | Game/mod UI sprite |
| `item:name` | Base-game or mod item icon resolved by Server Assist's item atlas |
| `atlas:atlasName:sprite` | Explicit custom atlas sprite |
| `plugin:PluginName` | Live icon from another Server Assist panel/provider via `GetPluginIcon` |
| `panel:panelId` | Live icon from a registered panel via `GetPanelIcon` |
| `none` | Use the vanilla fallback symbol |

The `plugin:` and `panel:` forms use Server Assist's new read-back hooks. If the target plugin or panel is not loaded, the role falls back to its built-in static sprite. That means AdminSuite, PermissionGui, PluginHotReload, and similar mod icons do **not** need new Twitch-specific textures.

Default mapping for the completed streaming set:

| Role | Default |
|---|---|
| Panel | `sprite:sa_stream_panel` |
| Home | `sprite:sa_stream_home` |
| Tier1 / Tier2 / Tier3 | `sprite:sa_stream_tier_1`, `_2`, `_3` |
| Tier fallback | `ui_game_symbol_star` |
| Buff / Debuff | `ui_game_symbol_twitch_buffs`, `ui_game_symbol_twitch_debuffs` (base game) |
| Reward | `sprite:sa_stream_reward` |
| Loadout / Armed / Locked | `sprite:sa_stream_loadout`, `_armed`, `ui_game_symbol_lock` |
| Link / Pending / Linked / Unlink / Cancel | `sprite:sa_stream_link`, `_pending`, `_linked`, `sa_stream_unlink`, `_cancel` |
| Admin | `plugin:AdminSuite` → fallback `sprite:sa_admin_suite` |
| Reload | `plugin:PluginHotReload` → fallback `sprite:sa_plugin_hot_reload` |
| Links / accounts | `plugin:PermissionGui` → fallback `sprite:sa_permission_gui` |
| Players | `sprite:sa_nav_players` |
| Audit | `sprite:sa_nav_logs` |
| Relay / Online / Offline / Pending | `sprite:sa_nav_server` (status tint distinguishes state) |
| Reconcile | `sprite:sa_plugin_hot_reload` |
| Test / Bits / Sub / Points | `sprite:sa_announce_test`, `item:casinoCoin`, `ui_game_symbol_star`, `item:casinoCoin` |
| Check / Cross / Lock | `ui_game_symbol_check`, `ui_game_symbol_x`, `ui_game_symbol_lock` |
| Status paused | `ui_game_symbol_clock` |
| Info / Help | `sprite:sa_nav_help` |
| Prev / Next / Refresh / Back | vanilla UI symbols |

Rewards default to base-game/mod item icons, so no new reward textures are required:

```json
{ "Id": "dukes_100", "Icon": "item:casinoCoin" }
{ "Id": "medkit_3", "Icon": "item:medicalFirstAidKit" }
```

Per-tier and per-effect icons override the role. Use `sprite:sa_stream_reward_dukes` only if you want the hand-painted streaming version.

`PanelIcon` is kept for older configs. New configs should treat `Icons.Panel` as authoritative, or use `/twitch iconset Panel <value>`.

The streaming-specific `sa_stream_*` sprites come from `ServerAssistArt`:

```text
ServerAssistArt/UIAtlases/UIAtlas/sa_stream_*.png
```

The exact current art request for the art agent is:

```text
ServerAssistArt/REQUESTS_TwitchIntegration.md
```

It lists the 30 required streaming sprites and a do-not-paint list for roles covered by base-game icons, existing mod art, or live Server Assist hooks.

Install the art pack on the client as `AAZ_ServerAssistArt` (or copy the PNGs into the Server Assist mod's `UIAtlases/UIAtlas/`). If a client is missing the PNGs, Server Assist falls back to a vanilla symbol. The workspace root `AAZ_ServerAssistArt-1.2.0.zip` includes the full current atlas, including the `sa_stream_*` and `sa_announce_*` sprites.

## Effect actions

Each effect has one or more actions. Supported types:

| Type | Example | Notes |
|---|---|---|
| `buff` | `twitch_buffFast` | Runs `buffplayer <entityId> "<buff>"` |
| `debuff` | `twitch_buffSlow` | Runs `debuffplayer <entityId> "<buff>"` |
| `item` | `resourceForgedSteel` | Runs `give <entityId> "<item>" <count> [quality]` |
| `command` | `chaos.trick {effect} {steamid}` | First token must be in `AllowedCommandPrefixes` |
| `hook` | `some_hook_value` | Calls `OnTwitchEffectAction(playerId, effectId, value, category, tierId, viewer, amount)` |

Supported command tokens: `{player}`, `{steamid}`, `{twitch}`, `{viewer}`, `{amount}`, `{tier}`, `{effect}`, `{reward}`, `{category}`.

The plugin also calls:

```csharp
OnTwitchEffect(string playerId, string effectId, string category, string tierId,
               string eventType, string viewerLogin, long amount)
```

That is the clean integration point for Chaos Tricks or any other mod that exposes a hook.

---

## Files

Plugin:

```text
oxide/plugins/TwitchIntegration.cs
oxide/config/TwitchIntegration.json          (created on first load)
oxide/data/TwitchIntegration/players.json    (links, loadouts, cooldowns, audit)
```

Relay:

```text
relay.json
data/secret.key        (AES key; protect it)
data/tokens.enc        (encrypted Twitch tokens)
data/events/*.json     (pending events per server)
```

---

## Troubleshooting

**The relay returns 401 to every plugin request**
- `ServerId` and `ServerKey` must match the relay's `Servers` entry exactly.
- The game server and relay clocks must be within 5 minutes. Check NTP.
- If you changed `relay.json`, restart the relay so the new key is loaded.

**`/twitch link` says the relay is not configured**
- Set `RelayUrl` and `ServerKey` in `oxide/config/TwitchIntegration.json`, then `/twitch reload`.

**The relay says `Twitch ClientId is not configured`**
- Create a Twitch application at <https://dev.twitch.tv/console/apps> and put its Client ID in `relay.json`.

**The relay log says `Name or service not known (eventsub.wttv.tv:443)`**
- You are running an older relay build. Twitch retired `eventsub.wttv.tv`; the current host is `wss://eventsub.wss.twitch.tv/ws`. Replace the relay with the current package, or set `"eventSubUrl": "wss://eventsub.wss.twitch.tv/ws"` in `relay.json` if your build supports that setting.

**The link code expires**
- The device code is short-lived by design. Run `/twitch link` again.

**No subscriptions appear in the relay log**
- The streamer may not be Affiliate/Partner (subs, Bits, and channel points require it).
- The token may be missing a scope. Unlink and re-link; the relay requests the scopes again.
- `channel.follow` and `channel.chat.message` depend on optional features; see `relay.json`.

**The Linux relay fails with an OpenSSL/libssl error**
- The self-contained build still uses the system OpenSSL for HTTPS to Twitch. Install `libssl3` on Ubuntu 22.04+/Debian 12, or `libssl1.1` on older distributions.

**`Permission denied` when starting the Linux relay**
- Run `chmod +x TwitchRelay`.
- Make sure the service account owns `relay.json` and `data/`.

**Remote relay**
- Do not expose plain HTTP to the internet. Terminate TLS at a reverse proxy (Caddy, nginx, IIS) or configure ASP.NET Core HTTPS certificates. Firewall the relay so only the game server can reach it.

## Known limitations / next steps

- **Channel point refunds:** effects apply on redemption `add`. If a redemption is later canceled/refunded, the effect is not automatically reversed. Add `channel:manage:redemptions` and auto-cancel/fulfill if you need that.
- **Native 7DtD Twitch integration:** the game ships its own Twitch extension config (`Data/Config/twitch.xml`). This plugin reuses the native `twitch_*` buffs but not the native backend. Do not run both systems on the same buffs/actions unless you want double effects.
- **Single relay process:** one WebSocket handles all linked broadcasters. For a very large network, shard by relay.
- **No web admin page:** reward/tier pools are edited in JSON and loaded with `/twitch reload`. The relay URL, server ID, and key can be edited from the Server Assist Admin panel or with `/twitch connect`.
- **No Twitch chat output:** notifications are in game chat. Add a bot account if you want Twitch chat replies.
