# AdminKits

> **Version:** 1.1.0 · **Type:** Server-side Oxide plugin with a Server Assist GUI panel (no client files) · **Source:** [AdminKits.cs](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/AdminKits/oxide/plugins/AdminKits.cs)

AdminKits adds item kits to the Server Assist GUI. Admins can **make** kits — empty kits or kits copied from their own inventory — and players or staff can **collect** a kit straight into an inventory. The whole kit editor and collection browser lives inside the existing Server Assist window.

AdminKits ships no client DLL, XUi XML or custom net package. Server Assist renders it through its GUI hook API. Items are stored with the game's own `ItemValue` serializer, base64 encoded, so quality, durability and installed mods survive a server restart. A kit is not a text list of item names that is rebuilt on every claim.

Here, **make** means Create Kit, Create From Inventory, Add Item, Add Held and Give To; **collect** means moving the kit's items into an inventory from the GUI or with `/kit <name>`.

---

## Requirements

| Requirement | Detail |
|---|---|
| Server | `Mods/AA0_ServerAssistMod` folder with `ServerAssistMod.dll`, `ModInfo.xml` and `Config` |
| Server Assist plugin | `oxide/plugins/ServerAssist.cs` version 2.1.0 or newer |
| AdminKits plugin | `oxide/plugins/AdminKits.cs` |
| Every client | The `Mods/AA0_ServerAssistMod` folder |
| Icons | `AAZ_ServerAssistArt/Mods` with the `sa_*` icon atlas; optional but recommended |
| EAC | Off |
| Client files for AdminKits | None |

AdminKits is server-only. Clients do not install an AdminKits mod. The server and clients must use the same Server Assist files. Without the icon pack, kit cards still work and fall back to the vanilla hammer symbol.

---

## Installation

1. Stop the server and the game.
2. Make sure Server Assist is installed on the server and on every client.
3. Copy `oxide/plugins/AdminKits.cs` into the server's `oxide/plugins` folder. Oxide compiles it on startup.
4. Start the server.
5. Join and press F7/F6, or type `/kits` or `/kit`, then open `Admin Kits`.
6. If you are not an Oxide administrator, grant the permissions below.

!!! warning
    Do not put the `.cs` file in the game's `Mods` folder. The mod loader does not compile `.cs` files; Oxide must compile the plugin from `oxide/plugins`.

On first load the plugin creates:

```text
oxide/config/AdminKits.json
oxide/data/AdminKits/kits.json
```

The administrators group receives `adminkits.admin`, `adminkits.use` and `adminkits.give` automatically. The players group receives `adminkits.use` when `GrantUseToDefaultGroup` is true, which is the default, so public kits work immediately.

A successful registration logs:

```text
[AdminKits] registered ServerAssist GUI hook 'adminkits'.
```

---

## First-time permissions

Players who should collect public kits:

```text
oxide.grant group players adminkits.use
```

Staff who should create and edit kits:

```text
oxide.grant group moderators adminkits.use
oxide.grant group moderators adminkits.admin
oxide.grant group moderators adminkits.give
oxide.grant group moderators serverassist.use
```

Per-kit permissions are optional. A kit with a blank permission is available to everyone who has `adminkits.use`. A kit with a permission node is only visible to players who have that node. If you type `auto` in the kit's Permission field, AdminKits creates:

```text
adminkits.kit.<kit-id>
```

and registers it with Oxide so it appears in PermissionGui. Grant that node to a group or a single player with PermissionGui, `/perm` or the normal Oxide commands.

---

## Opening the kit window

| Entry | Requirement |
|---|---|
| `/kits` | `adminkits.use` or `adminkits.admin` |
| `/kit` | Alias of `/kits` |
| F7 or F6 | Server Assist's keys |
| `[GUI] Admin Kits` home row | Config `HookPermission`, default `adminkits.use` |

Server Assist itself also requires `serverassist.use`. When Server Assist opens a GUI hook, its Home / Shared / My Buttons tabs are replaced by the hook view and a Back button. Every action stays inside the same Server Assist window; Back returns to the Server Assist board.

---

## Making a kit

1. Open Admin Kits → Manage Kits → Create Kit for an empty kit, or From Inventory to copy your toolbelt, backpack and equipment.
2. Type a name. The kit editor opens.
3. Press Items → Add Item and type `item [amount] [quality]`, for example `9mmBullet 100 6` or `foodCanChili 10`.
4. Internal names (`9mmBullet`) and display names (`9mm Bullet`) both work. Surrounding spaces are ignored.
5. Alternatively, hold an item and press Items → Add Held to copy the exact stack, including quality, durability and mods.
6. Optionally set Permission, Cooldown, Limits and Description. Press Back to see the detail screen, then Give To or leave the kit available for Collect.

Create From Inventory copies every non-empty toolbelt, backpack and equipment slot and preserves item data. `ClearInventoryOnCapture` is false by default, so the originals stay; set it to true for a true "collect my inventory into a kit and empty it" workflow.

### Kit editor buttons

| Button | Behaviour |
|---|---|
| Name | Rename the kit |
| Permission | Blank = everyone, `auto` = `adminkits.kit.<id>`, or any node |
| Cooldown | Minutes between collections; `0` = none |
| Limits | Total uses and uses per player; `0` = unlimited |
| Description | One-line description shown in the detail screen |
| Items | Add, inspect, remove or clear item stacks |
| Add Held | Copy the exact item stack currently in your hand |
| Enable/Hide | Control availability without deleting the kit |
| Delete | Permanent delete after a confirmation screen |

Kit ids are generated from the name. They are used internally and in commands; the GUI always shows friendly names.

---

## Collecting a kit

GUI: Collect a Kit → choose a kit → Collect.

Chat:

```text
/kit <name>
/kit list
```

A collection is refused when:

- the kit is disabled,
- you do not have the kit's permission,
- the kit reached its total use limit,
- you reached your per-player limit,
- the cooldown is still active,
- you do not have enough bag/toolbelt space for the whole kit,
- you are not spawned in the world.

AdminKits checks inventory space before it gives anything, so a full inventory does not consume the cooldown or silently eat half a kit.

Staff with `adminkits.admin` bypass the permission, use limits and cooldown when they collect a kit themselves. Staff with `adminkits.give` can use Give To on another online player. Give To still checks that the target has enough space and does not use that player's cooldown.

Hidden kits are not listed, but they still work with `/kit <name>` unless they are disabled or the player lacks the permission. Admins see hidden enabled kits in the collect list and all kits in Manage Kits.

---

## Commands

| Command | Permission | Behaviour |
|---|---|---|
| `/kits`, `/kit` | `adminkits.use` | Open the Admin Kits window |
| `/kit <name>` | `adminkits.use` plus the kit's permission, if set | Collect a kit; the name may contain spaces |
| `/kit list` | `adminkits.use` | List the kits you can collect |
| `/kit help` | `adminkits.use` | Short in-chat help |
| `/kitadmin`, `/kitadmin help` | `adminkits.admin` or `adminkits.give` | Show the admin command reference |
| `/kitadmin list` | `adminkits.admin` | List every kit with id, name, stack count, state, uses and permission |
| `/kitadmin create <name>` | `adminkits.admin` | Create an empty kit; names may contain spaces |
| `/kitadmin frominv <name>` | `adminkits.admin` | Create a kit from your toolbelt, backpack and equipment |
| `/kitadmin delete <name> confirm` | `adminkits.admin` | Delete a kit and all of its claim records |
| `/kitadmin enable <id or name>` | `adminkits.admin` | Enable a kit |
| `/kitadmin disable <id or name>` | `adminkits.admin` | Disable a kit |
| `/kitadmin additem <id or name> <item> [amount] [quality]` | `adminkits.admin` | Add an item stack |
| `/kitadmin removeitem <id or name> <index>` | `adminkits.admin` | Remove one item stack using the index shown in the GUI |
| `/kitadmin give <player> <id or name>` | `adminkits.give` | Give a kit to an online player |
| `/kitadmin reset <player> <id or name>` | `adminkits.admin` | Clear that player's claim count and cooldown for the kit |
| `/kitadmin reload` | `adminkits.admin` | Reload config, kit data, per-kit permissions and the ServerAssist hook |

Examples:

```text
/kitadmin additem starter 9mmBullet 100 6
/kitadmin additem Starter Kit foodCanChili 10
```

---

## Permissions

| Permission | Purpose |
|---|---|
| `adminkits.use` | Open the kit window and collect public kits; granted to the players group when `GrantUseToDefaultGroup` is true |
| `adminkits.admin` | Create, edit, enable, hide and delete kits; use the admin GUI screens and commands; granted to Oxide's administrators group automatically |
| `adminkits.give` | Give kits to other players; administrators receive this automatically |
| `adminkits.kit.<id>` | Optional per-kit permission created with `auto`; registering the node is automatic |

The hook permission is `HookPermission` (default `adminkits.use`). Changing it changes who sees the home card, but the plugin still enforces the individual kit and admin permissions inside the view.

---

## Configuration

File: `oxide/config/AdminKits.json`. Values are clamped to safe ranges on load.

| Key | Default | Notes |
|---|---|---|
| `ChatPrefix` | `[Kits]` | Chat prefix for command replies |
| `HookLabel` | `Admin Kits` | Server Assist home-row label |
| `HookDescription` | `Create, manage and collect server kits.` | Home-row description |
| `HookPermission` | `adminkits.use` | Permission needed to see and open the hook |
| `GrantUseToDefaultGroup` | `true` | Grant `adminkits.use` to the players group |
| `DefaultCooldownMinutes` | `0` | Cooldown applied to new kits; `0` = none |
| `DefaultMaxUses` | `0` | Total uses applied to new kits; `0` = unlimited |
| `DefaultMaxPerPlayer` | `0` | Per-player uses applied to new kits; `0` = unlimited |
| `DefaultItemQuality` | `1` | Quality used when Add Item omits a quality |
| `ClearInventoryOnCapture` | `false` | Empty the source inventory after Create From Inventory; the default is copy-only |
| `CaptureEquipment` | `true` | Include equipment slots in Create From Inventory |
| `LogClaims` | `true` | Write successful collections to the server log |
| `MaxKits` | `100` | Maximum number of defined kits |
| `MaxItemsPerKit` | `50` | Maximum item stacks in one kit |

After editing the config, run `/kitadmin reload` or `oxide.reload AdminKits`.

---

## Data

File: `oxide/data/AdminKits/kits.json`

The file holds kit definitions and per-player claim records. Each kit item stores:

| Field | Meaning |
|---|---|
| `Item` | Internal item name, for display and fallback |
| `Count` | Stack amount |
| `Data` | Base64-encoded `ItemValue` written by the game's serializer |

Because the item value is serialized by the game, quality, durability and installed mods are preserved exactly.

!!! warning
    Do not hand-edit `kits.json` while the server is running. Use the GUI or commands. Editing serialized `ItemValue` data by hand can corrupt a kit.

---

## Icons and art

AdminKits uses the finished Server Assist Art set. The panel card is `sprite:sa_admin_kits`; Collect uses `sprite:sa_nav_items`, Manage Kits uses `sprite:sa_nav_logs`, Create From Inventory uses `sprite:sa_dynamic_backpack`, and kit item rows use `item:<internalName>` for the real game item icon. Permission rows use `sprite:sa_permission_gui`, cooldown `sprite:sa_act_time`, edit `sprite:sa_act_repair`, delete/clear `sprite:sa_act_ban`, and warning/limit `sprite:sa_act_warn`.

Kit rows in the collect and manage lists change icon with their state:

| State | Sprite | Badge | Tint |
|---|---|---|---|
| Normal / ready | `sprite:sa_admin_kits` | — | none |
| Permission-restricted kit you can see | `sprite:sa_permission_gui` | PERM | none |
| Permission locked | `sprite:sa_permission_gui` | LOCKED | `#FFD166` |
| Cooldown active | `sprite:sa_act_time` | WAIT | `#66CCFF` |
| Total or per-player limit reached | `sprite:sa_act_warn` | LIMIT | `#FF6B6B` |
| Disabled | `sprite:sa_act_freeze` | OFF | `#9AA0A6` |
| Hidden | `sprite:sa_act_spectate` | HIDDEN | `#C79BFF` |

The icons are client-side and provided by the `AAZ_ServerAssistArt` pack. Copy its `UIAtlases` folder into `Mods/AAZ_ServerAssistArt/UIAtlases/UIAtlas` on the server and on every client.

If a row falls back to the hammer, the client is missing that PNG or the sprite name is wrong. `item:` icons depend on the matching item mod being installed on the client; unknown items fall back to `ui_game_symbol_hammer` by design.

!!! note
    The planned `sa_kits_*` action icons are not rendered yet. AdminKits uses the closest finished icons and keeps every sprite key in the `KitIcons` class, so they can be swapped later. The full mapping is in [ICONS.md](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/AdminKits/ICONS.md).

---

## Server Assist integration

| Item | Value |
|---|---|
| Model | Panel / hook; home row shows `[GUI] Admin Kits` |
| Hook id | `adminkits` |
| Label | Config `HookLabel`, default `Admin Kits` |
| Description | Config `HookDescription`, default `Create, manage and collect server kits.` |
| Permission | Config `HookPermission`, default `adminkits.use` |
| Icon | `KitIcons.Panel`, `sprite:sa_admin_kits`, via `SetPanelIcon` |
| Plugin name | `AdminKits` |
| Handler | `HandleGuiRequest` |
| Order | Not specified by the plugin; the six-field `RegisterGuiHook` call carries no order argument |
| Entry | `/kits`, `/kit`, F7/F6, `[GUI] Admin Kits` row |

The plugin registers through the six-string hook signature:

```csharp
object result = InvokePlugin(serverAssist, "RegisterGuiHook",
    new[] { typeof(string), typeof(string), typeof(string),
            typeof(string), typeof(string), typeof(string) },
    new object[] { HookId, config.HookLabel, config.HookDescription,
                   config.HookPermission, Name, nameof(HandleGuiRequest) });

if (result is bool && (bool)result)
{
    serverAssist.Call("SetPanelIcon", HookId, KitIcons.Panel);
}
```

Registration runs from two-second and five-second timers in `Init`, from `OnServerInitialized`, and again from `OnServerAssistReady`, so reloading either plugin is safe. `/kitadmin reload` reloads config and data, re-registers per-kit permissions and registers the hook again. `Unload` unregisters with `UnregisterGuiHook("adminkits")`.

The GUI opens through `OpenHookForPlayer(playerId, "adminkits", subAction)`. Every action re-checks the Oxide permission on the server. The player id comes from Server Assist's server-side net-package sender, not from anything the client can spoof. The view uses only the standard Server Assist line protocol: title, subtitle, status, one text input, up to 10 rows and up to 10 buttons. No client update is needed when this plugin changes.

See [Staff and utility companions](../../integration/companions/staff-utility.md) and [Panels and hooks](../../integration/api/panels.md).

---

## Typical workflows

### Publish a public starter kit

1. Open Manage Kits → Create Kit and name it `Starter Kit`.
2. Add items with Items → Add Item, or hold an item and use Add Held.
3. Leave Permission blank so everyone with `adminkits.use` can see it.
4. Set a Cooldown and Limits if the kit should not be claimable repeatedly.
5. Press Back and leave the kit enabled; players collect it from Collect a Kit or with `/kit Starter Kit`.

### Restrict a kit to a group

1. Edit the kit's Permission field to `auto` or paste a node.
2. With `auto`, AdminKits creates and registers `adminkits.kit.<kit-id>`.
3. Grant that node to the group or player through PermissionGui or the Oxide commands.
4. The kit shows a permission badge; players without the node cannot see or collect it.

### Give a kit directly to a player

1. Confirm the target is online and spawned.
2. Use Give To in the GUI, or run `/kitadmin give <player> <kit>`.
3. The target's inventory must have room for the whole kit.
4. Give To does not consume that player's cooldown or claim count.

---

## Troubleshooting

| Symptom | Check |
|---|---|
| The `Admin Kits` row is missing | Confirm both plugins compiled, look for the registration log line, confirm `adminkits.use` and `serverassist.use`, and check `HookPermission` |
| `Open the ServerAssist window and choose Admin Kits` | Server Assist is not loaded, so `/kits` cannot open its window. Install `oxide/plugins/ServerAssist.cs` and the client mod |
| `The ServerAssist client mod did not respond` | The client is missing `ServerAssistMod.dll` and its `Config` folder, or has not sent its hello yet |
| Add Item says the item was not found | Use the exact internal name (`9mmBullet`) or display name (`9mm Bullet`). Mods work if loaded on the server. If the name ends in a number, use the internal name |
| A kit is missing from the collect list | A hidden kit is not listed but works with `/kit <name>` unless disabled or permission-locked. Check enabled state, hidden state and the kit permission |
| Collect says there is not enough inventory space | AdminKits requires room for the whole kit. Free bag or toolbelt slots; nothing is removed and no cooldown is consumed on a failed check |
| Give To says the player is not online | Give To targets a spawned, connected player; type the exact name or platform id |
| Items show `Unknown item` | The item class comes from a mod not loaded on the server, or the kit data came from an older save |
| Clear Inventory On Capture did not empty my inventory | Set `ClearInventoryOnCapture` to true in `oxide/config/AdminKits.json` and run `/kitadmin reload`; the default is copy-only |
| Kit rows or cards show the hammer | The client is missing the `AAZ_ServerAssistArt` sprite or the sprite key is wrong. Install the art pack on that client |

---

## Related pages

- [Server Assist product guide](../../core/server-assist.md)
- [Server Assist API](../../integration/api/index.md)
- [Staff and utility companions](../../integration/companions/staff-utility.md)
- [Panels and hooks](../../integration/api/panels.md)
- [Icons](../../core/icons.md)
- [PermissionGui](permission-gui.md)
- [Companion problems](../../reference/troubleshooting/companions.md)
