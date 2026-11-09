# Building a Google Drive Mod Pack

This is the exact Google Drive-only setup. The client bootstrap is installed
once; after that it downloads the zips from Google Drive, verifies hashes,
extracts them, restarts the client, and applies them.

No `QLNPackServer`, no open port, and no GitHub are required.

---

## What you need

- A Google account and a Drive folder for the pack.
- The built bootstrap zip:
  `...\Oxide\QLNPackBootstrap\dist\000_QLNPackBootstrap.zip`
- One zip per client-required mod.
- The local copies of those zips (needed to compute SHA-256).
- Optional: a signing key if you want signed manifests.

The server still needs the same mods installed in its own `Mods` folder. This
guide only distributes them to clients.

---

## Overview

```text
Google Drive
  QLN Pack/
    QuantumLogisticsNetwork-0.0.1.zip   <- public file link
    SomeOtherClientMod-1.2.3.zip        <- public file link
    manifest.json                        <- public file link
    manifest.json.sig                    <- optional, public file link

Client
  000_QLNPackBootstrap
    packbootstrap.xml  -> ManifestUrl = manifest.json share link
    downloads each zip from its Drive share link
    verifies SHA-256
    extracts to Mods
    restarts, updater applies, relaunches
```

---

## Step 1 — Prepare one zip per mod

Create one zip per **client-required** mod.

Correct zip layouts:

```text
QuantumLogisticsNetwork-0.0.1.zip
  ModInfo.xml
  QLN.dll
  Config\...
  Resources\...
  UIAtlases\...
```

or:

```text
QuantumLogisticsNetwork-0.0.1.zip
  QuantumLogisticsNetwork\
    ModInfo.xml
    QLN.dll
    ...
```

The client accepts `ModInfo.xml` at the zip root or in a single top-level folder.
It does **not** accept multiple mod folders in one zip.

On Windows, the easiest way:

1. Open the mod folder.
2. Select the **contents** (`ModInfo.xml`, `QLN.dll`, `Config`, etc.).
3. Right-click → Send to → Compressed (zipped) folder.

Or zip the whole mod folder; that gives one top-level folder, which is also
accepted.

Do not include:

- `Data\`
- logs
- backup folders
- `.git`
- `cache`
- the bootstrap itself

Verify by opening the zip: `ModInfo.xml` must be visible at the root or exactly
one folder deep.

---

## Step 2 — Upload the mod zips to Google Drive

1. In Google Drive, create a folder, for example `QLN Pack`.
2. Upload each mod zip as a **file**.
3. Do not convert it to a Google Doc/Sheet. Upload the `.zip` unchanged.
4. Keep the local zip files on your PC. You need them for the manifest hashes.

Suggested names:

```text
QuantumLogisticsNetwork-0.0.1.zip
SomeOtherClientMod-1.2.3.zip
```

The Drive file name does not control the installed folder; `installFolder` in
the manifest does.

---

## Step 3 — Share each zip publicly

For every zip:

1. Right-click the file in Google Drive.
2. Choose **Share**.
3. Under **General access**, choose **Anyone with the link**.
4. Keep the role as **Viewer**.
5. Open the share settings gear and make sure
   **Viewers and commenters can see the option to download, print, and copy**
   is enabled. Otherwise the client can see the file but cannot download it.
6. Click **Copy link**.
7. Save that link. It looks like:

```text
https://drive.google.com/file/d/FILE_ID/view?usp=sharing
```

If the link contains `resourcekey=...`, keep the whole link. Shared-drive files
sometimes need it.

Test each link in a private/incognito browser window where you are not signed in.
If it asks you to sign in or request access, the sharing is wrong.

If you use a Google Workspace account, an administrator may block
“Anyone with the link” sharing. Use a personal Google account or another host.

Do **not** use folder links like:

```text
https://drive.google.com/drive/folders/FOLDER_ID
```

The client needs a file ID. The manifest builder rejects folder links.

---

## Step 4 — Create the items file

### Automatic (recommended)

If your mods are in a `pack\` folder, generate the items file from it:

```powershell
.\release\export-drive-items.ps1 `
  -PackDir .\pack `
  -ItemsFile .\release\google-drive-items.json
```

It:

- reads each mod folder's `ModInfo.xml`,
- creates `release\zips\<Mod>-<Version>.zip`,
- writes `release\google-drive-items.json`,
- uses relative `file` paths like `zips\QuantumLogisticsNetwork-1_0_0.zip`,
- fills URL placeholders like `REPLACE_QuantumLogisticsNetwork_ID`,
- preserves real Drive URLs from an existing items file on reruns.

When a mod changes, upload its new zip and either update its URL (if you created
a new Drive file with a new ID) or use **Manage versions** on the existing Drive
file so the old share link still points at the new content. Reruns preserve the
old URLs, so a new file ID must be updated manually or through `-UrlMap`.

If you already use `pack-release.ps1` and want to reuse its zips instead of
re-zipping:

```powershell
.\release\export-drive-items.ps1 `
  -PackDir .\pack `
  -ZipDir .\release-out `
  -UseExistingZips `
  -ItemsFile .\release\google-drive-items.json
```

If you only have a folder of zips and no mod folders, use `-FromZips`:

```powershell
.\release\export-drive-items.ps1 `
  -FromZips `
  -ZipDir .\path\to\zips `
  -ItemsFile .\release\google-drive-items.json
```

It reads `ModInfo.xml` from inside each zip. `installFolder` comes from the zip's
top-level folder, or from the zip filename with the version suffix removed.

After uploading, either edit the URLs in the generated JSON or maintain a URL
map:

```json
{
  "QuantumLogisticsNetwork": "https://drive.google.com/file/d/REAL_ID/view?usp=sharing",
  "SomeOtherClientMod": "https://drive.google.com/file/d/REAL_OTHER_ID/view?usp=sharing"
}
```

Then:

```powershell
.\release\export-drive-items.ps1 `
  -PackDir .\pack `
  -ItemsFile .\release\google-drive-items.json `
  -UrlMap .\release\drive-urls.json
```

The manifest builder refuses URLs containing `REPLACE_`, so you cannot
accidentally deploy placeholders.

### Fill links from a list (matcher)

If you already have a bunch of Drive links, put them in a file and let the
matcher fill the items:

```powershell
.\release\match-drive-links.ps1 `
  -LinksFile .\drive-links.txt `
  -ItemsFile .\release\google-drive-items.json
```

Supported link files:

- One URL per line. The matcher tries to read the public Drive page title to get
  the remote filename.
- CSV with `file,url` or `name,url`.
- JSON array of `{ "file": "...", "url": "..." }` or
  `{ "name": "...", "url": "..." }`.
- JSON object keyed by name: `{ "QuantumLogisticsNetwork": "https://..." }`.

The matcher reads names/versions from the generated items file (from
`ModInfo.xml`) and normalizes filenames/versions. It writes a report:

```text
release\drive-match-report.txt
```

Review `MATCHED`, `UNMATCHED LINK`, and `UNMATCHED ITEM` lines. The most reliable
setup is to name the Google Drive files exactly like the local zips, for example
`QuantumLogisticsNetwork-1_0_0.zip`. Raw URLs without names rely on Drive's page
title; if Drive blocks the page, use a CSV/JSON map instead.

### Manual

Create a local file, for example:

`...\Oxide\QLNPackBootstrap\release\google-drive-items.json`

Use the example:

`...\Oxide\QLNPackBootstrap\release\google-drive-items.example.json`

Fill in each mod:

```json
[
  {
    "name": "QuantumLogisticsNetwork",
    "displayName": "Quantum Logistics Network",
    "version": "0.0.1",
    "url": "https://drive.google.com/file/d/FILE_ID/view?usp=sharing",
    "file": "zips\\QuantumLogisticsNetwork-0.0.1.zip",
    "installFolder": "QuantumLogisticsNetwork"
  },
  {
    "name": "SomeOtherClientMod",
    "displayName": "Some Other Client Mod",
    "version": "1.2.3",
    "url": "https://drive.google.com/file/d/OTHER_ID/view?usp=sharing",
    "file": "zips\\SomeOtherClientMod-1.2.3.zip",
    "installFolder": "SomeOtherClientMod"
  }
]
```

Create a `zips` folder next to `google-drive-items.json` and put the local
zips in it. Relative `file` paths are resolved from the items file's folder, not
from wherever you run PowerShell. You can also pass `-ZipDir .\some\zip\folder`
to the builder.

The remote `url` is still an **absolute Google Drive share link**. That is not a
filesystem path; it points at Google Drive. The client cannot resolve a
filesystem-relative link to a file on your PC.

Field meanings:

| Field | Meaning |
|---|---|
| `name` | 7DTD-safe mod name; letters/digits/`_`/`-`. |
| `displayName` | Name shown in the client UI. |
| `version` | Version shown in the UI. |
| `url` | Google Drive **file share link** (absolute; not a local path). |
| `file` | Local zip path, used only to compute SHA-256 and size. Prefer relative to the items file, e.g. `zips\Mod.zip`. |
| `installFolder` | Folder name under the player's `Mods`. Safe characters only. |

Relative `file` paths are resolved from the items file's folder. `-ZipDir` can
point somewhere else. Absolute `file` paths still work, but the point of the
relative setup is that the JSON stays portable.

---

## Step 5 — Build the manifest

Open PowerShell:

```powershell
cd "C:\Users\robert\Desktop\7 Days 2 Die Mods\Oxide\QLNPackBootstrap"

.\release\build-manifest-from-urls.ps1 `
  -ItemsFile .\release\google-drive-items.json `
  -OutputDir .\release-out `
  -PackVersion 1.0.0 `
  -GameVersion 3.4
```

Relative `file` paths resolve from the folder containing
`google-drive-items.json` (`release\zips\...` in this example).

If the zips live somewhere else, add `-ZipDir`:

```powershell
.\release\build-manifest-from-urls.ps1 `
  -ItemsFile .\release\google-drive-items.json `
  -ZipDir .\release\zips `
  -OutputDir .\release-out `
  -PackVersion 1.0.0 `
  -GameVersion 3.4
```

With `-ZipDir`, use just the zip file name in `file`, for example
`"file": "QuantumLogisticsNetwork-0.0.1.zip"`.

Output:

```text
release-out\manifest.json
```

If you want signing:

```powershell
.\release\new-pack-key.ps1 -OutputDir .\keys
```

Then:

```powershell
.\release\build-manifest-from-urls.ps1 `
  -ItemsFile .\release\google-drive-items.json `
  -OutputDir .\release-out `
  -PackVersion 1.0.0 `
  -GameVersion 3.4 `
  -PrivateKeyXml .\keys\private-key.xml
```

Output also includes:

```text
release-out\manifest.json.sig
```

Keep `keys\private-key.xml` on your PC only. Never upload it to Drive and never
send it to players.

The script computes hashes from the **local** zips. If you later change a zip,
rerun the script.

---

## Step 6 — Upload the manifest to Google Drive

Upload:

```text
release-out\manifest.json
release-out\manifest.json.sig   (only if signed)
```

Share both as **Anyone with the link**, exactly like the mod zips.

Copy the share links:

```text
https://drive.google.com/file/d/MANIFEST_ID/view?usp=sharing
https://drive.google.com/file/d/SIGNATURE_ID/view?usp=sharing
```

The client can resolve them; you do not need to convert them to direct links.

### Keep the manifest link stable

If you upload a **new** `manifest.json` file each update, it gets a **new file
ID**, and every player would need a new `ManifestUrl`. Avoid that.

Use Google Drive's **Manage versions** on the existing `manifest.json`:

1. Right-click `manifest.json` in Drive.
2. Choose **Manage versions**.
3. Click **Upload new version**.
4. Select the new `release-out\manifest.json`.
5. Upload.

The file ID and share link stay the same. Do the same for
`manifest.json.sig` if signed.

If your Drive UI does not show Manage versions, open the file's information panel
and look for **Manage versions**, or keep the manifest on a small static host
instead.

---

## Step 7 — Configure the client

Edit the source config:

`...\Oxide\QLNPackBootstrap\packbootstrap.xml`

### Unsigned

```xml
<ManifestUrl>https://drive.google.com/file/d/MANIFEST_ID/view?usp=sharing</ManifestUrl>
<SignatureUrl></SignatureUrl>
<PublicKeyXml><![CDATA[]]></PublicKeyXml>
<RequireSignature>false</RequireSignature>
<AccessToken></AccessToken>
<AllowedHosts></AllowedHosts>
<AllowInsecureHttp>false</AllowInsecureHttp>
<AutoRestart>true</AutoRestart>
<ModsRoot></ModsRoot>
<GameVersion>3.4</GameVersion>
```

### Signed

```xml
<ManifestUrl>https://drive.google.com/file/d/MANIFEST_ID/view?usp=sharing</ManifestUrl>
<SignatureUrl>https://drive.google.com/file/d/SIGNATURE_ID/view?usp=sharing</SignatureUrl>
<PublicKeyXml><![CDATA[<RSAKeyValue>
  <Modulus>...</Modulus>
  <Exponent>AQAB</Exponent>
</RSAKeyValue>]]></PublicKeyXml>
<RequireSignature>true</RequireSignature>
<AccessToken></AccessToken>
<AllowedHosts></AllowedHosts>
<AllowInsecureHttp>false</AllowInsecureHttp>
<AutoRestart>true</AutoRestart>
<ModsRoot></ModsRoot>
<GameVersion>3.4</GameVersion>
```

Paste the contents of `keys\public-key.xml` into `PublicKeyXml`.

Notes:

- `AccessToken` is only for `QLNPackServer`; leave it empty for Drive.
- `AllowInsecureHttp` is `false` because Drive is HTTPS.
- `AllowedHosts` empty allows Drive. If you set it, include
  `drive.google.com` and `drive.usercontent.google.com`.
- `ModsRoot` empty means “install next to `000_QLNPackBootstrap`”, i.e. the
  active `Mods` folder. Leave it empty unless you need to force a path.
- `SignatureUrl` is required when `PublicKeyXml` is set; the client does not
  auto-guess `.sig` for Drive share links.

Build the client zip:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Send:

`...\dist\000_QLNPackBootstrap.zip`

---

## Step 8 — Test it before sending

### 8.1 Test the manifest link

Open the `ManifestUrl` in a private browser window.

- If you see the JSON content or a Drive preview, that is fine.
- If you see a sign-in/request-access page, sharing is wrong.
- If you see a quota error, wait or use another host.

The file ID is the part of the share link between `/d/` and `/view`:

```text
https://drive.google.com/file/d/MANIFEST_ID/view?usp=sharing
                              ^^^^^^^^^^^
```

You can also test the direct URL:

```text
https://drive.usercontent.google.com/download?id=MANIFEST_ID&export=download&confirm=t
```

If the manifest has `resourcekey=...`, append:

```text
&resourcekey=YOUR_RESOURCE_KEY
```

### 8.2 Test one mod zip

Open one mod's share link in a private window. It should be downloadable.

Direct test URL:

```text
https://drive.usercontent.google.com/download?id=FILE_ID&export=download&confirm=t
```

If that downloads the zip, the client should be able to fetch it.

### 8.3 Test on a clean client

1. Remove any manually installed copies of the pack mods from:
   - `%APPDATA%\7DaysToDie\Mods`
   - `...\7 Days To Die\Mods`
2. Extract `000_QLNPackBootstrap.zip` to
   `%APPDATA%\7DaysToDie\Mods\000_QLNPackBootstrap`.
3. Start `7DaysToDie.exe` (no EAC).
4. At the main menu, the QLN Mod Pack window should appear.
5. Click **Download & Install**.
6. Watch the download, verification, and “All mods downloaded” countdown.
7. The client should restart; the updater applies the pack and relaunches.
8. Check that the mods are in `%APPDATA%\7DaysToDie\Mods`, next to the bootstrap.
9. Check the logs if anything fails:

```text
%APPDATA%\7DaysToDie\Mods\000_QLNPackBootstrap\Data\logs\bootstrap.log
%APPDATA%\7DaysToDie\Mods\000_QLNPackBootstrap\Data\logs\updater.log
```

---

## Step 9 — Send it to the player

Send:

1. `000_QLNPackBootstrap.zip`
2. The `ManifestUrl` only if it is not already inside the zip config.
3. The `PLAYER-README.txt` is already inside the zip.

If you send the bootstrap zip through Google Drive too, share that zip as
**Anyone with the link** and send its link. That bootstrap file is separate from
the pack manifest; the player installs it once.

Suggested message:

```text
QLN server mod pack setup

1. Close 7 Days to Die.
2. Press Win+R, paste %APPDATA%\7DaysToDie\Mods, press Enter.
3. Right-click 000_QLNPackBootstrap.zip -> Extract All -> destination
   %APPDATA%\7DaysToDie\Mods -> Extract.
   Let Windows create the 000_QLNPackBootstrap folder.

   You must end up with:
   ...\Mods\000_QLNPackBootstrap\ModInfo.xml
   ...\Mods\000_QLNPackBootstrap\QLNPackBootstrap.dll
   ...\Mods\000_QLNPackBootstrap\QLNPackUpdater.exe
   ...\Mods\000_QLNPackBootstrap\packbootstrap.xml

   If you get 000_QLNPackBootstrap\000_QLNPackBootstrap, move the inner
   folder up one level. Do not dump the files directly into Mods.

4. Start 7 Days to Die WITHOUT EAC. The reliable way is to launch
   7DaysToDie.exe from the game folder with Steam already running,
   or use your no-EAC shortcut.

5. At the main menu, click Download & Install on the QLN Mod Pack window.
6. Wait for "All mods downloaded". It will restart automatically, apply the
   mods, and start again.

The mods install next to the bootstrap folder, not inside it.

If the window does not appear, check EAC and the folder layout. If you need help,
send %APPDATA%\7DaysToDie\Mods\000_QLNPackBootstrap\Data\logs\bootstrap.log
```

---

## Step 10 — Updating a Google Drive pack

### Update one mod

1. Create the new zip.
2. Upload it to Drive (new file or Manage versions).
3. Share it publicly.
4. Update `url-items.json` with the new link (if the file ID changed).
5. Rerun `build-manifest-from-urls.ps1`.
6. Upload the new `manifest.json` with **Manage versions** so the
   `ManifestUrl` stays the same.
7. If signed, update `manifest.json.sig` the same way.
8. Clients see the new hashes at their next main-menu check.

If you use **Manage versions** on the mod zip itself, the file ID and manifest
`url` stay the same, but you still must update the manifest hash from the new
local zip.

### Remove a mod

Use the manifest builder's `-Remove`:

```powershell
.\release\build-manifest-from-urls.ps1 `
  -ItemsFile .\release\google-drive-items.json `
  -OutputDir .\release-out `
  -PackVersion 1.1.0 `
  -GameVersion 3.4 `
  -Remove "OldMod,AnotherOld"
```

Or set `RemoveObsolete` to `true` in the client config. The client backs up
removed folders under `Data\backup`.

### Keep the manifest link stable

Use **Manage versions** for `manifest.json` and `manifest.json.sig`. If you
upload a new file and get a new file ID, every player's `ManifestUrl` must be
updated. Avoid that unless you have to.

---

## Step 11 — Google Drive problems

### “The URL returned an HTML page instead of a file”

- The link is a folder/preview/login page, not a public file.
- Share the file as **Anyone with the link**.
- Use a per-file zip, not a folder.
- Google Drive quota/permission pages cannot be bypassed.
- Try the direct URL:

```text
https://drive.usercontent.google.com/download?id=FILE_ID&export=download&confirm=t
```

### “Too many users have downloaded this file recently”

Google Drive has public download quotas. Wait, or use another host. For many
players, use `QLNPackServer` or a static host/CDN.

### Virus scan warning

Large public files can show Google's virus-scan/confirm page. The client parses
the common form and retries. If it still fails, paste the direct download URL in
the manifest instead of the share link.

### Signature mismatch

- The manifest changed after it was signed.
- You uploaded the wrong `manifest.json.sig`.
- The client has the wrong `PublicKeyXml`.
- `SignatureUrl` is missing or points at a different file.
Rerun the manifest builder with the matching private key, re-upload both files
with Manage versions, and check the client config.

### No pack window

- EAC is on.
- `ModInfo.xml` is not directly inside `000_QLNPackBootstrap`.
- The bootstrap is not in a folder named `Mods` (or `ModsRoot` is wrong).
- Check `bootstrap.log`.

### The updater fails

Read:

```text
%APPDATA%\7DaysToDie\Mods\000_QLNPackBootstrap\Data\logs\updater.log
%APPDATA%\7DaysToDie\Mods\000_QLNPackBootstrap\Data\updater-failed.txt
```

Fix the cause and click **Restart & Update**. The bootstrap will not auto-restart
while the failure marker exists.

---

## Google Drive checklist

- [ ] One zip per mod, `ModInfo.xml` at root or one folder deep.
- [ ] Each zip uploaded unchanged to Drive.
- [ ] Each zip shared **Anyone with the link**.
- [ ] Each link tested in an incognito window.
- [ ] Full `resourcekey` kept if present.
- [ ] No Google Drive folder links used.
- [ ] `google-drive-items.json` generated by `export-drive-items.ps1` or filled manually.
- [ ] It has correct relative local `file` paths and no `REPLACE_` URLs.
- [ ] Manifest built; hash/size are from the uploaded zips.
- [ ] `manifest.json` uploaded and shared.
- [ ] `manifest.json.sig` uploaded and shared if signed.
- [ ] `ManifestUrl` points at the manifest **file**, not a folder.
- [ ] `SignatureUrl` points at the sig file if `PublicKeyXml` is set.
- [ ] `AllowedHosts` empty or includes Drive hosts.
- [ ] `AllowInsecureHttp` is false.
- [ ] `AutoRestart` is true.
- [ ] `ModsRoot` empty unless intentionally overriding.
- [ ] Client zip rebuilt after config edits.
- [ ] Tested on a clean client with EAC off.
- [ ] Player note is in the zip.
