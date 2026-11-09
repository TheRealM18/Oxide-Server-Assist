# Server Assist patch (applied in 2.1.0)

Status: applied in ServerAssist 2.1.0. The ServerAssist folder now contains both
patches. This document is kept as the reference for the wire format and the
public hooks.

There are two independent patches:

1. Read back panel icons (optional; PermissionGui now uses three category
   icons instead, so it does not call this).
2. Optional icons on action buttons (server plugin + client DLL + XUi XML).

---

## Why these are needed

- PermissionGui now uses a built-in plugin-to-sprite map. That covers every
  bundled companion plugin, but it cannot see a custom panel icon that a server
  registered at runtime.
- Server Assist action buttons currently have no icon field. PermissionGui's
  action buttons therefore stay text-only until patch 2 is applied.

---

## Patch 1 — read back panel/provider icons (optional)

This patch is not required by the current PermissionGui build. It is here in
case you later want a page to reuse a custom panel icon registered at runtime.

**File:** `oxide/plugins/ServerAssist.cs`

Add these two hooks after `SetProviderIcon`:

```csharp
[HookMethod("GetPanelIcon")]
public string GetPanelIcon(string id)
{
    if (string.IsNullOrEmpty(id))
    {
        return string.Empty;
    }

    GuiHook hook;
    if (guiHooks.TryGetValue(Clean(id, 48).ToLowerInvariant(), out hook) && hook != null)
    {
        return hook.Icon ?? string.Empty;
    }

    return string.Empty;
}

[HookMethod("GetPluginIcon")]
public string GetPluginIcon(string pluginName)
{
    if (string.IsNullOrEmpty(pluginName))
    {
        return string.Empty;
    }

    string name = pluginName.Trim();
    foreach (KeyValuePair<string, GuiHook> pair in guiHooks)
    {
        GuiHook hook = pair.Value;
        if (hook != null && string.Equals(hook.PluginName, name, StringComparison.OrdinalIgnoreCase))
        {
            return hook.Icon ?? string.Empty;
        }
    }

    foreach (KeyValuePair<string, GuiProvider> pair in guiProviders)
    {
        GuiProvider provider = pair.Value;
        if (provider != null && string.Equals(provider.PluginName, name, StringComparison.OrdinalIgnoreCase))
        {
            return provider.Icon ?? string.Empty;
        }
    }

    return string.Empty;
}
```

PermissionGui would then prefer the live icon and fall back to its built-in map:

```csharp
string icon = serverAssist.Call("GetPluginIcon", pluginName) as string;
```

No client change is required for patch 1.

---

## Patch 2 — optional icons on action buttons

### Wire format

```text
btn=<index>|<action>|<label>|<enabled>|<tooltip>|<icon>|<tint>
```

The icon and tint are optional. The five-field form stays valid.

### File: `src/ServerAssistView.cs`

Add the fields to `ServerAssistButton`:

```csharp
public string Icon = string.Empty;
public string IconTint = string.Empty;
```

Change the `btn` parser to split 7 fields:

```csharp
string[] parts = value.Split(new[] { '|' }, 7);
```

and add:

```csharp
Icon = parts.Length > 5 ? ServerAssistProtocol.Decode(parts[5]) : string.Empty,
IconTint = parts.Length > 6 ? ServerAssistProtocol.Decode(parts[6]) : string.Empty
```

### File: `src/ServerAssistWindow.cs`

Add arrays next to `actionHovers`:

```csharp
private readonly XUiV_Sprite[] actionIcons = new XUiV_Sprite[ButtonCount];
private readonly XUiV_Label[] actionLabels = new XUiV_Label[ButtonCount];
```

In `Init`, inside the action-button loop:

```csharp
actionIcons[i] = ChildView<XUiV_Sprite>(actionButtons[i], "icon");
actionLabels[i] = ChildView<XUiV_Label>(actionButtons[i], "btnLabel");
```

In the render loop, after `button.ViewComponent.IsVisible = true;`:

```csharp
string iconSprite = string.Empty;
Color iconTint = Color.white;
if (!string.IsNullOrEmpty(action.Icon))
{
    ResolveIcon(action.Icon, action.IconTint, out iconSprite, out iconTint);
}

if (actionIcons[i] != null)
{
    if (!string.IsNullOrEmpty(iconSprite))
    {
        actionIcons[i].SpriteName = iconSprite;
        if (!action.Enabled)
        {
            iconTint.a *= 0.45f;
        }

        actionIcons[i].Color = iconTint;
        actionIcons[i].IsVisible = true;
    }
    else
    {
        actionIcons[i].IsVisible = false;
    }
}

if (actionLabels[i] != null)
{
    bool hasIcon = !string.IsNullOrEmpty(iconSprite);
    actionLabels[i].Position = new Vector2i(hasIcon ? 30 : 0, -18);
    actionLabels[i].Size = new Vector2i(hasIcon ? 124 : 160, 34);
}
```

### File: `Config/XUi_InGame/windows.xml`

Inside every `btn0` … `btn11` rect, add this before the `btnLabel`:

```xml
<sprite depth="5" name="icon" pos="6,-6" width="24" height="24" pivot="topleft" sprite="ui_game_symbol_hammer" color="[white]" visible="false" />
```

### File: `oxide/plugins/ServerAssist.cs`

Add the fields to `ButtonInfo`:

```csharp
public string Icon = string.Empty;
public string IconTint = string.Empty;
```

Emit them in the button loop:

```csharp
builder.Append("btn=").Append(i).Append('|')
    .Append(Encode(button.Action)).Append('|')
    .Append(Encode(button.Label)).Append('|')
    .Append(button.Enabled ? "1" : "0").Append('|')
    .Append(Encode(button.Tooltip));

if (!string.IsNullOrEmpty(button.Icon))
{
    builder.Append('|')
        .Append(Encode(button.Icon)).Append('|')
        .Append(Encode(button.IconTint));
}

builder.Append('\n');
```

### Build and deploy

1. Rebuild `ServerAssistMod.dll` with `dotnet build ServerAssistMod.csproj`.
2. Bump the plugin and `ModInfo.xml` to `2.1.0`.
3. Ship the new DLL **and** `windows.xml` to every client.
4. Reload `ServerAssist` and `PermissionGui` on the server.

A client older than this patch will show the extra fields as part of the button
tooltip and will not draw a button icon.

---

## Note about this file name

`ServerAssist/update.md` already exists. Windows treats `UPDATE.md` and
`update.md` as the same file, so this proposal lives at the repository root.
