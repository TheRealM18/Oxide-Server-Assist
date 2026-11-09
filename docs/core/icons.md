# Icons and art

Every Server Assist card has a 64×64 icon. Icons are stored as **keys**, not baked sprite paths, so a game update or a texture change does not invalidate the button data. The same syntax is used by built-in buttons, hook rows and provider rows.

---

## Icon syntax

| Syntax | Meaning | Example |
|---|---|---|
| `item:<itemName>` | Live icon of a game item | `item:gunPistol` |
| `sprite:<spriteName>` | A vanilla or loaded UI sprite | `sprite:ui_game_symbol_map` |
| `atlas:<atlasName>:<spriteName>` | A sprite that belongs to a custom atlas | `atlas:ServerAssistAtlas:permissions` |
| `<plainName>` | Treated as `sprite:<plainName>` | `ui_game_symbol_hammer` |
| *(blank)* | Use the default icon for the target | — |
| `auto` | Same as blank | — |

The server normalizes plain names to `sprite:` and stores the normalized value. The client accepts all four forms.

---

## Live item icons

```text
item:gunPistol
item:resourceForgedSteel
item:casinoCoin
item:medicalFirstAidBandage
```

Resolution on the server:

```csharp
ItemClass itemClass = ItemClass.GetItemClass(itemName, true);
```

If the item does not exist, the editor rejects it with `Item not found.` The stored value remains `item:<itemName>`.

Resolution on the client:

```csharp
ItemClass itemClass = ItemClass.GetItemClass(itemName, true);
string sprite = itemClass.GetIconName();
```

The client uses the game's current icon for that item. This is why the key is better than a path:

- A game update can rename or re-skin an icon; the key still resolves.
- A mod can change an item's icon; the key follows it.
- The same button data works across clients with the same item set.

Item sprites are drawn from `ItemIconAtlas`, which covers the base game's `Data/ItemIcons` textures and any mod that adds PNGs under `Mods/<mod>/UIAtlases/ItemIconAtlas`. If the sprite is not in that atlas, Server Assist checks the other loaded atlas managers and uses the one that contains it. Mod authors do not need to duplicate textures or register a second icon.

!!! note
    The item must exist on the **server** for the editor to accept it, and on the **client** for the icon to render. A client missing a server-side mod item gets the fallback icon.

---

## Sprites

```text
sprite:ui_game_symbol_map
sprite:ui_game_symbol_players
sprite:ui_game_symbol_backpack
sprite:ui_game_symbol_hammer
```

Use sprites for generic actions and UI concepts. They are cheap to resolve and do not depend on the item database.

Common vanilla symbols:

| Sprite | Usual meaning |
|---|---|
| `ui_game_symbol_hammer` | Generic tool, command or fallback |
| `ui_game_symbol_players` | Players / group |
| `ui_game_symbol_map` | Map, travel, vote |
| `ui_game_symbol_backpack` | Backpack |
| `ui_game_symbol_shop` | Shop, if present in the build |
| `ui_game_symbol_lock` | Locked or staff-only |
| `ui_game_symbol_info` | Info or help |

Sprite availability changes between game versions. If a sprite is missing, the client falls back to the hammer.

---

## Custom atlases

```text
atlas:ServerAssistAtlas:permissions
atlas:MyUiAtlas:kit
```

The client resolves the **last** colon-separated segment as the sprite name:

```text
atlas:ServerAssistAtlas:permissions  ->  permissions
```

The atlas name is metadata for your pack and for future atlas loading. Server Assist does not load an atlas that the client does not already have. Ship the atlas with the client mod that owns it, or use a sprite that the game has already loaded.

!!! important
    `atlas:Name:Sprite` is not a promise that `Name` will be mounted. It identifies a sprite that should be available on the client. If the sprite is not registered, the icon falls back.

---

## Default icons

When `Icon` is blank, the server picks a default based on the button:

| Condition | Default |
|---|---|
| Target type `command` | `ui_game_symbol_hammer` |
| Window target `map` | `ui_game_symbol_map` |
| Anything else | `ui_game_symbol_hammer` |

Hook rows default to `ui_game_symbol_hammer` unless the panel registers an icon. The DynamicBackpack panel registers `sprite:sa_dynamic_backpack` for its card, and its item cards use `item:<itemName>` with `x<count>` badges.

---

## Tint

`IconTint` applies an HTML color to the icon sprite:

```text
#RRGGBB
#RRGGBBAA
```

Examples:

| Tint | Effect |
|---|---|
| `#FFCC66` | Warm gold |
| `#66CCFF` | Teal-blue |
| `#FF6666` | Red warning |
| `#66FF99` | Green |
| `#FFFFFF80` | Half-transparent white |

Rules:

- Parsed with `ColorUtility.TryParseHtmlString`.
- An invalid value becomes white.
- Alpha is honored.
- A disabled row multiplies the final alpha by `0.45`, so tint and disabled state compose.
- Tint applies to the icon only; labels keep the standard white/grey/gold palette.

Tint is useful for categorizing buttons without making ten custom sprites:

```text
Shop      #FFD166
Kits      #66CCFF
Votes     #A78BFA
Moderation#FF7A7A
```

---

## Action-button icons

Action buttons use the same `item:`, `sprite:`, `atlas:` and plain-name syntax. Add an icon and optional tint after the tooltip:

```text
btn=0|refresh|Refresh|1|Reload this view|sprite:sa_stream_admin_reload|
btn=1|clear|Clear|1|Clear the filter|sprite:sa_stream_link_cancel|#FF8888
```

- The client draws a 24×24 icon at the left of the button.
- A button without an icon hides the icon view and uses the full text area.
- A disabled button fades the icon to 45% alpha.
- The five-field form is still valid and produces a text-only button.
- ServerAssistMod 2.1 parses the icon and tint fields. A 2.0 client parses five fields and treats the trailing values as tooltip text.

---

## Badges

A badge is short metadata attached to a row. The client uses it as a tooltip prefix:

```text
<badge>  |  <subtitle>
```

Common badges:

| Badge | Used by |
|---|---|
| `PLUGIN` | Hook rows and panel rows |
| `PERSONAL` | Personal buttons on Home |
| `ADMIN` | Custom provider rows |
| `LOCKED` | Custom provider rows |
| `NEW` | Custom provider rows |
| `OFF` | Disabled buttons in managers |
| `PANEL` | Example panels in the API docs |

Badges are optional and provider-defined. The built-in rows use `PLUGIN`, `PERSONAL` and `OFF`.

!!! note
    In the current window, the badge is not a separately drawn pill. It is prepended to the card tooltip. If you need a visible pill, use a short label prefix such as `[LOCKED]` and keep the badge for hover metadata. This keeps the protocol stable.

---

## Fallback chain

```text
1. Parse the icon key.
2. item:  -> resolve ItemClass -> GetIconName()
3. sprite: -> use the sprite name
4. atlas: -> use the segment after the last colon
5. plain  -> use the string as a sprite name
6. If the sprite is null/empty or the key is invalid
      -> ui_game_symbol_hammer
7. If the tint is invalid
      -> white
```

The fallback is deliberately boring: the board stays usable even when a custom atlas, item or mod is missing.

---

## Icon picker

The editor's **Find Item Icon** action opens the item picker:

- Search by internal name or localized name.
- Preview the actual game icon.
- Click a result to assign `item:<name>`.
- **Clear Icon** resets to the default.

Full details: [Item Search](item-search.md).

---

## Authoring guidance

### Prefer item icons for rewards and products

```text
item:casinoCoin
item:medicalFirstAidBandage
item:gunPistol
```

They are recognizable and automatically match the game's art.

### Prefer sprites for actions

```text
sprite:ui_game_symbol_map
sprite:ui_game_symbol_players
sprite:ui_game_symbol_backpack
```

Actions are concepts, not objects. A sprite is more stable than an arbitrary item icon.

### Use tint for categories

A single hammer sprite with four tints reads as four categories and costs no extra assets.

### Keep the label readable

The icon is 64×64 and the label is 20 px. Avoid relying on a highly detailed icon to carry meaning; the label should still make sense if the icon falls back.

### Validate after a game update

Run the editor on a test client:

1. Open each custom icon.
2. Confirm the preview is not the hammer fallback.
3. Check `item:` names against the current item list.
4. Re-check any custom atlas sprite.

---

## Examples

| Button | Icon | Tint |
|---|---|---|
| Admin Shop | `item:casinoCoin` | `#FFD166` |
| Admin Kits | `sprite:ui_game_symbol_backpack` | `#66CCFF` |
| Vote Skip Night | `sprite:ui_game_symbol_map` | `#A78BFA` |
| Permission Manager | `atlas:ServerAssistAtlas:permissions` | `#66CCFF` |
| Daily Rewards | `item:casinoCoin` | `#66FF99` |
| Moderation | `sprite:ui_game_symbol_lock` | `#FF7A7A` |
| A custom command | `sprite:ui_game_symbol_hammer` | — |

---

## Troubleshooting icons

| Symptom | Likely cause | Fix |
|---|---|---|
| Hammer appears instead of the item | Client does not have the item or its icon | Check the item on the client; verify the mod is installed |
| `Item not found.` in the editor | The server cannot resolve the item name | Use **Find Item Icon** or the exact internal name |
| Custom atlas icon is a hammer | Sprite not loaded on the client | Ship/register the atlas or use a vanilla sprite |
| Tint does nothing | Invalid color string | Use `#RRGGBB` or `#RRGGBBAA` |
| Action button icon does not appear | Client is older than 2.1 | Update `ServerAssistMod`; the 2.0 client parses only five button fields |
| Action button tooltip shows `\|sprite:...\|#...` | Server sends seven fields to a 2.0 client | Update the client or emit the five-field button form |
| Icon is very dark | Tint alpha or disabled state | Check the alpha and `Enabled` |
| Icon changes after a game update | Item icon was re-skinned | Expected; the key still resolves |

More: [Troubleshooting](../reference/troubleshooting/companions.md#icons-and-item-icons).

---

## Related pages

- [Item Search](item-search.md)
- [Buttons](buttons.md)
- [API](../integration/api/index.md)
- [Interface](interface.md)
