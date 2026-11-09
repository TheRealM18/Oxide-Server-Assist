# PermissionGui

> **Version:** 3.5.0 · **Type:** Server-side Oxide plugin with a Server Assist GUI panel (no client files) · **Source:** [PermissionGui.cs](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/PermissionGui/oxide/plugins/PermissionGui.cs)

PermissionGui is a permission manager for 7 Days to Die built entirely on Oxide's permission library. It manages groups, users, tracks, permissions and config-driven defaults, and adds a searchable browser for every registered permission and console command. The GUI is a Server Assist panel; the `/perm` chat commands still work when Server Assist is not installed.

PermissionGui no longer uses its own client mod. It does not read, write or create `oxide/data/PermissionGui/data.json`; Oxide owns group and user permission data. A small `state.json` stores only once-only default markers and temporary permissions.

---

## Requirements

| Requirement | Detail |
|---|---|
| Game | 7 Days to Die V3.4.x |
| EAC | Off |
| Server | Oxide / uMod with `oxide/plugins/PermissionGui.cs` |
| Server Assist server plugin | Required for the GUI only |
| Server Assist client mod | Required on every client that opens the GUI |
| Client mod for PermissionGui | Not required and no longer supported |

!!! note
    The `/perm` commands work without Server Assist. Without the client mod, `/permgui`, `/pgui` and `/perm gui` reply with a reminder to install it.

---

## Installation

1. Install Server Assist on the server and on every client.
2. Copy `oxide/plugins/PermissionGui.cs` into the server's `oxide/plugins` folder.
3. Restart the server.
4. Edit `oxide/config/PermissionGui.json`, then run:

    ```text
    oxide.reload PermissionGui
    ```

5. Remove any old PermissionGui client mod and its `Config/XUi_InGame` files from the game's `Mods` folder.

!!! warning
    Do not install a PermissionGui client mod. PermissionGui is server-only. An old client mod can still show the retired standalone window and must be removed.

A successful load logs:

```text
Loaded plugin PermissionGui v3.5.0
```

Verify in game with `/perm whoami`, `/perm data` and `/permgui`. The character needs either membership in Oxide's admin group (which receives the special admin grant) or an entry in `AutoGrantAdmin`.

---

## Opening the GUI

| Entry | Requirement |
|---|---|
| F7 → Permission Manager | `permissiongui.use` or full admin |
| `/permgui` | `permissiongui.use` or full admin |
| `/pgui` | Alias of `/permgui` |
| `/perm gui` | Alias of `/permgui` |

The Home page contains Groups, Users, Tracks, Permissions, Defaults, Apply Defaults and Reload Config. All group, user, track and permission editing is done in the GUI; the chat commands handle defaults, temporary permissions and diagnostics.

---

## Commands

Base access is `permissiongui.use`. In the Access column, **Admin** means the command requires `permissiongui.admin` and, by default, the configured admin wildcard.

| Command | Access | Behaviour |
|---|---|---|
| `/permgui`, `/pgui`, `/perm gui` | Use | Open the Permission Manager panel |
| `/perm defaults` | Use | Show the default group and default permissions |
| `/perm defaults reset <player\|all>` | Admin | Clear once-only default markers; defaults apply again on the next connect or `/perm apply` |
| `/perm adddefault <permission>` | Admin | Add a registered permission to `DefaultPermissions`, save the config and apply |
| `/perm removedefault <permission>` | Admin | Remove it from `DefaultPermissions` and revoke it from the default group and known direct holders |
| `/perm setdefaultgroup <group>` | Admin | Change `DefaultGroup`; online players join the new group but are not removed from the old one |
| `/perm search <text>` | Use | Search grantable permission nodes and commands |
| `/perm node <text>` | Use | Focused node lookup with grant instructions or closest command matches |
| `/perm apply` | Admin | Re-apply defaults and special admin grants now, even when `ApplyDefaultsOncePerPlayer` is true |
| `/perm data` | Admin | Show defaults, tracks, wildcard settings, markers, temp count, timestamps, paths and Oxide counts |
| `/perm save` | Admin | Force `permission.SaveData()` |
| `/perm reload` | Admin | Reload `oxide/config/PermissionGui.json` and apply defaults |
| `/perm cleanup` | Admin | Delete the legacy `oxide/data/PermissionGui/data.json` |
| `/perm whoami` | None | Print your id, admin/use flags, `oxide.*` result, direct permissions and groups |
| `/perm temp <player> <permission> <duration>` | Admin | Grant a temporary direct permission |
| `/perm temp remove <player> <permission>` | Admin | Remove an active temporary permission immediately |
| `/perm temp list [player]` | Admin | List active temporary permissions and remaining time |
| `/perm help` | Use | Show the command summary |

Temporary durations accept `s`, `m`, `h`, `d` and `w` suffixes and combinations such as `30m`, `2h`, `7d` and `1d12h`. A bare number means minutes.

---

## Permissions

| Permission | Purpose |
|---|---|
| `permissiongui.use` | Open the GUI and run the non-admin `/perm` commands |
| `permissiongui.admin` | Run admin commands and manage all configuration-driven defaults |
| `oxide.*` | Literal admin wildcard marker required for full admin access when `RequireWildcardForAdmin` is true |

### Admin wildcard

Full admin access requires **both** `permissiongui.admin` and the configured `AdminWildcard` (`oxide.*` by default). A player who receives `permissiongui.admin` through `DefaultPermissions` or a manual grant, but does not have `oxide.*`, is intentionally not a full admin.

The special admin grant adds both nodes, plus every currently registered permission when `GrantAllPermissionsToAdmin` is true. It is controlled by:

| Setting | Effect |
|---|---|
| `GrantAdminPermissionsToAdminGroup` | Give the special grant to Oxide's admin group |
| `AutoGrantAdmin` | Give the special grant to specific SteamID64s |

!!! note
    Oxide's normal grant API treats `oxide.*` as a wildcard pattern and would not store it literally. PermissionGui writes the literal marker directly to Oxide's group/user permission data, so it survives restarts. If `GrantAllPermissionsToAdmin` is true, Oxide's `*` expansion is also applied to the admin group or player. New permissions registered by a plugin loaded later are picked up by `/perm apply` or the next server start.

`/perm whoami` reports both checks:

```text
Admin: True/False
Admin wildcard (oxide.*): True/False
```

---

## Configuration

File: `oxide/config/PermissionGui.json`

| Key | Default | Notes |
|---|---|---|
| `ChatPrefix` | `Permissions:` | Prefix for chat replies |
| `DefaultGroup` | `default` | Group that receives the default permissions; set `""` to follow Oxide's configured default player group |
| `DefaultPermissions` | `[]` in code; `["permissiongui.use"]` in the example config | Permissions granted to `DefaultGroup` and mirrored directly to players when enabled |
| `ApplyDefaultsOnLoad` | `true` | Apply defaults when the plugin loads |
| `ApplyDefaultsOnPlayerConnect` | `true` | Apply defaults when a player connects |
| `ApplyDefaultsOncePerPlayer` | `true` | Apply the default group and direct defaults to each player only once |
| `GrantDefaultsDirectlyToPlayer` | `true` | Also grant the defaults directly to the player, not only through the default group |
| `CreateDefaultGroup` | `true` | Create `DefaultGroup` when it does not exist |
| `AddDefaultGroupForTrackUsers` | `false` | Keep the default group alongside a track group; when false the default group is removed unless it is part of the track ladder |
| `GrantAdminPermissionsToAdminGroup` | `true` | Enable the special admin grant for Oxide's admin group |
| `AdminWildcard` | `oxide.*` | Literal marker required for full admin access |
| `RequireWildcardForAdmin` | `true` | Require `permissiongui.admin` **and** the wildcard; the server console always bypasses this |
| `GrantAllPermissionsToAdmin` | `true` | Also grant every registered permission through Oxide's `*` expansion when granting special admin access |
| `AutoGrantUse` | `[]` | Per-character direct `permissiongui.use` grants |
| `AutoGrantAdmin` | `[]` in code; example includes `76561198044563889` | Per-character special admin grants |
| `Tracks` | `{}` in code; example defines `{"staff": ["default"]}` | Track name → ordered low-to-high list of group names |
| `MaxRows` | `10` | GUI rows per page, clamped to 1–10 |

A ready-to-copy example is included at `Config/PermissionGui.example.json`.

!!! warning
    `ApplyDefaultsOncePerPlayer` makes removals permanent until you deliberately reapply. Removing the default group or a default permission later will not be undone by the next connect or plugin reload. Use Defaults → Apply Now, `/perm apply`, or `/perm defaults reset <player|all>`.

### Tracks

Tracks live in the config, not in `data.json`. Each track is an ordered low-to-high list of groups. Promotion removes the other groups in that track and keeps unrelated groups.

When `AddDefaultGroupForTrackUsers` is false, the default group is removed so the player keeps one track group. Set it to true to keep the default group alongside the track group. Tracks from an old `data.json` are not migrated; recreate them in the config or the Tracks GUI page before running `/perm cleanup`.

---

## Data files

| Path | Purpose |
|---|---|
| `oxide/config/PermissionGui.json` | Configuration, `DefaultPermissions` and track definitions |
| `oxide/data/PermissionGui/state.json` | Once-only applied markers and temporary permissions; created automatically |
| `oxide/data/oxide.users.data` | Oxide player groups and direct permissions (binary protobuf) |
| `oxide/data/oxide.groups.data` | Oxide group permissions, ranks and parents (binary protobuf) |
| `oxide/data/PermissionGui/data.json` | Legacy file, no longer read or written |

!!! warning
    `oxide.users.data` and `oxide.groups.data` are binary protobuf files. Do not edit them by hand. Use PermissionGui or Oxide's permission commands.

`state.json` is not the old `data.json`. If the legacy file still exists, PermissionGui ignores it and logs a warning; delete it with `/perm cleanup`.

---

## Temporary permissions

Temporary permissions are direct user grants with an expiry. They are stored in `oxide/data/PermissionGui/state.json`, survive restarts, and are removed by a 30-second expiry timer. Granting the same permission again extends its expiry.

| Duration example | Meaning |
|---|---|
| `30s` | 30 seconds |
| `15m` | 15 minutes |
| `2h` | 2 hours |
| `7d` | 7 days |
| `2w` | 2 weeks |
| `1d12h` | 1 day and 12 hours |

In the GUI, use User → Temp Perms → Add Temp, then enter the node and the duration. Active temporary permissions show `temp <remaining>` in the user's direct permission list. Removing a temp permission, or toggling the same direct permission, clears the temp entry.

---

## Permission and command search

The search index is built from loaded runtime registries:

1. Oxide registered permissions from `permission.GetPermissions()`, with the owning plugin when reflection can resolve it.
2. Oxide plugin commands and their `PermissionsRequired` entries, read from Oxide's command table.
3. 7 Days to Die and mod console commands from `SdtdConsole.GetCommands()`, including aliases, description, source class/assembly and effective admin level.
4. `serveradmin.xml` command permissions from `GameManager.adminTools.Commands.GetCommands()` when not already in the console list.

Search terms are case-insensitive. Multiple space-separated terms are treated as OR terms. Selecting a permission row toggles it for a group or user. Selecting a command row shows its details; a 7DTD/mod console command is controlled by its admin level, not by an Oxide permission node, and the GUI says so.

Permission rows use category icons:

| Node category | Icon |
|---|---|
| `oxide.*` nodes | `sprite:sa_nav_server` |
| `7daystodie.*` and `game.*` nodes | `sprite:sa_nav_world` |
| Every other registered plugin node | `sprite:sa_plugin_hot_reload` |
| 7DTD command list entries | `sprite:sa_nav_world` |

The sprites ship in `AAZ_ServerAssistArt` 1.1.0 or newer; 1.1.1 also provides the `sa_perm_oxide`, `sa_perm_game` and `sa_perm_plugin` aliases. Icons are optional, and a missing icon keeps the normal row layout.

!!! note
    The search cannot discover a mod that uses a private permission system and never registers an Oxide permission, a console command or a serveradmin permission. Check that mod's own documentation.

---

## Server Assist integration

| Item | Value |
|---|---|
| Model | Panel / hook; home row shows `[GUI] Permission Manager` |
| Hook id | `permissions` |
| Label | `Permission Manager` |
| Description | `Groups, users, tracks, defaults and a searchable permission browser.` |
| Permission | `permissiongui.use` |
| Icon | `sprite:sa_permission_gui` via `SetPanelIcon` |
| Plugin name | `PermissionGui` |
| Handler | `HandleServerAssistRequest` (delegates to `HandleGuiRequest`) |
| Order | Not specified by the plugin; the six-field `RegisterGuiHook` call carries no order argument |
| Entry | F7 → Permission Manager, `/permgui`, `/pgui`, `/perm gui` |

The plugin registers with the six-field hook API:

```csharp
serverAssist.Call(
    "RegisterGuiHook",
    "permissions",
    "Permission Manager",
    "Groups, users, tracks, defaults and a searchable permission browser.",
    "permissiongui.use",
    "PermissionGui",
    nameof(HandleServerAssistRequest));

serverAssist.Call("SetPanelIcon", "permissions", "sprite:sa_permission_gui");
```

Registration happens from a three-second timer in `Init`, from `OnServerInitialized`, and again from `OnServerAssistReady`, so reloading either plugin is safe. `Unload` calls `UnregisterGuiHook("permissions")` inside a try/catch because Server Assist may already be unloading. `/perm reload` reloads only the PermissionGui config; use `oxide.reload PermissionGui` for a full registration cycle.

The GUI opens through `OpenHookForPlayerWithAction(player.Id, "permissions", subAction)`. Every request re-checks `CanUse` on the server: the player must be a full admin or have `permissiongui.use`. The player id comes from Server Assist's server-side sender, not from client-supplied data.

See [Core companions](../../integration/companions/core.md) and [Panels and hooks](../../integration/api/panels.md).

---

## Typical workflows

### Build a staff ladder with tracks

1. Create the groups you want, for example `helper`, `moderator` and `admin`, in the Groups page.
2. Open Tracks and create a track such as `staff` with the groups ordered low to high.
3. Enroll a player, then promote or demote them along the track.
4. Promotion removes the other groups in that track and keeps unrelated groups.
5. Leave `AddDefaultGroupForTrackUsers` false to keep one track group, or set it true to retain the default group as well.

### Grant a permission from search

1. Open Permissions or the Permission Search page.
2. Type a node, command or display name.
3. Select the permission row to add or remove it from a group or user.
4. Inherited grants are marked separately from direct grants.
5. Rows owned by a Server Assist panel show that mod's registered card icon; AdminSuite tool nodes use the matching tool or screen icon.

### Give temporary access

1. Run `/perm temp <player> <permission> 7d`, or open User → Temp Perms → Add Temp.
2. The permission appears in the user's direct list as `temp 6d 23h`.
3. Grant the same permission again to extend it.
4. Remove it early with `/perm temp remove <player> <permission>`, or let the expiry timer revoke it.

### Roll out defaults to the player base

1. Add permissions with `/perm adddefault <permission>`.
2. Set the target group with `/perm setdefaultgroup <group>` if needed.
3. Run `/perm apply` to force the defaults now.
4. Use `/perm defaults reset all` to clear once-only markers so every player receives defaults again on the next connect or apply.

---

## Troubleshooting

| Symptom | Check |
|---|---|
| The GUI page is missing | Server Assist is not installed or loaded, or hook registration failed. Check the server log; `/perm` commands still work |
| `Admin: False` even though `permissiongui.admin` is granted | `RequireWildcardForAdmin` is true. The player also needs `oxide.*`. Use `AutoGrantAdmin` or the admin group special grant |
| A permission cannot be added | Only permissions registered by a loaded plugin can be granted. New plugins may need `/perm apply` or a restart |
| Track groups are missing | The track references a group that does not exist. Create the group or remove the missing name from the track |
| `PermissionGui.json` is reset or not read | The plugin logs an error and does not overwrite a config it cannot parse. Fix the JSON and run `oxide.reload PermissionGui` |
| `data.json` still exists | It is ignored. Run `/perm cleanup` |
| The old PermissionGui window still appears | Remove the old PermissionGui client mod and its XUi files from `Mods` |
| A default keeps coming back | `ApplyDefaultsOncePerPlayer` is true and defaults were reapplied. Remove it again, or adjust `DefaultPermissions` before applying |

---

## Related pages

- [Server Assist product guide](../../core/server-assist.md)
- [Server Assist API](../../integration/api/index.md)
- [Permissions](../../core/permissions.md)
- [Core companions](../../integration/companions/core.md)
- [Panels and hooks](../../integration/api/panels.md)
- [Companion problems](../../reference/troubleshooting/companions.md)
- [Icons](../../core/icons.md)
