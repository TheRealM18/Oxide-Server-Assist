Player Profiles for 7 Days to Die (V3.4)
=========================================
Plugin version 1.0.0
Server Assist client mod 2.0.1 or newer

Multiple saved characters on one account. A profile stores the game's own
PlayerDataFile: inventory, backpack, equipment, held item, level, XP, perks,
books, quests, challenges, buffs, health/food/water, waypoints, bedroll spawn
points, position, kills/deaths/score/playtime, crafting queue, and owned
entities. Permissions and groups stay on the account, as requested.

With the updated Server Assist client mod, switching is in place: no
disconnect, no re-login. Without that client mod, or when starting a brand-new
profile, the plugin falls back to a safe reconnect.

Server Assist maintainers: update.md in this folder lists the exact Server
Assist plugin and client-mod patches, source hashes and acceptance checks.


WHAT THIS IS NOT
----------------

This is one Steam account with several characters. It is not a second account.

Shared across all profiles:

  * Oxide permissions, groups and admin status
  * Land claims, allies, parties and other PersistentPlayerData
  * World chests, bases, vehicles, traders and loot
  * Map exploration (a separate per-player map database)
  * Data stored by other plugins under your Steam ID (economy, kits,
    DailyRewards, AuctionHouse, Twitch/Discord links, and so on)
  * Your Steam name and login identity

Anything another plugin stores by player ID is shared unless that plugin is
changed to use the profile-scoped ID API described at the end of this file.

Character appearance (the player model/skin) is not changed by an in-place
switch. On a reconnect it follows the saved character profile when the server
has PersistentPlayerProfiles enabled. Inventory, armor and cosmetics do change.

Vehicles and drones are world entities. The owned-entity references are stored
per profile, but the actual vehicle/drone objects remain in the world. Exit a
vehicle or drone before switching. Land claims, allies and bedroll ownership
remain account/world-level.


REQUIREMENTS
------------

  * 7 Days to Die V3.4 (built against the 3.4 managed assemblies).
  * Oxide / uMod.
  * ServerAssist plugin 2.0.1 or newer and ServerAssistMod 2.0.1 or newer on
    the server and every client for instant switching. Older combinations
    still join and use the reconnect path; the updated client mod stays silent
    when the server plugin is older. Server Assist maintainers: see update.md
    in this folder for the exact companion patches and acceptance checks.
  * EAC must be off.
  * The ServerAssistArt UI atlas on clients is recommended for the panel icons
    (sprite:sa_profile_panel, sprite:sa_profile_identity and the rest of the
    sa_profile_* set). Asset makers: see icon-request.md for the exact eight
    sprites and sheet plan.


INSTALL - SERVER
----------------

1. Stop the server.
2. Install the ServerAssist plugin and the updated AA0_ServerAssistMod folder.
3. Copy the plugin:

       oxide/plugins/PlayerProfiles.cs

4. Start the server.
5. Check the log for:

       [PlayerProfiles] loaded. Permissions: playerprofiles.slots.1 - 10.
       [PlayerProfiles] registered the profiles panel with Server Assist.

The plugin writes oxide/config/PlayerProfiles.json on first start. It creates
the players/administrators default grants described below.


INSTALL - CLIENTS
-----------------

Install the same updated AA0_ServerAssistMod folder used by the server. There is
no separate PlayerProfiles client mod and no extra net-package type, so the
existing connection handshake keeps working.

If a client keeps the old ServerAssistMod, they can still join. The Profiles
panel will show "reconnect required" for switches; the server will kick them
with a reconnect message when they switch.


USING IT
--------

  F7 / F6        Open Server Assist.
  /profiles      Open the Profiles panel, or list profiles if Server Assist
                 is unavailable.
  /profile ...   Command form.

Inside the panel:

  Profile cards  One card per profile. The active profile has an ACTIVE badge
                 and a gold tint. A new profile has a NEW badge.
  New            Create a profile. New profiles start as level 1 with an empty
                 inventory. The first switch to a new profile reconnects once
                 so 7DTD can run its normal new-character spawn.
  Rename         Give a profile a new name. The saved file is not moved.
  Delete         Delete an inactive profile. The payload is archived first.
  Save Current   Capture the active profile immediately.
  Switch         Load the selected profile. With the updated client mod this
                 happens in place.
  Refresh        Rebuild the list.
  Reload         Admin only: reload config and permissions.
  Back           Return to Server Assist.

The panel uses one text input. Type a name in New or Rename and press Submit
(or press the Create/Save button after submitting).


COMMANDS
--------

  /profile list
  /profile use <name>            (alias: switch)
  /profile new <name>            (alias: create)
  /profile rename <old> <new>
  /profile delete <name> confirm
  /profile save
  /profile reload                admin only
  /profile help


PERMISSIONS
-----------

  playerprofiles.use
      Open the panel and use commands. Granted to the players group by
      default.

  playerprofiles.slots.1 ... playerprofiles.slots.N
      The maximum number of profiles the player may create. N is MaxSlots
      (default 10, hard cap 50). The highest granted node wins.
      Examples:
        playerprofiles.slots.1  = one profile
        playerprofiles.slots.2  = two profiles (default for players)
        playerprofiles.slots.10 = ten profiles

  playerprofiles.slots.*
      All configured slots.

  playerprofiles.admin
      Admin override. Grants MaxSlots, the Reload action and /profile reload.
      Granted to the administrators group by default.

Defaults:

  players          playerprofiles.use, playerprofiles.slots.2
  administrators   playerprofiles.use, playerprofiles.admin,
                   playerprofiles.slots.10

Existing profiles are never deleted when a slot permission is reduced. New
profiles cannot be created until the player is under the limit again.


CONFIG
------

oxide/config/PlayerProfiles.json:

  ChatPrefix                 Chat prefix for command replies.
  PermissionPrefix           Base permission node, default "playerprofiles".
  UsePermission              Panel/command permission.
  AdminPermission            Admin override node.
  MaxSlots                   Highest slot node registered. Default 10, cap 50.
  DefaultSlots               Slots for players with no slot node. Default 2.
  GrantDefaultSlotsToPlayers Grant slots.DefaultSlots to the players group.
  GrantAdminToAdministrators Grant admin and slots.MaxSlots to administrators.
  MaxNameLength              Profile name limit. Default 24, cap 48.
  BootstrapProfileName       Name for the automatically created first profile.
  NewProfileStartsFresh      true: new profiles start fresh (recommended).
                             false: copy the current character into the new
                             profile.
  AllowSeamlessSwitch        true: use the in-place client-mod protocol when
                             the client supports it.
  ReconnectMessage           Kick message for the reconnect path. Supports
                             {profile} and {player}.
  SwitchCooldownSeconds      Minimum time between switch attempts. Default 5.
  DisallowWhileDead          Block switching while dead. Default true.
  DisallowWhileInVehicle     Block switching while attached to a vehicle or
                             drone. Default true.
  KeepPositionOnSwitch       false: the profile's saved position is restored.
                             true: keep the current position after switching.
  AutosaveSeconds            Capture online active profiles this often. Default
                             60. Also captures on server save and disconnect.
  SwitchTimeoutSeconds       In-place handshake timeout. Default 20.
  ChunkBytes                 Profile payload chunk size. Default 24576.
  BackupCount                0-2 rotating backups per profile file. Default 2.
  ArchiveDeletedProfiles     Copy deleted payloads to deleted/ before removing.
  RequireSameModSet          Refuse to switch to a profile saved with a
                             different mod set. Default true.
  RequireSameGameVersion     Refuse to switch across game versions. Default
                             false (7DTD can usually read older files).
  Debug                      Extra log lines for captures and switches.


DATA
----

  oxide/data/PlayerProfiles/<safe-player-id>/state.json
  oxide/data/PlayerProfiles/<safe-player-id>/<profile-id>.ttp
  oxide/data/PlayerProfiles/<safe-player-id>/<profile-id>.ttp.bak1
  oxide/data/PlayerProfiles/<safe-player-id>/<profile-id>.ttp.bak2
  oxide/data/PlayerProfiles/<safe-player-id>/deleted/...

The .ttp files use the game's own save container:

  "ttp\0" | version byte | PlayerDataFile.Write(..., Persistency)

The game's ItemStack format is used, so quality, durability, ammo, mods,
cosmetic slots and other item data survive automatically.

The plugin never edits the live vanilla .ttp in place. It writes a temporary
file and replaces the target atomically. The vanilla save is the fallback copy.

state.json holds the profile names, IDs, active profile, pending switch,
metadata (level, deaths, playtime, bytes, saved time, game version and mod set
hash) and the player name. It does not hold permissions.


SWITCH FLOW (IN PLACE)
----------------------

  1. The player confirms a switch in the Profiles panel or by command.
  2. With the updated client mod:
       server -> client   @pp|prepare
       client             freezes input, locks outgoing player-data syncs, and
                          sends a full PlayerDataFile
       client -> server   @pp|ready
       server             captures the old profile, validates the target, and
                          streams the new .ttp payload in chunks
       client             applies the payload locally and refreshes the HUD,
                          inventory, quest tracker and crafting queue, while
                          staying frozen and not syncing
       client -> server   @pp|applied
       server             applies the same payload to the server entity, writes
                          the new vanilla .ttp, commits state, and refreshes the
                          active profile badge
       server -> client   @pp|done
       client             unfreezes and reopens the Profiles panel
  3. On any failure the old profile is kept. A partially applied client is
     kicked back to a reconnect so both sides resync.

The protocol is carried inside the existing Server Assist request/response
packets. Client-to-server messages carry the panel id, for example:

  @pp|playerprofiles|hello|1
  @pp|playerprofiles|ready|<token>

Server Assist strips the panel id and routes the payload to the PlayerProfiles
hook. It does not add a NetPackage type, so it does not change the server
package-ID table and cannot disconnect older clients with "Unknown package
type".


SWITCH FLOW (RECONNECT FALLBACK)
--------------------------------

Used for a new profile, for an old client mod, or when seamless switching is
disabled:

  1. The server captures the current profile.
  2. The server records a pending reconnect target.
  3. The server kicks the player with ReconnectMessage.
  4. On the next connect, OnUserApprove writes the target .ttp (or deletes the
     vanilla file for a new profile) before the vanilla spawn load.
  5. The game loads the character normally.

A new profile needs this once. After its first spawn it has data and later
switches can be in place.


CRASH AND DISCONNECT SAFETY
---------------------------

  * Autosave captures online active profiles every AutosaveSeconds.
  * OnServerSave and OnPlayerDisconnected also capture.
  * On restart, if the vanilla .ttp is newer than the profile file, it is
    adopted into the active profile.
  * If the profile file is newer, it is materialised over the vanilla .ttp
    before spawn.
  * An interrupted in-place switch is marked as pending. On the next login the
    server reverts to the profile the player came from, so a half-applied
    switch cannot become the active character.
  * Unreadable profiles fall back to .bak1/.bak2. A corrupt vanilla save is
    never allowed to lock a player out; vanilla fallbacks still apply.


TROUBLESHOOTING
---------------

The Profiles card is not on the Server Assist board.
  Check that playerprofiles.use is granted, PlayerProfiles.cs loaded without
  errors, and the log shows the panel registration line.

The card opens but says "reconnect required" for every switch.
  The server plugin and/or client mod is older than 2.0.1. Install both the
  2.0.1 or newer ServerAssist plugin and ServerAssistMod on the server and the
  client. Switches still work, but they reconnect.

"Your current profile could not be saved."
  The server could not read the live PlayerDataFile. Check the log for capture
  errors. The switch is cancelled so no progress is lost.

"That profile is dead. Reconnect to respawn it."
  A dead profile cannot be applied in place. Switch to it through the reconnect
  path (delete the client-mod capability by using an old mod, or wait for it to
  respawn), or reconnect after selecting it.

"That profile was saved with a different mod set."
  Item IDs are load-order integers. Restore the original mod set, or set
  RequireSameModSet to false if you understand the risk.

A new profile reset to level 1.
  This is expected for NewProfileStartsFresh. Set it to false if new profiles
  should copy the current character instead.

The switch happened but my quest tracker or inventory looked stale for a moment.
  The client closes cursor windows and rebuilds the quest tracker, toolbelt,
  backpack and equipment. If a window was open during the switch, reopen it.
  Report the exact window and game build if it stays stale.

Vehicles moved or ownership changed.
  Owned entities are stored per profile and replaced on switch. World vehicles
  are still world objects. Exit vehicles before switching. Land claims and
  allies are account-level and never switch.


FOR PLUGIN AUTHORS
------------------

The plugin exposes:

  GetActiveProfile(string playerId)      -> profile id or null
  GetActiveProfileName(string playerId)  -> display name or null
  GetProfileScopedId(string playerId)    -> "<playerId>#<profileId>" or playerId

Hooks:

  OnPlayerProfileCreated(IPlayer player, string profileId, string name)
  OnPlayerProfileRenamed(IPlayer player, string profileId, string oldName, string newName)
  OnPlayerProfileDeleted(IPlayer player, string profileId, string name)
  OnPlayerProfileSwitch(IPlayer player, string fromProfileId, string toProfileId, bool inPlace)

To store per-profile data in another plugin, key it by GetProfileScopedId
instead of the raw player ID. Without that change, the other plugin's data is
shared across profiles.

Permissions and groups deliberately remain account-wide.
