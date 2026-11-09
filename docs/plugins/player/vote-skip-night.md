# Vote Skip Night

> **Version:** 1.2.0 · **Type:** Server-side Oxide plugin with an optional Server Assist panel (no client DLL or XUi files) · **Source:** [VoteSkipNight.cs](https://github.com/TheRealM18/Oxide-Server-Assist/blob/main/VoteSkipNight/oxide/plugins/VoteSkipNight.cs)

Vote Skip Night lets players vote to skip the current night. A vote passes only when at least 90% of the eligible players who are online vote yes. Blood moon nights can never be skipped.

The plugin is server-only. It does not need a client DLL and does not change XUi files. If Server Assist is installed, Vote Skip Night adds a **Vote: Skip Night** panel inside the existing Server Assist window; chat voting works without any Server Assist files.

---

## Requirements

| Requirement | Detail |
|---|---|
| Game | 7 Days to Die with Oxide / uMod |
| Server | `oxide/plugins/VoteSkipNight.cs` compiled by Oxide |
| EAC | Off, as with every Oxide plugin |
| Server Assist | Optional for the GUI; chat commands do not need it |
| Server Assist client mod | Required on players who should see the panel |
| Art | Optional `AAZ_ServerAssistArt` or `ServerAssistArt/UIAtlases/UIAtlas` sprites |

!!! note
    The plugin does not change day length, blood moon frequency or any server setting. It only moves the current world time when a vote passes.

---

## Installation

1. Stop the server.
2. Copy `VoteSkipNight.cs` into `oxide/plugins/`.
3. Start the server. Oxide compiles the plugin and creates `oxide/config/VoteSkipNight.json`.
4. Join the server and type `/voteskip` at night.

You can also copy the whole VoteSkipNight package and move its `oxide/plugins/VoteSkipNight.cs` into the server's `oxide/plugins/` folder.

---

## Commands

The command is registered as `/voteskip`, `/skipnight` and `/votenight`.

| Command | Permission | Behaviour |
|---|---|---|
| `/voteskip` | None | At night, start a vote. If a vote is already running, vote yes. Aliases: `/skipnight`, `/votenight` |
| `/voteskip yes` | None | Vote yes, or change a no vote to yes. The starter is counted as yes by default. Aliases: `y`, `start` |
| `/voteskip no` | None | Vote no, or change a yes vote to no. Aliases: `n`, `veto` |
| `/voteskip status` | None | Show the current yes count, required count, no count and time left. Alias: `info` |
| `/voteskip cancel` | Vote starter or `voteskip.admin` | Cancel the running vote |
| `/voteskip gui` | `voteskip.use` or admin | Open the Vote Skip Night panel inside Server Assist. Alias: `panel` |
| `/voteskip force` | `voteskip.admin` | Skip the night immediately. Still refuses a blood moon and still requires night. Alias: `skip` |
| `/voteskip reload` | `voteskip.admin` | Reload `oxide/config/VoteSkipNight.json` |
| `/voteskip help` | None | Show the command list |

Normal chat voting does not require a permission. Everyone who is connected and eligible can start a vote and vote yes or no.

---

## How the vote works

- A vote can only be started while the world is dark.
- The required yes votes are calculated from the eligible players who are online right now:

  ```text
  required = ceil(eligible players * RequiredYesPercent / 100)
  ```

- With 10 eligible players and `RequiredYesPercent` at 90, 9 yes votes are required.
- The result is rounded up, so small servers need every eligible player to vote yes. For example, 3 eligible players require 3 yes votes, and 9 require 9.
- The check is live. If someone leaves, the requirement can drop. If someone joins, the requirement can rise. If someone starts or stops sleeping, the eligible count can change too. A message is sent when the requirement changes.
- A player who disconnects loses their vote and must vote again after reconnecting.
- When the threshold is reached, world time jumps to dawn plus `DawnBufferHours`.
- If the vote expires without enough yes votes, the night continues.
- Only one vote can run at a time. A cooldown prevents immediate repeat votes.

Time is changed with `World.SetTimeJump`, the same game call used by the vanilla `settime` command. Clients receive the new time through the normal world-time update.

---

## Sleeping players

Sleeping players are detected with Oxide's `IPlayer.IsSleeping` flag, which reads the game's `EntityPlayer.IsSleeping` value.

| Setting or permission | Effect |
|---|---|
| `IgnoreSleepingPlayers = true` | Sleeping players are left out of the eligible voter count and cannot vote. This is the default |
| `IgnoreSleepingPlayers = false` | All connected players are counted regardless of the sleeping flag |
| `voteskip.bypasssleepers` | A sleeping player with this permission is still counted and can still vote. It is not granted automatically |

If your server uses a custom sleeping or AFK mod, make sure that mod sets `EntityPlayer.IsSleeping`. If it does not, Vote Skip Night cannot identify those players and treats them as normal online players.

---

## Permissions

| Permission | Purpose |
|---|---|
| `voteskip.use` | Shows the Vote Skip Night panel in Server Assist. Granted to the default `players` group when `GrantUseToDefaultGroup` is true. Admins always see the panel. Chat commands do not require it |
| `voteskip.admin` | Allows `/voteskip cancel` on any vote, `/voteskip force` and `/voteskip reload`. Granted to the default `administrators` group automatically |
| `voteskip.bypasssleepers` | A sleeping player with this permission is still counted and can vote while sleeping. Not granted automatically |

The player also needs `serverassist.use` to open Server Assist itself. The panel is not a replacement for that board permission.

---

## Configuration

File: `oxide/config/VoteSkipNight.json`

| Key | Default | Range / notes |
|---|---|---|
| `ChatPrefix` | `[VoteSkip]` | Prefix added to all chat messages. Use `""` for no prefix |
| `RequiredYesPercent` | `90` | Percentage of currently eligible online players that must vote yes; clamped to 1–100 |
| `VoteDurationSeconds` | `120` | How long a vote stays open; clamped to 10–3600 |
| `CooldownSeconds` | `300` | Cooldown after a passed or failed vote; clamped to 0–86400. `0` disables it |
| `CancelCooldownSeconds` | `30` | Short cooldown after a cancelled vote to prevent start/cancel spam; clamped to 0–3600 |
| `MinimumOnlinePlayers` | `1` | Minimum eligible connected players needed to start a vote; clamped to 1–200 |
| `DawnBufferHours` | `1` | Hours after normal dawn to jump to; clamped to 0–6. `0` jumps exactly to dawn |
| `AutoVoteYesOnStart` | `true` | Count the player who starts the vote as the first yes |
| `IgnoreSleepingPlayers` | `true` | Exclude sleeping players from the eligible count and block their votes |
| `GrantUseToDefaultGroup` | `true` | Grant `voteskip.use` to the default `players` group so normal players can see the panel |

Delete the config and reload the plugin to regenerate the defaults.

---

## Data files

| Path | Purpose |
|---|---|
| `oxide/config/VoteSkipNight.json` | The only runtime file |

The active vote, its voters, the live requirement and the cooldown are held in memory. There is no player data file and no vote history file. A plugin reload or server restart clears a running vote and its cooldown.

---

## Server Assist integration

When Server Assist is loaded, Vote Skip Night registers a GUI hook:

| Item | Value |
|---|---|
| Hook id | `voteskip` |
| Label | `Vote: Skip Night` |
| Description | `Start or join the night-skip vote. Blood moons cannot be skipped.` |
| Permission | `voteskip.use` |
| Plugin name | `VoteSkipNight` |
| Handler | `HandleServerAssistRequest` |
| Icon | `sprite:sa_vote_skip_night` |

The plugin uses `RegisterGuiHook` followed by `SetPanelIcon`, opens the panel with `OpenHookForPlayer`, and unregisters the hook on unload. The row appears on the Server Assist home screen for players with `voteskip.use`; admins always see it.

The panel is rendered inside Server Assist's existing window as icon cards. It shows:

- the current time and whether it is day or night;
- the eligible voter count and the required yes votes;
- the live yes/no counts and time left;
- your current vote;
- the cooldown, if one is active;
- whether you are being counted as sleeping.

Start Vote, Vote Yes, Vote No, Cancel Vote and Refresh are clickable action cards. Admins also get a Force Skip card. The other cards are read-only status cards. The same server-side checks run whether the player clicks or types a command.

See [Panels and hooks](../../integration/api/panels.md) and the [Server Assist product guide](../../core/server-assist.md) for the hook lifecycle.

---

## Blood moon protection

The plugin refuses to start a vote and cancels a running vote when any of these is true:

- the world is in the blood moon world event;
- the AI Director reports `BloodMoonActive`;
- the current world time is inside the blood moon window, from dusk on the blood moon day through dawn on the next day.

The plugin re-checks the blood moon immediately before changing world time. Even `/voteskip force` cannot skip a blood moon.

A normal vote on the night before a blood moon is allowed. It jumps to the next morning, which does not remove the blood moon that starts that evening.

---

## Typical workflows

### Run a normal night vote

1. Wait until the world is dark and it is not a blood moon night.
2. A player types `/voteskip`. The starter is counted as the first yes.
3. Other online, eligible players type `/voteskip yes` or `/voteskip no`, or use the panel.
4. `/voteskip status` shows the live yes count, required count, no count and time left.
5. If the threshold is reached, the world jumps to dawn plus `DawnBufferHours`. If time expires first, the night continues.

### Run a vote through the panel

1. The player opens Server Assist with `F7` or `F6` and clicks the **Vote: Skip Night** card.
2. The panel shows the current state, eligible voters and the player's own vote.
3. The player clicks **Vote Yes**, **Vote No**, **Start Vote** or **Cancel Vote**.
4. Administrators can use **Force Skip** when the server needs an immediate skip.

### Let a sleeping player vote

1. Grant `voteskip.bypasssleepers` to that player.
2. The player remains in the eligible count and can cast a vote while sleeping.
3. Alternatively, set `IgnoreSleepingPlayers` to `false` to count every connected player.

### Tune the threshold for a small server

1. Lower `RequiredYesPercent` if unanimous votes are impractical.
2. Keep in mind that the required count uses `ceil`, so a 3-player server with 90% still needs all 3 yes votes.
3. Adjust `MinimumOnlinePlayers` if votes should not start with too few players online.

### Force a skip as an administrator

1. Confirm it is night and not a blood moon.
2. Run `/voteskip force` or click **Force Skip** in the panel.
3. The world jumps to dawn plus `DawnBufferHours`; the normal cooldown is applied.

---

## Troubleshooting

| Symptom | Check |
|---|---|
| `VoteSkipNight.cs did not compile` | Check the server log for the Oxide compiler line number. The plugin targets the current 7 Days to Die server assembly and current Oxide build |
| `Votes can only be skipped at night` | The plugin uses the game's own day/night calculation, which depends on `DaylightLength` and the world's dusk/dawn hours |
| `The blood moon cannot be skipped` | Intentional. The message appears when a vote is attempted during, or a force-skip is requested during, the blood moon window |
| No config file appears | Oxide only writes the config after the plugin loads successfully. Check the server log for `[VoteSkipNight]` messages |
| The Server Assist panel is missing | Confirm Server Assist is loaded, the player has `voteskip.use` or is an admin, and the client has the Server Assist mod. Reload VoteSkipNight or Server Assist and check the log. On an old Server Assist build, update it or create a shared command button with target `say /voteskip` |
| Icons show a hammer | The client is missing the art PNGs. Install `AAZ_ServerAssistArt`, or copy `UIAtlases/UIAtlas` into the Server Assist mod folder, on the server and every client. Sprite names never include `.png` |
| `I am sleeping and cannot vote` | Intentional while `IgnoreSleepingPlayers` is true. Grant `voteskip.bypasssleepers`, or set `IgnoreSleepingPlayers` to `false` |
| The panel opens with `Unknown action` | The server is running an older VoteSkipNight build. Replace the `.cs` file and reload the plugin |
| Command alias conflict | Another plugin already uses `/voteskip`, `/skipnight` or `/votenight`. Edit the `AddCovalenceCommand` line near the top of `VoteSkipNight.cs`, then reload the plugin |
| A vote disappears after a reload or restart | The active vote lives in memory only. Start a new vote after the reload |

---

## Related pages

- [Server Assist product guide](../../core/server-assist.md)
- [Panels and hooks](../../integration/api/panels.md)
- [Companion problems](../../reference/troubleshooting/companions.md)
- [Icons](../../core/icons.md)
- [Install](../../start/install.md)
