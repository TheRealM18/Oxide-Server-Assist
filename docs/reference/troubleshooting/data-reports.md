# Data and reports

Part of [Troubleshooting](index.md).

## Config and data

### Config did not load

The plugin logs:

```text
[ServerAssist] config could not be read: ...
```

It falls back to defaults and writes a normalized config. Fix the JSON and run `/sa reload`, or delete the file and restart to regenerate.

### Data did not load

The plugin logs:

```text
[ServerAssist] data could not be read: ...
```

It starts with empty data. Restore from backup or recreate the buttons.

### Buttons disappeared after a restart

- Check the data file path: `oxide/data/ServerAssist/data.json`.
- Check file permissions.
- Confirm the server did not start with a different `oxide/data` root.
- Restore the backup.

### Do not edit live data

The plugin keeps data in memory. Editing `data.json` while the server runs can be overwritten on the next save. Stop the server or use the GUI.

---

## Server-side errors

| Log | Meaning | Action |
|---|---|---|
| `GUI request failed` | An exception escaped a handler | Read the stack trace; fix the companion |
| `GUI hook '<id>' failed` | Hook handler threw | Read the inner exception |
| `GUI provider '<id>' failed` | Provider handler threw | Read the inner exception |
| `permission '<node>' could not be registered` | Duplicate/owned node or invalid name | Check ownership |
| `OnServerAssistReady notification failed` | A companion ready hook threw | Fix that companion |
| `could not open the client window` | Reflection bridge failed | Check the client DLL and player id |

---

## Collecting a useful report

Include:

1. Server and client Server Assist versions.
2. Game version.
3. Oxide version.
4. Whether EAC was off.
5. The exact error text.
6. Server log lines containing `[ServerAssist]`, `[DynamicBackpack]` and the companion name.
7. Client log lines containing `[ServerAssist]`.
8. A screenshot of the window.
9. The relevant `oxide/config/ServerAssist.json` with secrets removed.
10. Whether the issue happens for admins, players or both.

Do not paste Steam IDs, IP addresses or passwords into a public report.

---

## Repair sequence

When in doubt, do a clean client repair:

1. Exit the game.
2. Rename `Mods/AA0_ServerAssistMod` to `AA0_ServerAssistMod.old`.
3. Install the exact server build into a fresh folder.
4. Delete old XUi caches if your pack uses them.
5. Start the game.
6. Join.
7. Check the client log for the two init lines.
8. Press `F7`.
9. If it still fails, check the diagnostic line and the folder layout again.

A clean server repair:

1. Stop the server.
2. Back up config and data.
3. Remove `oxide/plugins/ServerAssist.cs`.
4. Remove the mod folder.
5. Reinstall both from the same release.
6. Start.
7. Check the compile and load lines.
8. Run `/sa reload`.

---

## Related pages

- [Install](../../start/install.md)
- [Server Assist](../../core/server-assist.md)
- [UI](../../core/interface.md)
- [Icons](../../core/icons.md)
- [Item Search](../../core/item-search.md)
- [Permissions](../../core/permissions.md)
- [Dynamic Backpack](../../integration/dynamic-backpack.md)
- [FAQ](../faq/index.md)
