# QLNPackServer

Serves the server's `Mods` folder as a client mod pack.

The client bootstrap reads `/manifest.json`, downloads each zip from `/mods/...`,
verifies SHA-256, and restarts the client when staging is complete. The server
agent zips the folders itself, so there is no separate release pipeline and no
GitHub repository needed.

```
7 Days to Die server
  Mods\
    QuantumLogisticsNetwork\    <-- source folders
    SomeOtherClientMod\

QLNPackServer.exe
  - reads QLNPackServer.json
  - zips the included folders into cache\
  - serves /manifest.json, /manifest.json.sig, /mods/*.zip
```

## Quick start

1. Put `QLNPackServer.exe` and `QLNPackServer.json` anywhere the server can read
   the Mods folder. Running it from the server root is convenient.
2. Edit `QLNPackServer.json`:

   ```json
   {
     "bindAddress": "0.0.0.0",
     "port": 8765,
     "publicBaseUrl": "",
     "accessToken": "change-me",
     "modsPath": "Mods",
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

   - `include` is the list from the server config. Only these folders are served.
     If it is empty, every folder under `modsPath` except `exclude` is served.
     When you remove a folder from `include`, add its client folder to `remove`
     (or set the client's `RemoveObsolete` to `true`) so players do not keep the
     old copy.
   - Put only **client-required** mods in `include`. Server-only XML modlets do
     not need to be downloaded by clients.
   - `modsPath` is relative to the config file unless absolute. Run the agent
     from the server root, or set an absolute path such as
     `"C:\\7DaysToDieServer\\Mods"`. The agent must read the **server's**
     `Mods` folder, not a copy under `Oxide`.
   - This is the server-side source folder. On the client, the bootstrap
     installs into the folder containing its own mod folder by default; that is
     the client's active `Mods` directory.
   - `accessToken` is optional. If set, clients must send the same token.
   - `signingKeyXml` is optional. Point it at `private-key.xml` from the client
     release tools to sign the manifest; put the matching `public-key.xml` in
     the client `packbootstrap.xml`.

3. Start it:

   ```powershell
   .\QLNPackServer.exe --config .\QLNPackServer.json
   ```

   The console prints the manifest URL to give clients. For example:

   `http://YOUR.SERVER.IP:8765/manifest.json`

4. Open the port in Windows Firewall (or your hosting provider's firewall):

   ```powershell
   New-NetFirewallRule -DisplayName "QLNPackServer" -Direction Inbound -Protocol TCP -LocalPort 8765 -Action Allow
   ```

5. Configure the client bootstrap:

   ```xml
   <ManifestUrl>http://YOUR.SERVER.IP:8765/manifest.json</ManifestUrl>
   <AccessToken>change-me</AccessToken>
   <AllowInsecureHttp>true</AllowInsecureHttp>
   <AllowedHosts></AllowedHosts>
   ```

   If you use a domain behind HTTPS, set `AllowInsecureHttp` to `false` and put
   the domain in `AllowedHosts`.

## How the pack is built

- Each included folder is served. `ModInfo.xml` is optional; when it is missing
  or invalid, the folder name becomes the mod name and the version is `0.0.0`.
- Both ModInfo layouts are supported: `<xml><Name ... /></xml>` and the older
  `<xml><ModInfo><Name ... /></ModInfo></xml>`.
- The agent zips the folder contents at the zip root, so the client sees
  `ModInfo.xml` directly inside the installed mod folder.
- Zips are cached in `cache\` and named with a fingerprint of the folder's file
  list, sizes, and timestamps.
- `refreshSeconds` controls how often the agent checks for changes. When a mod
  changes, the next refresh creates a new zip and the manifest hash changes.
- `packVersion: "auto"` derives a version from the included mod hashes. Set a
  literal version if you want a nicer label in the client UI.
- The client downloads only changed mods; unchanged hashes are skipped.
- `remove` is an optional list of old client folders to back up and remove.

## Does it need its own port?

By default, yes: **one inbound TCP port** (8765 in the sample). It cannot share
the game's UDP/Steam ports, and it cannot use the server's outgoing ephemeral
ports. The game protocol is not HTTP, and an outbound NAT port is not a stable
address that clients can dial.

You do **not** need a new inbound port if you already have any of these:

- **An existing web server on 80/443** — put Caddy, Nginx, or IIS in front and
  proxy a subdomain or path to `127.0.0.1:8765`.
- **An outbound tunnel** — Cloudflare Tunnel, Tailscale Funnel, ngrok, or a
  similar relay. The agent listens on `127.0.0.1`, makes an outbound connection
  to the relay, and clients use the relay's normal HTTPS 443 URL. No inbound port
  is opened.
- **Existing file hosting** — object storage, a CDN, or GitHub Releases. You do
  not need `QLNPackServer` at all; the client accepts any manifest URL. Use the
  GitHub mode described in the client README.
- **An existing open TCP port** — you can reuse it only with a reverse proxy.
  Two processes cannot bind the same port. For example, move the 7 Days to Die
  web dashboard to `127.0.0.1:8080` and put the proxy on the public port.

A reverse proxy is the usual way to “bum off” what is already open. For example,
a subdomain on an existing Caddy instance:

```caddyfile
packs.example.com {
    reverse_proxy 127.0.0.1:8765
}
```

Then use `https://packs.example.com/manifest.json` in the client and set
`publicBaseUrl` to `https://packs.example.com`.

A path on an existing domain also works:

```caddyfile
example.com {
    handle_path /pack/* {
        reverse_proxy 127.0.0.1:8765
    }
}
```

Then use `https://example.com/pack/manifest.json` and set `publicBaseUrl` to
`https://example.com/pack`. `handle_path` strips `/pack` before forwarding.

A quick Cloudflare Tunnel test (no inbound port):

```powershell
cloudflared tunnel --url http://127.0.0.1:8765
```

Use the generated `https://...trycloudflare.com` URL for the manifest and set it
as `publicBaseUrl` (or use a named tunnel with your own domain for production).

If you run the agent directly on a public port, the client's `AllowInsecureHttp`
must be `true` unless you put HTTPS in front of it. Prefer HTTPS plus the
`accessToken` and manifest signing for anything public.

## HTTPS / reverse proxy

The agent speaks plain HTTP on purpose so it does not need a certificate or
admin URL reservation. Put it behind Caddy, Nginx, or your host's proxy and set:

```json
"bindAddress": "127.0.0.1",
"port": 8765,
"publicBaseUrl": "https://packs.example.com"
```

The generated manifest URLs then use `https://packs.example.com/mods/...`, and
the client can use:

`https://packs.example.com/manifest.json`

If the proxy preserves `X-Forwarded-Proto` and `X-Forwarded-Host`, you can leave
`publicBaseUrl` empty; setting it explicitly is more predictable.

## Security

- `accessToken` stops random internet scans from downloading the pack. It is
  shared with players, so it is not a secret from them.
- For integrity, sign the manifest. HTTP can be modified in transit, but the
  client verifies the RSA signature and every zip's SHA-256.
- With no token and no signature, anyone who can reach the port can download the
  mods and the client has no protection against a tampered manifest. Use at
  least one, preferably both, and use HTTPS for public servers.
- The agent only serves files it created in its cache; `/mods/...` is a lookup
  against known zip names, not an arbitrary file server.
- It runs as the user that starts it. It does not need administrator rights to
  read the Mods folder or bind a high port.

## Test it

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\test-server.ps1
```

The script builds a temporary Mods folder, starts the agent on a test port,
checks `/health`, fetches the manifest with a token, downloads a zip, verifies
its SHA-256 and layout, and confirms that a request without the token gets 403.

## Running as a service

The agent is a console application. You can run it with NSSM, Task Scheduler
("At startup", "Run whether user is logged on or not"), or your host's process
manager. It does not need to be inside the 7 Days to Die process.

## Troubleshooting

**Port already in use** — change `port` in the JSON, then update the client URL.

**No mods in the manifest** — check `modsPath` and `include`. ModInfo.xml is
optional; folders without it are served using the folder name. The agent logs
every skipped folder.

**Downloads return 403** — the client's `AccessToken` does not match the server's
`accessToken`.

**Signature mismatch** — the manifest changed after signing, or the client has
the wrong public key. Re-check `signingKeyXml` and `PublicKeyXml`.

**A zip is stale** — wait for `refreshSeconds`, or restart the agent. Delete the
specific file in `cache\` to force a rebuild.

Log file: `QLNPackServer.log` (relative to the config unless absolute).
