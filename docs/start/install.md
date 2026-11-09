# Install Server Assist

!!! important
    Server Assist has two halves. The **server plugin** decides what a player may see. The **client mod** draws the window. A server cannot push an XUi window to a client, so every player who should use the board needs the client mod installed.

---

## Requirements

| Requirement | Detail |
|---|---|
| Game version | 7 Days to Die **V3.4.x**, built against the 3.4 managed assemblies |
| Server | Oxide / uMod installed and loading plugins |
| Anti-cheat | **EAC off** on the server and every client |
| Client mod | `ServerAssistMod.dll`, `ModInfo.xml` and `Config/XUi_InGame/` |
| Server plugin | `oxide/plugins/ServerAssist.cs` |
| Matching files | The same Server Assist build on the server and every client |
| Disk layout | Mod folder directly under `Mods/`, not nested inside `src/` or `obj/` |
| DynamicBackpack (optional) | Server-only Oxide plugin; no backpack client DLL or window |

!!! warning
    Do not run an old client folder next to a new one. Duplicate XUi groups and duplicated net packages produce confusing window-group and registration failures.

---

## Package contents

A release folder contains:

```text
ServerAssist/
  ServerAssistMod.dll
  ModInfo.xml
  README.txt
  Config/
    XUi_InGame/
      xui.xml
      windows.xml
  oxide/
    plugins/
      ServerAssist.cs
```

The ready-to-ship zip may rename the outer folder to `AA0_ServerAssistMod` so it sorts before other mods. Whatever name you choose, use the **same folder name** on the server and on every client.

| File | Goes to | Required on |
|---|---|---|
| `ServerAssistMod.dll` | `Mods/<folder>/` | Server and every client |
| `ModInfo.xml` | `Mods/<folder>/` | Server and every client |
| `Config/XUi_InGame/xui.xml` | `Mods/<folder>/Config/XUi_InGame/` | Server and every client |
| `Config/XUi_InGame/windows.xml` | `Mods/<folder>/Config/XUi_InGame/` | Server and every client |
| `oxide/plugins/ServerAssist.cs` | `oxide/plugins/` | Server only |

---

## Server install

1. Stop the server.
2. Create or replace the mod folder:

   ```text
   <server>/Mods/AA0_ServerAssistMod/
   ```

3. Copy `ServerAssistMod.dll`, `ModInfo.xml` and the entire `Config` folder into it.
4. Copy `ServerAssist.cs` into:

   ```text
   <server>/oxide/plugins/ServerAssist.cs
   ```

5. Start the server.
6. Confirm the server log contains the mod load and the plugin load:

   ```text
   [ServerAssist] loading companion mod from ...
   [ServerAssist] XUi window initialized
   [ServerAssist] registered ... net package(s)
   ```

7. Join with a client that also has the mod installed.
8. Press `F7` or `F6`, or type `/sa`.

!!! note
    Oxide compiles `ServerAssist.cs` at startup. Do not put the `.cs` file in the game's `Mods` folder; the mod loader does not compile it there.

---

## Companion plugins, client mods and programs

Server Assist is the base package. Everything else is optional and documented individually:

| Component | Installed as | Full reference |
|---|---|---|
| Companion plugins | `.cs` files in `oxide/plugins/` | [Plugins](../plugins/index.md) |
| Client mods | folders under `Mods/` | [Client mods](../plugins/client/index.md) |
| Standalone programs | services or tools outside the game/ Oxide | [Programs](../plugins/programs/index.md) |
| Art pack | `UIAtlases/UIAtlas` inside the Server Assist mod or a separate atlas mod | [Server Assist Art](../plugins/art/server-assist-art.md) |

Use the individual plugin page for its commands, permission nodes, config defaults and data paths.

---

## Client install

Do this for **every** player who should see the board, not only admins.

1. Stop the game completely.
2. Open the client's `Mods` folder.
3. Remove any older Server Assist folder or leftover client folder from the previous launcher generation. Do not keep both.
4. Copy the whole `ServerAssist` folder (or `AA0_ServerAssistMod`) into `Mods/`.
5. Verify the layout:

   ```text
   7 Days To Die/
     Mods/
       AA0_ServerAssistMod/
         ServerAssistMod.dll
         ModInfo.xml
         Config/
           XUi_InGame/
             xui.xml
             windows.xml
   ```

6. Start the game and join the server.
7. Press `F7` or `F6`, or type `/sa` in chat.

If the client log does not show `[ServerAssist] XUi window initialized`, the client did not parse `Config/XUi_InGame/xui.xml`. The usual cause is a nested folder such as `AA0_ServerAssistMod/ServerAssist/Config/...`.

### Optional DynamicBackpack

Install `oxide/plugins/DynamicBackpack.cs` on the server only. It registers a **Backpack** panel with Server Assist. Clients need no additional backpack DLL, XUi window or net package; the normal Server Assist client renders it. Remove any old standalone backpack client folder before starting.

---

## First run

On first load the plugin:

1. Registers `serverassist.use`, `serverassist.admin` and `serverassist.custom`.
2. Grants the three nodes to the configured administrators group.
3. Grants `serverassist.use` and `serverassist.custom` to the default players group when the matching config flags are enabled.
4. Writes `oxide/config/ServerAssist.json`.
5. Creates an empty `oxide/data/ServerAssist/data.json`. No example buttons are seeded.
6. Calls `OnServerAssistReady` so companion plugins can register.

Create shared buttons through **Shared** as an admin, or with `/sa add`.

---

## Verify the install

### Server

```text
/sa
/sa list
/sa reload
```

Expected log lines after a companion plugin registers:

```text
[ServerAssist] registered GUI hook 'adminshop' from AdminShop.HandleGuiRequest
[ServerAssist] registered GUI provider 'auctionhouse' from AuctionHouse.HandleAuctionGuiRequest
```

### Client

Press `F7` or `F6`. The window should show:

- title **SERVER ASSIST**
- left navigation: **Home**, **Shared**, **My Buttons**
- footer: `F7 or F6 toggles Server Assist`

Client log checks:

```text
[ServerAssist] XUi window initialized
[ServerAssist] window opened
```

### Permissions

```text
oxide.grant group admin serverassist.use
oxide.grant group admin serverassist.admin
oxide.grant group admin serverassist.custom
oxide.grant group players serverassist.use
oxide.grant group players serverassist.custom
```

Do not grant `serverassist.admin` to normal players.

---

## Recommended rollout order

Use this order on an existing server so players are never left with a half-updated client.

1. Announce a restart window.
2. Stop the server.
3. Back up `oxide/config/ServerAssist.json`, `oxide/data/ServerAssist/data.json` and the old mod folder.
4. Replace the server's `Mods` folder and `ServerAssist.cs`.
5. Update the companion plugins that call `plugins.Find("ServerAssist")`.
6. Publish the matching client folder to players.
7. Start the server.
8. Verify the server log.
9. Join with an updated client and test Home, Shared, My Buttons and one hook.
10. Ask players to remove the old client folder before installing the new one.

---

## Updating

1. Stop the server and clients.
2. Back up config and data.
3. Replace the mod folder on the server and on every client with the same build.
4. Replace `ServerAssist.cs`.
5. Start the server.
6. Run `/sa reload` to normalize config and data.
7. Test one shared button, one personal button, one hook and one provider.

Keep the server plugin and every client on the same build. A 2.0 client still loads, but it misses action-button icons, and `@pp|` features such as in-place PlayerProfiles switching fall back to their reconnect path. A 2.1 client with a 2.0 server may miss the fields it expects.

---

## Uninstall

1. Stop the server and clients.
2. Delete the `ServerAssist` mod folder from the server and every client.
3. Delete `oxide/plugins/ServerAssist.cs`.
4. Optionally keep the config and data for a future reinstall:

   ```text
   oxide/config/ServerAssist.json
   oxide/data/ServerAssist/data.json
   ```

5. Remove the `serverassist.*` permission grants if they are no longer needed.
6. Restart the server.

Companion plugins remain functional through chat commands when Server Assist is absent, unless their documentation says otherwise.

---

## Common install mistakes

| Mistake | Result | Fix |
|---|---|---|
| Client mod missing | `/sa` prints that the client mod is not installed | Install the folder on the client |
| Mod folder nested one level too deep | `window group serverAssist was not found` | Put `Config/XUi_InGame` directly under the mod folder |
| Old XUi cache after replacing files | Old layout or style errors | Fully restart the game |
| Different server and client builds | Empty window, missing fields, hello mismatch | Use the same build everywhere |
| EAC enabled | Mod does not load | Disable EAC |
| `.cs` file placed in `Mods/` | Plugin never loads | Move it to `oxide/plugins/` |
| Two client builds installed | Duplicate groups and net packages | Remove both, install one |
| Old DynamicBackpack client folder | Stale backpack window or duplicate client files | Remove the old backpack folder; the current plugin is server-only |
| `serverassist.use` missing | Window opens but reports no permission | Grant the node or use an admin account |

More: [Troubleshooting](../reference/troubleshooting/index.md).
