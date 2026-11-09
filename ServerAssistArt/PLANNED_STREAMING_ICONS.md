# Streaming + announcement icon plan

> **Superseded by `REQUESTS_TwitchIntegration.md`.** The Twitch agent’s final
> request is exactly 30 sprites. Use that file for the current Twitch batch.
> This 94-icon plan is retained only as background and is not the work order.

> **Not active for the current batch.** The only current request is
> `ICON_REQUEST.md`, which asks for the 8 missing PlayerProfiles icons. The
> streaming and announcement aliases already work; do not replace them yet.

Status: **interim aliases are in place.** The image account still returns
`Insufficient account funds`, so the 94 `sa_stream_*` and `sa_announce_*`
sprites currently in the atlas are copies of existing art under the final
sprite names. Replace those PNGs with the unique painted versions below when
credits are available; the names do not change, so no plugin config needs to be
rewritten.

The unique art plan below is still the target set.

This plan covers the new streaming rewards plugin (`TwitchIntegration`) and the
upcoming announcement/repeating-message plugin. It is intentionally
platform-neutral: no Twitch purple, no glitch logo, no “TTV”, no Prime crown,
and no literal “hype train”. The same sprites should work for YouTube, Kick,
TikTok, Trovo or a custom platform later.

Use the existing style reference: `bonus/server_assist.png`.

---

## Art rules

- Sprite prefix: `sa_stream_*` for streaming, `sa_announce_*` for announcements.
- 128x128, 32-bit ARGB PNG, transparent background, readable at 64x64.
- Keep the art mostly neutral metal/teal/amber so the plugin's green/amber/red
  status tints still read.
- The panel icon is reused as a banner and as a 12%-alpha background. Keep it a
  simple centered silhouette.
- No text, no platform wordmarks, no brand colors.
- The plugin now resolves these names through its `Icons` map and the
  `PanelIcon`, `Tier.Icon` and `Effect.Icon` fields. Keep the sprite names
  stable; remapping is config-only.

---

## Current placeholder keys in the plugin

These are the keys the streaming plugin uses today and should eventually be
replaced:

```text
ui_game_symbol_twitch_buffs
ui_game_symbol_twitch_debuffs
ui_game_symbol_gift
ui_game_symbol_map
ui_game_symbol_star
ui_game_symbol_hammer
ui_game_symbol_check
ui_game_symbol_x
ui_game_symbol_lock
```

The `item:` reward icons are real game item icons and can stay, or be replaced
with the hand-painted reward set below.

---

## Phase 1 — panel, navigation, buffs, debuffs, rewards

### Panel and navigation

| Sprite | Subject |
|---|---|
| `sa_stream_panel` | Broadcast tower with signal waves and a small play triangle |
| `sa_stream_home` | Dashboard/antenna with a short checklist |
| `sa_stream_tier` | Three stacked chevrons or a tier medal |
| `sa_stream_tier_1` | One spark/chevron — Spark |
| `sa_stream_tier_2` | Two surge chevrons — Surge |
| `sa_stream_tier_3` | Three overload chevrons — Overload |
| `sa_stream_buff` | Upward arrow with a glow |
| `sa_stream_debuff` | Downward arrow with a crack |
| `sa_stream_reward` | Gift/crate |
| `sa_stream_loadout` | Three-slot clipboard/kit |
| `sa_stream_catalog` | Open book/list with a star |
| `sa_stream_info` | Info/help plate |

### Linking

| Sprite | Subject |
|---|---|
| `sa_stream_link` | Chain link with signal waves |
| `sa_stream_link_pending` | Hourglass with a chain |
| `sa_stream_linked` | Chain with a check |
| `sa_stream_unlink` | Broken chain |
| `sa_stream_link_cancel` | Chain with an X |

### Loadouts / setups

| Sprite | Subject |
|---|---|
| `sa_stream_loadout_empty` | Empty three-slot tray |
| `sa_stream_loadout_armed` | Filled slots with a check |
| `sa_stream_loadout_locked` | Tray with a padlock |
| `sa_stream_slot_buff` | Buff slot marker |
| `sa_stream_slot_debuff` | Debuff slot marker |
| `sa_stream_slot_reward` | Reward slot marker |

### Buffs

| Sprite | Label |
|---|---|
| `sa_stream_buff_quick_feet` | Quick Feet |
| `sa_stream_buff_regen` | Regeneration |
| `sa_stream_buff_looter` | Looter's Luck |
| `sa_stream_buff_melee_surge` | Melee Surge |
| `sa_stream_buff_ranged_surge` | Ranged Surge |
| `sa_stream_buff_infinite_ammo` | Infinite Ammo |
| `sa_stream_buff_toughness` | Toughness |
| `sa_stream_buff_xp_gain` | XP Gain |
| `sa_stream_buff_pack_mule` | Pack Mule |

### Debuffs

| Sprite | Label |
|---|---|
| `sa_stream_debuff_heavy_legs` | Heavy Legs |
| `sa_stream_debuff_blurred_vision` | Blurred Vision |
| `sa_stream_debuff_no_running` | No Running |
| `sa_stream_debuff_no_melee` | No Melee |
| `sa_stream_debuff_no_ranged` | No Ranged |
| `sa_stream_debuff_encumbered` | Encumbered |
| `sa_stream_debuff_tougher_enemies` | Tougher Enemies |
| `sa_stream_debuff_double_spawns` | Double Spawns |
| `sa_stream_debuff_no_healing` | No Healing |

### Rewards

| Sprite | Label |
|---|---|
| `sa_stream_reward_dukes` | 100 Dukes |
| `sa_stream_reward_bandages` | 3 First Aid Bandages |
| `sa_stream_reward_ammo_9mm` | 30 9mm Rounds |
| `sa_stream_reward_forged_steel` | 50 / 200 Forged Steel |
| `sa_stream_reward_ammo_762` | 40 7.62mm Rounds |
| `sa_stream_reward_mega_crush` | 2 Mega Crush |
| `sa_stream_reward_ammo_762_ap` | 60 7.62mm AP Rounds |
| `sa_stream_reward_medkit` | 3 First Aid Kits |

---

## Phase 2 — triggers, admin, audit, relay, tests

### Triggers

| Sprite | Covers |
|---|---|
| `sa_stream_trigger_support` | Bits / tips / donations |
| `sa_stream_trigger_sub` | Subscriptions |
| `sa_stream_trigger_resub` | Resubs / month milestones |
| `sa_stream_trigger_gift_sub` | Gift subs |
| `sa_stream_trigger_points` | Channel points / redemptions |
| `sa_stream_trigger_auto_reward` | Automatic rewards |
| `sa_stream_trigger_raid` | Raids |
| `sa_stream_trigger_follow` | Follows |
| `sa_stream_trigger_momentum` | Hype train / momentum |
| `sa_stream_trigger_chat` | Chat message trigger |
| `sa_stream_trigger_any` | Catch-all |
| `sa_stream_trigger_custom` | Staff-defined trigger |

### Admin, audit, relay, tests

| Sprite | Subject |
|---|---|
| `sa_stream_admin` | Wrench and shield |
| `sa_stream_admin_reload` | Circular arrows and gear |
| `sa_stream_admin_reconcile` | Sync arrows and server |
| `sa_stream_admin_links` | Chain and list |
| `sa_stream_admin_players` | Dog tags/group |
| `sa_stream_audit` | Logbook and magnifier |
| `sa_stream_relay` | Server rack and signal |
| `sa_stream_relay_online` | Server rack and green signal |
| `sa_stream_relay_offline` | Server rack and red signal |
| `sa_stream_relay_pending` | Server rack and hourglass |
| `sa_stream_test` | Test tube and lightning |
| `sa_stream_test_bits` | Coin and test tube |
| `sa_stream_test_sub` | Star and test tube |
| `sa_stream_test_points` | Token and test tube |
| `sa_stream_status_armed` | Check and shield |
| `sa_stream_status_paused` | Pause bars |
| `sa_stream_status_opted_out` | Bell-off/eye-off |

---

## Phase 3 — announcement / repeating message plugin

The announcement plugin is not in the workspace yet. These are generic,
platform-neutral assets for when its menus are defined.

| Sprite | Subject / usage |
|---|---|
| `sa_announce_panel` | Megaphone with signal waves |
| `sa_announce_message` | Speech bubble with lines |
| `sa_announce_messages` | Stack of speech bubbles |
| `sa_announce_schedule` | Calendar and clock |
| `sa_announce_interval` | Circular repeat arrows |
| `sa_announce_trigger` | Lightning bolt on a switch |
| `sa_announce_channel` | Broadcast tower and speech bubble |
| `sa_announce_random` | Dice/shuffle arrows |
| `sa_announce_priority` | Star and flag |
| `sa_announce_test` | Play triangle and megaphone |
| `sa_announce_enable` | Power switch |
| `sa_announce_disable` | Power switch with a slash |
| `sa_announce_list` | Numbered list on a metal plate |
| `sa_announce_webhook` | Plug and signal waves |
| `sa_announce_silent` | Megaphone with a slash |
| `sa_announce_repeat_count` | Counter dial with arrows |

---

## Sheet plan

Generate each group as a 2x2 sheet, then slice with
`tools/slice-sheet.ps1`. Suggested grouping:

| Sheet | Icons |
|---|---|
| `stream_nav_1` | panel, home, tier, loadout |
| `stream_nav_2` | buff, debuff, reward, catalog |
| `stream_link_1` | link, link_pending, linked, unlink |
| `stream_tier_1` | tier_1, tier_2, tier_3, info |
| `stream_buffs_1` | quick_feet, regen, looter, melee_surge |
| `stream_buffs_2` | ranged_surge, infinite_ammo, toughness, xp_gain |
| `stream_buffs_3` | pack_mule, heavy_legs, blurred_vision, no_running |
| `stream_debuffs_1` | no_melee, no_ranged, encumbered, tougher_enemies |
| `stream_debuffs_2` | double_spawns, no_healing, reward_dukes, reward_bandages |
| `stream_rewards_1` | ammo_9mm, forged_steel, ammo_762, mega_crush |
| `stream_rewards_2` | ammo_762_ap, medkit, trigger_support, trigger_sub |
| `stream_triggers_1` | resub, gift_sub, points, auto_reward |
| `stream_triggers_2` | raid, follow, momentum, chat |
| `stream_triggers_3` | any, custom, admin, audit |
| `stream_admin_1` | relay, test, status_armed, status_paused |
| `announce_1` | panel, message, messages, schedule |
| `announce_2` | interval, trigger, channel, random |
| `announce_3` | priority, test, enable, disable |
| `announce_4` | list, webhook, silent, repeat_count |

## Shared sheet prompt

> Four separate game UI icons arranged in a clean 2x2 grid on a fully
> transparent background, each icon centered in its own quadrant with generous
> empty space between them, no grid lines, no borders, no text, no letters.
> Match the exact art style of the reference image: 7 Days to Die
> post-apocalyptic UI icon, hand-painted weathered metal, chipped teal paint,
> rust, scratches, bold dark outline, strong rim light, high contrast, chunky
> readable silhouette, slightly isometric three-quarter view. Each icon must be
> readable at 64x64. Color accents: teal metal, warm amber, brass, rust.
> Platform-neutral: no Twitch logos, no purple branding, no wordmarks.

## After rendering

1. Slice each sheet into `icons/`.
2. Add the names to `$preferred` in `tools/build-icons.ps1`.
3. Run `tools/build-icons.ps1`.
4. Copy the new atlas PNGs into `AAZ_ServerAssistArt/UIAtlases/UIAtlas/`.
5. Rebuild the icon mod zip and update the Drive manifest.

No plugin code changes are part of this plan.
