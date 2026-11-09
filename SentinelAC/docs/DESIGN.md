# SentinelAC design

SentinelAC is a server-side, evidence-based anti-cheat for 7 Days to Die. It is
an Oxide plugin: it runs inside the dedicated server process and uses the hook
surface that Oxide's 7 Days to Die extension already patches into the game.

The goal is not a single "cheat signature". The goal is a calibrated risk
estimate that a human can review, with enough context to distinguish a cheater
from a lucky or well-geared player.

## Threat model

SentinelAC assumes:

- the client is untrusted;
- the server is authoritative for the world, entities, inventory and damage;
- the server can observe only what the game sends it;
- a cheater may be careful, intermittent, and may deliberately stay below a
  single obvious threshold.

It does not assume:

- kernel access;
- client memory access;
- a specific cheat menu or DLL signature;
- that any single event is proof.

## Data flow

```text
game hook / timer sample
        |
        v
  module detector  (movement, combat, inventory, grief, exploit, progression)
        |
        v
  evidence record  (module, code, score, confidence, detail, time)
        |
        v
  risk engine      (module weight, suppression, exponential decay)
        |
        v
  alert / watch / action / export
```

Every detector is deliberately small. A detector should answer one question
("is this speed outside this player's baseline?"), not "is this player a
cheater?".

## Statistical primitives

### RunningStat

Welford's online algorithm is used for mean/variance:

```text
count += 1
delta  = x - mean
mean  += delta / count
m2    += delta * (x - mean)
variance = m2 / (count - 1)
z = (x - mean) / sqrt(variance)
```

An exponentially weighted mean and variance are also maintained so a recent
change in behaviour is visible before the lifetime variance catches up.

### RateWindow

A time-bounded queue. It supports:

- `Sum(now)` — total value in the window (XP, block changes);
- `Count(now)` — event count in the window (hits, kills).

Windows are used for XP per minute, kills per minute, hit rate, block-change
rate and repeat-kill detection.

### Baseline lifecycle

- A player's baseline starts empty on first join.
- Statistical evidence is suppressed until `MinPlayMinutes` and `MinSamples`.
- Baselines are persisted to `oxide/data/SentinelAC/profiles.json`.
- A returning player resumes their baseline; a new player starts clean.
- Global rate baselines are kept in the same file.
- XP rate, kill rate and wealth rate are checked against both the player's own
  baseline and the server-wide distribution. This catches a player who is an
  outlier for the population even when their own history has adapted.

## Risk model

Each evidence record contributes:

```text
contribution = score * confidence * moduleWeight * suppression * sensitivity

suppression = 1 / (1 + currentModuleRisk / 60)
contribution = min(cap, contribution)
cap = 70 for critical evidence (score >= 95), otherwise 40
risk = min(100, risk + contribution * (1 - risk / 110))
```

Properties:

- A high-confidence hard violation moves the score quickly.
- Repeated weak evidence in the same module is progressively suppressed.
- Multiple independent modules add up, which is the Matrix-like behaviour:
  speed + reach + headshots + item gain is much more suspicious than any one of
  them alone.
- Risk decays exponentially. A player who stops cheating drifts down over time.
- Evidence is retained for review but does not keep contributing forever.
- `/ac status` shows a logistic `p~` value for readability:
  `p = 1 / (1 + exp(-(risk - 60) / 12))`. This is a presentation of the risk
  score, not a calibrated statistical probability.

## Module summaries

### Movement

Samples position and health once per `SampleSeconds`. Skips vehicles, jetpacks,
climbing, spectators and trader areas. Computes horizontal and vertical speed
from position deltas (not client-reported velocity), then compares:

- hard ceiling;
- per-player z-score and EWMA z-score;
- upward speed;
- time airborne with near-zero vertical speed;
- feet and head inside collidable blocks for several samples (no-clip);
- position below the configured world floor (under-map);
- repeated long-distance jumps.

Fall-damage bypass is optional and off by default because perks, armor, mods and
powered landings can legitimately reduce fall damage.

### Combat

Observes `OnEntityTakeDamage` and `OnEntityDeath`. It records:

- melee reach;
- voxel line-of-sight for close hits (through-wall);
- ranged hits while a magazine weapon reports an empty magazine;
- hit rate in a one-second window;
- very fast target switching between different victims;
- headshot ratio over a rolling window;
- player-vs-player kill rate per minute against the player's own baseline and
  the server-wide distribution (zombie kill rate is excluded because horde
  nights would dominate it).

The line-of-sight check samples the voxel world between the attacker's chest and
the victim's chest. It ignores air, plants, ground cover and glass-like blocks.
It is intentionally conservative: a false wallbang accusation is worse than a
missed one.

### Inventory and economy

Scans the toolbelt, backpack and equipment grids. It detects:

- stack count above `ItemClass.MaxCount`;
- quality above the legal maximum (`max(configured, ItemClass.MaxQualityTier)`);
- unknown item ids;
- high-quality item gain rate;
- wealth z-score;
- duplicate signatures.

Duplicate signatures use `(type, quality, meta, seed)`. They are a low-weight
signal because legitimate items can share a signature; it becomes interesting
only when combined with other evidence.

### Grief

A small cube around each player is snapshotted every `SampleSeconds`. Only
structural changes are counted: air to solid, solid to air, or a type change.
Damage-only changes are ignored, so normal mining does not look like grief.

Attribution rules:

- the block must be within `AttributionDistance`;
- line of sight is required by default;
- if another player is closer to the block, the change is not attributed to this
  player;
- claim ownership comes from `World.GetLandClaimOwner`;
- land-claim keystones are only flagged when another player's claim is affected.

This is a bounded-cost compromise. Scanning the entire world would be accurate
but unacceptable on a busy server.

### Exploit

Checks server-visible state that should not occur on a non-exempt player:

- god mode;
- health/stamina/food/water above the modified maximum;
- jetpack active without a worn jetpack;
- vehicle speed above the ceiling;
- many damage events with no health loss (possible damage immunity).

### Progression

XP and level progression are sampled every ten seconds. The detector compares
the player's XP per minute against their own baseline and a hard ceiling, and
flags multi-level jumps.

## Alerts and actions

Alerts are rate-limited per player. They go to:

1. the Oxide log (`[SentinelAC] ...`);
2. every online player with `sentinelac.admin` or `sentinelac.alerts`;
3. the optional webhook.

Actions are separate from alerts:

- `DryRun = true` disables automatic actions;
- `AutoKick` and `AutoBan` are independent;
- an action requires the risk to stay above the threshold for
  `ActionSustainSeconds`;
- actions have a per-player cooldown;
- exempt players are never actioned.

## Server Assist menu

SentinelAC registers as a Server Assist provider, not just a single card. The
provider owns three tabs:

- **Risk** — a paged, searchable list of online players sorted by risk. Each
  card carries a risk badge, a state icon and a tint.
- **Evidence** — the selected player's summary and latest evidence records,
  each with a module icon, score badge and confidence tint.
- **Modules** — the selected player's per-module risk breakdown.

The handler keeps a small per-admin session (`tab`, page, search, selected
player). Actions are handled directly by the provider: select, page, search,
watch, clear, export, kick and ban. The **Plugins** button sends `home`, which
Server Assist consumes to return to the plugin board; the separate **Close**
button sends `close` and closes the whole Server Assist window. Older
ServerAssist builds without provider home support fall back to a normal panel
registration with the same handler.

No ServerAssist client change is required. The menu uses the existing view
protocol 2 features: provider tabs, text input, row icon/badge/tint, button
icon/tint, banner and background. The dedicated `sa_ac_*` art request lives in
`ServerAssistArt/REQUESTS_SentinelAC.md`; until it is rendered, the menu uses
finished `sa_nav_*` and `sa_act_*` sprites.

## Persistence

`oxide/data/SentinelAC/profiles.json` stores:

- player name and last-seen time;
- peak risk and flag count;
- per-player baselines (`speed`, `vertical`, `killrate`, `xprate`, `wealth`);
- global rate baselines.

Runtime-only data (position history, block snapshots, inventory maps, evidence
lists) is intentionally not persisted.

## Performance

Defaults are chosen for a typical 8-32 player server:

- one-second tick;
- movement every one second per player;
- inventory every five seconds per player;
- exploit every two seconds per player;
- grief cube every two seconds per player (11 x 9 x 11 blocks by default);
- progression every ten seconds per player.

The heaviest operation is the grief cube. Raise `Grief.SampleSeconds`, reduce
`Grief.Radius`/`VerticalRadius`, or disable the grief module on very large
servers. The plugin logs slow hook calls through Oxide's normal hook timing.

## False positives

The most likely sources are:

- mods that raise quality tiers, stack sizes, movement speed or vehicle speed;
- staff using god mode or teleport commands;
- event accounts and testers;
- heavily modded combat balance;
- players who transfer items quickly through containers.

Mitigations:

- `sentinelac.exempt` and `ExemptAdmins`;
- `MinPlayMinutes` and `MinSamples`;
- confidence weighting;
- severity caps per code;
- `DryRun` rollout;
- `/ac clear` after a reviewed false positive;
- per-module disable flags.

## Roadmap

Possible next steps:

- an optional Harmony bridge mod for exact block-break/place and shot events;
- per-weapon damage distributions learned from the server;
- Discord thread/report workflow;
- a web dashboard over the evidence export;
- container and lockable access tracking with actor identity;
- cross-wipe reputation and shared ban-list import.
