# MediaIntegration — Platform matrix and research plan

This is the working list of platforms to evaluate. It is deliberately conservative: **"Research"** means the API details must be read and tested before implementation. Do not implement a provider from memory.

> **Implementation status:** Twitch and the generic custom webhook are implemented. YouTube and Kick are next. Every other row is a research/priority target, not a compatibility promise. Platform APIs change, some are partner-only, and some have no official event API. The matrix records what to investigate, not what is already supported.

## Capability key

```text
C    chat
F    follow
S    subscription / membership
G    gift subscription / gift membership
D    donation / tip / cheer / Super Chat / gift
R    platform reward / channel points
Raid incoming raid
M    momentum / hype train / goal
SS   stream online/offline
VC   viewer count
-    not available or not planned
```

`Research` means the exact event coverage is unknown until the docs and a test account are checked.

## Priority

| Priority | Meaning |
|---|---|
| P0 | Existing Twitch parity |
| P1 | First expansion wave; official APIs, strong event coverage |
| P2 | Official but limited, regional, webhook-first, aggregators, tips/membership services |
| P3 | Unofficial/partner-only/mobile; opt-in and fragile |
| P4 | Long-tail unofficial; manual/webhook only |
| P5 | Generic webhook/manual fallback only |

**First expansion after Twitch: YouTube and Kick.** Trovo and regional official APIs follow in the next phase. The custom webhook provider shipped in Phase 0 as the safety net for every other platform.

Unofficial adapters are governed by `UNOFFICIAL_ADAPTERS.md`: manual linking + custom webhooks by default, automatic rewards only after a full audit and isolation.

## Matrix

| Provider | Transport / API | Link mode | Events | Confidence | Priority |
|---|---|---|---|---|---|
| Twitch | EventSub WebSocket, Helix, OAuth device | Device code | C F S G D R Raid M SS VC | Official (implemented) | P0 |
| YouTube Live | Data API v3 live chat, PubSubHubbub, OAuth | OAuth redirect | C S G D(Super Chat/Stickers) SS VC | Official, research quotas | P1 |
| Kick | Public API, OAuth, chat WebSocket; events TBD | OAuth | C F? S? G? D? SS VC | Research | P1 |
| Trovo | Developer API, OAuth, chat/webhooks | OAuth | C F? S? D? SS VC | Research | P2 |
| Custom webhook | HTTP POST + HMAC | Webhook token | Any staff-defined event | Internal | P0 (done) |
| Facebook Gaming | Graph API, OAuth, webhooks | OAuth | C S? D? F? SS VC | Limited/partner | P2 |
| CHZZK (Naver) | Open API, OAuth, chat WebSocket | OAuth | C D(cheer) S? SS VC | Official, research | P2 |
| SOOP (AfreecaTV) | API/chat, star balloons | Research | C D S? SS VC | Research | P2 |
| VK Play | API, OAuth, chat/donations | OAuth | C D S? SS VC | Research | P2 |
| Bilibili Live | Open Platform, live WebSocket, OAuth | OAuth | C D(gifts) S(guard) SS VC | Official, research | P2 |
| Restream | Restream Chat/API | OAuth or token | C; native gifts/subs unlikely | Research | P2 |
| StreamElements | Webhooks/API | Webhook token | D(tips), loyalty, merch | Official | P2 |
| Streamlabs | Socket API/webhooks | OAuth or token | D(tips), alerts | Official | P2 |
| Ko-fi | Webhooks | Webhook token | D, S(membership) | Official | P2 |
| Patreon | API/webhooks | OAuth | S(membership), D? | Official | P2 |
| DLive | GraphQL API, chat | OAuth/research | C D? S? F? SS VC | Limited | P3 |
| Rumble | API/chat research | Research | C D? S? SS VC | Research | P3 |
| TikTok Live | Unofficial WebSocket libraries | Manual challenge | C D(gifts) F? SS VC | Unofficial | P3 opt-in |
| Douyu | Partner/undocumented | Manual/staff | C D S? SS VC | Unofficial | P4 opt-in |
| Huya | Partner/undocumented | Manual/staff | C D S? SS VC | Unofficial | P4 opt-in |
| Kuaishou | Partner/undocumented | Manual/staff | C D S? SS VC | Unofficial | P4 opt-in |
| Nimo TV | Partner/mobile | Manual/staff | C D? SS VC | Unofficial | P4 opt-in |
| Bigo Live | Partner/mobile | Manual/staff | C D SS VC | Unofficial | P4 opt-in |
| Nonolive | Partner/mobile | Manual/staff | C D SS VC | Unofficial | P4 opt-in |
| YY | Partner/undocumented | Manual/staff | C D SS VC | Unofficial | P4 opt-in |
| X / Twitter | API v2 | OAuth | C? SS? | Research/limited | P4 |
| Instagram | Graph API | OAuth | C? SS? | Research/limited | P4 |
| LinkedIn Live | API research | OAuth | SS? | Research/limited | P5 |
| Floatplane | API research | OAuth | S? C? | Research | P5 |
| Locals | API research | OAuth | S? C? | Research | P5 |
| Generic IFTTT/Zapier/Streamer.bot | Custom webhook | Webhook token | Any mapped event | Internal | P0 (done) |

## What "Restream support" means

Restream is a **multistreaming and chat aggregator**, not a native source of every platform's gifts and subscriptions. The design treats it as:

- **Restream chat/event adapter** with `platform = restream` or original platform when identifiable.
- **Not** a substitute for linking native Twitch/YouTube/Kick accounts if the streamer wants native gift/sub rewards.
- **Dedupe required**: if Restream relays a Twitch chat message that the Twitch adapter also receives, do not count it twice.

If Restream exposes only unified chat, a Restream-tier trigger should be configured as chat-only or custom, not as a donation/sub trigger.

## Provider research checklist

Before writing an adapter, record all of these in a provider note:

1. Official developer docs URL and API version.
2. OAuth flow(s), scopes, token lifetime, refresh, revocation.
3. Event transport: WebSocket, webhook, polling, chat, or none.
4. Exact event payloads for every supported event type.
5. Rate limits, quotas, and subscription costs.
6. Webhook signature/HMAC scheme and replay protection.
7. Link mode and identity fields (immutable user/channel IDs).
8. Terms of service, brand rules, and partner requirements.
9. A test account/channel and a recorded fixture for every event type.
10. Fallback behavior when the API is unavailable or the account is not eligible.

## Per-platform notes and first research tasks

### Twitch — P0

- Existing adapter: EventSub WebSocket, device-code OAuth, Helix.
- Keep as the parity baseline while the core is refactored.
- Verify scopes and EventSub limits before adding multi-account support.

### YouTube Live — P1

- Data API v3: `liveBroadcasts`, `liveStreams`, `liveChatMessages`.
- PubSubHubbub for stream-start notifications; polling for live chat.
- OAuth redirect flow; Google device flow is limited, so plan a small callback page.
- Events: chat, Super Chat, Super Stickers, memberships, gift memberships, stream status, viewer count.
- Watch quota costs carefully; `liveChatMessages` polling is quota-expensive.
- No follows/raids/channel points.

### Kick — P1

- Verify current official API and event/webhook support before coding.
- Chat is available over a realtime socket; OAuth is available.
- Research: subs, gift subs, donations, follows, raids, rewards, stream status, viewer count.
- If official events are incomplete, start chat + stream status and document the gap.

### Trovo — P1

- Verify official developer API, OAuth, webhook/chat support.
- Research subscription/donation event coverage and regional availability.
- Start with chat + stream status if the event API is limited.

### Custom webhook — P0 (implemented)

- First non-Twitch provider because it validates the provider interface without a third-party API.
- Per-streamer webhook token, HMAC signature, replay protection, JSON mapping.
- Covers IFTTT, Zapier, Streamer.bot, custom scripts, and any platform with a webhook.
- Supports every normalized event type, including `custom`.

### Facebook Gaming — P2

- Graph API and webhooks; creator eligibility/partner requirements must be verified.
- Likely limited event coverage. Start with chat/stream status.

### CHZZK (Naver) — P2

- Official Open API, OAuth, chat WebSocket.
- Research cheer/donation and membership/guard events.
- Korean market; test account required.

### SOOP (AfreecaTV) — P2

- Research official API and star-balloon donation events.
- Likely chat + donations; subscriptions/memberships need verification.

### VK Play — P2

- Research OAuth, chat, donations, and stream status.
- Russian market; test account required.

### Bilibili Live — P2

- Open Platform, live WebSocket, OAuth.
- Events: chat, gifts, guard/membership, Super Chat-like messages, stream status, viewer count.
- Chinese regulatory and platform rules apply.

### Restream / StreamElements / Streamlabs — P2

- Treat as aggregators/event sources, not as native platforms.
- StreamElements/Streamlabs: tips, merch, loyalty; webhook/Socket APIs.
- Restream: unified chat; confirm whether any native event data is exposed.
- Dedupe against native adapters.
- Link via token/webhook or OAuth.

### Ko-fi / Patreon — P2

- Webhook-first; membership tiers and donations.
- Verify signatures, currency, and recurring vs one-time events.
- Good candidates for a "creator support" trigger family.

### DLive / Rumble — P3

- Research official APIs, chat, subscriptions, donations.
- Implement only if the API is stable and testable.

### TikTok Live — P3 (opt-in)

- No official public event API for gifts/comments as of this design.
- Third-party libraries use undocumented WebSocket protocols.
- Default: manual challenge link + custom webhook. No automatic reward granting.
- Automatic gifts/comments only after the `UNOFFICIAL_ADAPTERS.md` audit and isolation pass.
- High breakage risk and possible ToS issues; isolate behind an explicit `enabled` flag and a warning.

### Douyu / Huya / Kuaishou / Nimo / Bigo / Nonolive / YY — P4 (opt-in)

- Partner-only or undocumented APIs; mobile protocols.
- Default: manual/staff linking + custom webhooks; no automatic rewards.
- Automatic rewards only after `UNOFFICIAL_ADAPTERS.md` audit and isolation.
- Implement only if a streamer/server specifically needs it and accepts the maintenance risk.

### X / Twitter, Instagram, LinkedIn, Floatplane, Locals — P4/P5

- Mostly stream status/comments, not gift/sub events.
- Use custom webhooks or defer until there is a concrete request.

## Support policy

1. A provider is not enabled by default until it has:
   - a documented link flow,
   - contract tests with recorded fixtures,
   - a live smoke test,
   - a clear capability table,
   - and a failure/disable path.
2. Official APIs are preferred. Limited official APIs are acceptable with honest capability flags.
3. Unofficial adapters are opt-in, labeled, and isolated. They must never be required for the core Twitch/YouTube/Kick experience. The full audit/isolation gate is in `UNOFFICIAL_ADAPTERS.md`; default is manual linking + custom webhooks.
4. A provider that cannot identify a viewer or a stable event ID is not allowed to grant rewards automatically; it can use `custom`/manual approval instead.
5. Platform logos are not painted into the icon set. The UI uses text badges and neutral streaming icons; brand assets follow each platform's guidelines.

## Research order

1. Twitch parity (existing).
2. Custom webhook (validates the model).
3. YouTube (largest official event API after Twitch).
4. Kick.
5. Trovo.
6. CHZZK / VK Play / Bilibili / SOOP.
7. Restream / StreamElements / Streamlabs / Ko-fi / Patreon.
8. TikTok and regional long-tail (opt-in).
9. Anything else by request.
