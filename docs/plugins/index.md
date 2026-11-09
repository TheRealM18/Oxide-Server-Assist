# Plugins and programs

This section is the reference for every bundled plugin, client mod, program and art pack in the repository.

The core Server Assist product is documented separately:

- [Server Assist product guide](../core/server-assist.md)
- [Server Assist API](../integration/api/index.md)
- [Install Server Assist](../start/install.md)

## Administration

| Plugin | Version | Purpose |
|---|---|---|
| [PermissionGui](administration/permission-gui.md) | 3.5.0 | Groups, users, tracks, defaults and a searchable permission browser |
| [AdminSuite](administration/admin-suite.md) | 1.0.4 | Staff toolkit for players, moderation, inventory, world, logs, snapshots and rollback |
| [AdminKits](administration/admin-kits.md) | 1.1.0 | Item kits with exact item data, cooldowns, limits and per-kit permissions |
| [Sentinel AC](administration/sentinel-ac.md) | 1.0.2 | Statistical anti-cheat, anti-exploit and anti-grief evidence with staff review |

## Economy

| Plugin | Version | Purpose |
|---|---|---|
| [Admin Shop](economy/admin-shop.md) | 1.2.0 | Sell game items plus permanent or temporary permission products for dukes |
| [Auction House](economy/auction-house.md) | 1.1.1 | Player listings, buyout purchases, fees and admin controls |

## Player features

| Plugin | Version | Purpose |
|---|---|---|
| [Daily Rewards](player/daily-rewards.md) | 1.1.0 | Daily, weekly, monthly and seasonal playtime rewards |
| [Dynamic Backpack](../integration/dynamic-backpack.md) | 2.2.0 | Server-only nine-page item storage panel |
| [Player Profiles](player/player-profiles.md) | 1.0.0 | Multiple saved characters per account with in-place switching |
| [Vote Skip Night](player/vote-skip-night.md) | 1.2.0 | Player vote to skip the current night |

## Server utilities

| Plugin | Version | Purpose |
|---|---|---|
| [Plugin Hot Reload](utilities/plugin-hot-reload.md) | 1.0.1 | Watches and reloads Oxide plugins, with filters, history and commands |
| [Media Integration](utilities/media-integration.md) | 0.3.0 | Platform-neutral successor to TwitchIntegration; Twitch + signed custom webhooks |
| [Twitch Integration](utilities/twitch-integration.md) | 0.2.1 | Legacy Twitch-only implementation and rollback path |
| [BuggeeNet](utilities/buggee-net.md) | 0.2.0 | Redis-backed fleet registry, redirects and shared player state |

## Client mods

| Component | Version | Purpose |
|---|---|---|
| [Server Assist Mod](client/server-assist-mod.md) | 2.1.0 | Client XUi window, protocol renderer, net packages and Player Profiles client support |
| [QLN Pack Bootstrap](client/qln-pack-bootstrap.md) | 0.1.0 | Signed client pack download, verification, staging and updater |

## Programs

| Program | Version | Purpose |
|---|---|---|
| [QLN Pack Server](programs/qln-pack-server.md) | not declared | Serves the server's client mod pack and manifest |
| [MediaRelay](programs/media-relay.md) | 0.3.0 | MediaIntegration relay: Twitch EventSub, custom webhooks and event queues |
| [TwitchRelay](programs/twitch-relay.md) | 0.1.1 | Legacy Twitch EventSub relay |

## Art and assets

| Pack | Version | Purpose |
|---|---|---|
| [Server Assist Art](art/server-assist-art.md) | 1.3.0 | Hand-painted `sa_*` UI sprites used by Server Assist and companion plugins |

## Compatibility notes

- Every plugin that shows an in-game interface renders through **Server Assist 2.1.0**.
- Gameplay and chat commands keep working without Server Assist unless the plugin page says the panel is mandatory.
- All server-side plugins are installed into `oxide/plugins`.
- Client mods are installed under `Mods/` on the server and every client.
- Programs are standalone services or tools and do not load in Oxide.
