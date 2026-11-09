# BuggeeNet — Architecture (Milestone B)

## Why not a real proxy

A BungeeCord-style proxy needs: a documented wire protocol, a way to re-enter play state on the
same socket, and a client that tolerates a backend swap mid-session. 7DTD has none of these.
The session is bound to one loaded world (`GameManager.World`, entity IDs, chunk state) and the
transport is an encrypted EOS/Steam connection. A UDP relay can forward one backend, but changing
the mapping mid-flow breaks sequence numbers and keys.

So milestone B is **control plane + redirect**:

- the client always connects directly to a backend;
- the master/registry decides *where*;
- a shared Redis holds discovery, leases and player snapshots.

This also removes the proxy as a bandwidth/latency bottleneck and DDoS target.

## Injection point: `OnUserApprove`

The server's spawn path is `GameManager.RequestToSpawnPlayer`:

```csharp
PlayerDataFile playerDataFile = new PlayerDataFile();
playerDataFile.Load(GameIO.GetPlayerDataDir(), persistentPlayerId.CombinedString);
...
EntityPlayer ep = EntityFactory.CreateEntity(entityCreationData);
Interface.CallHook("OnPlayerConnected", _cInfo);     // too late: local file already loaded
ep.Respawn(...);
playerDataFile.ToPlayer(ep, StreamModeRead.Persistency);
```

`OnPlayerConnected` fires *after* the disk load and after the entity is created, so mutating
`ClientInfo.latestPlayerData` there does not affect spawn.

`OnUserApprove(ClientInfo)` fires before the spawn request. Writing the snapshot to
`<playerDataDir>/<InternalId.CombinedString>.ttp` there means the vanilla loader picks it up
with no Harmony patch and no client change.

**Filename gotcha:** the game uses `InternalId.CombinedString` (`Steam_7656...`), not Oxide's
`IPlayer.Id` / `ReadablePlatformUserIdentifier` (`7656...`). Redis keys use the readable ID;
disk files use the combined ID.

## Snapshot format

The transfer medium is the game's own save container:

```
"ttp\0" | version byte (62) | PlayerDataFile.Write(..., Persistency)
```

`PlayerDataFile` already covers belt, backpack, equipment, progression, buffs, quest journal,
waypoints and stats. `FromPlayer()` captures; `ToPlayer()` applies. We never hand-serialize
`ItemValue` fields, so modded items, ammo, durability and mod slots survive automatically.

Envelope stored in Redis (binary-safe, one key = atomic):

```
"BGG1" | u8 format | i64 savedAt | str sourceServer | str gameVersion | str modHash | i32 len | payload
```

## Redis keys

Default prefix `bn:`.

| Key | Type | TTL | Purpose |
|---|---|---|---|
| `bn:servers` | SET | none | all server IDs ever registered |
| `bn:srv:<id>` | HASH | heartbeat TTL | id, name, role, host, port, players, version, mods |
| `bn:active:<steamId>` | STRING | lease | which server currently owns this player |
| `bn:transfer:<steamId>` | STRING | 180s | one-shot ticket: destination server id |
| `bn:snap:<steamId>` | STRING (binary) | none | latest player snapshot envelope |
| `bn:snapmeta:<steamId>` | HASH | none | savedAt, server, reason, bytes |
| `bn:presence:<steamId>` | HASH | presence TTL | name, server |

## Transfer sequence

```
Player            Server A (home)                 Redis                Server B (horde)
  | /horde              |                            |                        |
  |-------------------->|                            |                        |
  |                     | pick peer (role=horde,     |                        |
  |                     |   lowest players)          |                        |
  |                     | mod/version gate --------->|                        |
  |                     | FromPlayer + envelope ---->| SET snap:<id>          |
  |                     | ticket ------------------->| SET transfer:<id> = B  |
  |<-- kick "B:26910" --|                            |                        |
  |                     | (disconnect) release lease |                        |
  |                     |                            |                        |
  | connect B directly ------------------------------------------------>       |
  |                     |                            | GET transfer:<id> == B?|
  |                     |                            | GET active:<id>       |
  |                     |                            |<-- claim lease --------|
  |                     |                            | GET snap:<id> -------->|
  |                     |                            |<-- .ttp -> playerDataDir
  |                     |                            |    vanilla Load()       |
```

## Dupe protection

1. **Session lease.** `bn:active:<id>` is set on approve, refreshed every heartbeat only by the
   owner (Lua compare-and-expire), and released on disconnect with compare-and-delete. A second
   server sees a live lease and rejects the login.
2. **One-shot ticket.** A direct join to a different server without `transfer:<id> == target`
   is rejected even if the lease has expired.
3. **Snapshot is overwritten on every disconnect.** A crash can lose the last few minutes, but
   cannot resurrect an older inventory.
4. **Local file wins when newer** (`LocalFileWinsWhenNewer`). Prevents rolling back a player who
   played while Redis was down.

## Failure modes

| Failure | Behavior |
|---|---|
| Redis unreachable at boot | Plugin logs an error, fleet features off, local `.ttp` still works |
| Redis dies mid-session | Commands fall back to local; transfers refuse; reconnect attempt on next heartbeat |
| Server crashes | Lease expires after `LeaseSeconds`; `/netrelease` clears it immediately |
| Snapshot corrupt | `DeserializePlayerData` returns null, warning logged, local save used |
| Game version mismatch | Transfer refused at both ends |
| Mod set mismatch | Transfer refused; `/netpeers` shows `DIFFERENT` |
| Player joins wrong server directly | Rejected with "already connected to \<server\>" |

## Deliberate non-goals for B

- Parties/queues and least-latency placement (needs a master service).
- Cross-server chat.
- Vehicle/drone transfer (world-bound entity IDs).
- Land claim / bedroll transfer.
- Automatic reconnection (requires a client-side DLL).

## Next steps after B is proven

1. **Master service** — move routing out of the plugin into a small HTTP service backed by Redis.
   Gives parties, queues, health scoring, admin dashboard.
2. **Vehicle transfer** — serialize `OwnedEntityData` with a stable per-player vehicle key and
   re-spawn on arrival; still needs per-world parking logic.
3. **Cross-server economy** — single ledger key, transactional with the snapshot write.
4. **Client mod** ("BuggeeConnect") — only if the kick-message UX is the thing that kills adoption.
   It would turn the same ticket into an automatic reconnect, no protocol work required.
