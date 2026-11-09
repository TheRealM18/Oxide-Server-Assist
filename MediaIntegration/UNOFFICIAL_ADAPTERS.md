# MediaIntegration — unofficial adapter safety policy

> **Status:** no unofficial adapters are implemented. They remain manual linking + custom webhook until they pass this policy.

Unofficial adapters are the highest-risk part of the platform expansion. This policy is deliberately strict.

## Default policy

1. **Default:** manual linking + custom webhooks. No third-party code, no automatic reward granting.
2. **Automatic rewards** from an unofficial source are allowed only after:
   - a full source and dependency audit,
   - isolation from the relay core,
   - contract tests with recorded fixtures,
   - a live smoke test,
   - explicit opt-in in config with a visible warning.
3. If any audit item fails and cannot be mitigated, the platform stays on manual linking + custom webhooks.
4. "Total safe" is not a claim we can make for an undocumented API. The claim is "reviewed, pinned, isolated, observable, and killable."

## Why this exists

Unofficial adapters can require:

- reverse-engineered WebSocket protocols,
- undocumented mobile API endpoints,
- third-party libraries with opaque dependency trees,
- credentials or cookies that grant broad account access,
- frequent breaking changes.

That is a supply-chain and account-security risk, not just a maintenance risk.

## Threat model

| Threat | Example | Control |
|---|---|---|
| Credential theft | Adapter reads another provider's token or the vault key | Separate process/container, per-provider token only, no vault access |
| Supply-chain compromise | A transitive NuGet package exfiltrates data | Pin, verify, scan, SBOM, no dynamic loading |
| Arbitrary code execution | Adapter calls `Process.Start`, `Assembly.Load`, reflection to arbitrary code | Code audit, banned APIs, sandbox |
| Data exfiltration | Adapter posts viewer data to an unknown host | Egress allowlist, TLS, network policy |
| Endpoint hijack | Adapter follows a redirect to an attacker host | No automatic redirects, host allowlist |
| Token overreach | Adapter asks for scopes it does not need | Minimum scopes, review |
| Silent breakage | Platform changes payload; adapter grants wrong reward | Schema validation, fixtures, fail closed |
| ToS violation | Scraping a private API | Legal/ToS review; opt-in warning |
| Update-channel attack | Adapter auto-updates from an unsigned source | Signed, pinned releases only |
| Log leakage | Adapter logs tokens/viewer data | Redaction, structured logging review |

## Mandatory audit checklist

### Provenance and build

- [ ] Source repository/commit is known and reviewable.
- [ ] License is compatible and documented.
- [ ] Build is reproducible from source.
- [ ] Release binaries are built by us, not downloaded prebuilt.
- [ ] No obfuscated code, packed binaries, or hidden native libraries.
- [ ] SBOM is generated.

### Dependencies

- [ ] Every direct and transitive dependency is pinned.
- [ ] `dotnet list package --vulnerable --include-transitive` is clean.
- [ ] NuGet package signatures verified where available.
- [ ] No dependency with install scripts, native payloads, or unexplained network access.
- [ ] No abandoned/unmaintained critical dependency.
- [ ] Dependency licenses reviewed.

### Code behavior

- [ ] No `Process.Start`, shell execution, or command construction.
- [ ] No `Assembly.Load`, dynamic code generation, or arbitrary reflection.
- [ ] No file writes outside a dedicated temp directory.
- [ ] No reads of `secret.key`, `tokens.enc`, environment secrets, or other providers' tokens.
- [ ] No network destinations beyond an explicit allowlist of provider API hosts.
- [ ] TLS certificate validation is enabled; no bypass callback.
- [ ] Redirects are disabled or restricted to the allowlist.
- [ ] No telemetry, analytics, or crash reporting to third parties.
- [ ] No logging of tokens, cookies, passwords, or full viewer payloads.
- [ ] Tokens are held in memory only for the provider that owns them.
- [ ] Event IDs are stable; malformed payloads fail closed.
- [ ] Rate limits, backoff, and timeouts are implemented.
- [ ] Resource usage is bounded (message size, queue size, reconnect storms).

### Data and privacy

- [ ] Only the minimum viewer data is retained.
- [ ] Retention period is documented.
- [ ] Unlink/delete path exists.
- [ ] No data is sent to non-provider third parties.
- [ ] Logs redact personal data where possible.

### Legal and platform

- [ ] Terms of service reviewed.
- [ ] The adapter does not require stream keys or passwords.
- [ ] The adapter does not bypass paywalls, DRM, or access controls.
- [ ] Brand/logo usage follows the platform's rules.
- [ ] The platform does not prohibit automated event reading in the target region.

### Operations

- [ ] Health endpoint and last-error reporting.
- [ ] Kill switch: disable provider without restarting the relay.
- [ ] Feature flag and explicit config warning.
- [ ] Contract tests with recorded fixtures for every event type.
- [ ] Live smoke test on a real test account.
- [ ] Documented failure mode: fail closed, do not grant rewards on ambiguity.

## Isolation requirements

An unofficial adapter **must not run in the same trust boundary as the relay core**.

Preferred order:

1. **Separate container** (best):
   - non-root,
   - read-only root filesystem,
   - no host mounts,
   - seccomp/AppArmor,
   - no host network,
   - egress allowlist only to the provider API,
   - CPU/memory/connection limits,
   - per-provider token only,
   - communicates with the relay through a local authenticated queue.
2. **Separate process/service**:
   - dedicated unprivileged user,
   - systemd hardening,
   - no access to the token vault or other providers' data,
   - egress firewall rules,
   - resource limits.
3. **Separate host/VM** for the highest-risk adapters.

The relay core must not load unofficial adapter assemblies in-process. If the adapter is in-process, automatic rewards are not allowed.

## Promotion criteria

An unofficial provider can grant automatic rewards only when all of these are true:

- [ ] Audit checklist complete; no unmitigated critical/high findings.
- [ ] Medium findings have documented mitigations.
- [ ] Isolation is in place and verified.
- [ ] Egress allowlist is enforced and tested.
- [ ] Contract tests and live smoke test pass.
- [ ] Stable event IDs and fail-closed validation.
- [ ] Rate limits/backoff verified.
- [ ] Kill switch verified.
- [ ] Legal/ToS review signed off.
- [ ] A maintainer is named for updates/breakage.

If any box is unchecked, the provider stays on:

```text
manual challenge link + custom webhook / staff approval
```

## Safe fallback: manual link + custom webhook

This path requires no third-party code:

1. Streamer runs `/media link tiktok <username>` (or the provider's manual link command).
2. The plugin/relay shows a one-time code.
3. The streamer puts the code in the channel title, chat, or profile for a short window.
4. The relay verifies by reading the official/public chat or a staff member approves.
5. Rewards are triggered by a **custom webhook** from the streamer's own tooling (Streamer.bot, IFTTT, Zapier, a custom script, or the platform's official webhook if one exists).
6. The custom webhook is signed, per-streamer, rate-limited, and mapped to normalized events.

This is less convenient but keeps the risk on the streamer's own tooling, not the relay.

## Candidate list

These are **manual/webhook first**. They are not approved for automatic rewards:

- TikTok Live
- Douyu
- Huya
- Kuaishou
- Nimo TV
- Bigo Live
- Nonolive
- YY
- Any other platform without a documented official event API

## Review record

For each unofficial adapter, keep a file:

```text
MediaIntegration/reviews/<provider>.md
```

It should contain:

- source repo/commit,
- dependency/SBOM summary,
- audit findings and resolutions,
- isolation design,
- event fixtures,
- live test evidence,
- ToS/legal notes,
- kill-switch test,
- approval signature/date.

No review file, no automatic rewards.
