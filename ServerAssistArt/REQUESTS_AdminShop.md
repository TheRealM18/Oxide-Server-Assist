# AdminShop icon request — scope lock: 12 icons

> **Not active for the current batch.** The only current request is
> `ICON_REQUEST.md`, which asks for the 8 missing PlayerProfiles icons. Do not
> render this file's icons yet.

Status: **requested, not rendered**

This is the only AdminShop art request. Render exactly these **12** icons. Do
not render the rest of `PLANNED_ICONS.md` for this request; AdminShop has no
other icon gaps. Other plugins will make their own requests.

AdminShop 1.1.1 already uses 18 finished sprites from the pack. These 12 are the
only remaining semantic gaps where the panel borrows art that means something
else.

The panel card icon `sa_admin_shop` already exists. Do not remake it.

## Copy-ready list

```text
sa_shop_categories
sa_shop_search
sa_shop_buy
sa_shop_permissions
sa_shop_weapons
sa_shop_ammo
sa_shop_food_medical
sa_shop_resources
sa_shop_blocks
sa_shop_books
sa_act_prev
sa_act_next
```

## Requested files

| # | Master file | Sprite key | Used for | Replaces |
|---|---|---|---|---|
| 1 | `icons/sa_shop_categories.png` | `sprite:sa_shop_categories` | All Items category row | `sa_nav_items` |
| 2 | `icons/sa_shop_search.png` | `sprite:sa_shop_search` | Search button and search banner | `sa_nav_logs` |
| 3 | `icons/sa_shop_buy.png` | `sprite:sa_shop_buy` | Buy 1 / Buy 10 / Buy 100 / Buy Permission | `sa_act_give` |
| 4 | `icons/sa_shop_permissions.png` | `sprite:sa_shop_permissions` | Permissions button | `sa_permission_gui` |
| 5 | `icons/sa_shop_weapons.png` | `sprite:sa_shop_weapons` | Weapons category row | `sa_admin_kits` |
| 6 | `icons/sa_shop_ammo.png` | `sprite:sa_shop_ammo` | Ammo category row | `sa_act_airdrop` |
| 7 | `icons/sa_shop_food_medical.png` | `sprite:sa_shop_food_medical` | Food & Medical category row | `sa_act_heal` |
| 8 | `icons/sa_shop_resources.png` | `sprite:sa_shop_resources` | Resources category row | `sa_qln_pack` |
| 9 | `icons/sa_shop_blocks.png` | `sprite:sa_shop_blocks` | Blocks category row | `sa_nav_world` |
| 10 | `icons/sa_shop_books.png` | `sprite:sa_shop_books` | Books & Schematics category row | `sa_nav_logs` |
| 11 | `icons/sa_act_prev.png` | `sprite:sa_act_prev` | Back and `< Prev` buttons | `sa_nav_teleport` |
| 12 | `icons/sa_act_next.png` | `sprite:sa_act_next` | `Next >` button | `sa_nav_teleport` |

## Why each one is needed

1. **Categories** — `sa_nav_items` is a generic open crate and is also used for
   the Max Purchase row. A shop-specific market/crate-and-awning icon makes the
   All Items entry read as the shop home.
2. **Search** — `sa_nav_logs` is a logbook with a magnifier and is also used for
   the Books category. A price-tag magnifier is specific to searching a shop.
3. **Buy** — `sa_act_give` is a hand offering a parcel. A shopping basket with
   Duke's tokens and a parcel reads as "buy", not "give".
4. **Permissions** — `sa_permission_gui` is the PermissionGui panel icon and is
   also used on permission product rows. A key fused to a price tag separates
   "buy a permission" from "manage permissions".
5. **Weapons** — `sa_admin_kits` is the AdminKits panel icon (a supply crate
   with a pistol). A dedicated weapon silhouette avoids looking like a kit crate.
6. **Ammo** — `sa_act_airdrop` is a parachute supply crate. An open ammo box and
   cartridges read immediately as ammunition.
7. **Food & Medical** — `sa_act_heal` is a medkit only. This category also sells
   food, so the icon needs canned food, a drink and a small medkit together.
8. **Resources** — `sa_qln_pack` is a QLN pack/download crate and is also used for
   the Bundle row. Ore, wood and a steel ingot are clearer for resources.
9. **Blocks** — `sa_nav_world` is a globe/storm icon. A stack of building blocks
   reads as placeable blocks.
10. **Books** — `sa_nav_logs` is a logbook with a magnifier and is also used for
    Search. An open book with a rolled schematic is specific to this category.
11. **Prev** — `sa_nav_teleport` is a compass. A chunky left arrow is the correct
    navigation control for Back and previous-page buttons.
12. **Next** — mirror of Prev for the next-page button.

## Do not make

- `sa_shop_armor` — the existing `sa_act_godmode` shield is acceptable.
- `sa_shop_mods` — the existing `sa_admin_suite` wrench/gear is acceptable.
- `sa_shop_other` — the existing `sa_nav_help` question sign is acceptable.
- `sa_shop_quality` — the existing `sa_act_repair` wrench/hammer is acceptable.
- `sa_shop_bundle` — the existing `sa_qln_pack` crates are acceptable.
- `sa_shop_price`, `sa_shop_dukes` — the existing `sa_admin_shop` register is
  acceptable for price and dukes rows.
- `sa_shop_trader` — the existing `sa_auction_house` gavel/coin is acceptable.
- `sa_shop_refresh` — the existing `sa_plugin_hot_reload` is acceptable.
- `sa_shop_owned` — the existing `sa_act_godmode` shield is acceptable.
- `sa_shop_locked` — the existing `sa_act_warn` triangle is acceptable.
- Any item-row icon. Item rows use live game item icons through `item:<name>`.
- The panel card `sa_admin_shop`.
- Any other sheet in `PLANNED_ICONS.md` (kits, auction, rewards, backpack,
  permissions, hot reload, vote).

Do not edit or replace any existing PNG. These are 12 new files.

## Exact sheet layout

The slicing tool requires four names per 2x2 sheet.

### Sheet E — `icons/_sheets/shop_actions_1.png`

Already specified in `PLANNED_ICONS.md`. Slice it with:

```powershell
powershell -File .\tools\slice-sheet.ps1 `
  -Sheet .\icons\_sheets\shop_actions_1.png `
  -OutDir .\icons `
  -Names @('sa_shop_categories','sa_shop_search','sa_shop_buy','sa_shop_permissions')
```

### Sheet A — `icons/_sheets/shop_categories_1.png`

| Quadrant | Sprite |
|---|---|
| top-left | `sa_shop_weapons` |
| top-right | `sa_shop_ammo` |
| bottom-left | `sa_shop_food_medical` |
| bottom-right | `sa_shop_resources` |

```powershell
powershell -File .\tools\slice-sheet.ps1 `
  -Sheet .\icons\_sheets\shop_categories_1.png `
  -OutDir .\icons `
  -Names @('sa_shop_weapons','sa_shop_ammo','sa_shop_food_medical','sa_shop_resources')
```

### Sheet B — `icons/_sheets/shop_categories_2.png`

| Quadrant | Sprite |
|---|---|
| top-left | `sa_shop_blocks` |
| top-right | `sa_shop_books` |
| bottom-left | `sa_act_prev` |
| bottom-right | `sa_act_next` |

```powershell
powershell -File .\tools\slice-sheet.ps1 `
  -Sheet .\icons\_sheets\shop_categories_2.png `
  -OutDir .\icons `
  -Names @('sa_shop_blocks','sa_shop_books','sa_act_prev','sa_act_next')
```

Standalone masters are also acceptable if a sheet workflow is not practical.

## Subject briefs

### 1. sa_shop_categories

Three weathered market crates under a small striped awning. A couple of blank
price tags and a few brass Duke's tokens. The awning and crate shapes must read
at 24x24; keep the stripes chunky.

### 2. sa_shop_search

Magnifying glass over a blank metal price tag, with a small brass coin and a
chipped teal frame. The magnifier and tag silhouette should carry the read at
24x24.

### 3. sa_shop_buy

Wire shopping basket filled with Duke's tokens and one small wrapped parcel. A
blank price tag hangs from the handle. Avoid hands and open palms; the basket is
the buy symbol.

### 4. sa_shop_permissions

Brass key fused to a blank price tag, with a small riveted ID badge or shield
accent. It should read as "permission for sale", not as the PermissionGui panel
card.

### 5. sa_shop_weapons

Compact pistol crossed with a combat knife, one brass cartridge, weathered metal
backing. Strong, chunky silhouettes; no hands.

### 6. sa_shop_ammo

Open ammo box with brass cartridges and one loose bullet, plus a small linked-belt
detail. The box and bullet must read at 24x24.

### 7. sa_shop_food_medical

Canned food, a water bottle and a small medkit with a red cross arranged as one
tight group. Balance food and medical; do not make it only a medkit.

### 8. sa_shop_resources

Pile of ore chunks, a wood plank and a forged steel ingot, with a small pickaxe
accent. Keep the ore, plank and ingot distinct at 24x24.

### 9. sa_shop_blocks

Stack of weathered building blocks: concrete, wood and cobblestone, with a small
trowel. The block stack is the main read; the trowel is secondary.

### 10. sa_shop_books

Open weathered book with a rolled blueprint or schematic, small brass clasp. No
magnifying glass; that belongs to Search.

### 11. sa_act_prev

Chunky weathered metal left arrow, chipped teal paint, rust, one brass rivet and
a bold dark outline. Simple enough to read at 24x24.

### 12. sa_act_next

Mirror of `sa_act_prev`: chunky weathered metal right arrow, same materials,
outline and rivet.

## Shared style

Match the existing pack exactly. Use `bonus/server_assist.png` as the style
reference.

> 7 Days to Die post-apocalyptic UI icon, hand-painted weathered metal, chipped
> teal paint, rust, scratches, bold dark outline, strong rim light, high
> contrast, chunky readable silhouette, slightly isometric three-quarter view,
> centered, no text, no letters, fully transparent background, square, readable
> at 64x64 and at 24x24.

Color accents: teal metal, warm amber, brass, rust. The art is already colored,
so no tint is applied by AdminShop for these icons.

## Format and delivery

- Masters: at least 626x626 per icon if using a 2x2 sheet, or 1024x1024 as
  standalone masters. Transparent 32-bit ARGB PNG.
- Output: run `tools/build-icons.ps1`. It trims, centers, and writes 128x128
  PNGs to `UIAtlases/UIAtlas/` plus 256x256 previews.
- Add the 12 names to the `$preferred` list in `tools/build-icons.ps1` if they
  are not already present.
- Copy the 12 generated PNGs into the shipped
  `AAZ_ServerAssistArt/UIAtlases/UIAtlas/` folder.
- Rebuild `AAZ_ServerAssistArt-<version>.zip`, update the pack manifest and its
  SHA-256, and upload it as a new art-pack version.
- Do not edit or replace any existing PNG.

## Swap plan after delivery

AdminShop keeps the current placeholders until the new PNGs are shipped. When
the pack is updated, the icon constants change as follows:

| Constant / use | Current | New |
|---|---|---|
| All Items category row | `sprite:sa_nav_items` | `sprite:sa_shop_categories` |
| Max Purchase row | `sprite:sa_nav_items` | unchanged |
| Search button / banner | `sprite:sa_nav_logs` | `sprite:sa_shop_search` |
| Buy buttons | `sprite:sa_act_give` | `sprite:sa_shop_buy` |
| Permissions button | `sprite:sa_permission_gui` | `sprite:sa_shop_permissions` |
| Permission product rows | `sprite:sa_permission_gui` | unchanged |
| Weapons category | `sprite:sa_admin_kits` | `sprite:sa_shop_weapons` |
| Ammo category | `sprite:sa_act_airdrop` | `sprite:sa_shop_ammo` |
| Food & Medical category | `sprite:sa_act_heal` | `sprite:sa_shop_food_medical` |
| Resources category | `sprite:sa_qln_pack` | `sprite:sa_shop_resources` |
| Bundle row | `sprite:sa_qln_pack` | unchanged |
| Blocks category | `sprite:sa_nav_world` | `sprite:sa_shop_blocks` |
| Books category | `sprite:sa_nav_logs` | `sprite:sa_shop_books` |
| Back / Prev | `sprite:sa_nav_teleport` | `sprite:sa_act_prev` |
| Next | `sprite:sa_nav_teleport` | `sprite:sa_act_next` |

The 12 keys above are the complete request.
