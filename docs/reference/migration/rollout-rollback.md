# Rollout and rollback

Part of the [Migration guide](index.md).

## Dynamic Backpack migration

The backpack is now a **server-only Server Assist panel**. DynamicBackpack registers a `backpack` panel with `RegisterPanel` and returns its nine pages through `HandleBackpackGui`. Server Assist owns the cards, page buttons and item icons.

Removed legacy identifiers:

```text
AdminHelper-1.2.0-backpack-1
ServerAssist-2.0.0-backpack-1
dynamicBackpack
windowDynamicBackpack
DynamicBackpackMod.DynamicBackpackServer
```

These identifiers no longer exist in 2.0. There is no backpack bridge, no backpack client DLL, no custom XUi window, no grid controller, no backpack net package and no `src/Backpack` folder in Server Assist.

| Old | New |
|---|---|
| Separate backpack client DLL | Server-only `DynamicBackpack.cs` |
| `dynamicBackpack` window group | `backpack` Server Assist panel |
| `windowDynamicBackpack` | Nine cards in the Server Assist window |
| Bridge build lookup | `RegisterPanel` / `HandleBackpackGui` |
| 9×9 grid with drag and drop | Nine pages of nine server-authoritative item cards |

### Cleanup steps

1. Stop the server and every client.
2. Delete the old standalone backpack client folder from every client.
3. Install the current Server Assist mod on the server and every client.
4. Replace `oxide/plugins/DynamicBackpack.cs` with the current server-only plugin.
5. Start the server and check:

   ```text
   [DynamicBackpack] loaded. Pages: backpack.1 - backpack.9. UI is provided by Server Assist.
   [DynamicBackpack] registered the backpack panel with Server Assist.
   ```

6. Open Server Assist and confirm the **Backpack** card, or run `/backpack`.

### Data

The 81-slot storage format is unchanged, so old data loads directly:

```text
oxide/data/DynamicBackpack/<player-id>.json
slot = (page - 1) * 9 + card
```

### Permissions

Permissions now unlock pages instead of rows:

```text
backpack.1  -> page 1
backpack.2  -> pages 1-2
...
backpack.9  -> pages 1-9
backpack.*  -> every page
backpack.admin -> AdminRows/admin override
```

The panel itself is registered with `backpack.1`, so a player needs at least page 1 to see the Backpack card.

More: [Dynamic Backpack](../../integration/dynamic-backpack.md).

---

## Companion plugin checklist

Update these plugins to look for `ServerAssist` and to use `OnServerAssistReady`:

- PermissionGui
- AdminShop
- AuctionHouse
- VoteSkipNight
- AdminKits
- AdminSuite
- DailyRewards
- DynamicBackpack
- any custom plugin that called `plugins.Find("AdminHelper")`

Search each plugin for the old name and replace it using section 1.

### Recommended compatibility order

While rolling out, a temporary fallback is acceptable:

```csharp
Plugin serverAssist = plugins.Find("ServerAssist");
if (serverAssist == null)
{
    serverAssist = plugins.Find("AdminHelper"); // temporary only
}
```

Remove the fallback after every client and plugin has been migrated. Shipping a permanent fallback keeps the old name alive and can hide a failed install.

---

## Client rollout checklist

1. Remove the old `Mods/AdminHelper` folder from every client.
2. Install `AA0_ServerAssistMod`.
3. Delete old XUi caches or restart the game completely.
4. Verify the log contains:

   ```text
   [ServerAssist] loading companion mod ...
   [ServerAssist] XUi window initialized
   ```

5. Press `F7` or `F6`, or type `/sa`.
6. Confirm the window title is **SERVER ASSIST** and the left navigation opens Home / Shared / My Buttons.
7. Confirm the old client DLL is gone:

   ```text
   Mods/AdminHelper/AdminHelperMod.dll   <- must not exist
   ```

!!! caution
    Do not run an old and new client DLL side by side. Duplicate XUi groups and net packages cause confusing window-group failures, duplicate rows and failed hellos.

---

## Server rollout checklist

1. Back up config and data.
2. Stop the server.
3. Replace `oxide/plugins/AdminHelper.cs` with `oxide/plugins/ServerAssist.cs`.
4. Replace the mod folder on the server.
5. Rename the config and data files.
6. Update companion plugins.
7. Start the server.
8. Check the log for compile errors and hook/provider registrations.
9. Run `/sa list` and `/sa reload`.
10. Test as an admin and as a normal player.

Expected log lines:

```text
[ServerAssist] registered GUI hook 'permissions' from PermissionGui.HandleServerAssistRequest
[ServerAssist] registered GUI hook 'adminshop' from AdminShop.HandleGuiRequest
[ServerAssist] registered GUI provider 'auctionhouse' from AuctionHouse.HandleAuctionGuiRequest
```

---

## Verification commands

```text
/sa
/sa list
/sa reload
```

Server log checks:

```text
[ServerAssist] registered GUI hook ...
[ServerAssist] registered GUI provider ...
```

Client log checks:

```text
[ServerAssist] XUi window initialized
[ServerAssist] window opened
```

If the window group is not found, the client logs the loaded XUi group IDs and controllers so the install can be checked. See [Troubleshooting](../troubleshooting/index.md#xui-group-diagnostics).

---

## Rollback

If the migration fails and you need to return to the old build:

1. Stop the server and all clients.
2. Restore the old mod folder on the server and every client.
3. Restore the old plugin file.
4. Restore the old config and data files.
5. Remove the new folder from every client. Do not run both.
6. Restart.
7. Re-grant the old permission nodes if you revoked them.

Keep the old backups until the new install has run successfully for a full restart cycle and one permission change.

---

## Known pitfalls

| Pitfall | Result | Fix |
|---|---|---|
| Old client folder left installed | Duplicate XUi groups, failed window lookup | Remove it from every client |
| Old and new server plugins both present | Two hook registries, duplicate rows | Keep only `ServerAssist.cs` |
| Old permission nodes only | Players see an empty board | Grant `serverassist.*` |
| Old `AdminHelper.json` left in config | New defaults, old buttons missing | Rename it |
| Old data folder | Buttons missing | Rename `AdminHelper/data.json` |
| Companion still calls `plugins.Find("AdminHelper")` | Row never appears | Change to `ServerAssist` |
| Companion still defines `OnAdminHelperReady` | No re-registration after reload | Rename to `OnServerAssistReady` |
| Old XUi cache | Layout or style errors | Restart the game completely |
| Old backpack client still installed | A stale standalone client folder remains | Remove the old backpack folder; the current plugin is server-only |
| Old welcome text | Players type `/ah` and get nothing | Update MOTD and docs |

---

## Post-migration audit

- [ ] Server log has no compile errors.
- [ ] Client log has both Server Assist init lines.
- [ ] `F7` / `F6` toggles the window.
- [ ] `/sa`, `/sa list`, `/sa reload` work.
- [ ] Home shows the expected shared buttons.
- [ ] Shared management works for an admin.
- [ ] Personal buttons work for a permitted player.
- [ ] Every `[GUI]` row opens and Back returns Home.
- [ ] AuctionHouse provider opens from its command.
- [ ] Buttons with icons show the intended art.
- [ ] PermissionGui lists the new `serverassist.*` nodes.
- [ ] The backpack opens and respects pages.
- [ ] No old client DLL remains on any client.
- [ ] No old permission node is still the only grant.
- [ ] The old config/data backup is stored off-server.

---

## Related pages

- [Install](../../start/install.md)
- [Permissions](../../core/permissions.md)
- [API](../../integration/api/index.md)
- [Companion Integration](../../integration/companions/index.md)
- [Dynamic Backpack](../../integration/dynamic-backpack.md)
- [Troubleshooting](../troubleshooting/index.md)
