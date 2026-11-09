# Migration and security

Part of the [FAQ](index.md).

## Migration

### Is this the same product as the old one?

Server Assist 2.0 is the successor. The old name, DLL, window group, commands, permissions, config paths and API hook names changed. See [Migration](../migration/index.md).

### Can I run the old and new clients side by side?

No. Remove the old client folder from every client.

### Do old permission nodes still work?

No. Re-grant `serverassist.*`.

### Do old companion plugins still work?

Some compatibility methods remain, but a companion that still looks up the legacy plugin name or defines the legacy ready hook must be updated. See [Migration](../migration/api-data.md#api-mapping).

### Can I keep my old buttons?

Yes, by renaming the config/data files and the permission nodes. Or recreate them cleanly. See [Migration](../migration/api-data.md#config-and-data-migration).

---

## Security

### Can a client fake its player id?

No. The player id is derived on the server from the sending connection, not from the payload.

### Can a client launch a hidden button?

No. The server builds the view and checks `Enabled`, permission and command restrictions. A modified client can only replay actions the server already allowed.

### Is a command button dangerous?

It runs locally on the client. Server commands inside it still enforce permissions. A client-side command could change that client's local state, so only create command buttons you trust.

### Should I grant `serverassist.admin` to players?

No. It allows shared button management and sees every shared button. Grant it to staff only.

### Is a blank button permission safe?

It means everyone who can open the board can see and launch it. That is fine for a public information button, not for staff tools.

### Does a hook permission secure the feature?

No. It is visibility. The companion must re-check permissions on every action.

### Are provider requests checked against `serverassist.use`?

No. Providers are routed before that check because they own their own permissions. A provider must enforce them.

---

## Limits and compatibility

### What game version?

7 Days to Die V3.4.x managed assemblies.

### What protocol version?

View protocol `2`. Old six-field rows and five-field buttons are still accepted. The optional `@pp|` companion protocol is version `1` and is independent of the view protocol.

### Can I use an older client with a newer server?

Not recommended. Update both. A 2.0 client misses action-button icons, and old clients may miss other icons, badges, tab labels and provider behaviour. A `@pp|` feature falls back to its own safe path.

### Can I use a newer client with an older server?

Not recommended. The server may not return the fields the client expects.

### Does the window support localization?

Titles, labels and status are plain text, so a companion or a server plugin can return localized strings. The window chrome itself ships in English.

### Does it support controllers?

The window is mouse/keyboard oriented, matching the vanilla simple-button UI. Check your pack's input settings if you use a controller.

---

## Support

### What should I include in a bug report?

Versions, game version, Oxide version, EAC state, exact error, server/client logs, screenshot, and whether it affects admins, players or both. See [Collecting a useful report](../troubleshooting/data-reports.md#collecting-a-useful-report).

### Where are the logs?

- Server: `oxide/logs/` and the server console log.
- Client: the game's `Logs/` folder and the console.
- Dynamic Backpack: server log lines prefixed `[DynamicBackpack]`.

### What is the first thing to try?

A full client restart with the exact server build, then check the two `[ServerAssist]` init lines. See [Repair sequence](../troubleshooting/data-reports.md#repair-sequence).

---

## Related pages

- [Home](../../index.md)
- [Install](../../start/install.md)
- [Server Assist](../../core/server-assist.md)
- [UI](../../core/interface.md)
- [Buttons](../../core/buttons.md)
- [Icons](../../core/icons.md)
- [Item Search](../../core/item-search.md)
- [Permissions](../../core/permissions.md)
- [API](../../integration/api/index.md)
- [Companion Integration](../../integration/companions/index.md)
- [Dynamic Backpack](../../integration/dynamic-backpack.md)
- [Migration](../migration/index.md)
- [Troubleshooting](../troubleshooting/index.md)
