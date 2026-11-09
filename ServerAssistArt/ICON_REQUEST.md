# Server Assist art agent - current icon request

Status: **active handoff. Only 8 icons are required.**

This list was produced by scanning every `.cs`, `.xml` and `.json` file under
`Oxide` for `sa_*` sprite references and comparing them with
`ServerAssistArt/UIAtlases/UIAtlas`.

Result:

- 132 icons already exist in the atlas.
- Every sprite referenced by current code exists **except** the 8 PlayerProfiles
  sprites below.
- The installed `AAZ_ServerAssistArt` atlas matches the workspace atlas exactly.

Do not use `ICON_PRIORITY.md` as the current work list. That file is a longer
plan. This file is the only scope for this batch.

---

## Required icons: exactly 8

All eight belong to **PlayerProfiles**. They are wired in
`PlayerProfiles/oxide/plugins/PlayerProfiles.cs`. Until they exist, the plugin
falls back to the default hammer symbol.

| # | Sprite key | Used by | Subject |
|---|---|---|---|
| 1 | `sa_profile_panel` | Home card, panel banner/background | Hero character profile card with a survivor portrait and dog tags. Not a server nameplate. |
| 2 | `sa_profile_identity` | Inactive profile rows, empty state | Single survivor ID badge with a small character silhouette and dog-tag chain. |
| 3 | `sa_profile_active` | Active profile row | The identity badge with a bright amber check, star or active marker. |
| 4 | `sa_profile_switch` | Switch button, Switch screen, Switch Now | Two overlapping character cards with a curved swap arrow. |
| 5 | `sa_profile_new` | New button, New Profile row, Create | Blank character card with a bold plus and a small survivor silhouette. |
| 6 | `sa_profile_rename` | Rename button, Rename screen, Save | Character card with a metal pencil/stamp and a blank name line. |
| 7 | `sa_profile_save` | Save Current | Rugged field notebook or metal save stamp with a bold check and a dog tag. |
| 8 | `sa_profile_delete` | Delete button, Delete screen, confirm | Trash can with a character card or dog-tag corner, clearly destructive. |

Keep the same character-card / dog-tag motif across all eight. The panel icon is
the hero; give it the most detail.

### Reuse existing icons - do not make new ones

| Sprite key | Used by |
|---|---|
| `sa_nav_help` | Help button, help rows, error row |
| `sa_plugin_hot_reload` | Admin Reload button |

---

## Do not make these

The following are **not** part of this request:

- The 19 unused server-profile icons: `sa_profile_description`,
  `sa_profile_rules`, `sa_profile_settings`, `sa_profile_mods`,
  `sa_profile_version`, `sa_profile_region`, `sa_profile_difficulty`,
  `sa_profile_pvp`, `sa_profile_pve`, `sa_profile_wipe`, `sa_profile_links`,
  `sa_profile_tags`, `sa_profile_banner`, `sa_profile_preview`,
  `sa_profile_import`, `sa_profile_export`, `sa_profile_duplicate`,
  `sa_profile_lock`, `sa_profile_visibility`.
- Any `sa_buggee_*` icon. BuggeeNet currently uses working `sa_stream_*` and
  `sa_nav_*` aliases. Unique BuggeeNet art is optional and not needed for the
  panel to work.
- `sa_rewards_*`, `sa_shop_*`, `sa_vote_*` or other planned icons. Current code
  does not reference them.
- Replacement `sa_stream_*`, `sa_announce_*` or `sa_perm_*` art. The existing
  aliases are already wired and in the atlas.
- Icons for Refresh, Back, Prev/Next, Cancel, `ACTIVE`, `NEW`, `DELETE`, `EMPTY`
  or `ERROR`. Those are text buttons, text badges or tints.

---

## Asset specs

- 128x128, 32-bit ARGB PNG with a transparent background.
- Masters in `icons/`; sheets in `icons/_sheets/`; final PNGs in
  `UIAtlases/UIAtlas/`.
- Sprite names never include `.png` in code.
- Keep the `sa_` prefix.
- Auto-trim and re-center with the existing 4 px margin.
- The card draws at 64x64; keep the silhouette readable at that size.
- Style reference: `bonus/server_assist.png`.
- Palette: chipped teal metal, rust, brass, warm amber. Restrained red only on
  the delete icon.
- No text, letters or numbers in the art.

## Sheet plan

Two 2x2 sheets, eight icons total.

### Sheet P1 - `icons/_sheets/profile_core_1.png`

| Quadrant | Sprite |
|---|---|
| 1 | `sa_profile_panel` |
| 2 | `sa_profile_identity` |
| 3 | `sa_profile_active` |
| 4 | `sa_profile_switch` |

### Sheet P2 - `icons/_sheets/profile_actions_1.png`

| Quadrant | Sprite |
|---|---|
| 1 | `sa_profile_new` |
| 2 | `sa_profile_rename` |
| 3 | `sa_profile_save` |
| 4 | `sa_profile_delete` |

## Build handoff

Run from `ServerAssistArt` after the two sheets are saved:

```powershell
powershell -File .\tools\slice-sheet.ps1 `
  -Sheet .\icons\_sheets\profile_core_1.png `
  -OutDir .\icons `
  -Names @('sa_profile_panel','sa_profile_identity','sa_profile_active','sa_profile_switch')

powershell -File .\tools\slice-sheet.ps1 `
  -Sheet .\icons\_sheets\profile_actions_1.png `
  -OutDir .\icons `
  -Names @('sa_profile_new','sa_profile_rename','sa_profile_save','sa_profile_delete')

powershell -ExecutionPolicy Bypass -File .\tools\build-icons.ps1
```

Add the eight names to `$preferred` in `tools/build-icons.ps1` before running
the build.

## Acceptance checklist

- [ ] Exactly these eight PNGs exist in `UIAtlases/UIAtlas/`:
      `sa_profile_panel.png`, `sa_profile_identity.png`,
      `sa_profile_active.png`, `sa_profile_switch.png`,
      `sa_profile_new.png`, `sa_profile_rename.png`,
      `sa_profile_save.png`, `sa_profile_delete.png`.
- [ ] No `sa_buggee_*`, `sa_rewards_*`, `sa_shop_*` or `sa_vote_*` icons were
      rendered for this batch.
- [ ] No server-profile icons besides the eight listed were rendered.
- [ ] The PlayerProfiles constants still use the sprite keys above.
- [ ] The Profiles home card shows `sa_profile_panel` in game.
- [ ] Inactive rows show `sa_profile_identity`; the active row shows
      `sa_profile_active` plus its amber tint and `ACTIVE` badge.
- [ ] Switch, New, Rename, Save and Delete screens/buttons show their matching
      icons.
- [ ] Help uses existing `sa_nav_help`; Reload uses existing
      `sa_plugin_hot_reload`.
- [ ] All eight are legible at 64x64 with clean alpha.
- [ ] The rebuilt atlas pack is installed on the server and every client, and the
      Server Assist art zip/manifest is updated.

The authoritative detailed request remains
`PlayerProfiles/icon-request.md`. This file is the short handoff and must match
it.
