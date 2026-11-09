# Runtime problems

Part of [Troubleshooting](index.md).

## Plugin not loaded

### `The ServerAssist Oxide plugin is not loaded on this server.`

Check:

1. `oxide/plugins/ServerAssist.cs` exists.
2. The server log has no compile error.
3. The filename matches the class and the plugin name.
4. Oxide is loaded.
5. The plugin is not disabled by a server manager.

Run:

```text
oxide.plugins
oxide.reload ServerAssist
```

If the plugin fails to compile, fix the reported line before doing anything else. Companion plugins cannot register against a plugin that is not loaded.

---

## Client mod not installed

### `The ServerAssist client mod is not installed or has not sent its hello yet.`

Check:

1. `Mods/<folder>/ServerAssistMod.dll` exists on the client.
2. `ModInfo.xml` exists and has `<Name value="ServerAssist" />`.
3. `Config/XUi_InGame/xui.xml` and `windows.xml` exist.
4. The client fully restarted after installation.
5. The client is not running an old DLL.
6. The server did not restart while the client stayed connected; rejoin.

The server tracks capability per client number. A reconnect normally re-sends the hello. If not, a full game restart is the reliable fix.

---

## Empty window

The window opens but no cards appear.

| Check | Action |
|---|---|
| Server log | Look for `[ServerAssist] GUI request failed` or a hook/provider exception |
| Client log | Look for net package registration errors |
| Plugin loaded | Run `/sa list` from the server console |
| Permission | Does the player have `serverassist.use`? |
| Buttons | Are any enabled and visible? |
| Hooks | Does a companion register? |
| Response | Is the server returning a non-empty view? |

The window requests `a|open`; the server returns a view. If the response is empty, the client keeps its pending "Waiting for server" view or shows the last view.

### No buttons at all

A fresh install starts with an empty shared list and does not seed any example buttons. If the list is empty:

- no shared buttons have been created yet;
- the buttons may all be disabled or permission-gated;
- a hook or provider may not have registered yet.

Create one through **Shared** as an admin.

---

## Button missing

| Check | How |
|---|---|
| `Enabled` | Shared manager shows `[on]` or `[off]` |
| Permission | Manager subtitle shows the node |
| Player has the node | `oxide.show user <id>` |
| `AdminSeesAllButtons` | Admins see disabled buttons only when true |
| Scope | Personal buttons are per player |
| Page | The button may be on page 2 |
| Search/filter | There is no filter; check the right tab |

A blank permission means everyone who can open the board sees it. A set permission hides it until granted.

### Personal button missing for its owner

- Confirm `AllowPlayerCustomButtons` is true.
- Confirm the player has `serverassist.custom`.
- Confirm the button is in `data.Personal[playerId]`.
- Confirm the owner still holds `serverassist.personal.<id>` if it uses `auto`.
- An admin can revoke that node; the button then disappears for the owner but remains in data.

---

## Permission denied

### Board says no permission

```text
You do not have permission to use the Server Assist.
```

Grant `serverassist.use` or use an administrator account.

### Shared tab says admin required

```text
You need the 'serverassist.admin' permission to manage shared buttons.
```

Grant `serverassist.admin` only to staff.

### My Buttons says custom required

```text
You need the 'serverassist.custom' permission to manage personal buttons.
```

Grant `serverassist.custom`, and verify `AllowPlayerCustomButtons`.

### Button launch says no permission

```text
You do not have permission to use '<label>'.
```

Grant the button's permission node. Check that you edited the right button and that the player is not on an old session; reopen the board after granting.

### Hook row is hidden

Grant the hook's permission (`permissiongui.use`, `adminshop.use`, …). Admins always pass hook visibility.

### Permission changes do not apply

- Reopen the board.
- Run `/sa reload` (re-registers button permissions, not hook permissions).
- Reload the companion plugin.
- Check the player's group membership and inherited groups.
- With PermissionGui, run `/perm whoami` and inspect the effective permissions.

---
