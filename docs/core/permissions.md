# Permissions

Server Assist uses Oxide's permission library for every check. Permissions are evaluated on the server, at request time, using the player id derived from the connected client. The client never supplies or overrides its own identity.

---

## Core nodes

| Node | Grants | Default grant |
|---|---|---|
| `serverassist.use` | Open the board and browse Home | Default players group when `GrantUseToDefaultGroup` is true; administrators always |
| `serverassist.admin` | Manage shared buttons, see all buttons (subject to config) and run `/sa` admin commands | Administrators group automatically |
| `serverassist.custom` | Create and edit personal buttons | Default players group when `GrantCustomToDefaultGroup` is true |
| `serverassist.button.<id>` | Access one shared button created with `auto` | Not granted automatically |
| `serverassist.personal.<id>` | Access one personal button created with `auto` | Granted to the creator of that personal button |

Oxide administrators (`serverassist.admin`) bypass `CanUse`, `CanManage` and `CanLaunch`.

---

## What each node actually gates

### `serverassist.use`

Checked in `CanUse` for every normal board request:

```text
CanUse(player) = IsAdmin(player) OR UserHasPermission(player, "serverassist.use")
```

It does not gate provider requests. A provider is responsible for its own permission checks and is routed before `serverassist.use` so a player can use AuctionHouse without seeing the board.

It also does not gate hook rows directly: a hook has its own permission, and the board must already be open for the row to be visible.

### `serverassist.admin`

Used by:

- `CanManage(player, "shared")` — always true for an admin.
- `CanManage(player, "personal")` — admins can edit any personal button.
- `CanLaunch` — admins can launch any enabled button.
- `IsVisibleOnHome` — admins see disabled buttons when `AdminSeesAllButtons` is true.
- `/sa list`, `/sa add`, `/sa remove`, `/sa reload`.
- The Shared tab.
- Permission bypass for hook rows and provider visibility is provider-specific, but hooks treat admins as always allowed.

### `serverassist.custom`

Used only for personal scope:

```text
CanManage(player, "personal") =
    IsAdmin(player)
    OR (AllowPlayerCustomButtons
        AND UserHasPermission(player, "serverassist.custom"))
```

It does not grant access to shared buttons and does not grant `serverassist.use`. Grant both to players who should see the board and create private buttons.

### Per-button nodes

A button with a permission is visible and launchable only for players holding that node (or admins).

| Button scope | Auto node |
|---|---|
| Shared | `serverassist.button.<id>` |
| Personal | `serverassist.personal.<id>` |

`auto` is resolved when the button is created. Editing the permission to `auto` on an existing button resolves immediately using the existing id.

---

## Permission ownership

Server Assist registers only nodes in its own namespaces:

```text
serverassist.button.*
serverassist.personal.*
```

It deliberately ignores a permission such as `adminshop.use`, `permissiongui.use` or `voteskip.use`. Those nodes belong to their companion plugins. Registering them here would steal ownership and break the other plugin's unload/reload lifecycle.

This means:

- A shared button can point at `adminshop.use` even though Server Assist does not own it.
- If AdminShop is removed, the button still exists but the node may no longer be registered.
- PermissionGui shows all registered nodes, including the other plugin's nodes.
- Permission rows use three category icons: `sa_nav_server` for `oxide.*`, `sa_nav_world` for `7daystodie.*` / `game.*`, and `sa_plugin_hot_reload` for every other plugin node.
- Both 7 Days to Die command types use `sa_nav_world`: SdtdConsole commands and `serveradmin.xml` command permissions.
- These three sprites ship in `AAZ_ServerAssistArt` 1.1.0 or newer. The 1.1.1 pack also provides `sa_perm_oxide`, `sa_perm_game` and `sa_perm_plugin` aliases.
- PermissionGui's home and sub-menu rows use the matching nav/action art. Server Assist 2.1 also supports optional icons and tints on action buttons; companions that supply them get icon-and-label buttons.

---

## Personal auto-grant safety

A personal button created with `auto` grants its creator the exact node:

```text
serverassist.personal.<buttonId>
```

The grant only happens when:

```text
def.Permission == "serverassist.personal." + def.Id
```

If the player typed a different permission by hand, Server Assist does **not** grant it. This prevents a player from creating a personal button with `serverassist.admin` or `adminshop.use` and self-granting it.

Admins can revoke the personal node later with the normal permission commands or PermissionGui.

---

## Visibility rules

```text
IsVisibleOnHome(player, button):
    if admin:
        return AdminSeesAllButtons OR CanLaunch(player, button)
    return CanLaunch(player, button)

CanLaunch(player, button):
    if button is null or !button.Enabled:
        return false
    if admin:
        return true
    if button.Permission is blank:
        return true
    return UserHasPermission(player, button.Permission)
```

Consequences:

- A disabled button is invisible to normal players.
- A button with a blank permission is visible to every player who can open the board.
- A button with a permission is invisible until the player holds it.
- Admins can see disabled and unlaunchable shared buttons when `AdminSeesAllButtons` is true.
- Personal buttons are stored per player, so another player cannot see them even with the same permission, unless they are an admin.

---

## Hook permissions

A GUI hook has its own permission, set at registration:

```csharp
serverAssist.Call(
    "RegisterPanel",
    "adminshop",
    "Admin Shop",
    "Buy items with dukes.",
    "adminshop.use",
    "AdminShop",
    "HandleGuiRequest",
    "ui_game_symbol_hammer",
    10);
```

Check:

```text
CanUseGuiHook(player, hook) =
    IsAdmin(player)
    OR hook.Permission is blank
    OR UserHasPermission(player, hook.Permission)
```

This check runs when the hook row is built, when the hook is opened and on every forwarded hook action. If permission is revoked while a player is inside the hook, the next action returns them to the board with a message.

A blank hook permission means everyone who can open the board can see and use the hook. That is a visibility choice, not a security boundary: the companion must still validate every action.

---

## Provider permissions

Providers appear on Home as `[GUI] <title>` cards with the `PROVIDER` badge on current builds, and can also be opened by their own command. The card is visible to everyone who can open the board, so the provider must check its own permission when the card is clicked and on every action. Server Assist routes provider requests before `serverassist.use`:

```text
if session.ActiveProvider != null:
    route to provider
```

The provider handler receives the player id and payload. It must enforce its own permission on every action.

!!! caution
    A provider that trusts the client or only checks permission once at open time can be abused. Re-check permission and object ownership inside every action branch.

---

## Granting permissions

### Oxide console

```text
oxide.grant group admin serverassist.use
oxide.grant group admin serverassist.admin
oxide.grant group admin serverassist.custom

oxide.grant group players serverassist.use
oxide.grant group players serverassist.custom

oxide.grant user 76561198000000000 serverassist.use
oxide.grant user 76561198000000000 serverassist.admin
```

### Revoking

```text
oxide.revoke group players serverassist.custom
oxide.revoke user 76561198000000000 serverassist.button.shop
```

### Checking

```text
oxide.show user 76561198000000000
oxide.show group players
```

With PermissionGui:

```text
/perm whoami
/perm search serverassist
/permgui
```

### Chat

Inside the game, the permission commands are usually run from the server console or an admin chat command, depending on the server's Oxide command configuration.

---

## Role recipes

### Normal player

```text
serverassist.use
```

Optionally:

```text
serverassist.custom
```

This lets the player open the board and create personal shortcuts.

### Staff member

```text
serverassist.use
serverassist.custom
serverassist.admin
```

Plus the companion nodes they need:

```text
permissiongui.use
adminshop.use
adminsuite.use
adminkits.use
voteskip.use
auctionhouse.use
dailyrewards.use
pluginhotreload.use
twitch.use
buggeecore.use
playerprofiles.use
```

`serverassist.admin` is powerful: it allows shared button management and sees every shared button. Grant it only to staff who should author the board.

### Server owner

```text
serverassist.use
serverassist.admin
serverassist.custom
oxide.*
```

Use the Oxide administrators group rather than individual grants for the owner account.

### VIP / donor

```text
serverassist.use
serverassist.custom
serverassist.button.vip-shop
serverassist.button.vip-kits
```

Use per-button nodes rather than `serverassist.admin` for tiered access.

### Per-button gating

1. Create the shared button with `auto`.
2. Note the generated node, for example `serverassist.button.vip-kits`.
3. Grant it to the VIP group.
4. Verify with a non-admin account.

---

## Companion page permissions

Companion panels keep their own nodes. DynamicBackpack, for example, uses page nodes rather than Server Assist button nodes:

```text
backpack.1 ... backpack.9   unlock pages 1-9
backpack.*                  unlock every page
backpack.admin              AdminRows/admin override
```

The backpack panel is registered with `backpack.1`, so a player needs at least page 1 to see the **Backpack** card. These nodes belong to DynamicBackpack, not to Server Assist. See [Dynamic Backpack](../integration/dynamic-backpack.md#permissions).

### PlayerProfiles

```text
playerprofiles.use              open the Profiles panel and use commands
playerprofiles.slots.1 ... N    maximum number of profiles (highest granted wins)
playerprofiles.slots.*          every configured slot
playerprofiles.admin            MaxSlots, Reload and /profile reload
```

Reducing a slot permission never deletes existing profiles; it only blocks creating new ones until the player is under the limit. These nodes belong to PlayerProfiles, not to Server Assist. See [PlayerProfiles](../integration/companions/media-network.md#playerprofiles).

---

## PermissionGui integration

PermissionGui registers a home row:

```text
id:         permissions
label:      Permission Manager
permission: permissiongui.use
plugin:     PermissionGui
handler:    HandleServerAssistRequest
```

It can list, grant and revoke Server Assist's per-button nodes like any other registered permission. The nodes appear after the button is created because Server Assist registers them at create time and on load.

PermissionGui also applies configured defaults. Do not mix a `DefaultPermissions` entry for `serverassist.admin` with a group grant unless that is intentional.

See [Companion Integration](../integration/companions/core.md#permissiongui).

---

## Migration of nodes

Older installs used a different permission namespace. The mapping is documented in [Migration](../reference/migration/index.md#permission-mapping). Server Assist does not grant or honor the old nodes; re-grant the new nodes to groups and users.

---

## Security checklist

- [ ] `serverassist.admin` is granted only to staff who should manage the board.
- [ ] Players who should not see the board lack `serverassist.use`.
- [ ] Personal command buttons are disabled unless the server explicitly wants them.
- [ ] Every hook and provider re-checks permission on every action.
- [ ] Economy, inventory, moderation and world actions validate object ownership and limits.
- [ ] Per-button nodes are granted to groups rather than individual players where possible.
- [ ] `PermissionGui` admin access requires `permissiongui.admin` and `oxide.*` per its own documentation.
- [ ] Old permission nodes are removed after migration.

---

## Debugging permission problems

| Check | Command |
|---|---|
| Does the player have the node? | `oxide.show user <id>` |
| Does the group have the node? | `oxide.show group <group>` |
| Is the node registered? | PermissionGui search, or `oxide.show perm <node>` if available |
| Is the button enabled? | `/sa list`, Shared manager `[on]` / `[off]` |
| Is the button's permission blank? | Shared manager subtitle |
| Is the player an admin? | `/perm whoami` with PermissionGui |
| Is the board itself open? | `/sa` or `F7` |

If `serverassist.use` is missing, the window can still be opened by a capable client but the server returns `You do not have permission to use the Server Assist.` This is expected.

---

## Related pages

- [Buttons](buttons.md)
- [API](../integration/api/index.md)
- [Companion Integration](../integration/companions/index.md)
- [Dynamic Backpack](../integration/dynamic-backpack.md)
- [Troubleshooting](../reference/troubleshooting/index.md)
- [FAQ](../reference/faq/index.md)
