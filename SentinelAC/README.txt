SentinelAC 1.0.2
================

A server-side statistical anti-cheat, anti-exploit and anti-grief plugin for
7 Days to Die. It is an Oxide plugin only: no kernel driver, no client DLL and
no code injected into the game client.

SentinelAC does not try to prove that a single event is impossible. It builds
baselines for each player and for the server, then treats unusual behaviour as
evidence. Evidence decays over time and is combined into a 0-100 risk score.
Staff can review the evidence, watch a player, or act with a command. Auto-kick
and auto-ban are opt-in and are off by default.

The design is deliberately "Matrix-like": many weak signals, per-player
statistics, server-wide baselines, confidence weighting, cooldowns and a full
evidence trail instead of one brittle check. XP, kill-rate and wealth-rate
outliers are compared both to the player's own history and to the whole server's
distribution.


FILES
-----

  oxide/plugins/SentinelAC.cs

Oxide compiles the .cs file at server start. There is no DLL to copy.


INSTALL
-------

1. Stop the server.
2. Copy SentinelAC.cs into the server's oxide/plugins folder.
3. Start the server. Oxide compiles it and creates:
     oxide/config/SentinelAC.json
     oxide/data/SentinelAC/profiles.json
4. Join the server as an admin and type /ac status.
5. Leave DryRun = true while you tune the thresholds. Read the alerts and the
   evidence. When you are comfortable, set DryRun = false and enable only the
   actions you want.

SentinelAC uses the same server-side hook surface as other 7 Days to Die Oxide
plugins. It does not replace game files, does not require Harmony, and does not
need players to install anything.


PERMISSIONS
-----------

  sentinelac.admin    Use /ac commands and open the ServerAssist panel.
  sentinelac.alerts   Receive in-game risk alerts.
  sentinelac.exempt   Never accumulate risk or trigger an action.
  sentinelac.use      Reserved for future player-facing features.

The default Oxide admin group receives sentinelac.admin and sentinelac.alerts
on the first server start. Admins are exempt by default (ExemptAdmins = true).
Add sentinelac.exempt to staff, testers, event accounts and service accounts
that should never be actioned.


COMMANDS
--------

  /ac status [player]       Risk, session risk, peak risk, flags, playtime and
                            the latest evidence.
  /ac top                   Highest-risk online players.
  /ac flags <player>        Recent evidence, newest first.
  /ac modules <player>      Active risk by detection module.
  /ac watch <player>        Toggle the staff watch flag.
  /ac clear <player>        Clear risk and evidence for a player.
  /ac kick <player> [reason]
  /ac ban <player> [hours] [reason]
  /ac export <player>       Write an evidence report to oxide/data/SentinelAC/.
  /ac gui                   Open the ServerAssist panel.
  /ac reload                Reload the config and restart the scan timers.

Aliases: /sentinel, /sentinelac.


DETECTION MODULES
-----------------

Movement
  - Horizontal speed against the player's own baseline and a hard ceiling.
  - Sustained upward movement / hover without a jetpack, ladder or vehicle.
  - Repeated long-distance position jumps in a short window.
  - Feet and head inside collidable blocks for several samples (no-clip).
  - Position below the configured world floor (under-map).
  - Optional fall-damage bypass check (off by default because perks and mods
    can legitimately reduce fall damage).

Combat
  - Melee reach beyond the configured maximum.
  - Hits through solid blocks (line-of-sight sampling against the voxel world).
  - Ranged hits while the magazine is empty (infinite-ammo signal).
  - Damage-event rate above the configured ceiling.
  - Very fast target switching between different victims.
  - Headshot ratio over a rolling window.
  - Player-vs-player kill rate against the player's own baseline and the
    server-wide distribution (zombie kill rate is deliberately excluded).

Inventory and economy
  - Stack counts above the item's legal maximum.
  - Item quality above the game/configured maximum.
  - Unknown item ids.
  - High-quality item gain rate.
  - Wealth z-score and duplicate item signatures (possible duplication).

Grief
  - A small rolling block snapshot around every player. The snapshot detects
    structural changes, not normal damage-only mining.
  - Changes inside another player's land claim.
  - Removal of another player's land-claim keystone.
  - Changes inside trader protection.
  - Block-change rate, burst changes and explosion-like bursts in one sample.
  - Repeat kills of the same victim in a short window (spawn camping signal).
  - Multiple vehicles spawning near one player in a short window.

Exploit
  - God mode on a non-exempt player.
  - Health, stamina, food or water above the modified maximum.
  - Jetpack active while not wearing a jetpack.
  - Vehicle speed above the configured ceiling.
  - Many damage events with no health loss (possible damage immunity).

Progression
  - XP per minute against the player's baseline and a hard ceiling.
  - Level jumps larger than one level per sample.


RISK SCORE
----------

Each piece of evidence has a module, a code, a raw score (0-100), a confidence
(0-1) and a detail string. The contribution is:

  contribution = score * confidence * module weight * suppression * sensitivity

Module weights: grief 1.5, exploit 1.5, inventory 1.3, combat 1.2, movement 1.0,
progression 0.8. A single evidence contribution is capped (40 normally, 70 for
critical evidence with a raw score of 95 or more), so one event cannot jump the
score to 100 by itself. Repeated evidence in the same module is also suppressed.

Suppression reduces the contribution when a module is already contributing a
lot, so one noisy module cannot dominate the score. Risk decays exponentially;
the default half-life is 30 minutes. Evidence is kept for two hours by default.

Thresholds (defaults):

  AlertThreshold = 40    Send the first admin alert.
  WatchThreshold = 60    Auto-watch (if AutoWatch is true).
  KickThreshold  = 90    Auto-kick if AutoKick is true.
  BanThreshold   = 97    Auto-ban if AutoBan is true.

The `/ac status` output and the Server Assist detail view also show a logistic
"p~" value. It is a readable presentation of the risk score (risk 60 maps to
about 50%, risk 90 to about 92%), not a calibrated statistical probability.

Hard violations (raw score >= 90) can be recorded during the first few minutes.
Statistical evidence is suppressed until MinPlayMinutes has elapsed, so a new
player is not punished for an empty baseline.


SAFETY
------

* DryRun = true is the default. Alerts and evidence still work; auto-kick and
  auto-ban are disabled.
* AutoKick and AutoBan are false by default.
* Actions require the risk to stay above the threshold for ActionSustainSeconds
  and then respect ActionCooldownMinutes.
* Exempt players never accumulate risk.
* Every action and alert is written to the Oxide log and, if enabled, to a
  Discord-compatible webhook.
* SentinelAC never returns a damage-blocking value from OnEntityTakeDamage and
  never blocks OnEntityDeath. It is an observer by default.

Recommended rollout:

  1. DryRun = true, AutoKick = false, AutoBan = false for at least one wipe.
  2. Read /ac top and /ac flags for a few days.
  3. Raise thresholds that produce noise; lower thresholds that miss obvious
     cheaters. Use /ac clear after a false positive.
  4. Enable AutoKick only after the false-positive rate is acceptable.
  5. Enable AutoBan last, and keep a staff review channel for the webhook.


CONFIG NOTES
------------

The plugin creates a full config with every field. The important groups are:

  Movement      speed, vertical movement, teleports, grace period.
  Combat        melee reach, hit rate, headshot ratio, kill rate.
  Inventory     quality/stack limits, wealth and duplicate detection.
  Grief         block snapshot radius, rate limits, claim/trader protection.
  Exploit       god mode, stat overflow, jetpack, vehicle speed, damage
                immunity.
  Progression   XP rate and level jumps.
  Webhook       Discord-compatible alert delivery.
  ServerAssist  panel id, icon and order.

If your server uses mods that raise quality tiers, stack sizes, movement speed
or vehicle speed, raise the matching limit. SentinelAC already uses the item's
own MaxCount and the game's MaxQualityTier where possible.

On a PvP or raiding server, set Grief.ProtectClaims = false if breaking blocks
inside another player's claim is legal on your server. Leave it true on PvE and
claim-protected servers.


PUBLIC API
----------

Other plugins can query SentinelAC through Oxide's plugin Call interface:

  double GetRisk(string playerId)
  int GetFlags(string playerId)
  bool IsWatched(string playerId)
  string GetEvidenceSummary(string playerId)
  void AddExternalEvidence(string playerId, string module, string code,
                           double score, double confidence, string detail)

Example:

  Plugin sentinel = plugins.Find("SentinelAC");
  double risk = sentinel != null ? (double)sentinel.Call("GetRisk", playerId) : 0.0;

AddExternalEvidence lets a companion plugin contribute a finding to the same
risk and evidence trail. Use a module name that describes the source, for
example "external" or "serverassist".


SERVERASSIST
------------

If ServerAssist is loaded, SentinelAC registers as a ServerAssist provider with
three real tabs:

  Risk       Online players sorted by risk, with name/id search and paging.
  Evidence   The selected player's risk summary and latest evidence records.
  Modules    The selected player's per-module risk breakdown.

Every player card, evidence row and module row carries an icon, badge and tint.
The selected player can be watched, cleared or exported from the Evidence and
Modules tabs. GUI kick and GUI ban buttons appear only when AllowGuiKick /
AllowGuiBan are true; they are false by default.

The Plugins button returns to the Server Assist plugin board. The separate
Close button closes the whole Server Assist window.

Older ServerAssist builds without provider support automatically fall back to a
normal home panel using the same handler, rows and buttons.

The current build uses finished ServerAssistArt sprites as placeholders:
sa_nav_moderation for the card, sa_act_warn for risk, sa_nav_logs for evidence,
sa_nav_server for modules, and the existing sa_nav_players / sa_act_* icons for
rows and buttons. Dedicated sa_ac_* art has been requested in:

  ServerAssistArt/REQUESTS_SentinelAC.md
  ICON-REQUESTS-SentinelAC.md

Install ServerAssistArt on the server and every client to see the icons. The
plugin works without the art pack; missing sprites fall back to the client's
default hammer icon.

No ServerAssist client-mod update is required for this version. The provider,
tabs, input, paging, icons and tint used here are already supported. See
SentinelAC/update.md for the API list and the optional future risk-gauge note.


LIMITATIONS
-----------

* 7 Days to Die does not expose a client-side aim angle, shot ray or input
  history to server plugins. Combat detection is therefore statistical
  (headshot ratio, reach, wall checks, rate) rather than a per-shot aim check.
* Block changes are sampled in a small radius around each player. Very fast
  movement, explosions outside that radius, or changes made while nobody is
  nearby can be missed. This keeps CPU cost bounded and avoids scanning the
  whole world.
* The server is authoritative for movement, but not every client-side exploit
  is visible as a server-side event. In particular, ESP/radar, wallhacks used
  only for information, and client-side visual cheats do not produce a reliable
  server-side event and are not claimed as detected here. SentinelAC is not a
  replacement for server configuration, permissions and a human review process.
* Modded weapons, perks, vehicles and quality tiers can legitimately exceed
  vanilla values. Tune the limits for your mod pack before enabling actions.
