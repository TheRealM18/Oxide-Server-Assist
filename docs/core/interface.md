# Interface

The Server Assist window is a fixed 1360×820 panel with a persistent left sidebar and a content pane. The layout is defined in `Config/XUi_InGame/windows.xml`; the controllers live in `ServerAssistMod.dll`.

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│  [hammer]  SERVER ASSIST                                       [X]           │
│            SERVER TOOLS  •  QUICK ACTIONS                                   │
├───────────────────┬──────────────────────────────────────────────────────────┤
│  NAVIGATION       │  Server Assist                                           │
│                   │  Home  |  12 entries available  |  page 1/2              │
│  [ Home ]         │  Status line                                              │
│  [ Shared ]       │                                                          │
│  [ My Buttons ]   │  ┌─────────────────────┐  ┌─────────────────────┐          │
│  [ Refresh ]      │  │ [icon] Admin Shop   │  │ [icon] Admin Kits   │          │
│  [ Back ]         │  │        dukes...     │  │        kits...      │          │
│                   │  └─────────────────────┘  └─────────────────────┘          │
│                   │  ┌─────────────────────┐  ┌─────────────────────┐          │
│                   │  │ ...                 │  │ ...                 │          │
│                   │  └─────────────────────┘  └─────────────────────┘          │
│                   │  ... 10 cards total                                       │
│                   │                                                          │
│  F7 / F6 to open  │  [input label]                                           │
│  Server Assist v2 │  [ text input ................................ ] [Submit] │
│                   │  [Cancel]                                                │
│                   │  [btn0] [btn1] [btn2] [btn3] [btn4]                      │
│                   │  [btn5] [btn6] [btn7] [btn8] [btn9]                      │
│                   │  [btn10] [btn11]                                         │
├───────────────────┴──────────────────────────────────────────────────────────┤
│  F7 or F6 toggles Server Assist  •  Click a card to launch  •  My Buttons ...│
└──────────────────────────────────────────────────────────────────────────────┘
```

---

## Dimensions

| Element | Position | Size |
|---|---|---|
| Window | centered | 1360 × 820 |
| Header icon | 24, −18 | 38 × 38 |
| Title | 72, −10 | 520 × 34 |
| Close button | 1260, −16 | 80 × 40 |
| Sidebar | 20, −84 | 250 × 700 |
| Content pane | 286, −84 | 1054 × 700 |
| Content title | 20, −12 | 760 × 34 |
| Subtitle | 20, −48 | 1010 × 24 |
| Status | 20, −74 | 1010 × 24 |
| Card grid | 16, −102 | 2 columns × 5 rows of 500 × 90 |
| Input label | 20, −588 | 760 × 22 |
| Text input | 20, −614 | 720 × 32 |
| Submit / Cancel | 752 / 870, −612 | 110 / 136 × 34 |
| Action buttons | 16…832, −654/−700/−746 | 12 × 160 × 36 |
| Footer | 20, −794 | 1320 × 20 |

The layout is designed for the vanilla in-game UI scale. If you run a very low resolution, the window may be clipped; use the game's UI scale setting or edit `windows.xml` for your server pack.

---

## Left sidebar

The sidebar is the navigation anchor and never scrolls away.

| Control | Behaviour |
|---|---|
| **Home** | Shared and personal buttons the player can launch, plus visible hooks |
| **Shared** | Admin-only shared-button manager |
| **My Buttons** | Personal-button manager; requires `serverassist.custom` and `AllowPlayerCustomButtons` |
| **Refresh** | Re-requests the current view |
| **Back** | Visible only in hook mode; leaves the hook and returns Home |

The active tab is rendered as `[Label]`. A provider can rename all three labels through `tab1label`, `tab2label` and `tab3label`; the layout and position stay the same.

### Tab permissions

| Tab | Requirement |
|---|---|
| Home | `serverassist.use` or admin |
| Shared | `serverassist.admin` |
| My Buttons | `serverassist.custom` plus `AllowPlayerCustomButtons`, or admin |

If a player clicks a tab they cannot manage, the server returns Home and sets a status message explaining the missing node.

---

## Content pane

### Header block

| Element | Source | Notes |
|---|---|---|
| Content title | `title=` | Defaults to `Server Assist`; providers usually set their own |
| Subtitle | `subtitle=` | Page context, counts and page number |
| Status | `status=` | Last action result, permission error or confirmation prompt |

The static brand in the window header is always **SERVER ASSIST**, even when a provider sets a different content title. The provider title is rendered in the content pane, not in the brand bar.

### Art cards

Each row is a clickable card with:

| Part | Detail |
|---|---|
| Icon | 64 × 64 sprite, resolved from `item:`, `sprite:`, `atlas:` or plain name |
| Label | 20 px primary text, one line |
| Subtitle | 14 px secondary text, up to two lines |
| Badge | Metadata used as a tooltip prefix (`PLUGIN`, `PERSONAL`, `OFF`, `ADMIN`, …) |
| Tint | Optional HTML color applied to the icon |
| State | Disabled cards use a faded icon (45% alpha) and grey labels; selected cards use a gold label |

Ten cards are shown per page. Pages are filled left-to-right, top-to-bottom.

A card is hidden when both its label and action are empty. A placeholder card ("No buttons are available.") is rendered when the list is empty.

### Card content by source

| Source | Label | Subtitle | Badge |
|---|---|---|---|
| Shared button | Button label | Description, `type: target`, permission, disabled/no-permission note | — |
| Personal button | Button label | Same, plus `personal` | `PERSONAL` |
| GUI hook | `[GUI] <label>` | Hook description plus permission | `PLUGIN` |
| Disabled shared button (admin) | Label | Includes `DISABLED` | — |

Admins with `AdminSeesAllButtons` see disabled shared buttons in the Shared manager and, subject to the same flag, on Home. Normal players never see a button they cannot launch.

### Shared and My Buttons managers

The managers use the same card area, but each card opens the editor instead of launching. The subtitle includes the target and permission; the label is prefixed `[on]` or `[off]`.

Manager actions:

| Action | Where |
|---|---|
| **New Button** | Manager toolbar |
| **Back to Home** | Manager toolbar |
| **Prev / Next** | Above 10 cards |
| Edit a card | Click it |

### Editor screen

The editor replaces the card list with nine read-only information rows:

1. Label
2. Description
3. Icon
4. Target Type
5. Target
6. Permission
7. Status
8. Id
9. Scope

The 12-action toolbar is described in [Buttons](buttons.md#the-12-action-editor-toolbar).

### Item icon picker

The picker replaces the card area with search results. Each result shows the localized item name, `item:<name>` and the live icon. Ten results per page.

See [Item Search](item-search.md).

### Text input

One text input is shared by every prompt:

| Prompt | Example label |
|---|---|
| New button label | `Button label` |
| Target type | `Type: window or command` |
| Target | `XUi window group name` or `Console command (without /)` |
| Permission | `Permission node (blank = everyone, auto = per-button node)` |
| Delete confirmation | `Type DELETE to remove '<label>'` |
| Icon | `Icon: item:name, sprite:name, atlas:name, or blank` |
| Item search | `Search items by name` |

The input is prefilled with the current value when editing. **Submit** sends `t|<encoded text>`; **Cancel** sends `a|cancel`. The client clears the field after submit.

### Action buttons

The 12 buttons are context-sensitive. They are hidden when unused. A button whose action is `close` closes the window locally; every other action is prefixed with `a|` and sent to the server.

Server Assist 2.1 also supports an optional 24×24 icon and tint on each action button. When an icon is supplied the label shifts right; when it is omitted the button stays text-only. A disabled button fades the icon to 45% alpha. The protocol fields are documented in [Button fields](../integration/api/view-protocol.md#button-fields).

Typical sets:

| Screen | Buttons |
|---|---|
| Home | Manage Shared, My Buttons, Refresh, paging |
| Shared / My Buttons | New Button, paging, Back to Home |
| Editor | The 12-action editor toolbar |
| Item picker | paging, Search Again, Clear Icon, Back |
| Hook | The hook's buttons plus the sidebar Back button |
| Provider | The provider's buttons; sidebar tabs remain |
| Page navigation | Page 1–9, Deposit Held, Refresh, Back |

### Page navigation panels

A panel can use the card area as a page of data and the action toolbar as a page switcher. The generic pattern is:

```text
9 cards          the current page of items
btn 0-8          page|1 ... page|9
btn 9            deposit
btn 10           refresh
btn 11           hook|back
```

A row card carries a page-specific action such as `withdraw|21`; Server Assist sends it back as `a|withdraw|21`. Page buttons send `a|page|3`, and the panel decides which page is valid. Locked pages are disabled.

DynamicBackpack uses nine item cards for the current page and exactly this nine-page toolbar. AuctionHouse can use the same pattern for listing pages. This is a convention, not a separate API; see [Page navigation](../integration/api/view-protocol.md#page-navigation).

---

## Hook mode

When a player opens a `[GUI]` row, Server Assist enters hook mode:

- `chrome=hook` and `tab=hook` are appended to the hook's response.
- Home / Shared / My Buttons disappear.
- The Refresh button disappears.
- The sidebar **Back** button appears.
- The content title and subtitle come from the hook.
- The hook can use all 10 rows and all 12 action buttons.

The Back button sends `a|hook|back`. Server Assist consumes that action, leaves the hook and returns Home. The provider never receives it.

Hook mode is intended for a self-contained feature screen. It is not a provider: there is no tab renaming and no independent entry command.

---

## Provider mode

Registered providers also appear on Home as `[GUI] <title>` cards with a `PROVIDER` badge. Clicking the card opens the provider in tab mode; a provider can also be opened by its own command with `OpenGuiProvider`.

When a provider opens, Server Assist keeps the normal chrome:

- Home / Shared / My Buttons stay visible.
- The provider can rename them with `tab1label`, `tab2label` and `tab3label`.
- There is no injected Back button.
- `/sa` and `/serverassist` return to the board.
- `F7` resumes the active provider.
- Provider requests are routed before the board's own `serverassist.use` check.

Provider mode is intended for a destination such as AuctionHouse, where Browse / My Listings / Sell are the real tabs.

---

## Window behaviour

| Situation | Behaviour |
|---|---|
| Press `F7` or `F6` | Toggle the window |
| Press while another modal is open | Ignored; the game already has a modal |
| Open from chat `/sa` | Open at Home and clear any provider/hook |
| Open from client console `sa` | Open at Home |
| Launch a window target | Close Server Assist, then open the target |
| Launch any other window | Close Server Assist, open the target modally |
| Launch a command | Close Server Assist (by default), run through the local console |
| Unknown target | Remain on the board and show a local error in the status line |
| Target `serverAssist` itself | Refused locally: "That button points at the Server Assist window itself." |

The close-before-launch flag is `1` in the `launch=` line. A provider can return `launch=<type>|<target>|0` if it wants the board to stay open, but the default and the built-in buttons always close first.

---

## Status messages

Status text is plain text, yellow in the content pane. Common messages:

| Message | Meaning |
|---|---|
| `Refreshed.` | Manual refresh |
| `Button '<name>' created.` | Create succeeded |
| `Grant '<node>' to allow access.` | Button uses a permission |
| `It is visible to everyone.` | Blank permission |
| `You need the 'serverassist.admin' permission...` | Tab or action denied |
| `That button is disabled.` | Launch refused |
| `You do not have permission to use '<name>'.` | Button permission missing |
| `Command buttons are disabled for player-made buttons.` | Personal command restriction |
| `Window group '<name>' was not found on this client.` | Client-side launch failure |
| `GUI hook '<name>' did not return a view.` | Companion returned null/empty |

Status is reset on navigation and on a new action unless the action sets a new message.

---

## Visual conventions

| Convention | Meaning |
|---|---|
| Gold label | Selected card in a manager |
| Faded icon | Disabled card or button |
| `[GUI]` prefix | Home row supplied by a hook |
| `PLUGIN` badge | Hook or plugin-provided panel |
| `PERSONAL` badge | Personal button |
| `OFF` badge | Disabled button in a manager |
| `[on]` / `[off]` | Manager state prefix |
| `[Tab]` | Active sidebar tab |
| `|` separators in subtitles | Structured metadata: description, target, permission, state |

---

## Accessibility and readability

- Titles and labels are plain text, so they can be localized by the server plugin.
- Long labels are truncated by the server at 48 characters; subtitles at 160.
- The window uses the game's UI font scale. Very small resolutions can clip the 1360×820 panel.
- Disabled state is communicated by both alpha and text (`DISABLED`, `no permission`), not by color alone.
- The action buttons are at least 38 px high, matching the vanilla simple-button hit area.

---

## Related pages

- [Buttons](buttons.md)
- [Icons](icons.md)
- [Item Search](item-search.md)
- [API](../integration/api/index.md)
- [Troubleshooting](../reference/troubleshooting/index.md)
