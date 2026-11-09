# QLN Pack Bootstrap

> **Version:** 0.1.0 · **Type:** client-side 7 Days to Die code mod + updater · **Source:** [QLNPackBootstrap](https://github.com/TheRealM18/Oxide-Server-Assist/tree/main/QLNPackBootstrap)

QLN Pack Bootstrap keeps a server's client mod pack in sync. It checks a signed
`manifest.json`, shows the player exactly what will be downloaded, verifies every
archive with SHA-256, stages the pack, and then a small updater applies the files
after the game closes and relaunches it.

It is **not** an Oxide plugin. Oxide runs inside the dedicated server process and
cannot write files on a player's PC. The player installs this bootstrap **once**;
after that, the in-game updater handles the pack.

One bootstrap can host several **pack profiles** — for example a full mod pack, a
lightweight pack, and a **Vanilla** profile. Profiles come from local `<Packs>`
entries, an optional remote `PackIndexUrl` index, or the legacy single-pack
configuration. The player switches between them with the pack tabs in the window.

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
   HTTP agent. To offer several packs, use the `<Packs>`/`PackIndexUrl`
   configuration described in [Pack profiles](#pack-profiles) instead of the
   top-level `ManifestUrl`.
5. Start 7 Days to Die **without EAC**.
6. At the main menu, pick a pack tab, review the **QLN Mod Packs** window and
   click **Download & Install**. The **Vanilla** tab removes pack folders
   instead.

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
| `ManifestUrl` | placeholder in shipped XML | Legacy single-pack URL; becomes the `default` profile when no `<Packs>` are defined |
| `SignatureUrl` | empty | Defaults to the manifest URL + `.sig` for plain `.json` URLs; set explicitly for share pages |
| `AccessToken` | empty | Sent as `?token=` on manifest and zip requests; profile tokens cover only that profile's manifest/signature |
| `PackIndexUrl` | empty | Optional remote JSON pack index merged into the local profile list |
| `DefaultPackId` | first non-vanilla profile with a manifest URL, else `vanilla` | Profile selected on a fresh install |
| `Packs` | empty | Local `<Pack>` profiles; see [Pack profiles](#pack-profiles) |
| `PublicKeyXml` | empty | RSA public key XML; never paste a private key |
| `RequireSignature` | `false` | Require a valid manifest signature |
| `AllowedHosts` | empty | Comma-separated host allowlist; empty means any HTTPS host |
| `AllowInsecureHttp` | `false` | Allow plain HTTP, only for a local/HTTP agent |
| `CheckOnMainMenu` | `true` | Check automatically when the main menu opens |
| `ShowMainMenuButton` | `true` | Show the **Mod Packs** button at top-right |
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

## Pack profiles

A profile is one selectable pack. The bootstrap reads local profiles from the
`<Packs>` block and can merge more from a remote index. The implementation is in
[PackConfig.cs](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/QLNPackBootstrap/src/PackConfig.cs),
[PackProfile.cs](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/QLNPackBootstrap/src/PackProfile.cs)
and
[PackBootstrapBehaviour.cs](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/QLNPackBootstrap/src/PackBootstrapBehaviour.cs).

```xml
<DefaultPackId>qln</DefaultPackId>
<PackIndexUrl>https://example.com/packs.json</PackIndexUrl>

<Packs>
  <Pack>
    <Id>qln</Id>
    <Name>Quantum Logistics Network</Name>
    <ManifestUrl>https://example.com/qln/manifest.json</ManifestUrl>
    <SignatureUrl>https://example.com/qln/manifest.json.sig</SignatureUrl>
    <AccessToken></AccessToken>
    <GameVersion>3.4</GameVersion>
    <Notes>Full client pack</Notes>
  </Pack>
  <Pack>
    <Id>lite</Id>
    <Name>Lite Pack</Name>
    <ManifestUrl>https://example.com/lite/manifest.json</ManifestUrl>
    <Vanilla>false</Vanilla>
  </Pack>
</Packs>
```

| Field | Meaning |
|---|---|
| `Id` | Stable profile identifier; required and unique. Remote index entries also need letters/digits/`_`/`-` only |
| `Name` | Label shown on the pack tab; falls back to `Id` |
| `ManifestUrl` | Absolute manifest URL for this profile |
| `SignatureUrl` | Optional; falls back to the top-level config for the default profile, or `ManifestUrl + ".sig"` for plain `.json` URLs |
| `AccessToken` | Optional; used for this profile's manifest and signature requests, falling back to the top-level `AccessToken` |
| `GameVersion` | Optional; falls back to the top-level `GameVersion` for UI warnings |
| `Notes` | Free text stored with the profile; not currently rendered in the window |
| `Vanilla` | `true` marks the profile as Vanilla; its `ManifestUrl` is cleared |

Local profiles are read in order, and duplicate ids inside `<Packs>` are ignored
after the first. If `<Packs>` is empty and the top-level `ManifestUrl` is set,
the bootstrap creates a legacy profile with id `default` and name
`Default Pack`, so existing single-pack configurations keep working.

### Remote pack index

`PackIndexUrl` points at a JSON document shaped like this:

```json
{
  "packs": [
    {
      "id": "community",
      "name": "Community Pack",
      "manifestUrl": "https://example.com/community/manifest.json",
      "signatureUrl": "https://example.com/community/manifest.json.sig",
      "accessToken": "",
      "gameVersion": "3.4",
      "notes": "Optional description",
      "vanilla": false
    }
  ]
}
```

The index is fetched once per session when the first check runs. A remote entry
is merged into the list only when it passes these checks; invalid entries are
logged and skipped:

- The id is non-empty, at most 64 characters, and uses only letters, digits, `_`
  or `-`.
- No profile with that id already exists locally. **Local configuration always
  wins over a remote entry with the same id.**
- A non-vanilla entry has an absolute `manifestUrl` using HTTPS, or HTTP when
  `AllowInsecureHttp` is enabled.
- The response is valid JSON and no larger than 2 MiB.

A missing or unparsable index does not break the window: the error is written to
`bootstrap.log` and the local profiles remain usable. Remote entries are marked
internally as remote; they are not written back to `packbootstrap.xml`.

!!! note
    A profile `AccessToken` covers its manifest and signature requests only.
    Zip downloads use the top-level `AccessToken`, so keep the top-level value
    set when the server agent requires a token for mod archives.

### Default pack and persisted selection

Selection follows this order:

1. `ActivePackId` in `Data\state.xml`, when it matches a known profile.
2. `DefaultPackId` from `packbootstrap.xml`.
3. The first non-vanilla profile that has a `ManifestUrl`.
4. The **Vanilla** profile.

If `DefaultPackId` is empty or unknown, the bootstrap picks the first
non-vanilla profile with a manifest URL, otherwise `vanilla`. A profile chosen in
the UI is recorded as `ActivePackId` when its pending update is applied, so the
same pack is selected on the next launch. `DefaultPackId` is only the fallback
for a fresh install or a state file that has no usable `ActivePackId`.

### Vanilla profile

Every configuration gets a **Vanilla** profile. If none is defined, one is
inserted automatically at the front of the list. Its id is `vanilla`, it has no
manifest, and selecting it builds an empty manifest with the pack version
`vanilla`.

Selecting Vanilla plans a removal for every folder recorded in
`Data\state.xml` that the empty manifest does not contain. The updater backs up
and deletes those folders, then writes the vanilla state. It never deletes
folders the bootstrap does not manage, so manually installed mods are left
alone.

!!! warning
    Switching away from a pack removes folders that pack installed, even when
    `RemoveObsolete` is `false`. The removed folders are backed up under
    `Data\backup\<timestamp>\` first.

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
  -> load Data\state.xml and choose the active pack
     (ActivePackId -> DefaultPackId -> first non-vanilla pack -> vanilla)
  -> PackIndexUrl configured and not loaded yet?
     -> GET index, validate entries, merge into the local profile list
        (local ids win; invalid remote entries are skipped)
  -> PackBootstrapBehaviour.CheckRoutine()
     -> Vanilla profile? build an empty manifest and plan removals
     -> GET manifest with the profile access token
     -> optional GET manifest signature
     -> verify RSA/SHA-256 signature over exact bytes
     -> validate manifest against packbootstrap.xml
     -> compute changes (new / update / unmanaged existing / remove)
        (a pack switch also marks the previous pack's managed folders)
     -> show update list

Download & Install
  -> create Data\staging
  -> download up to MaxParallelDownloads zips
  -> verify each SHA-256 and size
  -> extract safely into staging
  -> find the mod root
  -> write Data\pending.xml with the active pack id
  -> stage is complete
  -> AutoRestart ? 5-second countdown : wait for Restart & Update

Game exits
  -> QLNPackUpdater.exe waits for the game PID
  -> backs up replaced folders under Data\backup\<timestamp>\
  -> moves staged folders into Mods
  -> backs up and removes folders listed in `remove`
  -> writes Data\state.xml (including ActivePackId)
  -> deletes pending.xml and the staging folder
  -> prunes old backups to KeepBackups
  -> relaunches the game when configured
```

The index is fetched once per session, before the first check. **Refresh Packs**
in the window (shown only when `PackIndexUrl` is set) forces a new fetch and
re-runs the check. If the index cannot be loaded, the failure is logged and the
local profiles are used.

Change detection compares the installed folder's recorded `Version` and
`Sha256` against the manifest. A folder that exists but has no matching state
entry is treated as `UnmanagedExisting` and is replaced only after being backed
up. If nothing differs, the UI reports no update.

### Pack selector and switching

The window title is **QLN Mod Packs** and a scrollable tab row lists every
profile, with the active tab highlighted. Tabs are disabled while a check is
running. Selecting a different tab immediately starts a forced check for that
profile.

When the selected profile differs from `ActivePackId` in the state file, the
bootstrap treats the check as a pack switch and adds every folder recorded for
the previous pack that the new manifest does not list to the removal plan. Those
folders are backed up and deleted by the updater, so stale DLLs from the old pack
do not stay loaded. Switching to Vanilla uses the same rule against an empty
manifest.

### Signature

The manifest signature is RSA with SHA-256 and PKCS#1 v1.5 padding over the
exact manifest bytes. The client verifies it before parsing the manifest. The
`.sig` file is base64 text. Automatic `.sig` guessing applies only to plain
`.json` URLs, not Google Drive/Dropbox share pages; set `SignatureUrl`
explicitly for those.

### Security checks

- HTTPS for the manifest and downloads unless `AllowInsecureHttp` is on.
- Host allowlist for every mod URL.
- Remote pack index entries need a valid id and an absolute HTTPS `manifestUrl`,
  or HTTP when `AllowInsecureHttp` is on; invalid remote entries are ignored.
- Local profiles always win over a remote entry with the same id.
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
| `Data\state.xml` | Last successful pack version, **active pack id**, install time, and per-mod folder/version/SHA-256 |
| `Data\pending.xml` | Staged apply list, remove list, stage root, **active pack id** and post-update state |
| `Data\staging\` | Extracted mod folders waiting for the updater |
| `Data\backup\<timestamp>\` | Previous version of each replaced or removed folder |
| `Data\temp\` | Download workspace |
| `Data\x\` | Short extraction path used to avoid the Windows 260-character limit |
| `Data\updater-failed.txt` | Marker that suppresses auto-restart after a failed apply |
| `Data\logs\bootstrap.log` | Manifest, download, verification and staging log |
| `Data\logs\updater.log` | Apply, backup, state and relaunch log |

To discard a staged update, close the game and delete `Data\pending.xml` and
`Data\staging`. The active pack does not change until the pending update is
applied, because the updater copies `ActivePackId` from `pending.xml` into
`state.xml`. To recover from a failed apply, read `updater.log`, fix the
cause, and use **Restart & Update**; the updater clears the failure marker when
it starts.

---

## Player-facing behaviour

| UI state | What the player sees |
|---|---|
| Pack tabs | Scrollable profile row with the active pack highlighted; **Refresh Packs** when `PackIndexUrl` is set |
| Update available | Scrollable mod list with name, version and size; **Download & Install** |
| Vanilla selected | Explanation that managed pack folders will be removed; **Switch to Vanilla** |
| Pack switch | Warning that folders from the previous pack will be removed |
| Downloading | Per-mod progress bars, completed count, active parallelism |
| Staged | Countdown and **Restart & Update** / **Later** |
| No update | Short confirmation, auto-hides |
| Configuration needed | **Open Config** and **Close** |
| Error | Message, **Close**, and log location when `ShowCheckErrors` is on |
| Main-menu button | **Mod Packs**, or **Finish Mod Pack Update** when staged |

The window header shows the active pack label, its version, the game version and,
for non-vanilla profiles, the manifest host. If the player closes the window, the
top-right main-menu button reopens it. If the countdown ends while the player is
in a world, the client waits until they return to the main menu instead of
quitting out from under the session.

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
3. Point players at the zip and configure the packs they can choose from.
4. Have one player install it and run a full update on a backup save.
5. Publish the bootstrap link to everyone.

### Offer multiple packs

1. Add one `<Pack>` per profile in `packbootstrap.xml`, or publish a JSON index
   and set `PackIndexUrl`.
2. Set `DefaultPackId` to the profile a fresh install should use.
3. Optionally leave out a Vanilla entry; the bootstrap adds one automatically.
4. Publish the bootstrap and config together. Players can switch with the pack
   tabs; the next update removes the previous pack's managed folders.

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
| "Configuration needed" | No usable pack profile, empty/invalid `ManifestUrl`, missing public key, bootstrap not under `Mods` | Add a `<Pack>` or `ManifestUrl`, edit config, set `ModsRoot`, or move the folder |
| "No mod pack profiles are configured" | `<Packs>` has no valid entry and the top-level `ManifestUrl` is empty | Add a profile or a legacy `ManifestUrl` |
| Pack tab missing | Remote entry rejected (bad id, insecure URL without `AllowInsecureHttp`, or duplicate local id), or index unreachable | Check `bootstrap.log`, fix the index entry, or define the pack locally |
| Refresh Packs does nothing | `PackIndexUrl` is empty | Set `PackIndexUrl`, or edit the local `<Packs>` block |
| Could not download manifest | Bad URL, agent down, firewall, token mismatch | Check URL, port, firewall and `AccessToken` |
| Server returns 403 | Client token differs from server token | Make `AccessToken` and `accessToken` identical |
| Signature mismatch | Manifest changed, wrong key, stale `.sig` | Re-sign and re-upload both files; check `PublicKeyXml` |
| Updater waits forever | Game process still running | Close the game normally; check Task Manager |
| Updater fails | File lock, antivirus, unwritable Mods folder | Read `updater.log`; fix the cause; click **Restart & Update** |
| Game starts with old mods | Stage not applied | Use **Restart & Update** or run `QLNPackUpdater.exe` |
| Previous pack's mods removed after a switch | Expected: folders recorded for the old pack are not in the new pack | Restore from `Data\backup` if unintended; list shared folders in both packs |
| Vanilla leaves a mod installed | The folder is not recorded as bootstrap-managed in `state.xml` | Remove it manually; only pack-managed folders are deleted |
| Wrong pack selected at launch | `ActivePackId` in `state.xml` overrides `DefaultPackId` | Re-select the pack and apply an update, or edit `state.xml` with the game closed |
| Old duplicate mod wins | Old copy in the game install folder | Remove `...\7 Days To Die\Mods\<old>`; the user folder loads first |
| Antivirus/SmartScreen warning | Unsigned updater replaces DLLs | Code-sign the updater/DLL or document hashes |

More: [Troubleshooting](../../reference/troubleshooting/index.md).
