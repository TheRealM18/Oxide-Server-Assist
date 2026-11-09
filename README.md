# Server Assist and Oxide plugins

This repository contains the Server Assist client mod, the `ServerAssist` Oxide plugin, companion plugins, supporting programs and the documentation source.

## Documentation

The documentation is built with **MkDocs Material** and published through **Read the Docs**.

- Documentation source: `docs/`
- MkDocs configuration: `mkdocs.yml`
- Read the Docs configuration: `.readthedocs.yaml`

The published site uses nested folders, so every page is served as an `index.html` under its category path.

The docs include a full reference page for every bundled plugin, client mod, program and art pack under `docs/plugins/`.

## Projects

| Project | Type |
|---|---|
| `ServerAssist` | Client mod + Oxide plugin |
| `AdminKits` | Oxide plugin |
| `AdminShop` | Oxide plugin |
| `AdminSuite` | Oxide plugin |
| `AuctionHouse` | Oxide plugin |
| `BuggeeNet` | Oxide plugin |
| `DailyRewards` | Oxide plugin |
| `DynamicBackpack` | Oxide plugin |
| `PermissionGui` | Oxide plugin |
| `PlayerProfiles` | Oxide plugin |
| `PluginHotReload` | Oxide plugin |
| `TwitchIntegration` | Oxide plugin + relay |
| `VoteSkipNight` | Oxide plugin |
| `QLNPackBootstrap` | Client mod |
| `QLNPackServer` | Program |
| `ServerAssistArt` | Art source |

## Releases

Client mods, programs and plugin ZIPs are packaged by the manual **Build Release** GitHub Actions workflow.

- Prebuilt DLLs/exes live in `release-inputs/`.
- The workflow does not compile client mods or programs.
- Plugin releases are packaged from their `.cs` source.
- Use **Actions → Build Release → Run workflow**.
- Generated files are written to `release-out/` and attached to the GitHub Release.

The workflow produces:

- one ZIP per plugin
- one ZIP per mod
- one ZIP per program/runtime
- `Oxide-Plugins-<version>.zip`
- `Oxide-Mods-<version>.zip`
- `Oxide-Programs-<version>.zip`
- `Oxide-All-<version>.zip`
- `SHA256SUMS.txt`
