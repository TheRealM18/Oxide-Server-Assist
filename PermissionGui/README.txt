PermissionGui 3.5.0 for 7 Days to Die
=======================================

PermissionGui is a server-side Oxide plugin and an ServerAssist GUI page. It uses
only Oxide's Permission library and Oxide's own data files. It no longer reads,
writes or creates oxide/data/PermissionGui/data.json.

The GUI is opened from ServerAssist:

    F7 -> Permission Manager
    /permgui
    /perm gui

REQUIRES
--------
  - 7 Days to Die V3.4.x, EAC off
  - Oxide/uMod on the server
  - ServerAssist client mod + ServerAssist Oxide plugin for the GUI
    (the /perm commands work without ServerAssist)


WHAT THE GUI CAN DO
-------------------
Home
  - Groups, Users, Tracks, Permissions, Defaults
  - Apply Defaults
  - Reload Config

Groups
  - List/filter groups
  - Create group
  - Rename group
  - Delete group
  - Set title, rank and parent group
  - View/add/remove members
  - View direct and inherited permissions
  - Grant/revoke permissions, including from the permission search field

Users
  - List/filter known online and offline users
  - View groups and direct permissions
  - Add/remove groups
  - Grant/revoke direct permissions
  - View effective permissions (direct + all groups, full parent chain)
  - View per-user track membership
  - Enroll/promote/demote the user along a track
  - Add, list and remove temporary permissions with an expiry

Tracks
  - Tracks are stored in oxide/config/PermissionGui.json
  - Create/delete tracks
  - Add/remove groups in the ladder
  - Enroll a user into a track
  - Promote/demote a user along a track
  - Promotion removes the other groups in that track and keeps unrelated groups
  - By default the default group is removed so the player keeps one track group;
    set AddDefaultGroupForTrackUsers to true to keep it alongside the track

Permissions
  - Search all registered permissions from the search bar
  - See how many groups and users hold each permission
  - Select a permission and add/remove it from a group or user
  - Permission filters also exist inside each group/user permission page
  - Rows toggle direct grants; inherited grants are marked separately
  - Nodes owned by a ServerAssist panel show that mod's registered card icon
  - AdminSuite tool nodes use the matching tool/screen icon (kick, ban, teleport, logs, etc.)

Defaults
  - Config-driven default group and default permissions
  - Add/remove defaults
  - Set the default group
  - Apply defaults now (forces a reapply)
  - Reset the once-only applied markers
  - Toggle once-per-player and track default-group behavior
  - Reload config
  - Defaults are applied on load and on player connect, and mirrored directly
    to the player when GrantDefaultsDirectlyToPlayer is true


ADMIN WILDCARD: oxide.*
-----------------------
Full admin access requires BOTH:

    permissiongui.admin
    oxide.*

The special admin grant adds both, plus all currently registered permissions
when GrantAllPermissionsToAdmin is true.

The special admin grant is:

  - GrantAdminPermissionsToAdminGroup for Oxide's admin group
  - AutoGrantAdmin for specific SteamID64s

A player who is given permissiongui.admin through DefaultPermissions or a
manual grant, but who does not have oxide.*, is NOT a full admin. This is
intentional. /perm whoami shows:

    Admin: True/False
    Admin wildcard (oxide.*): True/False

Oxide's normal grant API treats "oxide.*" as a wildcard pattern and would not
store it literally, so the plugin writes the literal marker directly to Oxide's
group/user permission data. The marker survives restarts.

If GrantAllPermissionsToAdmin is true, Oxide's "*" expansion is also applied to
the admin group/player. New permissions registered by a plugin loaded later are
picked up by /perm apply or the next server start.


CONFIG
------
oxide/config/PermissionGui.json

{
  "ChatPrefix": "Permissions:",
  "DefaultGroup": "default",
  "DefaultPermissions": [
    "permissiongui.use"
  ],
  "ApplyDefaultsOnLoad": true,
  "ApplyDefaultsOnPlayerConnect": true,
  "ApplyDefaultsOncePerPlayer": true,
  "GrantDefaultsDirectlyToPlayer": true,
  "CreateDefaultGroup": true,
  "AddDefaultGroupForTrackUsers": false,
  "GrantAdminPermissionsToAdminGroup": true,
  "AdminWildcard": "oxide.*",
  "RequireWildcardForAdmin": true,
  "GrantAllPermissionsToAdmin": true,
  "AutoGrantUse": [],
  "AutoGrantAdmin": [
    "76561198044563889"
  ],
  "Tracks": {
    "staff": [
      "default"
    ]
  },
  "MaxRows": 10
}

Field notes:
  DefaultGroup
      Group that receives the default permissions. "default" is standard.
      Set "" to follow Oxide's configured default player group.

  DefaultPermissions
      JSON array of permissions granted to DefaultGroup and mirrored directly
      to connecting players when GrantDefaultsDirectlyToPlayer is true.

  ApplyDefaultsOnLoad / ApplyDefaultsOnPlayerConnect
      When to apply the defaults automatically.

  ApplyDefaultsOncePerPlayer
      When true, the default group and direct default permissions are applied
      to a player only once. Removing the default group or a default permission
      later is permanent; the next connect or plugin reload will not add it
      back. Use Defaults -> Apply Now or /perm apply for a deliberate reapply,
      or /perm defaults reset <player|all> to clear the once-only markers.
      On the first run of 3.5.0, known players are marked as already defaulted
      so the default group is not re-added to the existing player base. New
      players still receive it once.

  GrantDefaultsDirectlyToPlayer
      Grant the defaults directly to the player in addition to DefaultGroup.

  CreateDefaultGroup
      Create DefaultGroup if it does not exist.

  AddDefaultGroupForTrackUsers
      When false, a player enrolled in a track keeps only the track group; the
      default group is removed unless it is part of the track ladder. When
      true, the default group is kept alongside the track group.

  GrantAdminPermissionsToAdminGroup
      The special admin grant for Oxide's admin group.

  AdminWildcard
      Literal marker required for full admin access. Default "oxide.*".

  RequireWildcardForAdmin
      If true, permissiongui.admin is not enough; the player also needs
      AdminWildcard. Server console always bypasses this.

  GrantAllPermissionsToAdmin
      When granting special admin access, also grant every registered permission
      through Oxide's "*" expansion.

  AutoGrantUse / AutoGrantAdmin
      Direct per-character grants. AutoGrantAdmin is the special admin grant.

  Tracks
      Track name -> ordered low-to-high list of group names. Tracks live in the
      config, not data.json. Tracks from an old data.json are not migrated;
      recreate them in the config or in the Tracks GUI page before running
      /perm cleanup.

  MaxRows
      GUI rows per page, 1-10.

A ready-to-copy example is Config/PermissionGui.example.json.


COMMANDS
--------
    /permgui
    /pgui
    /perm gui
    /perm defaults
    /perm adddefault <permission>
    /perm removedefault <permission>
    /perm setdefaultgroup <group>
    /perm search <text>
    /perm node <text>
    /perm apply
    /perm data
    /perm save
    /perm reload
    /perm cleanup
    /perm whoami

The GUI does all group/user/track/permission edits. Chat commands are for the
defaults and diagnostics.

  /perm defaults
      Show default group and default permissions.

  /perm adddefault <permission>
      Add a registered permission to DefaultPermissions, save config, apply.

  /perm removedefault <permission>
      Remove it from DefaultPermissions, revoke from DefaultGroup and known
      direct holders.

  /perm setdefaultgroup <group>
      Change DefaultGroup. Online players are added to the new group but are not
      removed from the old group automatically.

  /perm search <text>
      Print matching results split into:
        - "Grantable permission nodes:" with NODE: <node>
        - "Commands:" with CMD: <command> | NODE: <node or NONE>
      Commands also show aliases, source and 7DTD admin level.

  /perm node <text>
      Focused node lookup. If an exact registered permission matches, it prints
      NODE: <node> and how to grant it. Otherwise it prints the closest command
      matches with NODE: <node or NONE>. If a 7DTD/mod command has no Oxide
      permission node, the output says so explicitly and shows its admin level.

  /perm apply
      Re-apply defaults and special admin grants now. This forces the defaults
      back even if ApplyDefaultsOncePerPlayer is true.

  /perm defaults reset <player|all>
      Clear the once-only default markers. The player(s) receive defaults again
      on their next connect or on /perm apply.

  /perm temp <player> <permission> <duration>
      Grant a temporary direct permission. Durations accept s, m, h, d and w:
      30m, 2h, 7d, 1d12h. A bare number means minutes.

  /perm temp remove <player> <permission>
      Remove an active temporary permission immediately.

  /perm temp list [player]
      List active temporary permissions and their remaining time.

  /perm data
      Show defaults, track count, AdminWildcard settings, once-only markers,
      temp permission count, last apply/save, Oxide data paths, and Oxide counts.

  /perm save
      Force permission.SaveData().

  /perm reload
      Reload oxide/config/PermissionGui.json and apply defaults.

  /perm cleanup
      Delete the legacy oxide/data/PermissionGui/data.json.

  /perm whoami
      Print your exact id, admin/use flags, oxide.* result, direct permissions
      and groups. Works without permissiongui.use.


PERMISSION / COMMAND SEARCH
---------------------------
ServerAssist -> Permission Search, or:

    /perm search <text>

The search index is built from loaded runtime registries:

  1. Oxide registered permissions
     permission.GetPermissions(), with the owning plugin when reflection can
     resolve it.

  2. Oxide plugin commands
     Each plugin's registered commands and their PermissionsRequired entries
     are read from Oxide's command table (reflection). This maps commands such
     as plugin chat commands to their permission nodes.

  3. 7 Days to Die / mod console commands
     SdtdConsole.GetCommands() is enumerated. Each command shows:
       - command name and aliases
       - description
       - source class and assembly (so you can tell which mod owns it)
       - its effective 7DTD admin level

  4. serveradmin.xml command permissions
     GameManager.adminTools.Commands.GetCommands() entries that are not already
     in the console command list are included with their configured level.

Select a permission row to add/remove it from a group or user. Select a command
row to see its full details. A 7DTD/mod console command is controlled by its
admin level, not by an Oxide permission node; the GUI says so when selected.
An Oxide plugin command may also expose a permission node, in which case
selecting the command selects that permission for granting.

Every permission row uses one of three category icons:
sa_nav_server for oxide.* nodes, sa_nav_world for 7daystodie.* and game.*
nodes, and sa_plugin_hot_reload for every other registered plugin node. The
7 Days to Die command list uses sa_nav_world for both SdtdConsole commands and
serveradmin.xml command permissions. These sprites ship in
AAZ_ServerAssistArt 1.1.0 or newer. The 1.1.1 pack also ships the
sa_perm_oxide, sa_perm_game and sa_perm_plugin aliases. Icons are optional; a
node with no matching icon keeps the normal row layout.

The same art is used on PermissionGui's own sub-menu rows: Groups and Users use
sa_nav_players, Tracks uses sa_nav_warps, Permission Search uses
sa_permission_gui and Defaults uses sa_nav_server. Action buttons remain
text-only.

Search terms are case-insensitive. Multiple space-separated terms are treated
as OR terms.

This cannot discover a mod that uses a private permission system and never
registers an Oxide permission, a console command, or a serveradmin permission.
For those mods, check their documentation/config.


TEMPORARY PERMISSIONS
---------------------
Temp permissions are direct user grants with an expiry. They are stored in
oxide/data/PermissionGui/state.json, survive restarts and are removed by a
30-second expiry timer. Granting the same permission again extends its expiry.

Grants:
    /perm temp <player> <permission> <duration>
    /perm temp list [player]
    /perm temp remove <player> <permission>

The GUI path is User -> Temp Perms -> Add Temp. Enter the permission node, then
the duration. Active temp permissions show "temp <remaining>" in the user's
direct permission list. Removing a temp permission, or toggling the same direct
permission, clears the temp entry.

Durations:
    30s   30 seconds
    15m   15 minutes
    2h    2 hours
    7d    7 days
    2w    2 weeks
    1d12h 1 day and 12 hours


DATA
----
Oxide owns all permission data:

    oxide/data/oxide.users.data   - player groups + direct permissions
    oxide/data/oxide.groups.data  - group permissions, ranks, parents

Those are binary protobuf files. Do not edit them by hand.

PermissionGui config / track data:

    oxide/config/PermissionGui.json
    oxide/data/PermissionGui/state.json   - once-only markers + temp permissions

state.json is created automatically. It is not the old data.json.

Old file, no longer used:

    oxide/data/PermissionGui/data.json

Delete it with:

    /perm cleanup


INSTALL
-------
1. Install ServerAssist on the server and every client.
2. Copy oxide/plugins/PermissionGui.cs into the server's oxide/plugins folder.
3. Restart the server.
4. Edit oxide/config/PermissionGui.json, then:

       oxide.reload PermissionGui

Do NOT install a PermissionGui client mod. PermissionGui is server-only now.
Remove any old PermissionGui client mod and its Config/XUi_InGame files from
Mods.


VERIFY
------
Server log:

    Loaded plugin PermissionGui v3.5.0

In game:

    /perm whoami
    /perm data
    /permgui

Your character needs either:

  - to be in Oxide's admin group, which receives the special admin grant, or
  - to be listed in AutoGrantAdmin


TROUBLESHOOTING
---------------
GUI page is missing
    ServerAssist is not installed/loaded, or the hook failed to register. Check
    the server log. /perm commands still work.

Admin: False even though permissiongui.admin is granted
    RequireWildcardForAdmin is true. You also need oxide.*. Use AutoGrantAdmin
    or the admin group special grant.

A permission cannot be added
    Only permissions registered by a loaded plugin can be granted. New plugins
    may need /perm apply or a restart.

Track groups are missing
    The track references a group that does not exist. Create the group or remove
    the missing group from the track.

PermissionGui.json is reset or not read
    The plugin logs an error and does not overwrite a config it cannot parse.
    Fix the JSON and run oxide.reload PermissionGui.

data.json still exists
    It is ignored. Run /perm cleanup.

Old PermissionGui window still appears
    Remove the old PermissionGui client mod and its XUi files from Mods.

