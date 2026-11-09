# ServerAssist update request — SentinelAC

Status: **no client-mod change required for SentinelAC 1.0.2**

SentinelAC now uses the existing ServerAssist 2.1 provider surface:

- `GetProviderHomeSupport`
- `RegisterGuiProvider` / `UnregisterGuiProvider`
- `OpenGuiProvider`
- `SetProviderIcon`
- `RegisterPanel` / `UnregisterPanel` / `OpenPanel` fallback
- provider view protocol 2: `tab`, `tab1label`, `tab2label`, `tab3label`,
  `input`, `row` icon/badge/tint, `btn` icon/tint, `background`, `banner`
- `t|...` text-input payloads
- `a|...` row/button actions
- `a|home` to return to the plugin board
- `close` to close the window (client-local)

The SentinelAC provider is:

```text
id:      sentinelac
title:   Sentinel AC
tabs:    Risk / Evidence / Modules
handler: HandleServerAssistRequest(string playerId, string payload)
```

No new net package, client control or protocol key is needed. The current
ServerAssist client can render the whole SentinelAC menu.

## Optional future request (not needed now)

If we later want a graphical risk gauge or a colored progress bar inside a row,
that would need a new view-protocol row type. SentinelAC does not require it; it
currently uses a numeric badge and the row tint, which the existing client
already supports.

Do not make any ServerAssist changes for SentinelAC 1.0.2.
