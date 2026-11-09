# QLN Pack Sync — Sending and Setup Guide

This guide is for the server owner. It covers:

- what to send to players,
- what to run on the server,
- how to preconfigure the client,
- how to test it,
- how to update it later,
- what to do when something fails.

The player-facing short version is in `PLAYER-README.txt` next to this file.

---

## Platform

This handoff targets **Windows** clients and a **Windows** 7 Days to Die server.

- The client paths use `%APPDATA%\7DaysToDie\Mods`.
- `QLNPackUpdater.exe` is a Windows .NET Framework executable.
- `QLNPackServer.exe` is a Windows .NET Framework executable.

On Linux/Steam Deck, the paths and updater are different; use a Windows client or
adapt the updater. For a Linux server that cannot run the agent, use Mode B
(Google Drive/Dropbox/static hosting).

## 0. What you are sending

| Item | Send to players? | Send to server owner? |
|---|---|---|
| `000_QLNPackBootstrap.zip` | **Yes** | Yes, for testing |
| `QLNPackServer` folder (`QLNPackServer.exe` + `QLNPackServer.json`) | No | **Yes**, if using the server-hosted mode |
| Pack mod zips (QLN etc.) | No — the bootstrap downloads them | Put them in the server's `Mods` folder |
| `manifest.json` URL | Yes | Yes |
| `AccessToken` value | Yes, if the server uses one | Yes |
| `private-key.xml` | **Never** | Keep it on your own machine only |
| `public-key.xml` / public key XML | Only inside the client config | Yes, for reference |
| Source projects, `Data\`, `keys\`, `cache\` | No | No |

Players install one zip once. Everything after that is automatic.

Do **not** send the whole `QLNPackBootstrap` source folder to players. Send the built zip:

`C:\Users\robert\Desktop\7 Days 2 Die Mods\Oxide\QLNPackBootstrap\dist\000_QLNPackBootstrap.zip`

The zip contains:

```text
ModInfo.xml
QLNPackBootstrap.dll
QLNPackUpdater.exe
packbootstrap.xml
PLAYER-README.txt
```

The updater EXE inside the zip can make email providers or Discord filters unhappy. If a file host blocks it, use Dropbox/Drive/Discord or a direct download link.

---

## 1. Pick a delivery mode

### Mode A — Server-hosted agent (recommended if you can run it)

Use `QLNPackServer` on the server. It reads the server's `Mods` folder and serves
the pack. It is a small standalone Windows service, **not** an Oxide plugin; it
runs next to the game server and does not need Oxide.

Best when:

- the server is Windows,
- you can run a small background EXE,
- you can open one TCP port (or put it behind an existing HTTPS proxy/tunnel),
- you want no Google Drive/Dropbox quotas.

### Mode B — Google Drive / Dropbox / OneDrive / GitHub

Use already-uploaded zips and a `manifest.json`. No server agent and no extra port.

For the exact Google Drive-only procedure, see `GOOGLE-DRIVE.md` next to this
file. The summary below works for Drive, Dropbox, OneDrive, and GitHub.

Best when:

- the server host does not allow extra executables,
- you cannot open another port,
- the pack is small and only a few people download it,
- you already host files somewhere.

Drive/Dropbox have bandwidth/quota limits. For a public server, Mode A or a normal
static host/CDN is more reliable.

---

## 2. Build the client zip

Do this on your machine.

1. Open the source config:

   `C:\Users\robert\Desktop\7 Days 2 Die Mods\Oxide\QLNPackBootstrap\packbootstrap.xml`

2. Configure it for your setup (see section 3).

3. Build:

   ```powershell
   cd "C:\Users\robert\Desktop\7 Days 2 Die Mods\Oxide\QLNPackBootstrap"
   powershell -ExecutionPolicy Bypass -File .\build.ps1
   ```

4. Send:

   `...\QLNPackBootstrap\dist\000_QLNPackBootstrap.zip`

Important: `build.ps1` deletes and recreates `dist`. Edit the **source**
`packbootstrap.xml`, not the copy in `dist`, if you are going to rebuild.
Editing the copy in `dist` is fine for a one-off zip if you do not rebuild.

---

## 3. Configure the client before sending

Edit `packbootstrap.xml`:

### Mode A — server-hosted

```xml
<ManifestUrl>http://YOUR.SERVER.IP:8765/manifest.json</ManifestUrl>
<AccessToken>the-same-token-as-the-server</AccessToken>
<AllowedHosts></AllowedHosts>
<AllowInsecureHttp>true</AllowInsecureHttp>
<AutoRestart>true</AutoRestart>
<ModsRoot></ModsRoot>
<GameVersion>3.4</GameVersion>
```

- `AccessToken` must match `QLNPackServer.json`.
- `AllowInsecureHttp` must be `true` for plain HTTP.
- If you put the agent behind HTTPS, use the HTTPS URL, set
  `AllowInsecureHttp` to `false`, and set `AllowedHosts` to your domain.
- `AllowedHosts` empty means any host. For tighter security, set it to your
  server host, for example `203.0.113.10` or `packs.example.com`.
- `ModsRoot` empty means “the folder containing `000_QLNPackBootstrap`”, which is
  the active Mods folder. Leave it empty unless you need to force a path.

### Mode B — Google Drive/Dropbox/GitHub

```xml
<ManifestUrl>https://www.dropbox.com/scl/fi/.../manifest.json?dl=0</ManifestUrl>
<AccessToken></AccessToken>
<AllowedHosts></AllowedHosts>
<AllowInsecureHttp>false</AllowInsecureHttp>
<AutoRestart>true</AutoRestart>
<ModsRoot></ModsRoot>
<GameVersion>3.4</GameVersion>
```

- `AccessToken` is only for `QLNPackServer`; leave empty for cloud mode.
- The client resolves Google Drive/Dropbox/OneDrive share links.
- If the manifest is signed, set `SignatureUrl` explicitly to the `.sig` share
  link and `RequireSignature` to `true`. Automatic `.sig` guessing does not work
  for share pages.

### Optional signing

On your machine:

```powershell
cd "C:\Users\robert\Desktop\7 Days 2 Die Mods\Oxide\QLNPackBootstrap"
.\release\new-pack-key.ps1 -OutputDir .\keys
```

- Keep `keys\private-key.xml` only on the machine that signs manifests.
- Put the contents of `keys\public-key.xml` into `packbootstrap.xml` inside
  `PublicKeyXml`.
- Set `RequireSignature` to `true`.
- Never send `private-key.xml` to players.

If you do not sign, the client shows an unsigned-manifest warning. That is okay
for a friends server, but signing is better for a public server.

---

## 4. Mode A — set up the server agent

### 4.1 Copy it to the server

Copy this folder to the server:

`...\Oxide\QLNPackServer\dist\QLNPackServer\`

It contains:

```text
QLNPackServer.exe
QLNPackServer.json
```

The server must be Windows with .NET Framework 4.8. On a Linux server, either run
it with Mono if your host supports that, or use Mode B/static hosting.

### 4.2 Edit `QLNPackServer.json`

Example:

```json
{
  "bindAddress": "0.0.0.0",
  "port": 8765,
  "publicBaseUrl": "",
  "accessToken": "the-same-token-as-the-client",
  "modsPath": "C:\\7DaysToDieServer\\Mods",
  "cacheDir": "cache",
  "include": [
    "QuantumLogisticsNetwork"
  ],
  "exclude": [
    "000_QLNPackBootstrap",
    "0_TFP_Harmony",
    "TFP_CommandExtensions",
    "Xample_MarkersMod"
  ],
  "packVersion": "auto",
  "gameVersion": "3.4",
  "notes": "QLN server pack",
  "remove": [],
  "signingKeyXml": "",
  "refreshSeconds": 30,
  "logFile": "QLNPackServer.log"
}
```

Key points:

- `modsPath` must point at the **server's** `Mods` folder. Use an absolute path
  if the agent is not in the server root.
- `include` is the client pack list. Only these folders are served.
- Put only client-required mods in `include`. Server-only XML modlets do not need
  to be downloaded.
- `exclude` should keep the TFP built-ins out of the client pack.
- `accessToken` should be a long random string if the port is public.
- `signingKeyXml` can point at your `private-key.xml` if you want signed
  manifests. Keep that file on the server only.
- `publicBaseUrl` is required for a reverse proxy/tunnel if the proxy does not
  preserve `X-Forwarded-Proto`/`X-Forwarded-Host`. Set it to the public HTTPS
  base, for example `https://packs.example.com`.

Generate a token if you need one:

```powershell
[guid]::NewGuid().ToString("N") + [guid]::NewGuid().ToString("N")
```

Players will know the token; it is not a password. It just stops random scans.

### 4.3 Make sure the server has the mods

The server itself must have the same client-required mods in its `Mods` folder,
plus any server-only mods. The agent only serves files; it does not install mods
on the server.

Also make sure the server is running with EAC off for code/asset mods such as
QLN.

### 4.4 Start the agent

```powershell
.\QLNPackServer.exe --config .\QLNPackServer.json
```

You should see:

```text
Listening on http://0.0.0.0:8765/
Client manifest URL: http://YOUR.SERVER.IP:8765/manifest.json
```

Keep the console open, or run it as a service with NSSM/Task Scheduler.

### 4.5 Open the port

On the server:

```powershell
New-NetFirewallRule -DisplayName "QLNPackServer" -Direction Inbound -Protocol TCP -LocalPort 8765 -Action Allow
```

Also open TCP 8765 in your hosting provider's firewall/router if applicable.

### 4.6 If you cannot open a port

Use an existing HTTPS reverse proxy or an outbound tunnel:

- Caddy/Nginx on an existing 80/443:
  - proxy `packs.example.com` or `/pack/*` to `127.0.0.1:8765`,
  - set `publicBaseUrl` to the public HTTPS base,
  - set the client to the HTTPS manifest URL,
  - set `AllowInsecureHttp` to `false`.
- Cloudflare Tunnel, Tailscale Funnel, or ngrok:
  - keep the agent on `127.0.0.1:8765`,
  - expose it via the tunnel,
  - set `publicBaseUrl` to the tunnel's HTTPS URL,
  - client uses the tunnel URL.

Do not try to share the game's UDP/Steam ports. They are not HTTP and cannot be
multiplexed.

### 4.7 Test the agent

From a machine that is not the server:

```text
http://YOUR.SERVER.IP:8765/health
```

Should return `ok`.

Then:

```text
http://YOUR.SERVER.IP:8765/manifest.json?token=YOUR_TOKEN
```

Should return JSON with a `mods` array. If you see HTML, a login page, or 403,
fix that before sending the client zip.

---

## 5. Mode B — Google Drive / Dropbox setup

### 5.1 Upload each mod as a zip

- One mod per zip.
- The zip should have `ModInfo.xml` at its root, or inside a single top-level
  folder.
- Share the file as **Anyone with the link**.
- Do not use folder links. Use a file link for each zip.

### 5.2 Build the manifest

Create `url-items.json`:

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

The `file` is the exact local zip you uploaded. It is relative to the items file
(or pass `-ZipDir`), not an absolute filesystem path. The script computes its
SHA-256 and size so the client can verify the remote download. The remote `url`
is still an absolute provider share link.

To generate this file from the pack folder automatically:

```powershell
.\release\export-drive-items.ps1 `
  -PackDir .\pack `
  -ItemsFile .\release\google-drive-items.json
```

It creates the zips, writes relative `file` paths, and preserves existing Drive
URLs on reruns. Use `-UrlMap .\drive-urls.json` to fill real URLs. If you only
have a folder of zips, use `-FromZips -ZipDir .\path\to\zips`; it reads
`ModInfo.xml` from inside each zip.

Run:

```powershell
cd "C:\Users\robert\Desktop\7 Days 2 Die Mods\Oxide\QLNPackBootstrap"

.\release\build-manifest-from-urls.ps1 `
  -ItemsFile .\url-items.json `
  -OutputDir .\release-out `
  -PackVersion 1.0.0 `
  -GameVersion 3.4 `
  -PrivateKeyXml .\keys\private-key.xml
```

Output:

```text
manifest.json
manifest.json.sig   (if signed)
```

### 5.3 Host the manifest

Upload `manifest.json` (and `manifest.json.sig` if signed) to anywhere public:

- a normal web host,
- GitHub Releases,
- Google Drive,
- Dropbox.

If the manifest is on Drive/Dropbox, the client resolves the share link. If it is
signed, set `SignatureUrl` explicitly to the `.sig` share link in the client
config.

### 5.4 Limits of cloud mode

- Google Drive/Dropbox folder links do not work; use per-file zips.
- Files must be public; login/permission pages cannot be bypassed.
- Google Drive quota pages cannot be bypassed.
- Google Drive may show a virus-scan/confirm page; the client handles the common
  form, but a direct link is more reliable.
- OneDrive short links may redirect to a preview; use a direct link if the client
  reports an HTML page.
- MediaFire/Mega and other JavaScript/decryption hosts are not supported.
- These services have bandwidth limits. For many players, use Mode A or a static
  host.

---

## 6. Send it to the player

Send:

1. `000_QLNPackBootstrap.zip` (it already includes `PLAYER-README.txt`)
2. The manifest URL and token if they are not already inside the zip config

Suggested message:

```text
QLN server mod pack setup

1. Close 7 Days to Die.
2. Press Win+R, paste %APPDATA%\7DaysToDie\Mods, press Enter.
3. Right-click 000_QLNPackBootstrap.zip -> Extract All -> destination
   %APPDATA%\7DaysToDie\Mods -> Extract. Let Windows create the
   000_QLNPackBootstrap folder, so you see:
   ...\Mods\000_QLNPackBootstrap\ModInfo.xml
   (If you get a folder inside a folder, move the inner folder up one level.
    Do not dump the files directly into Mods.)
4. Start 7 Days to Die WITHOUT EAC. The reliable way is to launch 7DaysToDie.exe
   from the game folder (Steam must be running), or use your no-EAC shortcut.
5. At the main menu, review the pack and click Download & Install.
6. It will restart automatically after downloading and unpacking. Let it finish.

If the window does not appear, EAC is probably on, or the folder is nested wrong.
Do not send me your whole Mods folder; if you need help, send:
%APPDATA%\7DaysToDie\Mods\000_QLNPackBootstrap\Data\logs\bootstrap.log
```

### Why the zip layout matters

Correct:

```text
%APPDATA%\7DaysToDie\Mods\000_QLNPackBootstrap\ModInfo.xml
%APPDATA%\7DaysToDie\Mods\000_QLNPackBootstrap\QLNPackBootstrap.dll
%APPDATA%\7DaysToDie\Mods\000_QLNPackBootstrap\QLNPackUpdater.exe
%APPDATA%\7DaysToDie\Mods\000_QLNPackBootstrap\packbootstrap.xml
```

Wrong:

```text
%APPDATA%\7DaysToDie\Mods\000_QLNPackBootstrap\000_QLNPackBootstrap\ModInfo.xml
```

If Windows Explorer's “Extract All” creates the nested folder, move the inner
folder up.

### Where the downloaded mods go

By default, they go next to the bootstrap folder:

```text
%APPDATA%\7DaysToDie\Mods\QuantumLogisticsNetwork
%APPDATA%\7DaysToDie\Mods\SomeOtherMod
```

That is the active Mods directory the bootstrap was loaded from. If the player
uses a custom game copy/profile, install the bootstrap in that copy's `Mods`
folder and it follows automatically. `ModsRoot` can force a different path.

Do not put the downloaded mods inside `000_QLNPackBootstrap`; 7 Days to Die only
loads immediate subfolders of `Mods`.

### Auto-restart

When staging finishes, the client shows “All mods downloaded” and restarts after
a five-second countdown. The updater then moves the extracted folders into
`Mods` and relaunches the game.

The countdown has a **Cancel Auto-Restart** button. If the player is inside a
world when it ends, the client waits until they return to the main menu.

---

## 7. What the player sees

1. Main menu → **QLN Mod Pack** window.
2. The list of mods, versions, sizes, and source host.
3. **Download & Install**.
4. Download, SHA-256 verification, extraction.
5. “All mods downloaded” + countdown.
6. Client closes; updater backs up old folders, moves new ones in, relaunches.
7. Next launch: the bootstrap checks again; if up to date, it briefly says so or
   stays quiet.

If the player clicks **Later**, the pending update stays; the next main-menu check
or next launch offers it again.

---

## 8. Updating the pack later

### Mode A — server agent

1. Replace or add the mod folder in the server's `Mods` folder.
2. Wait for `refreshSeconds`, or restart the agent.
3. Clients see the new manifest at their next main-menu check.
4. They click Download & Install; the client auto-restarts.

If you remove a mod from `include`, also add its client folder to `remove`, or set
the client's `RemoveObsolete` to `true`. Otherwise the old folder stays in the
player's Mods directory.

### Mode B — Drive/Dropbox

1. Upload the new zip(s).
2. Update `url-items.json` (new URL and local file path).
3. Re-run `build-manifest-from-urls.ps1`.
4. Replace `manifest.json` (and `.sig`) at the same public URL.
5. Clients detect the new hashes.

Bump the mod version in `ModInfo.xml` for a clean label; the SHA-256 change is
what actually triggers the update.

---

## 9. Pre-flight checklist

Before you send the client zip:

- [ ] The zip contains `ModInfo.xml`, `QLNPackBootstrap.dll`,
      `QLNPackUpdater.exe`, `packbootstrap.xml` at its root.
- [ ] `packbootstrap.xml` has the correct `ManifestUrl`.
- [ ] If using Mode A, `AccessToken` matches the server.
- [ ] If using HTTPS, `AllowInsecureHttp` is `false` and `AllowedHosts` is set.
- [ ] If using HTTP, `AllowInsecureHttp` is `true`.
- [ ] `AutoRestart` is `true` if you want automatic restarts.
- [ ] `ModsRoot` is empty unless you need a forced path.
- [ ] The manifest URL works from outside the server.
- [ ] The manifest returns JSON, not an HTML login/share page.
- [ ] One mod zip downloads and is a real zip (`PK` header).
- [ ] The manifest hashes match the downloaded zips.
- [ ] The server has the same mods in its `Mods` folder.
- [ ] EAC is off on the server and client.
- [ ] You tested on a clean client or a second Windows profile.

---

## 10. Troubleshooting

### The window never appears

- EAC is on. The bootstrap is marked `SkipWithAntiCheat`, so the game skips it.
  Launch `7DaysToDie.exe` directly or use the no-EAC launch option.
- The folder is nested wrong (`000_QLNPackBootstrap\000_QLNPackBootstrap`).
- `ModInfo.xml` is missing.
- The bootstrap is in the wrong `Mods` folder (use `%APPDATA%\7DaysToDie\Mods` or
  the game copy's `Mods`).
- Check `Data\logs\bootstrap.log`.

### “Configuration needed”

- `ManifestUrl` is empty/invalid.
- `RequireSignature`/`PublicKeyXml` is set without a `SignatureUrl`.
- The bootstrap is not inside a folder named `Mods`; install it under
  `...\Mods\000_QLNPackBootstrap` or set `ModsRoot`.

### HTTP 403

The client `AccessToken` does not match `QLNPackServer.json`'s `accessToken`.
The token is appended to the manifest and every zip URL.

### “The URL returned an HTML page instead of a file”

- The provider link is a folder/preview/login page, not a direct zip.
- Share the file as “Anyone with the link”.
- Use a per-file zip, not a folder link.
- Google Drive quota/permission pages cannot be bypassed.
- Try the direct download URL instead.

### The updater waits forever

It waits for the game process to exit. Close 7 Days to Die normally. If it
crashed, kill it in Task Manager.

### The updater fails

See `Data\logs\updater.log` and `Data\updater-failed.txt`. The bootstrap will not
auto-restart while the failure marker exists. Fix the cause and click
**Restart & Update**; the updater clears the marker when it starts.

### The game starts with old mods

The update is staged but not applied. With `AutoRestart` on, the client should
restart itself. If not, click **Restart & Update**, or run
`QLNPackUpdater.exe` manually with the arguments from `bootstrap.log`.

### Signature mismatch

- The manifest changed after signing, or the client has the wrong public key.
- For Mode A, check `signingKeyXml` on the server.
- For Mode B, re-sign the manifest and upload both `manifest.json` and
  `manifest.json.sig`; set `SignatureUrl` explicitly.

### Player has old copies in the game folder

If they previously installed mods manually in:

`...\7 Days To Die\Mods`

remove them. The user folder `%APPDATA%\7DaysToDie\Mods` loads first; a duplicate
mod name in the game folder is ignored. The bootstrap installs to the active
folder where the bootstrap lives.

### Logs to request from a player

```text
%APPDATA%\7DaysToDie\Mods\000_QLNPackBootstrap\Data\logs\bootstrap.log
%APPDATA%\7DaysToDie\Mods\000_QLNPackBootstrap\Data\logs\updater.log
%APPDATA%\7DaysToDie\Mods\000_QLNPackBootstrap\Data\updater-failed.txt
```

Do not ask for the whole Mods folder.

---

## 11. Security notes

- Never send `private-key.xml`.
- The access token is shared with players; it is not encryption. Use HTTPS for
  public servers.
- Sign manifests if the server is public. The client verifies SHA-256 and,
  optionally, the RSA signature.
- The bootstrap only writes folders listed in the manifest and backs up replaced
  folders under `Data\backup`.
- The updater is an unsigned EXE. Windows SmartScreen/antivirus may warn. Code
  sign it if you distribute widely.
- The server agent does not need administrator rights to read Mods or bind a high
  port. The firewall rule does require admin.

---

## 12. Quick command reference

```powershell
# Build the client bootstrap
cd "C:\Users\robert\Desktop\7 Days 2 Die Mods\Oxide\QLNPackBootstrap"
powershell -ExecutionPolicy Bypass -File .\build.ps1

# Build the server agent
cd "C:\Users\robert\Desktop\7 Days 2 Die Mods\Oxide\QLNPackServer"
powershell -ExecutionPolicy Bypass -File .\build.ps1

# Run the server agent
.\dist\QLNPackServer\QLNPackServer.exe --config .\dist\QLNPackServer\QLNPackServer.json

# Test the server agent
.\tests\test-server.ps1

# Generate a signing key
cd "C:\Users\robert\Desktop\7 Days 2 Die Mods\Oxide\QLNPackBootstrap"
.\release\new-pack-key.ps1 -OutputDir .\keys

# Build a cloud manifest
.\release\build-manifest-from-urls.ps1 `
  -ItemsFile .\url-items.json `
  -OutputDir .\release-out `
  -PackVersion 1.0.0 `
  -GameVersion 3.4 `
  -PrivateKeyXml .\keys\private-key.xml
```
