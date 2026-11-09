# QLN Pack Bootstrap

> **Version:** 0.1.0 · **Type:** client-side 7 Days to Die code mod + updater · **Source:** [QLNPackBootstrap](https://github.com/TheRealM18/Oxide-Server-Assist/tree/main/QLNPackBootstrap)

QLN Pack Bootstrap keeps a server's client mod pack in sync. It checks a signed
`manifest.json`, shows the player exactly what will be downloaded, verifies every
archive with SHA-256, stages the pack, and then a small updater applies the files
after the game closes and relaunches it.

It is **not** an Oxide plugin. Oxide runs inside the dedicated server process and
cannot write files on a player's PC. The player installs this bootstrap **once**;
after that, the in-game updater handles the pack.

!!! important
    The first bootstrap install is manual. There is no protocol that lets a
    server put the first file on a player's PC, and the server cannot force a
    player to accept an update.

---

## Requirements

| Requirement | Detail |
|---|---|
| Game version | 7 Days to Die **V3.x**; the default config targets **3.4** |
| Anti-cheat | **EAC off** for code/asset mods; `ModInfo.xml` sets `SkipWithAntiCheat` |
| Runtime | .NET Framework 4.8 on Windows (already required by the game) |
| Build tools | .NET SDK only when building from source |
| Write access | `%APPDATA%\7DaysToDie\Mods`; no administrator/UAC prompt needed |
| Hosting | A manifest URL reachable over HTTPS, or HTTP with `AllowInsecureHttp` |
| Server side | `QLNPackServer` or any static host/GitHub Releases serving the zips |

!!! warning
    The bootstrap and updater must live under the active `Mods` folder. Do not
    install them under `C:\Program Files...`, where elevation can become
    necessary.

---

## Installation (one time)

1. Download or build `000_QLNPackBootstrap.zip`.
2. Extract it under the active Mods folder:

   ```text
   %APPDATA%\7DaysToDie\Mods\000_QLNPackBootstrap\
       ModInfo.xml
       QLNPackBootstrap.dll
       QLNPackUpdater.exe
       packbootstrap.xml
   ```

3. Keep `ModInfo.xml` directly inside `000_QLNPackBootstrap`, not one level
   deeper.
4. Edit `packbootstrap.xml` and set at least `ManifestUrl`, the matching
   `AccessToken` when the server uses one, and `AllowInsecureHttp` for a plain
   HTTP agent.
5. Start 7 Days to Die **without EAC**.
6. At the main menu, review the **QLN Mod Pack** window and click
   **Download & Install**.

The install target is the folder that contains `000_QLNPackBootstrap` — the
active Mods directory the bootstrap was loaded from. If it is in
`%APPDATA%\7DaysToDie\Mods\000_QLNPackBootstrap`, downloaded mods go into
`%APPDATA%\7DaysToDie\Mods`. Set `ModsRoot` only to force a different folder.

### Build from source

```powershell
# from the QLNPackBootstrap folder
powershell -ExecutionPolicy Bypass -File .\build.ps1

# server agent
cd ..\QLNPackServer
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Output: `dist\000_QLNPackBootstrap\`. If the game is not in the default Steam
library:

```powershell
dotnet build .\QLNPackBootstrap.csproj -c Release -p:GameDir="D:\Steam\steamapps\common\7 Days To Die"
dotnet build .\updater\QLNPackUpdater.csproj -c Release
```

The mod project targets `net48`; the updater is a plain `net48` console
application with no game dependencies.

---

## Configuration

File: `000_QLNPackBootstrap\packbootstrap.xml`

| Key | Default | Notes |
|---|---|---|
| `ManifestUrl` | placeholder in shipped XML | Absolute URL of `manifest.json` |
| `SignatureUrl` | empty | Defaults to `ManifestUrl + ".sig"` for plain `.json` URLs; set explicitly for share pages |
| `AccessToken` | empty | Sent as `?token=` on manifest and zip requests |
| `PublicKeyXml` | empty | RSA public key XML; never paste a private key |
| `RequireSignature` | `false` | Require a valid manifest signature |
| `AllowedHosts` | empty | Comma-separated host allowlist; empty means any HTTPS host |
| `AllowInsecureHttp` | `false` | Allow plain HTTP, only for a local/HTTP agent |
| `CheckOnMainMenu` | `true` | Check automatically when the main menu opens |
| `ShowMainMenuButton` | `true` | Show **Check Mod Pack Updates** at top-right |
| `ShowCheckErrors` | `false` (code); `true` in shipped XML | Surface check failures in the UI |
| `KeepBackups` | `3` | Number of backup sets to retain |
| `MaxModBytes` | `2147483648` (2 GiB) | Per-archive compressed-size cap |
| `MaxTotalBytes` | `8589934592` (8 GiB) | Total manifest size cap |
| `MaxEntries` | `100000` | Zip entry-count cap |
| `MaxUncompressedBytes` | `8589934592` (8 GiB) | Zip expansion cap |
| `RemoveObsolete` | `false` | Remove pack-owned folders no longer in the manifest |
| `GameVersion` | `3.4` in shipped XML | Warning only |
| `GameExe` | empty | Empty means `<game folder>\7DaysToDie.exe` |
| `AutoRelaunch` | `true` | Relaunch after the updater applies the pack |
| `AutoRestart` | `true` | Restart a few seconds after staging |
| `MaxParallelDownloads` | `4` | Clamped to `1`–`10`; Drive often throttles above 3–4 |
| `ModsRoot` | empty | Empty means the folder containing the bootstrap folder |

`PublicKeyXml` is validated against accidental private-key fields (`<D>`,
`<P>`, `<Q>`) and is rejected with a clear error if one is present.

### Allowed host forms

- Empty: any HTTPS host.
- For Google Drive, include `drive.google.com` and
  `drive.usercontent.google.com`.
- For Dropbox, include `www.dropbox.com` and `dl.dropboxusercontent.com`.
- For OneDrive, include `1drv.ms` and `onedrive.live.com`.

The host check accepts the exact host or any subdomain ending in
`.<allowed>`.

---

## Manifest format

```json
{
  "schemaVersion": 1,
  "packVersion": "1.4.0",
  "gameVersion": "3.4",
  "generatedAt": "2026-11-07T00:00:00Z",
  "notes": "optional",
  "mods": [
    {
      "name": "QuantumLogisticsNetwork",
      "displayName": "Quantum Logistics Network",
      "version": "0.0.1",
      "url": "https://github.com/OWNER/REPO/releases/download/v1.4.0/QuantumLogisticsNetwork-0.0.1.zip",
      "sha256": "64 lowercase hex characters",
      "size": 12345678,
      "target": "client",
      "installFolder": "QuantumLogisticsNetwork",
      "required": true
    }
  ],
  "remove": []
}
```

| Field | Meaning |
|---|---|
| `packVersion` | Version shown in the UI; must be a safe name |
| `gameVersion` | Optional warning when it differs from config |
| `mods[].url` | Absolute zip URL |
| `mods[].sha256` | Required; download is refused on mismatch |
| `mods[].size` | Required; download size and progress check |
| `mods[].target` | `client`, `server`, or `both`; only client/both install |
| `mods[].installFolder` | Target folder under `Mods`; letters, digits, `_`, `-` |
| `remove` | Optional pack-owned folders to back up and remove |

The bootstrap validates `packVersion`, names, versions, install folders, SHA-256
values, sizes, URLs and host allowlists before it downloads anything. The
bootstrap folder name and `Data` are reserved and cannot be installed or removed
by a manifest.

!!! note
    Downloaded mods do **not** need a `ModInfo.xml`. The installer accepts a
    single wrapper folder or the archive root and uses the folder name when
    metadata is missing. Zips with `Config`, `XUi`, `Resources`, `Mods`,
    `Harmony`, `Assemblies`, `Localization`, `Patches`, `ItemIcons`,
    `UIAtlases`, `Unity` or `Data` at the root are treated as content, not as a
    wrapper.

---

## Operation

```text
Main menu opened
  -> PackBootstrapBehaviour.CheckRoutine()
     -> GET manifest with access token
     -> optional GET manifest signature
     -> verify RSA/SHA-256 signature over exact bytes
     -> validate manifest against packbootstrap.xml
     -> load Data\state.xml
     -> compute changes (new / update / unmanaged existing / remove)
     -> show update list

Download & Install
  -> create Data\staging
  -> download up to MaxParallelDownloads zips
  -> verify each SHA-256 and size
  -> extract safely into staging
  -> find the mod root
  -> write Data\pending.xml
  -> stage is complete
  -> AutoRestart ? 5-second countdown : wait for Restart & Update

Game exits
  -> QLNPackUpdater.exe waits for the game PID
  -> backs up replaced folders under Data\backup\<timestamp>\
  -> moves staged folders into Mods
  -> backs up and removes folders listed in `remove`
  -> writes Data\state.xml
  -> deletes pending.xml and the staging folder
  -> prunes old backups to KeepBackups
  -> relaunches the game when configured
```

Change detection compares the installed folder's recorded `Version` and
`Sha256` against the manifest. A folder that exists but has no matching state
entry is treated as `UnmanagedExisting` and is replaced only after being backed
up. If nothing differs, the UI reports no update.

### Signature

The manifest signature is RSA with SHA-256 and PKCS#1 v1.5 padding over the
exact manifest bytes. The client verifies it before parsing the manifest. The
`.sig` file is base64 text. Automatic `.sig` guessing applies only to plain
`.json` URLs, not Google Drive/Dropbox share pages; set `SignatureUrl`
explicitly for those.

### Security checks

- HTTPS for the manifest and downloads unless `AllowInsecureHttp` is on.
- Host allowlist for every mod URL.
- SHA-256 for every archive before extraction.
- Optional RSA signature over the exact manifest bytes.
- Zip-slip protection: absolute paths, drive letters and paths outside the
  destination are rejected.
- Entry-count and uncompressed-size limits.
- Only manifest-listed folders are touched; the Mods folder is never wiped.
- No installer scripts from the manifest are executed.

---

## Data and runtime outputs

Everything the bootstrap writes lives inside its own folder:

```text
000_QLNPackBootstrap\
  Data\
    logs\bootstrap.log
    logs\updater.log
    state.xml
    pending.xml
    updater-failed.txt
    staging\
    backup\
    temp\
    x\
```

| Path | Purpose |
|---|---|
| `Data\state.xml` | Last successful pack version, install time, and per-mod folder/version/SHA-256 |
| `Data\pending.xml` | Staged apply list, remove list, stage root and post-update state |
| `Data\staging\` | Extracted mod folders waiting for the updater |
| `Data\backup\<timestamp>\` | Previous version of each replaced or removed folder |
| `Data\temp\` | Download workspace |
| `Data\x\` | Short extraction path used to avoid the Windows 260-character limit |
| `Data\updater-failed.txt` | Marker that suppresses auto-restart after a failed apply |
| `Data\logs\bootstrap.log` | Manifest, download, verification and staging log |
| `Data\logs\updater.log` | Apply, backup, state and relaunch log |

To discard a staged update, close the game and delete `Data\pending.xml` and
`Data\staging`. To recover from a failed apply, read `updater.log`, fix the
cause, and use **Restart & Update**; the updater clears the failure marker when
it starts.

---

## Player-facing behaviour

| UI state | What the player sees |
|---|---|
| Update available | Scrollable mod list with name, version and size; **Download & Install** |
| Downloading | Per-mod progress bars, completed count, active parallelism |
| Staged | Countdown and **Restart & Update** / **Later** |
| No update | Short confirmation, auto-hides |
| Configuration needed | **Open Config** and **Close** |
| Error | Message, **Close**, and log location when `ShowCheckErrors` is on |
| Main-menu button | **Check Mod Pack Updates**, or **Finish Mod Pack Update** when staged |

If the player closes the window, the top-right main-menu button reopens it. If
the countdown ends while the player is in a world, the client waits until they
return to the main menu instead of quitting out from under the session.

The bootstrap does not update itself. Replace its folder while the game is
closed when a new bootstrap build is needed.

---

## Server Assist integration

The bootstrap has no Server Assist API calls and registers no panel. Its
relationship to Server Assist is distribution and art:

- `ServerAssistMod.dll` and its `Config` folder are ordinary pack entries. When
  Server Assist changes, update the pack entry's `sha256` and `size`.
- The `ServerAssist/deploy-to-pack.ps1` workflow stages exactly the client files:
  `ServerAssistMod.dll`, `ModInfo.xml`, `Config/XUi_InGame/xui.xml`,
  `Config/XUi_InGame/windows.xml` and `Config/XUi_Common/styles.xml`.
- The art pack supplies `sprite:sa_qln_pack` for Server Assist cards and shared
  buttons; it is separate from the bootstrap executable.
- Server Assist's own panel is not required for the bootstrap. The bootstrap
  runs at the main menu before a world is loaded.

See [Server Assist Mod](server-assist-mod.md) for the client files and
[Server Assist Art](../art/server-assist-art.md) for the icon set.

---

## Typical workflows

### First rollout

1. Build `dist\000_QLNPackBootstrap`.
2. Zip the `000_QLNPackBootstrap` folder and host it.
3. Point players at the zip and the `ManifestUrl`.
4. Have one player install it and run a full update on a backup save.
5. Publish the bootstrap link to everyone.

### Publish an update

1. Update the client-required mod folders on the server or in the pack folder.
2. Regenerate `manifest.json` with new `sha256` and `size` values.
3. Sign the manifest and upload `manifest.json.sig` if signatures are required.
4. Restart the bootstrap check or wait for the next main-menu check.

For the server-hosted flow, `QLNPackServer` builds the manifest and zips
automatically. See [QLN Pack Server](../programs/qln-pack-server.md).

### Roll back a bad pack

1. Close the game.
2. Open `Data\backup\<timestamp>\`.
3. Copy the previous mod folders back into the Mods directory.
4. Delete `Data\pending.xml` if it still exists.
5. Restart the game and correct the manifest.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Window does not appear | Wrong folder, nested `ModInfo.xml`, EAC on | Put `ModInfo.xml` directly inside `000_QLNPackBootstrap`; disable EAC |
| "Configuration needed" | Empty/invalid `ManifestUrl`, missing public key, bootstrap not under `Mods` | Edit config, set `ModsRoot`, or move the folder |
| Could not download manifest | Bad URL, agent down, firewall, token mismatch | Check URL, port, firewall and `AccessToken` |
| Server returns 403 | Client token differs from server token | Make `AccessToken` and `accessToken` identical |
| Signature mismatch | Manifest changed, wrong key, stale `.sig` | Re-sign and re-upload both files; check `PublicKeyXml` |
| Updater waits forever | Game process still running | Close the game normally; check Task Manager |
| Updater fails | File lock, antivirus, unwritable Mods folder | Read `updater.log`; fix the cause; click **Restart & Update** |
| Game starts with old mods | Stage not applied | Use **Restart & Update** or run `QLNPackUpdater.exe` |
| Old duplicate mod wins | Old copy in the game install folder | Remove `...\7 Days To Die\Mods\<old>`; the user folder loads first |
| Antivirus/SmartScreen warning | Unsigned updater replaces DLLs | Code-sign the updater/DLL or document hashes |

More: [Troubleshooting](../../reference/troubleshooting/index.md).
