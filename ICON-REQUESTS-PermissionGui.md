# PermissionGui icon requests

Status: **request only — do not wire these into the plugin until the PNGs exist.**
Target art pack: `ServerAssistArt` / `AAZ_ServerAssistArt`
Naming prefix: `sa_perm_`

## Why these are needed

PermissionGui currently reuses unrelated nav/action sprites as placeholders:

| Current placeholder | Used for |
|---|---|
| `sa_nav_server` | Oxide permission category + Defaults page |
| `sa_nav_world` | 7 Days to Die permission category (both command types) |
| `sa_plugin_hot_reload` | Generic plugin permission category + Reload Config |
| `sa_nav_players` | Groups and Users pages/rows |
| `sa_nav_warps` | Tracks page/rows |

The six icons below replace the placeholder reuse that actually matters. Nothing
else is needed for the current PermissionGui UI.

## Required icons — 6 total

Priority: icons 1–3 first (permission rows); icons 4–6 second (page rows).

### 1. `sa_perm_oxide`

- Used on permission rows where the node starts with `oxide.`
- Also the Oxide command category when no permission node exists
- Concept: Oxide/server core plus a permission key or badge
- Replaces: `sprite:sa_nav_server` on permission rows

### 2. `sa_perm_game`

- Used on permission rows where the node starts with `7daystodie.` or `game.`
- Used for **both** 7 Days to Die command types:
  - `consolecmd` — SdtdConsole / mod console commands
  - `configcmd` — serveradmin.xml command permissions
- Concept: 7 Days to Die world/game mark plus a command or key motif
- Replaces: `sprite:sa_nav_world` on permission rows and command rows

### 3. `sa_perm_plugin`

- Used for every registered plugin permission that is not Oxide or 7DTD
- Concept: puzzle piece or plugin chip plus a permission key/badge
- Replaces: `sprite:sa_plugin_hot_reload` on permission rows

### 4. `sa_perm_groups`

- Used on the Groups page and group rows
- Concept: several riveted ID badges or a small cluster of people
- Replaces: `sprite:sa_nav_players` on group rows

### 5. `sa_perm_users`

- Used on the Users page and user rows
- Concept: one ID badge with a person silhouette, or a single dog tag
- Replaces: `sprite:sa_nav_players` on user rows

### 6. `sa_perm_tracks`

- Used on the Tracks page and track rows
- Concept: connected metal nodes or a ladder with arrows showing progression
- Replaces: `sprite:sa_nav_warps` on track rows

## Do NOT make these

These are already covered or not used, so they would be wasted work:

- `sa_perm_browser` — Permission Search already uses `sa_permission_gui`, which
  reads as a key/badge and is appropriate.
- `sa_perm_defaults` — Defaults already uses `sa_nav_server`, which reads as
  server configuration.
- `sa_perm_temp` — the Temp Perms button is text-only; temp rows use the
  permission category icons.
- The old planned names `sa_permissions_groups`, `sa_permissions_users`,
  `sa_permissions_browser`, `sa_permissions_tracks` — superseded by the
  `sa_perm_*` names above.
- Any `sa_kits_*`, `sa_shop_*`, `sa_auction_*`, `sa_rewards_*`,
  `sa_backpack_*`, `sa_hotreload_*` or `sa_vote_*` icons — not part of
  PermissionGui.

## Art/package requirements

- The three category masters `sa_perm_oxide`, `sa_perm_game` and `sa_perm_plugin`
  currently exist as byte-identical aliases of other sprites. Replace those
  alias files with original dedicated art; keep the exact names.
- `sa_perm_groups`, `sa_perm_users` and `sa_perm_tracks` are new files.
- 128×128, 32-bit ARGB PNG, transparent background
- Same hand-painted 7 Days to Die style as the existing `sa_*` set; use
  `bonus/server_assist.png` as the style reference
- Readable at 64×64 with the label still carrying the meaning
- Add masters to `ServerAssistArt/icons/` using the exact names above
- Run `ServerAssistArt/tools/build-icons.ps1`
- Include the generated PNGs in the next `AAZ_ServerAssistArt` package
- Do not rename or remove the existing `sa_nav_*`, `sa_*` or placeholder files

## After the icons exist

The plugin mappings to switch are:

- `PermissionTypeIcon`:
  - `oxide.*` → `sprite:sa_perm_oxide`
  - `7daystodie.*` / `game.*` → `sprite:sa_perm_game`
  - everything else → `sprite:sa_perm_plugin`
- `UiIcon`:
  - `perm|cmd|oxidecmd` → `sprite:sa_perm_oxide`
  - `perm|cmd|consolecmd` / `perm|cmd|configcmd` → `sprite:sa_perm_game`
  - `perm|select` → `sprite:sa_perm_plugin`
  - group rows → `sprite:sa_perm_groups`
  - user rows → `sprite:sa_perm_users`
  - track rows → `sprite:sa_perm_tracks`

Keep the code on the current working sprites until the new PNGs are present.
