# Migration from AdminHelper to Server Assist 2.0

!!! warning
    This page intentionally keeps the old product name **AdminHelper** so server owners and companion plugin authors can find and replace it. It is the only page in this wiki that should contain that name. Do not install a second AdminHelper client next to Server Assist.

Server Assist 2.0 is the successor to AdminHelper 1.x. The product name, runtime identities, folder names, commands, permissions, config paths and API hook names all changed.

This page is a complete migration sheet.

---

## Fast replacement table

Use these replacements in companion plugins, configs, scripts, documentation and server notes.

| Old | New |
|---|---|
| `AdminHelper` | `ServerAssist` |
| `AdminHelperMod` | `ServerAssistMod` |
| `Admin Helper` | `Server Assist` |
| `adminHelper` | `serverAssist` |
| `adminhelper` | `serverassist` |
| `AdminHelperWindowGroup` | `ServerAssistWindowGroup` |
| `AdminHelperWindow` | `ServerAssistWindow` |
| `AdminHelperClient` | `ServerAssistClient` |
| `AdminHelperServer` | `ServerAssistServer` |
| `AdminHelperBridge` | `ServerAssistBridge` |
| `AdminHelperProtocol` | `ServerAssistProtocol` |
| `AdminHelperView` | `ServerAssistView` |
| `AdminHelper-1.2.0-backpack-1` | Removed legacy bridge id — no backpack bridge in 2.0 |
| `ServerAssist-2.0.0-backpack-1` | Removed legacy bridge id — no backpack bridge in 2.0 |

The same replacements apply to file names and XUi controller strings. Backpack-specific removals are listed in section 8.

---

## Folder and file mapping

| Old | New |
|---|---|
| `Mods/AdminHelper/` | `Mods/AA0_ServerAssistMod/` |
| `AdminHelperMod.dll` | `ServerAssistMod.dll` |
| `Config/XUi_InGame/xui.xml` | `Config/XUi_InGame/xui.xml` |
| `Config/XUi_InGame/windows.xml` | `Config/XUi_InGame/windows.xml` |
| `oxide/plugins/AdminHelper.cs` | `oxide/plugins/ServerAssist.cs` |
| `oxide/config/AdminHelper.json` | `oxide/config/ServerAssist.json` |
| `oxide/data/AdminHelper/data.json` | `oxide/data/ServerAssist/data.json` |

### XUi identity

| Old | New |
|---|---|
| Window group `adminHelper` | Window group `serverAssist` |
| Window `windowAdminHelper` | Window `windowServerAssist` |
| Controller `AdminHelperWindowGroup, AdminHelperMod` | `ServerAssistWindowGroup, ServerAssistMod` |
| Controller `AdminHelperWindow, AdminHelperMod` | `ServerAssistWindow, ServerAssistMod` |

A client with the old DLL cannot use the new server. Remove the old folder from every client before installing Server Assist.

---

## Command mapping

| Old | New |
|---|---|
| `/ah` | `/sa` |
| `/ah open` | `/sa open` |
| `/ah list` | `/sa list` |
| `/ah add ...` | `/sa add ...` |
| `/ah remove ...` | `/sa remove ...` |
| `/ah reload` | `/sa reload` |
| `/adminhelper` | `/serverassist` |
| `!ah` | `!sa` |
| `!adminhelper` | `!serverassist` |

The client console command is now `serverassist` / `sa`.

### Chat aliases

| Old | New |
|---|---|
| `!ah` | `!sa` |
| `!adminhelper` | `!serverassist` |
| `/ah` | `/sa` |
| `/adminhelper` | `/serverassist` |

Update any welcome message, Discord pinned post, website or MOTD that mentions the old commands.

---

## Permission mapping

| Old | New |
|---|---|
| `adminhelper.use` | `serverassist.use` |
| `adminhelper.admin` | `serverassist.admin` |
| `adminhelper.custom` | `serverassist.custom` |
| `adminhelper.button.<id>` | `serverassist.button.<id>` |
| `adminhelper.personal.<id>` | `serverassist.personal.<id>` |

Server Assist 2.0 does **not** grant the old permission nodes. Re-grant the new nodes to groups and users.

### Group grants

```text
oxide.grant group admin serverassist.use
oxide.grant group admin serverassist.admin
oxide.grant group admin serverassist.custom

oxide.grant group players serverassist.use
oxide.grant group players serverassist.custom
```

### User grants

```text
oxide.grant user 76561198000000000 serverassist.use
oxide.grant user 76561198000000000 serverassist.admin
```

### Per-button grants

Map them one for one:

```text
adminhelper.button.shop      ->  serverassist.button.shop
adminhelper.personal.my-kits ->  serverassist.personal.my-kits
```

### Revoke the old nodes

After verifying the new grants:

```text
oxide.revoke group admin adminhelper.use
oxide.revoke group admin adminhelper.admin
oxide.revoke group admin adminhelper.custom
```

Repeat for every group and user that held an old node. Leaving the old nodes in place is harmless to Server Assist but confusing to staff and to PermissionGui searches.

---

---

## Migration pages

| Page | Covers |
|---|---|
| [API and data migration](api-data.md) | API mapping, view protocol additions and config/data migration |
| [Rollout and rollback](rollout-rollback.md) | Dynamic Backpack, companion checklist, client/server rollout, verification, rollback and pitfalls |
