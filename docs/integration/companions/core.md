# Core companion recipes

These are the core companion plugins used by most servers. Full installation, command, permission and configuration reference lives on the individual plugin pages.

| Plugin | Model | Server Assist id | Full reference |
|---|---|---|---|
| PermissionGui | Panel / hook | `permissions` | [PermissionGui](../../plugins/administration/permission-gui.md) |
| Admin Shop | Panel / hook | `adminshop` | [Admin Shop](../../plugins/economy/admin-shop.md) |
| Auction House | Provider with panel fallback | `auctionhouse` | [Auction House](../../plugins/economy/auction-house.md) |

## Integration notes

- **PermissionGui** registers `permissions`, uses `permissiongui.use`, and sets the `sprite:sa_permission_gui` card icon.
- **Admin Shop** registers `adminshop`, uses `adminshop.use`, and sets the `sprite:sa_admin_shop` card icon.
- **Auction House** registers the `auctionhouse` provider, uses `auctionhouse.use`, sets `sprite:sa_auction_house`, and falls back to a normal home panel when `GetProviderHomeSupport()` is false.
- All three enforce their own permission checks on every action. The Server Assist hook or provider permission is visibility, not the only security boundary.

For registration code and the current API, see [Panels and hooks](../../integration/api/panels.md) and [Providers](../../integration/api/providers.md).
