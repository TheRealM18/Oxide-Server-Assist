# Requested icons: Plugin Hot Reload panel

Status: **no new icons required for the current Server Assist UI**

This is a scoped request for the ServerAssistArt agent. For the current Server
Assist `Plugin Hot Reload` panel, the required new-icon count is **zero**. Every
sprite the panel references already exists in
`ServerAssistArt/UIAtlases/UIAtlas`.

Do **not** render the four `sa_hotreload_*` icons from
`PLANNED_ICONS.md` Sheet J for this UI. They are action-button art, and current
Server Assist action buttons do not render icons. The panel must not ask the art
agent to spend time on unused sprite names.

---

## What the panel already uses

| UI element | Sprite | Status |
|---|---|---|
| Panel card, background and banner | `sprite:sa_plugin_hot_reload` | exists |
| AdminKits row | `sprite:sa_admin_kits` | exists |
| AdminShop row | `sprite:sa_admin_shop` | exists |
| AdminSuite row | `sprite:sa_admin_suite` | exists |
| AuctionHouse row | `sprite:sa_auction_house` | exists |
| DailyRewards row | `sprite:sa_daily_rewards` | exists |
| DynamicBackpack row | `sprite:sa_dynamic_backpack` | exists |
| PermissionGui row | `sprite:sa_permission_gui` | exists |
| PluginHotReload row | `sprite:sa_plugin_hot_reload` | exists |
| VoteSkipNight row | `sprite:sa_vote_skip_night` | exists |
| QLNPack / QLNPackServer / QLNPackBootstrap rows | `sprite:sa_qln_pack` | exists |
| ServerAssist row | `sprite:sa_nav_server` | exists |
| Empty / no-files / fallback row | `sprite:sa_nav_server` | exists |

Action buttons (Refresh, Scan, Enable/Disable Watch, Reload ALL, Prev/Next) are
text-only in the current Server Assist UI. They do not render an icon view, so
they must not have dedicated art.

---

## Not requested

Do **not** render these for this panel:

- `sa_hotreload_reload`
- `sa_hotreload_scan`
- `sa_hotreload_watch`
- `sa_hotreload_status`

Those four are the Sheet J concepts in `PLANNED_ICONS.md`. They are useful only
if Server Assist later gains rendered button icons. That is a separate future
request, not this one.

Also do not render the rest of `PLANNED_ICONS.md` for this panel:

- Sheets A-D — AdminKits actions and kit categories
- Sheet E — AdminShop actions
- Sheet F — AuctionHouse actions
- Sheet G — DailyRewards actions
- Sheet H — DynamicBackpack actions
- Sheet I — PermissionGui actions
- Sheet K — VoteSkipNight actions

Each of those plugins already has a card icon in the atlas, and this panel uses
that card icon directly on its row.

Do not create a generic fallback plugin icon. `sprite:sa_plugin_hot_reload` is
the fallback for an unknown plugin row for now.

Do not create a Server Assist brand mark. The 1.1.0 / 1.2.0 art packages already
provide `sa_server_assist.png`; this panel uses `sprite:sa_nav_server` for the
ServerAssist row.

---

## If the UI changes later

If Server Assist action buttons gain rendered icons, the only new assets this
panel would need are the four Sheet J keys above:

| Sprite key | Would be used for |
|---|---|
| `sa_hotreload_reload` | Reload ALL and refresh |
| `sa_hotreload_scan` | Scan |
| `sa_hotreload_watch` | Enable/Disable Watch |
| `sa_hotreload_status` | Watcher status / empty state |

That is a future, separate request. Do not start it from this file.

---

## Active handoff note

`ServerAssistArt/ICON_REQUEST.md` is the current active handoff and requests only
the eight PlayerProfiles icons. This file is a scoped PluginHotReload check, not
a replacement for that handoff, and it adds no work.
