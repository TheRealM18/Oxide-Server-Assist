# Item icon search

The button editor includes a built-in item icon picker. It searches the game's item catalog, previews the real icon and stores a stable `item:<name>` key.

Open it from the editor with **Find Item Icon**.

---

## What it searches

The picker enumerates:

```csharp
ItemClass.ItemNames
```

For each item name it matches the query against:

1. the internal item name (`gunPistol`, `resourceForgedSteel`, `casinoCoin`);
2. the localized display name (`Pistol`, `Forged Steel`, `Duke's Casino Tokens`).

Matching is case-insensitive substring matching. An empty query lists the catalog from the beginning.

!!! note
    The picker shows items that exist on the **server**. A client-only mod item may not be present in `ItemClass.ItemNames` on the server.

---

## Workflow

1. Open **Shared** or **My Buttons**.
2. Click a button card.
3. Click **Find Item Icon** (action 4 of the editor toolbar).
4. Type a search term in the prompt and submit.
5. Browse the results. Each card shows:

   | Part | Value |
   |---|---|
   | Label | Localized item name, falling back to the internal name |
   | Subtitle | `item:<internalName>` |
   | Icon | The live item icon |
   | Action | `iconset\|scope\|id\|item:<name>` |

6. Click a result. The icon is assigned, saved and the editor returns.
7. Or use **Clear Icon** to reset to the default.
8. Use **Back** to cancel without changing the icon.

---

## Paging

| Limit | Value |
|---|---|
| Results per page | 10 |
| Page buttons | `< Prev` and `Next >` when needed |
| Page state | Stored in the player session |

A search resets the page to 0. Paging does not change the query.

---

## Empty and no-match states

| State | Message |
|---|---|
| Empty query | `Showing common item icons.` |
| Query with matches | `Showing items matching '<query>'.` |
| No matches | `No items matched. Try a shorter search, or clear the search to browse.` |

A no-match screen still shows **Search Again**, **Clear Icon** and **Back** so the player is never trapped.

---

## What is stored

The picker stores:

```text
item:<internalName>
```

It does **not** store:

- a sprite path;
- an atlas coordinate;
- a localized name;
- an item id;
- a quality or mod variant.

This is intentional. The key follows game updates and mod icon changes. See [Icons](icons.md#live-item-icons).

---

## Search tips

| Goal | Query |
|---|---|
| Pistol | `pistol` |
| Forged steel | `forged` or `steel` |
| Dukes | `casino` or `duke` |
| First aid | `first aid` or `medical` |
| Ammo | `9mm` or `ammo` |
| Food | `food` |
| Mods | `mod` |
| A modded item | its internal name from the mod's items.xml |

If a display name has spaces, type part of it. The matcher is a substring search, not a tokenizer.

---

## Validation

When a result is clicked, the server calls `NormalizeIcon`:

```csharp
if (value.StartsWith("item:", ...))
{
    string itemName = value.Substring(5).Trim();
    if (string.IsNullOrEmpty(itemName) ||
        ItemClass.GetItemClass(itemName, true) == null)
    {
        // "Item not found."
    }

    return "item:" + itemName;
}
```

The click path uses a known item name from the catalog, so it normally cannot fail. Typing `item:name` manually can fail if the name is misspelled.

---

## Performance

- The catalog is read from `ItemClass.ItemNames`, an in-memory collection.
- Filtering is linear over the catalog for each search.
- Only 10 rows are sent per page.
- Each visible row resolves one `ItemClass` and one icon.
- The picker does not cache results between searches; the item catalog is the cache.

On a heavily modded server with tens of thousands of items, a very broad query can take a moment. Prefer a specific term.

---

## Related protocol fields

The `row=` line for a picker result looks like:

```text
row=0|iconset%7Cshared%7Cadmin-shop%7Citem:casinoCoin|Duke's Casino Tokens|item:casinoCoin|1|0|item:casinoCoin||
```

The action field contains its own pipe-separated arguments, so the pipes inside it are percent-encoded. Decoded, the action is:

```text
iconset|shared|admin-shop|item:casinoCoin
```

The row structure is the standard nine-field row:

```text
row=<index>|<action>|<label>|<sub>|<enabled>|<selected>|<icon>|<badge>|<tint>
```

Only the fields relevant to Server Assist's own renderer are used. A companion plugin can use the same icon fields in its own rows.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `No items matched.` | Query is too specific or the item is not on the server | Try a shorter term; check the item with `listitems` |
| The icon is a hammer after selecting | The client cannot resolve the item icon | Verify the item exists on the client |
| The picker is empty for a modded item | The mod is not loaded on the server | Load the mod and restart; `ItemClass.ItemNames` is built at startup |
| `Item not found.` when typing manually | Wrong internal name | Use the picker, or copy the exact name from `listitems` |
| The search is slow | Very broad query on a large catalog | Use a more specific term |

More: [Troubleshooting](../reference/troubleshooting/companions.md#item-icons).

---

## Related pages

- [Icons](icons.md)
- [Buttons](buttons.md)
- [Interface](interface.md)
- [API](../integration/api/index.md)
