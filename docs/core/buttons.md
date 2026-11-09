# Buttons

A Server Assist button is a permission-aware shortcut. It has a label, an optional description and icon, a scope, a target type, a target and a permission. Clicking it either opens an XUi window group or runs a client console command.

Buttons are the core of the product. This page covers scopes, targets, creation, editing, validation, ordering, limits and security.

---

## Button model

| Field | Purpose | Example |
|---|---|---|
| `Id` | Stable identifier used in actions and permission nodes | `admin-shop` |
| `Label` | Card title | `Admin Shop` |
| `Description` | Card subtitle and tooltip context | `Buy items with dukes.` |
| `TargetType` | `window` or `command` | `window` |
| `Target` | Window group name or console command | `character` |
| `Permission` | Visibility/launch gate; blank = everyone | `adminshop.use` |
| `Icon` | Icon key | `item:casinoCoin` |
| `IconTint` | Optional color | `#FFCC66` |
| `Enabled` | Launch state | `true` |
| `Order` | Sort order | `10` |

The data is stored in `oxide/data/ServerAssist/data.json` under `Shared` and `Personal`.

---

## Scopes

### Shared buttons

Shared buttons belong to the server. They are stored once in `data.Shared` and appear for every player whose permissions allow them.

- Created and edited by `serverassist.admin`.
- Live in the **Shared** tab.
- Use `serverassist.button.<id>` for an auto permission.
- The default limit is 60 (`MaxSharedButtons`, clamped 1–200).
- Admins can see disabled shared buttons when `AdminSeesAllButtons` is true.

### Personal buttons

Personal buttons belong to one player. They are stored under `data.Personal[playerId]`.

- Require `serverassist.custom` and `AllowPlayerCustomButtons`.
- Live in the **My Buttons** tab.
- Use `serverassist.personal.<id>` for an auto permission.
- The default limit is 20 per player (`MaxPersonalButtons`, clamped 0–100).
- The creator receives the auto personal node automatically.
- An admin can edit or revoke the node later.

### Shared vs personal

| Behaviour | Shared | Personal |
|---|---|---|
| Who can create | `serverassist.admin` | `serverassist.custom` |
| Who sees it | Anyone passing the permission | The owner (and admins) |
| Stored under | `data.Shared` | `data.Personal[playerId]` |
| Auto node | `serverassist.button.<id>` | `serverassist.personal.<id>` |
| Auto node granted to creator | No | Yes |
| Command target default | Allowed | Disabled unless `AllowPlayerCommandButtons` |
| Visible in managers | Shared tab | My Buttons tab |
| Limit | `MaxSharedButtons` | `MaxPersonalButtons` |

!!! note
    "Personal" is a scope, not a privacy guarantee. An Oxide administrator can see and edit every personal button. Do not store secrets in a button target.

---

## Target types

### `window`

Opens an XUi window group on the client.

```text
TargetType: window
Target:     character
```

Requirements:

- The target mod and its XUi config must already be installed on the client.
- The target name must match the window group name exactly (case-insensitive).
- Server Assist closes itself first when `launch=...|1`.
- Unknown groups produce a local status error and leave the board usable.

Known behaviours:

| Target | Result |
|---|---|
| `map` | Map window, if the client build exposes that group |
| any custom group | Modal window group |
| `serverAssist` | Refused locally to avoid recursion |

Window group names are restricted to letters, digits, `_`, `-` and `.`. No spaces, slashes or `|`.

!!! warning
    A button can only open a window that already exists on the client. Server Assist does not ship other mods' XUi XML and cannot create a window group at runtime.

### `command`

Runs a console command through the client's local console.

```text
TargetType: command
Target:     serverassist
```

Rules:

- The leading `/` is optional and is stripped.
- The command cannot contain `|`.
- The command runs locally on the client that clicked the button.
- Server-authoritative commands still enforce their own permissions.
- A command button is **not** an admin grant.
- Personal command buttons are disabled by default. An admin can enable `AllowPlayerCommandButtons` if the server's threat model allows it.

Command buttons are best for local UI toggles, chat shortcuts and client-side console commands. For a server action, expose it through a companion panel or provider that validates the player on the server.

---

## Creating a button

### Through the GUI

1. Open **Shared** (admin) or **My Buttons** (`serverassist.custom`).
2. Click **New Button**.
3. Answer the prompts:

   | Prompt | Input | Notes |
   |---|---|---|
   | Label | `Admin Shop` | Required, max 48 |
   | Type | `window` or `command` | Defaults to `window` |
   | Target | `adminshop` or `say hello` | Depends on type |
   | Permission | blank, `auto`, or a node | `auto` creates a per-button node |

4. The button is created, saved and selected in the manager.
5. Grant the permission if you used one.

The create flow validates each field immediately. A bad value re-prompts with an explanation instead of discarding the draft.

### Through the console

```text
/sa add <label> <window|command> <target> [permission]
```

Example:

```text
/sa add Shop window adminshop adminshop.use
```

Limitations:

- The label is one console argument, so it cannot contain spaces.
- The description is empty.
- The icon is the default for the target.
- The order is appended to the end.
- The button is shared.

Use the GUI for normal authoring.

---

## The 12-action editor toolbar

Selecting a button opens the editor. The nine information rows are read-only; every change goes through one of twelve actions:

| # | Button | Action | What it does |
|---|---|---|---|
| 1 | **Edit Label** | `editfield\|scope\|id\|label` | Prompt for a new label (max 48) |
| 2 | **Edit Description** | `editfield\|scope\|id\|description` | Prompt for a description (max 160, blank allowed) |
| 3 | **Edit Icon** | `editfield\|scope\|id\|icon` | Prompt for an icon key |
| 4 | **Find Item Icon** | `iconsearch\|scope\|id` | Open the item icon picker |
| 5 | **Target Type** | `editfield\|scope\|id\|type` | Switch `window` / `command`; clears the target |
| 6 | **Edit Target** | `editfield\|scope\|id\|target` | Prompt for a window group or command |
| 7 | **Edit Permission** | `editfield\|scope\|id\|permission` | Prompt for blank, `auto`, or a node |
| 8 | **Disable / Enable** | `toggle\|scope\|id` | Flip `Enabled` and save |
| 9 | **Move Up** | `move\|scope\|id\|up` | Move earlier in the list |
| 10 | **Move Down** | `move\|scope\|id\|down` | Move later in the list |
| 11 | **Delete** | `delete\|scope\|id` | Prompt for `DELETE` |
| 12 | **Back** | `back` | Return to the manager |

Exactly twelve actions is a protocol limit. The editor uses all twelve.

### Editing rules

- **Label** cannot be empty.
- **Description** may be blank.
- **Icon** may be blank, `auto`, `item:name`, `sprite:name`, `atlas:name:sprite`, or a plain sprite name.
- **Target Type** clears the old target because a window group and a command are not interchangeable.
- **Target** is validated for the current type.
- **Permission** accepts blank, `auto`, or letters/digits/`_`/`-`/`.` only.
- **Auto** on an existing button resolves immediately to `serverassist.button.<id>` or `serverassist.personal.<id>`.
- **Delete** requires typing `DELETE`; anything else cancels.

### Changing the permission

When the permission is edited:

1. The new node is normalized.
2. Server Assist registers it if it is in its own namespace.
3. If the scope is personal and the node is exactly `serverassist.personal.<id>`, the owner is granted it.
4. The data is saved.

Server Assist deliberately does **not** register or grant a permission owned by another plugin. A button can point at `adminshop.use`, but AdminShop owns that node.

---

## Ordering and paging

- New buttons get `max(Order) + 10`.
- Move Up / Move Down swap the button with its neighbour and rewrite all orders as `(index + 1) * 10`.
- The first button cannot move up; the last cannot move down. The status line explains the edge.
- Ten cards fit on one page. Paging buttons appear when there are more.
- The home list is sorted by `Order`, then label. Buttons inside a scope break ties by id.
- Hooks have their own order values and are interleaved with buttons on Home.

Page movements do not change the order values; only Move Up / Move Down rewrites them.

---

## Enable, disable and delete

### Disable

A disabled button:

- is not launchable by anyone, including admins;
- is hidden from normal players;
- is still visible to admins in the manager and on Home when `AdminSeesAllButtons` is true;
- is shown in the Shared manager with `[off]` and an `OFF` badge;
- keeps its data and permission.

Use Disable instead of Delete when you want to keep the configuration for later.

### Delete

1. Click **Delete**.
2. Type `DELETE` in the input.
3. Submit.

The button is removed from the scope map and the data is saved. There is no undo. Back up `data.json` before large cleanups.

---

## Validation reference

| Field | Rule | Error |
|---|---|---|
| Label | Trim, collapse newlines, max 48, not empty | `Button label (cannot be empty)` |
| Description | Max 160 | Truncated silently |
| Window target | Letters, digits, `_`, `-`, `.` | `Window group names can only use letters, numbers, '_', '-' and '.'` |
| Command target | Max 160, not empty, no `|` | `Command cannot contain the '|' character.` |
| Permission | Letters, digits, `_`, `-`, `.`, max 96 | `Permission nodes can only use letters, numbers, '_', '-' and '.'` |
| Icon `item:` | Item must resolve on the server | `Item not found.` |
| Icon plain | Letters, digits, `_`, `-`, `.` | `Use item:name, sprite:name, atlas:name, or a plain sprite name.` |

---

## Permissions and auto nodes

### Blank permission

Everyone who can open Server Assist can see and launch the button.

### Named permission

The player must hold the node, unless they are an Oxide administrator.

```text
adminshop.use
permissiongui.use
voteskip.use
```

Server Assist does not register these; the owning plugin does.

### `auto`

At create time:

| Scope | Node |
|---|---|
| Shared | `serverassist.button.<id>` |
| Personal | `serverassist.personal.<id>` |

Server Assist registers nodes in its own namespaces and shows them in PermissionGui. Grant them to groups or users normally.

### Admin default permissions

The administrators group receives:

```text
serverassist.use
serverassist.admin
serverassist.custom
```

The default players group receives `serverassist.use` and, when configured, `serverassist.custom`.

---

## Visibility examples

| Button | Permission | Non-admin sees it? | Admin sees it? |
|---|---|---|---|
| Admin Shop | `adminshop.use` | Only with the node | Yes |
| Staff Kits | `adminkits.use` | Only with the node | Yes |
| Disabled shared test | blank, `Enabled=false` | No | Yes, if `AdminSeesAllButtons` |
| Personal button | `serverassist.personal.<id>` | Owner only | Yes |

---

## Target recipes

| Goal | Type | Target | Permission |
|---|---|---|---|
| Vanilla map | window | `map` | blank |
| Open the backpack from a custom button | command | `backpack` | `backpack.1` |
| Open the board from a command | command | `serverassist` | blank |
| Open the shop | command | `shop` | `adminshop.use` |
| Open the auction | command | `auction` | `auctionhouse.use` |
| Open PermissionGui | *(use the hook row)* | — | `permissiongui.use` |
| Open AdminSuite | *(use the hook row)* | — | `adminsuite.use` |

!!! tip
    PermissionGui, AdminShop, VoteSkipNight, AdminKits, AdminSuite, DailyRewards, DynamicBackpack, PluginHotReload, TwitchIntegration, BuggeeNet and PlayerProfiles are **panels/hook rows**, not window targets. They appear as cards automatically when their plugins register. Do not create buttons that target old standalone windows for them; a `backpack` command button, if you need one, runs the chat command and opens the panel.

---

## Security notes

- The server re-checks every launch. A modified client cannot make a hidden button launch.
- A `window` target runs entirely on the client. Only point it at windows the player is allowed to see.
- A `command` target runs on the client. Server commands inside it still check the player's permission on the server.
- Personal buttons cannot be used to self-grant a shared button's permission. The auto-grant path only grants the exact personal node for that button.
- Server Assist only registers permission nodes in `serverassist.button.*` and `serverassist.personal.*`. It never claims another plugin's node.
- Do not grant `serverassist.admin` to players who should only use personal buttons.

---

## Example data file

```json
{
  "Version": 1,
  "SeededExamples": true,
  "Shared": {
    "admin-shop": {
      "Id": "admin-shop",
      "Label": "Admin Shop",
      "Description": "Buy items and permission products with dukes.",
      "TargetType": "window",
      "Target": "adminshop",
      "Permission": "adminshop.use",
      "Icon": "item:casinoCoin",
      "IconTint": "#FFD166",
      "Enabled": true,
      "Order": 30
    }
  },
  "Personal": {
    "76561198000000000": {
      "my-kits": {
        "Id": "my-kits",
        "Label": "My Kits",
        "Description": "Open the kit browser.",
        "TargetType": "window",
        "Target": "adminshop",
        "Permission": "serverassist.personal.my-kits",
        "Icon": "atlas:ServerAssistAtlas:kits",
        "Enabled": true,
        "Order": 10
      }
    }
  }
}
```

This example is illustrative. Do not hand-edit the live file while the server is running.

---

## Related pages

- [Icons](icons.md)
- [Item Search](item-search.md)
- [Permissions](permissions.md)
- [Interface](interface.md)
- [Troubleshooting](../reference/troubleshooting/index.md)
