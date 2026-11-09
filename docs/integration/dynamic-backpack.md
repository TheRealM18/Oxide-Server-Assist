# Dynamic Backpack

The Dynamic Backpack is an optional **server-only** Oxide plugin. It stores 81 slots and presents them as **nine pages of nine item cards inside Server Assist**. Server Assist owns the window, cards, page buttons and item icons; the plugin owns the stored items, permission checks and item movement.

There is no Dynamic Backpack client DLL, no custom XUi window, no grid controller and no backpack net package. If the plugin is not installed, the Server Assist board works exactly as before and no Backpack card appears.

---

## Identity

| Item | Value |
|---|---|
| Plugin | `DynamicBackpack` (`Oxide.Plugins.DynamicBackpack`) |
| Version | `2.2.0` |
| Panel id | `backpack` |
| Panel label | `Backpack` |
| Panel permission | `backpack.1` |
| Handler | `HandleBackpackGui` |
| Storage | 81 slots |
| Presentation | 9 pages × 9 item cards |
| Data | `oxide/data/DynamicBackpack/<player-id>.json` |
| Entry | Server Assist **Backpack** card or `/backpack` |

!!! important
    The plugin is server-only. Clients install only the normal Server Assist mod. There is no second backpack client mod to deploy.

---

## Requirements

| Requirement | Detail |
|---|---|
| Game | 7 Days to Die V3.4.x |
| Server | Oxide / uMod |
| Server Assist | 2.1.0 or newer on the server |
| Client | The same Server Assist mod on every player who should see the panel |
| EAC | Off |
| Old backpack client | Removed |

Server Assist must be loaded before the Backpack card can appear. If Server Assist is absent, `/backpack` explains that Server Assist is required and the chat commands still work.

---

## Install

### Server

1. Stop the server.
2. Install the Server Assist plugin and mod folder as described in [Install](../start/install.md).
3. Copy `oxide/plugins/DynamicBackpack.cs` into `oxide/plugins/`.
4. Remove any old standalone Dynamic Backpack client folder from the server and every client.
5. Start the server.
6. Check the server log:

   ```text
   [DynamicBackpack] loaded. Pages: backpack.1 - backpack.9. UI is provided by Server Assist.
   [DynamicBackpack] registered the backpack panel with Server Assist.
   ```

### Client

1. Stop the game.
2. Install the same Server Assist mod folder used by the server.
3. Do **not** install a separate Dynamic Backpack mod. There is none.
4. Start the game and join.
5. Press `F7` or `F6`, open the **Backpack** card, or type `/backpack`.

The client log does not need a backpack-specific line. The panel is rendered by the Server Assist client that is already loaded.

---

## Using the panel

The panel shows one page at a time:

| UI element | Behaviour |
|---|---|
| 9 item cards | The nine slots for the current page |
| Page 1 … Page 9 | Switch pages; locked pages are disabled |
| Deposit Held | Store the item in your hand |
| Refresh | Rebuild the current page view |
| Back | Return to the Server Assist home board |

That is **9 page buttons + Deposit Held + Refresh + Back = 12 action buttons**, matching the Server Assist toolbar.

### Item cards

Each card represents one slot on the current page:

| Card state | Appearance | Action |
|---|---|---|
| Item stack | Real item icon, localized name, `x<count>`, quality when present | Withdraw the whole stack |
| Empty slot | Backpack symbol, `Empty slot`, `EMPTY` badge | Disabled |
| Locked slot | Backpack symbol, `Locked`, `LOCKED` badge | Disabled |

Clicking an item card withdraws the **whole stack** into the player's inventory. If the inventory is full, the stack is dropped at the player's feet. The server validates the slot, the page permission and the player entity before any item moves.

### Deposit Held

**Deposit Held** stores the stack in the player's hand:

1. It fills matching partial stacks on the current page first.
2. It then uses empty slots on the current page.
3. It then continues through the other unlocked pages.
4. Partial stacks are merged up to the item's maximum stack size.
5. Anything that does not fit stays in the player's hand.

The status line reports how many items were stored and how many remain in hand.

### Page and slot mapping

```text
slot = (page - 1) * 9 + cardIndex
```

| Page | Cards | Slot indices |
|---|---|---|
| 1 | 9 | 0–8 |
| 2 | 9 | 9–17 |
| 3 | 9 | 18–26 |
| 4 | 9 | 27–35 |
| 5 | 9 | 36–44 |
| 6 | 9 | 45–53 |
| 7 | 9 | 54–62 |
| 8 | 9 | 63–71 |
| 9 | 9 | 72–80 |

The page index is stored per player for the current session. `page|N` is clamped to the unlocked range.

### Icons and badges

The server emits standard Server Assist row fields:

```text
item:<itemName>     real game item icon
x<count>            badge and subtitle amount
Quality <n>         quality suffix when the item has quality
```

Empty and locked cards use `sprite:ui_game_symbol_backpack`.

---

## Permissions

Permissions unlock **pages**, not rows:

| Node | Unlocks |
|---|---|
| `backpack.1` | Page 1 — 9 slots |
| `backpack.2` | Pages 1–2 — 18 slots |
| `backpack.3` | Pages 1–3 — 27 slots |
| `backpack.4` | Pages 1–4 — 36 slots |
| `backpack.5` | Pages 1–5 — 45 slots |
| `backpack.6` | Pages 1–6 — 54 slots |
| `backpack.7` | Pages 1–7 — 63 slots |
| `backpack.8` | Pages 1–8 — 72 slots |
| `backpack.9` | Pages 1–9 — 81 slots |
| `backpack.*` | Every configured page |
| `backpack.admin` | Configured `AdminRows`, default 9 pages |

The highest granted level wins. A player with `backpack.4` is not reduced by also having `backpack.2`.

The panel itself is registered with `backpack.1`, so a player needs at least the first page to see the **Backpack** card on the Server Assist home board.

Examples:

```text
oxide.grant group players backpack.1
oxide.grant group vip backpack.4
oxide.grant user 76561198000000000 backpack.6
oxide.revoke user 76561198000000000 backpack.6
```

Server administrators (`IPlayer.IsAdmin`) receive the configured `AdminRows` even without an explicit `backpack.admin` grant.

Permission changes are picked up for online players immediately, with a 30-second fallback check.

---

## Configuration

File: `oxide/config/DynamicBackpack.json`

| Key | Default | Notes |
|---|---|---|
| `ChatPrefix` | `[Backpack]` | Chat reply prefix |
| `PermissionPrefix` | `backpack` | Base node; changing it changes every page node |
| `MaxRows` | `9` | Maximum pages; hard cap 9 |
| `DefaultRows` | `0` | Pages for players with no backpack node |
| `GrantFirstRowToPlayers` | `true` | Grant `<prefix>.1` to the default players group |
| `AdminPermission` | `backpack.admin` | Admin override node |
| `AdminRows` | `9` | Pages for the admin node |
| `GrantAdminToAdministrators` | `true` | Grant admin pages to the administrators group |
| `SaveIntervalSeconds` | `15` | Dirty-backpack write interval |
| `DropOverflowOnDowngrade` | `false` | Drop items above the page limit instead of keeping them hidden |

The config keys keep the historical `Row`/`Rows` names, but their values are **page counts**. `MaxRows`, `DefaultRows` and `AdminRows` are clamped to 0–9. Changing `PermissionPrefix` changes every page node.

Reload after editing:

```text
/backpack reload
```

`/backpack reload` is admin-only.

---

## Commands

| Command | Behaviour |
|---|---|
| `/backpack` | Open the panel through Server Assist `OpenPanel` |
| `/backpack open` | Same as `/backpack` |
| `/backpack info` | Show unlocked pages, slots and stored stack count |
| `/backpack pages` | Alias for `info` |
| `/backpack rows` | Alias for `info` |
| `/backpack save` | Force-save your backpack |
| `/backpack reload` | Reload config and re-register permissions (admin) |
| `/backpack help` | Show the command list |

The plugin also registers the `dbp` command alias.

`/backpack` requires Server Assist and a capable client. If the panel cannot open, the plugin replies with a chat message instead of failing silently.

---

## Data and compatibility

Backpacks are stored per player:

```text
oxide/data/DynamicBackpack/<player-id>.json
```

Each file contains:

```json
{
  "Data": "<base64 ItemStackGrid>",
  "Rows": 9,
  "Name": "PlayerName",
  "UpdatedUtc": "2026-01-01T00:00:00.0000000Z"
}
```

The `Rows` field stores the last known page count for compatibility with the old format. The 81-slot `ItemStackGrid` payload is unchanged. Existing backpack data loads directly; the slot mapping is the same:

```text
slot = (page - 1) * 9 + cardIndex
```

Writes happen every `SaveIntervalSeconds`, on server save, on server shutdown, on player disconnect and on plugin unload.

### Permission changes

- Granting more pages exposes the stored items again.
- Reducing pages hides the extra items in locked pages.
- With `DropOverflowOnDowngrade = false` (default), locked-page items stay saved.
- With `DropOverflowOnDowngrade = true`, items above the new page limit are moved to a dropped loot container at the player's feet on the next check.
- The server rejects withdraw requests for locked slots and only deposits into unlocked pages.

---

## Server Assist integration

DynamicBackpack registers a panel with `RegisterPanel`:

```csharp
serverAssist.Call(
    "RegisterPanel",
    "backpack",
    "Backpack",
    "Permission-based virtual storage pages.",
    "backpack.1",
    "DynamicBackpack",
    nameof(HandleBackpackGui),
    "sprite:sa_dynamic_backpack",
    30);
```

The handler signature is:

```csharp
public string HandleBackpackGui(string playerId, string payload)
```

Payloads:

| Payload | Meaning |
|---|---|
| `a\|open` | Initial panel view |
| `a\|page\|N` | Switch to page N |
| `a\|withdraw\|<slot>` | Withdraw the whole stack at `slot` |
| `a\|deposit` | Store the held stack |
| `a\|refresh` | Rebuild the view |
| `a\|hook\|back` | Handled by Server Assist; returns to the home board |

The server encodes every dynamic row and button field with `%`, `|` and newline escaping. Page and action routing is the generic Server Assist [Page navigation](api/view-protocol.md#page-navigation) pattern; no backpack-specific client API exists.

On unload the plugin calls:

```csharp
serverAssist.Call("UnregisterPanel", "backpack");
```

The panel is re-registered from `Init`, `OnServerInitialized` and `OnServerAssistReady`, so reloading either plugin is safe.

---

## Verification

Server log:

```text
[DynamicBackpack] loaded. Pages: backpack.1 - backpack.9. UI is provided by Server Assist.
[DynamicBackpack] registered the backpack panel with Server Assist.
```

In game:

```text
/backpack info
/backpack
```

Expected:

- `/backpack info` reports the unlocked page count and stored stacks.
- The Server Assist home board shows a **Backpack** card with a `PLUGIN` badge.
- The panel shows nine cards and twelve action buttons.
- Page 1 works with `backpack.1`.
- A second page becomes available with `backpack.2`.

No assembly inspection is required for this integration. `ServerAssistMod.dll` has no backpack types, no backpack window and no backpack net packages in 2.0.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Backpack card missing | No `backpack.1`, plugin not loaded, or panel not registered | Check the log and grant `backpack.1` |
| Card opens but pages are locked | Permission changed while the panel was open | Reopen the panel; `/backpack info` |
| `Server Assist is required...` | Server Assist plugin or client mod missing | Install Server Assist |
| Clicking a card does nothing | The card is empty or locked | Only item cards are actionable |
| Deposit Held says there is no room | All unlocked pages are full | Withdraw items or grant more pages |
| Items in locked pages disappeared | Pages were reduced | They are hidden, not deleted; grant pages |
| Items dropped at feet | Inventory was full on withdraw, or overflow was enabled | Make room; check `DropOverflowOnDowngrade` |
| Old backpack window still opens | Old client backpack folder is still installed | Remove the old client folder |
| Page contents look wrong | Page/slot mapping | `slot = (page - 1) * 9 + card` |

More: [Troubleshooting](../reference/troubleshooting/companions.md#dynamic-backpack).

---

## Removed legacy build

The previous standalone backpack window, its client DLL and its build identifiers are removed in 2.0. If you are upgrading an old install, see [Migration](../reference/migration/rollout-rollback.md#dynamic-backpack-migration) for the removed identifiers and the cleanup steps.

There is no `src/Backpack` folder in Server Assist 2.0 and no backpack code in `ServerAssistMod.dll`.

---

## Related pages

- [Install](../start/install.md)
- [API](api/index.md)
- [Companion Integration](companions/index.md)
- [Migration](../reference/migration/index.md)
- [Troubleshooting](../reference/troubleshooting/index.md)
