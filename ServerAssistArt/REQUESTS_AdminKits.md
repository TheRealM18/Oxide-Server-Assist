# AdminKits icon request — scope lock: 7 icons

> **Not active for the current batch.** The only current request is
> `ICON_REQUEST.md`, which asks for the 8 missing PlayerProfiles icons. Do not
> render this file's icons yet.

Status: **requested, not rendered**

This is the only AdminKits art request. Render exactly these seven icons. Do
not render the twelve kit-category icons or any other icon from
`PLANNED_ICONS.md`; AdminKits has no category UI and will not use them.

AdminKits 1.1.0 already uses 17 finished sprites from this pack. These seven are
the only remaining semantic gaps where the panel currently borrows art that
means something else.

## Requested files

| # | Master file | Sprite key | Used for | Replaces |
|---|---|---|---|---|
| 1 | `icons/sa_kits_collect.png` | `sprite:sa_kits_collect` | Collect a Kit row and Collect button | `sa_nav_items` |
| 2 | `icons/sa_kits_manage.png` | `sprite:sa_kits_manage` | Manage Kits row | `sa_nav_logs` |
| 3 | `icons/sa_kits_create.png` | `sprite:sa_kits_create` | Create Kit row and button | `sa_admin_kits` |
| 4 | `icons/sa_kits_from_inventory.png` | `sprite:sa_kits_from_inventory` | Create From Inventory row and button | `sa_dynamic_backpack` |
| 5 | `icons/sa_kits_edit.png` | `sprite:sa_kits_edit` | Edit button and kit editor banner | `sa_act_repair` |
| 6 | `icons/sa_kits_delete.png` | `sprite:sa_kits_delete` | Delete, Clear Items, Remove and confirmation buttons | `sa_act_ban` |
| 7 | `icons/sa_kits_limits.png` | `sprite:sa_kits_limits` | Limits button, Limits row and Limits screen banner | `sa_act_warn` |

Copy-ready list:

```text
sa_kits_collect
sa_kits_manage
sa_kits_create
sa_kits_from_inventory
sa_kits_edit
sa_kits_delete
sa_kits_limits
```

## Why each one is needed

1. **Collect** — the panel, normal kit state and kit detail all use the open
   crate. Collect currently uses `sa_nav_items`, so it reads as "items", not
   "take this kit". A crate with a downward arrow/hands separates the action.
2. **Manage** — `sa_nav_logs` is a logbook and reads as audit logs. A clipboard
   with a crate and checklist clearly means managing kit definitions.
3. **Create** — `sa_admin_kits` is the panel icon. A crate with a plus sign is
   the standard "new" action and stops Create from looking like Open.
4. **From Inventory** — `sa_dynamic_backpack` is the DynamicBackpack plugin's
   card icon. Reusing it inside AdminKits suggests opening the backpack rather
   than copying the inventory into a kit.
5. **Edit** — `sa_act_repair` is a repair action. A crate with a wrench/gear and
   pencil clearly means editing the kit definition.
6. **Delete** — `sa_act_ban` is a moderation ban hammer. A crate with a red X or
   scrap stamp is unambiguous for deleting or clearing kit contents.
7. **Limits** — `sa_act_warn` is a generic warning and is also used for the
   "limit reached" state. A gauge/counter/padlock icon distinguishes configuring
   limits from the warning state.

## Do not make

- The twelve kit category icons in `PLANNED_ICONS.md` Sheets B, C and D:
  `sa_kit_starter`, `sa_kit_builder`, `sa_kit_medic`, `sa_kit_food`,
  `sa_kit_weapons`, `sa_kit_tools`, `sa_kit_farming`, `sa_kit_vehicle`,
  `sa_kit_treasure`, `sa_kit_admin`, `sa_kit_pvp`, `sa_kit_event`.
  AdminKits has no categories.
- Any Shop, Auction, Rewards, Backpack, PermissionGui, Hot Reload or Vote icon.
- Replacements for the panel card (`sa_admin_kits`), normal kit state,
  permission (`sa_permission_gui`), cooldown (`sa_act_time`), claims
  (`sa_daily_rewards`), per-player (`sa_nav_players`), hidden/show
  (`sa_act_spectate`), give/add (`sa_act_give`), refresh
  (`sa_plugin_hot_reload`), help (`sa_nav_help`) or item rows (`item:<name>`).
  Those already fit and are not part of this request.
- Do not fill the fourth quadrant of Sheet B with an extra icon. Leave it fully
  transparent.

## Subject briefs

### 1. sa_kits_collect

Open military supply crate seen in three-quarter view. The lid is up. A bold
amber downward arrow points into the crate, and two gloved hands reach in to
lift out a can or medkit. The silhouette must still read at 24x24: keep the
arrow and crate rim thick and simple.

### 2. sa_kits_manage

Riveted metal clipboard with a small supply crate stamped on it, a gear at one
corner and three short checklist lines. Optional pencil or stub of chalk. The
crate shape and gear should carry the read at small size; do not rely on the
checklist lines.

### 3. sa_kits_create

Closed or half-open metal supply crate with a bold plus sign on the lid. Amber
or warm brass plus against chipped teal metal. Strong, centered silhouette. No
text.

### 4. sa_kits_from_inventory

Rugged canvas survival backpack tipped forward, spilling a can, bandage and
small ammo box into an open supply crate. Keep the backpack strap and crate lip
thick so the transfer reads at 24x24.

### 5. sa_kits_edit

Supply crate with an adjustable wrench or gear crossed by a chipped pencil or
small screwdriver. It should read as "configure/edit the kit", not as the plain
repair wrench used by `sa_act_repair`. Keep the tool shapes chunky.

### 6. sa_kits_delete

Supply crate with a bold red X stamp or prohibition mark across the lid, plus a
small scrap or crushed-can detail. It must read as "delete this kit", not as a
ban hammer. Avoid gavels and moderation symbols.

### 7. sa_kits_limits

Riveted metal gauge or counter mounted on a supply crate, with a small padlock
or stop marker. Conveys total/per-player limits without using a warning
triangle. The gauge needle or padlock should remain readable at 24x24.

## Exact sheet layout

The slicing tool requires four names per 2x2 sheet.

### Sheet A — `icons/_sheets/kits_actions_1.png`

| Quadrant | Sprite |
|---|---|
| top-left | `sa_kits_collect` |
| top-right | `sa_kits_manage` |
| bottom-left | `sa_kits_create` |
| bottom-right | `sa_kits_from_inventory` |

Slice:

```powershell
powershell -File .\tools\slice-sheet.ps1 `
  -Sheet .\icons\_sheets\kits_actions_1.png `
  -OutDir .\icons `
  -Names @('sa_kits_collect','sa_kits_manage','sa_kits_create','sa_kits_from_inventory')
```

### Sheet B — `icons/_sheets/kits_actions_2.png`

| Quadrant | Sprite |
|---|---|
| top-left | `sa_kits_edit` |
| top-right | `sa_kits_delete` |
| bottom-left | `sa_kits_limits` |
| bottom-right | **leave fully transparent, no icon** |

Slice with a temporary placeholder, then delete the placeholder master so it
never reaches the atlas:

```powershell
powershell -File .\tools\slice-sheet.ps1 `
  -Sheet .\icons\_sheets\kits_actions_2.png `
  -OutDir .\icons `
  -Names @('sa_kits_edit','sa_kits_delete','sa_kits_limits','_unused_kits_4')

Remove-Item .\icons\_unused_kits_4.png
```

Three standalone masters are also acceptable, but use the sheet workflow above
if it is easier.

## Shared style

Match the existing pack exactly. Use `bonus/server_assist.png` as the style
reference.

> 7 Days to Die post-apocalyptic UI icon, hand-painted weathered metal, chipped
> teal paint, rust, scratches, bold dark outline, strong rim light, high
> contrast, chunky readable silhouette, slightly isometric three-quarter view,
> centered, no text, no letters, fully transparent background, square, readable
> at 64x64 and at 24x24.

Color accents: teal metal, warm amber, brass, rust. The art is already colored,
so no tint is applied by AdminKits for these seven icons.

## Format and delivery

- Masters: at least 626x626 per icon if using a 2x2 sheet, or 1024x1024 as
  standalone masters. Transparent 32-bit ARGB PNG.
- Output: run `tools/build-icons.ps1`. It trims, centers, and writes 128x128
  PNGs to `UIAtlases/UIAtlas/` plus 256x256 previews.
- The seven names are already pre-added to `$preferred` in
  `tools/build-icons.ps1`, so the contact sheet order is ready. No edit is
  needed there.
- Copy the seven generated PNGs into the shipped
  `AAZ_ServerAssistArt/UIAtlases/UIAtlas/` folder.
- Rebuild `AAZ_ServerAssistArt-<version>.zip`, update the pack manifest and its
  SHA-256, and upload it as a new art-pack version.
- Do not edit or replace any existing PNG. These are seven new files.

## Swap plan after delivery

AdminKits will keep using the current icons until the new PNGs are shipped.
When the pack is updated, the `KitIcons` constants change as follows:

| Constant | Current | New |
|---|---|---|
| `Collect` | `sprite:sa_nav_items` | `sprite:sa_kits_collect` |
| `Items` | `sprite:sa_nav_items` | unchanged |
| `Manage` | `sprite:sa_nav_logs` | `sprite:sa_kits_manage` |
| `Create` | `sprite:sa_admin_kits` | `sprite:sa_kits_create` |
| `FromInventory` | `sprite:sa_dynamic_backpack` | `sprite:sa_kits_from_inventory` |
| `Edit` | `sprite:sa_act_repair` | `sprite:sa_kits_edit` |
| `Delete` | `sprite:sa_act_ban` | `sprite:sa_kits_delete` |
| `Limits` (new constant) | `sprite:sa_act_warn` | `sprite:sa_kits_limits` |
| `Warning` | `sprite:sa_act_warn` | unchanged for the LIMIT state and confirmations |

The `Collect` split matters: the new collect icon is for the action only, while
the generic item list keeps `sa_nav_items`. The `Limits` split matters: the new
limits icon is for configuring limits, while the warning icon stays on the
"limit reached" state and destructive confirmations.
