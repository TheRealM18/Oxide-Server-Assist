# BuggeeNet

> **Version:** 0.2.0 (Oxide plugin name `BuggeeCore`) · **Type:** Server-side Oxide plugin with a shared Redis control plane · **Source:** [BuggeeCore.cs](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/BuggeeNet/oxide/plugins/BuggeeCore.cs)

BuggeeNet is a fleet and redirect system for 7 Days to Die. Players move between backend servers through a shared Redis instance while keeping their inventory, progression, buffs, and other player state.

There is **no client mod**. The client always connects directly to a backend. The "proxy" is a control plane, not a data plane: Redis carries server discovery, session leases, and player snapshots, and a transfer is a kick message containing the destination `host:port`.

!!! note
    The folder and product are **BuggeeNet**, but the Oxide plugin is **`BuggeeCore`**. Use `plugins.Find("BuggeeCore")`, `oxide.reload BuggeeCore`, `oxide/config/BuggeeCore.json`, and the `buggeecore.*` permissions.

---

## Overview

```text
        Redis (shared): registry / leases / snapshots
          |               |               |
      [home:26900]   [horde:26910]   [arena:26920]
       BuggeeCore     BuggeeCore      BuggeeCore
          ▲               ▲               ▲
          └──── players connect directly ─┘
                (redirect = kick + address)
```

| Capability | Status |
|---|---|
| Shared server registry and heartbeats | Working |
| Online/offline peer discovery | Working |
| `/hub`, `/horde`, `/goto` redirect | Working (kick with reconnect address) |
| Prevent dual-login and ghost sessions | Working (Redis lease, compare-and-delete) |
| Shared inventory, belt, backpack, equipment | Working (the game's own `PlayerDataFile` blob) |
| Shared progression, perks, buffs, recipes | Working |
| Cross-server economy, vehicles, land claims | Not yet |
| Seamless handoff without reconnect | Not possible without a client mod |

The transfer sequence is: save snapshot → set transfer ticket → kick with `host:port` → player rejoins → destination writes the snapshot before the vanilla spawn load. No Harmony patch and no client DLL are involved.

### Why redirect instead of a real proxy

A BungeeCord-style proxy needs a documented wire protocol, a way to re-enter play state on the same socket, and a client that tolerates a backend swap. 7 Days to Die has none of these. The session is bound to one loaded world (`GameManager.World`, entity IDs, chunk state) and the transport is an encrypted EOS/Steam connection.

The injection point is `OnUserApprove(ClientInfo)`, which fires before the spawn request. Writing the snapshot to `<playerDataDir>/<InternalId.CombinedString>.ttp` there means the vanilla loader picks it up with no Harmony patch. `OnPlayerConnected` fires too late, after the disk load and after the entity is created.

!!! note
    The game uses `InternalId.CombinedString` for the filename, such as `Steam_7656...`, not Oxide's `IPlayer.Id` or readable platform identifier. Redis keys use the readable ID; disk files use the combined ID.

---

## Requirements

| Requirement | Detail |
|---|---|
| Game server | 7 Days to Die with Oxide/uMod; `EACEnabled=false` |
| Game build | Identical game build on every backend |
| Mod set | Identical mod set on every backend, or transfers are refused when `RequireSameModSet` is true |
| Redis | Shared Redis reachable from every game server |
| Redis durability | `maxmemory-policy noeviction`, `appendonly yes`; snapshots are player data, not a cache |
| Network | Outbound access from each game server to Redis; no inbound fleet ports |
| Client install | None; no client mod |
| Optional | Server Assist for the fleet panel |

Do not expose Redis to the public internet. Use a private network or TLS:

- **Private network (recommended):** WireGuard or Tailscale between the game hosts and the Redis host. Bind Redis to the private IP and firewall 6379 to those peers.
- **TLS:** `tls-port 6379`, `port 0`, certificates from a private CA, and `rediss://` semantics. The current client uses plain TCP, so TLS would need a small addition.

Minimum production settings:

```conf
bind <private-ip>
protected-mode yes
requirepass <long-random-password>
maxmemory-policy noeviction
appendonly yes
appendfsync everysec
```

Also enable host firewalling, keep Redis off public DNS, and back up the AOF/RDB volume. Losing Redis means losing snapshots, not world saves — but that is still player progress.

---

## Installation

### 1. Start Redis

For local development with Docker Desktop:

```powershell
docker compose -f "BuggeeNet\docker\docker-compose.yml" up -d
docker exec buggee-redis redis-cli -a buggee_dev_only_change_me --no-auth-warning PING
```

Expected: `PONG`. The dev container listens on `127.0.0.1:6379`. Its `redis.conf` sets `maxmemory-policy noeviction`, `appendonly yes` plus RDB, and binds to loopback.

### 2. Install the plugin

Copy `oxide/plugins/BuggeeCore.cs` into each server's `oxide/plugins/` directory. Reload or restart. First load writes `oxide/config/BuggeeCore.json`.

### 3. Configure each backend

```json
{
  "ServerId": "home",
  "DisplayName": "Buggee Home",
  "Role": "home",
  "PublicHost": "home.example.com",
  "GamePort": 26900,
  "HomeServerId": "",
  "Redis": {
    "Enabled": true,
    "Host": "127.0.0.1",
    "Port": 6379,
    "Password": "buggee_dev_only_change_me",
    "Database": 0,
    "ConnectTimeoutMs": 1500,
    "CommandTimeoutMs": 1000
  },
  "Network": {
    "KeyPrefix": "bn:",
    "HeartbeatSeconds": 15,
    "HeartbeatTtlSeconds": 45,
    "LeaseSeconds": 300
  },
  "Transfer": {
    "Enabled": true,
    "KickMessage": "Buggee Network\nMoving you to {server}.\n\nReconnect to:\n{host}:{port}",
    "SpawnMode": "Fixed",
    "SpawnX": 0.0,
    "SpawnY": 0.0,
    "SpawnZ": 0.0,
    "ClearOwnedEntities": true,
    "ClearWaypoints": false,
    "RequireSameModSet": true,
    "LocalFileWinsWhenNewer": true
  },
  "Debug": false
}
```

For the horde server, change `ServerId`, `DisplayName`, `Role`, `PublicHost`, and `GamePort`:

```json
"ServerId": "horde",
"DisplayName": "Buggee Horde",
"Role": "horde",
"PublicHost": "horde.example.com",
"GamePort": 26910
```

Set `SpawnMode` and `SpawnX`/`SpawnY`/`SpawnZ` per server. `Fixed` is the safe default because a snapshot carries the source world's coordinates, which may be inside rock on the destination map. Use `Keep` only if both servers share the same map and seed.

!!! warning
    Every backend must run the identical game build and mod set. Item IDs are load-order integers; a mismatch corrupts inventories.

---

## Commands

| Command | Permission | Purpose |
|---|---|---|
| `/buggee`, `/net` | `buggeecore.use` | Show this server's status |
| `/hub`, `/home` | `buggeecore.use` | Redirect to the home server |
| `/horde`, `/hn` | `buggeecore.use` | Redirect to the horde server |
| `/goto <serverId>` | `buggeecore.admin` | Redirect yourself, or from console redirect a named player |
| `/netpeers` | `buggeecore.admin` | Show registry entries and mod-set comparison |
| `/netdiag` | `buggeecore.admin` | Show Redis ping, key prefix, mod hash, game version, and registry size |
| `/netrelease <playerId>` | `buggeecore.admin` | Clear a stuck session lease after a crash |

From the server console, `/goto <serverId> <player>` redirects a named player. From a connected player, `/goto <serverId>` redirects that player.

`/hub` uses `HomeServerId` when it is set. When it is empty, the plugin picks the healthiest peer with `Role == "home"`. `/horde` picks a peer with `Role == "horde"`.

---

## Permissions

| Permission | Grants |
|---|---|
| `buggeecore.use` | Status, `/hub`, `/horde`, and the Server Assist panel |
| `buggeecore.admin` | `/goto`, `/netpeers`, `/netdiag`, `/netrelease`, and panel diagnostics |

Both permissions are registered during `Init`. Travel commands and the panel handler check `buggeecore.use`; admin commands are registered with `buggeecore.admin`.

---

## Configuration

File: `oxide/config/BuggeeCore.json`

| Key | Default | Notes |
|---|---|---|
| `ServerId` | `"home"` | Unique fleet id for this backend |
| `DisplayName` | `"Buggee Home"` | Name shown in status and peer lists |
| `Role` | `"home"` | Free-form; `home` and `horde` are the travel conventions |
| `PublicHost` | `"127.0.0.1"` | Address players reconnect to |
| `GamePort` | `26900` | Game port players reconnect to |
| `HomeServerId` | `""` | Explicit `/hub` destination; empty picks the healthiest `home` peer |
| `Debug` | `false` | Verbose debug logging |
| `Redis.Enabled` | `true` | When false, the plugin runs local-only |
| `Redis.Host` / `Redis.Port` | `"127.0.0.1"` / `6379` | Redis endpoint |
| `Redis.Password` | `"buggee_dev_only_change_me"` | Replace in production |
| `Redis.Database` | `0` | Redis database index |
| `Redis.ConnectTimeoutMs` / `CommandTimeoutMs` | `1500` / `1000` | Minimum 250 each |
| `Network.KeyPrefix` | `"bn:"` | Prefix for every Redis key |
| `Network.HeartbeatSeconds` / `HeartbeatTtlSeconds` | `15` / `45` | Minimum 5; TTL forced above the heartbeat interval when invalid |
| `Network.LeaseSeconds` | `300` | Session lease lifetime; minimum 60 |
| `Transfer.Enabled` | `true` | Master switch for transfers |
| `Transfer.KickMessage` | `"Buggee Network\nMoving you to {server}.\n\nReconnect to:\n{host}:{port}"` | Placeholders: `{server}`, `{host}`, `{port}`, `{role}`, `{reason}` |
| `Transfer.SpawnMode` | `"Fixed"` | `Fixed`, `Keep`, or `WorldSpawn` |
| `Transfer.SpawnX` / `SpawnY` / `SpawnZ` | `0.0` / `0.0` / `0.0` | Fixed spawn coordinates |
| `Transfer.ClearOwnedEntities` | `true` | Remove vehicles and drones before transfer |
| `Transfer.ClearWaypoints` | `false` | Clear waypoints on transfer |
| `Transfer.RequireSameModSet` | `true` | Refuse transfers when mod sets differ |
| `Transfer.LocalFileWinsWhenNewer` | `true` | Keep a local `.ttp` newer than the fleet snapshot |

`SpawnMode` behavior:

| Value | Behavior |
|---|---|
| `Fixed` | Use `SpawnX`, `SpawnY`, `SpawnZ` on the destination |
| `Keep` | Keep source coordinates; only safe when both servers share the same map and seed |
| `WorldSpawn` | Ask the target world for its spawn point |

---

## Data and storage

BuggeeCore does not use an `oxide/data` file. Its durable state is Redis plus the game's own player-data files.

| Path | Purpose |
|---|---|
| `oxide/plugins/BuggeeCore.cs` | The Oxide plugin |
| `oxide/config/BuggeeCore.json` | Configuration, created on first load |
| `<playerDataDir>/<InternalId.CombinedString>.ttp` | Per-player save used as the transfer medium |
| Redis keys under `KeyPrefix` | Registry, leases, tickets, snapshots, presence |

The snapshot is the game's own save container:

```text
"ttp\0" | version byte (62) | PlayerDataFile.Write(..., Persistency)
```

`PlayerDataFile` covers belt, backpack, equipment, progression, buffs, quest journal, waypoints, and stats. `FromPlayer()` captures and `ToPlayer()` applies. The plugin never hand-serializes `ItemValue` fields, so modded items, ammo, durability, and mod slots survive.

The Redis envelope is binary-safe and atomic in one key:

```text
"BGG1" | u8 format | i64 savedAt | str sourceServer | str gameVersion | str modHash | i32 len | payload
```

### Redis keys

Default prefix `bn:`.

| Key | Type | TTL | Purpose |
|---|---|---|---|
| `bn:servers` | SET | none | All server IDs ever registered |
| `bn:srv:<id>` | HASH | heartbeat TTL | `id`, `name`, `role`, `host`, `port`, `players`, `version`, `mods` |
| `bn:active:<steamId>` | STRING | lease | Which server owns this player |
| `bn:transfer:<steamId>` | STRING | 180s | One-shot ticket: destination server id |
| `bn:snap:<steamId>` | STRING (binary) | none | Latest snapshot envelope |
| `bn:snapmeta:<steamId>` | HASH | none | `savedAt`, `server`, `reason`, `bytes` |
| `bn:presence:<steamId>` | HASH | presence TTL | `name`, `server` |

Redis must use `noeviction`. Snapshots are player data, not a cache.

---

## Redis relay and fleet state

Redis is the shared relay between the backends. It is not a traffic proxy: players still connect directly to a game server. Redis carries:

- **Server registry** — `bn:servers` plus one `bn:srv:<id>` hash per backend.
- **Heartbeats** — each server refreshes its record every `HeartbeatSeconds`; the record expires after `HeartbeatTtlSeconds`.
- **Session leases** — `bn:active:<steamId>` records which server owns a player.
- **Transfer tickets** — `bn:transfer:<steamId>` is a one-shot destination ticket with a 180-second lifetime.
- **Player snapshots** — `bn:snap:<steamId>` and `bn:snapmeta:<steamId>`.
- **Presence** — `bn:presence:<steamId>` with name and server.

If Redis is disabled or unreachable at boot, the plugin logs an error, fleet features go offline, and local `.ttp` saves still work. If Redis dies mid-session, commands fall back to local behavior, transfers refuse, and the plugin retries on the next heartbeat.

### Fleet and peer status

| Tool | What it shows |
|---|---|
| `/buggee` or `/net` | Server name, id, role, public address, Redis connected/offline, game version, available commands |
| `/netpeers` | Every registered peer with name, id, `host:port`, role, player count, and `mods=match` or `mods=DIFFERENT`; stale entries marked `(stale)` |
| `/netdiag` | Redis `PING`, key prefix, local mod hash, local game version, registry entry count |
| Server Assist panel | Overview, All Servers, Confirm, and Diagnostics screens |

`/netdiag` returning a Redis error means the control plane is unavailable; players can still use local saves.

---

## Server Assist integration

| Item | Value |
|---|---|
| Panel id | `buggee` |
| Label | Buggee Network |
| Permission | `buggeecore.use` |
| Icon | `sprite:sa_stream_link` |
| Order | `95` |
| Handler | `HandleGuiRequest` |
| Entry | `/buggee`, `/net`, Home row |

Screens:

- **Overview** — this server with a `YOU` badge, plus every healthy peer with role, player count, and mod-set status.
- **Confirm** — target address, role, population, and mod-set check; travel is disabled when mod sets differ.
- **All Servers** — the same list with full `host:port`.
- **Diagnostics** (`buggeecore.admin`) — Redis ping, endpoint, key prefix, registry size, game version, mod hash.

Registration retries at 2 and 5 seconds and again on `OnServerAssistReady()`, falls back to `RegisterGuiHook` on older Server Assist builds, calls `SetPanelIcon` after a fallback registration, and unregisters with `UnregisterPanel` on plugin unload. The panel is additive: the commands still work without Server Assist.

---

## Travel workflow

```text
home                         Redis                         horde
  | /horde                     |                              |
  |--> pick peer, gate mods -->| SET snap:<id>                |
  |--> save snapshot, ticket ->| SET transfer:<id> = horde    |
  |<-- kick "horde:26910" -----|                              |
  | connect directly ---------------------------------------->|
  |                            | GET transfer:<id>, active:   |
  |                            |<-- claim lease --------------|
  |                            | GET snap:<id> -------------->|
  |                            |<-- .ttp -> playerDataDir     |
```

The destination writes the snapshot into `<playerDataDir>/<InternalId.CombinedString>.ttp` in `OnUserApprove`, before the vanilla spawn load.

### Dupe protection

1. **Session lease** — `bn:active:<id>` is set on approve, refreshed every heartbeat only by the owner, and released on disconnect with compare-and-delete. A second server sees a live lease and rejects the login.
2. **One-shot ticket** — a direct join to a different server without `transfer:<id> == target` is rejected even if the lease has expired.
3. **Snapshot overwrite** — the snapshot is overwritten on every disconnect. A crash can lose the last few minutes, but cannot resurrect an older inventory.
4. **Local file wins when newer** — `LocalFileWinsWhenNewer` prevents rolling back a player who played while Redis was down.

---

## Typical workflows

- **Two-server fleet setup:** start Redis and confirm `PING`; copy `BuggeeCore.cs` to both servers; set `ServerId`, `DisplayName`, `Role`, `PublicHost`, and `GamePort` per server; point both configs at the same Redis; start both and confirm `/buggee` shows `Redis: connected`; `/netpeers` should show `mods=match`.
- **Redirect a player:** the player runs `/horde` or `/hub`; the plugin selects the peer, saves a snapshot, sets the ticket, and kicks with the destination address; the player reconnects; the destination claims the lease, loads the snapshot, and spawns them.
- **Server Assist board:** open **Buggee Network**, check roles, population, and mod-set status, open **All Servers** for full addresses, then select a peer and confirm. Admins can open **Diagnostics**.
- **Crash recovery:** wait for `LeaseSeconds`, or run `/netrelease <playerId>` on another server. It deletes both the active lease and the transfer ticket.
- **Rollback guard:** after playing offline, the newer local `.ttp` wins over an older Redis snapshot when `LocalFileWinsWhenNewer` is true.

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| `Redis: offline` or `/netdiag` error | Check host, port, password, database, and reachability; confirm Redis `PING` succeeds; check the server log |
| `/netpeers` shows `DIFFERENT` | Mod sets differ; install the same mods in the same load order on every backend |
| Transfer refused for version mismatch | Snapshots are build-specific; update all backends to the same game build |
| Rejected with `already connected to <server>` | A live lease exists; wait for `LeaseSeconds` or run `/netrelease <playerId>` |
| Player joins the wrong server directly | A direct join without a transfer ticket is rejected; use `/hub`, `/horde`, or `/goto` |
| Snapshot is corrupt | `DeserializePlayerData` returns null, a warning is logged, and the local save is used |
| Players arrive at invalid coordinates | Use `SpawnMode` `Fixed` with safe coordinates, or `WorldSpawn`; `Keep` only works on the same map and seed |
| Vehicles do not travel | Intentional; `ClearOwnedEntities` removes them because they are world-bound entity IDs |
| Land claims or bedrolls do not travel | They are per-server; decide which server is "home" |
| Server Assist panel missing | Confirm Server Assist is loaded and the player has `buggeecore.use`; check the log for `registered ServerAssist panel 'buggee'`; reload both plugins |
| Travel permission denied | Grant `buggeecore.use` for status and travel, or `buggeecore.admin` for admin commands |

---

## Known limitations

- **Manual reconnect.** Without a client mod the only redirect is a kick message; players must paste or type the address.
- **Vehicles and drones do not travel.** They stay with the source world.
- **Land claims, bedrolls, and sleeping bags are per-server.**
- **Quests that point at POIs** may be uncompletable on a map that lacks that POI.
- **Snapshots are build-specific.** The plugin refuses cross-version transfers.
- **Redis is a soft dependency.** If it is down, players keep using local saves; fleet features go offline. There is no queue-and-retry yet.
- **No master process.** Routing lives in the plugin and config. Parties, queues, and least-latency placement need a separate service.

---

## Related pages

- [Server Assist API](../../integration/api/index.md)
- [Companion plugin integration](../../integration/companions/index.md)
- [Server Assist product guide](../../core/server-assist.md)
- [Icons](../../core/icons.md)
- [Troubleshooting](../../reference/troubleshooting/index.md)
