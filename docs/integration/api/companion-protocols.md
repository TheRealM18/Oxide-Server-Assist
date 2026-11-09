# Companion protocols

Part of the [Server Assist API](index.md).

## Optional companion protocols (`@pp|`)

Server Assist 2.1 can route a companion protocol that needs client-side work outside a normal Server Assist window session. PlayerProfiles uses this for its in-place character switch. It is optional: if the plugin, the server hook or the client build is missing, normal panels and older clients continue to work.

### Routing

A client-to-server companion payload carries the target hook id:

```text
@pp|<hook-id>|<protocol body>
```

Server Assist resolves the hook, checks the same panel permission used for a normal hook, and passes the companion handler:

```text
@pp|<protocol body>
```

Example:

```text
Client -> server:  @pp|playerprofiles|hello|1
Server -> handler: @pp|hello|1
```

The route runs before the normal `a|` / `t|` dispatcher and does not require the Server Assist window or an active hook session. The companion must still validate the player and every protocol request in its handler.

### Responses

A handler response that starts with `@pp|` is returned verbatim. Server Assist does **not** append hook chrome, `tab=hook`, `background=` or `banner=` to it.

```text
@pp|ready|token
@pp|chunk|token|0|base64...
@pp|done|token
```

Do not use `@pp|` for an ordinary panel view. Return a normal `v2` view when the player should see Server Assist chrome.

### Compatibility

- No new NetPackage type is registered. The protocol travels inside the existing request/response packages, so the server package-ID table does not change.
- An older client does not start a companion protocol and keeps its existing panel behaviour.
- An older server plugin does not understand `@pp|`; the client bridge stays silent instead of showing an error window.
- The reference implementation is `PlayerProfiles` (`playerprofiles`, protocol version `1`).

---
