# PlayerProfiles icon request

Status: definitive. This supersedes the **Server Profiles** block in
`ServerAssistArt/ICON_PRIORITY.md`.

The active art-agent handoff is `ServerAssistArt/ICON_REQUEST.md`. It requests
only the 8 icons below and nothing else.

The 23-icon "Server Profiles" block currently in `ICON_PRIORITY.md` is for a
server browser / server-info system: region, difficulty, PvP/PvE, wipe, mods,
version, visibility, import/export and so on. **PlayerProfiles is not that
plugin.** It is the player-character profile system: one Steam account with
multiple saved characters, each with its own inventory, quests, level, buffs,
waypoints and position.

Use this file as the authoritative list. Do not spend budget on the other 19
server-profile icons.

---

## 1. What the plugin actually has

Server Assist home card:

```text
Profiles
```

Inside the panel:

- profile list, one card per character
- active profile highlighted with a gold tint and `ACTIVE` text badge
- new character marked with a `NEW` text badge
- detail screen with Switch, Rename and Delete
- New and Rename screens use one text input
- Delete and Switch confirmation screens
- Help screen
- pagination buttons and a Reload admin button

There is no server browser, no per-profile permissions, no visibility toggle,
no import/export, no duplicate action and no server metadata. Those icons are
not needed.

---

## 2. Exact icons needed: 8

| # | Sprite key | Used by | Subject |
|---|---|---|---|
| 1 | `sa_profile_panel` | Server Assist home card, panel banner/background | Player character profile card with a survivor portrait/dog tag. Not a server nameplate. |
| 2 | `sa_profile_identity` | Inactive profile row, empty state | Single survivor ID badge with a small character silhouette and dog-tag chain. |
| 3 | `sa_profile_active` | Active profile row | Same ID badge/card with a bright amber check, star or active marker. |
| 4 | `sa_profile_switch` | Detail `Switch`, confirm `Switch Now` | Two overlapping character cards with a curved swap/transfer arrow, meaning "load this character". |
| 5 | `sa_profile_new` | List `New`, New Profile screen row | Blank character card with a bold plus and a small survivor silhouette. |
| 6 | `sa_profile_rename` | Detail `Rename`, Rename screen `Save` | Character card with a metal pencil/stamp and a blank name line. |
| 7 | `sa_profile_save` | List `Save Current` | Rugged field notebook or metal save stamp with a bold check and a dog tag. A floppy disk is acceptable only if it stays readable at 64x64. |
| 8 | `sa_profile_delete` | Detail `Delete`, Delete screen row/button | Trash can with a character card or dog tag corner, clearly destructive. |

Keep the same character-card / dog-tag motif across all eight so they read as
one plugin set. The panel icon is the hero; spend the most detail there.

No text, letters or numbers in the art.

### Existing icons to reuse (do not render new ones)

| Sprite key | Used by |
|---|---|
| `sa_nav_help` | `Help` button, help rows, error row |
| `sa_plugin_hot_reload` | Admin `Reload` button |

No icons are needed for Refresh, First/Prev/Next/Last, Back, Cancel, `ACTIVE`,
`NEW`, `DELETE`, `EMPTY`, `ERROR`, dead profiles or slot limits. Those are text
buttons, text badges and tints.

---

## 3. Correction to `ICON_PRIORITY.md`

The P0 **Server Profiles — 23** block should become this 8-icon block.

Keep these 4 names and redefine their subjects:

```text
sa_profile_panel
sa_profile_identity
sa_profile_save
sa_profile_delete
```

Add these 4 names:

```text
sa_profile_active
sa_profile_switch
sa_profile_new
sa_profile_rename
```

Drop these 19 names for this plugin:

```text
sa_profile_description
sa_profile_rules
sa_profile_settings
sa_profile_mods
sa_profile_version
sa_profile_region
sa_profile_difficulty
sa_profile_pvp
sa_profile_pve
sa_profile_wipe
sa_profile_links
sa_profile_tags
sa_profile_banner
sa_profile_preview
sa_profile_import
sa_profile_export
sa_profile_duplicate
sa_profile_lock
sa_profile_visibility
```

Why they are not needed:

- `description`, `rules`, `settings`, `mods`, `version`, `region`,
  `difficulty`, `pvp`, `pve`, `wipe`, `links`, `tags`, `banner`, `preview`
  are server-listing fields. This plugin has no server browser.
- `import`, `export`, `duplicate` are not implemented. New profiles are
  created with the New action; copy-on-create is a config flag, not a separate
  button.
- `lock`, `visibility` are not implemented. Permissions are account-wide and
  existing profiles remain readable when the slot permission is reduced.
- `sa_profile_identity` is the row icon. `sa_profile_active` is its active-state
  variant. There is no separate server identity badge.

Budget impact: 8 icons = 2 sheets instead of 23 icons = 6 sheets.

---

## 4. Wiring map (already wired in the plugin)

These constants in `PlayerProfiles/oxide/plugins/PlayerProfiles.cs` point at
the names above. Do not rename the sprites without updating the plugin.

| Constant | Sprite | Where |
|---|---|---|
| `PanelIcon` | `sprite:sa_profile_panel` | `RegisterPanel`, `SetPanelIcon`, `background`, `banner` |
| `RowIcon` | `sprite:sa_profile_identity` | inactive profile rows, detail rows, empty state |
| `ActiveRowIcon` | `sprite:sa_profile_active` | active profile row |
| `NewIcon` | `sprite:sa_profile_new` | `New` button, New Profile row, Create button |
| `RenameIcon` | `sprite:sa_profile_rename` | Rename button, Rename screen row and Save button |
| `DeleteIcon` | `sprite:sa_profile_delete` | Delete button, Delete screen row and confirm button |
| `SwitchIcon` | `sprite:sa_profile_switch` | Switch button, Switch screen row, Switch Now button |
| `SaveIcon` | `sprite:sa_profile_save` | `Save Current` button |
| `HelpIcon` | `sprite:sa_nav_help` | Help button, help rows, error row |
| `ReloadIcon` | `sprite:sa_plugin_hot_reload` | admin Reload button |

Until the PNGs are installed, Server Assist falls back to its default symbol
for the missing sprite. That is expected; install the art pack on the server
and every client.

---

## 5. Sheet plan

Two 2x2 sheets, eight icons total.

### Sheet P1 - `icons/_sheets/profile_core_1.png`

| Quadrant | Sprite | Subject |
|---|---|---|
| 1 | `sa_profile_panel` | Hero character profile card, survivor portrait, dog tags, chipped teal metal |
| 2 | `sa_profile_identity` | Single survivor ID badge, portrait silhouette, dog-tag chain |
| 3 | `sa_profile_active` | The identity badge with a bright amber check/star active marker |
| 4 | `sa_profile_switch` | Two overlapping character cards with a curved swap arrow |

### Sheet P2 - `icons/_sheets/profile_actions_1.png`

| Quadrant | Sprite | Subject |
|---|---|---|
| 1 | `sa_profile_new` | Blank character card with a bold plus and small survivor silhouette |
| 2 | `sa_profile_rename` | Character card with a metal pencil/stamp and a blank name line |
| 3 | `sa_profile_save` | Rugged field notebook or save stamp with a bold check and dog tag |
| 4 | `sa_profile_delete` | Trash can with a character card or dog-tag corner, destructive red accent |

Shared sheet prompt:

> Four separate game UI icons arranged in a clean 2x2 grid on a fully
> transparent background, each icon centered in its own quadrant with generous
> empty space between them, no grid lines, no borders, no text, no letters, no
> numbers. Match the exact art style of the reference image: 7 Days to Die
> post-apocalyptic UI icon, hand-painted weathered metal, chipped teal paint,
> rust, scratches, bold dark outline, strong rim light, high contrast, chunky
> readable silhouette, slightly isometric three-quarter view. Each icon must be
> readable at 64x64. The set shares a player-character / dog-tag motif. Color
> accents: teal metal, warm amber, brass, rust. The active icon gets a clear
> amber accent; the delete icon gets a restrained red accent.

Use `ServerAssistArt/bonus/server_assist.png` as the style reference.

---

## 6. Asset specs

- 128x128, 32-bit ARGB PNG with a transparent background in
  `UIAtlases/UIAtlas/`.
- Masters in `icons/`; sheets in `icons/_sheets/`.
- Auto-trim and re-center with a 4 px margin, as the existing build script
  does.
- The card draws at 64x64; keep the silhouette readable at that size.
- Sprite names never include `.png` in code.
- Keep the `sa_` prefix; it prevents collisions in the shared global atlas.

Build:

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

---

## 7. Acceptance checklist

- [ ] Exactly these eight PNGs exist in `UIAtlases/UIAtlas/`:
      `sa_profile_panel.png`, `sa_profile_identity.png`,
      `sa_profile_active.png`, `sa_profile_switch.png`,
      `sa_profile_new.png`, `sa_profile_rename.png`,
      `sa_profile_save.png`, `sa_profile_delete.png`.
- [ ] No server-profile icons were rendered for this plugin: description,
      rules, settings, mods, version, region, difficulty, PvP, PvE, wipe,
      links, tags, banner, preview, import, export, duplicate, lock,
      visibility.
- [ ] The plugin constants still match the wiring map in section 4.
- [ ] In game, the Profiles home card uses `sa_profile_panel`.
- [ ] Inactive rows use `sa_profile_identity`; the active row uses
      `sa_profile_active` and the gold tint/ACTIVE badge.
- [ ] New/Rename/Delete/Switch screens and their primary buttons show the
      matching action icons.
- [ ] Help uses the existing `sa_nav_help`; Reload uses the existing
      `sa_plugin_hot_reload`.
- [ ] All icons are legible at 64x64 and have no dark box or lost alpha.
- [ ] The updated atlas pack is installed on the server and every client, and
      the Server Assist art zip/manifest is rebuilt.

If a card still shows the hammer, the PNG is missing, the sprite name is
misspelled, or the client has an older atlas. Sprite names never include
`.png`.
