# FAQ

Short answers to the questions server owners ask first. Each answer links to the detailed page.

---

## Product

### What is Server Assist?

A permission-aware GUI launcher for 7 Days to Die. It owns one XUi window; companion plugins register panels, hooks or providers and Server Assist renders them. See [Server Assist](../../core/server-assist.md).

### Is it a replacement for a lot of separate menus?

Yes. PermissionGui, AdminShop, VoteSkipNight, AdminKits, AdminSuite, DailyRewards, DynamicBackpack, PluginHotReload, MediaIntegration, TwitchIntegration, BuggeeNet, PlayerProfiles and SentinelAC render through Server Assist instead of shipping their own client windows. AuctionHouse uses the provider model. One client mod renders all of them.

### Is Server Assist a client mod or a server plugin?

Both. `oxide/plugins/ServerAssist.cs` runs the data and permissions. `ServerAssistMod.dll` draws the window. The server cannot push XUi windows to a client, so every player needs the client folder.

### Does it work without Oxide?

No. The server half is an Oxide plugin.

### Does it work with EAC on?

No. EAC must be off for code mods and client DLLs.

---

## Install

### Where does the client folder go?

```text
Mods/AA0_ServerAssistMod/
  ServerAssistMod.dll
  ModInfo.xml
  Config/XUi_InGame/xui.xml
  Config/XUi_InGame/windows.xml
```

The folder name can differ, but it must be the same on the server and every client.

### Where does the plugin go?

```text
oxide/plugins/ServerAssist.cs
```

### Do players need to install it?

Yes, every player who should see the board. Admins are not special at the client level.

### Can I install it only for admins?

The server plugin loads for everyone, but only clients with the mod can open the window. You can gate the board with `serverassist.use`, but the client files are still required for the people who use it.

### Do I need to install the server mod folder if no one uses the UI?

The client assembly has a server-side half that bridges requests and tracks capable clients. Install it on the server as documented.

### How do I update?

Replace the mod folder on the server and every client with the same build, replace `ServerAssist.cs`, restart, then `/sa reload`. See [Install](../../start/install.md#updating).

### Can I keep the old config and data?

Yes. Back them up, then migrate them. See [Migration](../migration/index.md).

---

## Window and keys

### How do I open it?

`F7` or `F6`, `/sa`, `/serverassist`, or the client console `sa` / `serverassist`.

### Why does F7 do nothing?

The client mod is missing, XUi XML was not parsed, EAC is on, or another modal window is already open. See [Troubleshooting](../troubleshooting/index.md#window-will-not-open).

### Why does `/sa` say the client mod is not installed?

The server has not received the client's hello. Rejoin after a full game restart and confirm the client folder.

### What is the difference between F7 and `/sa`?

`F7` toggles the window and resumes an active provider. `/sa` returns to the home board and clears the provider/hook.

### Can I change the keybind?

Yes, by rebuilding the client with a different `ToggleKey` / `AlternateToggleKey` in `ServerAssistClient.cs`. The shipped build uses `F7` and `F6`.

### Does it work in single-player?

The assembly is built for the game's client and server halves. The documented target is a dedicated server with Oxide. Test single-player separately before shipping a pack.

---

---

## FAQ sections

| Page | Covers |
|---|---|
| [Buttons, icons and permissions](buttons-icons.md) | Shared/personal buttons, icon syntax, item search and permission nodes |
| [API and companions](api-companions.md) | Hooks, providers, action icons, `@pp\|`, Dynamic Backpack and Player Profiles |
| [Migration and security](migration-security.md) | Upgrades, security, limits, compatibility and support reporting |
