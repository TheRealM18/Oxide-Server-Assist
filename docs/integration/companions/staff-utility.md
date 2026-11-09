# Staff and utility companion recipes

These plugins cover staff tools and common server utilities. Follow the individual page for installation, commands, permissions and configuration.

| Plugin | Model | Server Assist id | Full reference |
|---|---|---|---|
| Vote Skip Night | Panel / hook | `voteskip` | [Vote Skip Night](../../plugins/player/vote-skip-night.md) |
| AdminKits | Panel / hook | `adminkits` | [AdminKits](../../plugins/administration/admin-kits.md) |
| AdminSuite | Panel / hook | `adminsuite` | [AdminSuite](../../plugins/administration/admin-suite.md) |
| Daily Rewards | Panel / hook | `dailyrewards` | [Daily Rewards](../../plugins/player/daily-rewards.md) |
| Dynamic Backpack | Panel / hook | `backpack` | [Dynamic Backpack](../../integration/dynamic-backpack.md) |
| Plugin Hot Reload | Panel / hook | `pluginhotreload` | [Plugin Hot Reload](../../plugins/utilities/plugin-hot-reload.md) |

## Integration notes

- Each plugin is server-only and renders through the existing Server Assist client.
- Each plugin registers a stable lowercase id and owns its permission node.
- Dynamic Backpack uses `backpack.1` for the panel and `backpack.1` … `backpack.9` for page unlocks.
- Staff plugins still validate every action on the server. The panel permission only controls visibility and entry.

For registration code and page navigation, see [Panels and hooks](../../integration/api/panels.md) and [Page navigation](../../integration/api/view-protocol.md#page-navigation).
