# QLN Pack Server

> **Version:** not declared in the shipping README or configuration · **Type:** standalone Windows .NET Framework 4.8 HTTP service · **Source:** [QLNPackServer](https://github.com/TheRealM18/Oxide-Server-Assist/tree/main/QLNPackServer)

QLNPackServer serves the server's `Mods` folder as a client mod pack. It zips the
included mod folders itself, writes a `manifest.json`, signs it when a signing key
is configured, and serves the zips. No GitHub repository, release pipeline or
separate manifest file is needed.

```text
7 Days to Die server
  Mods\
    QuantumLogisticsNetwork\    <-- source folders
    SomeOtherClientMod\

QLNPackServer.exe
  - reads QLNPackServer.json
  - zips the included folders into cache\
  - serves /manifest.json, /manifest.json.sig, /mods/*.zip
```

The client side is [QLN Pack Bootstrap](../client/qln-pack-bootstrap.md). The
agent is plain HTTP on purpose; put Caddy, Nginx or another reverse proxy in
front when clients connect over the internet.

---

## Requirements

| Requirement | Detail |
|---|---|
| Host | Windows with .NET Framework 4.8 |
| Server | Read access to the 7 Days to Die server's `Mods` folder |
| Network | One inbound TCP port by default (`8765`), or a reverse proxy/tunnel |
| Client | `000_QLNPackBootstrap` installed once |
| Permissions | No administrator rights to read `Mods` or bind a high port |
| Signing | Optional `private-key.xml` matching the client `public-key.xml` |

The agent does not need to run inside the 7 Days to Die process and does not
share the game's UDP/Steam ports.

---

## Build and start

```powershell
cd QLNPackServer
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

The project is `net48` and has no external DLLs.

Start the agent:

```powershell
.\QLNPackServer.exe --config .\QLNPackServer.json
```

| Command | Meaning |
|---|---|
| `QLNPackServer.exe` | Load `QLNPackServer.json` next to the executable |
| `QLNPackServer.exe --config <path>` | Load a specific config file |
| `QLNPackServer.exe --help` | Print the usage line and exit |

If the config file is missing the process prints the expected path and exits with
code `2`. It creates `cacheDir` on startup and logs the mods folder, cache folder
and client manifest URL.

Open the port on Windows Firewall when clients connect directly:

```powershell
New-NetFirewallRule -DisplayName "QLNPackServer" -Direction Inbound -Protocol TCP -LocalPort 8765 -Action Allow
```

---

## Configuration

File: `QLNPackServer.json`, next to the executable unless `--config` points
elsewhere.

| Key | Default | Notes |
|---|---|---|
| `bindAddress` | `0.0.0.0` | Use `127.0.0.1` behind a local reverse proxy |
| `port` | `8765` | One inbound TCP port |
| `publicBaseUrl` | empty | Public URL used for manifest zip links; no trailing slash needed |
| `accessToken` | empty | Shared token required on manifest and zip requests when set |
| `modsPath` | `Mods` | Relative to the config file unless absolute |
| `cacheDir` | `cache` | Relative to the config file unless absolute |
| `include` | empty | Client-required folder names; empty means all folders except `exclude` |
| `exclude` | `000_QLNPackBootstrap`, `0_TFP_Harmony`, `TFP_CommandExtensions`, `Xample_MarkersMod` | Skipped by folder name |
| `packVersion` | `auto` | `auto` derives from mod hashes; a literal value is a nicer UI label |
| `gameVersion` | `3.4` | Sent in the manifest as a warning |
| `notes` | empty | Sent in the manifest |
| `remove` | empty | Old client folders to back up and remove |
| `signingKeyXml` | empty | Path to `private-key.xml`; omit to serve an unsigned manifest |
| `refreshSeconds` | `30` | Catalog refresh interval; `0` disables the timer |
| `logFile` | `QLNPackServer.log` | Relative to the config file unless absolute |

Paths are resolved from the config directory. Ports outside `1`–`65535` fall back
to `8765`; blank `modsPath`, `cacheDir` and `logFile` fall back to their
defaults. The server trims list values and drops empty entries.

!!! warning
    The agent must read the **server's** `Mods` folder, not a copy under
    `Oxide`. On the client, the bootstrap installs into the folder containing
    its own bootstrap folder by default.

### What to include

- Put only **client-required** mods in `include`. Server-only XML modlets do not
  need to be downloaded by clients.
- If `include` is empty, every directory under `modsPath` is served except the
  names in `exclude`.
- If you remove a folder from `include`, add its client folder to `remove`, or
  set the client's `RemoveObsolete` to `true`; otherwise the old copy stays on
  players' disks.

---

## HTTP endpoints

| Method | Path | Auth | Response |
|---|---|---|---|
| `GET`, `HEAD` | `/health` | No | `200 text/plain` with `ok` |
| `GET`, `HEAD` | `/manifest.json` | Yes | `200 application/json` |
| `GET`, `HEAD` | `/manifest.json.sig` | Yes | `200 text/plain`, base64 RSA signature; `404` when unsigned |
| `GET`, `HEAD` | `/mods/<zipname>` | Yes | `200` zip; `404` for an unknown zip |
| Other | any | — | `405 Method Not Allowed` |

Authentication uses the configured `accessToken`:

```text
?token=<accessToken>
X-Pack-Token: <accessToken>
```

Requests without a matching token receive `403`. The token is accepted from the
query string or the `X-Pack-Token` header. The bootstrap adds the token to both
the manifest request and every zip request.

`/health` is deliberately unauthenticated and returns the service liveness. The
`/api/status`, link and event endpoints belong to
[TwitchRelay](twitch-relay.md), not to this agent.

### Base URL selection

When `publicBaseUrl` is set, manifest URLs use it exactly; otherwise the agent
derives the scheme and host from `X-Forwarded-Proto` and `X-Forwarded-Host`,
then `Host`, and finally falls back to `http://127.0.0.1:<port>`. Set
`publicBaseUrl` explicitly for predictable URLs.

---

## Publishing pack files

The agent builds each zip from the folder under `modsPath`:

1. Read `ModInfo.xml` if present.
2. Accept both `<xml><Name ... /></xml>` and
   `<xml><ModInfo><Name ... /></ModInfo></xml>`.
3. If metadata is missing or invalid, use the folder name and version `0.0.0`.
4. Sanitize the install folder to letters, digits, `_` and `-`.
5. Fingerprint the folder from its relative file paths, sizes and UTC
   timestamps.
6. Zip the folder contents at the zip root, so `ModInfo.xml` sits directly inside
   the installed mod folder.
7. Cache the zip as `<SafeFolder>-<12hex>.zip` under `cacheDir`.

The manifest entry is:

| Field | Value |
|---|---|
| `name` | `ModInfo` name, or sanitized folder name |
| `displayName` | `DisplayName`, or folder name |
| `version` | `Version`, or `0.0.0` |
| `url` | `<baseUrl>/mods/<zipname>` |
| `sha256` | Hash of the built zip |
| `size` | Zip length in bytes |
| `target` | `client` |
| `installFolder` | Sanitized folder name |
| `required` | `true` |

The manifest also carries `schemaVersion: 1`, `packVersion`,
`gameVersion`, `generatedAt`, `notes` and `remove`.

- With `packVersion: "auto"`, the version is `auto-<12hex>` derived from each
  entry's name, version and SHA-256.
- A literal `packVersion` is sent unchanged.
- The catalog refreshes every `refreshSeconds`; a changed folder produces a new
  fingerprint, a new zip and a new manifest hash.
- Cached zips that are no longer referenced are deleted.
- The client downloads only changed mods; unchanged hashes are skipped.

!!! note
    The agent only serves zip names present in its current catalog. `/mods/...`
    is not an arbitrary file server, so a request cannot walk into `Mods` or the
    filesystem.

### Signing the manifest

Set `signingKeyXml` to the path of the `private-key.xml` generated by the client
release tools. The agent signs the exact manifest bytes with RSA/SHA-256 and
PKCS#1 v1.5, serves the base64 signature at `/manifest.json.sig`, and caches
signatures by manifest hash. Put the matching `public-key.xml` in the client's
`PublicKeyXml` and set `RequireSignature` to `true`.

If `signingKeyXml` is not set, `/manifest.json.sig` returns `404`. Clients with
`RequireSignature` will reject the unsigned manifest.

---

## Operation

```text
Program.Main
  -> load QLNPackServer.json
  -> Normalize paths and logging
  -> create cacheDir
  -> PackCatalog.Start()
       -> Refresh()
       -> timer every refreshSeconds
  -> HttpFileServer.Start()
       -> TcpListener on bindAddress:port
       -> print client manifest URL
  -> wait for Ctrl+C / process exit
```

Runtime log examples:

```text
INFO  Config: ...\QLNPackServer.json
INFO  Serving Quantum Logistics Network v0.0.1 as QuantumLogisticsNetwork-<hash>.zip
INFO  Catalog refreshed: 1 mod(s), version <12hex>
INFO  Listening on http://0.0.0.0:8765/
INFO  Client manifest URL: http://YOUR.SERVER.IP:8765/manifest.json
```

| Output | Location |
|---|---|
| Manifest JSON and signature | Generated in memory |
| Zips | `cacheDir` |
| Log | `logFile`, console, and file |
| Hash cache | In memory; rebuilt when zip size or write time changes |

The agent is a console application. Run it with NSSM, Task Scheduler ("At
startup", "Run whether user is logged on or not"), or your host's process
manager.

---

## Deployment

### Direct public port

Set the client's `AllowInsecureHttp` to `true` and `AccessToken` to the server's
`accessToken`. Prefer HTTPS plus signing for anything public.

### Reverse proxy

Keep the agent on loopback:

```json
"bindAddress": "127.0.0.1",
"port": 8765,
"publicBaseUrl": "https://packs.example.com"
```

Caddy:

```caddyfile
packs.example.com {
    reverse_proxy 127.0.0.1:8765
}
```

Caddy path prefix:

```caddyfile
example.com {
    handle_path /pack/* {
        reverse_proxy 127.0.0.1:8765
    }
}
```

Then use `https://example.com/pack/manifest.json` and set `publicBaseUrl` to
`https://example.com/pack`. `handle_path` strips `/pack` before forwarding.

### Tunnel

A Cloudflare Tunnel test needs no inbound port:

```powershell
cloudflared tunnel --url http://127.0.0.1:8765
```

Use the generated HTTPS URL for the manifest and set it as `publicBaseUrl`.
The process runs as the user that starts it. That account must be able to read
`modsPath`, write `cacheDir` and write `logFile`; no administrator rights are
needed for a high port.

---

## Security

| Concern | Control |
|---|---|
| Random scans | `accessToken` on manifest and zip requests |
| Tampered manifest | RSA signature plus per-zip SHA-256 on the client |
| Tampered zip | SHA-256 checked before extraction |
| Arbitrary file access | `/mods/...` resolves only known cached zip names |
| Exposure | Bind loopback behind TLS, or firewall the port to the game server |

The access token is shared with players, so it is not a secret from them. With no
token and no signature, anyone who can reach the port can download the pack and
could serve a tampered manifest. Use at least one, preferably both, and use HTTPS
for public servers.

The agent does not use Redis or another external store. Its state is the
filesystem: source folders under `Mods`, generated zips under `cacheDir`, and
the log file. Deleting a cached zip forces it to be rebuilt on the next refresh.

---

## Server Assist integration

QLNPackServer has no Server Assist API calls and does not register a panel. It is
the delivery path for the Server Assist **client** folder:

- Add `AA0_ServerAssistMod` (or `AAY_ServerAssistMod`) to `include`.
- The agent zips `ServerAssistMod.dll`, `ModInfo.xml`, `Config/` and any
  `UIAtlases/` in that folder.
- On the next refresh, the manifest hash changes and the bootstrap installs the
  new client build.
- The server-side `ServerAssist.cs` is **not** part of the client pack; it lives
  in `oxide/plugins/`.

The same applies to the art pack if it is shipped as its own folder. See
[Server Assist Mod](../client/server-assist-mod.md).

---

## Typical workflows

**Publish the current server mods.** Put client-required folders under the
server's `Mods`, add their names to `include`, start or reload the agent, then
give clients `http://YOUR.SERVER.IP:8765/manifest.json` and the matching
`AccessToken`/`AllowInsecureHttp` settings.

**Publish one mod update.** Replace the folder under `Mods`, wait for
`refreshSeconds` or restart, confirm the new zip and catalog version in the log,
and let clients check at the main menu.

**Force a rebuild.** Delete the specific zip in `cache\` and wait for the next
refresh, or restart the agent.

**Test the agent.**

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\test-server.ps1
```

The script builds a temporary Mods folder, starts the agent on a test port,
checks `/health`, fetches the manifest with a token, downloads a zip, verifies its
SHA-256 and layout, and confirms that a request without the token gets `403`.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Port already in use | Another process owns the port | Change `port` and update the client URL |
| No mods in manifest | Wrong `modsPath`, empty/mismatched `include` | Check the log; `ModInfo.xml` is optional |
| Downloads return 403 | Client and server tokens differ | Make `AccessToken` and `accessToken` identical |
| Signature mismatch | Manifest changed after signing or wrong public key | Re-check `signingKeyXml` and `PublicKeyXml` |
| Zip is stale | Refresh has not run | Wait for `refreshSeconds`, restart, or delete the cache zip |
| Manifest URLs use the wrong host | `publicBaseUrl` empty behind a proxy | Set `publicBaseUrl` explicitly |
| Config not found | Wrong working directory or `--config` path | Use an absolute path or run from the executable folder |

Log file: `QLNPackServer.log`, relative to the config unless absolute. More:
[Troubleshooting](../../reference/troubleshooting/index.md).
