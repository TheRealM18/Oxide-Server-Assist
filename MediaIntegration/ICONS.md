# MediaIntegration — icon policy

The icon system is already platform-neutral. The rename does not require a new
icon for every platform.

## Rules

1. Keep the existing `Icons` role map. The 30 requested `sa_stream_*` sprites are
   still the complete art request.
2. Platform identity is a **text badge**, not a painted logo:

   ```text
   TWITCH
   YOUTUBE
   KICK
   TROVO
   CHZZK
   SOOP
   VKPLAY
   BILIBILI
   TIKTOK
   RESTREAM
   CUSTOM
   ```

   The plugin already supports row badges; the Media panel uses the badge field
   for the provider name.
3. Do not add Twitch/YouTube/Kick/etc. logos to the atlas. Platform brand assets
   have their own usage rules, and a 20-platform logo set would be a maintenance
   problem.
4. If a server explicitly wants platform icons later, prefer official brand
   assets supplied by the platform and referenced with `atlas:`/`sprite:`; do
   not paint lookalikes.
5. Provider-specific status is communicated with the existing neutral relay,
   link, test, and status icons plus the row tint.
6. The current art request remains:

   ```text
   ServerAssistArt/REQUESTS_TwitchIntegration.md
   ```

   In Phase 0 it will be renamed to `REQUESTS_MediaIntegration.md`. The 30 sprite
   names do not change.

## Why

- The core UI is shared, so one icon per role is enough.
- Platform names are text and can be localized/renamed without new art.
- The user asked the art agent to make only the icons that are needed. Platform
  logos are not needed for the reward system to work.
