# VoteSkipNight icon request

> **Not active for the current batch.** The only current request is
> `ICON_REQUEST.md`, which asks for the 8 missing PlayerProfiles icons. Do not
> render this file's icons yet.

Status: **requested, not rendered**. This file is the render brief for the
ServerAssistArt agent.

Plugin: `VoteSkipNight` 1.2.0
UI: Server Assist 2.0 GUI hook, 10 cards, 64x64 row icons
Target sheet: `icons/_sheets/vote_actions_1.png` (one 2x2 sheet)

## Exactly four icons are needed

Do not add any other VoteSkipNight icons. The rest of the panel reuses the
finished art listed under "Do not recreate" below.

| Sprite key | Subject | Used on |
|---|---|---|
| `sa_vote_yes` | Blank weathered voting slip with a bold green check mark, held upright by a small metal clip. The green check is the dominant shape. | Vote Yes action card |
| `sa_vote_no` | Blank weathered voting slip with a bold red X, held upright by a small metal clip. The red X is the dominant shape. | Vote No action card |
| `sa_vote_cancel` | Metal ballot box with a red stop/octagon plaque on the front and a gloved hand pulling a voting slip out. Add a short curved return arrow. | Cancel Vote action card |
| `sa_vote_bloodmoon` | Blood-red crescent moon rising behind a metal ballot box, red rim light, faint zombie claw scratches on the box, dark storm clouds. The moon and box are the dominant shapes. | Status card when a blood moon blocks the vote |

All four must be readable at 64x64 with no text and no letters.

## Do not recreate these — they are already finished and already in use

| Panel card | Existing sprite key |
|---|---|
| Panel card, Start Vote, night | `sprite:sa_vote_skip_night` |
| Force Skip | `sprite:sa_act_time` |
| Eligible Voters | `sprite:sa_nav_players` |
| Current Vote | `sprite:sa_nav_moderation` |
| Your Vote | `sprite:sa_permission_gui` |
| Refresh | `sprite:sa_plugin_hot_reload` |
| Cooldown status | `sprite:sa_act_warn` |
| Sleeping status | `sprite:sa_act_freeze` |
| Day status | `sprite:sa_nav_world` |
| Ready status | `sprite:sa_act_godmode` |

## Explicitly out of scope

The earlier Sheet K draft listed `sa_vote_start` and `sa_vote_night`. Drop both:
`sa_vote_skip_night` already covers those states. Do not make:

- `sa_vote_start`
- `sa_vote_night`
- `sa_vote_sleeping` (reusing `sa_act_freeze`)
- `sa_vote_cooldown` (reusing `sa_act_warn`)
- `sa_vote_ready` (reusing `sa_act_godmode`)
- `sa_vote_current` (reusing `sa_nav_moderation`)
- `sa_vote_your` (reusing `sa_permission_gui`)
- `sa_vote_force` (reusing `sa_act_time`)
- `sa_vote_eligible` (reusing `sa_nav_players`)
- `sa_vote_refresh` (reusing `sa_plugin_hot_reload`)

## Shared sheet prompt

> Four separate game UI icons arranged in a clean 2x2 grid on a fully
> transparent background, each icon centered in its own quadrant with generous
> empty space between them, no grid lines, no borders, no text, no letters.
> Match the exact art style of the reference image: 7 Days to Die
> post-apocalyptic UI icon, hand-painted weathered metal, chipped teal paint,
> rust, scratches, bold dark outline, strong rim light, high contrast, chunky
> readable silhouette, slightly isometric three-quarter view. Each icon must be
> readable at 64x64. Color accents: teal metal, warm amber, brass, rust, plus
> the green check, red X, red stop plaque, and blood-red moon called out above.

Use `bonus/server_assist.png` as the style reference.

## After rendering

1. Slice the sheet:

   ```powershell
   powershell -File .\tools\slice-sheet.ps1 `
     -Sheet .\icons\_sheets\vote_actions_1.png `
     -OutDir .\icons `
     -Names @('sa_vote_yes','sa_vote_no','sa_vote_cancel','sa_vote_bloodmoon')
   ```

2. Add the four names to `$preferred` in `tools/build-icons.ps1` after
   `vote_skip_night`.
3. Run `powershell -ExecutionPolicy Bypass -File .\tools\build-icons.ps1`.
4. Copy the new 128x128 PNGs into
   `AAZ_ServerAssistArt/UIAtlases/UIAtlas/` and rebuild the art mod zip.
5. After the atlas is rebuilt, update the four VoteSkipNight icon constants
   (`IconVoteYes`, `IconVoteNo`, `IconCancelVote`, `IconBloodMoon`) to
   `sprite:sa_vote_yes`, `sprite:sa_vote_no`, `sprite:sa_vote_cancel`, and
   `sprite:sa_vote_bloodmoon`. Do not switch them before the PNGs are deployed,
   or the cards will fall back to the hammer.

## Current VoteSkipNight mapping (for reference)

| Card | Sprite key |
|---|---|
| Panel / Start Vote | `sprite:sa_vote_skip_night` |
| Vote Yes | `sprite:sa_vote_yes` -> new |
| Vote No | `sprite:sa_vote_no` -> new |
| Cancel Vote | `sprite:sa_vote_cancel` -> new |
| Force Skip | `sprite:sa_act_time` |
| Current Vote | `sprite:sa_nav_moderation` |
| Your Vote | `sprite:sa_permission_gui` |
| Eligible Voters | `sprite:sa_nav_players` |
| Status / blood moon | `sprite:sa_vote_bloodmoon` -> new |
| Status / cooldown | `sprite:sa_act_warn` |
| Status / sleeping | `sprite:sa_act_freeze` |
| Status / day | `sprite:sa_nav_world` |
| Status / ready | `sprite:sa_act_godmode` |
| Refresh | `sprite:sa_plugin_hot_reload` |
