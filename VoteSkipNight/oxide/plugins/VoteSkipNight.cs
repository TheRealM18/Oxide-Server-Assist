using System;
using System.Collections.Generic;
using System.Text;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("VoteSkipNight", "TheRealM18", "1.2.0")]
    [Description("Players vote to skip the night. Blood moon nights can never be skipped. Includes a ServerAssist panel.")]
    public class VoteSkipNight : CovalencePlugin
    {
        private const string PermUse = "voteskip.use";
        private const string PermAdmin = "voteskip.admin";
        private const string PermBypassSleepers = "voteskip.bypasssleepers";
        private const string ServerAssistHookId = "voteskip";

        // Server Assist Art sprite keys (AAZ_ServerAssistArt / UIAtlases/UIAtlas).
        private const string IconPanel = "sprite:sa_vote_skip_night";
        private const string IconStartVote = "sprite:sa_vote_skip_night";
        private const string IconVoteYes = "sprite:sa_daily_rewards";
        private const string IconVoteNo = "sprite:sa_act_ban";
        private const string IconCancelVote = "sprite:sa_act_kick";
        private const string IconForceSkip = "sprite:sa_act_time";
        private const string IconEligibleVoters = "sprite:sa_nav_players";
        private const string IconCurrentVote = "sprite:sa_nav_moderation";
        private const string IconYourVote = "sprite:sa_permission_gui";
        private const string IconRefresh = "sprite:sa_plugin_hot_reload";
        private const string IconBloodMoon = "sprite:sa_act_weather";
        private const string IconCooldown = "sprite:sa_act_warn";
        private const string IconSleeping = "sprite:sa_act_freeze";
        private const string IconDay = "sprite:sa_nav_world";
        private const string IconReady = "sprite:sa_act_godmode";

        private Configuration config;
        private VoteSession vote;
        private Timer tickTimer;
        private DateTime cooldownUntil = DateTime.MinValue;
        private int lastRequired = -1;

        #region Lifecycle

        private void Init()
        {
            permission.RegisterPermission(PermUse, this);
            permission.RegisterPermission(PermAdmin, this);
            permission.RegisterPermission(PermBypassSleepers, this);

            LoadConfig();

            AddCovalenceCommand(new[] { "voteskip", "skipnight", "votenight" }, nameof(CommandVoteSkip));

            timer.Once(1f, EnsureDefaultPermissions);
            timer.Once(5f, EnsureDefaultPermissions);
            timer.Once(3f, RegisterServerAssistHook);
        }

        private void OnServerInitialized()
        {
            EnsureDefaultPermissions();
            RegisterServerAssistHook();
        }

        private void OnServerAssistReady()
        {
            RegisterServerAssistHook();
        }

        private void Unload()
        {
            DestroyTick();
            vote = null;
            UnregisterServerAssistHook();
        }

        private void OnUserConnected(IPlayer player)
        {
            if (vote == null)
            {
                return;
            }

            EvaluateVote();

            if (vote != null && player != null)
            {
                if (IsEligibleVoter(player))
                {
                    Reply(player, "A vote to skip the night is running: " + CountYes() + "/" +
                                 GetRequiredVotes(CountEligibleVoters()) +
                                 " yes required. Type /voteskip to vote yes or /voteskip no to vote no.");
                }
                else
                {
                    Reply(player, "You are marked as sleeping and are not counted in the current night vote.");
                }
            }
        }

        private void OnUserDisconnected(IPlayer player, string reason)
        {
            if (player == null || vote == null)
            {
                return;
            }

            vote.Yes.Remove(player.Id);
            vote.No.Remove(player.Id);

            // The Covalence connected list is updated after this hook, so let the
            // next tick recalculate the requirement instead of doing it here.
        }

        protected override void LoadDefaultConfig()
        {
            config = new Configuration();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<Configuration>();
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[VoteSkipNight] config could not be read: " + exception.Message);
                config = null;
            }

            if (config == null)
            {
                config = new Configuration();
            }

            config.RequiredYesPercent = Clamp(config.RequiredYesPercent, 1, 100);
            config.VoteDurationSeconds = Clamp(config.VoteDurationSeconds, 10, 3600);
            config.CooldownSeconds = Clamp(config.CooldownSeconds, 0, 86400);
            config.CancelCooldownSeconds = Clamp(config.CancelCooldownSeconds, 0, 3600);
            config.MinimumOnlinePlayers = Clamp(config.MinimumOnlinePlayers, 1, 200);
            config.DawnBufferHours = Clamp(config.DawnBufferHours, 0, 6);

            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        #endregion

        #region Commands

        private void CommandVoteSkip(IPlayer player, string command, string[] args)
        {
            if (player == null || player.IsServer)
            {
                Interface.Oxide.LogInfo("[VoteSkipNight] This command must be used in-game.");
                return;
            }

            string sub = args.Length == 0 ? String.Empty : args[0].ToLowerInvariant();

            switch (sub)
            {
                case "":
                case "yes":
                case "y":
                case "start":
                    if (vote == null)
                    {
                        StartVote(player);
                    }
                    else
                    {
                        CastYes(player);
                    }
                    break;

                case "no":
                case "n":
                case "veto":
                    CastNo(player);
                    break;

                case "status":
                case "info":
                    ShowStatus(player);
                    break;

                case "cancel":
                    CancelVote(player);
                    break;

                case "gui":
                case "panel":
                    OpenServerAssistGui(player);
                    break;

                case "force":
                case "skip":
                    ForceSkip(player);
                    break;

                case "reload":
                    ReloadConfig(player);
                    break;

                default:
                    ShowHelp(player);
                    break;
            }
        }

        private void StartVote(IPlayer player)
        {
            string status;
            if (!TryStartVote(player, out status))
            {
                Reply(player, status);
            }
        }

        private bool TryStartVote(IPlayer player, out string status)
        {
            World world = GetWorld();
            if (world == null)
            {
                status = "The world is not loaded yet.";
                return false;
            }

            if (!CanPlayerVote(player, out status))
            {
                return false;
            }

            if (vote != null)
            {
                status = "A vote is already running. Type /voteskip to vote yes or /voteskip no to vote no.";
                return false;
            }

            if (DateTime.UtcNow < cooldownUntil)
            {
                status = "The last vote just ended. Try again in " + FormatSeconds(RemainingCooldownSeconds()) + ".";
                return false;
            }

            if (!IsNight(world))
            {
                status = "Nights can only be skipped at night.";
                return false;
            }

            if (WouldSkipBloodMoon(world))
            {
                status = "The blood moon cannot be skipped.";
                return false;
            }

            int eligible = CountEligibleVoters();
            if (eligible < config.MinimumOnlinePlayers)
            {
                status = "At least " + config.MinimumOnlinePlayers + " eligible players must be online to start a vote.";
                return false;
            }

            vote = new VoteSession();
            vote.StarterId = player.Id;
            vote.StarterName = player.Name;
            vote.EndsUtc = DateTime.UtcNow.AddSeconds(config.VoteDurationSeconds);

            if (config.AutoVoteYesOnStart)
            {
                vote.Yes.Add(player.Id);
            }

            lastRequired = GetRequiredVotes(eligible);

            Broadcast(player.Name + " started a vote to skip the night. Yes: " + CountYes() + "/" + lastRequired +
                      " required (" + config.RequiredYesPercent + "% of " + eligible + " eligible). " +
                      "Type /voteskip to vote yes or /voteskip no to vote no. " +
                      FormatSeconds(config.VoteDurationSeconds) + " to vote.");

            Interface.Oxide.LogInfo("[VoteSkipNight] " + player.Name + " started a vote (" + eligible + " eligible, " +
                                    lastRequired + " yes required).");

            EvaluateVote();

            if (vote != null)
            {
                tickTimer = timer.Repeat(1f, -1, TickVote);
            }

            status = vote != null ? "Vote started." : "The vote passed and the night was skipped.";
            return true;
        }

        private void CastYes(IPlayer player)
        {
            string status;
            if (!TryCastYes(player, out status))
            {
                Reply(player, status);
            }
        }

        private bool TryCastYes(IPlayer player, out string status)
        {
            if (!CanPlayerVote(player, out status))
            {
                return false;
            }

            if (vote == null)
            {
                status = "No vote is running. Start one with /voteskip.";
                return false;
            }

            if (vote.Yes.Contains(player.Id))
            {
                status = "You already voted yes.";
                return false;
            }

            bool changed = vote.No.Remove(player.Id);
            vote.Yes.Add(player.Id);

            Broadcast(player.Name + (changed ? " changed their vote to yes." : " voted yes.") +
                      " Yes: " + CountYes() + "/" + GetRequiredVotes(CountEligibleVoters()) + " required.");

            EvaluateVote();

            status = changed ? "Your vote was changed to yes." : "Your yes vote was recorded.";
            return true;
        }

        private void CastNo(IPlayer player)
        {
            string status;
            if (!TryCastNo(player, out status))
            {
                Reply(player, status);
            }
        }

        private bool TryCastNo(IPlayer player, out string status)
        {
            if (!CanPlayerVote(player, out status))
            {
                return false;
            }

            if (vote == null)
            {
                status = "No vote is running. Start one with /voteskip.";
                return false;
            }

            if (vote.No.Contains(player.Id))
            {
                status = "You already voted no.";
                return false;
            }

            bool changed = vote.Yes.Remove(player.Id);
            vote.No.Add(player.Id);

            Broadcast(player.Name + (changed ? " changed their vote to no." : " voted no.") +
                      " Yes: " + CountYes() + "/" + GetRequiredVotes(CountEligibleVoters()) + " required.");

            status = changed ? "Your vote was changed to no." : "Your no vote was recorded.";
            return true;
        }

        private void ShowStatus(IPlayer player)
        {
            if (vote == null)
            {
                Reply(player, "No vote is running. Start one with /voteskip.");
                return;
            }

            int eligible = CountEligibleVoters();
            int required = GetRequiredVotes(eligible);
            int yes = CountYes();

            Reply(player, "Vote: " + yes + "/" + required + " yes required (" + config.RequiredYesPercent + "% of " +
                         eligible + " eligible), " + CountNo() + " no. " +
                         FormatSeconds(RemainingVoteSeconds()) + " left. Starter: " + vote.StarterName + ".");

            if (!IsEligibleVoter(player))
            {
                Reply(player, "You are marked as sleeping and are not counted in this vote.");
            }
        }

        private void CancelVote(IPlayer player)
        {
            string status;
            if (!TryCancelVote(player, out status))
            {
                Reply(player, status);
            }
        }

        private bool TryCancelVote(IPlayer player, out string status)
        {
            if (vote == null)
            {
                status = "No vote is running.";
                return false;
            }

            bool isStarter = string.Equals(player.Id, vote.StarterId, StringComparison.OrdinalIgnoreCase);
            if (!isStarter && !IsAdmin(player))
            {
                status = "Only the player who started the vote or an admin can cancel it.";
                return false;
            }

            if (config.CancelCooldownSeconds > 0)
            {
                cooldownUntil = DateTime.UtcNow.AddSeconds(config.CancelCooldownSeconds);
            }

            CompleteVote(false, "Vote cancelled by " + player.Name + ".", false);
            status = "Vote cancelled.";
            return true;
        }

        private void ForceSkip(IPlayer player)
        {
            string status;
            if (!TryForceSkip(player, out status))
            {
                Reply(player, status);
            }
        }

        private bool TryForceSkip(IPlayer player, out string status)
        {
            if (!IsAdmin(player))
            {
                status = "You need the '" + PermAdmin + "' permission.";
                return false;
            }

            string message;
            if (!TrySkipToDawn(out message))
            {
                status = message;
                return false;
            }

            if (vote != null)
            {
                CompleteVote(false, null, false);
            }

            if (config.CooldownSeconds > 0)
            {
                cooldownUntil = DateTime.UtcNow.AddSeconds(config.CooldownSeconds);
            }

            Broadcast("An admin skipped the night. " + message);
            Interface.Oxide.LogInfo("[VoteSkipNight] " + player.Name + " forced a night skip.");
            status = "Night skipped. " + message;
            return true;
        }

        private void ReloadConfig(IPlayer player)
        {
            if (!IsAdmin(player))
            {
                Reply(player, "You need the '" + PermAdmin + "' permission.");
                return;
            }

            LoadConfig();
            EnsureDefaultPermissions();
            Reply(player, "Configuration reloaded.");
        }

        private void ShowHelp(IPlayer player)
        {
            Reply(player, "/voteskip - start a night vote, or vote yes while one is running");
            Reply(player, "/voteskip yes|no - cast or change your vote");
            Reply(player, "/voteskip status - show the current vote");
            Reply(player, "/voteskip cancel - cancel your vote (admins can cancel any vote)");
            Reply(player, "/voteskip gui - open the vote panel inside ServerAssist");
            Reply(player, "/voteskip force - skip the night immediately (admin only)");
            Reply(player, "/voteskip reload - reload the config (admin only)");
        }

        #endregion

        #region ServerAssist integration

        private void RegisterServerAssistHook()
        {
            try
            {
                Plugin serverAssist = plugins.Find("ServerAssist");
                if (serverAssist == null)
                {
                    return;
                }

                string pluginName = string.IsNullOrEmpty(Name) ? GetType().Name : Name;
                serverAssist.Call(
                    "RegisterGuiHook",
                    ServerAssistHookId,
                    "Vote: Skip Night",
                    "Start or join the night-skip vote. Blood moons cannot be skipped.",
                    PermUse,
                    pluginName,
                    nameof(HandleServerAssistRequest));

                serverAssist.Call("SetPanelIcon", ServerAssistHookId, IconPanel);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[VoteSkipNight] could not register the ServerAssist hook: " + exception.Message);
            }
        }

        private void UnregisterServerAssistHook()
        {
            try
            {
                Plugin serverAssist = plugins.Find("ServerAssist");
                if (serverAssist != null)
                {
                    serverAssist.Call("UnregisterGuiHook", ServerAssistHookId);
                }
            }
            catch
            {
                // ServerAssist may already be unloading.
            }
        }

        private void OpenServerAssistGui(IPlayer player)
        {
            if (!IsAdmin(player) && !player.HasPermission(PermUse))
            {
                Reply(player, "You need the '" + PermUse + "' permission to open the ServerAssist vote panel.");
                return;
            }

            Plugin serverAssist = plugins.Find("ServerAssist");
            if (serverAssist == null)
            {
                Reply(player, "ServerAssist is not loaded on this server.");
                return;
            }

            try
            {
                object result = serverAssist.Call("OpenHookForPlayer", player.Id, ServerAssistHookId);
                if (!(result is bool) || !(bool)result)
                {
                    Reply(player, "The ServerAssist vote panel could not be opened. Make sure the ServerAssist client mod is installed.");
                }
            }
            catch (Exception exception)
            {
                Reply(player, "Could not open the ServerAssist panel: " + exception.Message);
            }
        }

        public string HandleServerAssistRequest(string playerId, string payload)
        {
            try
            {
                IPlayer player = players.FindPlayerById(playerId);
                if (player == null || !player.IsConnected)
                {
                    return BuildServerAssistErrorView("Player not found.");
                }

                if (!IsAdmin(player) && !player.HasPermission(PermUse))
                {
                    return BuildServerAssistErrorView("You do not have permission to use Vote Skip Night.");
                }

                string status = String.Empty;
                if (!string.IsNullOrEmpty(payload) && payload.StartsWith("a|", StringComparison.Ordinal))
                {
                    string action = payload.Substring(2);
                    string message;

                    if (string.Equals(action, "open", StringComparison.OrdinalIgnoreCase))
                    {
                        status = String.Empty;
                    }
                    else if (string.Equals(action, "refresh", StringComparison.OrdinalIgnoreCase))
                    {
                        status = "Refreshed.";
                    }
                    else if (string.Equals(action, "start", StringComparison.OrdinalIgnoreCase))
                    {
                        TryStartVote(player, out message);
                        status = message;
                    }
                    else if (string.Equals(action, "vote|yes", StringComparison.OrdinalIgnoreCase))
                    {
                        TryCastYes(player, out message);
                        status = message;
                    }
                    else if (string.Equals(action, "vote|no", StringComparison.OrdinalIgnoreCase))
                    {
                        TryCastNo(player, out message);
                        status = message;
                    }
                    else if (string.Equals(action, "cancel", StringComparison.OrdinalIgnoreCase))
                    {
                        TryCancelVote(player, out message);
                        status = message;
                    }
                    else if (string.Equals(action, "force", StringComparison.OrdinalIgnoreCase))
                    {
                        TryForceSkip(player, out message);
                        status = message;
                    }
                    else
                    {
                        status = "Unknown action.";
                    }
                }

                return BuildServerAssistView(player, status);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogException("[VoteSkipNight] ServerAssist request failed", exception);
                return BuildServerAssistErrorView("Internal error: " + exception.Message);
            }
        }

        private string BuildServerAssistView(IPlayer player, string status)
        {
            World world = GetWorld();
            bool night = IsNight(world);
            bool bloodMoonNow = world != null && IsBloodMoon(world);
            bool skipBlocked = world != null && WouldSkipBloodMoon(world);
            int eligible = CountEligibleVoters();
            int required = GetRequiredVotes(eligible);
            bool eligiblePlayer = IsEligibleVoter(player);
            bool alreadyYes = vote != null && vote.Yes.Contains(player.Id);
            bool alreadyNo = vote != null && vote.No.Contains(player.Id);
            bool cooldown = DateTime.UtcNow < cooldownUntil;

            bool canVote = vote != null && eligiblePlayer && night && !skipBlocked;
            bool canStart = vote == null && eligiblePlayer && night && !skipBlocked &&
                            !cooldown && eligible >= config.MinimumOnlinePlayers;
            bool canCancel = vote != null &&
                             (string.Equals(player.Id, vote.StarterId, StringComparison.OrdinalIgnoreCase) ||
                              IsAdmin(player));
            bool canForce = IsAdmin(player) && night && !skipBlocked;

            List<GuiRow> rows = new List<GuiRow>();

            string startHint;
            if (vote != null)
            {
                startHint = "A vote is already running.";
            }
            else if (!eligiblePlayer)
            {
                startHint = "Sleeping players cannot start a vote.";
            }
            else if (!night)
            {
                startHint = "Night votes only run at night.";
            }
            else if (skipBlocked)
            {
                startHint = "The blood moon cannot be skipped.";
            }
            else if (cooldown)
            {
                startHint = "Cooldown: " + FormatSeconds(RemainingCooldownSeconds()) + ".";
            }
            else if (eligible < config.MinimumOnlinePlayers)
            {
                startHint = "Need at least " + config.MinimumOnlinePlayers + " eligible " +
                            (config.MinimumOnlinePlayers == 1 ? "player." : "players.");
            }
            else
            {
                startHint = "Begin a " + config.RequiredYesPercent + "% night vote.";
            }
            rows.Add(new GuiRow("start", "Start Vote", startHint, canStart,
                IconStartVote, "VOTE", String.Empty));

            string yesHint;
            if (vote == null)
            {
                yesHint = "No vote is running.";
            }
            else if (!eligiblePlayer)
            {
                yesHint = "Sleeping players are not counted.";
            }
            else if (alreadyYes)
            {
                yesHint = "You already voted yes.";
            }
            else
            {
                yesHint = "Count your yes vote.";
            }
            rows.Add(new GuiRow("vote|yes", "Vote Yes", yesHint, canVote && !alreadyYes,
                IconVoteYes, "YES", String.Empty));

            string noHint;
            if (vote == null)
            {
                noHint = "No vote is running.";
            }
            else if (!eligiblePlayer)
            {
                noHint = "Sleeping players are not counted.";
            }
            else if (alreadyNo)
            {
                noHint = "You already voted no.";
            }
            else
            {
                noHint = "Vote against skipping.";
            }
            rows.Add(new GuiRow("vote|no", "Vote No", noHint, canVote && !alreadyNo,
                IconVoteNo, "NO", String.Empty));

            string cancelHint;
            if (vote == null)
            {
                cancelHint = "No vote is running.";
            }
            else if (canCancel)
            {
                cancelHint = "Cancel the current vote.";
            }
            else
            {
                cancelHint = "Only the starter or an admin can cancel.";
            }
            rows.Add(new GuiRow("cancel", "Cancel Vote", cancelHint, canCancel,
                IconCancelVote, "STOP", String.Empty));

            if (IsAdmin(player))
            {
                string forceHint;
                if (!night)
                {
                    forceHint = "Only available at night.";
                }
                else if (skipBlocked)
                {
                    forceHint = "The blood moon cannot be skipped.";
                }
                else
                {
                    forceHint = "Skip the night immediately.";
                }

                rows.Add(new GuiRow("force", "Force Skip", forceHint, canForce,
                    IconForceSkip, "ADMIN", String.Empty));
            }

            string voteSub;
            string voteBadge;
            if (vote == null)
            {
                voteBadge = "IDLE";
                voteSub = "No vote is running.";
            }
            else
            {
                voteBadge = "LIVE";
                voteSub = "Yes " + CountYes() + "/" + required + " needed, " + CountNo() + " no, " +
                          FormatSeconds(RemainingVoteSeconds()) + " left, by " + vote.StarterName + ".";
            }
            rows.Add(new GuiRow(String.Empty, "Current Vote", voteSub, false,
                IconCurrentVote, voteBadge, String.Empty));

            string yourVote;
            string yourBadge;
            if (!eligiblePlayer)
            {
                yourVote = "Not counted (sleeping).";
                yourBadge = "NONE";
            }
            else if (alreadyYes)
            {
                yourVote = "Yes.";
                yourBadge = "YES";
            }
            else if (alreadyNo)
            {
                yourVote = "No.";
                yourBadge = "NO";
            }
            else
            {
                yourVote = "Not voted.";
                yourBadge = "NONE";
            }
            rows.Add(new GuiRow(String.Empty, "Your Vote", yourVote, false,
                IconYourVote, yourBadge, String.Empty));

            rows.Add(new GuiRow(String.Empty, "Eligible Voters",
                eligible + " eligible, " + required + " yes required", false,
                IconEligibleVoters, config.RequiredYesPercent + "%", String.Empty));

            string timeText = world == null
                ? "World not loaded."
                : "Day " + GameUtils.WorldTimeToDays(world.worldTime) + " " +
                  GameUtils.WorldTimeToHourMinutesString(world.worldTime) + " - " +
                  (night ? "night" : "day") + ".";

            string statusIcon;
            string statusBadge;
            string statusSub;
            if (world == null)
            {
                statusIcon = IconDay;
                statusBadge = "OFFLINE";
                statusSub = timeText;
            }
            else if (bloodMoonNow)
            {
                statusIcon = IconBloodMoon;
                statusBadge = "BLOCKED";
                statusSub = timeText + " Blood moon cannot be skipped.";
            }
            else if (cooldown)
            {
                statusIcon = IconCooldown;
                statusBadge = "WAIT";
                statusSub = timeText + " Cooldown: " + FormatSeconds(RemainingCooldownSeconds()) + ".";
            }
            else if (!eligiblePlayer && IsPlayerSleeping(player))
            {
                statusIcon = IconSleeping;
                statusBadge = "SLEEP";
                statusSub = timeText + " Sleeping - not counted.";
            }
            else if (IsPlayerSleeping(player))
            {
                statusIcon = IconSleeping;
                if (!config.IgnoreSleepingPlayers)
                {
                    statusBadge = "SLEEP";
                    statusSub = timeText + " Sleeping - counted (IgnoreSleepingPlayers is false).";
                }
                else
                {
                    statusBadge = "BYPASS";
                    statusSub = timeText + " Sleeping - counted with voteskip.bypasssleepers.";
                }
            }
            else if (!night)
            {
                statusIcon = IconDay;
                statusBadge = "DAY";
                statusSub = timeText + " Night votes only run at night.";
            }
            else
            {
                statusIcon = IconReady;
                statusBadge = "READY";
                statusSub = timeText + (vote != null ? " Vote in progress." : " Ready for a night vote.");
            }
            rows.Add(new GuiRow(String.Empty, "Status", statusSub, false,
                statusIcon, statusBadge, String.Empty));

            rows.Add(new GuiRow("refresh", "Refresh", "Reload this panel.", true,
                IconRefresh, "SYNC", String.Empty));

            StringBuilder builder = new StringBuilder(4096);
            builder.Append("v2\n");
            builder.Append("title=").Append(ServerAssistEncode("Vote Skip Night")).Append('\n');
            builder.Append("subtitle=")
                .Append(ServerAssistEncode(config.RequiredYesPercent +
                                           "% of eligible players must vote yes. Blood moons cannot be skipped."))
                .Append('\n');
            builder.Append("status=").Append(ServerAssistEncode(status)).Append('\n');
            builder.Append("input=||0\n");

            for (int i = 0; i < rows.Count && i < 10; i++)
            {
                GuiRow row = rows[i];
                builder.Append("row=").Append(i).Append('|')
                    .Append(ServerAssistEncode(row.Action)).Append('|')
                    .Append(ServerAssistEncode(row.Label)).Append('|')
                    .Append(ServerAssistEncode(row.Sub)).Append('|')
                    .Append(row.Enabled ? "1" : "0").Append('|')
                    .Append("0|")
                    .Append(ServerAssistEncode(row.Icon)).Append('|')
                    .Append(ServerAssistEncode(row.Badge)).Append('|')
                    .Append(ServerAssistEncode(row.Tint)).Append('\n');
            }

            return builder.ToString();
        }

        private string BuildServerAssistErrorView(string message)
        {
            StringBuilder builder = new StringBuilder(512);
            builder.Append("v2\n");
            builder.Append("title=").Append(ServerAssistEncode("Vote Skip Night")).Append('\n');
            builder.Append("subtitle=").Append(ServerAssistEncode("Vote Skip Night")).Append('\n');
            builder.Append("status=").Append(ServerAssistEncode(message)).Append('\n');
            builder.Append("input=||0\n");
            builder.Append("row=0|refresh|Refresh|Reload this panel.|1|0|")
                .Append(ServerAssistEncode(IconRefresh)).Append("|SYNC|\n");
            return builder.ToString();
        }

        private static string ServerAssistEncode(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return String.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '%': builder.Append("%25"); break;
                    case '|': builder.Append("%7C"); break;
                    case '\n': builder.Append("%0A"); break;
                    case '\r': break;
                    default: builder.Append(c); break;
                }
            }
            return builder.ToString();
        }

        #endregion

        #region Vote logic

        private void TickVote()
        {
            if (vote == null)
            {
                DestroyTick();
                return;
            }

            World world = GetWorld();
            if (world == null)
            {
                CompleteVote(false, "Vote cancelled: the world is not loaded.", false);
                return;
            }

            if (!IsNight(world))
            {
                CompleteVote(false, "Vote cancelled: the night is over.", false);
                return;
            }

            if (WouldSkipBloodMoon(world))
            {
                CompleteVote(false, "Vote cancelled: the blood moon cannot be skipped.", false);
                return;
            }

            if (RemainingVoteSeconds() <= 0)
            {
                int eligible = CountEligibleVoters();
                int required = GetRequiredVotes(eligible);
                int yes = CountYes();

                CompleteVote(false, "Vote failed: " + yes + "/" + required + " yes (" +
                                    Percent(yes, eligible) + "%). The night continues.", true);
                return;
            }

            EvaluateVote();
        }

        private void EvaluateVote()
        {
            if (vote == null)
            {
                return;
            }

            World world = GetWorld();
            if (world == null)
            {
                return;
            }

            if (!IsNight(world))
            {
                CompleteVote(false, "Vote cancelled: the night is over.", false);
                return;
            }

            if (WouldSkipBloodMoon(world))
            {
                CompleteVote(false, "Vote cancelled: the blood moon cannot be skipped.", false);
                return;
            }

            int eligible = CountEligibleVoters();
            if (eligible < config.MinimumOnlinePlayers)
            {
                CompleteVote(false, "Vote cancelled: not enough eligible players are online.", false);
                return;
            }

            int required = GetRequiredVotes(eligible);
            if (required != lastRequired)
            {
                lastRequired = required;
                Broadcast("Vote requirement changed to " + required + " yes (" + eligible + " eligible).");
            }

            int yes = CountYes();
            if (yes >= required)
            {
                PassVote(yes, required, eligible);
            }
        }

        private void PassVote(int yes, int required, int eligible)
        {
            string message;
            if (!TrySkipToDawn(out message))
            {
                CompleteVote(false, "Vote passed, but the night could not be skipped: " + message, true);
                return;
            }

            CompleteVote(true, "Vote passed: " + yes + "/" + required + " yes (" + Percent(yes, eligible) +
                               "%). " + message, true);

            Interface.Oxide.LogInfo("[VoteSkipNight] Vote passed with " + yes + "/" + required + " yes.");
        }

        private void CompleteVote(bool passed, string message, bool applyCooldown)
        {
            DestroyTick();
            vote = null;
            lastRequired = -1;

            if (applyCooldown && config.CooldownSeconds > 0)
            {
                cooldownUntil = DateTime.UtcNow.AddSeconds(config.CooldownSeconds);
            }

            if (!string.IsNullOrEmpty(message))
            {
                Broadcast(message);
            }

            if (!passed && message != null && message.StartsWith("Vote failed", StringComparison.OrdinalIgnoreCase))
            {
                Interface.Oxide.LogInfo("[VoteSkipNight] " + message);
            }
        }

        private bool TrySkipToDawn(out string message)
        {
            World world = GetWorld();
            if (world == null)
            {
                message = "The world is not loaded.";
                return false;
            }

            if (!IsNight(world))
            {
                message = "The night is already over.";
                return false;
            }

            if (IsBloodMoon(world))
            {
                message = "The blood moon cannot be skipped.";
                return false;
            }

            ulong target = ComputeDawnTarget(world);
            if (CrossesBloodMoonStart(world, world.worldTime, target))
            {
                message = "The blood moon cannot be skipped.";
                return false;
            }

            try
            {
                world.SetTimeJump(target);
            }
            catch (Exception exception)
            {
                message = "Failed to change the world time: " + exception.Message;
                return false;
            }

            message = "It is now day " + GameUtils.WorldTimeToDays(target) + " " +
                      GameUtils.WorldTimeToHourMinutesString(target) + ".";
            return true;
        }

        private ulong ComputeDawnTarget(World world)
        {
            int day = GameUtils.WorldTimeToDays(world.worldTime);
            int dawnHour = world.DawnHour;
            if (dawnHour < 0 || dawnHour > 23)
            {
                dawnHour = 4;
            }

            int targetDay = day;
            int targetHour = dawnHour + Math.Max(0, config.DawnBufferHours);
            while (targetHour > 23)
            {
                targetHour -= 24;
                targetDay++;
            }

            ulong target = GameUtils.DayTimeToWorldTime(targetDay, targetHour, 0);
            if (target <= world.worldTime)
            {
                target += 24000UL;
            }
            return target;
        }

        private void DestroyTick()
        {
            if (tickTimer != null)
            {
                tickTimer.Destroy();
                tickTimer = null;
            }
        }

        #endregion

        #region Helpers

        private static World GetWorld()
        {
            GameManager manager = GameManager.Instance;
            return manager == null ? null : manager.World;
        }

        private static bool IsNight(World world)
        {
            return world != null && world.IsDark();
        }

        private static bool IsBloodMoon(World world)
        {
            if (world == null)
            {
                return false;
            }

            if (world.IsWorldEvent(World.WorldEvent.BloodMoon))
            {
                return true;
            }

            if (world.aiDirector != null && world.aiDirector.BloodMoonComponent != null &&
                world.aiDirector.BloodMoonComponent.BloodMoonActive)
            {
                return true;
            }

            int bloodMoonDay = GameStats.GetInt(EnumGameStats.BloodMoonDay);
            if (bloodMoonDay <= 0)
            {
                return false;
            }

            int day = GameUtils.WorldTimeToDays(world.worldTime);
            int hour = GameUtils.WorldTimeToHours(world.worldTime);
            int duskHour = world.DuskHour;
            int dawnHour = world.DawnHour;

            if (day == bloodMoonDay && hour >= duskHour)
            {
                return true;
            }

            return day > 1 && day == bloodMoonDay + 1 && hour < dawnHour;
        }

        private bool WouldSkipBloodMoon(World world)
        {
            if (world == null)
            {
                return false;
            }

            if (IsBloodMoon(world))
            {
                return true;
            }

            ulong target = ComputeDawnTarget(world);
            return CrossesBloodMoonStart(world, world.worldTime, target);
        }

        private static bool CrossesBloodMoonStart(World world, ulong from, ulong to)
        {
            int bloodMoonDay = GameStats.GetInt(EnumGameStats.BloodMoonDay);
            if (bloodMoonDay <= 0)
            {
                return false;
            }

            int duskHour = world.DuskHour;
            if (duskHour < 0 || duskHour > 23)
            {
                duskHour = 22;
            }

            ulong bloodMoonStart = GameUtils.DayTimeToWorldTime(bloodMoonDay, duskHour, 0);
            return from < bloodMoonStart && to >= bloodMoonStart;
        }

        private bool IsEligibleVoter(IPlayer player)
        {
            if (player == null || !player.IsConnected)
            {
                return false;
            }

            if (!config.IgnoreSleepingPlayers || !IsPlayerSleeping(player))
            {
                return true;
            }

            return player.HasPermission(PermBypassSleepers);
        }

        private static bool IsPlayerSleeping(IPlayer player)
        {
            if (player == null)
            {
                return false;
            }

            try
            {
                return player.IsSleeping;
            }
            catch
            {
                // A player can be connected before their entity is added to the
                // world. In that short window the game object lookup can throw.
                return false;
            }
        }

        private bool CanPlayerVote(IPlayer player, out string status)
        {
            if (player == null || !player.IsConnected)
            {
                status = "You are not connected.";
                return false;
            }

            if (!IsEligibleVoter(player))
            {
                status = "Sleeping players are not counted in night votes. You need '" +
                         PermBypassSleepers + "' to vote while sleeping.";
                return false;
            }

            status = null;
            return true;
        }

        private int CountEligibleVoters()
        {
            int count = 0;
            foreach (IPlayer player in players.Connected)
            {
                if (IsEligibleVoter(player))
                {
                    count++;
                }
            }
            return count;
        }

        private int CountYes()
        {
            return CountEligibleVotes(vote.Yes);
        }

        private int CountNo()
        {
            return CountEligibleVotes(vote.No);
        }

        private int CountEligibleVotes(HashSet<string> votes)
        {
            int count = 0;
            foreach (string id in votes)
            {
                IPlayer player = players.FindPlayerById(id);
                if (IsEligibleVoter(player))
                {
                    count++;
                }
            }
            return count;
        }

        private int GetRequiredVotes(int eligible)
        {
            if (eligible <= 0)
            {
                return 0;
            }

            int required = (int)Math.Ceiling(eligible * (config.RequiredYesPercent / 100.0));
            if (required < 1)
            {
                required = 1;
            }
            if (required > eligible)
            {
                required = eligible;
            }
            return required;
        }

        private int RemainingVoteSeconds()
        {
            if (vote == null)
            {
                return 0;
            }

            double seconds = (vote.EndsUtc - DateTime.UtcNow).TotalSeconds;
            return seconds <= 0 ? 0 : (int)Math.Ceiling(seconds);
        }

        private int RemainingCooldownSeconds()
        {
            double seconds = (cooldownUntil - DateTime.UtcNow).TotalSeconds;
            return seconds <= 0 ? 0 : (int)Math.Ceiling(seconds);
        }

        private static int Percent(int value, int total)
        {
            if (total <= 0)
            {
                return 0;
            }

            return (int)Math.Round(value * 100.0 / total);
        }

        private static string FormatSeconds(int seconds)
        {
            if (seconds < 60)
            {
                return seconds + "s";
            }

            int minutes = seconds / 60;
            int rest = seconds % 60;
            return rest == 0 ? minutes + "m" : minutes + "m " + rest + "s";
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }
            if (value > max)
            {
                return max;
            }
            return value;
        }

        private bool IsAdmin(IPlayer player)
        {
            return player != null && (player.IsAdmin || player.HasPermission(PermAdmin));
        }

        private void EnsureDefaultPermissions()
        {
            string adminGroup = Interface.Oxide.Config.Options.DefaultGroups.Administrators;
            if (!string.IsNullOrEmpty(adminGroup))
            {
                GrantGroupIfMissing(adminGroup, PermAdmin);
                GrantGroupIfMissing(adminGroup, PermUse);
            }

            string playerGroup = Interface.Oxide.Config.Options.DefaultGroups.Players;
            if (config.GrantUseToDefaultGroup && !string.IsNullOrEmpty(playerGroup))
            {
                GrantGroupIfMissing(playerGroup, PermUse);
            }
        }

        private void GrantGroupIfMissing(string group, string permissionName)
        {
            if (string.IsNullOrEmpty(group) || !permission.GroupExists(group))
            {
                return;
            }

            if (!permission.GroupHasPermission(group, permissionName))
            {
                permission.GrantGroupPermission(group, permissionName, this);
            }
        }

        private void Broadcast(string message)
        {
            string formatted = Format(message);
            foreach (IPlayer player in players.Connected)
            {
                if (player != null)
                {
                    player.Reply(formatted);
                }
            }
        }

        private void Reply(IPlayer player, string message)
        {
            if (player == null || player.IsServer)
            {
                Interface.Oxide.LogInfo("[VoteSkipNight] " + message);
                return;
            }

            player.Reply(Format(message));
        }

        private string Format(string message)
        {
            return string.IsNullOrEmpty(config.ChatPrefix) ? message : config.ChatPrefix + " " + message;
        }

        #endregion

        #region Classes

        private sealed class Configuration
        {
            public string ChatPrefix = "[VoteSkip]";
            public int RequiredYesPercent = 90;
            public int VoteDurationSeconds = 120;
            public int CooldownSeconds = 300;
            public int CancelCooldownSeconds = 30;
            public int MinimumOnlinePlayers = 1;
            public int DawnBufferHours = 1;
            public bool AutoVoteYesOnStart = true;
            public bool IgnoreSleepingPlayers = true;
            public bool GrantUseToDefaultGroup = true;
        }

        private sealed class VoteSession
        {
            public string StarterId = String.Empty;
            public string StarterName = String.Empty;
            public DateTime EndsUtc;
            public readonly HashSet<string> Yes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> No = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class GuiRow
        {
            public readonly string Action;
            public readonly string Label;
            public readonly string Sub;
            public readonly bool Enabled;
            public readonly string Icon;
            public readonly string Badge;
            public readonly string Tint;

            public GuiRow(string action, string label, string sub, bool enabled,
                string icon, string badge, string tint)
            {
                Action = action ?? String.Empty;
                Label = label ?? String.Empty;
                Sub = sub ?? String.Empty;
                Enabled = enabled;
                Icon = icon ?? String.Empty;
                Badge = badge ?? String.Empty;
                Tint = tint ?? String.Empty;
            }
        }

        #endregion
    }
}

