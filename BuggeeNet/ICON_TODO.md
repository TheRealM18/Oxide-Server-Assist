# BuggeeNet icon request — ServerAssistArt

> ## Not active for the current batch
>
> The only current request is `ServerAssistArt/ICON_REQUEST.md`, which asks for
> the 8 missing PlayerProfiles icons. BuggeeNet already works with its interim
> aliases. Do not render these 4 icons yet; keep this file for the next batch.
>
> ## ART AGENT: render exactly 4 icons when this file is active
>
> Only the four sprites in this file are referenced by `BuggeeCore`.
> This **supersedes the 15-icon BungeeNet block** in
> `ServerAssistArt/ICON_PRIORITY.md`.
> Do **not** render the other 11 names — they have no consumer.

Status: the panel is live with interim aliases. The unique `sa_buggee_*` art is
not made yet. `BuggeeCore.SaIcons` currently points at `sa_stream_*` /
`sa_nav_*` so the panel is usable today (`AAZ_ServerAssistArt-1.2.0` ships those
aliases).

Do not edit `BuggeeCore.cs` until the new PNGs exist. When they do, change only
the four `SaIcons` constants.

---

## Why exactly four

The panel asks the client for four sprite keys, and every row, banner and
background reuses one of them. Buttons are not a factor: ServerAssist's client
parser reads only `index|action|label|enabled|tooltip` for a `btn=` line, so
button icons are never drawn. That is why no `sa_act_*` art is needed here.

| # | New sprite key | Replaces today | Used for | Refs |
|---|---|---|---|---|
| 1 | `sprite:sa_buggee_panel` | `sprite:sa_stream_link` | Panel card icon, banner/background on Overview, Servers and Confirm, generic-server rows | 7 |
| 2 | `sprite:sa_buggee_home` | `sprite:sa_stream_home` | "This server" row and every `home`-role row | 2 |
| 3 | `sprite:sa_buggee_info` | `sprite:sa_stream_info` | Detail rows: confirm (role, players, mod set), diagnostics (Redis, endpoint, prefix, registry, version, hash) | 10 |
| 4 | `sprite:sa_buggee_warn` | `sprite:sa_nav_help` | "No peers" rows, error banner, `horde`-role rows | 5 |

`sa_nav_help` must be replaced: it is a question-mark sign, but this slot means
warning/danger, and it is currently what the horde server shows.

---

## Icon briefs

### 1. `sa_buggee_panel`

Three rugged server racks joined into a mesh, with a central hub and short
signal arcs or cables between the nodes. It is the BuggeeNet brand mark.

- Must also work as a **large, low-opacity background watermark** behind the
  cards, so keep one bold silhouette and avoid fine detail in the outer 12 px.
- No text, no logos, no arrows.
- Full colour, no tint applied.

### 2. `sa_buggee_home`

A server rack with a small house silhouette or shield emblem bolted to the
front. Should read as "safe base / home server" next to the generic network
mark, not as a literal house.

- Full colour, no tint applied.
- Must stay distinguishable from `sa_buggee_panel` at 64×64.

### 3. `sa_buggee_info`

A stamped metal information placard: riveted border, a single raised dot glyph
(not a letter), slightly angled. This is the generic detail-row icon, so it
must look neutral rather than warning-shaped.

- Used on rows that receive a **grey tint** (`#9AA0A6`), so keep the master
  mostly desaturated metal. Do not bake in a strong hue.
- No text or lettering.

### 4. `sa_buggee_warn`

A warning triangle fused with a snapped link/cable, or a broken server node.
Amber core with a dark outline.

- Used on rows tinted **amber** (`#FFD166`) and **red** (`#FF6B6B`), so the
  base art must survive a tint. Keep the colour accents light.
- Also shown for the `horde` role, so "danger / blood moon" fits.
- No text or lettering.

---

## Shared style

7 Days to Die post-apocalyptic UI icon, hand-painted weathered metal, chipped
teal paint, rust, scratches, bold dark outline, strong rim light, high contrast,
chunky readable silhouette, slightly isometric three-quarter view, centered, no
text, fully transparent background, square 1024×1024, readable at 64×64.

Match the existing `sa_*` pack. The art is already coloured; leave `IconTint`
blank for the intended look.

---

## Format and acceptance

- [ ] 128×128, 32-bit ARGB PNG, transparent background.
- [ ] Auto-trimmed and re-centred with a 4 px margin, like the rest of the pack.
- [ ] Readable at 64×64 (the card size) and usable as a watermark for
      `sa_buggee_panel`.
- [ ] Exact filenames: `sa_buggee_panel.png`, `sa_buggee_home.png`,
      `sa_buggee_info.png`, `sa_buggee_warn.png`.
- [ ] Packaged under `AAZ_ServerAssistArt/UIAtlases/UIAtlas/`.
- [ ] Added to `tools/build-icons.ps1` and the contact sheet.
- [ ] Sprite names never include `.png` in code.
- [ ] One 2×2 sheet covers all four.

---

## Integration after the PNGs land

Change only these four lines in `BuggeeNet/oxide/plugins/BuggeeCore.cs`:

```csharp
private static class SaIcons
{
    public const string Panel = "sprite:sa_buggee_panel"; // was sprite:sa_stream_link
    public const string Home  = "sprite:sa_buggee_home";  // was sprite:sa_stream_home
    public const string Info  = "sprite:sa_buggee_info";  // was sprite:sa_stream_info
    public const string Warn  = "sprite:sa_buggee_warn";  // was sprite:sa_nav_help
    ...
}
```

`IconForRole` keeps reusing the same four:

- `home` → `Home`
- `horde` → `Warn`
- anything else → `Panel`

No config, YAML or ServerAssist changes are required.

---

## Do NOT render — 11 names deliberately skipped

These appear in the older 15-icon block and must not be produced. Each has a
working substitute or no consumer.

| Sprite | Why it is not needed |
|---|---|
| `sa_buggee_horde` | Horde rows reuse `sa_buggee_warn`; a warning mark reads correctly for a blood-moon server. |
| `sa_buggee_server` | Generic peers reuse `sa_buggee_panel`. |
| `sa_buggee_fleet` | The Servers screen reuses the panel banner; buttons draw no icons. |
| `sa_buggee_travel` | Confirm rows use the target's role icon (`home`/`horde`/`panel`). |
| `sa_buggee_confirm` | Same as travel — the action is a button, and buttons have no icon. |
| `sa_buggee_diag` | The diagnostics screen reuses `sa_buggee_info`. |
| `sa_buggee_error` | Errors reuse `sa_buggee_warn` with a red tint. |
| `sa_buggee_you` | The `YOU` badge already marks the local server; the icon is `sa_buggee_home`. |
| `sa_buggee_players` | The Players row reuses `sa_buggee_info`. |
| `sa_buggee_redis` | The Redis row reuses `sa_buggee_info`. |
| `sa_buggee_mods` | The mod-set row reuses `sa_buggee_info`. |

If any of these becomes genuinely necessary later, add the constant to
`SaIcons` first, then request the art. Do not pre-render them.

---

## Summary

**4 icons. One 2×2 sheet. Four `SaIcons` constants. Nothing else.**

See `ServerAssistArt/ICON_PRIORITY.md` for the global budget; this file is the
authoritative BungeeNet scope.
