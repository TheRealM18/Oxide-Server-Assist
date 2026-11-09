# MediaIntegration — migration plan

> **Status:** Phase 0 is implemented in `MediaIntegration.cs` 0.3.0 and `MediaRelay`. Twitch and the custom webhook work; YouTube/Kick are next. The old `TwitchIntegration` files are left in place for rollback.

This is the TwitchIntegration → MediaIntegration rename and compatibility plan. Phase 0 does the rename and core refactor; Twitch must stay at parity.

## Rename map

| Old | New |
|---|---|
| `TwitchIntegration/` | `MediaIntegration/` |
| `oxide/plugins/TwitchIntegration.cs` | `oxide/plugins/MediaIntegration.cs` |
| `[Info("TwitchIntegration", ...)]` | `[Info("MediaIntegration", ...)]` |
| `oxide/config/TwitchIntegration.json` | `oxide/config/MediaIntegration.json` |
| `oxide/data/TwitchIntegration/players.json` | `oxide/data/MediaIntegration/players.json` |
| `TwitchRelay` service/binary | `MediaRelay` service/binary |
| `/twitch` | `/media` (keep `/twitch` alias for one release) |
| `twitch.use` | `media.use` |
| `twitch.streamer` | `media.streamer` |
| `twitch.admin` | `media.admin` |
| `twitch.tier.<id>` | `media.tier.<id>` |
| `twitch` group | `media` group (legacy group retained for migration) |
| Server Assist panel id `twitch` | `media` |
| Panel label `Twitch` | `Media` |
| `TwitchEvent` | `MediaEvent` |
| `TwitchEvent.Type` | `MediaEvent.Type` |
| `BroadcasterUserId` / `ViewerUserId` | `BroadcasterId` / `ViewerId` (wire-compatible with `MEDIA_EVENT.schema.json`) |

The `sa_stream_*` sprite names stay; they are platform-neutral by design.

## Compatibility rules

1. **Do not break an existing Twitch link during the rename.**
2. Keep `/twitch` as an alias for at least one release.
3. Keep legacy `twitch.*` permissions registered while granting/mirroring `media.*`.
4. Accept old Twitch JSON events from an old relay build with `platform` defaulting to `twitch`.
5. Keep the relay API paths (`/api/...`) stable; add `platform` fields rather than renaming endpoints.
6. Existing loadouts, cooldowns, pending effects, and tier selections must survive.
7. The old and new plugins must not both register a Server Assist panel for the same player at the same time.

## Plugin data migration

On first load of `MediaIntegration`:

1. If `oxide/data/MediaIntegration/players.json` does not exist, try `oxide/data/TwitchIntegration/players.json`.
2. For each `PlayerProfile`:
   - Move `TwitchUserId`, `TwitchLogin`, `TwitchDisplayName` into `Links["twitch"]`.
   - Keep `SteamId`, `PlayerName`, `OptedIn`, `Paused`, per-category toggles, loadouts, cooldowns, pending effects.
   - Add empty `Links` entries for other platforms as needed.
3. Save the new file. Do not delete the old file automatically; archive or leave it as a rollback copy.
4. Log a summary: profiles migrated, Twitch links migrated, errors.

Suggested JSON:

```json
{
  "Players": {
    "7656119...": {
      "SteamId": "7656119...",
      "PlayerName": "Streamer",
      "OptedIn": true,
      "Paused": false,
      "AllowBuffs": true,
      "AllowDebuffs": true,
      "AllowRewards": true,
      "Links": {
        "twitch": {
          "Platform": "twitch",
          "UserId": "123456",
          "Login": "streamer",
          "DisplayName": "Streamer",
          "LinkedAtUtc": 1760000000
        }
      },
      "Loadouts": { "...": "..." }
    }
  }
}
```

## Plugin config migration

On first load:

1. If `oxide/config/MediaIntegration.json` does not exist, try `oxide/config/TwitchIntegration.json`.
2. Set `ConfigVersion = 4`.
3. Preserve `ChatPrefix`, `RelayUrl`, `ServerId`, `ServerKey`, tiers, effects, icons, cooldowns, and command prefixes.
4. Add `CurrencyConversion` with `Enabled = false` and an empty rate table. Staff opt in and set rates; migration never invents rates.
5. Preserve or add optional `Platforms` on tiers. Empty means every provider.
6. For each trigger:
   - If it has no `Platform`, default it to `twitch` so a legacy Twitch trigger cannot start firing from a custom webhook by accident. Staff can clear the field or set another platform later.
   - Add `Unit` where known: bits for Twitch cheers, viewers for raids, months for resubs, currency for fiat.
   - `MinUsd` defaults to `0`; set it only when a USD threshold should be shared across platforms.
7. Add a `Providers` display section for the platforms the server wants enabled. Credentials stay on the relay.
8. Save the new config.

The first providers to enable after migration are Twitch (parity), then YouTube and Kick. Do not enable an unofficial provider until it passes `UNOFFICIAL_ADAPTERS.md`.

## Permission migration

Register both sets during the transition:

```text
media.use
media.streamer
media.admin
media.tier.<id>

twitch.use        (legacy alias)
twitch.streamer   (legacy alias)
twitch.admin      (legacy alias)
twitch.tier.<id>  (legacy alias)
```

Permissions are shared across platforms. There are no per-platform tier permission nodes; per-platform behavior is controlled by the optional `Tiers[].Platforms` list.

Migration behavior:

- If a user/group has `twitch.use`, grant `media.use`.
- If a user/group has `twitch.admin`, grant `media.admin`.
- If a user/group has `twitch.tier.X`, grant `media.tier.X`.
- If the config still uses the legacy default `TierPermissionPrefix = "twitch.tier."`, rewrite it and every tier permission to `media.tier.*`. Custom prefixes are left alone.
- Move members of the legacy `twitch` group to `media`; keep the old group until staff confirm.
- Do not revoke legacy nodes automatically in the first release.
- Run the permission mirror once, tracked by `StoredData.LegacyPermissionsMigrated`, so later staff revocations are not undone on restart.
- Log the migration; a dedicated permissions report is still future work.

## Relay migration

Rename the project/binary to `MediaRelay`, but keep the API contract stable:

- `/api/link/start` gains `platform`; if omitted, default `twitch`.
- `/api/link/status` returns the same shape plus `platform`.
- `/api/links` returns links grouped by platform.
- `/api/events/pending` returns `MediaEvent` objects with `platform`.
- `/api/events/ack` accepts `platform:id` keys (preferred) and plain ids for compatibility.
- `/api/status` adds per-provider health; existing fields stay.
- Old Twitch-only queued events parse with `Platform = "twitch"`.
- Token records carry a `platform` field; old records default to `twitch`. Twitch records remain keyed by the provider user id, and custom webhook records are keyed by the SHA-256 token hash.

Configuration:

```json
{
  "providers": {
    "twitch": { "...": "..." },
    "youtube": { "enabled": false, "...": "..." }
  }
}
```

Old `clientId`, `clientSecret`, `eventSubUrl`, and `servers` keys should still load; the relay maps them into `providers.twitch` for backward compatibility. Write the new shape on the next save.

## Server Assist panel migration

- Unregister any old panel id `twitch` when the old plugin unloads.
- Register `media` with label `Media` and the same shared icon (`Icons.Panel`, default `sprite:sa_stream_panel`).
- Keep the same layout: Home, Loadouts, Catalog, Admin.
- Add a **Links** page with one row per provider.
- Do not create a separate panel per platform. Platforms are rows/badges inside the shared panel.
- If both old and new plugins are loaded, the new plugin should detect the old panel and warn staff to remove `TwitchIntegration.cs`.

## Commands

```text
/media                  open the panel
/media link <platform>  start a link (twitch, youtube, kick, ...)
/media unlink <platform>
/media on | off
/media pause | resume
/media status
/media loadout
/media catalog

/media providers        list configured providers and capabilities
/media relaytest
/media reload
/media test <platform> <event> [amount]
/media tier <player> <tierId>
/media untier <player> <tierId>
/media migrate          run data/config/permission migration manually
```

`/twitch` remains an alias for one release and forwards to `/media`.

## Icons

- Keep the `sa_stream_*` role names.
- No platform logos are added to the atlas.
- Platform identity is a text badge (`TWITCH`, `YOUTUBE`, `KICK`, etc.) on rows/audit.
- Optional per-platform icons can be added later through `Icons` if each platform's brand guidelines allow it.
- The existing icon request remains valid: 30 streaming-specific sprites, no new platform art.

## Rollout

1. **Design approval** — this document and `PLATFORMS.md`.
2. **Phase 0 code**:
   - Rename class/file/config/data paths.
   - Add `Platform` to the event model and triggers.
   - Add `Links` to the player profile.
   - Port the Twitch adapter to `IMediaProvider`.
   - Add the generic webhook provider.
   - Implement migration and legacy aliases.
3. **Staging**:
   - Run old and new data through migration tests.
   - Verify Twitch link, loadout, reward, cooldown, audit, and relay polling.
   - Verify `/twitch` alias and legacy permissions.
4. **Production**:
   - Stop the server.
   - Remove `TwitchIntegration.cs`/old panel registration.
   - Install `MediaIntegration.cs`.
   - Keep `oxide/data/TwitchIntegration/players.json` as a backup.
   - Start, verify migration log, run `/media relaytest`, test a Twitch redemption.
5. **Cleanup release**:
   - Remove `/twitch` alias and legacy permissions after staff confirm.
   - Archive old data/config.

## Rollback

- Keep the old plugin file and old data/config untouched until the new version has run for a full week.
- To roll back, stop the server, remove `MediaIntegration.cs`, restore `TwitchIntegration.cs`, and restore the old data/config if the new version modified them.
- The relay API is backward compatible, so the old plugin can still poll the new relay for Twitch events (platform defaults to `twitch`).

## Breaking changes to announce

- Plugin name and config/data paths change.
- `/twitch` is an alias, but `/media` is the documented command.
- Permissions move from `twitch.*` to `media.*`; legacy nodes are mirrored temporarily.
- Server Assist panel id changes from `twitch` to `media`.
- Triggers gain `Platform`; unqualified triggers may apply to more platforms than before. Staff should review them.

## Phase 0 checklist

- [ ] Rename plugin class/file, config, data, commands, permissions.
- [ ] Add `MediaEvent.Platform`.
- [ ] Add `PlayerProfile.Links`.
- [ ] Migrate old Twitch profile data.
- [ ] Migrate old config and triggers.
- [ ] Register legacy permission aliases and migrate nodes.
- [ ] Port Twitch to `IMediaProvider`.
- [ ] Add `/api/providers` and per-provider health.
- [ ] Add generic webhook provider.
- [ ] Keep `/twitch` command alias.
- [ ] Test migration with a real TwitchIntegration data/config pair.
- [ ] Update Server Assist panel registration and docs.
