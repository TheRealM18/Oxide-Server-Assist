VoteSkipNight 1.2.0
===================

A server-side Oxide plugin for 7 Days to Die. Players vote to skip the current
night. A vote passes only when at least 90% of the eligible players who are
online vote yes. Blood moon nights can never be skipped.

This is an Oxide plugin only. It does not need a client DLL and it does not
change XUi files. If ServerAssist is installed, VoteSkipNight adds a "Vote: Skip
Night" panel inside the existing ServerAssist window.


FILES
-----

  oxide/plugins/VoteSkipNight.cs

The plugin is compiled by Oxide at server start. There is no DLL to copy.


INSTALL
-------

1. Stop the server.
2. Copy VoteSkipNight.cs into the server's oxide/plugins folder.
3. Start the server. Oxide compiles it and creates:
     oxide/config/VoteSkipNight.json
4. Join the server and type /voteskip at night.

You can also copy the whole VoteSkipNight folder from this package and then
move its oxide/plugins/VoteSkipNight.cs into the server's oxide/plugins folder.


COMMANDS
--------

/voteskip
  At night, starts a vote. If a vote is already running, votes yes.
  Aliases: /skipnight and /votenight.

/voteskip yes
  Vote yes, or change your no vote to yes. The starter is counted as yes by
  default.

/voteskip no
  Vote no, or change your yes vote to no.

/voteskip status
  Show the current yes count, the required count, the no count, and the time
  left.

/voteskip cancel
  Cancel the vote. The player who started it and admins can cancel.

/voteskip gui
  Open the Vote Skip Night panel inside ServerAssist. Requires voteskip.use (or
  admin), and the ServerAssist client mod must be installed on that player's
  client.

/voteskip force
  Skip the night immediately. Admin only. This still refuses to skip a blood
  moon and still requires it to be night.

/voteskip reload
  Reload oxide/config/VoteSkipNight.json. Admin only.

/voteskip help
  Show the command list.


HOW THE VOTE WORKS
------------------

* A vote can only be started while the world is dark.
* The required yes votes are calculated from the eligible players who are
  online right now:
      required = ceil(eligible players * RequiredYesPercent / 100)
* Example: 10 eligible players and RequiredYesPercent 90 means 9 yes votes.
* The result is rounded up, so small servers need every eligible player to vote
  yes. For example, 3 eligible players require 3 yes votes, and 9 require 9.
* The check is live. If someone leaves, the requirement can drop. If someone
  joins, the requirement can rise. If someone starts or stops sleeping, the
  eligible count can change too. A message is sent when the requirement
  changes.
* A player who disconnects loses their vote and must vote again after
  reconnecting.
* When the threshold is reached, the world time jumps to dawn plus
  DawnBufferHours (default 1 hour).
* If the vote expires without enough yes votes, the night continues.
* Only one vote can run at a time. A cooldown prevents immediate repeat votes.


SLEEPING PLAYERS
----------------

Sleeping players are detected with Oxide's IPlayer.IsSleeping flag (the game's
EntityPlayer.IsSleeping value).

  IgnoreSleepingPlayers = true
    Sleeping players are left out of the eligible voter count and cannot vote.
    This is the default.

  voteskip.bypasssleepers
    A sleeping player with this permission is still counted and can still vote.
    Use it for players who should not be able to avoid a vote by sleeping.
    It is not granted automatically; grant it with PermissionGui or the normal
    Oxide permission commands.

If your server uses a custom sleeping or AFK mod, make sure that mod sets the
EntityPlayer.IsSleeping flag. If it does not, VoteSkipNight cannot identify
those players and will treat them as normal online players.

Set IgnoreSleepingPlayers to false if you want all connected players counted
regardless of the sleeping flag.


SERVER ASSIST INTEGRATION
-------------------------

When ServerAssist is loaded, VoteSkipNight registers a GUI hook named
"Vote: Skip Night". It appears on the ServerAssist home screen as a [GUI] button
for players who have voteskip.use (admins always see it).

The panel is rendered inside ServerAssist's existing window as icon cards. It
shows:

  * the current time and whether it is day or night
  * the eligible voter count and the required yes votes
  * the live yes/no counts and time left
  * your current vote
  * the cooldown, if one is active
  * whether you are being counted as sleeping

Start Vote, Vote Yes, Vote No, Cancel Vote and Refresh are clickable action
cards. Admins also get a Force Skip card. The other cards are read-only status
cards.

Click "Vote Yes" or "Vote No" in the panel to cast or change your vote without
typing a command. The same server-side checks run either way.

You can also open the panel with /voteskip gui.


ICONS
-----

The panel uses the Server Assist Art set from ServerAssistArt /
AAZ_ServerAssistArt. The plugin-card icon is:

  sprite:sa_vote_skip_night

The cards use these sprite keys:

  Start Vote        sprite:sa_vote_skip_night
  Vote Yes          sprite:sa_daily_rewards
  Vote No           sprite:sa_act_ban
  Cancel Vote       sprite:sa_act_kick
  Force Skip        sprite:sa_act_time
  Current Vote      sprite:sa_nav_moderation
  Your Vote         sprite:sa_permission_gui
  Eligible Voters   sprite:sa_nav_players
  Status / blood moon   sprite:sa_act_weather
  Status / cooldown     sprite:sa_act_warn
  Status / sleeping     sprite:sa_act_freeze
  Status / day          sprite:sa_nav_world
  Status / ready        sprite:sa_act_godmode
  Refresh           sprite:sa_plugin_hot_reload

The art is client-side. Install the PNGs in the Server Assist mod folder or in
the separate AAZ_ServerAssistArt mod:

  Mods/AAZ_ServerAssistArt/UIAtlases/UIAtlas/sa_*.png

The game folds every mod's UIAtlases/UIAtlas PNGs into the global UIAtlas at
launch. If the art mod is missing, Server Assist falls back to the default
hammer sprite. Sprite names never include .png.

The source art and the full key map are in:

  C:\Users\robert\Desktop\7 Days 2 Die Mods\Oxide\ServerAssistArt

Four dedicated vote icons are requested in
ServerAssistArt\VOTESKIPNIGHT_ICON_REQUEST.md:

  sa_vote_yes, sa_vote_no, sa_vote_cancel, sa_vote_bloodmoon

Until those are rendered, the panel uses the stand-in icons listed above
(sa_daily_rewards, sa_act_ban, sa_act_kick, sa_act_weather).


voteskip.use
  Shows the Vote Skip Night panel in ServerAssist. Granted to the default
  players group automatically (GrantUseToDefaultGroup). Admins can always see
  the panel even without this permission. The player also needs
  serverassist.use to open ServerAssist itself.

If you do not have the ServerAssist client mod, use the chat commands. Chat
voting does not need any ServerAssist files.


BLOOD MOON PROTECTION
---------------------

The plugin refuses to start a vote and cancels a running vote when any of these
is true:

* The world is in the blood moon world event.
* The AI Director reports BloodMoonActive.
* The current world time is inside the blood moon window (dusk on the blood
  moon day through dawn on the next day).

The plugin also re-checks the blood moon immediately before changing the world
time. Even /voteskip force cannot skip a blood moon.

A normal vote on the night BEFORE a blood moon is allowed. It jumps to the next
morning, which does not remove the blood moon that starts that evening.


CONFIGURATION
-------------

Oxide creates oxide/config/VoteSkipNight.json on first start. Delete it and
reload the plugin to regenerate the defaults.

  ChatPrefix               "[VoteSkip]"
      Prefix added to all chat messages. Use "" for no prefix.

  RequiredYesPercent       90
      Percentage of currently eligible online players that must vote yes.
      Values are clamped to 1-100.

  VoteDurationSeconds      120
      How long a vote stays open. Values are clamped to 10-3600.

  CooldownSeconds          300
      Cooldown after a passed or failed vote. 0 disables it.

  CancelCooldownSeconds    30
      Short cooldown after a vote is cancelled, to prevent start/cancel spam.

  MinimumOnlinePlayers     1
      Minimum number of eligible connected players needed to start a vote.

  DawnBufferHours          1
      Hours after the normal dawn time to jump to. 0 jumps exactly to dawn.

  AutoVoteYesOnStart       true
      Count the player who starts the vote as the first yes.

  IgnoreSleepingPlayers    true
      Exclude sleeping players from the eligible voter count and block their
      votes. They can bypass the exclusion with voteskip.bypasssleepers.

  GrantUseToDefaultGroup   true
      Grant voteskip.use to the default players group so the ServerAssist panel
      is visible to normal players. Admins always see the panel.


PERMISSIONS
-----------

voteskip.use
  Shows the Vote Skip Night panel in ServerAssist. Granted to the default players
  group automatically when GrantUseToDefaultGroup is true. Admins always see the
  panel. Chat commands do not require this permission.

voteskip.admin
  Allows /voteskip cancel on any vote, /voteskip force and /voteskip reload.
  Granted to the default administrators group automatically on startup.

voteskip.bypasssleepers
  A sleeping player with this permission is still counted in the vote and can
  vote while sleeping. It is not granted automatically.

Normal chat voting does not require a permission. Everyone who is connected and
eligible can start a vote and vote yes or no.


NOTES
-----

* The vote threshold is based on eligible connected players, not on all players
  who have ever joined the server.
* On a 1-player server a single yes vote is 100% and passes immediately.
* Time is changed with World.SetTimeJump, the same game call used by the
  vanilla settime command. Clients receive the new time through the normal
  world-time update.
* The plugin does not change day length, blood moon frequency, or any server
  setting.


TROUBLESHOOTING
---------------

"VoteSkipNight.cs did not compile"
  Check the server log for the Oxide compiler line number. The plugin targets
  the current 7 Days to Die server assembly and current Oxide build.

"Votes can only be skipped at night"
  The plugin uses the game's own day/night calculation, which depends on
  DaylightLength and the world's dusk/dawn hours.

"The blood moon cannot be skipped"
  This is intentional. The message appears when a vote is attempted during, or
  a force-skip is requested during, the blood moon window.

No config file appears
  Oxide only writes the config after the plugin loads successfully. Check the
  server log for [VoteSkipNight] messages.

The ServerAssist panel is missing
  Make sure ServerAssist is loaded, the player has voteskip.use (or is an admin),
  and the player has the ServerAssist client mod. Reload VoteSkipNight or
  ServerAssist and check the server log for the registered GUI hook message.
  If your ServerAssist build is too old to support GUI hooks, update ServerAssist,
  or create a shared command button with target "say /voteskip".

Icons show a hammer
  The client is missing the Server Assist Art PNGs. Install AAZ_ServerAssistArt
  (or copy UIAtlases/UIAtlas into the Server Assist mod folder) on the server and
  every client. Verify the sprite names have no .png extension.

"I am sleeping and cannot vote"
  This is intentional while IgnoreSleepingPlayers is true. Grant
  voteskip.bypasssleepers to that player, or set IgnoreSleepingPlayers to false.

The ServerAssist panel opens with "Unknown action"
  The server is running an older VoteSkipNight build. Replace the .cs file and
  reload the plugin.

Command alias conflict
  Another plugin already uses /voteskip, /skipnight or /votenight. Edit the
  AddCovalenceCommand line near the top of VoteSkipNight.cs, then reload the
  plugin. The command name in the file and the file name do not need to match.


