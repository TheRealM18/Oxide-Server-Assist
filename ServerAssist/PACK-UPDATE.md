# Server Assist pack staging

The QLN pack source folder is:

```text
C:\Users\robert\Desktop\Server Mods\Mods\AAY_ServerAssistMod
```

After building the client mod, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy-to-pack.ps1
```

The script copies only the client mod files:

```text
ServerAssistMod.dll
ModInfo.xml
Config/XUi_InGame/xui.xml
Config/XUi_InGame/windows.xml
Config/XUi_Common/styles.xml
```

It does **not** touch:

```text
C:\Users\robert\Desktop\Server Mods\Mods\AAZ_ServerAssistArt
```

That art folder is managed separately.

After the copy:

1. Hand the `AAY_ServerAssistMod` folder to the QLN pack agent.
2. The pack agent should zip the folder, upload it, and update the ServerAssist
   manifest entry (`sha256` and `size`).
3. The next bootstrap update will then install the current Server Assist build.

The server-side Oxide plugins are packaged separately and do not go in this
client mod staging folder.
