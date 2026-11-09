DailyRewards 1.1.0
==================

Daily, weekly, monthly and seasonal playtime rewards for 7 Days to Die.
The player-facing GUI is a server-side Server Assist panel, so the plugin needs
no client DLL or XUi XML of its own. Server Assist and its client mod are still
required for the GUI; the chat commands work without it.

The plugin tracks real playtime in the background. The daily reward is locked
until the player has played the configured amount of time (1 real-life hour by
default) during the current daily period. Weekly, monthly and seasonal rewards
default to no additional playtime requirement, but each tier has its own
RequiredPlaytimeMinutes setting.


FILES
-----

Server:
  oxide/plugins/DailyRewards.cs

Optional local reference:
  Config/DailyRewards.example.json

The plugin writes its runtime files to:
  oxide/config/DailyRewards.json
  oxide/data/DailyRewards/players.json


INSTALL
-------

1. Stop the server.
2. Copy DailyRewards.cs into the server's oxide/plugins folder.
3. Make sure ServerAssist is installed on the server and on every client.
   ServerAssist is only needed for the GUI. The plugin still works through chat.
4. Copy the ServerAssistArt/UIAtlases folder into the Server Assist mod folder
   on the server and every client if you want the bundled card and row icons.
   The icons are optional; Server Assist falls back to its hammer symbol when a
   sprite is missing.
5. Start the server.
6. Edit oxide/config/DailyRewards.json if you want different rewards or
   playtime requirements, then use /rewards reload or restart the server.

EAC must be off, as with every Oxide plugin.

Because this plugin is a Server Assist panel, no DailyRewards client files are
needed. Server Assist registers the panel automatically after the plugin loads.


HOW IT WORKS
------------

Each player has one independent claim per tier per period:

  Daily     resets every day at DailyResetHour
  Weekly    resets every week on WeeklyResetDay at DailyResetHour
  Monthly   resets on the 1st of each month at DailyResetHour
  Seasonal  resets every three calendar months, or when manually rotated

The daily period is the important one for playtime. The plugin tracks how long
a player is connected and adds that time to all four tier periods. A player can
disconnect and reconnect; the time accumulates until the period resets.

Examples:

  * Daily required playtime is 60 minutes.
  * A player plays 25 minutes, disconnects, and returns later the same day.
  * Their daily period playtime is 25 minutes until they play another
    35 minutes.
  * After 60 minutes in the current daily period the daily reward becomes
    claimable.

The requirement is period-based, not lifetime-based. If the daily period rolls
over, the daily playtime counter resets to zero. Set
Daily.RequiredPlaytimeMinutes to 0 if you want the daily reward to be instantly
claimable. Set it to a higher value if you want a longer gate.

Time is measured with real-world server time. Playtime is only counted while
the player is connected. The plugin flushes active sessions every 60 seconds and
saves player data every SaveIntervalSeconds (300 seconds by default), on
disconnect, on claim, on server save, on reload and on shutdown.

The server's local time zone is used for DailyResetHour, WeeklyResetDay, month
boundaries and calendar seasons. If the server machine runs in UTC, the resets
are UTC.


SERVER ASSIST GUI
---------------

When ServerAssist is loaded, a card named "[GUI] Daily Rewards" appears on the
ServerAssist home screen for players with the configured HookPermission
(dailyrewards.use by default). The card uses the calendar-and-gift icon from
the ServerAssistArt pack.

The overview shows all four tiers with their current status, reward contents,
playtime progress and next reset. Each tier row carries its own icon, status
badge and tint. Click a tier row for details. The claim buttons grant the tier
if the player is eligible.

Administrators also get an "Admin Tools" row and button. Admin Tools can grant
any tier to an online player without using that player's claim, and it can
reset a player's current claim for any tier. These actions prompt for a player
name or SteamID.

If ServerAssist is not loaded, /rewards falls back to a chat status display and
/rewards claim <tier> still works.


COMMANDS
--------

/rewards
/daily
  Open the ServerAssist GUI. If ServerAssist is unavailable, show status.

/rewards claim <daily|weekly|monthly|seasonal>
  Claim a tier directly from chat.

/rewards status [player]
  Show total playtime, daily streak and status for every tier.
  Inspecting another player requires dailyrewards.admin.

/rewards playtime [player]
  Show tracked playtime and the current period counters.
  Inspecting another player requires dailyrewards.admin.

/rewards grant <player name or SteamID> <tier>
  Admin only. Grant a tier without using the target's claim. The target must be
  online and spawned. This is the manual "hand out a reward" command.

/rewards grantall <tier>
  Admin only. Grant a tier to every online, spawned player.

/rewards reset <player name or SteamID> <tier>
  Admin only. Clear one player's current claim for a tier. This works for
  offline players that already have stored data.

/rewards resetall <tier>
  Admin only. Clear the current claim for every stored player.

/rewards season
  Admin only. Show the current season mode and season name.

/rewards season <name>
  Admin only. Start a manual season. Changing the name immediately creates a
  new seasonal period, so everyone can claim the new seasonal reward.

/rewards season auto
  Admin only. Return to calendar seasons.

/rewards reload
  Admin only. Flush playtime, save data, reload the config and data, and
  re-register permissions and the ServerAssist hook.

/rewards help
  Show the command list.

Commands also work from the server console where applicable. Admin grants still
require an online, spawned player because the reward items need an inventory.


PERMISSIONS
-----------

dailyrewards.use
  Use the Daily Rewards GUI and claim normal rewards. Granted to the default
  player group when GrantUseToDefaultGroup is true. You can leave this to
  everyone, or remove it and use tier permissions for controlled access.

dailyrewards.admin
  Use the Admin Tools GUI and all admin commands. Granted to the default
  administrators group automatically.

dailyrewards.<anything>
  Any tier Permission or reward Permissions entry that starts with
  "dailyrewards." is registered by this plugin. For example a VIP tier can use
  "dailyrewards.vip". Permission nodes owned by other plugins are not
  registered by this plugin; grant them with their own plugin, PermissionGui or
  the normal Oxide permission commands.

Tier Permission
  Optional per-tier node in the config. A player must have dailyrewards.use
  (or the configured HookPermission) and the tier's Permission to claim it.
  Administrators bypass both checks for admin grants.


CONFIG
------

Run the server once, then edit oxide/config/DailyRewards.json. A complete
example is also in Config/DailyRewards.example.json.

Top-level options:

  ChatPrefix
    Prefix for chat replies. Default "[Rewards]".

  GrantUseToDefaultGroup
    Grant dailyrewards.use to the default players group. Default true.

  HookLabel / HookDescription / HookPermission
    The Server Assist card text and permission. HookPermission may be blank to
    show the card to everyone who can open Server Assist. Default
    "dailyrewards.use".

  PanelIcon
    Server Assist icon for the Daily Rewards card and the default background/
    banner art. Accepts sprite:, item:, atlas: or a plain sprite name. Default
    "sprite:sa_daily_rewards".

  PanelOrder
    Sort order for the card on the Server Assist home screen. Lower numbers
    appear first. Default 30.

  NotifyWhenReady
    Send the player a chat message when their daily reward becomes claimable.
    Default true.

  SaveIntervalSeconds
    How often player data is written while the server is running. Clamped to
    30-3600. Default 300.

  MaxTickAccrualSeconds
    Safety cap for one playtime tick. If the server stalls, this prevents a
    giant time jump from being credited. Default 300. The normal tick is 60
    seconds.

  DailyResetHour
    0-23. Hour of the server's local day when the daily, weekly, monthly and
    seasonal periods roll over. Default 0 (midnight).

  WeeklyResetDay
    DayOfWeek name for weekly resets: Monday, Tuesday, ... Sunday. Default
    "Monday".

  SouthernHemisphere
    Swap the calendar season names for the southern hemisphere. The boundary
    months stay the same. Default false.

  SeasonMode
    "calendar" for northern/southern calendar seasons, or "manual" for
    administrator-controlled seasons. Default "calendar".

  ManualSeason
    Season name used while SeasonMode is "manual". Default "Season 1".

Each of the four tier objects has the same shape:

  Enabled
    Turn the tier on or off. Default true.

  DisplayName
    Name shown in the GUI and messages. Defaults to Daily, Weekly, Monthly,
    Seasonal.

  Icon
    Server Assist row icon for this tier. Accepts sprite:, item:, atlas: or a
    plain sprite name. Defaults:
      Daily     sprite:sa_daily_rewards
      Weekly    sprite:sa_act_time
      Monthly   sprite:sa_nav_snapshots
      Seasonal  sprite:sa_act_weather

  RequiredPlaytimeMinutes
    Playtime required during the current period before the tier can be claimed.
    Default 60 for Daily and 0 for the other tiers.

  Permission
    Optional extra permission node for this tier.

  Items
    List of item rewards. Each entry has:
      Item     internal item name, display name, or a recognizable partial name
      Count    number of items, at least 1
      Quality  quality for items that support quality (1-6, 0 = default 6)

  Commands
    List of server console commands to run when the reward is granted.
    Supported placeholders:
      {playerid}  player's platform/Steam ID
      {steamid}   same as playerid
      {entityid}  current game entity id
      {name}      player display name
      {player}    same as name
      {tier}      daily, weekly, monthly or seasonal

    Wrap placeholders in quotes in the command if the player name may contain
    spaces, for example: say "Reward claimed by {name}".

  Permissions
    List of permission nodes to grant to the player as part of the reward.

Default item rewards use casinoCoin (dukes) and medicalFirstAidBandage. Both
are vanilla 7 Days to Die items. Change them to whatever your server uses.

If a reward item cannot be found, the plugin logs one warning per item name and
skips that item. If every item is invalid and there are no commands or
permissions, the claim is not consumed. This prevents a typo in the config from
silently eating a player's daily reward.

If the player's inventory is full, item rewards are added to any existing
stacks first and the remainder is dropped at the player's feet. The claim is
still consumed because the reward was delivered.


SEASONS
-------

Calendar seasons use these boundaries, with DailyResetHour applied:

  Winter   December 1 - February 28/29
  Spring   March 1 - May 31
  Summer   June 1 - August 31
  Fall     September 1 - November 30

SouthernHemisphere swaps the season names while keeping the same boundary
months, so December-February is Summer and March-May is Autumn, for example.

Manual seasons are useful if your server runs its own seasons. Run:

  /rewards season Season 1

Then rotate it later with:

  /rewards season Season 2

Every change to the manual season name creates a fresh seasonal period. Use
/rewards season auto to return to calendar seasons.


DATA AND API
------------

Player data is stored in oxide/data/DailyRewards/players.json. Do not edit it
while the server is running. Use the admin commands instead.

The plugin exposes these methods to other Oxide plugins through Plugin.Call:

  GrantReward(string playerId, string tierKey)
    Grant a tier without using the player's claim. The player must be online.

  ClaimReward(string playerId, string tierKey)
    Run the normal claim checks for that player. This still enforces permission,
    claim state and playtime.

  ResetReward(string playerId, string tierKey)
    Clear the player's current claim for one tier.

  GetPlaytimeSeconds(string playerId)
    Return total tracked playtime in seconds.

  GetPeriodPlaytimeSeconds(string playerId, string tierKey)
    Return playtime in the current tier period, in seconds.

  GetRewardStatus(string playerId, string tierKey)
    Return a short human-readable status string such as "Ready to claim" or
    "Locked | 12m 00s / 1h 00m played this period".

Example from another plugin:

  object result = plugins.Find("DailyRewards").Call(
      "GrantReward", playerId, "weekly");


ICONS
-----

The plugin uses the ServerAssistArt sprite set. Install the PNGs from
ServerAssistArt/UIAtlases/UIAtlas in the Server Assist mod folder on the server
and every client. No XML edit is needed because Server Assist reads the default
UIAtlas. The full source-file mapping and design notes are in ICON_MAP.md.

Current icon mapping:

  Daily Rewards card       sprite:sa_daily_rewards
  Daily tier               sprite:sa_daily_rewards
  Weekly tier              sprite:sa_act_time
  Monthly tier             sprite:sa_nav_snapshots
  Seasonal tier            sprite:sa_act_weather
  Playtime                 sprite:sa_act_time
  Daily streak             sprite:sa_nav_logs
  Grant reward             sprite:sa_act_give
  Reset claim              sprite:sa_nav_snapshots
  Admin tools              sprite:sa_nav_moderation
  Season settings          sprite:sa_act_weather
  Permissions              sprite:sa_permission_gui
  Commands                 sprite:sa_nav_server
  Reward items             item:<item name> (live item icon)
  Missing/locked/error     sprite:sa_act_warn
  Online players           sprite:sa_nav_players

Rows use short badges such as READY, LOCKED, CLAIMED, OFF, TIME, STREAK,
ADMIN, GIVE, RESET, SEASON, ONLINE, ITEM, PERM and CMD. The tint colors are
chosen per tier and state. The card's registered icon is also used as the
panel background and banner.

The planned sa_rewards_daily, sa_rewards_weekly, sa_rewards_monthly and
sa_rewards_claim icons are listed in ServerAssistArt/PLANNED_ICONS.md but are
not rendered in the current pack. This build therefore uses the closest
existing art. If those four icons are added later, change PanelIcon and each
tier's Icon in the config to the new sprite keys.

If an icon is missing, Server Assist falls back to its default symbol. The
spelling matters: use the sprite name without .png.


SERVER ASSIST INTEGRATION
-------------------------

The plugin registers the panel id "dailyrewards" with:

  RegisterPanel(
      "dailyrewards",
      HookLabel,
      HookDescription,
      HookPermission,
      "DailyRewards",
      "HandleGuiRequest",
      PanelIcon,
      PanelOrder)

RegisterPanel and the v2 icon/badge/tint fields require Server Assist 2.0+. On
older 1.x installs the plugin falls back to RegisterGuiHook followed by
SetPanelIcon, but the per-row icon fields are ignored by that client. It
re-registers on OnServerAssistReady, opens through OpenPanel with an
OpenHookForPlayer fallback, and unregisters on Unload. It adds no XUi windows of
its own.

The Server Assist panel handler method is:

  public string HandleGuiRequest(string playerId, string payload)

It accepts the standard a|open, a|<action> and t|<encoded text> payloads and
returns the Server Assist v2 view protocol with 9-field rows:

  row=index|action|label|sub|enabled|selected|icon|badge|tint


TROUBLESHOOTING
---------------

The [GUI] Daily Rewards row does not appear:
  Check that ServerAssist is loaded, the player has dailyrewards.use (or the
  configured HookPermission), and the plugin compiled without errors. Reload
  ServerAssist or run /rewards reload.

The card or rows show the default hammer instead of an icon:
  The client is missing the sprite. Copy ServerAssistArt/UIAtlases/UIAtlas into
  the Server Assist mod folder on that client, then restart the game. Check that
  the sprite key in DailyRewards.json still matches a PNG name without .png.

The GUI says the player has no permission:
  Grant dailyrewards.use to the default player group, or change HookPermission
  to an existing node. Admins always get access through dailyrewards.admin.

The daily reward stays locked:
  The plugin counts playtime in the current daily period only. Check
  /rewards playtime. A player who just installed the plugin does not have
  retroactive time from before it was loaded.

Items did not appear:
  Check the server log for "reward item ... was not found". Use the exact item
  name from listitems. If the inventory was full, look at the player's feet.

The daily reward was claimed but the player wants it again:
  Use /rewards reset <player> daily as an administrator. Admin grants use
  /rewards grant and do not consume the player's normal claim.

Weekly/monthly/seasonal rewards reset at the wrong time:
  These use the server's local time zone and DailyResetHour. Check that the
  server machine's time zone is what you expect.


