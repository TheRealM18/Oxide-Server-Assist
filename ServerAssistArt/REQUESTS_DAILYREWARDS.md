# DailyRewards icon request

> **Not active for the current batch.** The only current request is
> `ICON_REQUEST.md`, which asks for the 8 missing PlayerProfiles icons. Do not
> render this file's icons yet.

Status: **requested, not rendered**

This request is intentionally limited to the three icons the DailyRewards UI
still needs. Do not render the full planned rewards batch.

Current UI references are in
`../DailyRewards/oxide/plugins/DailyRewards.cs` and the mapping is documented in
`../DailyRewards/ICON_MAP.md`.

## Make only these three icons

### 1. `sa_rewards_weekly`

- Use: Weekly reward tier row in the DailyRewards panel.
- Current placeholder: `sprite:sa_act_time` (generic alarm clock).
- Subject: A weathered metal calendar page with seven tally marks and a small
  star or check mark. The seven marks should read as a week at a glance.
- Readability: Must still read at 64x64. Avoid tiny numbers or lettering.
- Suggested accent: teal metal with warm amber marks.

### 2. `sa_rewards_monthly`

- Use: Monthly reward tier row in the DailyRewards panel.
- Current placeholder: `sprite:sa_nav_snapshots` (camera and rewind clock, not a
  calendar).
- Subject: A weathered metal calendar page with a crescent moon and a star, or
  a calendar grid page with a moon emblem. It should read as a monthly cycle,
  not as a camera or history icon.
- Readability: The calendar silhouette should stay clear at 64x64.
- Suggested accent: teal metal with amber moon/star highlights.

### 3. `sa_rewards_seasonal`

- Use: Seasonal reward tier row in the DailyRewards panel, and optionally the
  season settings row.
- Current placeholder: `sprite:sa_act_weather` (storm cloud, not a season).
- Subject: A weathered metal calendar page with a simple four-season motif:
  a small sun, a leaf, and a snowflake, or a split sun/snowflake emblem. Keep it
  to one clear silhouette, not three separate icons.
- Readability: The seasonal emblem must read at 64x64.
- Suggested accent: teal metal with amber sun and a cool blue snowflake/leaf.

## Do not make these

These already have good-enough existing art or are not used by the current UI:

- `sa_rewards_daily` - `sprite:sa_daily_rewards` already covers both the card and
  the daily tier.
- `sa_rewards_claim` - Server Assist action buttons do not support per-button
  icons, and `sprite:sa_act_give` already covers the grant action.
- `sa_rewards_streak` - `sprite:sa_nav_logs` is acceptable for the streak row.
- `sa_rewards_reset` - `sprite:sa_nav_snapshots` is acceptable after monthly gets
  its own icon.
- `sa_rewards_playtime` - `sprite:sa_act_time` is already exact for playtime.
- `sa_rewards_admin` - `sprite:sa_nav_moderation` fits Admin Tools.
- `sa_rewards_permissions` - `sprite:sa_permission_gui` already fits permissions.
- `sa_rewards_commands` - `sprite:sa_nav_server` already fits server commands.
- `sa_rewards_items` - reward rows use live `item:<itemName>` icons, with
  `sprite:sa_nav_items` as the fallback.
- `sa_rewards_locked` - `sprite:sa_act_warn` already fits locked/error states.
- `sa_rewards_online` - `sprite:sa_nav_players` already fits online players.

## Technical requirements

- 128x128, 32-bit ARGB PNG with a transparent background.
- Same style as the existing `sa_*` set: hand-painted weathered metal, chipped
  teal paint, rust, scratches, bold dark outline, strong rim light, high
  contrast, chunky readable silhouette, slightly isometric three-quarter view.
- Reference style: `bonus/server_assist.png` and the existing `icons/sa_*.png`.
- Auto-trim and re-center with the existing `tools/build-icons.ps1` pipeline.
- No text, letters, numbers, grid lines, borders, or background panels.
- Exact sprite keys: `sa_rewards_weekly`, `sa_rewards_monthly`,
  `sa_rewards_seasonal`. No `.png` in the sprite key.
- Add the masters to `icons/`, run `tools/build-icons.ps1`, and confirm the
  generated 128x128 files land in `UIAtlases/UIAtlas/`.

## Acceptance check

- All three PNGs exist in `UIAtlases/UIAtlas/`.
- Each icon is readable at 64x64.
- No extra reward icons were added.
- `ServerAssistArt/README.md` and the contact sheet can be updated by the art
  agent if that is part of its normal pipeline.

## What happens after the icons arrive

DailyRewards will switch these config defaults:

- `Weekly.Icon` -> `sprite:sa_rewards_weekly`
- `Monthly.Icon` -> `sprite:sa_rewards_monthly`
- `Seasonal.Icon` -> `sprite:sa_rewards_seasonal`

The existing fallback icons remain in the config until the new PNGs are
installed on the server and every client.
