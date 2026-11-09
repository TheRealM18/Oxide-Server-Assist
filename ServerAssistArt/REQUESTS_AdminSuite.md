# AdminSuite icon request

> **Not active for the current batch.** The only current request is
> `ICON_REQUEST.md`, which asks for the 8 missing PlayerProfiles icons. Do not
> render this file's icons yet.

Status: **requested, not rendered**

Scope: **AdminSuite only**. Render only the 12 icons listed in this file. Do not
render the rest of `PLANNED_ICONS.md` for this request and do not create item
textures. Other plugins make their own requests.

The AdminSuite panel card icon `sa_admin_suite` already exists in the finished
set. Do not remake it.

AdminSuite currently uses these finished ServerAssistArt icons as placeholders.
They are acceptable until the requested art exists:

| Current AdminSuite use | Placeholder |
|---|---|
| Kill | `sprite:sa_act_ban` |
| Staff note | `sprite:sa_nav_help` |
| Clear inventory / delete item | `sprite:sa_nav_items` |
| Take all items | `sprite:sa_nav_items` |
| Jail | `sprite:sa_act_freeze` |
| Apply buff | `sprite:sa_act_godmode` |
| Remove buff | `sprite:sa_act_godmode` |
| Search items | `sprite:sa_nav_items` |
| Open creative menu | `sprite:sa_nav_items` |
| Rollback | `sprite:sa_nav_snapshots` |
| Save world | `sprite:sa_nav_server` |
| Spawn entity / wandering horde / scouts | `sprite:sa_nav_world` |

Do **not** create icons for spectate, snapshot, inventory inspect, backpack,
toolbelt, equipment, buy, sell, or item categories. The finished set already has
usable art for those, and item rows use live game item icons.

---

## Requested icons

| Sprite key | Master file | Used for | Subject |
|---|---|---|---|
| `sa_act_kill` | `icons/sa_act_kill.png` | Player actions -> Kill | Weathered skull or skull-and-crossbones, teal metal backing, bold dark outline |
| `sa_act_note` | `icons/sa_act_note.png` | Player staff tools -> Note | Folded parchment note or clipboard with a stub pencil, brass rivet |
| `sa_act_clear` | `icons/sa_act_clear.png` | Clear Inventory / Delete Item | Chunky metal trash bin or wipe symbol, red-painted slash, rusted rim |
| `sa_act_take` | `icons/sa_act_take.png` | Take All Items | Work-gloved hand pulling a small parcel toward itself |
| `sa_act_jail` | `icons/sa_act_jail.png` | Player staff tools -> Jail | Heavy iron cell bars with a brass lock plate and rivets |
| `sa_act_buff` | `icons/sa_act_buff.png` | Buffs -> Apply Buff | Upward golden sparkle over a small shield, teal metal backing |
| `sa_act_debuff` | `icons/sa_act_debuff.png` | Buffs -> Remove Buff | Cracked or downward shield with a dark red break, same materials |
| `sa_act_search` | `icons/sa_act_search.png` | Items -> Search Items | Magnifying glass over a small blank tag or item silhouette |
| `sa_act_creative` | `icons/sa_act_creative.png` | Items -> Open Creative Menu | Paintbrush and small palette, or a crate tagged with a star |
| `sa_act_rollback` | `icons/sa_act_rollback.png` | Player staff tools -> Rollback | Circular rewind arrow with a small clock hand, teal metal |
| `sa_act_save` | `icons/sa_act_save.png` | World -> Save World | Floppy disk or save stamp with a check mark, brass corner rivets |
| `sa_act_spawn` | `icons/sa_act_spawn.png` | Spawn Entity / Wandering Horde / Scouts | Zombie claw rising from a cracked ground portal, green-teal glow |

---

## Style and format

Use the existing ServerAssistArt style:

> 7 Days to Die post-apocalyptic UI icon, hand-painted weathered metal, chipped
> teal paint, rust, scratches, bold dark outline, strong rim light, high
> contrast, centered subject, no text.

Technical requirements:

- Final PNG: 128 x 128, transparent background
- Folder: `ServerAssistArt/UIAtlases/UIAtlas/`
- File name must exactly match the sprite key, for example
  `sa_act_kill.png`
- Must remain readable at 24 x 24 in AdminSuite list rows
- Do not rename, overwrite or recolor existing PNGs
- Do not copy the icons into AdminSuite or any other mod

## After the art is added

1. Drop the 12 PNGs into `ServerAssistArt/UIAtlases/UIAtlas/`.
2. Confirm every key in the table resolves as `sprite:<key>`.
3. Leave AdminSuite source unchanged. The plugin will switch from the
   placeholder keys to these keys in a separate pass.

## Why these 12

These are the only AdminSuite rows where the current placeholder is visually
misleading: Kill shares the ban hammer, Jail shares the freeze snowflake,
Apply/Remove Buff shares godmode, clear/take share the generic item chest, and
save/spawn share navigation icons. The other rows already match their art well
enough or use live item textures.
