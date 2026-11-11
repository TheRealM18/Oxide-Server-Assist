# Sentinel AC

> **Version:** 1.0.2 · **Type:** Server-side statistical anti-cheat, anti-exploit and anti-grief Oxide plugin (optional Server Assist provider; no client mod) · **Source:** [SentinelAC.cs](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/SentinelAC/oxide/plugins/SentinelAC.cs)

Sentinel AC builds a statistical risk profile for every player instead of matching one cheat signature. It watches movement, combat, inventory/economy, griefing, exploits and progression, then combines weak signals into a 0–100 score with a reviewable evidence trail. It is an Oxide plugin: no kernel driver, no client injection, no Harmony and no companion client mod. Server Assist is optional and only adds the graphical review menu.

!!! note "Server-side only"
    Sentinel AC observes the hooks and state the dedicated server already has. It cannot inspect client memory, input history, aim angles or rendered frames.

---

## Overview

Each player gets a personal baseline for speed, vertical movement, kill rate, XP rate and wealth. XP, kill-rate and wealth are also compared with the whole server's distribution, which catches a player whose own history has adapted to cheating. Every finding becomes an **evidence record** with a module, code, raw score, confidence and detail.

Contributions are module-weighted and suppressed when one module already dominates, so several independent signals are more suspicious than one noisy check. Risk decays exponentially. Staff can review, watch, clear, export, kick or ban from commands or the optional Server Assist menu.

The design is deliberately "Matrix-like": many weak signals, per-player and server-wide statistics, confidence weighting, cooldowns and a full audit trail instead of one brittle check. It is an observer and decision-support tool, not proof that a player cheated, and not a replacement for server configuration, permissions or human review.

---

## Requirements

| Requirement | Detail |
|---|---|
| Server | 7 Days to Die dedicated server running Oxide |
| EAC | **Off**; Oxide requires Easy Anti-Cheat to be disabled |
| Plugin file | `oxide/plugins/SentinelAC.cs` |
| Client mod for Sentinel AC | None |
| Harmony | Not required; only the normal Oxide hook surface is used |
| Server Assist (optional) | Server Assist 2.1.0+ and the Server Assist mod on the server and every client that should see the GUI |
| Disk access | Write access to `oxide/config` and `oxide/data` |

Sentinel AC runs without Server Assist. Commands, alerts, evidence and exports still work; only `/ac gui` and the GUI card are unavailable.

---

## Install

1. Stop the server.
2. Copy `SentinelAC.cs` into the server's `oxide/plugins` folder.
3. Start the server. Oxide compiles the plugin.
4. Join as an administrator and run `/ac status`.
5. Leave `DryRun = true` while you review alerts and tune thresholds.
6. Set `DryRun = false` only when you are ready to allow automatic actions.

!!! warning "Use oxide/plugins, not Mods"
    Do not put the `.cs` file in the game's `Mods` folder. The mod loader does not compile C# plugins; Oxide must compile them from `oxide/plugins`.

On first load the plugin creates `oxide/config/SentinelAC.json` and `oxide/data/SentinelAC/profiles.json`. The generated config contains every field. A missing or unreadable config is replaced with defaults, validated and written back.

Oxide's default administrator group receives `sentinelac.admin` and `sentinelac.alerts` automatically on first start. To grant them explicitly:

```text
oxide.grant group admin sentinelac.admin
oxide.grant group admin sentinelac.alerts
oxide.grant user <steamid> sentinelac.exempt
```

The last command exempts testers, event accounts and service accounts. Use `/ac clear` for a reviewed false positive and `/ac export` to preserve a report before clearing or restarting.

---

## Permissions

| Permission | Purpose |
|---|---|
| `sentinelac.admin` | Use `/ac`, open the Server Assist provider and its views, and use staff actions |
| `sentinelac.alerts` | Receive in-game risk alerts |
| `sentinelac.exempt` | Never accumulate risk or trigger an automatic action |
| `sentinelac.use` | Registered but reserved for future player-facing features |

- `sentinelac.admin` is checked by every command and by the Server Assist handler. Oxide administrators (`player.IsAdmin`) also pass.
- `sentinelac.alerts` only adds the player to alert broadcasts; it does not grant command access.
- `sentinelac.exempt` is honoured in addition to the configurable `ExemptPermission` node.
- `sentinelac.use` has no effect in 1.0.2.
- `ExemptAdmins` exempts entries in the server's admin tools list, which is separate from Oxide group membership.

---

## Commands

All commands require `sentinelac.admin` or Oxide administrator access.

| Command | Behaviour |
|---|---|
| `/ac status [player]` | Risk, session risk, peak risk, flags, playtime, samples, kills, watch/exempt state and latest evidence |
| `/ac top` | Up to ten highest-risk online players |
| `/ac flags <player>` | Recent evidence, newest first, with module, score, confidence and detail |
| `/ac modules <player>` | Active risk per detection module, highest first |
| `/ac watch <player>` | Toggle the staff watch flag |
| `/ac clear <player>` | Clear risk, module risk, evidence and flags |
| `/ac kick <player> [reason]` | Kick an online player |
| `/ac ban <player> [hours] [reason]` | Ban a player; `hours` defaults to `DefaultBanHours` (24) |
| `/ac export <player>` | Write an evidence report to `oxide/data/SentinelAC/` |
| `/ac gui` | Open the Server Assist menu |
| `/ac reload` | Reload config, re-register the exemption permission, restart timers and re-register the GUI |

Aliases: `/sentinel`, `/sentinelac`. Subcommand aliases: `/ac info` for `status`, `/ac evidence` for `flags`, `/ac panel` for `gui`.

```text
/ac status Notch
/ac flags Notch
/ac modules Notch
/ac watch Notch
/ac export Notch
/ac kick Notch suspected reach
/ac ban Notch 48 repeated exploitation
```

`/ac status` shows a logistic `p~` value beside the score. It is a readable presentation, not a calibrated probability: risk 60 maps to about 50%, risk 90 to about 92%.

---

## Detection modules

| Module | Signals | Representative codes |
|---|---|---|
| **Movement** | Horizontal speed versus personal baseline and a hard ceiling; sustained upward movement or hover; airborne time; repeated long jumps; feet/head inside collidable blocks; position below the world floor; optional fall-damage bypass | `MOV_SPEED_STAT`, `MOV_SPEED_HARD`, `MOV_UPWARD`, `MOV_HOVER`, `MOV_TELEPORT_CHAIN`, `MOV_NOCLIP`, `MOV_UNDERMAP`, `MOV_FALL_BYPASS` |
| **Combat** | Melee reach; voxel line-of-sight; empty-magazine ranged hits; hit rate; fast target switching; headshot ratio; player-vs-player kill rate versus personal and global baselines | `CMB_REACH`, `CMB_WALLBANG`, `EXP_EMPTY_MAG`, `CMB_HIT_RATE`, `CMB_TARGET_SWITCH`, `CMB_HEADSHOT_RATIO`, `CMB_KILL_RATE`, `CMB_KILL_GLOBAL` |
| **Inventory and economy** | Stacks above legal maximum; quality above legal maximum; unknown item ids; high-quality item gain rate; wealth z-score; duplicate item signatures | `EXP_STACK_OVERFLOW`, `EXP_QUALITY_OVERFLOW`, `EXP_INVALID_ITEM`, `ECO_HIGH_VALUE_GAIN`, `ECO_WEALTH_STAT`, `ECO_WEALTH_GLOBAL`, `ECO_DUPLICATE_SIGNATURE` |
| **Grief** | Structural block changes in a sampled cube; changes inside another player's claim; claim keystone removal; trader-area changes; block-change rate, bursts and explosion-like bursts; repeat kills of one victim; multiple vehicles spawning near one player | `GRF_BLOCK_RATE`, `GRF_CLUSTER`, `GRF_EXPLOSION`, `GRF_CLAIM`, `GRF_LANDCLAIM`, `GRF_TRADER`, `GRF_SPAWN_CAMP`, `GRF_VEHICLE_SPAWN` |
| **Exploit** | God mode on a non-exempt player; health/stamina/food/water above the modified maximum; jetpack without a worn jetpack; vehicle speed; many damage events without health loss | `EXP_GODMODE`, `EXP_STAT_OVERFLOW`, `EXP_JETPACK`, `EXP_VEHICLE_SPEED`, `EXP_DAMAGE_IMMUNITY` |
| **Progression** | XP per minute versus personal baseline and hard ceiling; level jumps larger than one level per sample | `PRG_XP_STAT`, `PRG_XP_HARD`, `PRG_XP_GLOBAL`, `PRG_LEVEL_JUMP` |

Movement uses server position deltas, not client velocity. Vehicles, jetpacks, climbing, spectators, trader areas and spawn grace are skipped. No-clip requires several consecutive samples.

Combat's line-of-sight check samples the voxel world between attacker and victim and ignores air, plants, ground cover, glass, doors, ladders and similar pass-through blocks. Zombie kill rate is excluded from the player-kill baseline because horde nights would dominate it.

Inventory uses `ItemClass.MaxCount` for stacks and `max(configured, ItemClass.MaxQualityTier)` for quality where available. Duplicate signatures are low-weight because legitimate items can share a signature.

Grief counts only structural changes (air↔solid or type change), so damage-only mining is ignored. Attribution requires proximity and, by default, line of sight; if another player is closer, the change is not attributed. Keystones are flagged only when another player's claim is affected.

Exploit damage immunity is inferred from many damage events without matching health loss. Progression is sampled every ten seconds and has the lowest module weight.

---

## Risk and evidence model

An evidence record contains `Module`, `Code`, `Score` (0–100), `Confidence` (0–1), `Detail`, `TimeUtc` and `RiskAfter`.

```text
contribution = score * confidence * moduleWeight * suppression * Sensitivity
suppression  = 1 / (1 + currentModuleRisk / 60)
contribution = min(cap, contribution)
cap          = MaxContributionCritical (70) when score >= CriticalScoreThreshold (95),
               otherwise MaxContributionNormal (40)
risk         = min(100, risk + contribution * (1 - risk / 110))
```

| Module | Weight |
|---|---|
| Grief | 1.5 |
| Exploit | 1.5 |
| Inventory | 1.3 |
| Combat | 1.2 |
| Movement | 1.0 |
| Progression | 0.8 |

- A single event cannot reach 100 because of the per-contribution cap.
- Repeated evidence in the same module is progressively suppressed.
- Independent modules add up: speed plus reach plus headshots plus item gain is much more suspicious than any one signal.
- Risk decays exponentially with `RiskDecayHalfLifeMinutes` (30 minutes by default).
- Evidence is retained for `EvidenceRetentionMinutes` (120 minutes) and capped at 200 records per player in memory.
- `p = 1 / (1 + exp(-(risk - 60) / 12))` produces the `p~` readout. It is presentation, not a calibrated probability.

---

## Configuration

The config is generated as `oxide/config/SentinelAC.json`. Values are clamped to safe ranges on load. `/ac reload` re-reads the file, re-registers the exemption permission, restarts the timers and re-registers the menu. The defaults below match the example config and the source.

### Core and risk engine

| Key(s) | Default | Purpose |
|---|---|---|
| `ChatPrefix` | `[Sentinel]` | Prefix for replies |
| `Debug` / `DryRun` | `false` / `true` | Extra logging; dry-run keeps alerts but disables automatic actions |
| `ExemptAdmins` / `ExemptPermission` | `true` / `sentinelac.exempt` | Exempt entries in the server admin tools list; additional exemption permission |
| `ScanIntervalSeconds` / `MinSamples` / `MinPlayMinutes` | `1.0` / `15` / `5.0` | Main tick (0.5–10 s); sample gate; statistical-evidence playtime gate |
| `Sensitivity` | `1.0` | Global contribution multiplier (0.1–5.0) |
| `MaxContributionNormal` / `MaxContributionCritical` / `CriticalScoreThreshold` | `40.0` / `70.0` / `95.0` | Per-event caps and critical raw-score threshold |
| `RiskDecayHalfLifeMinutes` | `30.0` | Exponential risk half-life |
| `AlertThreshold` / `WatchThreshold` / `KickThreshold` / `BanThreshold` | `40.0` / `60.0` / `90.0` / `97.0` | Alert, auto-watch, auto-kick and auto-ban thresholds |
| `AutoWatch` / `AutoKick` / `AutoBan` | `true` / `false` / `false` | Automatic watch, kick and ban |
| `ActionSustainSeconds` / `ActionCooldownMinutes` | `30.0` / `15.0` | Risk must stay high this long; per-player action cooldown |
| `AlertCooldownSeconds` / `EvidenceCooldownSeconds` / `EvidenceRetentionMinutes` | `15.0` / `10.0` / `120.0` | Alert rate limit; duplicate-code window; evidence retention |
| `DefaultBanHours` | `24.0` | Ban duration for `/ac ban` and GUI bans |
| `AllowGuiKick` / `AllowGuiBan` | `false` / `false` | Show and allow GUI kick and ban |

!!! note "Hard violations and playtime"
    Evidence with a raw score of 90 or more can be recorded immediately. Statistical evidence below that is suppressed until `MinPlayMinutes` has elapsed, so a new player is not punished for an empty baseline. Several checks also wait for `MinSamples`.

### Movement

| Key(s) | Default | Purpose |
|---|---|---|
| `Enabled` / `SampleSeconds` | `true` / `1.0` | Enable module; position sample interval |
| `ZThreshold` / `HardSpeed` | `5.0` / `18.0` | Personal z-score threshold; absolute speed ceiling |
| `MaxUpwardSpeed` / `MaxAirSeconds` | `8.0` / `5.0` | Vertical speed ceiling; airborne time before hover signal |
| `MaxTeleportDistance` / `MaxTeleportsPerWindow` / `TeleportWindowMinutes` | `60.0` / `6` / `10.0` | Jump distance, allowed count and window |
| `SpawnGraceSeconds` / `MinWorldY` | `10.0` / `-5.0` | Post-spawn grace; world floor for under-map detection |
| `DetectNoClip` / `NoClipSamples` | `true` / `3` | Terrain no-clip check and consecutive samples |
| `DetectFallBypass` / `MinFallDistance` / `MinFallDamageLoss` | `false` / `12.0` / `2.0` | Optional fall-damage bypass check |

### Combat

| Key(s) | Default | Purpose |
|---|---|---|
| `Enabled` | `true` | Enable module |
| `MaxMeleeReach` / `ThroughWallDistance` | `5.0` / `10.0` | Legal melee reach; maximum wall-check distance |
| `MaxHitsPerSecond` / `HeadshotRatioThreshold` / `MinHeadshots` | `15.0` / `0.9` / `30` | Hit-rate ceiling; suspicious headshot ratio; minimum sample |
| `DetectEmptyMagazineShots` | `true` | Flag ranged hits with an empty magazine |
| `KillRateZThreshold` / `GlobalZThreshold` / `MinKillsForRate` | `5.0` / `7.0` / `5` | Personal and global z-scores; minimum kills |

### Inventory and economy

| Key(s) | Default | Purpose |
|---|---|---|
| `Enabled` / `SampleSeconds` | `true` / `5.0` | Enable module; scan interval |
| `MaxLegalQuality` / `MaxHighValueGainPerMinute` | `6` / `6.0` | Quality ceiling; high-quality gain rate |
| `WealthZThreshold` / `GlobalZThreshold` / `MaxIdenticalSignatures` | `5.0` / `7.0` / `3` | Personal and global wealth z-scores; duplicate allowance |
| `DetectInvalidItems` / `DetectStackOverflow` / `DetectQualityOverflow` | `true` / `true` / `true` | Unknown ids, illegal stacks and illegal quality |

### Grief

| Key(s) | Default | Purpose |
|---|---|---|
| `Enabled` / `SampleSeconds` | `true` / `2.0` | Enable module; snapshot interval |
| `Radius` / `VerticalRadius` / `AttributionDistance` | `5` / `4` / `8.0` | Snapshot size and attribution range |
| `MaxChangesPerMinute` / `MaxChangesPerSample` / `ExplosionChangeThreshold` | `90.0` / `20` / `40` | Rate, burst and explosion-like limits |
| `ProtectClaims` / `ProtectLandClaimBlocks` / `FlagTraderArea` / `RequireLineOfSight` | `true` / `true` / `true` / `true` | Claim, keystone, trader and line-of-sight rules |

The default cube is 11 × 9 × 11 blocks. It is the heaviest sampling operation; on a busy server raise `SampleSeconds`, reduce the radii, or disable the module.

### Exploit

| Key(s) | Default | Purpose |
|---|---|---|
| `Enabled` / `SampleSeconds` | `true` / `2.0` | Enable module; sample interval |
| `DetectGodMode` / `DetectStatOverflow` / `DetectJetpack` | `true` / `true` / `true` | God mode, stat overflow and jetpack checks |
| `DetectVehicleSpeed` / `MaxVehicleSpeed` | `true` / `60.0` | Vehicle speed check and ceiling |
| `DetectDamageImmunity` / `DamageImmunityHits` / `StatOverflowFactor` | `true` / `12` / `1.25` | Damage-immunity signal and stat multiplier |

### Progression

| Key(s) | Default | Purpose |
|---|---|---|
| `Enabled` / `MaxXpPerMinute` | `true` / `20000.0` | Enable module; hard XP ceiling |
| `XpZThreshold` / `GlobalZThreshold` / `MinXpPerMinute` | `5.0` / `7.0` / `500.0` | Personal/global z-scores; ignore very low rates |

### Webhook and Server Assist

| Key(s) | Default | Purpose |
|---|---|---|
| `Webhook.Enabled` / `Webhook.Url` | `false` / `""` | Discord-compatible webhook delivery |
| `Webhook.Username` / `Webhook.MinRisk` | `SentinelAC` / `55.0` | Display name; minimum risk to post |
| `Webhook.MinIntervalSeconds` / `Webhook.IncludeEvidence` | `60.0` / `true` | Per-player rate limit; append latest five evidence records |
| `ServerAssist.Enabled` / `ServerAssist.PanelId` | `true` / `sentinelac` | Register the provider; provider and panel id |
| `ServerAssist.Icon` / `ServerAssist.Order` | `sprite:sa_nav_moderation` / `30` | Provider icon; board sort order |

Webhook posts are JSON `POST` requests with `username` and `content`, truncated to 1900 characters with a 10-second timeout. Failures go to the Oxide log.

---

## Data and persistence

| Path | Contents |
|---|---|
| `oxide/config/SentinelAC.json` | Full configuration |
| `oxide/data/SentinelAC/profiles.json` | Player profiles and baselines |
| `oxide/data/SentinelAC/export-<id>.json` | Evidence report from `/ac export` |

`profiles.json` stores name and last-seen time, peak risk, flag count, per-player baselines (`speed`, `vertical`, `killrate`, `xprate`, `wealth`) and global rate baselines. It is saved every five minutes and on unload.

Runtime-only data is not persisted: position history, block snapshots, inventory maps, active risk, module risk and evidence. After a restart, a returning player resumes baselines, peak risk and flags, but the live evidence list starts empty. Export before restarting or clearing if you need to keep it.

Exports include id, name, current/session/peak risk, flags, playtime, kill counts, module risk and current evidence. The file name uses the sanitized id, for example `oxide/data/SentinelAC/export-76561198000000000.json`.

---

## Server Assist integration

Sentinel AC registers as a Server Assist **provider** with three tabs. If the provider API is unavailable, it registers a normal home panel with the same handler and views.

| Item | Value |
|---|---|
| Provider id | `sentinelac` |
| Title | `Sentinel AC` |
| Tabs | `Risk`, `Evidence`, `Modules` |
| Handler | `HandleServerAssistRequest(string playerId, string payload)` |
| Default icon | `sprite:sa_nav_moderation` |
| Protocol | Server Assist view protocol 2 |

### Registration and fallback

1. Sentinel AC checks that Server Assist is loaded and `ServerAssist.Enabled` is true.
2. It calls `GetProviderHomeSupport`. When supported, it calls `RegisterGuiProvider` with the id, plugin name, handler, title and tab labels.
3. On success it calls `SetProviderIcon`, removes any old normal panel and switches to provider mode.
4. If provider registration throws or returns false, it calls `RegisterPanel` with the same id, title, description, `sentinelac.admin`, handler, icon and order.
5. If opening a provider fails because the client has an older Server Assist mod, it unregisters the provider, registers the panel fallback and tries `OpenPanel` again.

Older Server Assist builds therefore still show a usable menu instead of an empty card.

!!! note "No Server Assist client update for 1.0.2"
    The provider, tabs, text input, paging, row icon/badge/tint, button icon/tint, banner and background already exist in the current client. See [update.md](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/SentinelAC/update.md). No new net package, client control or protocol key is required.

### Permissions and visibility

Server Assist lists registered providers on its board to players who can open the board, but the provider must enforce its own access. Sentinel AC returns an error view unless the player is connected and is an Oxide administrator or has `sentinelac.admin`. Opening the board still requires the normal Server Assist permission (`serverassist.use`, usually granted to players).

### Tabs and actions

| Tab | Contents |
|---|---|
| **Risk** | Paged, searchable online players sorted by risk, with risk badge, state icon and tint |
| **Evidence** | Selected player's summary and latest evidence, with module icon, score badge and confidence tint |
| **Modules** | Selected player's per-module risk breakdown |

The handler keeps a per-admin session with tab, page, search and selected player. It accepts `t|...` text input for search and `a|...` actions for select, page, watch, clear, export, kick, ban, refresh and home. **Plugins** sends `home` to return to the board; **Close** is client-handled and closes the whole window.

GUI kick and ban appear and work only when `AllowGuiKick` and `AllowGuiBan` are true; both are false by default. GUI kick uses the reason `SentinelAC: staff action`, and GUI ban uses `DefaultBanHours`.

---

## Safety defaults and tuning workflow

!!! danger "Automatic bans are off by default for a reason"
    Sentinel AC is statistical. A score is evidence for review, not proof. Keep `DryRun = true`, `AutoKick = false` and `AutoBan = false` until you have watched the evidence for a full wipe and tuned the limits for your mod pack.

```json
"DryRun": true,
"AutoKick": false,
"AutoBan": false,
"ExemptAdmins": true,
"AllowGuiKick": false,
"AllowGuiBan": false,
"ActionSustainSeconds": 30.0,
"ActionCooldownMinutes": 15.0
```

- Alerts, evidence, watch and export work in dry-run mode; only automatic kick/ban is suppressed.
- A player must stay above `KickThreshold` or `BanThreshold` for `ActionSustainSeconds`.
- Automatic actions share a per-player `ActionCooldownMinutes` cooldown.
- `AutoBan` is evaluated before `AutoKick`; a ban wins when both apply.
- Exempt players never accumulate risk and are never actioned.
- Alerts and automatic actions are written to the Oxide log and, if enabled, to the webhook.
- Sentinel AC never returns a damage-blocking value from `OnEntityTakeDamage` and never blocks `OnEntityDeath`. It is an observer by default.

### Recommended rollout

1. Run with `DryRun = true`, `AutoKick = false` and `AutoBan = false` for at least one wipe.
2. Read `/ac top`, `/ac flags` and `/ac modules` for a few days.
3. Raise thresholds that produce noise; lower thresholds that miss obvious cheaters.
4. Use `/ac clear` after each reviewed false positive and fix the matching module limit.
5. Enable `AutoKick` first, with a high threshold and a long sustain time.
6. Enable `AutoBan` last, and keep a staff review channel for the webhook.

### Common tuning

- Too many speed alerts: raise `Movement.HardSpeed` or `Movement.ZThreshold`.
- Too many headshot alerts on PvE: raise `Combat.HeadshotRatioThreshold` or `Combat.MinHeadshots`.
- Modded quality tiers: raise `Inventory.MaxLegalQuality`.
- Fast builders or large modded bases: raise `Grief.MaxChangesPerMinute`.
- Busy server or high CPU: raise `Grief.SampleSeconds`, reduce the radii, or disable the grief module.
- Staff using god mode or teleport: `ExemptAdmins` is already true; also grant `sentinelac.exempt` to shared accounts.
- Custom stat or vehicle mods: raise `Exploit.StatOverflowFactor` and `Exploit.MaxVehicleSpeed`.
- Noisy webhook: raise `Webhook.MinRisk` or `Webhook.MinIntervalSeconds`.

---

## Troubleshooting

| Symptom | Check |
|---|---|
| `/ac` says a permission is required | Grant `sentinelac.admin` or use an Oxide administrator account |
| Alerts do not arrive in chat | Grant `sentinelac.alerts`; check `AlertThreshold` and `AlertCooldownSeconds` |
| Server Assist menu does not open | Server Assist loaded, `ServerAssist.Enabled` true, `sentinelac.admin` and `serverassist.use`, and the Server Assist client mod installed |
| No GUI kick or ban button | `AllowGuiKick` and `AllowGuiBan` are false by default |
| Automatic actions never fire | Check `DryRun`, `AutoKick`, `AutoBan`, the threshold, `ActionSustainSeconds`, `ActionCooldownMinutes` and exemption |
| Suspicious player has zero risk | Player may be within `MinPlayMinutes`, exempt, or below `MinSamples`; hard violations scored 90+ are still recorded |
| Staff are flagged by god mode or teleport | `ExemptAdmins` covers only the server admin tools list; grant `sentinelac.exempt` explicitly |
| Many false positives after adding mods | Raise the matching movement, quality, stack, vehicle, XP or grief limit |
| Evidence disappears after a restart | Active risk and evidence are runtime-only; export before restarting |
| Grief changes are missed | The sampler sees only its cube and requires proximity/line of sight; increase radii or lower `SampleSeconds` at a CPU cost |
| Export is not created | Check write access to `oxide/data/SentinelAC` and that the player state exists |
| Config edits appear ignored | Run `/ac reload`; an unreadable config is replaced with defaults on load |
| Icons show the default hammer | Install Server Assist Art on the server and every client; missing sprites fall back to the hammer |

---

## Limitations

- **No client aim data.** 7 Days to Die does not expose client aim angles, shot rays or input history to server plugins. Combat detection is statistical (reach, line of sight, hit rate, headshot ratio, kill rate), not a per-shot aimbot check.
- **Bounded grief sampling.** Block changes are sampled in a small cube around each player. Very fast movement, explosions outside the radius, changes made while nobody is nearby, or changes that cannot be attributed can be missed. This keeps CPU cost bounded and avoids scanning the whole world.
- **Not every exploit is visible.** ESP/radar, information-only wallhacks and client-side visual cheats do not produce a reliable server-side event and are not claimed as detected.
- **Modded values differ.** Modded weapons, perks, vehicles, stack sizes and quality tiers can legitimately exceed vanilla values. Tune the limits for your mod pack before enabling actions.
- **Statistical scores are not proof.** The `p~` value is presentation, not a calibrated probability. Treat every flag as a lead for a human to review.
- **Server-side observer.** Sentinel AC records evidence and can kick/ban, but it does not replace server configuration, permissions, land-claim settings or a staff review process.

---

## Art status

Server Assist Art **1.4.0** now includes the full 12-icon `sa_ac_*` set. SentinelAC 1.0.2 still uses the older `sa_nav_*` and `sa_act_*` placeholders until its icon constants are switched to the delivered keys.

| Sentinel AC use | Delivered sprite key |
|---|---|
| Provider card, banner, background | `sprite:sa_ac_panel` |
| Risk tab and risk rows | `sprite:sa_ac_risk` |
| Evidence tab and rows | `sprite:sa_ac_evidence` |
| Modules tab | `sprite:sa_ac_modules` |
| Movement module | `sprite:sa_ac_movement` |
| Combat module | `sprite:sa_ac_combat` |
| Inventory/economy module | `sprite:sa_ac_inventory` |
| Grief module | `sprite:sa_ac_grief` |
| Exploit module | `sprite:sa_ac_exploit` |
| Progression module | `sprite:sa_ac_progression` |
| Clear evidence | `sprite:sa_ac_clear` |
| Export evidence | `sprite:sa_ac_export` |

Until the plugin constants are switched, the existing placeholders remain visible: `sa_nav_moderation`, `sa_act_warn`, `sa_nav_logs`, `sa_nav_server`, `sa_nav_players`, `sa_act_spectate`, `sa_nav_snapshots`, `sa_act_kick`, `sa_act_ban` and `sa_plugin_hot_reload`. Install Server Assist Art on the server and every client; without the pack, missing sprites fall back to the client's hammer.

---

## Related pages

- [Server Assist product guide](../../core/server-assist.md)
- [Server Assist API](../../integration/api/index.md)
- [Providers](../../integration/api/providers.md)
- [View protocol](../../integration/api/view-protocol.md)
- [Handlers and lifecycle](../../integration/api/handlers.md)
- [Staff and utility companions](../../integration/companions/staff-utility.md)
- [Permissions](../../core/permissions.md)
- [Server Assist Art](../art/server-assist-art.md)
- [PermissionGui](permission-gui.md)
- [AdminSuite](admin-suite.md)
- [Companion problems](../../reference/troubleshooting/companions.md)

Package references: [README](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/SentinelAC/README.txt) · [Design notes](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/SentinelAC/docs/DESIGN.md) · [Quick start](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/SentinelAC/docs/QUICKSTART.md) · [Example config](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/SentinelAC/Config/SentinelAC.example.json)
