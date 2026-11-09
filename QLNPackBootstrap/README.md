# QLN Mod Pack Bootstrap

A client-side 7 Days to Die mod that keeps a server's mod pack in sync.

The bootstrap checks a signed `manifest.json`, shows the player exactly what will be
downloaded, verifies every file with SHA-256, stages the pack, and then a small
updater applies the files after the game closes and relaunches it.

It is **not** an Oxide plugin. Oxide runs only inside the dedicated server process
and cannot write files on a player's PC. A server-side plugin cannot install client
mods, and 7 Days to Die has no client mod-download protocol. The player must install
this bootstrap **once**; after that, the in-game updater handles the pack.

## How it works

```
Player PC                                      Your game server
┌──────────────────────────────┐   HTTP(S)    ┌──────────────────────────┐
│ 7 Days to Die client         │─────────────>│ QLNPackServer.exe        │
│   └─ 000_QLNPackBootstrap    │              │  /manifest.json          │
│        - consent window      │<─────────────│  /mods/*.zip             │
│        - SHA-256 check       │              │  reads Mods\ + config    │
│        - stage to Data       │              └──────────────────────────┘
│        - auto-restart       │
│                              │
│ QLNPackUpdater.exe           │
│   waits for game exit        │
│   backs up old mods          │
│   moves staged mods in       │
│   writes state, relaunches   │
└──────────────────────────────┘
```

Key detail: 7 Days to Die locks every mod DLL at startup. A running game cannot
replace another mod's DLL. The updater waits for the game process to exit, then
applies the pack; that is why a restart is required.

## Requirements

- 7 Days to Die V3.x (the config defaults to V3.4).
- Easy Anti-Cheat **off** for code/asset mods. The bootstrap itself is marked
  `SkipWithAntiCheat`, so it will not load with EAC on.
- .NET Framework 4.8 on Windows (the game already requires this).
- .NET SDK only if you are building from source.
- The player needs write access to `%APPDATA%\7DaysToDie\Mods`. That is a
  per-user folder; **no administrator/UAC prompt is needed**.

## Install the bootstrap (one time)

1. Download or build `000_QLNPackBootstrap.zip`. Host it somewhere stable
   (the same release repository is fine) and give players that link.
2. Extract it so the folder layout is:

   ```
   %APPDATA%\7DaysToDie\Mods\000_QLNPackBootstrap\
       ModInfo.xml
       QLNPackBootstrap.dll
       QLNPackUpdater.exe
       packbootstrap.xml
   ```

   `ModInfo.xml` must be directly inside `000_QLNPackBootstrap`, not one level
   deeper.

   The install target is the folder that contains `000_QLNPackBootstrap` — the
   active Mods directory the bootstrap was loaded from. If it is in
   `%APPDATA%\7DaysToDie\Mods\000_QLNPackBootstrap`, downloaded mods go into
   `%APPDATA%\7DaysToDie\Mods`. If you use a separate game copy or launcher
   profile, put the bootstrap in that copy's `Mods` folder and it follows
   automatically. Set `ModsRoot` in `packbootstrap.xml` only if you need to
   force a different folder.

3. Edit `packbootstrap.xml` in the bootstrap folder and set at least:
   - `ManifestUrl` to your server agent, for example:
     `http://YOUR.SERVER.IP:8765/manifest.json`
   - `AccessToken` to the same value as `QLNPackServer.json` (leave both empty
     if you are not using a token).
   - `AllowInsecureHttp` to `true` while using the plain HTTP agent. Set it to
     `false` when the agent is behind HTTPS.
   - `AllowedHosts` to your server host, or leave it empty to allow any host.
   - `AutoRestart` to `true` so the client restarts by itself after the pack is
     staged.
   - `CheckOnMainMenu` to auto-check when the main menu opens. `ShowMainMenuButton`
     adds a **Check Mod Pack Updates** button at the top-right of the main menu
     so the window can be reopened after it is closed.
   - `MaxParallelDownloads` to control parallel downloads. Default `4`; range
     `1`–`10`. Google Drive often throttles above 3–4 connections.
   - Optionally paste `public-key.xml` into `PublicKeyXml` and set
     `RequireSignature` to `true`.

   The download window shows a scrollable list with a progress bar for every
   mod, plus the number completed and the active parallelism.

4. Start 7 Days to Die **without EAC**.
5. At the main menu, the **QLN Mod Pack** window appears. Review the list, then
   click **Download & Install**. If you close it, click **Check Mod Pack
   Updates** at the top-right of the main menu to reopen it.
6. When staging finishes, the client shows a short countdown and restarts
   automatically. `QLNPackUpdater.exe` applies the pack and relaunches the game.
   If `AutoRestart` is `false`, click **Restart & Update** instead.

After the first install, players only need to launch normally; the bootstrap
checks the manifest and offers updates. Downloaded mods do **not** need a
`ModInfo.xml`: the bootstrap installs the archive root or a single wrapper folder
and falls back to the folder name when metadata is missing.

The server still needs the same mods in its own `Mods` folder. This bootstrap
only handles the client side. If a player already has an old copy of a pack mod
in the game's installation folder (`...\7 Days To Die\Mods`), remove it: the user
folder `%APPDATA%\7DaysToDie\Mods` loads first and would otherwise win with the
new copy, leaving the old duplicate ignored.

## Build from source

```powershell
# from this folder
powershell -ExecutionPolicy Bypass -File .\build.ps1

# server agent
cd ..\QLNPackServer
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

The output is `dist\000_QLNPackBootstrap\`. If the game is not installed in the
default Steam library, pass the path:

```powershell
dotnet build .\QLNPackBootstrap.csproj -c Release -p:GameDir="D:\Steam\steamapps\common\7 Days To Die"
dotnet build .\updater\QLNPackUpdater.csproj -c Release
```

The main project targets `net48` because it references the game's assemblies.
`QLNPackUpdater.exe` is a plain `net48` console application and has no game
dependencies. The server agent is also `net48` and has no external DLLs.

## Server-hosted pack (recommended)

This is the mode where clients download directly from your game server. The
server-side agent lives in the sibling folder `..\QLNPackServer`.

1. On the server, build or copy `QLNPackServer.exe` and
   `QLNPackServer.json` somewhere convenient. Edit the JSON:
   - `modsPath` → the server's `Mods` folder (`Mods` when the agent sits in the
     server root).
   - `include` → the list of client-required mod folder names. This is the list
     from the server's config; only these are served.
   - `port` and `accessToken` → choose a port and a shared token.
   - `signingKeyXml` → optional path to `private-key.xml` if you want signed
     manifests.
2. Start `QLNPackServer.exe --config .\QLNPackServer.json`. It uses one inbound
   TCP port by default; if you cannot open another port, put it behind an
   existing HTTPS reverse proxy or an outbound tunnel (Cloudflare Tunnel,
   Tailscale Funnel, etc.). It cannot share the game's UDP/Steam ports.
3. On each client, configure `packbootstrap.xml`:

   ```xml
   <ManifestUrl>http://YOUR.SERVER.IP:8765/manifest.json</ManifestUrl>
   <AccessToken>your-shared-token</AccessToken>
   <AllowInsecureHttp>true</AllowInsecureHttp>
   <AllowedHosts></AllowedHosts>
   <AutoRestart>true</AutoRestart>
   <ShowMainMenuButton>true</ShowMainMenuButton>
   ```

4. The bootstrap fetches the manifest, downloads the zips from the server's
   `Mods` folder, verifies hashes, stages the pack, and restarts the client
   automatically after a short countdown.

The agent zips the mod folders itself and caches them in `cache\`; you do not
need GitHub, a release pipeline, or a separate manifest file. See
`..\QLNPackServer\README.md` for firewall, HTTPS reverse-proxy, signing, and
service setup.

If you use HTTPS behind Caddy/Nginx, set the agent's `publicBaseUrl` to the HTTPS
URL and set the client's `AllowInsecureHttp` to `false`.

## Alternative: publish to GitHub Releases

### 1. Arrange the pack

Create a folder with one subfolder per **client-required** mod. Do not include
server-only mods (pure XML gameplay modlets are already pushed by the server).

```
pack\
  QuantumLogisticsNetwork\
    ModInfo.xml
    QLN.dll
    Config\...
    Resources\...
  SomeOtherClientMod\
    ModInfo.xml
    ...
```

Each zip is created with the mod contents at the **zip root**, so `ModInfo.xml`
ends up directly inside the installed mod folder. `ModInfo.xml` is optional:
pure `Config`/`XUi`/asset mods are also packed, using the folder name as the
mod name and a `0.0.0` version placeholder.

### 2. Generate a signing key (recommended)

```powershell
.\release\new-pack-key.ps1 -OutputDir .\keys
```

Keep `private-key.xml` out of the repository. The public key goes into the
bootstrap config.

### 3. Build the manifest and zips

```powershell
.\release\pack-release.ps1 `
  -PackDir .\pack `
  -Version 1.4.0 `
  -GameVersion 3.4 `
  -BaseUrl "https://github.com/OWNER/REPO/releases/download/v1.4.0" `
  -PrivateKeyXml .\keys\private-key.xml `
  -OutputDir .\release-out
```

The output contains:

- `manifest.json`
- `manifest.json.sig` (if a private key was supplied)
- one zip per mod

Upload all of it to a GitHub Release, for example:

```powershell
gh release create v1.4.0 .\release-out\* --title "v1.4.0" --notes "Mod pack 1.4.0"
```

Point `ManifestUrl` at either:

- `https://github.com/OWNER/REPO/releases/latest/download/manifest.json`, or
- `https://github.com/OWNER/REPO/releases/download/v1.4.0/manifest.json`.

`releases/latest/download` follows the newest release; a versioned URL is
reproducible. The RSA signature protects the manifest even when the URL is
mutable.

### 4. Configure the bootstrap

Paste `public-key.xml` into `packbootstrap.xml`:

```xml
<PublicKeyXml><![CDATA[<RSAKeyValue>
  <Modulus>...</Modulus>
  <Exponent>AQAB</Exponent>
</RSAKeyValue>]]></PublicKeyXml>
<RequireSignature>true</RequireSignature>
```

## Google Drive, Dropbox, OneDrive, and similar hosts

The client already downloads a **zip**, verifies its SHA-256, extracts it to a
staging folder, and the updater moves the extracted folders into `Mods` before
the client relaunches. You do not unzip anything manually.

What the client now also handles is the difference between a **share link** and
a direct file link:

- Google Drive file links are converted to a `drive.usercontent.google.com`
  download URL. If Google returns its virus-scan/confirm page, the client parses
  that page and retries once.
- Dropbox links have `dl=0` changed to `dl=1`.
- OneDrive links get `download=1`.
- Any other HTTPS URL is downloaded as-is.

### Build the manifest for remote zips

You can generate the items file from the same `pack\` folder the packer uses:

```powershell
.\release\export-drive-items.ps1 `
  -PackDir .\pack `
  -ItemsFile .\release\google-drive-items.json
```

It creates the zips, writes relative `file` paths, fills URL placeholders, and
preserves existing Drive URLs on reruns. Pass `-UrlMap .\drive-urls.json` to
fill real URLs automatically.

If you only have a folder of zips, use `-FromZips -ZipDir .\path\to\zips`; the
exporter reads `ModInfo.xml` from inside each zip. Zips without it are still
included: the archive or wrapper folder name becomes the install folder, the
version falls back to `0.0.0`, and the client installs the files as-is.

Or fill the JSON manually. Upload each mod as its own zip to Drive/Dropbox,
share it as **Anyone with the link**, and record the share link plus the local
zip path in a JSON file:

```json
[
  {
    "name": "QuantumLogisticsNetwork",
    "displayName": "Quantum Logistics Network",
    "version": "0.0.1",
    "url": "https://www.dropbox.com/scl/fi/.../QuantumLogisticsNetwork-0.0.1.zip?rlkey=...&dl=0",
    "file": "zips\\QuantumLogisticsNetwork-0.0.1.zip",
    "installFolder": "QuantumLogisticsNetwork"
  }
]
```

Then:

```powershell
.\release\build-manifest-from-urls.ps1 `
  -ItemsFile .\release\url-items.json `
  -OutputDir .\release-out `
  -PackVersion 1.0.0 `
  -GameVersion 3.4 `
  -PrivateKeyXml .\keys\private-key.xml
```

This computes SHA-256 and size from the **local** zip (the same file you
uploaded), so the client can verify the remote download.

The `file` field is a local path used only for hashing. Prefer a path relative to
the items file, for example `zips\QuantumLogisticsNetwork-0.0.1.zip`; the builder
resolves it from the items file folder. The remote `url` is still an absolute
Google Drive/Dropbox share link — it is not a filesystem path. `-ZipDir` can
point at a different local zip folder.

Host `manifest.json` anywhere the clients can reach — a normal web host, GitHub,
or even Drive/Dropbox. If you sign it, upload `manifest.json.sig` too and set
`SignatureUrl` explicitly; automatic `.sig` guessing only applies to plain
`.json` URLs, not share pages.

### Limits

- Google Drive/Dropbox **folder** links are not supported. Use one zip per mod
  with its own file link.
- Google Drive login/permission pages and quota pages cannot be bypassed. The
  file must be public ("Anyone with the link") and under quota.
- Google Drive may show a virus-scan confirmation for larger files; the client
  handles the common form, but a direct link is more reliable.
- OneDrive short links sometimes redirect to a preview page instead of a file.
  If the client reports an HTML page, replace the link with a direct download
  URL.
- MediaFire, Mega, and other hosts that require JavaScript, decryption, or a
  multi-step page are not supported. Use a direct zip URL or another host.
- These services have bandwidth and download limits. For a public server, use
  `QLNPackServer` or a normal static host/CDN.
- If `AllowedHosts` is not empty, add the provider hosts
  (`drive.google.com`, `drive.usercontent.google.com`, `www.dropbox.com`,
  `dl.dropboxusercontent.com`, `1drv.ms`, `onedrive.live.com`) or leave it
  empty to allow any HTTPS host.

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
| `packVersion` | Version shown in the UI. Changing it is not required if the files changed; per-mod version/hash is used. |
| `gameVersion` | Optional warning when it does not match the bootstrap config. |
| `mods[].url` | HTTPS URL of a zip. GitHub release asset URLs are fine. |
| `mods[].sha256` | Required. The bootstrap refuses a download that does not match. |
| `mods[].size` | Required. Used for the download size check and progress. |
| `mods[].target` | `client`, `server`, or `both`. The bootstrap installs only `client`/`both`. |
| `mods[].installFolder` | Target folder under `Mods`. Letters, digits, `_`, `-` only. Defaults to `name`. |
| `remove` | Optional folder names to back up and remove. Only list pack-owned folders. |

The release script sets `target` to `client` for every packed mod. Put only
client-required mods in the pack folder.

When you remove a mod from the pack, either add its folder to `remove` or set
`RemoveObsolete` to `true` in `packbootstrap.xml`. Otherwise the old folder stays
in the player's Mods folder (the bootstrap never deletes folders it does not own).

## Security and permissions

### Unzipping and UAC

Extraction into `%APPDATA%\7DaysToDie\Mods` does **not** require administrator
rights. Windows does not show a UAC prompt for that folder. Do not install the
bootstrap or the game's mods into `C:\Program Files...`; that is where elevation
can become necessary.

The bootstrap asks for the player's consent in its own UI. Windows will not ask
on its own, and a server cannot grant consent on the client's behalf.

### What the bootstrap enforces

- HTTPS for the manifest and downloads (HTTP only if `AllowInsecureHttp` is on).
- Host allowlist for mod URLs.
- SHA-256 for every archive before extraction.
- Optional RSA-3072 signature over the exact manifest bytes.
- Zip-slip protection and a maximum entry/uncompressed-size limit.
- Backups of replaced folders under `Data\backup`.
- It only touches folders listed in the manifest. It never wipes the Mods folder.
- It never runs an installer script from the manifest.

### Server-hosted mode

- Set a shared `accessToken` in `QLNPackServer.json` and the client
  `packbootstrap.xml`. It stops random scans; players still know it.
- For integrity, set `signingKeyXml` on the server and `PublicKeyXml` on the
  client. HTTP can be modified in transit, but the signature and per-zip SHA-256
  will detect it.
- Prefer HTTPS through Caddy/Nginx for public servers. Set the agent's
  `publicBaseUrl` to the HTTPS URL and the client's `AllowInsecureHttp` to
  `false`.
- Without a token and without a signature, anyone who can reach the port can
  download the pack and could serve a tampered manifest. Do not expose that
  configuration to the public internet.

### Antivirus and SmartScreen

An unsigned updater EXE that waits for a process and replaces DLLs can look
suspicious to antivirus. If you distribute this widely, code-sign
`QLNPackUpdater.exe` and `QLNPackBootstrap.dll`. Keep the source available and
publish the manifest hashes. For a friends-only server, the source + hashes are
usually enough.

Downloaded zips may carry Mark-of-the-Web. The bootstrap extracts them with
.NET and verifies SHA-256; this is the integrity check that matters.

### Public vs private artifacts

If players are allowed to have the mods, the packaged zips are not secret. Keep
the **source** repository private and publish the built artifacts in a public
release repository. This avoids authentication entirely.

If the artifacts must be private:

- Do not embed a GitHub PAT in the bootstrap; it can be extracted.
- Use a small endpoint you control (Cloudflare Worker + R2/S3 presigned URLs, or
  a GitHub App token service) that authenticates the player and returns
  short-lived URLs.
- For a friends-only server, a shared pack password over HTTPS is usually enough.

A Maven/Nexus repository is unnecessary. A static `manifest.json` plus immutable
zip URLs is the same idea with less tooling.

## Data and logs

Everything the bootstrap writes lives inside the mod folder:

```
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
```

- `state.xml` records what the last successful update installed.
- `pending.xml` tells the updater what to apply.
- `backup\<timestamp>\<mod>` holds the previous version of each replaced folder.
- The last `KeepBackups` backup sets are kept.

If an update gets stuck, close the game and inspect `Data\logs`. To discard a
staged update, delete `Data\pending.xml` and `Data\staging`.

## Troubleshooting

**The window does not appear**

- The mod is client-side; it must be in `%APPDATA%\7DaysToDie\Mods`.
- `ModInfo.xml` must be directly inside the `000_QLNPackBootstrap` folder.
- EAC must be off; `SkipWithAntiCheat` makes the mod skip itself otherwise.
- Check `Data\logs\bootstrap.log`.

**I closed the update window**

Click **Check Mod Pack Updates** at the top-right of the main menu. It reopens
the window and checks again. If an update is already staged, the button reads
**Finish Mod Pack Update**. Set `ShowMainMenuButton` to `false` to hide it.

**"Configuration needed"**

The `ManifestUrl` is empty or invalid, `RequireSignature` is on without a public
key, or the bootstrap is not inside a folder named `Mods`. Use the **Open Config**
button, install it under `...\Mods\000_QLNPackBootstrap`, or set `ModsRoot` to
the active Mods directory.

**"Could not download the manifest"**

The URL is wrong, the server agent is not running, the firewall is closed, or
the token does not match. The UI only shows this when `ShowCheckErrors` is true.

**The server returns 403**

The client's `AccessToken` and `QLNPackServer.json`'s `accessToken` must be
identical. The token is added to both the manifest request and each zip request.

**The updater waits forever**

It waits for the game process to exit. Close 7 Days to Die normally. If the game
crashed, the process may still be running in Task Manager.

**The updater fails**

Read `Data\logs\updater.log`. It keeps the pending file, writes
`Data\updater-failed.txt`, and exits with a message box. Common causes: the game
was still holding a file, antivirus blocked the move, or the Mods folder is not
writable. The bootstrap will not auto-restart while that marker exists; fix the
cause and click **Restart & Update** (the updater clears the marker when it
starts).

**The game starts with the old mods**

The update is staged but not applied. With `AutoRestart` on, the client should
restart itself after a few seconds. If it did not, use **Restart & Update** in
the window, or close the game and run `QLNPackUpdater.exe` manually with the same
arguments from the log. The bootstrap starts it for you normally.

If you are in a world when the countdown ends, the client waits until you return
to the main menu instead of quitting out from under the session.

**Signature mismatch**

The manifest changed after it was signed, the server did not reload the new mod
yet, or the wrong public key is configured. Check `signingKeyXml` on the server
and `PublicKeyXml` on the client. For the GitHub mode, re-run `pack-release.ps1`
with the matching private key and re-upload both `manifest.json` and
`manifest.json.sig`.

## Tests

The offline harnesses do not need the game running:

```powershell
dotnet build .\tests\CoreHarness\CoreHarness.csproj -c Debug
.\tests\CoreHarness\bin\Debug\net48\CoreHarness.exe

dotnet build .\tests\UpdaterHarness\UpdaterHarness.csproj -c Debug
.\tests\UpdaterHarness\bin\Debug\net48\UpdaterHarness.exe .\dist\000_QLNPackBootstrap\QLNPackUpdater.exe
```

`CoreHarness` covers manifest validation, path traversal, token URLs, hashing,
zip-slip protection, change detection, staging, and RSA signatures.
`UpdaterHarness` runs the real updater against a temporary Mods folder and checks
the backup, state file, and replaced files.

The server agent has its own end-to-end test:

```powershell
cd ..\QLNPackServer
powershell -ExecutionPolicy Bypass -File .\tests\test-server.ps1
```

## Limitations

- The first bootstrap install is manual. There is no way for a server to put the
  first file on a player's PC.
- A player can decline the update. The server cannot force a file write.
- Mods are applied only at restart; 7 Days to Die does not hot-load DLLs.
- The bootstrap updates other mods, but it does not update itself. Replace its
  folder while the game is closed when a new bootstrap build is needed.
- This package does not include a server-side version gate. A server-side Oxide
  or 7DTD mod can advertise the required pack version and kick outdated clients,
  but that is a separate piece; the client still uses this bootstrap to install.

## Files

| File | Purpose |
|---|---|
| `src/PackBootstrapMod.cs` | The `IModApi` entry point. |
| `src/PackBootstrapBehaviour.cs` | Main-menu window and download/stage flow. |
| `src/PackInstaller.cs` | Manifest validation, hashing, safe extraction, staging. |
| `src/PackConfig.cs` | XML configuration. |
| `src/PackManifest.cs` | JSON manifest model. |
| `src/PackUrls.cs` | Adds the shared access token to URLs. |
| `src/PackDownloadResolver.cs` | Resolves Drive/Dropbox/OneDrive share links to direct downloads. |
| `src/PackState.cs` / `src/PackPending.cs` | Installed/pending state. |
| `src/PackSignature.cs` | RSA manifest signature verification. |
| `packbootstrap.xml` | Client settings (URL, token, AutoRestart, signing key). |
| `updater/Program.cs` | Waits for game exit, applies the pack, relaunches. |
| `release/pack-release.ps1` | GitHub-mode: zips mods, hashes them, writes/signs the manifest. |
| `release/build-manifest-from-urls.ps1` | Builds a manifest for zips already uploaded to Drive/Dropbox/etc. |
| `release/export-drive-items.ps1` | Reads the pack folder/zips and generates `google-drive-items.json` with relative file paths. |
| `release/match-drive-links.ps1` | Matches a list of Drive links to items and writes a match report. |
| `release/new-pack-key.ps1` | Generates an RSA key pair. |
| `tests/CoreHarness` | Offline tests for validation, hashing, zip safety, staging. |
| `tests/UpdaterHarness` | Offline test for the updater. |
| `..\QLNPackServer` | Server agent that serves the server's `Mods` folder. |

## License

Use it however you like on your own server. The bootstrap is provided as-is;
test updates on a backup save before rolling them out.
