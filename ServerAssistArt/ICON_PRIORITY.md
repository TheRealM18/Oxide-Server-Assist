# Icon priority list — final audit

Status: **P0 + P1 delivered.** 60 unique icons are rendered, built into the
atlas, and packaged as `AAZ_ServerAssistArt-1.3.0`. P2 (30 icons) remains.

Cleanup: 15 unreferenced `sa_announce_*` placeholder aliases were removed from
the atlas. The 48 `sa_stream_*` legacy aliases remain because TwitchIntegration
still references them for config migration.

The Twitch agent’s final request is `ServerAssistArt/REQUESTS_TwitchIntegration.md`
— exactly **30** sprites. It replaces the earlier 78-icon streaming plan.

## Reconciliation

| Area | Originally planned | Now | Removed | Added |
|---|---:|---:|---:|---:|
| Server Profiles → PlayerProfiles | 23 | 8 | 19 | 4 |
| Streaming → TwitchIntegration | 78 | 30 | 48 | 0 |
| Announcement plugin | 16 | 0 | 16 | 0 |
| BungeeNet | 15 | 4 | 11 | 0 |
| PermissionGui | 7 | 6 | 1 | 0 |
| Other plugin/kit requests | 40 | 42 | 24 | 26 |
| **Total** | **179** | **90** | **119** | **30** |

- **Originally planned:** 179
- **Removed by the audits:** 119
- **Newly added by the audits:** 30
- **Now to do:** **90**

## Confirmed requests — 90 icons

| Plugin | Icons | Source |
|---|---:|---|
| PlayerProfiles | 8 | `PlayerProfiles/icon-request.md` |
| TwitchIntegration | 30 | `ServerAssistArt/REQUESTS_TwitchIntegration.md` |
| AdminKits | 7 | `ServerAssistArt/REQUESTS_AdminKits.md` |
| AdminShop | 12 | `ServerAssistArt/REQUESTS_AdminShop.md` |
| AuctionHouse | 4 | `ServerAssistArt/REQUESTS_AUCTIONHOUSE.md` |
| DailyRewards | 3 | `ServerAssistArt/REQUESTS_DAILYREWARDS.md` |
| PermissionGui | 6 | `ICON-REQUESTS-PermissionGui.md` |
| VoteSkipNight | 4 | `ServerAssistArt/VOTESKIPNIGHT_ICON_REQUEST.md` |
| AdminSuite | 12 | `ServerAssistArt/REQUESTS_AdminSuite.md` |
| BuggeeNet | 4 | `BuggeeNet/ICON_TODO.md` |
| **Total** | **90** | |

## P0 — first render (26 icons, 7 sheets)

- **PlayerProfiles — 8**
  `sa_profile_panel`, `sa_profile_identity`, `sa_profile_active`,
  `sa_profile_switch`, `sa_profile_new`, `sa_profile_rename`,
  `sa_profile_save`, `sa_profile_delete`
- **Twitch panel/navigation — 12**
  `sa_stream_panel`, `sa_stream_home`, `sa_stream_tier_1`,
  `sa_stream_tier_2`, `sa_stream_tier_3`, `sa_stream_loadout`,
  `sa_stream_catalog`, `sa_stream_link`, `sa_stream_link_pending`,
  `sa_stream_linked`, `sa_stream_link_cancel`, `sa_stream_reward`
- **PermissionGui — 6**
  `sa_perm_oxide`, `sa_perm_game`, `sa_perm_plugin`, `sa_perm_groups`,
  `sa_perm_users`, `sa_perm_tracks`

## P1 — second render (34 icons, 9 sheets)

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

## P2 — third render (30 icons, 8 sheets)

- **Twitch buffs — 9**
  `sa_stream_buff_quick_feet`, `sa_stream_buff_regen`,
  `sa_stream_buff_looter`, `sa_stream_buff_melee_surge`,
  `sa_stream_buff_ranged_surge`, `sa_stream_buff_infinite_ammo`,
  `sa_stream_buff_toughness`, `sa_stream_buff_xp_gain`,
  `sa_stream_buff_pack_mule`
- **Twitch debuffs — 9**
  `sa_stream_debuff_heavy_legs`, `sa_stream_debuff_blurred_vision`,
  `sa_stream_debuff_no_running`, `sa_stream_debuff_no_melee`,
  `sa_stream_debuff_no_ranged`, `sa_stream_debuff_encumbered`,
  `sa_stream_debuff_tougher_enemies`, `sa_stream_debuff_double_spawns`,
  `sa_stream_debuff_no_healing`
- **AdminSuite — 12**
  `sa_act_kill`, `sa_act_note`, `sa_act_clear`, `sa_act_take`,
  `sa_act_jail`, `sa_act_buff`, `sa_act_debuff`, `sa_act_search`,
  `sa_act_creative`, `sa_act_rollback`, `sa_act_save`, `sa_act_spawn`

## $17.50 plan

Render **P0 + P1 = 60 icons in 16 sheets**. Estimated **~$16.75** at the current
rate, leaving about **$0.75** in reserve.

That covers PlayerProfiles, Twitch panel/navigation, PermissionGui, AdminKits,
AdminShop, AuctionHouse, DailyRewards, VoteSkipNight and BuggeeNet. P2 (Twitch
buffs/debuffs + AdminSuite) is about **$8.37** and needs a small top-up.

## Removed / not needed

- **119 icons removed** across the audits, including:
  - 19 server-profile icons that PlayerProfiles does not use
  - 48 streaming aliases the Twitch panel no longer requests
  - 16 announcement icons for a plugin that is not present
  - 11 BungeeNet names that `BuggeeCore` does not reference
  - 1 PermissionGui browser icon
  - 24 other plugin/kit icons with no consumer
- **30 icons added** by the audits: 4 new PlayerProfiles states
  (`active`, `switch`, `new`, `rename`), 3 AdminKits actions, 8 AdminShop
  categories/navigation, 1 DailyRewards seasonal, 2 VoteSkipNight states,
  12 AdminSuite actions.
- Interim `sa_stream_*` / `sa_announce_*` aliases remain in the atlas for config
  migration and optional remapping; they are not part of the 90 painted icons.
