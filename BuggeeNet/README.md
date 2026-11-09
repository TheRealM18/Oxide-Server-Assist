# BuggeeNet — Milestone B

Fleet + redirect for 7 Days to Die (Oxide), **no client mod**. The client always connects
directly to a backend; a shared Redis instance carries server discovery, session leases and
player snapshots. The "proxy" is a control plane, not a data plane.

```
                     ┌──────────────────────┐
                     │  Redis (shared)      │
                     │  registry / leases   │
                     │  player snapshots    │
                     └───────┬──────────────┘
                             │
        ┌────────────────────┼────────────────────┐
        ▼                    ▼                    ▼
  [home:26900]         [horde:26910]        [arena:26920]
   BuggeeCore           BuggeeCore           BuggeeCore
        ▲                    ▲                    ▲
        └────────── players connect directly ────┘
                 (redirect = kick + address)
```

## What this milestone actually does

| Capability | Status |
|---|---|
| Shared server registry + heartbeats | Working |
| Online/offline peer discovery | Working |
| `/hub`, `/horde`, `/goto` redirect | Working (kick with reconnect address) |
| Prevent dual-login / ghost sessions | Working (Redis lease, compare-and-delete) |
| Shared inventory, belt, backpack, equipment | Working (game's own `PlayerDataFile` blob) |
| Shared progression, perks, buffs, recipes | Working |
| Cross-server economy / vehicles / land claims | **Not yet** |
| Seamless handoff without reconnect | **Not possible without a client mod** |

The handoff is: save snapshot → set transfer ticket → kick with `host:port` → player rejoins →
destination writes the snapshot into its player-data dir *before* the vanilla spawn load.
No Harmony patch, no client DLL.

## Layout

```
BuggeeNet/
  oxide/plugins/BuggeeCore.cs          the plugin (single file, net48)
  docker/docker-compose.yml            dev Redis
  docker/redis.conf                    dev Redis config
  tests/PluginCompile/                 compile-check harness against the game DLLs
  docs/ARCHITECTURE.md                 design + failure modes
  _recon/                              local decompiler output for reference only — do not ship
```

## 1. Dev Redis (this machine)

Docker Desktop must be running.

```powershell
docker compose -f "BuggeeNet\docker\docker-compose.yml" up -d
docker exec buggee-redis redis-cli -a buggee_dev_only_change_me --no-auth-warning PING
```

Expected: `PONG`. The container is already running and healthy on `127.0.0.1:6379`.

Important settings already applied in `redis.conf`:

- `maxmemory-policy noeviction` — Redis must **never** silently evict a player snapshot.
- `appendonly yes` + RDB — this is player data, not a cache.
- Bound to `127.0.0.1` in dev.

## 2. Prod Redis (rented Linux servers)

Do **not** expose Redis to the public internet. Choose one:

1. **Private network (recommended):** WireGuard or Tailscale between the game hosts and the
   Redis host. Bind Redis to the private IP, firewall 6379 to those peers only.
2. **TLS:** `tls-port 6379`, `port 0`, certs from a private CA, and `rediss://` semantics.
   The current client does plain TCP, so TLS would need a small addition.

Minimum prod config changes:

```conf
bind <private-ip>
protected-mode yes
requirepass <long-random-password>     # replace the dev password everywhere
maxmemory-policy noeviction
appendonly yes
appendfsync everysec
```

Also: turn on host firewalling (`ufw`/`nftables`), keep Redis off the public DNS name, and back
up the AOF/RDB volume. Losing Redis means losing snapshots, not world saves — but that is still
player progress.

## 3. Install the plugin

Copy `oxide/plugins/BuggeeCore.cs` into each server's `oxide/plugins/` directory. Reload or
restart. First load writes `oxide/config/BuggeeCore.json`.

### Home server

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
  "Debug": true
}
```

### Horde server

Same file, change:

```json
"ServerId": "horde",
"DisplayName": "Buggee Horde",
"Role": "horde",
"PublicHost": "horde.example.com",
"GamePort": 26910
```

**Set `SpawnMode` / `SpawnX/Y/Z` per server.** `Fixed` is the safe default because a snapshot
carries the source world's coordinates, which may be inside rock on the destination map.
Use `Keep` only if both servers share the same map and seed.

### Required server settings

- `EACEnabled=false` (already required for Oxide).
- Identical game build and identical mod set on every backend, or transfers are refused
  (`RequireSameModSet`). Item IDs are load-order integers; a mismatch corrupts inventories.

## 4. Commands

| Command | Permission | Purpose |
|---|---|---|
| `/buggee` | `buggeecore.use` | status |
| `/hub` | `buggeecore.use` | redirect to the home server |
| `/horde` | `buggeecore.use` | redirect to the horde server |
| `/goto <serverId>` | `buggeecore.admin` | redirect self (or a named player from console) |
| `/netpeers` | `buggeecore.admin` | registry + mod-set comparison |
| `/netdiag` | `buggeecore.admin` | Redis ping, prefix, mod hash |
| `/netrelease <playerId>` | `buggeecore.admin` | clear a stuck session lease after a crash |

## 4b. Server Assist panel

BuggeeCore registers a native Server Assist panel:

| | |
|---|---|
| Panel id | `buggee` |
| Label | Buggee Network |
| Permission | `buggeecore.use` |
| Icon | `sprite:sa_stream_link` |
| Handler | `HandleGuiRequest(playerId, payload)` |

Screens:

- **Overview** — this server (`YOU` badge) plus every healthy peer, with role, player count
  and mod-set status. Click a peer to continue.
- **Confirm** — target address, role, population and mod-set check. Travel is disabled when
  the mod sets differ.
- **All Servers** — the same list with full `host:port`.
- **Diagnostics** (`buggeecore.admin`) — Redis ping, endpoint, key prefix, registry size,
  game version and mod hash.

Registration is defensive:

- retries at 2s and 5s after load, and again on `OnServerAssistReady()`;
- falls back to `RegisterGuiHook` on older ServerAssist builds;
- unregisters on plugin unload.

The panel is additive: the `/hub`, `/horde`, `/goto` commands still work if ServerAssist is
not installed.

## 5. Test plan

1. Start one server. `/buggee` → `Redis: connected`. `/netdiag` → `PONG`.
2. Start the second with a different `ServerId`/`Role`.
3. On either: `/netpeers` → two entries, `mods=match`.
4. On home: put items in belt and backpack, `/horde`.
5. Expect a kick message containing the horde `host:port`. Join it manually.
6. Inventory and progression should be intact. Check `/netdiag` on the new server.
7. **Server Assist:** open Server Assist → Buggee Network. Check the overview rows, open
   **All Servers**, then confirm a transfer. Run **Diagnostics** as an admin.
8. **Dupe/dual-login:** while on home, try to connect directly to horde without a ticket →
   rejected with "already connected to Buggee Home".
9. **Crash recovery:** kill the home server, then `/netrelease <steamId>` on horde, or wait
   `LeaseSeconds`.
10. **Rollback guard:** play offline on one server, then rejoin; the newer local `.ttp` should
    win over an older Redis snapshot.

## 6. Known limitations

- **Manual reconnect.** Without a client mod the only redirect is a kick message. Players must
  paste/type the address. This is the accepted cost of milestone B.
- **Vehicles and drones do not travel** (`ClearOwnedEntities`). They stay with the source world.
- **Land claims, bedrolls and sleeping bags are per-server.** Decide which server is "home".
- **Quests that point at POIs** may be uncompletable on a map that lacks that POI.
- **Snapshots are build-specific.** The `.ttp` binary is tied to the game version; the plugin
  refuses cross-version transfers.
- **Redis is a soft dependency.** If it is down, players keep using local saves; fleet features
  go offline. There is no queue-and-retry yet.
- **No master process.** Routing lives in the plugin and config. That is deliberate for B;
  extract it into a service when you need parties, queues or least-latency placement.
