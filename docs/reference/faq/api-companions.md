# API and companions

Part of the [FAQ](index.md).

## API and companions

### How does a plugin add a row?

```csharp
serverAssist.Call(
    "RegisterPanel",
    "myplugin",
    "My Plugin",
    "Does a thing.",
    "myplugin.use",
    "MyPlugin",
    "HandleGuiRequest",
    "ui_game_symbol_hammer",
    50);
```

See [API](../../integration/api/index.md).

### What handler signature is required?

```csharp
public string HandleGuiRequest(string playerId, string payload)
```

### What payloads arrive?

```text
a|open
a|<action>
t|<encoded text>
```

### What does the handler return?

A newline-separated view: title, subtitle, status, optional input, rows and buttons. See [View protocol](../../integration/api/view-protocol.md#view-protocol).

### How many rows and buttons can a handler use?

Ten rows and twelve buttons. The docs often say ten action buttons because the older hook API reserved ten; the current window supports twelve.

### What is the difference between a hook and a provider?

A hook appears on Home with a `PLUGIN` badge and gets an injected Back button. A provider keeps the tabs, can rename them, and appears on current builds as a `[GUI] <title>` card with a `PROVIDER` badge; older builds expose it only through the provider command. See [API](../../integration/api/providers.md#provider-mode-vs-hook-mode).

### How does a companion survive a reload?

Define `OnServerAssistReady` and register again. Registration replaces by id.

### How does a companion clean up?

Call `UnregisterPanel`, `UnregisterGuiHook` or `UnregisterGuiProvider` from `Unload`.

### Does the companion need a client DLL?

No. The usual companion is server-only. If a feature needs client-side code, such as PlayerProfiles, that code is built into ServerAssistMod as a companion protocol; the companion still ships no separate DLL.

### Can a companion use the text input?

Yes, one input. Return `input=label|1|value` and handle `t|...`.

### Can a companion launch a window?

Yes, return a `launch=window|target|1` line. It must check permission first.

### Can a companion add icons to action buttons?

Yes. Add optional `icon` and `tint` fields after the tooltip in `btn=` lines. Server Assist 2.1 renders them; a 2.0 client treats them as tooltip text. See [Button fields](../../integration/api/view-protocol.md#button-fields).

### Can a companion talk to the client without opening a panel?

Yes, with the optional `@pp|` companion protocol. Send `@pp|<hook-id>|<body>`; Server Assist routes it to the hook and returns a `@pp|` response verbatim, without chrome. It reuses the existing packets and adds no NetPackage type. See [Optional companion protocols](../../integration/api/companion-protocols.md#optional-companion-protocols-pp).

### How do I read another panel's icon?

Call `GetPanelIcon(id)` or `GetPluginIcon(pluginName)`. Both return the live card icon or an empty string. See [GetPanelIcon and GetPluginIcon](../../integration/api/panels.md#getpanelicon-and-getpluginicon).

### Why is my hook row missing?

The companion did not register, the id/label/plugin/method is empty, the permission is missing, or the row is on page 2. See [Hook row missing](../troubleshooting/companions.md#hook-row-missing).

### Why does my provider say not found?

It was not registered, the id is wrong, or the plugin unloaded. See [Provider errors](../troubleshooting/companions.md#provider-errors).

---

## Dynamic Backpack

### Do I need a separate backpack client mod?

No. DynamicBackpack is a server-only Oxide plugin. Clients install only the normal Server Assist mod. Remove any old standalone backpack client folder.

### How many slots?

81 slots, presented as nine pages of nine item cards.

### How do pages unlock?

`backpack.1` unlocks page 1, `backpack.2` unlocks pages 1–2, through `backpack.9` for pages 1–9. `backpack.*` unlocks every page and `backpack.admin` uses `AdminRows`.

### How do I open it?

`/backpack`, or open Server Assist and click the **Backpack** card.

### Why is the Backpack card missing?

The player lacks `backpack.1`, the plugin is not loaded, or the panel did not register with Server Assist. Check `/backpack info` and the server log. See [Dynamic Backpack](../../integration/dynamic-backpack.md#troubleshooting).

### What happens when I click an item card?

It withdraws that whole stack into your inventory. If your inventory is full, the stack is dropped at your feet.

### What does Deposit Held do?

It stores the stack in your hand, filling the current page first, then the other unlocked pages. Partial stacks are merged; leftovers stay in your hand.

### What happens when I lower a player's pages?

Items in locked pages stay saved and hidden by default. Set `DropOverflowOnDowngrade` to true to drop them at the player's feet instead.

### Does the backpack drop on death?

The plugin does not add a death-drop handler; its stored bag is separate from the vanilla bag and toolbelt. Another mod that clears inventories would need its own handling.

### Is there any backpack client component?

No. DynamicBackpack is a server-only plugin. The old backpack client DLL, custom XUi window and net packages were removed in 2.0. See [Migration](../migration/rollout-rollback.md#dynamic-backpack-migration).

---

## Player Profiles

### What is PlayerProfiles?

An optional server-only plugin that saves multiple complete characters per account — inventory, backpack, equipment, level, perks, quests, buffs, position and more — and switches between them through Server Assist.

### Do I need a separate client mod?

No. The client half of the in-place switch is built into ServerAssistMod 2.1 (or the companion-protocol 2.0.1 build). There is no PlayerProfiles DLL, XUi window or extra NetPackage type. Older clients still join and use the reconnect fallback.

### How does switching work?

With the updated client mod the character is captured, validated and applied in place without disconnecting. New profiles, old clients or a disabled seamless mode use a safe reconnect instead. On any failure the old profile is kept.

### What permissions does it use?

`playerprofiles.use` opens the panel; `playerprofiles.slots.1` … `playerprofiles.slots.N` controls how many profiles a player may create; `playerprofiles.admin` grants the maximum slots and reload. Permissions and groups remain account-wide.

### Where is the data stored?

`oxide/data/PlayerProfiles/<safe-player-id>/state.json` plus one `<profile-id>.ttp` file per profile. The `.ttp` format is the game's own PlayerDataFile container.

### Can another plugin store per-profile data?

Yes. Key it by `GetProfileScopedId(playerId)` instead of the raw player id, or hook `OnPlayerProfileSwitch` and handle the transition. Other plugins that key by raw Steam ID share data across profiles.

---
