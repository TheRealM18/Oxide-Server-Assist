# Planned plugin menu icon batch

> **Superseded by the per-plugin requests and `ICON_PRIORITY.md`.** Use
> `REQUESTS_AdminKits.md`, `REQUESTS_AdminShop.md`,
> `REQUESTS_AUCTIONHOUSE.md`, `REQUESTS_DAILYREWARDS.md`,
> `REQUESTS_AdminSuite.md`, `VOTESKIPNIGHT_ICON_REQUEST.md` and
> `ICON-REQUESTS-PermissionGui.md` for the current scope. Many sheets in this
> file are canceled (kit categories, backpack, hot reload).

> **Not active for the current batch.** The only current request is
> `ICON_REQUEST.md`, which asks for the 8 missing PlayerProfiles icons. Ignore
> this file until those are installed.

> **DailyRewards scope note:** see `REQUESTS_DAILYREWARDS.md`. For DailyRewards,
> only `sa_rewards_weekly`, `sa_rewards_monthly`, and `sa_rewards_seasonal` are
> needed. Do not render `sa_rewards_daily` or `sa_rewards_claim` for that plugin;
> existing art covers those UI elements.

Status: **not rendered yet** — the image account returned
`Insufficient account funds` on the first test sheet.

> **Current PluginHotReload check:** The Server Assist `Plugin Hot Reload` panel
> needs **no new icons**. Its card and rows use existing sprites. Do **not**
> render Sheet J or the other sheets for this UI; current action buttons are
> text-only. See
> [REQUESTED_PLUGIN_HOT_RELOAD_ICONS.md](REQUESTED_PLUGIN_HOT_RELOAD_ICONS.md).

> For the current AdminShop-only request, see `REQUESTS_AdminShop.md`. Do not
> render this full batch for that request; it lists the only 12 icons AdminShop
> needs.

> For the current AdminKits request, see `REQUESTS_AdminKits.md`. Only the
> seven icons in that file are requested. Do not render the twelve kit-category
> icons in Sheets B, C and D; AdminKits has no category UI.

AdminKits 1.1.0 already ships with a complete icon layout built from the
finished `sa_nav_*`, `sa_act_*` and plugin-card art. The mapping is in
`AdminKits/ICONS.md`. The seven `sa_kits_*` action icons in the request file
replace the spots where AdminKits currently borrows art from another meaning.

This batch covers the remaining eight plugins. The kit-category library in
Sheets B, C and D is not part of the current AdminKits request; see
`REQUESTS_AdminKits.md` for the seven icons that are actually needed. The full
batch is designed as eleven 2x2 sheets (44 icons) only if every planned icon is
ever wanted. When credits are available, generate each requested sheet into
`icons/_sheets/`, slice it with `tools/slice-sheet.ps1`, then run
`tools/build-icons.ps1`.

## Shared sheet prompt

> Four separate game UI icons arranged in a clean 2x2 grid on a fully
> transparent background, each icon centered in its own quadrant with generous
> empty space between them, no grid lines, no borders, no text, no letters.
> Match the exact art style of the reference image: 7 Days to Die
> post-apocalyptic UI icon, hand-painted weathered metal, chipped teal paint,
> rust, scratches, bold dark outline, strong rim light, high contrast, chunky
> readable silhouette, slightly isometric three-quarter view. Each icon must be
> readable at 64x64. Color accents: teal metal, warm amber, brass, rust.

Use `bonus/server_assist.png` as the style reference.

---

## Sheet A — AdminKits actions

File: `icons/_sheets/kits_actions_1.png`

| Sprite | Subject |
|---|---|
| `sa_kits_collect` | Open supply crate with a downward amber arrow and two gloved hands |
| `sa_kits_manage` | Metal clipboard with a small crate, gear and checklist |
| `sa_kits_create` | Metal supply crate with a bold plus sign on the lid |
| `sa_kits_from_inventory` | Rugged backpack tipping items into an open crate |

## Sheet B — Kit categories 1

File: `icons/_sheets/kits_categories_1.png`

| Sprite | Subject |
|---|---|
| `sa_kit_starter` | Basic wooden crate with a small amber star |
| `sa_kit_builder` | Hammer, nails and stacked planks |
| `sa_kit_medic` | Military medkit with a red cross and bandage |
| `sa_kit_food` | Canned food, water bottle and spoon |

## Sheet C — Kit categories 2

File: `icons/_sheets/kits_categories_2.png`

| Sprite | Subject |
|---|---|
| `sa_kit_weapons` | Pistol, rifle round and small ammo box |
| `sa_kit_tools` | Pickaxe crossed with a wrench |
| `sa_kit_farming` | Hoe, corn cob and wheat bundle |
| `sa_kit_vehicle` | Vehicle wheel with an engine part and wrench |

## Sheet D — Kit categories 3

File: `icons/_sheets/kits_categories_3.png`

| Sprite | Subject |
|---|---|
| `sa_kit_treasure` | Treasure chest with gold coins and a gem |
| `sa_kit_admin` | Shield with a star and a small key |
| `sa_kit_pvp` | Crossed machete and pistol with a target ring |
| `sa_kit_event` | Gift crate with a star burst and ribbon |

## Sheet E — AdminShop

File: `icons/_sheets/shop_actions_1.png`

| Sprite | Subject |
|---|---|
| `sa_shop_categories` | Three market crates with a small awning |
| `sa_shop_search` | Magnifying glass over a blank price tag |
| `sa_shop_buy` | Wire shopping basket with Duke's tokens |
| `sa_shop_permissions` | Brass key fused to a blank price tag |

## Sheet F — AuctionHouse

Status: **requested** by AuctionHouse. Render this sheet only; the work order is
`REQUESTS_AUCTIONHOUSE.md`.

File: `icons/_sheets/auction_actions_1.png`

| Sprite | Subject |
|---|---|
| `sa_auction_browse` | Magnifying glass over a shipping crate |
| `sa_auction_mine` | Clipboard with a small crate and list lines |
| `sa_auction_sell` | Gavel resting on a blank price tag and coin |
| `sa_auction_cancel` | Crate with a red X stamp and a recall arrow |

## Sheet G — DailyRewards

File: `icons/_sheets/rewards_actions_1.png`

| Sprite | Subject |
|---|---|
| `sa_rewards_daily` | Sun over a small calendar page |
| `sa_rewards_weekly` | Calendar page with a star and seven tally marks |
| `sa_rewards_monthly` | Calendar page with a crescent moon and star |
| `sa_rewards_claim` | Gift box with a bold green check mark |

## Sheet H — DynamicBackpack

File: `icons/_sheets/backpack_actions_1.png`

| Sprite | Subject |
|---|---|
| `sa_backpack_page` | Backpack with three stacked page tabs |
| `sa_backpack_prev` | Left arrow with a page corner |
| `sa_backpack_next` | Right arrow with a page corner |
| `sa_backpack_locked` | Padlock on a canvas pouch |

## Sheet I — PermissionGui

File: `icons/_sheets/permissions_actions_1.png`

| Sprite | Subject |
|---|---|
| `sa_permissions_groups` | Three riveted ID badges fanned out |
| `sa_permissions_users` | Single ID badge with a person silhouette |
| `sa_permissions_browser` | Magnifying glass over a ring of brass keys |
| `sa_permissions_tracks` | Three connected metal nodes with arrows |

## Sheet J — PluginHotReload

File: `icons/_sheets/hotreload_actions_1.png`

| Sprite | Subject |
|---|---|
| `sa_hotreload_reload` | Microchip with a thick circular reload arrow |
| `sa_hotreload_scan` | Radar sweep cone over a microchip |
| `sa_hotreload_watch` | Eye inside a radar ring over a file page |
| `sa_hotreload_status` | Gauge with amber and teal status lights |

## Sheet K — VoteSkipNight (revised 2026-11-08)

File: `icons/_sheets/vote_actions_1.png`

Only these four are needed. The earlier `sa_vote_start` and `sa_vote_night`
draft entries were dropped because `sa_vote_skip_night` already covers the
panel, Start Vote, and night states.

| Sprite | Subject |
|---|---|
| `sa_vote_yes` | Blank voting slip with a bold green check mark |
| `sa_vote_no` | Blank voting slip with a bold red X |
| `sa_vote_cancel` | Ballot box with a red stop plaque and a gloved hand pulling the slip out |
| `sa_vote_bloodmoon` | Blood-red crescent moon over a metal ballot box with claw scratches |

See `VOTESKIPNIGHT_ICON_REQUEST.md` for the full render brief and the explicit
"do not make" list.

---

## After rendering

1. Slice each sheet:

   ```powershell
   powershell -File .\tools\slice-sheet.ps1 `
     -Sheet .\icons\_sheets\kits_actions_1.png `
     -OutDir .\icons `
     -Names @('sa_kits_collect','sa_kits_manage','sa_kits_create','sa_kits_from_inventory')
   ```

2. Add the new names to `$preferred` in `tools/build-icons.ps1`.
3. Run `tools/build-icons.ps1`.
4. Copy the new atlas PNGs into `AAZ_ServerAssistArt/UIAtlases/UIAtlas/`.
5. Rebuild `AAZ_ServerAssistArt-1.1.1.zip`, update the manifest entry and
   upload it as a new version of the existing Drive file.
