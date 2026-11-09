# ServerAssistMod Patch: item icon atlas resolution

> Location note: this file lives in `AdminSuite` because AdminSuite's item icons
> depend on this ServerAssistMod fix. The runtime change itself is in the
> `ServerAssist` project. Paths below are relative to
> `C:\Users\robert\Desktop\7 Days 2 Die Mods\Oxide\ServerAssist` unless stated
> otherwise.

Status: required for AdminSuite item icons and any other plugin that sends
`item:<internalName>` icons.

Assume this patch is **not applied** unless the detection step below says
otherwise. Apply only the changes in this file. Do not reformat or rewrite the
surrounding code, and do not copy item textures anywhere.

Apply patches 1 through 5 as one set. The project is not expected to compile
between the `ResolveIcon` change and the `ApplyPanelImage` change; build only
after all five patches are in place.

## Why this patch exists

`ServerAssistMod` already resolves:

```csharp
ItemClass.GetItemClass(itemName, true)
ItemClass.GetIconName()
```

but it only assigned `XUiV_Sprite.SpriteName`. It never assigned
`XUiV_Sprite.UIAtlas`. XUi sprites default to the `UIAtlas` manager, while item
textures live in the `ItemIconAtlas` manager.

Consequences before the patch:

- Vanilla items looked for sprites such as `casinoCoin` in `UIAtlas` and failed.
- Modded items looked for sprites such as `modArkBattery` in `UIAtlas` and
  failed.
- The icon view kept its default hammer.
- Mod authors were forced to duplicate textures into another atlas.

After the patch:

- `item:` icons resolve through `ItemIconAtlas`.
- Base-game `Data/ItemIcons` sprites and mod sprites under
  `Mods/<mod>/UIAtlases/ItemIconAtlas` work with no copies.
- If a sprite is in a different loaded atlas manager, the code checks the other
  managers and uses the one that actually contains the sprite.
- `sprite:` icons continue to use `UIAtlas`.
- `atlas:<atlasName>:<spriteName>` continues to work and still auto-checks the
  requested atlas first.

AdminSuite already sends `item:<internalName>`. It does not need to be patched
for this fix.

## Files to patch

| File | Change |
| --- | --- |
| `src/ServerAssistWindow.cs` | Add atlas output, atlas selection, and set `UIAtlas` before `SpriteName` |
| `ServerAssistMod.csproj` | Add a reference to `NGUI.dll` |
| `ModInfo.xml` | Bump version from `2.0.1` to `2.0.2` |

Optional non-runtime docs: `README.txt` and `API.md`. Updating them is safe but
not required for the fix.

## Detection: is the patch already applied?

Run from the `ServerAssist` folder:

```powershell
Select-String -Path "src\ServerAssistWindow.cs" -Pattern "UIAtlas = atlas|BestAtlas|ItemAtlasFor"
```

If results are found, the patch is already applied. Stop. Do not apply it twice.

If no results are found, continue.

## Patch 1: row icon rendering

File: `src/ServerAssistWindow.cs`

Find this block:

```csharp
            string sprite;
            Color tint;
            ResolveIcon(row.Icon, row.IconTint, out sprite, out tint);
            if (!row.Enabled)
            {
                tint.a *= 0.45f;
            }

            if (rowIcons[i] != null)
            {
                rowIcons[i].SpriteName = sprite;
                rowIcons[i].Color = tint;
                rowIcons[i].IsVisible = true;
            }
```

Replace it with:

```csharp
            string sprite;
            string atlas;
            Color tint;
            ResolveIcon(row.Icon, row.IconTint, out sprite, out atlas, out tint);
            if (!row.Enabled)
            {
                tint.a *= 0.45f;
            }

            if (rowIcons[i] != null)
            {
                rowIcons[i].UIAtlas = atlas;
                rowIcons[i].SpriteName = sprite;
                rowIcons[i].Color = tint;
                rowIcons[i].IsVisible = true;
            }
```

## Patch 2: panel banner/background rendering

File: `src/ServerAssistWindow.cs`

Change the method signature from:

```csharp
    private static void ApplyPanelImage(string key, XUiV_Sprite view, float alpha)
```

to:

```csharp
    private void ApplyPanelImage(string key, XUiV_Sprite view, float alpha)
```

Then find:

```csharp
        string sprite;
        Color color;
        ResolveIcon(key, string.Empty, out sprite, out color);
        if (string.IsNullOrEmpty(sprite))
        {
            view.IsVisible = false;
            return;
        }

        view.SpriteName = sprite;
        color.a = alpha;
        view.Color = color;
        view.IsVisible = true;
    }
```

Replace it with:

```csharp
        string sprite;
        string atlas;
        Color color;
        ResolveIcon(key, string.Empty, out sprite, out atlas, out color);
        if (string.IsNullOrEmpty(sprite))
        {
            view.IsVisible = false;
            return;
        }

        view.UIAtlas = atlas;
        view.SpriteName = sprite;
        color.a = alpha;
        view.Color = color;
        view.IsVisible = true;
    }
```

The `static` removal is required because the resolver reads the window's `xui`
field to inspect loaded atlas managers.

## Patch 3: replace `ResolveIcon`

File: `src/ServerAssistWindow.cs`

Find the complete method:

```csharp
    private static void ResolveIcon(string icon, string tint, out string sprite, out Color color)
    {
        sprite = FallbackIcon;
        color = Color.white;

        if (string.IsNullOrEmpty(icon))
        {
            return;
        }

        string value = icon.Trim();
        if (value.StartsWith("item:", StringComparison.OrdinalIgnoreCase))
        {
            string itemName = value.Substring(5).Trim();
            ItemClass itemClass = ItemClass.GetItemClass(itemName, true);
            if (itemClass != null)
            {
                string iconName = itemClass.GetIconName();
                if (!string.IsNullOrEmpty(iconName))
                {
                    sprite = iconName;
                }
            }
        }
        else if (value.StartsWith("sprite:", StringComparison.OrdinalIgnoreCase))
        {
            string spriteName = value.Substring(7).Trim();
            if (!string.IsNullOrEmpty(spriteName))
            {
                sprite = spriteName;
            }
        }
        else if (value.StartsWith("atlas:", StringComparison.OrdinalIgnoreCase))
        {
            string atlasValue = value.Substring(6);
            int separator = atlasValue.LastIndexOf(':');
            string spriteName = (separator >= 0 ? atlasValue.Substring(separator + 1) : atlasValue).Trim();
            if (!string.IsNullOrEmpty(spriteName))
            {
                sprite = spriteName;
            }
        }
        else if (!string.IsNullOrEmpty(value))
        {
            sprite = value;
        }

        if (string.IsNullOrEmpty(sprite))
        {
            sprite = FallbackIcon;
        }

        if (!string.IsNullOrEmpty(tint) && !ColorUtility.TryParseHtmlString(tint, out color))
        {
            color = Color.white;
        }
    }
```

Replace it with:

```csharp
    private void ResolveIcon(string icon, string tint, out string sprite, out string atlas, out Color color)
    {
        sprite = FallbackIcon;
        atlas = "UIAtlas";
        color = Color.white;

        if (string.IsNullOrEmpty(icon))
        {
            return;
        }

        string value = icon.Trim();
        if (value.StartsWith("item:", StringComparison.OrdinalIgnoreCase))
        {
            string itemName = value.Substring(5).Trim();
            ItemClass itemClass = ItemClass.GetItemClass(itemName, true);
            if (itemClass != null)
            {
                string iconName = itemClass.GetIconName();
                if (!string.IsNullOrEmpty(iconName))
                {
                    sprite = iconName;
                    atlas = BestAtlas(ItemAtlasFor(iconName), iconName);
                }
            }
        }
        else if (value.StartsWith("sprite:", StringComparison.OrdinalIgnoreCase))
        {
            string spriteName = value.Substring(7).Trim();
            if (!string.IsNullOrEmpty(spriteName))
            {
                sprite = spriteName;
                atlas = BestAtlas("UIAtlas", spriteName);
            }
        }
        else if (value.StartsWith("atlas:", StringComparison.OrdinalIgnoreCase))
        {
            string atlasValue = value.Substring(6);
            int separator = atlasValue.LastIndexOf(':');
            string atlasName = (separator >= 0 ? atlasValue.Substring(0, separator) : string.Empty).Trim();
            string spriteName = (separator >= 0 ? atlasValue.Substring(separator + 1) : atlasValue).Trim();
            if (!string.IsNullOrEmpty(atlasName))
            {
                atlas = atlasName;
            }

            if (!string.IsNullOrEmpty(spriteName))
            {
                sprite = spriteName;
                atlas = BestAtlas(atlas, spriteName);
            }
        }
        else if (!string.IsNullOrEmpty(value))
        {
            sprite = value;
            atlas = BestAtlas("UIAtlas", sprite);
        }

        if (string.IsNullOrEmpty(sprite))
        {
            sprite = FallbackIcon;
            atlas = "UIAtlas";
        }

        if (!string.IsNullOrEmpty(tint) && !ColorUtility.TryParseHtmlString(tint, out color))
        {
            color = Color.white;
        }
    }

    private string BestAtlas(string preferred, string spriteName)
    {
        string fallback = string.IsNullOrEmpty(preferred) ? "UIAtlas" : preferred;
        if (string.IsNullOrEmpty(spriteName) || xui == null)
        {
            return fallback;
        }

        if (AtlasHasSprite(fallback, spriteName))
        {
            return fallback;
        }

        try
        {
            foreach (var pair in xui.allMultiSourceAtlases)
            {
                if (pair.Value != null && AtlasHasSprite(pair.Key, spriteName))
                {
                    return pair.Key;
                }
            }
        }
        catch
        {
            // Fall through to the preferred atlas.
        }

        return fallback;
    }

    private bool AtlasHasSprite(string atlasName, string spriteName)
    {
        if (xui == null || string.IsNullOrEmpty(atlasName) || string.IsNullOrEmpty(spriteName))
        {
            return false;
        }

        try
        {
            INGUIAtlas atlas = xui.GetAtlasByName(atlasName, spriteName);
            return atlas != null && atlas.GetSprite(spriteName) != null;
        }
        catch
        {
            return false;
        }
    }

    private static string ItemAtlasFor(string iconName)
    {
        if (string.IsNullOrEmpty(iconName))
        {
            return "UIAtlas";
        }

        string value = iconName.Trim();
        if (value.StartsWith("ui_", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("sa_", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("menu_", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("symbol_", StringComparison.OrdinalIgnoreCase))
        {
            return "UIAtlas";
        }

        // 7 Days to Die stores item art in the ItemIconAtlas manager. Mods can
        // add their own item PNGs to the same folder and they resolve here
        // without copying or re-registering the texture.
        return "ItemIconAtlas";
    }
```

## Patch 4: add the NGUI reference

File: `ServerAssistMod.csproj`

`INGUIAtlas` and `XUi.GetAtlasByName` signatures come from `NGUI.dll`. Without
this reference the project fails with a type-resolution error.

Find:

```xml
    <Reference Include="Assembly-CSharp">
      <HintPath>$(GameDir)\7DaysToDie_Data\Managed\Assembly-CSharp.dll</HintPath>
      <Private>false</Private>
    </Reference>
```

Add immediately after it:

```xml
    <Reference Include="NGUI">
      <HintPath>$(GameDir)\7DaysToDie_Data\Managed\NGUI.dll</HintPath>
      <Private>false</Private>
    </Reference>
```

## Patch 5: version bump

File: `ModInfo.xml`

Find:

```xml
  <Version value="2.0.1" />
```

Replace with:

```xml
  <Version value="2.0.2" />
```

If the version is already newer than `2.0.2`, leave it alone.

## Build

From the `ServerAssist` folder:

```powershell
dotnet build "C:\Users\robert\Desktop\7 Days 2 Die Mods\Oxide\ServerAssist\ServerAssistMod.csproj" -v q --nologo
```

Expected result:

```text
Build succeeded.
    0 Error(s)
```

Do not continue if there are compile errors.

## Deploy

Server pack:

```powershell
powershell -ExecutionPolicy Bypass -File "C:\Users\robert\Desktop\7 Days 2 Die Mods\Oxide\ServerAssist\deploy-to-pack.ps1"
```

Default destination:

```text
C:\Users\robert\Desktop\Server Mods\Mods\AAY_ServerAssistMod
```

Local client mods folder:

```text
C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die\Mods\AAY_ServerAssistMod
```

Important: `ServerAssistMod.dll` is locked while `7DaysToDie` is running. If the
copy fails with:

```text
The requested operation cannot be performed on a file with a user-mapped section open.
```

close the game, copy the files again, then relaunch. Do not delete the old DLL
while the game is running.

## Verification

1. Close and relaunch 7 Days to Die after the DLL is copied.
2. Open ServerAssist and a menu that shows item rows, such as AdminSuite ->
   Items -> Search, or a player's inventory.
3. Confirm Duke's coin shows `casinoCoin.png`, and modded items such as the Ark
   battery/solar/water items show their own ARK icons.
4. Confirm `sprite:sa_*` AdminSuite art icons still show.
5. Confirm no new texture files were added to any mod.

## Hard constraints for the patching agent

- Do not copy, rename, re-export or duplicate item textures.
- Do not hard-code a single atlas for all icons. `sprite:` uses `UIAtlas`;
  `item:` prefers `ItemIconAtlas` and falls back to whichever loaded manager
  actually contains the sprite.
- Do not change the wire protocol or AdminSuite's `item:<internalName>` keys.
- Do not rewrite `ResolveIcon` call sites other than the two shown above.
- If the detection step finds `BestAtlas`, assume the patch is already live.
  Verify the build and deployment instead of applying it again.

## Files/agents this patch affects

- `ServerAssistMod` client DLL and its source.
- Any server-side plugin that sends `item:<internalName>` row icons, including
  AdminSuite.
- Mods that ship item PNGs under `UIAtlases/ItemIconAtlas` or the base game
  `Data/ItemIcons` folder.

It does not affect AdminSuite's plugin source, config format, permissions, or
network messages.
