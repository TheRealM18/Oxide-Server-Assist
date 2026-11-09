# API and data migration

Part of the [Migration guide](index.md).

## API mapping

### Plugin lookup

Old:

```csharp
Plugin plugin = plugins.Find("AdminHelper");
```

New:

```csharp
Plugin plugin = plugins.Find("ServerAssist");
```

### Ready hook

Old:

```csharp
private void OnAdminHelperReady()
{
    RegisterMyHook();
}
```

New:

```csharp
private void OnServerAssistReady()
{
    RegisterMyPanel();
}
```

### Home-row registration

| Old call | New call |
|---|---|
| `RegisterGuiHook(...)` | `RegisterPanel(...)` preferred, or `RegisterGuiHook(...)` |
| `UnregisterGuiHook(id)` | `UnregisterPanel(id)` preferred, or `UnregisterGuiHook(id)` |
| `OpenHookForPlayer(playerId, id)` | `OpenPanel(playerId, id, "")` preferred, or `OpenHookForPlayer(...)` |
| `OpenHookForPlayer(playerId, id, action)` | `OpenPanel(playerId, id, action)` preferred, or `OpenHookForPlayerWithAction(...)` |

`RegisterPanel` adds an icon and an explicit order:

```csharp
serverAssist.Call(
    "RegisterPanel",
    "voteskip",
    "Vote: Skip Night",
    "Start or join the night-skip vote",
    "voteskip.use",
    "VoteSkipNight",
    "HandleVoteSkipRequest",
    "ui_game_symbol_map",
    20);
```

### Provider/tab API

These remain valid under Server Assist:

```csharp
RegisterGuiProvider(providerId, pluginName, handlerMethod,
                    title, tab1, tab2, tab3);
OpenGuiProvider(playerId, providerId, payload);
UnregisterGuiProvider(providerId);
```

Provider handlers receive the original payload:

```text
a|open
a|buy|12
t|<encoded text>
```

### Handler signature

Unchanged:

```csharp
public string HandleGuiRequest(string playerId, string payload)
```

The return protocol is the same line protocol, now with optional icon fields.

### Reflection

The preferred methods are marked `[HookMethod]`, so `Plugin.Call` works. Legacy companions that used reflection for `OpenHookForPlayer(playerId, id, subAction)` can keep the reflection helper; the overload is retained as a public method.

---

## View protocol additions

The new client understands these optional row fields:

```text
row=index|action|label|sub|enabled|selected|icon|badge|tint
```

Examples:

```text
row=0|launch|shared|shop|Shop|Buy and sell items|1|0|ui_game_symbol_hammer|PLUGIN|
row=1|launch|shared|kit|Admin Kits|Give a kit|1|0|item:gunPistol||#FFCC66
```

Icon values:

```text
item:gunPistol
sprite:ui_game_symbol_map
atlas:ServerAssistAtlas:permissions
```

Plain sprite names are also accepted and treated as `sprite:<name>`.

The AdminHelper-era six-field row is still accepted:

```text
row=0|action|label|sub|1|0
```

Missing icon fields fall back to `ui_game_symbol_hammer`.

The new optional top-level keys are:

```text
tab1label=
tab2label=
tab3label=
chrome=normal|hook
protocol=2
```

---

## Config and data migration

1. Stop the server and all clients.
2. Back up:

   ```text
   oxide/config/AdminHelper.json
   oxide/data/AdminHelper/data.json
   ```

3. Rename them:

   ```text
   AdminHelper.json       -> ServerAssist.json
   AdminHelper/data.json  -> ServerAssist/data.json
   ```

4. Change permission nodes inside the data file if you used per-button permissions:

   ```text
   adminhelper.button.*    -> serverassist.button.*
   adminhelper.personal.*  -> serverassist.personal.*
   ```

5. Start the server. Server Assist writes a normalized config and data file.

6. Verify:

   ```text
   /sa list
   /sa reload
   ```

If you prefer a clean install:

1. Delete the old config and data.
2. Start the server.
3. No example button is seeded.
4. Create your buttons through the GUI.
5. Re-grant the new permission nodes.

!!! note
    Server Assist normalizes unknown fields and truncates overlong strings. A hand-edited old file with unexpected shapes may load partially. Back it up before migration.

---
