# DailyRewards icon map

Icons are taken from `ServerAssistArt/UIAtlases/UIAtlas`. The sprite name is used
without `.png`. The source masters are in `ServerAssistArt/icons` and the 256 px
previews are in `ServerAssistArt/preview`.

| UI element | Sprite key | Source file | Why |
|---|---|---|---|
| Daily Rewards card/panel | `sprite:sa_daily_rewards` | `icons/daily_rewards.png` | Calendar + wrapped gift matches the plugin exactly |
| Daily tier | `sprite:sa_daily_rewards` | `icons/daily_rewards.png` | Same calendar/gift subject |
| Weekly tier | `sprite:sa_act_time` | `icons/sa_act_time.png` | Alarm clock reads as a repeating time period |
| Monthly tier | `sprite:sa_nav_snapshots` | `icons/sa_nav_snapshots.png` | Clock with a repeat arrow suggests a longer cycle |
| Seasonal tier | `sprite:sa_act_weather` | `icons/sa_act_weather.png` | Storm/weather art is the closest season subject in the pack |
| Playtime | `sprite:sa_act_time` | `icons/sa_act_time.png` | Clock = tracked time |
| Daily streak | `sprite:sa_nav_logs` | `icons/sa_nav_logs.png` | Logbook = recorded claim history |
| Grant reward | `sprite:sa_act_give` | `icons/sa_act_give.png` | Hand offering a gift/parcel |
| Reset claim | `sprite:sa_nav_snapshots` | `icons/sa_nav_snapshots.png` | Rewind/clock art reads as reset |
| Admin tools | `sprite:sa_nav_moderation` | `icons/sa_nav_moderation.png` | Gavel + shield = staff actions |
| Season settings | `sprite:sa_act_weather` | `icons/sa_act_weather.png` | Weather/season subject |
| Permissions | `sprite:sa_permission_gui` | `icons/permission_gui.png` | Key + ID badge = permission node |
| Commands | `sprite:sa_nav_server` | `icons/sa_nav_server.png` | Server rack = server command |
| Reward items | `item:<itemName>` | live game item icon | Matches the actual configured item; falls back to `sprite:sa_nav_items` |
| Locked / error | `sprite:sa_act_warn` | `icons/sa_act_warn.png` | Warning triangle |
| Online players | `sprite:sa_nav_players` | `icons/sa_nav_players.png` | Survivor dog tags |

## Notes

- The card's icon is also written to the view as `background=` and `banner=`.
- Rows use short badges: `READY`, `LOCKED`, `CLAIMED`, `OFF`, `TIME`, `STREAK`,
  `ADMIN`, `GIVE`, `RESET`, `SEASON`, `ONLINE`, `ITEM`, `PERM`, `EXTRA`, `ERROR`.
- Tier tint colors: daily green, weekly cyan, monthly amber, seasonal purple.
- `ServerAssistArt/PLANNED_ICONS.md` lists `sa_rewards_daily`,
  `sa_rewards_weekly`, `sa_rewards_monthly` and `sa_rewards_claim`, but those
  four are not rendered in the current art pack. This build deliberately does not
  reference them, because missing sprites fall back to the hammer. If they are
  rendered later, replace the tier icons through the config `Icon` fields and
  the card icon through `PanelIcon`.
- The PNGs must be installed in the Server Assist mod folder on the server and
  every client:
  `AAZ_ServerAssistMod/UIAtlases/UIAtlas/`.
- The art-agent request for the missing weekly/monthly/seasonal tier icons is in
  `../ServerAssistArt/REQUESTS_DAILYREWARDS.md`.
