# SentinelAC icon request

> **Not active for the current batch.** The current active handoff is
> `ICON_REQUEST.md` (the 8 PlayerProfiles icons). Do not displace that batch.
> This is the next SentinelAC request.

Status: **requested, not rendered**

Scope: **SentinelAC only**. Render only the 12 icons listed in this file. Do not
render other batches from `PLANNED_ICONS.md`.

SentinelAC uses Server Assist provider mode with three tabs: **Risk**,
**Evidence** and **Modules**. The current build deliberately uses finished
ServerAssistArt sprites as placeholders so the menu is usable today.

---

## Current placeholder map

| SentinelAC use | Current placeholder |
|---|---|
| Provider/home card, banner, background | `sprite:sa_nav_moderation` |
| Risk tab / risk rows | `sprite:sa_act_warn` |
| Evidence tab / evidence rows | `sprite:sa_nav_logs` |
| Modules tab | `sprite:sa_nav_server` |
| Player rows | `sprite:sa_nav_players` |
| Watch / unwatch | `sprite:sa_act_spectate` (existing, keep) |
| Clear evidence | `sprite:sa_nav_logs` |
| Export evidence | `sprite:sa_nav_snapshots` |
| Kick | `sprite:sa_act_kick` |
| Ban | `sprite:sa_act_ban` |
| Refresh | `sprite:sa_plugin_hot_reload` |
| Previous / next page | `sprite:sa_act_prev`, `sprite:sa_act_next` |
| Movement module | `sprite:sa_nav_teleport` |
| Combat module | `sprite:sa_nav_moderation` |
| Inventory/economy module | `sprite:sa_nav_items` |
| Grief module | `sprite:sa_nav_world` |
| Exploit module | `sprite:sa_act_godmode` |
| Progression module | `sprite:sa_nav_snapshots` |

The existing kick, ban, refresh, player, prev/next and close icons are good
enough and must **not** be redrawn.

---

## Requested icons — 12 total

Priority: `sa_ac_panel`, `sa_ac_risk`, `sa_ac_evidence` first. The module icons
are the second priority.

| # | Sprite key | Master file | Used for | Subject |
|---|---|---|---|---|
| 1 | `sa_ac_panel` | `icons/sa_ac_panel.png` | Provider/home card, banner, background | Weathered riot shield with a radar sweep and a small alert blip, chipped teal metal, warm amber light |
| 2 | `sa_ac_risk` | `icons/sa_ac_risk.png` | Risk tab, risk summary row | Heavy analog gauge with the needle in the amber/red zone, brass bezel and rivets |
| 3 | `sa_ac_evidence` | `icons/sa_ac_evidence.png` | Evidence tab, evidence dossier rows | Folded evidence dossier or clipboard with a magnifying glass and redacted lines |
| 4 | `sa_ac_modules` | `icons/sa_ac_modules.png` | Modules tab, module summary rows | Three stacked detection cards or gears with a thin scan line, teal/amber |
| 5 | `sa_ac_movement` | `icons/sa_ac_movement.png` | Movement module | Survivor boot with short motion streaks and a cracked speed line |
| 6 | `sa_ac_combat` | `icons/sa_ac_combat.png` | Combat module | Crosshair with a bullet trail and a small warning notch, teal/brass |
| 7 | `sa_ac_inventory` | `icons/sa_ac_inventory.png` | Inventory/economy module | Open supply crate with two identical item cards and an alert chevron |
| 8 | `sa_ac_grief` | `icons/sa_ac_grief.png` | Grief module | Cracked land-claim keystone or broken shield over a small base footprint |
| 9 | `sa_ac_exploit` | `icons/sa_ac_exploit.png` | Exploit module | Mechanical bug/exploit spider over a broken padlock, restrained red accent |
| 10 | `sa_ac_progression` | `icons/sa_ac_progression.png` | Progression module | XP bar with an upward arrow and a small level badge, amber glow |
| 11 | `sa_ac_clear` | `icons/sa_ac_clear.png` | Clear evidence button | Stiff brush or eraser sweeping away a small evidence card, red-painted edge |
| 12 | `sa_ac_export` | `icons/sa_ac_export.png` | Export evidence button | Field report/document with an outward download or transfer arrow |

---

## Do not make these

These are already covered and should not be redrawn:

- `sa_act_kick`, `sa_act_ban` — kick/ban buttons
- `sa_plugin_hot_reload` — refresh button
- `sa_act_prev`, `sa_act_next` — paging
- `sa_nav_players` — player rows
- `sa_act_spectate` — watch/unwatch already uses this and it reads correctly
- Any item textures. Player inventory/evidence detail is text; item rows use live `item:` icons if added later.
- Any text, letters or numbers in the art.
- Any replacement for `sa_nav_*`, `sa_act_*`, `sa_stream_*` or `sa_perm_*`.

---

## Sheet plan

Three 2x2 sheets:

### Sheet S1 — `icons/_sheets/sentinel_core_1.png`

| Quadrant | Sprite |
|---|---|
| top-left | `sa_ac_panel` |
| top-right | `sa_ac_risk` |
| bottom-left | `sa_ac_evidence` |
| bottom-right | `sa_ac_modules` |

### Sheet S2 — `icons/_sheets/sentinel_modules_1.png`

| Quadrant | Sprite |
|---|---|
| top-left | `sa_ac_movement` |
| top-right | `sa_ac_combat` |
| bottom-left | `sa_ac_inventory` |
| bottom-right | `sa_ac_grief` |

### Sheet S3 — `icons/_sheets/sentinel_modules_2.png`

| Quadrant | Sprite |
|---|---|
| top-left | `sa_ac_exploit` |
| top-right | `sa_ac_progression` |
| bottom-left | `sa_ac_clear` |
| bottom-right | `sa_ac_export` |

Use the shared sheet prompt from `PLANNED_ICONS.md`:

> Four separate game UI icons arranged in a clean 2x2 grid on a fully
> transparent background, each icon centered in its own quadrant with generous
> empty space between them, no grid lines, no borders, no text, no letters.
> Match the exact art style of the reference image: 7 Days to Die
> post-apocalyptic UI icon, hand-painted weathered metal, chipped teal paint,
> rust, scratches, bold dark outline, strong rim light, high contrast, chunky
> readable silhouette, slightly isometric three-quarter view. Each icon must be
> readable at 64x64. Color accents: teal metal, warm amber, brass, rust.

Reference image: `bonus/server_assist.png`.

---

## Output requirements

- Final PNG: 128 x 128, 32-bit ARGB, transparent background.
- Masters in `ServerAssistArt/icons/`.
- Final generated PNGs in `ServerAssistArt/UIAtlases/UIAtlas/`.
- Use the exact sprite keys above; no `.png` in code.
- Run `ServerAssistArt/tools/build-icons.ps1` after adding masters.
- Do not rename or overwrite existing PNGs.

## After the art exists

1. Add the PNGs to `ServerAssistArt/UIAtlases/UIAtlas/`.
2. Confirm each key resolves as `sprite:<key>`.
3. Switch the icon constants in `SentinelAC/oxide/plugins/SentinelAC.cs` from the
   placeholder `sa_nav_*`/`sa_act_*` keys to the matching `sa_ac_*` keys.
4. Keep `sa_act_kick`, `sa_act_ban`, `sa_plugin_hot_reload`, `sa_act_prev`,
   `sa_act_next`, `sa_nav_players` and `sa_nav_help` as-is.

## ServerAssist mod update

No ServerAssist client-mod change is requested for SentinelAC. The current
provider API, three tabs, search input, paging, row/button icons, tint, banner
and background are sufficient. See `SentinelAC/update.md`.
