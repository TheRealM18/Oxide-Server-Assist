# SentinelAC icon requests

Status: **requested, not rendered — follow-up batch**

This is the SentinelAC batch. It does not replace the current active handoff in
`ServerAssistArt/ICON_REQUEST.md` (the 8 PlayerProfiles icons). Render this batch
after, or when the art agent is explicitly pointed at it.

Full work order: `ServerAssistArt/REQUESTS_SentinelAC.md`

Target art pack: `ServerAssistArt` / `AAZ_ServerAssistArt`
Naming prefix: `sa_ac_`
Count: **12 icons**

SentinelAC is already wired as a Server Assist provider with three tabs:
**Risk**, **Evidence** and **Modules**. It currently uses finished `sa_nav_*` and
`sa_act_*` sprites as placeholders. The 12 `sa_ac_*` icons below replace only the
misleading placeholders; kick, ban, refresh, paging, player rows and close keep
the existing art.

## Required icons

| # | Sprite key | Used for |
|---|---|---|
| 1 | `sa_ac_panel` | Provider/home card, banner, background |
| 2 | `sa_ac_risk` | Risk tab and risk summary row |
| 3 | `sa_ac_evidence` | Evidence tab and dossier rows |
| 4 | `sa_ac_modules` | Modules tab and module summary rows |
| 5 | `sa_ac_movement` | Movement module |
| 6 | `sa_ac_combat` | Combat module |
| 7 | `sa_ac_inventory` | Inventory/economy module |
| 8 | `sa_ac_grief` | Grief module |
| 9 | `sa_ac_exploit` | Exploit module |
| 10 | `sa_ac_progression` | Progression module |
| 11 | `sa_ac_clear` | Clear evidence button |
| 12 | `sa_ac_export` | Export evidence button |

## Do not paint

- `sa_act_kick`, `sa_act_ban`, `sa_plugin_hot_reload`, `sa_act_prev`,
  `sa_act_next`, `sa_nav_players`, `sa_act_spectate` — existing art is correct.
  Watch/unwatch keeps `sa_act_spectate`; Close is a text-only button.
- Any text, letters, numbers, item textures or replacement icons for other
  plugins.

## Format

- 128 x 128, 32-bit ARGB PNG, transparent background.
- Masters in `ServerAssistArt/icons/`.
- Final PNGs in `ServerAssistArt/UIAtlases/UIAtlas/`.
- Run `ServerAssistArt/tools/build-icons.ps1`.
- Keep the `sa_ac_` prefix.

## ServerAssist mod changes

None required. See `SentinelAC/update.md`.
