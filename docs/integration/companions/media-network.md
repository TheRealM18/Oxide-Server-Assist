# Media and network companion recipes

These plugins connect Server Assist to external services or multi-server state.

| Plugin | Model | Server Assist id | Full reference |
|---|---|---|---|
| Twitch Integration | Panel / hook | `twitch` | [Twitch Integration](../../plugins/utilities/twitch-integration.md) |
| BuggeeNet | Panel / hook | `buggee` | [BuggeeNet](../../plugins/utilities/buggee-net.md) |
| Player Profiles | Panel / hook + `@pp\|` | `playerprofiles` | [Player Profiles](../../plugins/player/player-profiles.md) |

## Integration notes

- **Twitch Integration** registers `twitch`, uses `twitch.use`, and depends on the standalone TwitchRelay service for linking and live events.
- **BuggeeNet** is the product name; the Oxide plugin and folder name are `BuggeeCore`, with `buggeecore.*` permission nodes and the `buggee` panel id.
- **Player Profiles** uses the optional `@pp|` companion protocol for in-place character switching. Older client/server combinations fall back to a safe reconnect.

See [Companion protocols](../../integration/api/companion-protocols.md) for the `@pp|` protocol and [Panels and hooks](../../integration/api/panels.md) for normal registration.
