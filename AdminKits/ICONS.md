# AdminKits icon map

These are the Server Assist Art icons chosen for the AdminKits panel. They are
all finished, game-ready sprites from `ServerAssistArt/UIAtlases/UIAtlas/`.
No new art is required for this release.

The planned `sa_kits_*` action icons in `ServerAssistArt/PLANNED_ICONS.md` are
not rendered yet, so this build uses the closest finished icons instead. The
focused art request for the seven replacements is
`ServerAssistArt/REQUESTS_AdminKits.md`. Only those seven are requested; the
twelve kit-category icons are not needed. The sprite keys are centralised in the
`KitIcons` class in `oxide/plugins/AdminKits.cs`, so swapping them later is a
one-line change per entry.

## Panel card

| Where | Sprite | Why |
|---|---|---|
| Server Assist home card | `sprite:sa_admin_kits` | Open supply crate: the kit pack itself |

## Home rows

| Row | Sprite | Badge | Why |
|---|---|---|---|
| Collect a Kit | `sprite:sa_nav_items` | TAKE | Open crate with gear: browse kit contents |
| Manage Kits | `sprite:sa_nav_logs` | LIST | Logbook: the kit registry |
| Create Kit | `sprite:sa_admin_kits` | NEW | Empty supply crate |
| Create From Inventory | `sprite:sa_dynamic_backpack` | COPY | Backpack tipping into a kit |
| Help | `sprite:sa_nav_help` | INFO | Question-mark sign |

## Kit state icons

The collect and manage lists pick the icon from the kit's state. This makes the
list readable at a glance without opening every kit.

| State | Sprite | Badge | Tint |
|---|---|---|---|
| Normal / ready | `sprite:sa_admin_kits` | (none) | none |
| Permission-restricted kit you can see | `sprite:sa_permission_gui` | PERM | none |
| Permission locked | `sprite:sa_permission_gui` | LOCKED | `#FFD166` |
| Cooldown active | `sprite:sa_act_time` | WAIT | `#66CCFF` |
| Total or per-player limit reached | `sprite:sa_act_warn` | LIMIT | `#FF6B6B` |
| Disabled | `sprite:sa_act_freeze` | OFF | `#9AA0A6` |
| Hidden | `sprite:sa_act_spectate` | HIDDEN | `#C79BFF` |

Badges appear in the card tooltip. Tints are deliberately subtle and only used
for states; the normal icons keep the painted colors from the art pack.

## Detail and editor rows

| Row | Sprite | Why |
|---|---|---|
| Description / Name | `sprite:sa_nav_logs` | Written record |
| Items | `sprite:sa_nav_items` | Open supply crate |
| Permission | `sprite:sa_permission_gui` | Key and ID badge |
| Cooldown / Cooldown Status | `sprite:sa_act_time` | Alarm clock |
| Uses / Total Uses | `sprite:sa_admin_kits` | Kit being consumed |
| Uses So Far / Your Claims | `sprite:sa_daily_rewards` | Calendar and reward |
| Per Player | `sprite:sa_nav_players` | Survivor dog tags |
| Limits / Warning | `sprite:sa_act_warn` | Warning triangle |
| State (enabled) | `sprite:sa_act_godmode` | Shield |
| State (disabled) | `sprite:sa_act_freeze` | Ice block |
| State (hidden) | `sprite:sa_act_spectate` | Eye in a ring |
| Item detail rows | `item:<internalName>` | The actual game item icon |
| Unknown item | `sprite:sa_nav_items` | Crate fallback |

Help rows reuse the matching command icon: kit, collect, create, backpack, give
or permission. This keeps every row from falling back to the vanilla hammer.

## Buttons

| Button | Sprite | Why |
|---|---|---|
| Collect | `sprite:sa_nav_items` | Take the crate contents |
| Give To | `sprite:sa_act_give` | Hand offering a parcel |
| Items | `sprite:sa_nav_items` | Open crate |
| Edit | `sprite:sa_act_repair` | Wrench and hammer |
| Add Item / Add Held | `sprite:sa_act_give` | Adding to the parcel |
| Clear Items / Remove / Delete | `sprite:sa_act_ban` | Prohibition hammer |
| Name / Description | `sprite:sa_nav_logs` | Written record |
| Permission | `sprite:sa_permission_gui` | Key and badge |
| Cooldown | `sprite:sa_act_time` | Alarm clock |
| Limits / Max Uses | `sprite:sa_act_warn` | Restriction warning |
| Max Per Player | `sprite:sa_nav_players` | Player-specific |
| Disable | `sprite:sa_act_freeze` | Freeze the kit |
| Enable | `sprite:sa_act_godmode` | Shield / enabled |
| Hide / Show | `sprite:sa_act_spectate` | Visibility eye |
| Refresh | `sprite:sa_plugin_hot_reload` | Circular refresh arrows |
| Help | `sprite:sa_nav_help` | Question-mark sign |
| Back / Home / Prev / Next / Cancel | (none) | Navigation stays text-only so labels get full width |

## Screen banner and background

| Screen | Art |
|---|---|
| Home | `sprite:sa_admin_kits` |
| Collect list | `sprite:sa_nav_items` |
| Manage list | `sprite:sa_nav_logs` |
| Kit detail | `sprite:sa_admin_kits` |
| Kit editor | `sprite:sa_act_repair` |
| Limits | `sprite:sa_act_time` |
| Item list | `sprite:sa_nav_items` |
| Item detail | `item:<internalName>` |
| Delete confirmation | `sprite:sa_act_ban` |
| Clear-items confirmation | `sprite:sa_act_warn` |
| Help | `sprite:sa_nav_help` |

## Client install

Server Assist loads every `UIAtlases/UIAtlas/*.png` into the global UI atlas at
launch. Install the `AAZ_ServerAssistArt` mod folder on the server and on every
client, or merge its `UIAtlases` folder into `AA0_ServerAssistMod`:

```text
Mods/AAZ_ServerAssistArt/
  ModInfo.xml
  UIAtlases/
    UIAtlas/
      sa_admin_kits.png
      sa_nav_items.png
      sa_nav_logs.png
      sa_dynamic_backpack.png
      sa_act_repair.png
      sa_act_give.png
      sa_act_ban.png
      sa_act_warn.png
      sa_act_time.png
      sa_act_freeze.png
      sa_act_godmode.png
      sa_act_spectate.png
      sa_permission_gui.png
      sa_nav_players.png
      sa_daily_rewards.png
      sa_plugin_hot_reload.png
      sa_nav_help.png
      ...
```

If a row falls back to the hammer, the client is missing that PNG or the sprite
name is wrong. The `item:` icons depend on the matching item mod being installed
on the client; unknown items fall back to `ui_game_symbol_hammer` by design.
