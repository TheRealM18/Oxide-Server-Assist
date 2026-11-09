# PlayerProfiles - required Server Assist patches

Status: implemented and compiled in this workspace, but this file is the
authoritative handoff for the Server Assist agent. Re-apply or verify these
patches before shipping PlayerProfiles 1.0.0.

PlayerProfiles version: 1.0.0
ServerAssist plugin: patched 2.1.0 build (workspace [Info] is 2.1.0)
ServerAssistMod client: patched 2.1.0 build (workspace ModInfo is 2.1.0)
Protocol: 1 (`@pp|`)

The plugin is optional. If any Server Assist piece is missing or older, the
plugin falls back to a safe reconnect switch. The patches below are what make
the in-place switch possible and prevent old/new combinations from showing
error windows.

---

## 0. Compatibility contract

These values must match on both sides. Do not rename them without updating both
the plugin and the client mod.

```text
Panel / hook id:        playerprofiles
Panel permission:       playerprofiles.use
Plugin name:            PlayerProfiles
Handler method:         HandleGuiRequest(string playerId, string payload)
Protocol prefix:        @pp|
Protocol version:       1
Client request shape:   @pp|playerprofiles|<protocol body>
Server response shape:  @pp|<protocol body>
```

No new NetPackage type is registered. The protocol travels inside the existing
`NetPackageServerAssistRequest` and `NetPackageServerAssistResponse` packets.
Adding a new NetPackage type would change the server package-id table and would
disconnect every client that does not have the matching type with
"Unknown package type". Do not do that.

Documentation-only edits in `ServerAssist/README.txt` and
`ServerAssist/API.md` describe the extension. They are not required at runtime,
but keeping them makes the next Server Assist update safer.

Compatibility matrix:

| ServerAssist plugin | ServerAssistMod | PlayerProfiles | Result |
|---|---|---|---|
| patched | patched | installed | In-place switch |
| patched | unpatched | installed | Reconnect switch, no errors |
| unpatched | patched | installed | Reconnect switch, no errors |
| patched | patched | not installed | `@pp` requests are ignored; no error window |
| unpatched | unpatched | installed | Reconnect switch |

---

## 1. ServerAssist plugin patches

File: `ServerAssist/oxide/plugins/ServerAssist.cs`

### 1.1 Bump the plugin version

Bump to at least:

```csharp
[Info("ServerAssist", "TheRealM18", "2.0.1")]
```

The workspace has since moved to `2.1.0`; that is fine. The version is
informational. The real capability check used by the client mod is the
presence of `RouteCompanionProtocol` (see 2.4), not the version string.

### 1.2 Route companion payloads in `HandleGuiRequest`

Anchor: inside `HandleGuiRequest`, immediately after the empty/`open` payload
normalization and before the `a|provider|` handling.

Current anchor text:

```csharp
if (string.IsNullOrEmpty(payload) || payload == "open")
{
    payload = "a|open";
}
```

Insert:

```csharp
// Optional companion protocols (for example PlayerProfiles).
// The payload carries the target hook id so it can be routed
// without an active Server Assist session:
//
//   @pp|<hook-id>|<protocol body>
//
// The hook receives the normal "@pp|<protocol body>" payload.
if (payload.StartsWith("@pp|", StringComparison.Ordinal))
{
    return RouteCompanionProtocol(playerId, payload);
}
```

This must be before the `CanUse(playerId)` check and before the normal
`a|`/`t|` dispatch. The companion hook performs its own permission check.

### 1.3 Add `RouteCompanionProtocol`

Anchor: inside the `ServerAssist` plugin class, immediately before
`FindGuiHook`.

Add:

```csharp
/// <summary>
/// Routes "@pp|&lt;hook-id&gt;|&lt;body&gt;" to a registered GUI hook
/// without requiring the Server Assist window or hook session to be
/// active. This is used by the optional PlayerProfiles client protocol.
/// </summary>
private string RouteCompanionProtocol(string playerId, string payload)
{
    string body = payload.Substring(4);
    int separator = body.IndexOf('|');
    if (separator <= 0)
    {
        return null;
    }

    string hookId = body.Substring(0, separator);
    string protocolBody = body.Substring(separator + 1);

    GuiHook hook = FindGuiHook(hookId);
    if (hook == null || !CanUseGuiHook(playerId, hook))
    {
        return null;
    }

    return CallGuiHook(hook, playerId, "@pp|" + protocolBody);
}
```

Do not rename this method. `ServerAssistBridge.SupportsCompanionProtocol`
looks for this exact method name and signature to decide whether the server
plugin understands companion payloads. If the method is absent, the bridge
stays silent for `@pp` requests, which is the correct old-plugin behavior.

`CallGuiHook` is intentionally called without `AddHookChrome`; the companion
protocol must receive the raw string.

### 1.4 Keep protocol responses out of `AddHookChrome`

Anchor: `AddHookChrome`, immediately after the empty/null check.

Add:

```csharp
// Companion protocol responses must not receive Server Assist
// chrome; the client's protocol handler consumes them verbatim.
if (view.StartsWith("@pp|", StringComparison.Ordinal))
{
    return view;
}
```

Why this is required: the initial switch request is a normal panel action
(`a|switch_confirm|<id>`). That action is routed through `RouteGuiHook`, which
normally calls `AddHookChrome`. Without this guard, the `@pp|prepare|...`
response is followed by `chrome=hook`, `tab=hook`, `background=...` and
`banner=...` lines, which corrupts the protocol payload. The client would still
parse the token fields, but the final name field and the protocol contract
would be polluted.

### 1.5 Failure behavior if the plugin patches are missing

- 1.2 missing: `@pp` requests hit the normal action dispatcher and return
  `Unknown GUI request`. The client mod's bridge guard in 2.4 prevents this from
  reaching the client on an old plugin, so the user sees reconnect-only.
- 1.3 missing: `SupportsCompanionProtocol` is false; the bridge ignores all
  `@pp` requests. Reconnect-only.
- 1.4 missing: the panel-initiated `prepare` response is chrome-wrapped. The
  in-place handshake may start but the payload is polluted; treat this as a
  broken patch.

---

## 2. ServerAssistMod patches

File set: `ServerAssist/src/*.cs`, compiled into `ServerAssistMod.dll`.
The project already includes `src/**/*.cs`; no csproj compile item is needed.

### 2.1 Add `src/PlayerProfilesClient.cs`

Copy the exact file from:

```text
ServerAssist/src/PlayerProfilesClient.cs
```

SHA-256:

```text
5CF4251E872E082F0908D172FA6890CBCDC12594C462EE0EE0E1C6D8393C365C
```

Do not reimplement it by hand. It contains the client half of the handshake,
the .ttp payload parser, the local apply sequence, the send-timer lock, the
freeze-until-commit logic and the recovery paths.

Required public surface:

```csharp
public static class PlayerProfilesClient
{
    public const string ProtocolPrefix = "@pp|";
    public const string PanelId = "playerprofiles";
    public const int ProtocolVersion = 1;

    public static void Tick();
    public static void OnLocalPlayerSpawned();
    public static void Reset();
    public static void SendHello();
    public static bool TryHandle(string payload);
}
```

Client-to-server requests carry the panel id so Server Assist can route them
without window state:

```text
@pp|playerprofiles|hello|1
@pp|playerprofiles|ready|token
@pp|playerprofiles|chunk|token|index
@pp|playerprofiles|applied|token|sha256
@pp|playerprofiles|failed|token|reason
@pp|playerprofiles|recover|token|reason
@pp|playerprofiles|abort|token|reason
```

Server-to-client responses do not carry the panel id:

```text
@pp|prepare|token|fromName|toName
@pp|begin|token|chunks|sha256|bytes|chunkBytes
@pp|chunk|token|index|base64
@pp|done|token
@pp|abort|token|reason
@pp|ok
```

The client must not add any `NetPackage` type. It uses
`NetPackagePlayerData` for the pre-switch capture and the existing Server Assist
request/response packages for the protocol.

Safety behavior that must not be removed:

- locks `countdownSendPlayerDataFileToServer` and
  `countdownSendPlayerInventoryToServer` during the handshake;
- suppresses the outgoing player-data/inventory sync until commit;
- closes cursor windows and clears drag/drop;
- clears the old buffs, waypoints, owned entities, spawn point, recipes and
  quest journal before applying;
- applies the payload with `StreamModeRead.Persistency`;
- refreshes toolbelt, backpack, equipment, quest tracker and crafting data;
- stays frozen until `@pp|done` (or a 10 second fallback), which closes the
  window where the client has new inventory while the server still has old;
- sends `recover` on a partially applied client so the server restores the old
  profile and kicks;
- does not render `@pp|` responses as a normal window.

### 2.2 `ServerAssistClient.cs`

Required changes:

1. In `Reset()`, add:

```csharp
PlayerProfilesClient.Reset();
```

2. In `Update()`, after the normal hello check and before `GetXui()`, add:

```csharp
PlayerProfilesClient.Tick();
```

3. In `SendHello()`, after the normal `"hello"` packet is sent, add:

```csharp
PlayerProfilesClient.SendHello();
```

4. In `RequestOpen(...)`, at the top, before `pendingPayload` is replaced:

```csharp
// Refresh the optional PlayerProfiles capability before the panel
// request arrives. The server can then offer an in-place switch
// instead of a reconnect.
PlayerProfilesClient.SendHello();
```

5. In `OnResponse(string payload)`, after the null/empty check and before
   `GetXui()`, add:

```csharp
if (PlayerProfilesClient.TryHandle(payload))
{
    return;
}
```

The intercept must be before `GetXui()`. Chunk responses must still be handled
when the Server Assist window is closed or the XUi is not ready.

### 2.3 `ServerAssistMod.cs`

In `OnPlayerSpawnedInWorld`, inside `if (_data.IsLocalPlayer)`, after
`ServerAssistClient.SendHello();`, add:

```csharp
PlayerProfilesClient.OnLocalPlayerSpawned();
```

This sends the capability hello early. Without it, the 60 second tick eventually
does the same, but the first panel open may show reconnect-only.

### 2.4 `ServerAssistBridge.cs`

This is the server-side half of the mod assembly. It must not forward `@pp`
payloads to a Server Assist plugin that does not understand them.

At the top of `HandleRequest`, add:

```csharp
bool profileProtocol = payload != null &&
                       payload.StartsWith("@pp|", StringComparison.Ordinal);
```

Change the missing-player-id, missing-plugin, missing-method and
empty-response returns to stay silent for `profileProtocol`:

```csharp
if (string.IsNullOrEmpty(playerId))
{
    return profileProtocol ? null : ErrorView("Could not determine your player id.");
}

object plugin = FindPlugin();
if (plugin == null)
{
    return profileProtocol
        ? null
        : ErrorView("The ServerAssist Oxide plugin is not loaded on this server.");
}

if (profileProtocol && !SupportsCompanionProtocol(plugin))
{
    return null;
}

if (method == null)
{
    return profileProtocol
        ? null
        : ErrorView("The ServerAssist plugin does not expose " + PluginMethod + ".");
}

string response = result as string;
if (string.IsNullOrEmpty(response))
{
    return profileProtocol
        ? null
        : ErrorView("The ServerAssist plugin returned an empty response.");
}
```

Add the capability check:

```csharp
private static bool SupportsCompanionProtocol(object plugin)
{
    try
    {
        return plugin != null &&
               plugin.GetType().GetMethod(
                   "RouteCompanionProtocol",
                   BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                   null,
                   new[] { typeof(string), typeof(string) },
                   null) != null;
    }
    catch
    {
        return false;
    }
}
```

Why this is required: the new client mod may be installed while the server runs
an older ServerAssist plugin. The older plugin returns `Unknown GUI request` for
`@pp` payloads. Without this guard, every hello would pop an error window. With
it, the request is dropped and PlayerProfiles uses the reconnect path.

`ServerAssistServer.SendResponse` already skips null/empty responses, so a
`null` return is safe.

### 2.5 ModInfo and build

The workspace `ServerAssist/ModInfo.xml` currently reports `2.0.2`. Keep the
mod version at 2.0.2 or newer. The PlayerProfiles README requires 2.0.1+, so a
2.0.2 build satisfies it.

Build:

```powershell
dotnet build "ServerAssist/ServerAssistMod.csproj" -c Release --nologo
```

Expected output:

```text
ServerAssist -> ...\ServerAssist\ServerAssistMod.dll
Build succeeded. 0 Error(s)
```

The DLL hash is not pinned. A rebuild after unrelated Server Assist changes
will produce a different hash. Verify the source anchors in section 2 and the
build result instead.

### 2.6 No NetPackage changes

Do not add `PlayerProfiles` packet classes, do not call
`ServerAssistNetPackages.RegisterAll` for them, and do not modify
`NetPackageServerAssistRequest` or `NetPackageServerAssistResponse` wire
formats. The protocol is entirely string payloads inside the existing packets.

---

## 3. Server plugin compile check

The PlayerProfiles plugin compiles against the game and Oxide assemblies with:

```powershell
dotnet build "PlayerProfiles/tests/PluginCompile/PlayerProfiles.csproj" -c Release --nologo
```

Expected: `Build succeeded. 0 Error(s)`.

A ServerAssist plugin compile check can use the same reference set. In this
workspace it was verified with a temporary `net48` harness that compiles
`ServerAssist/oxide/plugins/ServerAssist.cs`; expected: `0 Error(s)`.

Source hashes at the handoff snapshot:

```text
PlayerProfiles.cs        D3236E1DA9093241A6EF41D619C48DB3C51497B81258EA5FA670BE0563CF5DC2
ServerAssist.cs          C2AB8843E4D5D71609B2D13103A237E79E98E47F6891F16B20D8CCE78C7C81FA
PlayerProfilesClient.cs  5CF4251E872E082F0908D172FA6890CBCDC12594C462EE0EE0E1C6D8393C365C
ServerAssistClient.cs    05A4BC99D628B291D89C6088FEC742F568ED03FBEB03E822B7B9E882A422F7FD
ServerAssistBridge.cs    152316CE15C5F64C773114C0A6CEB9549BA107E4B1F82EF02CEC8070F5C6F0CE
ServerAssistMod.cs       4F364DFC90AB5B18FD3FB84F6D1F0DA22F4F2A3014BD1E23A94BBCC298FD1B7D
```

The Server Assist agent may add unrelated changes while applying these patches.
If a ServerAssist hash differs, verify the anchors in sections 1 and 2 rather
than rejecting the build. `PlayerProfiles.cs` and `PlayerProfilesClient.cs` are
the two files owned by this plugin and should match unless you intentionally
change the icon wiring or protocol.

---

## 4. Packaging

Rebuild both Server Assist archives after the source changes. Preserve the
Server Assist agent's existing root-folder layout; the current package may nest
these under `AA0_ServerAssistPlugin/` or `AA0_ServerAssistMod/`.

`ServerAssistPlugin.zip`:

```text
oxide/plugins/ServerAssist.cs
API.md
update.md
README.txt
MIGRATION.md
```

`ServerAssistMod.zip`:

```text
AA0_ServerAssistMod/ModInfo.xml
AA0_ServerAssistMod/ServerAssistMod.dll
AA0_ServerAssistMod/API.md
AA0_ServerAssistMod/README.txt
AA0_ServerAssistMod/MIGRATION.md
AA0_ServerAssistMod/Config/XUi_Common/styles.xml
AA0_ServerAssistMod/Config/XUi_InGame/windows.xml
AA0_ServerAssistMod/Config/XUi_InGame/xui.xml
```

Use forward slashes in ZIP entry names. Windows `Compress-Archive` can emit
backslashes; build the entries explicitly or normalize with
`Replace('\','/')`.

`PlayerProfiles-1.0.0.zip`:

```text
oxide/plugins/PlayerProfiles.cs
README.txt
update.md
icon-request.md
Config/PlayerProfiles.example.json
```

---

## 5. Acceptance checks

Run these before declaring the integration done.

### 5.1 Static checks

```powershell
Select-String -Path "ServerAssist/oxide/plugins/ServerAssist.cs" `
  -Pattern 'RouteCompanionProtocol|@pp\||Companion protocol responses'
```

Expected: the routing branch, the method, and the `AddHookChrome` guard.

```powershell
Select-String -Path "ServerAssist/src/ServerAssistClient.cs" `
  -Pattern 'PlayerProfilesClient'
```

Expected: `Reset`, `Tick`, `SendHello`, and `TryHandle`.

```powershell
Select-String -Path "ServerAssist/src/ServerAssistBridge.cs" `
  -Pattern 'profileProtocol|SupportsCompanionProtocol'
```

Expected: the guard and the capability check.

### 5.2 Compile checks

```text
PlayerProfiles/tests/PluginCompile/PlayerProfiles.csproj   -> 0 errors
ServerAssist/ServerAssistMod.csproj                        -> 0 errors
ServerAssist.cs compile harness                            -> 0 errors
```

### 5.3 Runtime matrix

1. New plugin + new mod + PlayerProfiles:
   - Server log: `[PlayerProfiles] registered the profiles panel with Server Assist`.
   - Open Profiles, switch an existing profile. No disconnect; inventory,
     level, quests and position change.
2. New plugin + old mod:
   - Switch shows "reconnect required"; the server kicks with
     `ReconnectMessage`; after reconnect the target profile loads. No
     `Unknown GUI request` error.
3. Old plugin + new mod:
   - The client's hello is ignored by the bridge; the panel uses reconnect.
     No error window on spawn.
4. New plugin + new mod without PlayerProfiles:
   - `@pp` requests are ignored by `RouteCompanionProtocol` (hook not found);
     no error window.
5. Partial apply recovery:
   - Simulate a client-side apply failure after the freeze (for example by
     temporarily throwing in `ApplyPayload` after `ClearOldPlayerState`).
   - Expect `@pp|recover`, the server restores the previous profile and kicks
     the client, and the player rejoins with the old character.
6. Fresh profile:
   - Create a profile, switch once, reconnect. It spawns level 1.
   - Switch away and back afterwards. The second switch is in place.
7. Slot permission:
   - Grant `playerprofiles.slots.3`; create a third profile.
   - Revoke it; existing profiles remain readable, new creation is blocked.

### 5.4 Log lines to expect

```text
[PlayerProfiles] loaded. Permissions: playerprofiles.slots.1 - 10.
[PlayerProfiles] registered the profiles panel with Server Assist (sprite:sa_nav_snapshots).
```

Debug mode adds capture/hello lines. Set `"Debug": true` in
`oxide/config/PlayerProfiles.json` when diagnosing.

---

## 6. Rollback

To remove the seamless path without removing PlayerProfiles:

1. Replace the Server Assist archives with the previous 2.0.0 builds.
2. Rebuild `ServerAssistMod.dll` from a source tree without
   `PlayerProfilesClient.cs` and without the five call sites in 2.2/2.3/2.4.
3. Remove the `@pp` routing branch, `RouteCompanionProtocol` and the
   `AddHookChrome` guard from `ServerAssist.cs`.

PlayerProfiles will still work, using the reconnect path. It does not require
the seamless patches to function.

---

## 7. Notes for the Server Assist agent

- Apply by anchors and method names, not line numbers; Server Assist may have
  moved on.
- Do not rename `RouteCompanionProtocol`. The bridge uses it as the capability
  probe.
- Do not remove the `AddHookChrome` guard; the first switch response is returned
  through a normal panel action.
- Keep the client request prefix `@pp|playerprofiles|`. Server Assist strips the
  panel id and gives the hook `@pp|<body>`.
- Keep `PlayerProfilesClient.cs` in the same assembly as `ServerAssistClient`;
  it calls `ServerAssistClient.SendRequest` directly and intercepts responses
  in `ServerAssistClient.OnResponse`.
- No Server Assist XUi XML, permission node, or net-package registration is
  needed.
- The PlayerProfiles plugin owns the `playerprofiles` hook registration and the
  `playerprofiles.use` permission. Server Assist only routes to the hook.
- If the Server Assist agent wants to expose a generic companion-protocol API
  later, `@pp|` is reserved for PlayerProfiles; use a different prefix for
  another protocol.
