# View protocol

Part of the [Server Assist API](index.md). Companions return this newline-separated protocol from every handler.

## View protocol

A view is a newline-separated list of `key=value` lines. The first line may be `v2` or `v1`; the parser ignores lines without `=`.

```text
v2
protocol=2
title=Admin Shop
subtitle=Buy items with dukes
status=Ready
tab=home
chrome=normal
tab1label=Browse
tab2label=My Listings
tab3label=Sell
input=Search|1|
row=0|buy|Pistol|9mm|1|0|item:gunPistol|HOT|#FFCC66
row=1|open|Kits|Give a kit|1|0|sprite:ui_game_symbol_backpack||
btn=0|refresh|Refresh|1|Reload this view|sprite:sa_stream_admin_reload|
btn=1|close|Close|1|Close the window|sprite:sa_stream_link_cancel|#FF8888
launch=window|adminshop|1
```

### Top-level keys

| Key | Values | Effect |
|---|---|---|
| `v2` / `protocol` | `2` | Informational parser marker; `protocol=2` is accepted and ignored |
| `title` | text | Content-pane title |
| `subtitle` | text | Content-pane subtitle |
| `status` | text | Status line |
| `tab` | `home`, `shared`, `mine`, `hook` | Active tab highlight |
| `tab1label` | text | Rename the first tab |
| `tab2label` | text | Rename the second tab |
| `tab3label` | text | Rename the third tab |
| `chrome` | `normal`, `hook` | Hook chrome hides tabs and Refresh, shows Back |
| `provider` | text | Provider id carried on a provider view |
| `background` | icon | Large watermark behind the cards |
| `banner` | icon | 72 × 72 image in the content header |
| `input` | `label\|visible\|value` | One text input |
| `row` | nine fields | One card |
| `btn` | five or seven fields | One action button; icon and tint are optional |
| `launch` | `type\|target\|close` | Launch a window or command |

### Limits

| Item | Limit |
|---|---|
| Rows | 10 (`row=0` … `row=9`) |
| Action buttons | 12 (`btn=0` … `btn=11`) |
| Text inputs | 1 |
| Title/subtitle/status | Plain text after decoding |
| Row label | Keep short; the card is 500 × 90 |
| Row subtitle | Two lines at 14 px |

### Panel images

Any hook or provider view can add a custom submenu image:

```text
background=sprite:sa_admin_shop
banner=sprite:sa_admin_shop
```

- `background` is a large, low-opacity watermark behind the cards.
- `banner` is a 72 × 72 image in the content header.
- Both accept the same `item:`, `sprite:`, `atlas:` or plain-name syntax as row icons.
- Server Assist fills both automatically from the panel's registered icon (`RegisterPanel`, `SetPanelIcon` or `SetProviderIcon`) when the view does not set them, so a companion can override either field for its own artwork.

### Page navigation

A panel can use its action buttons as a page switcher. There is no special page API: every page button sends a normal action to the active hook or provider handler.

```text
btn=0|page|1|Page 1|1|Open page 1
btn=1|page|2|Page 2|1|Open page 2
...
btn=8|page|9|Page 9|0|Locked
btn=9|deposit|Deposit Held|1|Store the held stack
btn=10|refresh|Refresh|1|Reload the view
btn=11|hook|back|Back|1|Return to Server Assist
```

The handler receives:

```text
a|page|1
a|page|2
a|deposit
a|refresh
```

`hook|back` is handled by Server Assist itself and returns to the home board. It is not forwarded to the panel.

Row cards can carry page-specific actions such as `withdraw|21` or a provider action such as `view|12`. Because a row action is a single protocol field, percent-encode any `|` inside it:

```text
row=0|withdraw%7C21|AK-47|x1|1|0|item:gunPistol|x1|
```

This is the same pattern Dynamic Backpack uses for nine pages of nine item cards, and it is available to AuctionHouse and any other multi-page panel. AuctionHouse can use `page|N` buttons for listing pages; its current `page|prev` / `page|next` actions are another valid use of the same forwarding rule.

### Row fields

```text
row=<index>|<action>|<label>|<sub>|<enabled>|<selected>|<icon>|<badge>|<tint>
```

| Field | Required | Meaning |
|---|---|---|
| `index` | Yes | 0–9 |
| `action` | Yes | Sent back as `a|<action>` when clicked |
| `label` | Yes | Card title |
| `sub` | No | Card subtitle |
| `enabled` | Yes | `1` or `0` |
| `selected` | Yes | `1` or `0`; selected labels are gold |
| `icon` | No | `item:`, `sprite:`, `atlas:`, plain, blank |
| `badge` | No | Tooltip prefix |
| `tint` | No | `#RRGGBB` or `#RRGGBBAA` |

The original six-field row is still valid:

```text
row=0|open|Shop|Buy items|1|0
```

Missing icon, badge and tint fields fall back to defaults.

Examples:

```text
row=0|launch|shared|shop|Admin Shop|Buy items|1|0|item:casinoCoin|PLUGIN|#FFD166
row=1|open|kits|Admin Kits|Give a kit|1|0|sprite:ui_game_symbol_backpack||
row=2|view%7C12|AK-47|$500 / Seller / 2h|1|0|item:gunPistol|x1|
```

AuctionHouse uses the last form for listing rows and the held-item preview: an `item:<itemShortName>` icon plus a `x<count>` badge. The badge is the tooltip prefix and the amount is also visible in the subtitle.

If the action itself contains pipes, percent-encode them:

```text
row=0|iconset%7Cshared%7Cadmin-shop%7Citem:casinoCoin|Duke's Casino Tokens|item:casinoCoin|1|0|item:casinoCoin||
```

### Button fields

```text
btn=<index>|<action>|<label>|<enabled>|<tooltip>|<icon>|<tint>
```

| Field | Required | Meaning |
|---|---|---|
| `index` | Yes | 0–11 |
| `action` | Yes | Sent back as `a|<action>` |
| `label` | Yes | Button caption |
| `enabled` | Yes | `1` or `0` |
| `tooltip` | No | Hover text |
| `icon` | No | `item:`, `sprite:` or `atlas:` icon on the button |
| `tint` | No | HTML colour for the icon |

The five-field form is still valid. A 2.0 client parses only those five fields and treats the trailing icon/tint values as part of the tooltip; use a 2.1 client for button icons.

Example:

```text
btn=0|refresh|Refresh|1|Reload this view|sprite:sa_stream_admin_reload|
btn=1|close|Close|1|Close the window
```

`action=close` closes the window locally.

!!! note
    ServerAssistMod 2.1 parses all seven button fields. A 2.0 client parses only five, so the trailing icon and tint values appear as part of the tooltip. Update the client to 2.1 to render action-button icons; the five-field form remains valid and shows a text-only button.

### Launch field

```text
launch=<type>|<target>|<close>
```

| Field | Meaning |
|---|---|
| `type` | `window` or `command` |
| `target` | Window group name or command |
| `close` | `1` closes Server Assist first; `0` keeps it open |

The client treats `launch=` as a server-approved instruction and does not derive targets from local state.

For `window`:

- the target group opens modally;
- `serverAssist` itself is refused;
- an unknown group shows a local error.

For `command`:

- the leading `/` is stripped;
- the command runs through `SdtdConsole.Instance.ExecuteSync`;
- server-side command permissions still apply.

!!! warning
    Do not return a `launch=` line for a button the player is not allowed to use. The server is the authority for launch eligibility. A provider that emits a launch is responsible for checking its own permission first.

### Error views

Return a normal view with a row and a retry button rather than throwing. Server Assist catches exceptions and returns its own error view, but a clean view is easier to support.

```csharp
return "v2\n" +
       "title=" + Encode("Admin Shop") + "\n" +
       "subtitle=" + Encode("Unavailable") + "\n" +
       "status=" + Encode("The shop is not ready.") + "\n" +
       "row=0||" + Encode("No data") + "|" + Encode("Try again later.") + "|0|0\n" +
       "btn=0|refresh|" + Encode("Retry") + "|1\n";
```

---

## Encoding helpers

Use these in every companion plugin:

```csharp
private static string Encode(string value)
{
    if (string.IsNullOrEmpty(value))
    {
        return string.Empty;
    }

    return value
        .Replace("%", "%25")
        .Replace("|", "%7C")
        .Replace("\n", "%0A")
        .Replace("\r", string.Empty);
}

private static string Decode(string value)
{
    if (string.IsNullOrEmpty(value))
    {
        return string.Empty;
    }

    return value
        .Replace("%0A", "\n")
        .Replace("%7C", "|")
        .Replace("%25", "%");
}
```

Encode every dynamic string that is placed in a `title=`, `subtitle=`, `status=`, `row=` or `btn=` field. Never inject a raw `|` or newline into a field.

---
