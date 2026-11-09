# Requested icon batch: AuctionHouse

> **Not active for the current batch.** The only current request is
> `ICON_REQUEST.md`, which asks for the 8 missing PlayerProfiles icons. Do not
> render this file's icons yet.

Status: **requested, not rendered**

This is a work order for the ServerAssistArt agent. Render **only the four icons
in this document**. Do not render the other batches listed in
`PLANNED_ICONS.md`.

These are the existing **Sheet F — AuctionHouse** icons from
`PLANNED_ICONS.md`. This request is to finish that one sheet, nothing else.

---

## 1. Scope

Exactly four new sprite masters:

| # | Sprite key | Subject | Priority |
|---|---|---|---|
| 1 | `sa_auction_browse` | Magnifying glass over a shipping crate | required |
| 2 | `sa_auction_mine` | Clipboard with a small crate and list lines | required |
| 3 | `sa_auction_sell` | Gavel resting on a blank price tag and coin | required |
| 4 | `sa_auction_cancel` | Crate with a red X stamp and a recall arrow | required |

No other AuctionHouse icons are requested.

---

## 2. Where each icon is used

AuctionHouse already has the wiring points. These four replace generic icons
that are currently borrowed from other plugin screens:

| Sprite key | Used for | Currently borrowed |
|---|---|---|
| `sa_auction_browse` | Browse banner, Browse button, empty Browse card | `sprite:sa_nav_items` |
| `sa_auction_mine` | My Listings banner, My Listings button, empty My Listings card | `sprite:sa_nav_logs` |
| `sa_auction_sell` | Sell banner, Sell button, List Item action card | `sprite:sa_act_give` |
| `sa_auction_cancel` | Cancel Listing action card | `sprite:sa_act_ban` |

These existing icons stay as-is and must **not** be replaced or redrawn:

- `sa_auction_house` — Auction House provider card, background and detail banner
- `sa_admin_shop` — price, fee, tax and Buy Now rows; balance uses the
  `item:<currency>` icon with `sa_admin_shop` as its fallback
- `sa_dynamic_backpack` — quantity and held-item rows
- `sa_act_time` — duration and expiry rows
- `sa_act_warn` — expired/broken state
- `sa_nav_players` — seller row
- `sa_plugin_hot_reload` — Refresh button
- `sa_nav_server` — SA Home button
- `sa_act_give` — still used elsewhere; do not alter it
- `sa_act_ban` — still used elsewhere; do not alter it

Item rows use real game item icons (`item:<itemName>`), so no new art is needed
for item cards.

---

## 3. Sheet layout and style

Generate one 2x2 sheet:

```text
icons/_sheets/auction_actions_1.png
```

Quadrant order:

```text
top-left:      sa_auction_browse
top-right:     sa_auction_mine
bottom-left:   sa_auction_sell
bottom-right:  sa_auction_cancel
```

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

Do not add text, numbers, logos, UI frames or drop shadows that extend into the
neighboring quadrant.

---

## 4. Output requirements

After rendering the sheet:

1. Slice it:

   ```powershell
   powershell -File .\tools\slice-sheet.ps1 `
     -Sheet .\icons\_sheets\auction_actions_1.png `
     -OutDir .\icons `
     -Names @('sa_auction_browse','sa_auction_mine','sa_auction_sell','sa_auction_cancel')
   ```

2. Add the four names to `$preferred` in `tools/build-icons.ps1`.
3. Run `tools/build-icons.ps1`.
4. Confirm the generated files exist in `UIAtlases/UIAtlas/`:

   ```text
   sa_auction_browse.png
   sa_auction_mine.png
   sa_auction_sell.png
   sa_auction_cancel.png
   ```

5. Update `preview/` and `contact-sheet.png` through the normal build script.
6. Package the new atlas as the next `AAZ_ServerAssistArt` version.

Required technical format:

- 128x128, 32-bit ARGB PNG
- fully transparent background
- auto-trimmed and re-centered with the normal 4 px margin
- readable at 64x64 in the Server Assist home/card layout
- exact filenames above; sprite names never include `.png`
- no text baked into the art

---

## 5. Do not create

To keep this request minimal, do **not** render:

- any other sheet from `PLANNED_ICONS.md` (A, B, C, D, E, G, H, I, J, K)
- alternate or extra AuctionHouse icons
- replacements for `sa_auction_house`, `sa_admin_shop`, item icons or any
  existing `sa_*` sprite
- a new Buy Now icon — the existing `sa_admin_shop` cash-register art is
  intentionally reused for purchase/price rows

Only the four icons in section 1 are needed for AuctionHouse.
