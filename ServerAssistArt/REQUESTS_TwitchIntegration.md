# TwitchIntegration icon request

Target plugin: `TwitchIntegration` 0.2.1
Target sprite prefix: `sa_stream_*`

This is the **complete** list for the current Twitch panel. Please paint only the
30 sprites below. The plugin already falls back to base-game icons, existing mod
icons, and live Server Assist `GetPluginIcon`/`GetPanelIcon` hooks for everything
else, so any sprite not on this list is intentionally not requested.

The atlas already contains interim aliases under these names. Replace the 30
requested PNGs with unique painted versions; leave the other `sa_stream_*` aliases
as-is. They are only kept for config migration and optional remapping.

---

## Style and output rules

- Match `bonus/server_assist.png`: 7 Days to Die post-apocalyptic UI icon,
  hand-painted weathered metal, chipped teal paint, rust, scratches, bold dark
  outline, strong rim light, high contrast, chunky readable silhouette, slightly
  isometric three-quarter view.
- 128x128, 32-bit ARGB PNG, fully transparent background.
- Readable at 64x64.
- No text, no letters, no wordmarks, no Twitch purple, no glitch logo, no “TTV”,
  no Prime crown, no platform branding. The set is platform-neutral.
- Keep the art mostly neutral metal/teal/amber so the plugin's green/amber/red
  status tints still read.
- Sprite names are exact. Do not rename, add suffixes, or include `.png` in the
  plugin references.
- File names must be `<sprite>.png` exactly.

---

## Required sprites (30)

### P0 — panel, navigation, tiers, links, rewards (12)

| # | Sprite | Subject | Used by |
|---|---|---|---|
| 1 | `sa_stream_panel` | Broadcast tower with signal waves and a small play triangle | Panel card, banner, background watermark |
| 2 | `sa_stream_home` | Dashboard/antenna with a short checklist | Home / Server Assist buttons |
| 3 | `sa_stream_tier_1` | One spark / single heavy chevron — Spark | Default Tier 1 card |
| 4 | `sa_stream_tier_2` | Two stacked surge chevrons — Surge | Default Tier 2 card |
| 5 | `sa_stream_tier_3` | Three stacked overload chevrons — Overload | Default Tier 3 card |
| 6 | `sa_stream_loadout` | Three-slot clipboard or field kit (buff / debuff / reward slots) | Loadout row and buttons |
| 7 | `sa_stream_catalog` | Open book or list plate with a star | Reward catalog row and buttons |
| 8 | `sa_stream_link` | Chain link with signal waves | Link action |
| 9 | `sa_stream_link_pending` | Hourglass fused to a chain | Pending link state |
| 10 | `sa_stream_linked` | Chain with a bold check | Linked state |
| 11 | `sa_stream_link_cancel` | Chain with a bold X | Cancel link action |
| 12 | `sa_stream_reward` | Gift box or supply crate | Generic reward category and buttons |

### P1 — buff effect icons (9)

| # | Sprite | Subject |
|---|---|---|
| 13 | `sa_stream_buff_quick_feet` | Boot with speed lines or a small wing |
| 14 | `sa_stream_buff_regen` | Heart with a medical cross or glowing plus |
| 15 | `sa_stream_buff_looter` | Loot bag, magnifier, and a few coins |
| 16 | `sa_stream_buff_melee_surge` | Fist or melee weapon with an upward power arrow |
| 17 | `sa_stream_buff_ranged_surge` | Crossbow or rifle with an upward power arrow |
| 18 | `sa_stream_buff_infinite_ammo` | Ammo box and bullet with an infinity symbol |
| 19 | `sa_stream_buff_toughness` | Riveted shield or armor plate |
| 20 | `sa_stream_buff_xp_gain` | Star/XP chevron with an upward arrow |
| 21 | `sa_stream_buff_pack_mule` | Heavy backpack with a weight or upward arrow |

### P1 — debuff effect icons (9)

| # | Sprite | Subject |
|---|---|---|
| 22 | `sa_stream_debuff_heavy_legs` | Leg/boot with a weight or anchor |
| 23 | `sa_stream_debuff_blurred_vision` | Eye with blur waves or a cracked lens |
| 24 | `sa_stream_debuff_no_running` | Running boot with a bold slash |
| 25 | `sa_stream_debuff_no_melee` | Fist or melee weapon with a bold slash |
| 26 | `sa_stream_debuff_no_ranged` | Crossbow or rifle with a bold slash |
| 27 | `sa_stream_debuff_encumbered` | Overloaded backpack with a weight |
| 28 | `sa_stream_debuff_tougher_enemies` | Zombie/skull head with armor plating |
| 29 | `sa_stream_debuff_double_spawns` | Two zombie silhouettes or a plus sign over a spawn |
| 30 | `sa_stream_debuff_no_healing` | Medkit/health cross with a bold slash |

---

## Do NOT paint these

These are already covered. Painting them would waste art credits.

### Base-game or live Server Assist icons

| Role / value | Why |
|---|---|
| `ui_game_symbol_twitch_buffs`, `ui_game_symbol_twitch_debuffs` | Base-game Twitch buff/debuff symbols |
| `ui_game_symbol_check`, `ui_game_symbol_x`, `ui_game_symbol_lock`, `ui_game_symbol_clock` | Base-game status symbols |
| `ui_game_symbol_star`, arrows, refresh | Base-game navigation/fallback symbols |
| `item:*` reward icons | Server Assist resolves base-game and mod item icons through the item atlas |
| `plugin:AdminSuite`, `plugin:PluginHotReload`, `plugin:PermissionGui` | Resolved live through `GetPluginIcon` |
| `sa_admin_suite`, `sa_permission_gui`, `sa_plugin_hot_reload` | Existing plugin-card art (fallback) |
| `sa_nav_players`, `sa_nav_logs`, `sa_nav_server`, `sa_nav_help` | Existing ServerAssistArt icons |
| `sa_announce_test` | Existing announcement test icon used for the Twitch test button |

### Streaming aliases not used by the current UI

Do not paint unique art for these. They remain as interim aliases only:

```text
sa_stream_tier
sa_stream_buff
sa_stream_debuff
sa_stream_status_armed
sa_stream_status_opted_out
sa_stream_status_paused
sa_stream_test
sa_stream_test_bits
sa_stream_test_sub
sa_stream_test_points
sa_stream_loadout_armed
sa_stream_loadout_empty
sa_stream_loadout_locked
sa_stream_unlink
sa_stream_slot_buff
sa_stream_slot_debuff
sa_stream_slot_reward
sa_stream_info
sa_stream_admin
sa_stream_admin_reload
sa_stream_admin_reconcile
sa_stream_admin_links
sa_stream_admin_players
sa_stream_audit
sa_stream_relay
sa_stream_relay_online
sa_stream_relay_offline
sa_stream_relay_pending
sa_stream_reward_dukes
sa_stream_reward_bandages
sa_stream_reward_ammo_9mm
sa_stream_reward_forged_steel
sa_stream_reward_ammo_762
sa_stream_reward_mega_crush
sa_stream_reward_ammo_762_ap
sa_stream_reward_medkit
sa_stream_trigger_support
sa_stream_trigger_sub
sa_stream_trigger_resub
sa_stream_trigger_gift_sub
sa_stream_trigger_points
sa_stream_trigger_auto_reward
sa_stream_trigger_raid
sa_stream_trigger_follow
sa_stream_trigger_momentum
sa_stream_trigger_chat
sa_stream_trigger_any
sa_stream_trigger_custom
```

`LegacyRoleIcons` and `LegacyStreamRewardIcons` in the plugin still reference some
of these names for one-time config migration. Those are not art requests.

### Announcement set

`sa_announce_*` belongs to the future announcement/repeating-message plugin. Do
not paint it as part of this request. The only exception is `sa_announce_test`,
which already exists and is reused by the Twitch admin test button.

---

## After painting

1. Add the 30 names to `$preferred` in `tools/build-icons.ps1` if they are not
   already there.
2. Run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\tools\build-icons.ps1
   ```

3. Copy the generated PNGs into `UIAtlases/UIAtlas/`.
4. Rebuild the art pack zip (next version after `1.2.0`).
5. Install on the server and every client.
6. Verify the sprite names match this document exactly. The plugin references
   them without `.png`.

## Quick verification checklist

- [ ] Exactly 30 new painted PNGs.
- [ ] All files are 128x128, 32-bit ARGB, transparent.
- [ ] All names match exactly.
- [ ] No text/logos/platform branding.
- [ ] No unique art was made for the do-not-paint list.
- [ ] `sa_stream_panel` reads clearly at 64x64 and works as a 12%-alpha background.
- [ ] Tier 1/2/3 read as one/two/three chevrons at 64x64.
- [ ] Buff and debuff pairs are visually distinguishable at a glance.
