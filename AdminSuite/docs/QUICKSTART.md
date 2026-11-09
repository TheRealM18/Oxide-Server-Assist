# AdminSuite Quick Start

AdminSuite is a server-only Oxide plugin that registers a GUI hook with
ServerAssist. It does not need a client DLL of its own.

## What you need

Server:
- `ServerAssist` client mod in the server's Mods folder
- `oxide/plugins/ServerAssist.cs` 2.0.2+
- `oxide/plugins/AdminSuite.cs`

Every client:
- `ServerAssist` client mod

## Item icons

AdminSuite sends item icon names, not baked textures. The ServerAssist client
resolves each one with `ItemClass.GetIconName()`, so vanilla items and items from
installed item mods show their real textures automatically. Clients should have
the same item mods as the server so modded item icons resolve. If an item has no
icon, AdminSuite falls back to a category icon such as FOOD, MED, TOOL or BLOCK.

## Install

1. Stop the server.
2. Copy `oxide/plugins/AdminSuite.cs` into `oxide/plugins`.
3. Start the server.
4. Join and press F7/F6 or type `/as`.
5. If the "Admin Suite" row is missing, grant the staff permissions below.

## Minimum staff grants

Grant these to a moderator group:

```
oxide.grant group moderators serverassist.use
oxide.grant group moderators adminsuite.use
oxide.grant group moderators adminsuite.players.view
oxide.grant group moderators adminsuite.players.kick
oxide.grant group moderators adminsuite.players.mute
oxide.grant group moderators adminsuite.players.freeze
oxide.grant group moderators adminsuite.logs
```

Oxide administrators automatically receive `adminsuite.admin`, which bypasses
every granular check.

## Most useful permissions

| Permission | Unlocks |
| --- | --- |
| `adminsuite.use` | Open the menu |
| `adminsuite.admin` | Everything |
| `adminsuite.players.view` | Player list and profiles |
| `adminsuite.players.teleport` | Teleport self / send players |
| `adminsuite.players.bring` | Bring a player |
| `adminsuite.players.spawnontop` | Spawn above a player |
| `adminsuite.players.spectate` | Spectate a player |
| `adminsuite.players.kick` | Kick |
| `adminsuite.players.ban` | Ban / unban |
| `adminsuite.players.mute` | Mute / unmute |
| `adminsuite.players.freeze` | Freeze / unfreeze / jail / timeout |
| `adminsuite.players.kill` | Kill |
| `adminsuite.players.heal` | Heal / set health / heal all |
| `adminsuite.players.feed` | Feed / set food and water / feed all |
| `adminsuite.players.god` | Toggle god mode |
| `adminsuite.players.fly` | Fly in single-player; debug menu on dedicated |
| `adminsuite.players.inventory` | Inspect / repair items |
| `adminsuite.players.give` | Give items / creative menu |
| `adminsuite.players.clearinventory` | Clear or delete items |
| `adminsuite.players.takeinventory` | Take all items |
| `adminsuite.players.snapshot` | Capture snapshots |
| `adminsuite.players.rollback` | Restore snapshots |
| `adminsuite.players.warn` | Warnings |
| `adminsuite.players.notes` | Staff notes |
| `adminsuite.players.buffs` | Buff tools |
| `adminsuite.world` | Time, weather, hordes, airdrops, resets |
| `adminsuite.logs` | Audit logs |
| `adminsuite.snapshots` | Snapshot list / cleanup |
| `adminsuite.server` | Save, announce, restart warning, shutdown |
| `adminsuite.warps` | Warps |
| `adminsuite.moderation.banlist` | Ban list |
| `adminsuite.moderation.adminlist` | Server admin list |
| `adminsuite.moderation.whitelist` | Whitelist |
| `adminsuite.moderation.kickall` | Kick all |
| `adminsuite.reload` | Reload |

## Ten things to try first

1. `/as` and open **Players**.
2. Click a player and use **Teleport To**, **Bring** and **Spawn On Top**.
3. Open **More...** and use **Kick**, **Ban**, **Mute** or **Timeout**.
4. Open **Staff Tools** and try **Warn**, **Note** and **Snapshot**.
5. Open **Inventory** to inspect the toolbelt, backpack and equipment.
6. Use **Take All** and **Repair** on a test player.
7. Open **Buffs** to apply/remove a buff or set health/food.
8. Open **World** and set day/night, clear weather or send an airdrop.
9. Open **Logs** and filter by chat, deaths or admin actions.
10. Open **Snapshots**, restore a snapshot, and confirm the pre-rollback
    snapshot was captured automatically.

## Notes

- Destructive GUI actions ask for confirmation.
- Destructive chat commands require the word `confirm` at the end.
- A snapshot is taken before kick, ban, clear, take all, delete item and
  rollback when `SnapshotBeforeDestructive` is true.
- The inventory view is a live server-side inspector. It is not a
  drag-and-drop remote inventory window; that would require a client mod.

## Handy chat commands

```
/as ontop <player>        spawn on top of a player
/as spectate <player>     teleport near and spectate
/as inv <player>          open the inventory inspector for a player
/as jail <player>         teleport to jail and freeze
/as creative              toggle the creative menu on your client
/as debug                 toggle the debug menu on your client
/as spectator             toggle spectator mode
/as fly                   fly in single-player; opens debug menu on dedicated
/as playerlist            open the vanilla player list
/as tppoi                 open the POI teleporter
/as give <player> <item> [amount] [quality]
/as admin add <player> [level] | /as admin remove <player>
/as whitelist add <player> | /as whitelist remove <player>
/as clearmutes | /as clearwarnings
```

`/as give` accepts both internal names (`9mmBullet`) and display names with
spaces (`9mm Bullet`). If the backpack and toolbelt are full, the remainder is
dropped at the player's feet with the vanilla give command.


