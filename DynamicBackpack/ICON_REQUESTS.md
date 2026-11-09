# ServerAssistArt icon request — DynamicBackpack 2.2.0

Status: **no new icons requested**

The current DynamicBackpack 2.2.0 plugin needs exactly **one** custom
ServerAssistArt icon, and that icon already exists. Do not render a new batch
for this plugin.

## Required icon (already finished)

| Sprite | File | Status | Use |
|---|---|---|---|
| `sa_dynamic_backpack` | `UIAtlases/UIAtlas/sa_dynamic_backpack.png` | ready, 128x128 | Server Assist panel card icon |

Source art already in the pack:

- `icons/dynamic_backpack.png` — 1254x1254 source
- `preview/sa_dynamic_backpack.png` — 256x256 preview

The plugin references this sprite as `sprite:sa_dynamic_backpack` through
`RegisterPanel` and `SetPanelIcon`. No redraw is needed unless the pack's art
direction changes.

## Cancel these planned icons — they are not used

`PLANNED_ICONS.md`, Sheet H (DynamicBackpack), lists four icons that the
current 2.2.0 design does not reference anywhere:

| Planned sprite | Why it is not needed |
|---|---|
| `sa_backpack_page` | The page title and page count are text-only. |
| `sa_backpack_prev` | Prev/Next/First/Last are text-only action buttons. |
| `sa_backpack_next` | Same as above. |
| `sa_backpack_locked` | Locked rows use the vanilla `ui_game_symbol_backpack` sprite. |

Please skip or remove Sheet H from the render queue for DynamicBackpack. Those
four sprites are now obsolete for this plugin.

## Do not create these either

These are base-game assets and must not be redrawn for the pack:

- `ui_game_symbol_backpack` — vanilla locked/empty/no-result/error row icon
- `item:<itemName>` — every stored item row uses the live game item icon

## Why no button icons are needed

Server Assist's action buttons are text-only in the current UI. The backpack
provides 11 text actions:

`Prev`, `Next`, `First`, `Last`, `-10`, `+10`, `Go To Page`, `Search`,
`Clear Search`, `Deposit Held`, `Refresh`

There is no icon slot for those buttons, so navigation, search, deposit and
refresh glyphs would be unused. Page selection and search are handled by the
Server Assist input field, not by new art.

## Current icon usage in the panel

| UI element | Icon source |
|---|---|
| Server Assist panel card | `sprite:sa_dynamic_backpack` |
| Stored item row | `item:<itemName>` (vanilla) |
| Locked page row | `sprite:ui_game_symbol_backpack` (vanilla) |
| Empty slot row | `sprite:ui_game_symbol_backpack` (vanilla) |
| No search results row | `sprite:ui_game_symbol_backpack` (vanilla) |
| Error row | `sprite:ui_game_symbol_backpack` (vanilla) |
| Action buttons | text only |

## Deliverable expected from the art agent

Nothing new. Just verify that `sa_dynamic_backpack.png` is present in the
shipped ServerAssistArt UI atlas on clients. If the art agent is keeping a
queue, the only action for DynamicBackpack is to cancel Sheet H.
