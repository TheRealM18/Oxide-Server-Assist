# Companion problems

Part of [Troubleshooting](index.md). For install, command, permission and configuration details, use the [plugin reference](../../plugins/index.md).

## Icons and item icons

### The icon is a hammer

The client fell back. Causes:

| Cause | Fix |
|---|---|
| `item:` name does not exist on the client | Install the item mod on the client or choose a vanilla item |
| `sprite:` name is not loaded | Use a vanilla sprite or ship the atlas |
| `atlas:` sprite is not registered | Ship/register the atlas, or use a plain sprite |
| The icon field is empty | Set an icon or accept the default |
| The tint is so dark it looks blank | Use a brighter `#RRGGBB` |

### `Item not found.` in the editor

The server could not resolve the item name. Use **Find Item Icon** or the exact internal name from `listitems`.

### Icon changes after a game update

Expected. `item:` keys resolve to the game's current icon. Re-check custom atlases and sprites after major updates.

### Tint does not apply

Use `#RRGGBB` or `#RRGGBBAA`. Invalid values become white. A disabled card multiplies alpha by 0.45.

---

## Item icons

### The picker shows no results

- Clear the search and browse.
- Try a shorter term.
- Confirm the item is loaded on the **server**, not only on the client.
- Modded items added at runtime may need a server restart before `ItemClass.ItemNames` includes them.

### The picker shows an item but the card is a hammer

The server knows the item; the client does not. Install the matching item mod on the client.

### Typing `item:name` fails

The exact internal name is required. Use the picker. `listitems` on the server console also lists names.

---

## Hook row missing

A `[GUI] ...` row does not appear.

1. Is Server Assist loaded? `/sa list` works?
2. Is the companion loaded? Check its plugin line and compile errors.
3. Did it register? Search the server log for:

   ```text
   [ServerAssist] registered GUI hook '<id>' from <plugin>.<method>
   ```

4. Does the player hold the hook permission?
5. Is the hook label empty? Registration rejects an empty id, label, plugin or method.
6. Is the companion using the old plugin name? It must `plugins.Find("ServerAssist")`.
7. Is the companion waiting for a ready hook that never fired? Register from `Init` with a timer as well.
8. Is the row on page 2? Ten rows per page.

### Hook opens but is empty

The handler returned null/empty. Check the companion log and the server log:

```text
[ServerAssist] GUI hook '<id>' did not return a view.
```

The handler must return a valid view for `a|open`.

### Hook opens but Back is missing

Back is injected only when the response has `chrome=hook` and `tab=hook`, which Server Assist appends itself. If you see no Back button:

- confirm you are in hook mode, not provider mode;
- confirm the window XML is current;
- check the client log for parse errors.

### Hook actions do nothing

- The handler must return a view for every action.
- The action string is what your row/button returned.
- `a|hook|back` is consumed by Server Assist and never reaches the handler.
- Check for encoded pipes in the action.

---

## Permission Manager row missing

The PermissionGui row is a hook:

```text
id:         permissions
label:      Permission Manager
permission: permissiongui.use
plugin:     PermissionGui
handler:    HandleServerAssistRequest
```

Check:

1. Server Assist is loaded.
2. `oxide/plugins/PermissionGui.cs` compiled.
3. The server log says:

   ```text
   [ServerAssist] registered GUI hook 'permissions' from PermissionGui.HandleServerAssistRequest
   ```

4. The player has `permissiongui.use` or is an admin.
5. You are not looking for an old standalone `permGui` window. Server Assist removes a legacy seeded button that targets it.
6. You did not install an old PermissionGui client mod. It is no longer needed.

`/permgui` and `/perm gui` should also open the hook. If they fall back to chat, the client mod or the hook is missing.

---

## Provider errors

### `PROVIDER` card missing from Home

1. Confirm the provider registered and the server log contains:

   ```text
   [ServerAssist] registered GUI provider '<id>' from <plugin>.<method>
   ```

2. Confirm the player can open the board. Provider cards are shown on Home to everyone who can open the board, but the provider still enforces its own permission when clicked.
3. The card may be on page 2. The board shows ten entries per page.
4. Older Server Assist builds do not list providers on Home. `GetProviderHomeSupport()` returns `false` there; open the provider through its own command or register a normal panel fallback.

### `GUI provider not found.`

The provider id is not registered. Check the companion's registration log and the exact id casing (ids are lowercased server-side).

### `GUI provider '<id>' plugin is not loaded.`

The provider was registered, then the companion unloaded. Reload the companion; it must re-register.

### `GUI provider '<id>' method ... was not found.`

The handler name is wrong or the method is not `public string Method(string, string)`.

### Provider opens but tabs are wrong

Return:

```text
tab1label=Browse
tab2label=My Listings
tab3label=Sell
```

Each label is max 32 characters.

### Provider opens but `/sa` does not leave it

`/sa` sends `a|home`, which clears `ActiveProvider`. If it does not, the client may be on an older build. Update the client.

### Provider permission is bypassed

Provider requests are routed before `serverassist.use` by design. The provider must check its own permission. This is not a Server Assist bug.

---

## Dynamic Backpack

The backpack is a server-only Server Assist panel. There is no backpack client DLL, XUi window, grid, bridge or net package.

### The Backpack card is missing

- `oxide/plugins/DynamicBackpack.cs` is missing or failed to compile.
- The player lacks `backpack.1`; the panel is registered with that node.
- Server Assist was not loaded when the panel registered. Check the server log:

  ```text
  [DynamicBackpack] loaded. Pages: backpack.1 - backpack.9. UI is provided by Server Assist.
  [DynamicBackpack] registered the backpack panel with Server Assist.
  ```

- The server is still running an old DynamicBackpack build. Replace it with the current 2.2.0 server-only plugin.

### The card opens but pages are locked

- The player only has the page permissions reported by `/backpack info`.
- Reopen the panel after a permission change.
- Check `backpack.N`, `backpack.*` and `backpack.admin`.

### `Server Assist is required to open the backpack.`

- `oxide/plugins/ServerAssist.cs` is missing or failed to compile.
- The player's client does not have the Server Assist mod, so `OpenPanel` cannot reach it.
- Rejoin after installing the same Server Assist build.

### Clicking an item card does nothing

Empty and locked cards are disabled by design. Only a card with an item sends `withdraw|<slot>`.

### Deposit Held says there is no room

All unlocked pages are full. The current page is filled first, then the other unlocked pages, merging partial stacks. Grant more pages or withdraw items.

### Items in locked pages disappeared

They are hidden, not deleted. Grant the player enough `backpack.N` pages to expose them again. `/backpack info` shows the unlocked page count.

### Items dropped at the player's feet

- The inventory was full when a card was withdrawn.
- `DropOverflowOnDowngrade` is enabled and the player's page permission was reduced.

### The old backpack window still opens

An old standalone backpack client folder is still installed. Remove it from every client. The current Server Assist mod has no dedicated backpack window group and no backpack window.

### Page contents look wrong

The slot mapping is:

```text
slot = (page - 1) * 9 + cardIndex
```

If the mapping appears shifted, verify the plugin is the current 2.2.0 build and the stored bag is an 81-slot `ItemStackGrid`.

More: [Dynamic Backpack](../../integration/dynamic-backpack.md).

---

## Player Profiles

PlayerProfiles is an optional server-only panel. It needs Server Assist 2.1 and ServerAssistMod 2.1 for an in-place switch; older combinations still work by reconnecting.

### The Profiles card is missing

- `oxide/plugins/PlayerProfiles.cs` is missing or failed to compile.
- The player lacks `playerprofiles.use`.
- Server Assist was not loaded when the panel registered. Check the server log:

  ```text
  [PlayerProfiles] loaded. Permissions: playerprofiles.slots.1 - 10.
  [PlayerProfiles] registered the profiles panel with Server Assist.
  ```

- The player cannot create profiles because they are at their slot limit. Check `playerprofiles.slots.N`.

### Switches always reconnect

The client mod, the server plugin or both are older than the companion-protocol build. Install ServerAssistMod 2.1 and the current ServerAssist plugin. Switches still complete; they just use the reconnect path.

### The switch aborts and keeps the old profile

- The live `PlayerDataFile` could not be captured; check the server log.
- The profile was saved with a different mod set and `RequireSameModSet` is true.
- The player is dead or attached to a vehicle and the matching guard is enabled.
- A partially applied client sends `recover`; the server restores the old profile and kicks so both sides resync.

### The profile looks stale after switching

The client closes cursor windows and rebuilds the toolbelt, backpack, equipment and quest tracker. If a window was open during the switch, reopen it. Report the exact window and game build if it stays stale.

### Data or recovery

Files live under `oxide/data/PlayerProfiles/<safe-player-id>/`. Keep the `.bak1` / `.bak2` files; an unreadable profile falls back to them. The vanilla save is never edited in place.

More: [PlayerProfiles](../../plugins/player/player-profiles.md).

---

## Media and Twitch integrations

### Both MediaIntegration and TwitchIntegration are installed

Remove one before reloading. Both poll the same relay and can register a Server Assist panel, which can duplicate links or apply the same reward twice. Use [Media Integration](../../plugins/utilities/media-integration.md) as the current platform-neutral path and keep [Twitch Integration](../../plugins/utilities/twitch-integration.md) only as a rollback copy.

### Relay reachable but no events arrive

- Confirm the plugin's `ServerKey` matches `servers[].apiKey` in the relay configuration.
- Confirm the relay is running and reachable over HTTPS.
- Check the relay log for EventSub or webhook errors.
- For custom webhooks, verify the HMAC signature, timestamp, nonce and the 64 KiB body limit.

---
