# MediaIntegration — hardened relay web admin

> **Status:** design only. Phase 0 does not expose an admin listener or web UI. The requirements below must be met before one is enabled.

The web admin is allowed only if the relay is deployed as a locked-down service. This document defines that deployment.

## 1. Two listeners, two trust levels

```text
Game server API:  http://127.0.0.1:8090     (HMAC server keys)
Admin UI/API:     http://127.0.0.1:8091     (session auth + TOTP + CSRF)
```

- The game API listener is the only listener the game server can reach.
- The admin listener binds to loopback or a private tailnet address. It is **never** bound to `0.0.0.0` on a public interface.
- The admin UI is served by the same `MediaRelay` process or a separate admin process, but it has its own Kestrel listener, auth middleware, and route prefix (`/admin`).
- If remote admin access is required, use one of:
  1. SSH tunnel (`ssh -L 8091:127.0.0.1:8091 relay-host`),
  2. Tailscale/WireGuard address,
  3. Caddy/nginx on a **separate admin hostname** with mTLS client certificates and an IP allowlist.

Plain HTTP to a public admin port is not supported. Do not put the admin behind the same public listener as the game API.

## 2. Reverse proxy example

Public game API:

```text
relay.example.com {
    reverse_proxy 127.0.0.1:8090
}
```

Admin, only from the tailnet or with mTLS:

```text
admin.example.com {
    tls {
        client_auth {
            mode require_and_verify
            trust_pool file /etc/caddy/admin-clients.pem
        }
    }
    @not_admin_net not remote_ip 100.64.0.0/10 192.168.0.0/16
    respond @not_admin_net 403

    reverse_proxy 127.0.0.1:8091
}
```

- Separate hostnames.
- TLS 1.2+ only.
- HSTS after verification.
- Request body limits and timeouts.
- Admin path is never linked from the public site.

## 3. Authentication

- Local admin accounts only. No social login, no provider OAuth for admin login.
- Passwords hashed with Argon2id (or PBKDF2-HMAC-SHA256 with a high work factor if Argon2 is unavailable).
- TOTP second factor required for every admin account.
- Session cookie:
  - `__Host-mediaadmin`
  - `Secure`, `HttpOnly`, `SameSite=Strict`, `Path=/`
  - short idle timeout (15 minutes) and absolute timeout (8 hours)
  - rotate session ID on login and privilege change
- CSRF token on every state-changing request.
- Rate limit login attempts; exponential backoff; lockout after repeated failures.
- Re-authentication required for:
  - revealing/changing provider secrets,
  - rotating API keys,
  - disabling a provider,
  - deleting a link,
  - changing currency rates,
  - creating/removing admin accounts.
- No secrets in URLs, query strings, logs, or browser storage.

## 4. Authorization

Roles:

```text
viewer   read-only provider health, links, event counts
operator test events, reload config, manage links
admin    secrets, provider enable/disable, currency rates, admin accounts
```

- Principle of least privilege.
- Every mutation writes an audit record with actor, IP, timestamp, action, before/after summary.
- Destructive actions require an explicit confirmation string and re-auth.

## 5. Secret handling

- Provider client IDs/secrets and tokens are **write-only** in the UI.
- After saving, the UI shows `configured` or a masked suffix, never the value.
- The relay never returns decrypted tokens to the browser.
- Revealing a secret requires re-auth and writes an audit record.
- Secrets are encrypted at rest with the same vault/key discipline as the Twitch tokens.
- Rotation is a first-class action: generate new key, show once, invalidate old key.

## 6. Input and output safety

- Validate every request body against a schema; reject unknown fields.
- Provider IDs are allowlisted; no dynamic type loading from the web.
- No file uploads.
- No arbitrary command execution from the admin UI.
- No server-side template injection: use a fixed template engine or static HTML + JSON.
- Output encoding/escaping everywhere.
- CSP: no inline scripts, no third-party CDNs, self-hosted assets only.
- `X-Frame-Options: DENY` / `frame-ancestors 'none'`.
- `Referrer-Policy: no-referrer`.
- `X-Content-Type-Options: nosniff`.
- `Permissions-Policy` minimal.
- Request size limits, JSON depth limits, timeouts.

## 7. Network lockdown

- Default-deny inbound firewall.
- Allow 443 from the game server IP(s) to the public game API.
- Allow the admin listener only from loopback/tailnet/admin IPs.
- No direct external access to 8090 or 8091.
- Outbound egress allowlist to provider API hostnames only. If a provider adds a new host, update the allowlist intentionally.
- DNS resolution through the host resolver; monitor for unexpected destinations.
- Optional: fail2ban/CrowdSec for repeated admin login failures.
- Optional: separate network namespace/container for the relay with only the required egress.

## 8. systemd hardening

```ini
[Service]
User=mediarelay
Group=mediarelay
WorkingDirectory=/opt/mediarelay
ExecStart=/opt/mediarelay/MediaRelay
Restart=always
RestartSec=5
Environment=MEDIA_RELAY_CONFIG=/opt/mediarelay/relay.json
NoNewPrivileges=true
PrivateTmp=true
PrivateDevices=true
ProtectSystem=strict
ProtectHome=true
ProtectKernelTunables=true
ProtectKernelModules=true
ProtectControlGroups=true
ProtectClock=true
ProtectHostname=true
RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX
RestrictNamespaces=true
RestrictRealtime=true
RestrictSUIDSGID=true
LockPersonality=true
MemoryDenyWriteExecute=true
SystemCallFilter=@system-service
SystemCallErrorNumber=EPERM
CapabilityBoundingSet=
UMask=0077
ReadWritePaths=/opt/mediarelay/data
LimitNOFILE=65536
TasksMax=512
```

- Run as a dedicated unprivileged user.
- `relay.json` and `data/` owned by that user, mode `600`/`700`.
- No shell, no compiler, no package manager on the relay host.
- Keep the OS and .NET runtime patched.
- Use a read-only root filesystem where possible; mount only `data/` writable.

## 9. Admin UI features (and limits)

Allowed:

- Provider health: connected, last event, last error, subscription count.
- Links: list, inspect, unlink.
- Event queue: counts, recent normalized events, ack state.
- Test event per provider.
- Enable/disable provider.
- Currency conversion rates and effective date.
- Tier platform restrictions.
- Reload config.
- Rotate server API keys.
- Audit log viewer.

Not allowed:

- Editing provider code from the web.
- Uploading plugins/assemblies.
- Running arbitrary shell commands.
- Viewing decrypted tokens.
- Disabling audit logging.
- Changing the admin listener bind address from the web.

## 10. Audit and monitoring

- Append-only audit log with actor, IP, timestamp, action, result.
- Optional hash chain for tamper evidence.
- Alert on:
  - repeated login failures,
  - secret reveal/rotation,
  - provider disable/enable,
  - key rotation,
  - unexpected egress attempts,
  - event queue backlog.
- Retain audit logs according to the server's policy.
- Back up `relay.json`, `data/secret.key`, and `data/tokens.enc` securely; a backup without the key is useless, and a backup with the key is sensitive.

## 11. Threat model

| Threat | Control |
|---|---|
| Credential stuffing | Argon2id, TOTP, rate limit, lockout |
| Session theft | Secure/HttpOnly/SameSite cookies, short TTL, rotation |
| CSRF | Per-request CSRF token, SameSite=Strict |
| XSS | Escaping, CSP, no inline scripts, no CDNs |
| SSRF | No user-supplied URLs; provider host allowlist |
| Command injection | No shell execution from admin UI |
| Token exfiltration | Write-only secrets, encrypted vault, re-auth, audit |
| Rogue unofficial adapter | Separate process/container, egress allowlist, no host FS, per-provider tokens |
| Admin UI exposed publicly | Loopback/tailnet bind, separate hostname, mTLS/IP allowlist |
| DoS | Rate limits, body limits, timeouts, connection limits |
| Insider abuse | Roles, audit, re-auth, least privilege |
| Backup leak | Encrypt backups, protect key material, restrict access |

## 12. Super-lockdown checklist

- [ ] Admin listener bound to `127.0.0.1` or a tailnet address only.
- [ ] Public game API and admin UI on separate hostnames/listeners.
- [ ] TLS 1.2+; mTLS for admin if remote.
- [ ] Admin IP allowlist / tailnet ACL.
- [ ] Argon2id + TOTP for every admin.
- [ ] Secure/HttpOnly/SameSite session cookies, short TTL.
- [ ] CSRF tokens on mutations.
- [ ] CSP, HSTS, frame deny, no inline JS/CDN.
- [ ] Secrets write-only, encrypted at rest, re-auth to reveal/rotate.
- [ ] Audit log append-only and monitored.
- [ ] systemd hardening applied.
- [ ] Firewall default deny; only 443 public, 8091 private.
- [ ] Egress allowlist to provider APIs.
- [ ] Dedicated unprivileged user; data dir `700`; config `600`.
- [ ] No shell/compiler/package manager on the relay host.
- [ ] Backups encrypted and tested.
- [ ] Last-pass review of the admin routes and middleware before exposure.

If any item is missing, keep the admin UI on loopback/SSH tunnel only.
