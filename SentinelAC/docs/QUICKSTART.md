# SentinelAC quick start

1. Copy `oxide/plugins/SentinelAC.cs` into the server's `oxide/plugins` folder.
2. Restart the server. Oxide compiles the plugin and creates
   `oxide/config/SentinelAC.json`.
3. Grant the admin permission to your staff group:

   ```text
   oxide.grant group admin sentinelac.admin
   oxide.grant group admin sentinelac.alerts
   ```

4. Mark event accounts, testers and service accounts as exempt:

   ```text
   oxide.grant user <steamid> sentinelac.exempt
   ```

5. Leave the safe defaults for the first days:

   ```json
   "DryRun": true,
   "AutoKick": false,
   "AutoBan": false
   ```

6. Check the board:

   ```text
   /ac top
   /ac status <player>
   /ac flags <player>
   /ac modules <player>
   ```

7. Open `/ac gui` if ServerAssist is installed. The menu opens as a provider
   with Risk, Evidence and Modules tabs. Use the search field on Risk to filter
   by player name or ID, and click a player card to open their evidence. The
   **Plugins** button returns to the Server Assist plugin board; **Close** closes
   the whole window.

8. When the noise level is acceptable, lower/raise thresholds in
   `oxide/config/SentinelAC.json` and run `/ac reload`.

9. Enable `AutoKick` first, with a high threshold and a long sustain time. Enable
   `AutoBan` only after you have watched the webhook and the evidence for a full
   wipe.

## Common tuning

- Too many speed alerts: raise `Movement.HardSpeed` or
  `Movement.ZThreshold`.
- Too many headshot alerts on a PvE server: raise
  `Combat.HeadshotRatioThreshold` or set `Combat.MinHeadshots` higher.
- Modded quality tiers: raise `Inventory.MaxLegalQuality`.
- Large modded bases or fast builders: raise `Grief.MaxChangesPerMinute` or
  lower `Grief.SampleSeconds` (less frequent).
- Busy server: raise `Grief.SampleSeconds`, reduce `Grief.Radius`.
- Staff using god mode: `ExemptAdmins` is already true; also grant
  `sentinelac.exempt` to shared staff accounts.

## What to do with a flag

1. Read `/ac flags <player>` and `/ac modules <player>`.
2. Look at the recent evidence, not just the score.
3. Watch the player with `/ac watch <player>`.
4. If the evidence is wrong, `/ac clear <player>` and tune the matching module.
5. If the evidence is real, use `/ac kick` or `/ac ban`, or your normal admin
   tools. Attach `/ac export <player>` to your report.
