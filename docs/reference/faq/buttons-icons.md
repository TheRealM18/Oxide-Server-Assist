# Buttons, icons and permissions

Part of the [FAQ](index.md).

## Buttons

### What is a shared button?

A server-wide button stored once in `data.Shared`, visible to players who pass its permission. See [Buttons](../../core/buttons.md#scopes).

### What is a personal button?

A per-player button stored under `data.Personal[playerId]`, created with `serverassist.custom`.

### Can players create buttons?

Yes, if `AllowPlayerCustomButtons` is true and they have `serverassist.custom`.

### Can players run commands?

Personal command buttons are disabled by default. An admin can enable `AllowPlayerCommandButtons`. Shared command buttons are created by admins.

### Does a command button grant admin?

No. It runs a command through the client's local console. Server-authoritative commands still check the player's permissions.

### How do I make a button visible to everyone?

Leave the permission blank.

### How do I make a button visible only to a group?

Create it with `auto`, then grant the generated `serverassist.button.<id>` to the group.

### Why is my button missing?

It is disabled, permission-gated, on another page, or personal to another player. See [Button missing](../troubleshooting/runtime.md#button-missing).

### How many buttons can I have?

60 shared and 20 personal by default. The config clamps shared to 1–200 and personal to 0–100.

### How many fit on one page?

Ten cards. Additional buttons paginate.

### Can I reorder buttons?

Yes. Move Up / Move Down in the editor rewrites the order values.

### Can I delete a button permanently?

Yes. Delete requires typing `DELETE`. Back up the data file before large cleanups.

### What does "auto" mean in the permission prompt?

It creates a per-button node: `serverassist.button.<id>` for shared, `serverassist.personal.<id>` for personal.

---

## Icons

### How do I use an item icon?

```text
item:gunPistol
item:casinoCoin
```

Or use **Find Item Icon** in the editor. See [Icons](../../core/icons.md).

### How do I use a vanilla sprite?

```text
sprite:ui_game_symbol_map
```

A plain sprite name also works.

### How do I use a custom atlas?

```text
atlas:MyAtlas:mySprite
```

The sprite must be loaded on the client. Server Assist does not load atlases.

### Why is the icon a hammer?

The item, sprite or atlas could not be resolved; the client fell back. See [Icons and item icons](../troubleshooting/companions.md#icons-and-item-icons).

### Can I color an icon?

Yes, with `IconTint`:

```text
#FFCC66
#FFCC6680
```

### What is a badge?

Short metadata shown as a tooltip prefix, such as `PLUGIN`, `PERSONAL` or `OFF`.

### Why does my custom icon not appear after a game update?

Item icons follow the game. Custom atlas sprites may need a rebuild or a new atlas registration.

---

## Item search

### How does the picker search?

It filters `ItemClass.ItemNames` by internal name and localized name, case-insensitive substring. See [Item Search](../../core/item-search.md).

### Why are no items shown?

The query is too narrow or the item is not on the server. Clear the query and browse.

### Why does a modded item not appear?

The mod may not be loaded on the server at startup, or `ItemClass.ItemNames` has not rebuilt. Restart the server.

### Does it store the icon path?

No. It stores `item:<internalName>`, which follows game updates.

---

## Permissions

### What are the core nodes?

```text
serverassist.use
serverassist.admin
serverassist.custom
serverassist.button.<id>
serverassist.personal.<id>
```

See [Permissions](../../core/permissions.md).

### Who gets them by default?

Administrators get all three core nodes. The default players group gets `serverassist.use` and `serverassist.custom` when the config flags are true.

### Do admins bypass button permissions?

Yes. `CanLaunch` returns true for an admin if the button is enabled.

### Do admins see disabled buttons?

Yes, when `AdminSeesAllButtons` is true (the default).

### Why can a player open the board but see nothing?

They have `serverassist.use` but none of the buttons' permissions, and no blank-permission buttons exist.

### Can I use another plugin's permission on a button?

Yes, for example `adminshop.use`. Server Assist will not register or own that node.

### Does Server Assist register old permission nodes?

No. Re-grant the current `serverassist.*` nodes. See [Migration](../migration/index.md#permission-mapping).

### Where do I manage permissions visually?

With PermissionGui, which registers a `[GUI] Permission Manager` row. `/permgui` or `/perm gui` also opens it.

---
