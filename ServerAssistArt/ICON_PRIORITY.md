# Icon priority list — post-audit

Status: **all 102 confirmed icons are delivered.** P0 + P1 (60) shipped in
`AAZ_ServerAssistArt-1.3.0`; P2 (42) shipped in `AAZ_ServerAssistArt-1.4.0`.

## New / deprecated plugins

- **MediaIntegration** replaces **TwitchIntegration** (the old plugin is
  deprecated and left in place for rollback).
  - No new art. It keeps the same 30 `sa_stream_*` sprites.
  - `MediaIntegration/ICONS.md` explicitly says the 30-icon request is still
    complete and platform logos are text badges, not painted art.
- **SentinelAC** is new and requests **12 `sa_ac_*` icons**.
  - `ServerAssistArt/REQUESTS_SentinelAC.md` is the authoritative list.
- No other plugin has an outstanding icon request.

## Reconciliation

| Area | Originally planned | Now | Removed | Added |
|---|---:|---:|---:|---:|
| Server Profiles → PlayerProfiles | 23 | 8 | 19 | 4 |
| Streaming → MediaIntegration | 78 | 30 | 48 | 0 |
| Announcement plugin | 16 | 0 | 16 | 0 |
| BungeeNet | 15 | 4 | 11 | 0 |
| PermissionGui | 7 | 6 | 1 | 0 |
| Other plugin/kit requests | 40 | 42 | 24 | 26 |
| SentinelAC (new) | 0 | 12 | 0 | 12 |
| **Total** | **179** | **102** | **119** | **42** |

- **Originally planned:** 179
- **Removed by the audits:** 119
- **Newly added by the audits:** 42
- **Now to do:** **102 total**, of which **60 are delivered** and **42 remain**.

## Delivered — P0 + P1 (60 icons)

- **PlayerProfiles — 8**
  `sa_profile_panel`, `sa_profile_identity`, `sa_profile_active`,
  `sa_profile_switch`, `sa_profile_new`, `sa_profile_rename`,
  `sa_profile_save`, `sa_profile_delete`
- **MediaIntegration panel/navigation — 12**
  `sa_stream_panel`, `sa_stream_home`, `sa_stream_tier_1`,
  `sa_stream_tier_2`, `sa_stream_tier_3`, `sa_stream_loadout`,
  `sa_stream_catalog`, `sa_stream_link`, `sa_stream_link_pending`,
  `sa_stream_linked`, `sa_stream_link_cancel`, `sa_stream_reward`
- **PermissionGui — 6**
  `sa_perm_oxide`, `sa_perm_game`, `sa_perm_plugin`, `sa_perm_groups`,
  `sa_perm_users`, `sa_perm_tracks`
- **AdminKits — 7**
  `sa_kits_collect`, `sa_kits_manage`, `sa_kits_create`,
  `sa_kits_from_inventory`, `sa_kits_edit`, `sa_kits_delete`,
  `sa_kits_limits`
- **AdminShop — 12**
  `sa_shop_categories`, `sa_shop_search`, `sa_shop_buy`,
  `sa_shop_permissions`, `sa_shop_weapons`, `sa_shop_ammo`,
  `sa_shop_food_medical`, `sa_shop_resources`, `sa_shop_blocks`,
  `sa_shop_books`, `sa_act_prev`, `sa_act_next`
- **AuctionHouse — 4**
  `sa_auction_browse`, `sa_auction_mine`, `sa_auction_sell`,
  `sa_auction_cancel`
- **DailyRewards — 3**
  `sa_rewards_weekly`, `sa_rewards_monthly`, `sa_rewards_seasonal`
- **VoteSkipNight — 4**
  `sa_vote_yes`, `sa_vote_no`, `sa_vote_cancel`, `sa_vote_bloodmoon`
- **BuggeeNet — 4**
  `sa_buggee_panel`, `sa_buggee_home`, `sa_buggee_info`, `sa_buggee_warn`

## Delivered — P2 (42 icons, 11 sheets)

### MediaIntegration buffs — 9

`sa_stream_buff_quick_feet`, `sa_stream_buff_regen`,
`sa_stream_buff_looter`, `sa_stream_buff_melee_surge`,
`sa_stream_buff_ranged_surge`, `sa_stream_buff_infinite_ammo`,
`sa_stream_buff_toughness`, `sa_stream_buff_xp_gain`,
`sa_stream_buff_pack_mule`

### MediaIntegration debuffs — 9

`sa_stream_debuff_heavy_legs`, `sa_stream_debuff_blurred_vision`,
`sa_stream_debuff_no_running`, `sa_stream_debuff_no_melee`,
`sa_stream_debuff_no_ranged`, `sa_stream_debuff_encumbered`,
`sa_stream_debuff_tougher_enemies`, `sa_stream_debuff_double_spawns`,
`sa_stream_debuff_no_healing`

### AdminSuite — 12

`sa_act_kill`, `sa_act_note`, `sa_act_clear`, `sa_act_take`, `sa_act_jail`,
`sa_act_buff`, `sa_act_debuff`, `sa_act_search`, `sa_act_creative`,
`sa_act_rollback`, `sa_act_save`, `sa_act_spawn`

### SentinelAC — 12

`sa_ac_panel`, `sa_ac_risk`, `sa_ac_evidence`, `sa_ac_modules`,
`sa_ac_movement`, `sa_ac_combat`, `sa_ac_inventory`, `sa_ac_grief`,
`sa_ac_exploit`, `sa_ac_progression`, `sa_ac_clear`, `sa_ac_export`

## Cost

P2 was **42 icons / 11 sheets**, delivered in `AAZ_ServerAssistArt-1.4.0`. The
batch used **12 image calls** because the first AdminSuite action sheet had to be
regenerated with the correct subjects. At ~$0.91 per call that is about
**$10.92**, leaving roughly **$1.50–$2.00** from the $12.50 top-up.

## Cleanup / deprecation

- 15 unreferenced `sa_announce_*` placeholders were removed.
- The 48 `sa_stream_*` legacy aliases remain because MediaIntegration still
  references them for config migration.
- TwitchIntegration is deprecated, but its 30 `sa_stream_*` sprites are still
  used by MediaIntegration. No textures were removed for the rename.
- No platform logo art is needed; MediaIntegration uses text badges.
