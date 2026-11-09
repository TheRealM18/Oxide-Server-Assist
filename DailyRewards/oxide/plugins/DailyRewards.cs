using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("DailyRewards", "TheRealM18", "1.1.0")]
    [Description("Daily, weekly, monthly and seasonal playtime rewards for the Server Assist panel.")]
    public class DailyRewards : CovalencePlugin
    {
        private const string PermUse = "dailyrewards.use";
        private const string PermAdmin = "dailyrewards.admin";
        private const string ServerAssistPluginName = "ServerAssist";
        private const string ServerAssistHookId = "dailyrewards";
        private const string PluginTitle = "Daily Rewards";
        private const string DataFileName = "DailyRewards/players";
        private const int UiRows = 10;
        private const int UiButtons = 12;

        // Server Assist Art sprite keys. Install the AAZ_ServerAssistArt atlas on
        // the server and every client; Server Assist falls back to its hammer
        // symbol when a sprite is missing.
        private const string DefaultPanelIcon = "sprite:sa_daily_rewards";
        private const string IconDaily = "sprite:sa_daily_rewards";
        private const string IconWeekly = "sprite:sa_act_time";
        private const string IconMonthly = "sprite:sa_nav_snapshots";
        private const string IconSeasonal = "sprite:sa_act_weather";
        private const string IconPlaytime = "sprite:sa_act_time";
        private const string IconStreak = "sprite:sa_nav_logs";
        private const string IconAdmin = "sprite:sa_nav_moderation";
        private const string IconGrant = "sprite:sa_act_give";
        private const string IconReset = "sprite:sa_nav_snapshots";
        private const string IconSeasonSettings = "sprite:sa_act_weather";
        private const string IconPlayers = "sprite:sa_nav_players";
        private const string IconPermissions = "sprite:sa_permission_gui";
        private const string IconCommands = "sprite:sa_nav_server";
        private const string IconItems = "sprite:sa_nav_items";
        private const string IconWarning = "sprite:sa_act_warn";

        private static readonly string[] TierKeys = { "daily", "weekly", "monthly", "seasonal" };

        private Configuration config;
        private StoredData data;
        private Timer trackTimer;
        private DayOfWeek weeklyResetDay = DayOfWeek.Monday;
        private DateTime nextSaveUtc = DateTime.UtcNow;
        private bool loggedHookRegistration;

        private readonly Dictionary<string, UiSession> sessions =
            new Dictionary<string, UiSession>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, ActiveSession> activeSessions =
            new Dictionary<string, ActiveSession>(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> warnedItems =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        #region Lifecycle

        private void Init()
        {
            permission.RegisterPermission(PermUse, this);
            permission.RegisterPermission(PermAdmin, this);

            LoadConfig();
            LoadData();
            RegisterConfiguredPermissions();

            AddCovalenceCommand(new[] { "rewards", "daily" }, nameof(CommandRewards));

            timer.Once(1f, EnsureDefaultPermissions);
            timer.Once(2f, TryRegisterServerAssistPanel);
            timer.Once(5f, EnsureDefaultPermissions);
            timer.Once(5f, TryRegisterServerAssistPanel);
            timer.Once(8f, StartTrackingAllOnline);

            trackTimer = timer.Every(60f, TickPlaytime);
            nextSaveUtc = DateTime.UtcNow.AddSeconds(config.SaveIntervalSeconds);
        }

        private void OnServerInitialized(bool initial)
        {
            EnsureDefaultPermissions();
            RegisterConfiguredPermissions();
            StartTrackingAllOnline();
            TryRegisterServerAssistPanel();
        }

        private void OnServerSave()
        {
            FlushAllOnline();
            SaveData();
        }

        private void OnServerShutdown()
        {
            FlushAllOnline();
            SaveData();
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin != null && string.Equals(plugin.Name, ServerAssistPluginName, StringComparison.OrdinalIgnoreCase))
            {
                TryRegisterServerAssistPanel();
            }
        }

        private void OnServerAssistReady()
        {
            TryRegisterServerAssistPanel();
        }

        private void Unload()
        {
            FlushAllOnline();
            SaveData();
            TryUnregisterServerAssistPanel();

            if (trackTimer != null)
            {
                timer.Destroy(ref trackTimer);
            }

            sessions.Clear();
            activeSessions.Clear();
        }

        private void OnUserConnected(IPlayer player)
        {
            StartSession(player);
        }

        private void OnUserDisconnected(IPlayer player, string reason)
        {
            if (player == null || string.IsNullOrEmpty(player.Id))
            {
                return;
            }

            FlushSession(player.Id, DateTime.UtcNow, DateTime.Now, false);
            activeSessions.Remove(player.Id);
            sessions.Remove(player.Id);
            SaveData();
        }

        protected override void LoadDefaultConfig()
        {
            config = new Configuration();
            NormalizeConfig();
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
                Interface.Oxide.LogWarning("[DailyRewards] config could not be read: " + exception.Message);
                config = null;
            }

            if (config == null)
            {
                config = new Configuration();
            }

            NormalizeConfig();
            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        private void LoadData()
        {
            try
            {
                data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(DataFileName);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[DailyRewards] data could not be read: " + exception.Message);
                data = null;
            }

            NormalizeData();
        }

        private void SaveData()
        {
            try
            {
                Interface.Oxide.DataFileSystem.WriteObject(DataFileName, data, true);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogError("[DailyRewards] data could not be saved: " + exception);
            }

            nextSaveUtc = DateTime.UtcNow.AddSeconds(config.SaveIntervalSeconds);
        }

        private void NormalizeData()
        {
            if (data == null)
            {
                data = new StoredData();
            }

            Dictionary<string, PlayerData> players =
                new Dictionary<string, PlayerData>(StringComparer.OrdinalIgnoreCase);

            if (data.Players != null)
            {
                foreach (KeyValuePair<string, PlayerData> pair in data.Players)
                {
                    if (string.IsNullOrEmpty(pair.Key))
                    {
                        continue;
                    }

                    PlayerData player = pair.Value ?? new PlayerData();
                    player.Name = string.IsNullOrEmpty(player.Name) ? pair.Key : player.Name.Trim();

                    Dictionary<string, TierProgress> tiers =
                        new Dictionary<string, TierProgress>(StringComparer.OrdinalIgnoreCase);

                    if (player.Tiers != null)
                    {
                        foreach (KeyValuePair<string, TierProgress> tierPair in player.Tiers)
                        {
                            string tierKey = NormalizeTierKey(tierPair.Key);
                            if (string.IsNullOrEmpty(tierKey))
                            {
                                continue;
                            }

                            tiers[tierKey] = tierPair.Value ?? new TierProgress();
                        }
                    }

                    player.Tiers = tiers;
                    EnsureTierProgress(player);

                    if (player.FirstSeenUtc <= 0)
                    {
                        player.FirstSeenUtc = UnixNow();
                    }

                    players[pair.Key] = player;
                }
            }

            data.Players = players;
        }

        private void NormalizeConfig()
        {
            if (config == null)
            {
                config = new Configuration();
            }

            config.ChatPrefix = Clean(config.ChatPrefix, 32);
            if (string.IsNullOrEmpty(config.ChatPrefix))
            {
                config.ChatPrefix = "[Rewards]";
            }

            config.HookLabel = Clean(config.HookLabel, 48);
            if (string.IsNullOrEmpty(config.HookLabel))
            {
                config.HookLabel = PluginTitle;
            }

            config.HookDescription = Clean(config.HookDescription, 160);
            if (string.IsNullOrEmpty(config.HookDescription))
            {
                config.HookDescription = "Claim daily, weekly, monthly and seasonal rewards.";
            }

            if (config.HookPermission == null)
            {
                config.HookPermission = PermUse;
            }
            else
            {
                config.HookPermission = config.HookPermission.Trim();
            }

            config.PanelIcon = Clean(config.PanelIcon, 120);
            if (string.IsNullOrEmpty(config.PanelIcon))
            {
                config.PanelIcon = DefaultPanelIcon;
            }

            config.PanelOrder = Clamp(config.PanelOrder, 0, 1000);

            config.DailyResetHour = ((config.DailyResetHour % 24) + 24) % 24;
            config.SaveIntervalSeconds = Clamp(config.SaveIntervalSeconds, 30, 3600);
            config.MaxTickAccrualSeconds = Clamp(config.MaxTickAccrualSeconds, 60, 86400);

            config.WeeklyResetDay = Clean(config.WeeklyResetDay, 16);
            DayOfWeek parsedDay;
            if (!Enum.TryParse(config.WeeklyResetDay, true, out parsedDay))
            {
                parsedDay = DayOfWeek.Monday;
            }

            weeklyResetDay = parsedDay;
            config.WeeklyResetDay = parsedDay.ToString();

            config.SeasonMode = string.Equals(config.SeasonMode, "manual", StringComparison.OrdinalIgnoreCase)
                ? "manual"
                : "calendar";

            config.ManualSeason = Clean(config.ManualSeason, 48);
            if (string.IsNullOrEmpty(config.ManualSeason))
            {
                config.ManualSeason = "Season 1";
            }

            config.Daily = NormalizeTier(config.Daily, "daily");
            config.Weekly = NormalizeTier(config.Weekly, "weekly");
            config.Monthly = NormalizeTier(config.Monthly, "monthly");
            config.Seasonal = NormalizeTier(config.Seasonal, "seasonal");
        }

        private RewardTier NormalizeTier(RewardTier tier, string tierKey)
        {
            if (tier == null)
            {
                tier = CreateDefaultTier(tierKey);
            }

            tier.DisplayName = Clean(tier.DisplayName, 32);
            if (string.IsNullOrEmpty(tier.DisplayName))
            {
                tier.DisplayName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(tierKey);
            }

            tier.RequiredPlaytimeMinutes = Clamp(tier.RequiredPlaytimeMinutes, 0, 10080);
            tier.Permission = Clean(tier.Permission, 96);
            tier.Icon = Clean(tier.Icon, 120);
            if (string.IsNullOrEmpty(tier.Icon))
            {
                tier.Icon = DefaultTierIcon(tierKey);
            }

            List<RewardItem> items = new List<RewardItem>();
            if (tier.Items != null)
            {
                for (int i = 0; i < tier.Items.Count; i++)
                {
                    RewardItem item = tier.Items[i];
                    if (item == null || string.IsNullOrEmpty(item.Item))
                    {
                        continue;
                    }

                    item.Item = Clean(item.Item, 96);
                    item.Count = Clamp(item.Count, 1, 100000);
                    item.Quality = Clamp(item.Quality, 0, 6);
                    items.Add(item);
                }
            }

            tier.Items = items;
            tier.Commands = NormalizeStringList(tier.Commands, 30, 512);
            tier.Permissions = NormalizeStringList(tier.Permissions, 30, 96);
            return tier;
        }

        private static string DefaultTierIcon(string tierKey)
        {
            switch (NormalizeTierKey(tierKey))
            {
                case "daily":
                    return IconDaily;
                case "weekly":
                    return IconWeekly;
                case "monthly":
                    return IconMonthly;
                case "seasonal":
                    return IconSeasonal;
                default:
                    return IconItems;
            }
        }

        private static RewardTier CreateDefaultTier(string tierKey)
        {
            tierKey = NormalizeTierKey(tierKey);
            RewardTier tier = new RewardTier
            {
                DisplayName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(tierKey),
                RequiredPlaytimeMinutes = string.Equals(tierKey, "daily", StringComparison.Ordinal) ? 60 : 0
            };

            switch (tierKey)
            {
                case "daily":
                    tier.Items = new List<RewardItem>
                    {
                        new RewardItem { Item = "casinoCoin", Count = 10 },
                        new RewardItem { Item = "medicalFirstAidBandage", Count = 1 }
                    };
                    break;

                case "weekly":
                    tier.Items = new List<RewardItem>
                    {
                        new RewardItem { Item = "casinoCoin", Count = 75 },
                        new RewardItem { Item = "medicalFirstAidBandage", Count = 3 }
                    };
                    break;

                case "monthly":
                    tier.Items = new List<RewardItem>
                    {
                        new RewardItem { Item = "casinoCoin", Count = 300 },
                        new RewardItem { Item = "medicalFirstAidBandage", Count = 10 }
                    };
                    break;

                default:
                    tier.Items = new List<RewardItem>
                    {
                        new RewardItem { Item = "casinoCoin", Count = 1500 },
                        new RewardItem { Item = "medicalFirstAidBandage", Count = 20 }
                    };
                    break;
            }

            return tier;
        }

        private static List<string> NormalizeStringList(List<string> source, int maxEntries, int maxLength)
        {
            List<string> result = new List<string>();
            if (source == null)
            {
                return result;
            }

            for (int i = 0; i < source.Count && result.Count < maxEntries; i++)
            {
                string value = Clean(source[i], maxLength);
                if (!string.IsNullOrEmpty(value))
                {
                    result.Add(value);
                }
            }

            return result;
        }

        private void RegisterConfiguredPermissions()
        {
            permission.RegisterPermission(PermUse, this);
            permission.RegisterPermission(PermAdmin, this);

            for (int i = 0; i < TierKeys.Length; i++)
            {
                RewardTier tier = GetTier(TierKeys[i]);
                if (tier == null)
                {
                    continue;
                }

                RegisterOwnedPermission(tier.Permission);

                for (int j = 0; j < tier.Permissions.Count; j++)
                {
                    RegisterOwnedPermission(tier.Permissions[j]);
                }
            }

            RegisterOwnedPermission(config.HookPermission);
        }

        private void RegisterOwnedPermission(string permissionName)
        {
            if (string.IsNullOrEmpty(permissionName) ||
                !permissionName.StartsWith("dailyrewards.", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                permission.RegisterPermission(permissionName, this);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[DailyRewards] permission '" + permissionName +
                                            "' could not be registered: " + exception.Message);
            }
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
            if (!string.IsNullOrEmpty(playerGroup) && config.GrantUseToDefaultGroup)
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

        #endregion

        #region Playtime tracking

        private void StartTrackingAllOnline()
        {
            foreach (IPlayer player in players.Connected)
            {
                StartSession(player);
            }
        }

        private void StartSession(IPlayer player)
        {
            if (player == null || string.IsNullOrEmpty(player.Id) || player.IsServer || !player.IsConnected)
            {
                return;
            }

            PlayerData playerData = GetOrCreatePlayerData(player.Id, player.Name);
            playerData.Name = string.IsNullOrEmpty(player.Name) ? playerData.Name : player.Name.Trim();
            playerData.LastSeenUtc = UnixNow();

            if (playerData.FirstSeenUtc <= 0)
            {
                playerData.FirstSeenUtc = playerData.LastSeenUtc;
            }

            NormalizeAllTiers(playerData, DateTime.Now);

            activeSessions[player.Id] = new ActiveSession
            {
                LastAccruedUtc = DateTime.UtcNow
            };

            SaveDataSoon();
        }

        private void TickPlaytime()
        {
            DateTime utcNow = DateTime.UtcNow;
            DateTime localNow = DateTime.Now;

            List<string> ids = activeSessions.Keys.ToList();
            for (int i = 0; i < ids.Count; i++)
            {
                string playerId = ids[i];
                IPlayer player = players.FindPlayerById(playerId);
                if (player == null || !player.IsConnected)
                {
                    FlushSession(playerId, utcNow, localNow, false);
                    activeSessions.Remove(playerId);
                    continue;
                }

                FlushSession(playerId, utcNow, localNow, true);
            }

            if (utcNow >= nextSaveUtc)
            {
                SaveData();
            }
        }

        private void FlushAllOnline()
        {
            DateTime utcNow = DateTime.UtcNow;
            DateTime localNow = DateTime.Now;

            List<string> ids = activeSessions.Keys.ToList();
            for (int i = 0; i < ids.Count; i++)
            {
                FlushSession(ids[i], utcNow, localNow, false);
            }
        }

        private void FlushSession(string playerId, DateTime utcNow, DateTime localNow, bool notify)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return;
            }

            ActiveSession session;
            if (!activeSessions.TryGetValue(playerId, out session))
            {
                return;
            }

            PlayerData playerData;
            if (!data.Players.TryGetValue(playerId, out playerData) || playerData == null)
            {
                activeSessions.Remove(playerId);
                return;
            }

            double elapsed = (utcNow - session.LastAccruedUtc).TotalSeconds;
            session.LastAccruedUtc = utcNow;

            if (elapsed < 0)
            {
                elapsed = 0;
            }

            if (elapsed > config.MaxTickAccrualSeconds)
            {
                elapsed = config.MaxTickAccrualSeconds;
            }

            if (elapsed > 0)
            {
                playerData.TotalPlaySeconds += elapsed;
                playerData.LastSeenUtc = UnixNow();

                NormalizeAllTiers(playerData, localNow);
                for (int i = 0; i < TierKeys.Length; i++)
                {
                    TierProgress progress = GetProgress(playerData, TierKeys[i]);
                    progress.PeriodSeconds += elapsed;
                }
            }

            if (notify)
            {
                NotifyDailyReady(playerId, playerData, localNow);
            }
        }

        private void NotifyDailyReady(string playerId, PlayerData playerData, DateTime now)
        {
            if (!config.NotifyWhenReady || config.Daily == null || !config.Daily.Enabled)
            {
                return;
            }

            if (!CanUseTier(playerId, config.Daily))
            {
                return;
            }

            NormalizeProgress(playerData, "daily", now);
            TierProgress progress = GetProgress(playerData, "daily");

            if (string.Equals(progress.ClaimedPeriod, progress.PeriodKey, StringComparison.Ordinal))
            {
                playerData.LastNotifiedDailyPeriod = progress.PeriodKey;
                return;
            }

            double required = RequiredSeconds(config.Daily);
            if (required > 0 && progress.PeriodSeconds < required)
            {
                return;
            }

            if (string.Equals(playerData.LastNotifiedDailyPeriod, progress.PeriodKey, StringComparison.Ordinal))
            {
                return;
            }

            playerData.LastNotifiedDailyPeriod = progress.PeriodKey;

            IPlayer player = players.FindPlayerById(playerId);
            if (player != null)
            {
                Reply(player, "Your daily reward is ready. Use /rewards to claim it.");
            }
        }

        #endregion

        #region Reward periods

        private string GetPeriodKey(string tierKey, DateTime now)
        {
            switch (tierKey)
            {
                case "daily":
                    return GetDailyStart(now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                case "weekly":
                    return GetWeeklyStart(now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                case "monthly":
                    return GetMonthlyStart(now).ToString("yyyy-MM", CultureInfo.InvariantCulture);
                case "seasonal":
                    return GetSeasonKey(now);
                default:
                    return now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }

        private DateTime GetNextReset(string tierKey, DateTime now)
        {
            switch (tierKey)
            {
                case "daily":
                    return GetDailyStart(now).AddDays(1);
                case "weekly":
                    return GetWeeklyStart(now).AddDays(7);
                case "monthly":
                    return GetMonthlyStart(now).AddMonths(1);
                case "seasonal":
                    string name;
                    string key;
                    DateTime start;
                    DateTime next;
                    GetSeasonWindow(now, out name, out key, out start, out next);
                    return next;
                default:
                    return now.AddDays(1);
            }
        }

        private DateTime GetDailyStart(DateTime now)
        {
            DateTime shifted = now.AddHours(-config.DailyResetHour);
            return shifted.Date.AddHours(config.DailyResetHour);
        }

        private DateTime GetWeeklyStart(DateTime now)
        {
            DateTime shifted = now.AddHours(-config.DailyResetHour).Date;
            int difference = ((int)shifted.DayOfWeek - (int)weeklyResetDay + 7) % 7;
            return shifted.AddDays(-difference).AddHours(config.DailyResetHour);
        }

        private DateTime GetMonthlyStart(DateTime now)
        {
            DateTime shifted = now.AddHours(-config.DailyResetHour);
            return new DateTime(shifted.Year, shifted.Month, 1).AddHours(config.DailyResetHour);
        }

        private string GetSeasonKey(DateTime now)
        {
            string name;
            string key;
            DateTime start;
            DateTime next;
            GetSeasonWindow(now, out name, out key, out start, out next);
            return key;
        }

        private string GetSeasonName(DateTime now)
        {
            string name;
            string key;
            DateTime start;
            DateTime next;
            GetSeasonWindow(now, out name, out key, out start, out next);
            return name;
        }

        private void GetSeasonWindow(DateTime now, out string name, out string key, out DateTime start, out DateTime next)
        {
            if (string.Equals(config.SeasonMode, "manual", StringComparison.OrdinalIgnoreCase))
            {
                name = config.ManualSeason;
                key = "manual|" + name.ToLowerInvariant();
                start = DateTime.MinValue;
                next = DateTime.MaxValue;
                return;
            }

            DateTime shifted = now.AddHours(-config.DailyResetHour);
            int month = shifted.Month;
            int seasonIndex;
            DateTime seasonStart;

            if (month == 12)
            {
                seasonIndex = 0;
                seasonStart = new DateTime(shifted.Year, 12, 1);
            }
            else if (month <= 2)
            {
                seasonIndex = 0;
                seasonStart = new DateTime(shifted.Year - 1, 12, 1);
            }
            else if (month <= 5)
            {
                seasonIndex = 1;
                seasonStart = new DateTime(shifted.Year, 3, 1);
            }
            else if (month <= 8)
            {
                seasonIndex = 2;
                seasonStart = new DateTime(shifted.Year, 6, 1);
            }
            else
            {
                seasonIndex = 3;
                seasonStart = new DateTime(shifted.Year, 9, 1);
            }

            string[] northernNames = { "Winter", "Spring", "Summer", "Fall" };
            string[] southernNames = { "Summer", "Autumn", "Winter", "Spring" };
            name = config.SouthernHemisphere ? southernNames[seasonIndex] : northernNames[seasonIndex];

            start = seasonStart.AddHours(config.DailyResetHour);
            next = start.AddMonths(3);
            key = "calendar|" + seasonStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private void NormalizeProgress(PlayerData playerData, string tierKey, DateTime now)
        {
            string periodKey = GetPeriodKey(tierKey, now);
            TierProgress progress = GetProgress(playerData, tierKey);

            if (!string.Equals(progress.PeriodKey, periodKey, StringComparison.Ordinal))
            {
                progress.PeriodKey = periodKey;
                progress.PeriodSeconds = 0;
            }
        }

        private void NormalizeAllTiers(PlayerData playerData, DateTime now)
        {
            EnsureTierProgress(playerData);
            for (int i = 0; i < TierKeys.Length; i++)
            {
                NormalizeProgress(playerData, TierKeys[i], now);
            }
        }

        private void EnsureTierProgress(PlayerData playerData)
        {
            if (playerData.Tiers == null)
            {
                playerData.Tiers = new Dictionary<string, TierProgress>(StringComparer.OrdinalIgnoreCase);
            }

            for (int i = 0; i < TierKeys.Length; i++)
            {
                if (!playerData.Tiers.ContainsKey(TierKeys[i]))
                {
                    playerData.Tiers[TierKeys[i]] = new TierProgress();
                }
            }
        }

        private TierProgress GetProgress(PlayerData playerData, string tierKey)
        {
            EnsureTierProgress(playerData);

            TierProgress progress;
            if (!playerData.Tiers.TryGetValue(tierKey, out progress) || progress == null)
            {
                progress = new TierProgress();
                playerData.Tiers[tierKey] = progress;
            }

            return progress;
        }

        private double RequiredSeconds(RewardTier tier)
        {
            return tier == null ? 0 : Math.Max(0, tier.RequiredPlaytimeMinutes) * 60.0;
        }

        #endregion

        #region Reward granting

        private bool ClaimOrGrantReward(string playerId, string tierKey, bool adminGrant, out string message)
        {
            message = string.Empty;

            tierKey = NormalizeTierKey(tierKey);
            RewardTier tier = GetTier(tierKey);
            if (tier == null)
            {
                message = "Unknown reward tier.";
                return false;
            }

            if (!tier.Enabled)
            {
                message = tier.DisplayName + " rewards are disabled.";
                return false;
            }

            IPlayer player = players.FindPlayerById(playerId);
            if (player == null || !player.IsConnected)
            {
                message = "That player is not online.";
                return false;
            }

            EntityPlayer entity = player.Object as EntityPlayer;
            if (entity == null)
            {
                message = "That player is not spawned yet. Try again in a moment.";
                return false;
            }

            if (!adminGrant && !CanUseTier(playerId, tier))
            {
                message = "You do not have permission to claim " + tier.DisplayName + " rewards.";
                return false;
            }

            PlayerData playerData = GetOrCreatePlayerData(playerId, player.Name);
            DateTime now = DateTime.Now;
            NormalizeAllTiers(playerData, now);

            TierProgress progress = GetProgress(playerData, tierKey);
            string periodKey = progress.PeriodKey;

            if (!adminGrant)
            {
                if (string.Equals(progress.ClaimedPeriod, periodKey, StringComparison.Ordinal))
                {
                    message = "You already claimed the " + tier.DisplayName + " reward this period. " +
                              FormatReset(tierKey, now) + ".";
                    return false;
                }

                double required = RequiredSeconds(tier);
                if (required > 0 && progress.PeriodSeconds < required)
                {
                    message = "The " + tier.DisplayName + " reward is locked. Play " +
                              FormatDuration(required - progress.PeriodSeconds) +
                              " more this period (" + FormatDuration(progress.PeriodSeconds) + " / " +
                              FormatDuration(required) + ").";
                    return false;
                }
            }

            string detail;
            if (!GiveRewardBundle(player, entity, tierKey, tier, out detail))
            {
                message = "The reward bundle could not be granted: " + detail;
                return false;
            }

            if (!adminGrant)
            {
                progress.ClaimedPeriod = periodKey;
                progress.LastClaimUtc = UnixNow();
                progress.ClaimCount++;

                if (string.Equals(tierKey, "daily", StringComparison.OrdinalIgnoreCase))
                {
                    UpdateDailyStreak(playerData, now, periodKey);
                    playerData.LastNotifiedDailyPeriod = periodKey;
                }
            }

            SaveData();

            if (adminGrant)
            {
                Interface.Oxide.LogInfo("[DailyRewards] Admin grant: " + tier.DisplayName + " -> " +
                                        player.Name + " (" + player.Id + "): " + detail);
            }

            string action = adminGrant ? "Granted " : "Claimed ";
            message = action + tier.DisplayName + " reward: " + detail;
            return true;
        }

        private void UpdateDailyStreak(PlayerData playerData, DateTime now, string currentPeriodKey)
        {
            string previousPeriodKey = GetPeriodKey("daily", now.AddDays(-1));
            if (string.Equals(playerData.LastDailyClaimPeriod, previousPeriodKey, StringComparison.Ordinal))
            {
                playerData.DailyStreak++;
            }
            else
            {
                playerData.DailyStreak = 1;
            }

            playerData.LastDailyClaimPeriod = currentPeriodKey;
        }

        private bool GiveRewardBundle(IPlayer player, EntityPlayer entity, string tierKey, RewardTier tier, out string detail)
        {
            List<string> parts = new List<string>();
            bool grantedSomething = false;

            for (int i = 0; i < tier.Items.Count; i++)
            {
                RewardItem rewardItem = tier.Items[i];
                ItemClass itemClass = FindItemClass(rewardItem.Item);
                if (itemClass == null)
                {
                    WarnMissingItem(rewardItem.Item);
                    continue;
                }

                ItemValue itemValue = CreateRewardValue(itemClass, rewardItem.Quality);
                if (itemValue == null || itemValue.IsEmpty())
                {
                    WarnMissingItem(rewardItem.Item);
                    continue;
                }

                int added;
                int dropped;
                GiveItemCount(entity, itemClass, itemValue, rewardItem.Count, out added, out dropped);

                if (added > 0 || dropped > 0)
                {
                    grantedSomething = true;
                    string label = rewardItem.Count + "x " + rewardItem.Item;
                    if (dropped > 0)
                    {
                        label += " (" + dropped + " dropped at your feet)";
                    }

                    parts.Add(label);
                }
            }

            for (int i = 0; i < tier.Permissions.Count; i++)
            {
                string node = tier.Permissions[i];
                if (string.IsNullOrEmpty(node))
                {
                    continue;
                }

                try
                {
                    if (!permission.UserHasPermission(player.Id, node))
                    {
                        permission.GrantUserPermission(player.Id, node, this);
                    }

                    grantedSomething = true;
                    parts.Add("permission " + node);
                }
                catch (Exception exception)
                {
                    Interface.Oxide.LogWarning("[DailyRewards] could not grant permission '" + node + "' to " +
                                                player.Name + ": " + exception.Message);
                }
            }

            for (int i = 0; i < tier.Commands.Count; i++)
            {
                string command = ExpandCommand(tier.Commands[i], player, entity, tierKey);
                if (string.IsNullOrEmpty(command))
                {
                    continue;
                }

                try
                {
                    SdtdConsole console = SdtdConsole.Instance;
                    if (console != null)
                    {
                        console.ExecuteSync(command, null);
                        grantedSomething = true;
                        parts.Add("server command");
                    }
                }
                catch (Exception exception)
                {
                    Interface.Oxide.LogWarning("[DailyRewards] reward command failed: " + command + " - " +
                                                exception.Message);
                }
            }

            if (!grantedSomething)
            {
                detail = "the reward bundle is empty or every configured item is invalid.";
                return false;
            }

            detail = string.Join(", ", parts.ToArray());
            return true;
        }

        private string ExpandCommand(string command, IPlayer player, EntityPlayer entity, string tierKey)
        {
            if (string.IsNullOrEmpty(command))
            {
                return string.Empty;
            }

            string entityId = entity != null
                ? entity.entityId.ToString(CultureInfo.InvariantCulture)
                : string.Empty;

            return command
                .Replace("{playerid}", player.Id)
                .Replace("{steamid}", player.Id)
                .Replace("{entityid}", entityId)
                .Replace("{name}", player.Name)
                .Replace("{player}", player.Name)
                .Replace("{tier}", tierKey);
        }

        private void GiveItemCount(EntityPlayer player, ItemClass itemClass, ItemValue itemValue, int count,
                                   out int added, out int dropped)
        {
            added = 0;
            dropped = 0;

            if (player == null || itemClass == null || itemValue == null || count <= 0)
            {
                return;
            }

            int maxStack = GetMaxStack(itemClass);
            int remaining = count;

            while (remaining > 0)
            {
                int amount = Math.Min(remaining, maxStack);
                ItemStack stack = new ItemStack(itemValue.Clone(), amount);
                int received = AddItemToPlayer(player, stack);
                added += received;

                if (received < amount)
                {
                    int missing = amount - received;
                    DropItems(player, itemValue, missing);
                    dropped += missing;
                }

                remaining -= amount;
            }
        }

        private int AddItemToPlayer(EntityPlayer player, ItemStack stack)
        {
            if (player == null || stack == null || stack.IsEmpty())
            {
                return 0;
            }

            try
            {
                int original = stack.count;
                ItemStack remaining = stack.Clone();

                if (player.bag != null)
                {
                    player.bag.TryStackItem(0, remaining);
                }

                if (remaining.count > 0 && player.inventory != null)
                {
                    player.inventory.TryStackItem(0, remaining);
                }

                if (remaining.count > 0 && player.bag != null && player.bag.AddItem(remaining))
                {
                    remaining.count = 0;
                }
                else if (remaining.count > 0 && player.inventory != null && player.inventory.AddItem(remaining))
                {
                    remaining.count = 0;
                }

                return original - remaining.count;
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogError("[DailyRewards] could not add an item to " +
                                          (player.PlayerDisplayName ?? "a player") + ": " + exception);
                return 0;
            }
        }

        private void DropItems(EntityPlayer player, ItemValue itemValue, int count)
        {
            if (player == null || itemValue == null || count <= 0)
            {
                return;
            }

            try
            {
                if (GameManager.Instance == null || GameManager.Instance.World == null)
                {
                    return;
                }

                ItemClass itemClass = ItemClass.list != null && itemValue.type >= 0 && itemValue.type < ItemClass.list.Length
                    ? ItemClass.list[itemValue.type]
                    : null;
                int maxStack = GetMaxStack(itemClass);
                int remaining = count;

                while (remaining > 0)
                {
                    int amount = Math.Min(remaining, maxStack);
                    ItemStack stack = new ItemStack(itemValue.Clone(), amount);
                    GameManager.Instance.ItemDropServer(stack, player.GetPosition(), Vector3.zero);
                    remaining -= amount;
                }
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[DailyRewards] could not drop overflow items for " +
                                            (player.PlayerDisplayName ?? "a player") + ": " + exception.Message);
            }
        }

        private ItemValue CreateRewardValue(ItemClass itemClass, int configuredQuality)
        {
            if (itemClass == null)
            {
                return null;
            }

            int quality = 1;
            if (itemClass.HasQuality)
            {
                quality = configuredQuality;
                if (quality < 1 || quality > 6)
                {
                    quality = 6;
                }
            }

            try
            {
                return new ItemValue(itemClass.Id, quality, quality, false);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[DailyRewards] could not create item value for '" +
                                            itemClass.Name + "': " + exception.Message);
                return null;
            }
        }

        private static int GetMaxStack(ItemClass itemClass)
        {
            int maxStack = itemClass != null && itemClass.CanStack() ? itemClass.MaxCount : 1;
            return Math.Max(1, maxStack);
        }

        private ItemClass FindItemClass(string name)
        {
            if (string.IsNullOrEmpty(name) || ItemClass.list == null)
            {
                return null;
            }

            try
            {
                ItemClass direct = ItemClass.GetItemClass(name, true);
                if (direct != null)
                {
                    return direct;
                }
            }
            catch
            {
                // Fall through to a manual match.
            }

            string needle = name.Trim().ToLowerInvariant();
            for (int i = 0; i < ItemClass.list.Length; i++)
            {
                ItemClass item = ItemClass.list[i];
                if (item == null)
                {
                    continue;
                }

                string internalName = item.Name ?? string.Empty;
                if (string.Equals(internalName, needle, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }

                try
                {
                    string displayName = item.GetItemName();
                    if (!string.IsNullOrEmpty(displayName) &&
                        string.Equals(displayName, needle, StringComparison.OrdinalIgnoreCase))
                    {
                        return item;
                    }
                }
                catch
                {
                    // Ignore items whose display name cannot be read.
                }
            }

            return null;
        }

        private void WarnMissingItem(string itemName)
        {
            if (string.IsNullOrEmpty(itemName) || !warnedItems.Add(itemName))
            {
                return;
            }

            Interface.Oxide.LogWarning("[DailyRewards] reward item '" + itemName +
                                        "' was not found. Check the config against the server item list.");
        }

        #endregion

        #region Player data helpers

        private PlayerData GetOrCreatePlayerData(string playerId, string playerName)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return new PlayerData();
            }

            PlayerData playerData;
            if (!data.Players.TryGetValue(playerId, out playerData) || playerData == null)
            {
                playerData = new PlayerData
                {
                    Name = string.IsNullOrEmpty(playerName) ? playerId : playerName.Trim(),
                    FirstSeenUtc = UnixNow(),
                    LastSeenUtc = UnixNow()
                };

                data.Players[playerId] = playerData;
            }

            EnsureTierProgress(playerData);

            if (!string.IsNullOrEmpty(playerName))
            {
                playerData.Name = playerName.Trim();
            }

            return playerData;
        }

        private bool TryGetPlayerData(string playerId, out PlayerData playerData)
        {
            playerData = null;
            return !string.IsNullOrEmpty(playerId) && data.Players.TryGetValue(playerId, out playerData) &&
                   playerData != null;
        }

        private PlayerData ResolvePlayerData(string nameOrId, out string playerId)
        {
            playerId = null;
            if (string.IsNullOrEmpty(nameOrId))
            {
                return null;
            }

            PlayerData direct;
            if (data.Players.TryGetValue(nameOrId, out direct) && direct != null)
            {
                playerId = nameOrId;
                return direct;
            }

            IPlayer player = players.FindPlayer(nameOrId);
            if (player != null && !string.IsNullOrEmpty(player.Id))
            {
                PlayerData byPlayer;
                if (data.Players.TryGetValue(player.Id, out byPlayer) && byPlayer != null)
                {
                    playerId = player.Id;
                    return byPlayer;
                }
            }

            foreach (KeyValuePair<string, PlayerData> pair in data.Players)
            {
                if (pair.Value != null &&
                    string.Equals(pair.Value.Name, nameOrId, StringComparison.OrdinalIgnoreCase))
                {
                    playerId = pair.Key;
                    return pair.Value;
                }
            }

            return null;
        }

        private IPlayer ResolveOnlinePlayer(string nameOrId)
        {
            if (string.IsNullOrEmpty(nameOrId))
            {
                return null;
            }

            IPlayer player = players.FindPlayerById(nameOrId);
            if (player == null)
            {
                player = players.FindPlayer(nameOrId);
            }

            return player != null && player.IsConnected ? player : null;
        }

        private string GetPlayerName(string playerId)
        {
            IPlayer player = players.FindPlayerById(playerId);
            if (player != null && !string.IsNullOrEmpty(player.Name))
            {
                return player.Name;
            }

            PlayerData playerData;
            if (TryGetPlayerData(playerId, out playerData) && !string.IsNullOrEmpty(playerData.Name))
            {
                return playerData.Name;
            }

            return playerId;
        }

        #endregion

        #region Tier helpers

        private RewardTier GetTier(string tierKey)
        {
            switch (NormalizeTierKey(tierKey))
            {
                case "daily":
                    return config.Daily;
                case "weekly":
                    return config.Weekly;
                case "monthly":
                    return config.Monthly;
                case "seasonal":
                    return config.Seasonal;
                default:
                    return null;
            }
        }

        private static string NormalizeTierKey(string tierKey)
        {
            if (string.IsNullOrEmpty(tierKey))
            {
                return string.Empty;
            }

            switch (tierKey.Trim().ToLowerInvariant())
            {
                case "daily":
                case "day":
                    return "daily";
                case "weekly":
                case "week":
                    return "weekly";
                case "monthly":
                case "month":
                    return "monthly";
                case "seasonal":
                case "season":
                case "seasonly":
                    return "seasonal";
                default:
                    return string.Empty;
            }
        }

        private bool CanUseTier(string playerId, RewardTier tier)
        {
            if (!permission.IsLoaded)
            {
                return false;
            }

            if (IsAdmin(playerId))
            {
                return true;
            }

            if (!permission.UserHasPermission(playerId, PermUse))
            {
                if (string.IsNullOrEmpty(config.HookPermission) ||
                    !permission.UserHasPermission(playerId, config.HookPermission))
                {
                    return false;
                }
            }

            return tier == null || string.IsNullOrEmpty(tier.Permission) ||
                   permission.UserHasPermission(playerId, tier.Permission);
        }

        private bool CanUseGui(string playerId)
        {
            if (!permission.IsLoaded)
            {
                return false;
            }

            if (IsAdmin(playerId))
            {
                return true;
            }

            if (permission.UserHasPermission(playerId, PermUse))
            {
                return true;
            }

            if (string.IsNullOrEmpty(config.HookPermission))
            {
                return true;
            }

            return permission.UserHasPermission(playerId, config.HookPermission);
        }

        private bool IsAdmin(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return false;
            }

            if (playerId.Equals("server_console", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return permission.IsLoaded && permission.UserHasPermission(playerId, PermAdmin);
        }

        private bool IsAdmin(IPlayer player)
        {
            return player != null && (player.IsServer || IsAdmin(player.Id));
        }

        private string DescribeTier(string playerId, PlayerData playerData, string tierKey, DateTime now, out bool ready)
        {
            ready = false;
            RewardTier tier = GetTier(tierKey);
            if (tier == null)
            {
                return "Unknown tier";
            }

            if (!tier.Enabled)
            {
                return "Disabled";
            }

            if (!CanUseTier(playerId, tier))
            {
                return "No permission";
            }

            NormalizeProgress(playerData, tierKey, now);
            TierProgress progress = GetProgress(playerData, tierKey);
            string reset = " | " + FormatReset(tierKey, now);

            if (string.Equals(progress.ClaimedPeriod, progress.PeriodKey, StringComparison.Ordinal))
            {
                return "Claimed" + reset;
            }

            double required = RequiredSeconds(tier);
            if (required > 0 && progress.PeriodSeconds < required)
            {
                return "Locked | " + FormatDuration(progress.PeriodSeconds) + " / " + FormatDuration(required) +
                       " played this period" + reset;
            }

            ready = true;
            return "Ready to claim" + reset;
        }

        private string GetItemIcon(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
            {
                return IconItems;
            }

            return FindItemClass(itemName) != null ? "item:" + itemName : IconItems;
        }

        private static string GetTierIcon(string tierKey, RewardTier tier)
        {
            if (tier != null && !string.IsNullOrEmpty(tier.Icon))
            {
                return tier.Icon;
            }

            return DefaultTierIcon(tierKey);
        }

        private string GetTierBadge(string playerId, PlayerData playerData, string tierKey, DateTime now, bool ready)
        {
            RewardTier tier = GetTier(tierKey);
            if (tier == null || !tier.Enabled)
            {
                return "OFF";
            }

            if (!CanUseTier(playerId, tier))
            {
                return "LOCKED";
            }

            NormalizeProgress(playerData, tierKey, now);
            TierProgress progress = GetProgress(playerData, tierKey);
            if (string.Equals(progress.ClaimedPeriod, progress.PeriodKey, StringComparison.Ordinal))
            {
                return "CLAIMED";
            }

            return ready ? "READY" : "LOCKED";
        }

        private static string TierTint(string tierKey)
        {
            switch (NormalizeTierKey(tierKey))
            {
                case "daily":
                    return "#66FF99";
                case "weekly":
                    return "#66CCFF";
                case "monthly":
                    return "#FFD166";
                case "seasonal":
                    return "#A78BFA";
                default:
                    return "#CCCCCC";
            }
        }

        private static string StateTint(string tierKey, string badge)
        {
            if (string.Equals(badge, "CLAIMED", StringComparison.OrdinalIgnoreCase))
            {
                return "#999999";
            }

            if (string.Equals(badge, "LOCKED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(badge, "OFF", StringComparison.OrdinalIgnoreCase))
            {
                return "#FFB347";
            }

            return TierTint(tierKey);
        }

        private string DescribeReward(RewardTier tier)
        {
            if (tier == null)
            {
                return "No reward configured";
            }

            List<string> parts = new List<string>();
            for (int i = 0; i < tier.Items.Count; i++)
            {
                parts.Add(tier.Items[i].Count + "x " + tier.Items[i].Item);
            }

            if (tier.Commands.Count > 0)
            {
                parts.Add(tier.Commands.Count + " command" + (tier.Commands.Count == 1 ? string.Empty : "s"));
            }

            if (tier.Permissions.Count > 0)
            {
                parts.Add(tier.Permissions.Count + " permission" + (tier.Permissions.Count == 1 ? string.Empty : "s"));
            }

            return parts.Count == 0 ? "No reward configured" : string.Join(", ", parts.ToArray());
        }

        private string FormatReset(string tierKey, DateTime now)
        {
            DateTime next = GetNextReset(tierKey, now);
            if (next == DateTime.MaxValue)
            {
                return "manual reset (admin controlled)";
            }

            if (next <= now)
            {
                return "next reset in a moment";
            }

            return "next reset in " + FormatCountdown(next - now);
        }

        private static string FormatCountdown(TimeSpan span)
        {
            if (span.TotalDays >= 1)
            {
                return (int)span.TotalDays + "d " + span.Hours + "h";
            }

            if (span.TotalHours >= 1)
            {
                return (int)span.TotalHours + "h " + span.Minutes + "m";
            }

            return Math.Max(0, span.Minutes) + "m";
        }

        private static string FormatDuration(double seconds)
        {
            long total = (long)Math.Round(Math.Max(0, seconds));
            long hours = total / 3600;
            long minutes = (total % 3600) / 60;
            long secs = total % 60;

            if (hours > 0)
            {
                return hours + "h " + minutes.ToString("00", CultureInfo.InvariantCulture) + "m";
            }

            if (minutes > 0)
            {
                return minutes + "m " + secs.ToString("00", CultureInfo.InvariantCulture) + "s";
            }

            return secs + "s";
        }

        #endregion

        #region Commands

        private void CommandRewards(IPlayer player, string command, string[] args)
        {
            if (player == null)
            {
                return;
            }

            string sub = args == null || args.Length == 0 ? "open" : args[0].ToLowerInvariant();

            switch (sub)
            {
                case "":
                case "open":
                case "gui":
                    OpenRewardsGui(player);
                    return;

                case "claim":
                    if (args.Length < 2)
                    {
                        Reply(player, "Usage: /rewards claim <daily|weekly|monthly|seasonal>");
                        return;
                    }

                    ClaimCommand(player, args[1]);
                    return;

                case "status":
                case "info":
                    ShowStatusCommand(player, args);
                    return;

                case "playtime":
                case "time":
                    ShowPlaytimeCommand(player, args);
                    return;

                case "grant":
                    AdminGrantCommand(player, args);
                    return;

                case "grantall":
                    AdminGrantAllCommand(player, args);
                    return;

                case "reset":
                    AdminResetCommand(player, args);
                    return;

                case "resetall":
                    AdminResetAllCommand(player, args);
                    return;

                case "season":
                    AdminSeasonCommand(player, args);
                    return;

                case "reload":
                    AdminReloadCommand(player);
                    return;

                case "help":
                    ShowHelp(player);
                    return;

                default:
                    ShowHelp(player);
                    return;
            }
        }

        private void OpenRewardsGui(IPlayer player)
        {
            if (player == null || player.IsServer)
            {
                Reply(player, "This command must be used in-game.");
                return;
            }

            if (!CanUseGui(player.Id))
            {
                Reply(player, "You do not have permission to use Daily Rewards.");
                return;
            }

            if (!TryOpenRewardsPanel(player.Id, null))
            {
                Reply(player, "The Server Assist panel is not available, so here is your status:");
                ShowStatus(player, player.Id, player.Name);
            }
        }

        private void ClaimCommand(IPlayer player, string tierArgument)
        {
            string tierKey = NormalizeTierKey(tierArgument);
            if (string.IsNullOrEmpty(tierKey))
            {
                Reply(player, "Unknown tier. Use daily, weekly, monthly or seasonal.");
                return;
            }

            string message;
            ClaimOrGrantReward(player.Id, tierKey, false, out message);
            Reply(player, message);
        }

        private void ShowStatusCommand(IPlayer player, string[] args)
        {
            string target = player.Id;
            string targetName = player.Name;

            if (args.Length > 1)
            {
                if (!IsAdmin(player))
                {
                    Reply(player, "You need the 'dailyrewards.admin' permission to inspect another player.");
                    return;
                }

                string requested = string.Join(" ", args, 1, args.Length - 1);
                string resolvedId;
                PlayerData resolved = ResolvePlayerData(requested, out resolvedId);
                if (resolved == null)
                {
                    Reply(player, "No stored data for '" + requested + "'.");
                    return;
                }

                target = resolvedId;
                targetName = string.IsNullOrEmpty(resolved.Name) ? requested : resolved.Name;
            }

            ShowStatus(player, target, targetName);
        }

        private void ShowStatus(IPlayer actor, string playerId, string playerName)
        {
            PlayerData playerData;
            if (!TryGetPlayerData(playerId, out playerData))
            {
                Reply(actor, "No stored data for " + playerName + " yet.");
                return;
            }

            DateTime now = DateTime.Now;
            NormalizeAllTiers(playerData, now);

            Reply(actor, "Daily Rewards status for " + playerName + ":");
            Reply(actor, "  Total playtime: " + FormatDuration(playerData.TotalPlaySeconds));
            Reply(actor, "  Daily streak: " + playerData.DailyStreak + " day(s)");

            for (int i = 0; i < TierKeys.Length; i++)
            {
                string tierKey = TierKeys[i];
                RewardTier tier = GetTier(tierKey);
                if (tier == null)
                {
                    continue;
                }

                bool ready;
                string status = DescribeTier(playerId, playerData, tierKey, now, out ready);
                TierProgress progress = GetProgress(playerData, tierKey);
                string play = tier.RequiredPlaytimeMinutes > 0
                    ? " | period play " + FormatDuration(progress.PeriodSeconds) + " / " +
                      FormatDuration(RequiredSeconds(tier))
                    : string.Empty;

                Reply(actor, "  " + tier.DisplayName + ": " + status + play);
            }
        }

        private void ShowPlaytimeCommand(IPlayer player, string[] args)
        {
            string targetId = player.Id;
            string targetName = player.Name;

            if (args.Length > 1)
            {
                if (!IsAdmin(player))
                {
                    Reply(player, "You need the 'dailyrewards.admin' permission to inspect another player.");
                    return;
                }

                string requested = string.Join(" ", args, 1, args.Length - 1);
                string resolvedId;
                PlayerData resolved = ResolvePlayerData(requested, out resolvedId);
                if (resolved == null)
                {
                    Reply(player, "No stored data for '" + requested + "'.");
                    return;
                }

                targetId = resolvedId;
                targetName = string.IsNullOrEmpty(resolved.Name) ? requested : resolved.Name;
            }

            PlayerData playerData;
            if (!TryGetPlayerData(targetId, out playerData))
            {
                Reply(player, "No stored data for " + targetName + " yet.");
                return;
            }

            DateTime now = DateTime.Now;
            NormalizeAllTiers(playerData, now);

            Reply(player, targetName + " playtime:");
            Reply(player, "  Total: " + FormatDuration(playerData.TotalPlaySeconds));
            for (int i = 0; i < TierKeys.Length; i++)
            {
                RewardTier tier = GetTier(TierKeys[i]);
                if (tier == null)
                {
                    continue;
                }

                TierProgress progress = GetProgress(playerData, TierKeys[i]);
                string requirement = tier.RequiredPlaytimeMinutes > 0
                    ? " / " + FormatDuration(RequiredSeconds(tier)) + " required"
                    : string.Empty;
                Reply(player, "  " + tier.DisplayName + ": " + FormatDuration(progress.PeriodSeconds) +
                               requirement + " | " + FormatReset(TierKeys[i], now));
            }
        }

        private void AdminGrantCommand(IPlayer player, string[] args)
        {
            if (!RequireAdmin(player))
            {
                return;
            }

            if (args.Length < 3)
            {
                Reply(player, "Usage: /rewards grant <player name or SteamID> <daily|weekly|monthly|seasonal>");
                return;
            }

            string tierArgument = args[args.Length - 1];
            string targetArgument = string.Join(" ", args, 1, args.Length - 2);
            string tierKey = NormalizeTierKey(tierArgument);

            if (string.IsNullOrEmpty(tierKey))
            {
                Reply(player, "Unknown tier. Use daily, weekly, monthly or seasonal.");
                return;
            }

            IPlayer target = ResolveOnlinePlayer(targetArgument);
            if (target == null)
            {
                Reply(player, "Player '" + targetArgument + "' is not online. Grant rewards require an online, spawned player.");
                return;
            }

            string message;
            bool granted = ClaimOrGrantReward(target.Id, tierKey, true, out message);
            Reply(player, message);
            if (granted)
            {
                Reply(target, message);
            }
        }

        private void AdminGrantAllCommand(IPlayer player, string[] args)
        {
            if (!RequireAdmin(player))
            {
                return;
            }

            if (args.Length < 2)
            {
                Reply(player, "Usage: /rewards grantall <daily|weekly|monthly|seasonal>");
                return;
            }

            string tierKey = NormalizeTierKey(args[1]);
            if (string.IsNullOrEmpty(tierKey))
            {
                Reply(player, "Unknown tier. Use daily, weekly, monthly or seasonal.");
                return;
            }

            int granted = 0;
            int failed = 0;
            foreach (IPlayer target in players.Connected.ToList())
            {
                string message;
                if (ClaimOrGrantReward(target.Id, tierKey, true, out message))
                {
                    granted++;
                    Reply(target, message);
                }
                else
                {
                    failed++;
                }
            }

            Reply(player, "Granted " + GetTier(tierKey).DisplayName + " reward to " + granted +
                           " player(s); " + failed + " failed or not spawned.");
        }

        private void AdminResetCommand(IPlayer player, string[] args)
        {
            if (!RequireAdmin(player))
            {
                return;
            }

            if (args.Length < 3)
            {
                Reply(player, "Usage: /rewards reset <player name or SteamID> <daily|weekly|monthly|seasonal>");
                return;
            }

            string tierArgument = args[args.Length - 1];
            string targetArgument = string.Join(" ", args, 1, args.Length - 2);
            string tierKey = NormalizeTierKey(tierArgument);

            if (string.IsNullOrEmpty(tierKey))
            {
                Reply(player, "Unknown tier. Use daily, weekly, monthly or seasonal.");
                return;
            }

            string targetId;
            PlayerData targetData = ResolvePlayerData(targetArgument, out targetId);
            if (targetData == null)
            {
                Reply(player, "No stored data for '" + targetArgument + "'.");
                return;
            }

            TierProgress progress = GetProgress(targetData, tierKey);
            progress.ClaimedPeriod = string.Empty;
            progress.LastClaimUtc = 0;
            SaveData();

            Reply(player, "Reset the " + GetTier(tierKey).DisplayName + " claim for " + targetData.Name + ".");
        }

        private void AdminResetAllCommand(IPlayer player, string[] args)
        {
            if (!RequireAdmin(player))
            {
                return;
            }

            if (args.Length < 2)
            {
                Reply(player, "Usage: /rewards resetall <daily|weekly|monthly|seasonal>");
                return;
            }

            string tierKey = NormalizeTierKey(args[1]);
            if (string.IsNullOrEmpty(tierKey))
            {
                Reply(player, "Unknown tier. Use daily, weekly, monthly or seasonal.");
                return;
            }

            int count = 0;
            foreach (PlayerData playerData in data.Players.Values)
            {
                if (playerData == null)
                {
                    continue;
                }

                TierProgress progress = GetProgress(playerData, tierKey);
                progress.ClaimedPeriod = string.Empty;
                progress.LastClaimUtc = 0;
                count++;
            }

            SaveData();
            Reply(player, "Reset the " + GetTier(tierKey).DisplayName + " claim for " + count + " player(s).");
        }

        private void AdminSeasonCommand(IPlayer player, string[] args)
        {
            if (!RequireAdmin(player))
            {
                return;
            }

            if (args.Length < 2)
            {
                Reply(player, "Season mode: " + config.SeasonMode + " | Current: " + GetSeasonName(DateTime.Now));
                Reply(player, "Usage: /rewards season <name> to start a manual season, or /rewards season auto for calendar seasons.");
                return;
            }

            string value = string.Join(" ", args, 1, args.Length - 1).Trim();
            if (string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "calendar", StringComparison.OrdinalIgnoreCase))
            {
                config.SeasonMode = "calendar";
                Reply(player, "Season mode set to calendar.");
            }
            else
            {
                string seasonName = Clean(value, 48);
                if (string.IsNullOrEmpty(seasonName))
                {
                    Reply(player, "Season name cannot be empty. Use /rewards season auto for calendar seasons.");
                    return;
                }

                config.ManualSeason = seasonName;
                config.SeasonMode = "manual";
                Reply(player, "Manual season started: " + config.ManualSeason);
            }

            SaveConfig();
            warnedItems.Clear();
            TryRegisterServerAssistPanel();
        }

        private void AdminReloadCommand(IPlayer player)
        {
            if (!RequireAdmin(player))
            {
                return;
            }

            FlushAllOnline();
            SaveData();
            LoadConfig();
            LoadData();
            RegisterConfiguredPermissions();
            EnsureDefaultPermissions();
            warnedItems.Clear();
            TryRegisterServerAssistPanel();
            Reply(player, "Reloaded DailyRewards config and data.");
        }

        private void ShowHelp(IPlayer player)
        {
            Reply(player, "Daily Rewards commands:");
            Reply(player, "  /rewards - open the Server Assist panel");
            Reply(player, "  /rewards status [player] - show reward status");
            Reply(player, "  /rewards playtime [player] - show tracked playtime");
            Reply(player, "  /rewards claim <tier> - claim a reward");
            Reply(player, "  /rewards grant <player> <tier> - admin: grant without using a claim");
            Reply(player, "  /rewards grantall <tier> - admin: grant to all online players");
            Reply(player, "  /rewards reset <player> <tier> - admin: clear a claim");
            Reply(player, "  /rewards resetall <tier> - admin: clear all claims for a tier");
            Reply(player, "  /rewards season [name|auto] - admin: inspect or change the season");
            Reply(player, "  /rewards reload - admin: reload config and data");
            Reply(player, "Tiers: daily, weekly, monthly, seasonal");
        }

        private bool RequireAdmin(IPlayer player)
        {
            if (IsAdmin(player))
            {
                return true;
            }

            Reply(player, "You need the 'dailyrewards.admin' permission.");
            return false;
        }

        private void Reply(IPlayer player, string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            if (player == null || player.IsServer)
            {
                Interface.Oxide.LogInfo("[DailyRewards] " + message);
                return;
            }

            player.Reply(string.IsNullOrEmpty(config.ChatPrefix) ? message : config.ChatPrefix + " " + message);
        }

        #endregion

        #region ServerAssist hook

        private void TryRegisterServerAssistPanel()
        {
            if (config == null)
            {
                LoadConfig();
            }

            Plugin serverAssist = plugins.Find(ServerAssistPluginName);
            if (serverAssist == null)
            {
                return;
            }

            object result = InvokePlugin(
                serverAssist,
                "RegisterPanel",
                new[]
                {
                    typeof(string), typeof(string), typeof(string), typeof(string),
                    typeof(string), typeof(string), typeof(string), typeof(int)
                },
                new object[]
                {
                    ServerAssistHookId,
                    config.HookLabel,
                    config.HookDescription,
                    config.HookPermission,
                    "DailyRewards",
                    nameof(HandleGuiRequest),
                    config.PanelIcon,
                    config.PanelOrder
                },
                false);

            bool registered = result is bool && (bool)result;

            if (!registered)
            {
                // Server Assist 1.x compatibility.
                result = InvokePlugin(
                    serverAssist,
                    "RegisterGuiHook",
                    new[]
                    {
                        typeof(string), typeof(string), typeof(string),
                        typeof(string), typeof(string), typeof(string)
                    },
                    new object[]
                    {
                        ServerAssistHookId,
                        config.HookLabel,
                        config.HookDescription,
                        config.HookPermission,
                        "DailyRewards",
                        nameof(HandleGuiRequest)
                    },
                    false);

                registered = result is bool && (bool)result;
            }

            if (registered)
            {
                InvokePlugin(
                    serverAssist,
                    "SetPanelIcon",
                    new[] { typeof(string), typeof(string) },
                    new object[] { ServerAssistHookId, config.PanelIcon },
                    false);
            }

            if (registered && !loggedHookRegistration)
            {
                loggedHookRegistration = true;
                Interface.Oxide.LogInfo("[DailyRewards] registered Server Assist panel '" + ServerAssistHookId +
                                        "' with icon " + config.PanelIcon + ".");
            }
            else if (!registered && !loggedHookRegistration)
            {
                Interface.Oxide.LogWarning("[DailyRewards] Server Assist is loaded but the Daily Rewards panel could not be registered.");
            }
        }

        private void TryUnregisterServerAssistPanel()
        {
            Plugin serverAssist = plugins.Find(ServerAssistPluginName);
            if (serverAssist == null)
            {
                return;
            }

            object result = InvokePlugin(
                serverAssist,
                "UnregisterPanel",
                new[] { typeof(string) },
                new object[] { ServerAssistHookId },
                false);

            if (!(result is bool && (bool)result))
            {
                InvokePlugin(
                    serverAssist,
                    "UnregisterGuiHook",
                    new[] { typeof(string) },
                    new object[] { ServerAssistHookId },
                    false);
            }
        }

        private bool TryOpenRewardsPanel(string playerId, string subAction)
        {
            Plugin serverAssist = plugins.Find(ServerAssistPluginName);
            if (serverAssist == null)
            {
                return false;
            }

            string action = subAction ?? string.Empty;
            object result = InvokePlugin(
                serverAssist,
                "OpenPanel",
                new[] { typeof(string), typeof(string), typeof(string) },
                new object[] { playerId, ServerAssistHookId, action },
                false);

            if (result is bool && (bool)result)
            {
                return true;
            }

            result = InvokePlugin(
                serverAssist,
                "OpenHookForPlayer",
                new[] { typeof(string), typeof(string), typeof(string) },
                new object[] { playerId, ServerAssistHookId, subAction },
                false);

            return result is bool && (bool)result;
        }

        private object InvokePlugin(Plugin plugin, string methodName, Type[] parameterTypes, object[] args)
        {
            return InvokePlugin(plugin, methodName, parameterTypes, args, true);
        }

        private object InvokePlugin(Plugin plugin, string methodName, Type[] parameterTypes, object[] args,
                                    bool logMissing)
        {
            if (plugin == null)
            {
                return null;
            }

            try
            {
                MethodInfo method = plugin.GetType().GetMethod(
                    methodName,
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    parameterTypes,
                    null);

                if (method == null)
                {
                    if (logMissing)
                    {
                        Interface.Oxide.LogWarning("[DailyRewards] " + plugin.Name + " does not expose " +
                                                    methodName + ".");
                    }

                    return null;
                }

                return method.Invoke(plugin, args);
            }
            catch (TargetInvocationException exception)
            {
                Exception inner = exception.InnerException ?? exception;
                Interface.Oxide.LogError("[DailyRewards] " + plugin.Name + "." + methodName + " failed: " + inner);
                return null;
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogError("[DailyRewards] " + plugin.Name + "." + methodName + " failed: " + exception);
                return null;
            }
        }

        public string HandleGuiRequest(string playerId, string payload)
        {
            try
            {
                if (string.IsNullOrEmpty(playerId))
                {
                    return ErrorView("Could not determine your player id.");
                }

                if (!permission.IsLoaded)
                {
                    return ErrorView("The Oxide permission system is not loaded.");
                }

                if (!CanUseGui(playerId))
                {
                    return ErrorView("You do not have permission to use Daily Rewards.");
                }

                UiSession session = GetSession(playerId);

                if (string.IsNullOrEmpty(payload) || string.Equals(payload, "open", StringComparison.OrdinalIgnoreCase))
                {
                    payload = "a|open";
                }

                if (payload.StartsWith("t|", StringComparison.Ordinal))
                {
                    return HandleText(playerId, session, Decode(payload.Substring(2)));
                }

                if (payload.StartsWith("a|", StringComparison.Ordinal))
                {
                    return HandleAction(playerId, session, payload.Substring(2));
                }

                return ErrorView("Unknown reward request.");
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogException("[DailyRewards] GUI request failed", exception);
                return ErrorView("Internal error: " + exception.Message);
            }
        }

        private UiSession GetSession(string playerId)
        {
            UiSession session;
            if (!sessions.TryGetValue(playerId, out session))
            {
                session = new UiSession();
                sessions[playerId] = session;
            }

            return session;
        }

        private string HandleAction(string playerId, UiSession session, string action)
        {
            string[] parts = action.Split('|');
            string command = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;

            switch (command)
            {
                case "open":
                    session.Screen = "overview";
                    ClearPrompt(session);
                    session.Status = string.Empty;
                    break;

                case "overview":
                case "home":
                case "back":
                    session.Screen = "overview";
                    ClearPrompt(session);
                    session.Status = string.Empty;
                    break;

                case "refresh":
                    break;

                case "tier":
                    if (parts.Length > 1)
                    {
                        string tierKey = NormalizeTierKey(parts[1]);
                        if (string.IsNullOrEmpty(tierKey))
                        {
                            session.Status = "Unknown reward tier.";
                        }
                        else
                        {
                            session.Screen = tierKey;
                            ClearPrompt(session);
                            session.Status = string.Empty;
                        }
                    }
                    break;

                case "claim":
                    if (parts.Length > 1)
                    {
                        string tierKey = NormalizeTierKey(parts[1]);
                        if (string.IsNullOrEmpty(tierKey))
                        {
                            session.Status = "Unknown reward tier.";
                        }
                        else
                        {
                            string message;
                            ClaimOrGrantReward(playerId, tierKey, false, out message);
                            session.Status = message;
                            ClearPrompt(session);
                        }
                    }
                    break;

                case "admin":
                    if (!IsAdmin(playerId))
                    {
                        session.Status = "You need the 'dailyrewards.admin' permission.";
                    }
                    else
                    {
                        session.Screen = "admin";
                        ClearPrompt(session);
                        session.Status = string.Empty;
                    }
                    break;

                case "grantprompt":
                    if (!IsAdmin(playerId))
                    {
                        session.Status = "You need the 'dailyrewards.admin' permission.";
                    }
                    else if (parts.Length > 1)
                    {
                        string tierKey = NormalizeTierKey(parts[1]);
                        RewardTier tier = GetTier(tierKey);
                        if (tier == null)
                        {
                            session.Status = "Unknown reward tier.";
                        }
                        else
                        {
                            session.Screen = "admin";
                            Prompt(session, "grant|" + tierKey,
                                "Player name or SteamID to grant " + tier.DisplayName, string.Empty);
                            session.Status = string.Empty;
                        }
                    }
                    break;

                case "resetprompt":
                    if (!IsAdmin(playerId))
                    {
                        session.Status = "You need the 'dailyrewards.admin' permission.";
                    }
                    else if (parts.Length > 1)
                    {
                        string tierKey = NormalizeTierKey(parts[1]);
                        RewardTier tier = GetTier(tierKey);
                        if (tier == null)
                        {
                            session.Status = "Unknown reward tier.";
                        }
                        else
                        {
                            session.Screen = "admin";
                            Prompt(session, "reset|" + tierKey,
                                "Player name or SteamID to reset " + tier.DisplayName, string.Empty);
                            session.Status = string.Empty;
                        }
                    }
                    break;

                default:
                    session.Status = "Unknown action: " + command;
                    break;
            }

            return Render(playerId, session);
        }

        private string HandleText(string playerId, UiSession session, string text)
        {
            if (string.IsNullOrEmpty(session.InputMode))
            {
                session.Status = "Nothing is waiting for text input.";
                return Render(playerId, session);
            }

            string[] parts = session.InputMode.Split('|');
            string mode = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
            string tierKey = parts.Length > 1 ? NormalizeTierKey(parts[1]) : string.Empty;
            ClearPrompt(session);

            if (!IsAdmin(playerId))
            {
                session.Status = "You need the 'dailyrewards.admin' permission.";
                return Render(playerId, session);
            }

            if (string.IsNullOrEmpty(tierKey))
            {
                session.Status = "Unknown reward tier.";
                return Render(playerId, session);
            }

            string targetArgument = text == null ? string.Empty : text.Trim();
            if (string.IsNullOrEmpty(targetArgument))
            {
                session.Status = "Enter a player name or SteamID.";
                return Render(playerId, session);
            }

            if (mode == "grant")
            {
                IPlayer target = ResolveOnlinePlayer(targetArgument);
                if (target == null)
                {
                    session.Status = "Player '" + targetArgument + "' is not online.";
                }
                else
                {
                    string message;
                    ClaimOrGrantReward(target.Id, tierKey, true, out message);
                    session.Status = message;
                    Reply(target, message);
                }
            }
            else if (mode == "reset")
            {
                string targetId;
                PlayerData targetData = ResolvePlayerData(targetArgument, out targetId);
                if (targetData == null)
                {
                    session.Status = "No stored data for '" + targetArgument + "'.";
                }
                else
                {
                    TierProgress progress = GetProgress(targetData, tierKey);
                    progress.ClaimedPeriod = string.Empty;
                    progress.LastClaimUtc = 0;
                    SaveData();
                    session.Status = "Reset the " + GetTier(tierKey).DisplayName + " claim for " +
                                     targetData.Name + ".";
                }
            }
            else
            {
                session.Status = "Unknown input action.";
            }

            return Render(playerId, session);
        }

        private void Prompt(UiSession session, string inputMode, string label, string value)
        {
            session.InputMode = inputMode;
            session.InputLabel = label ?? string.Empty;
            session.InputValue = value ?? string.Empty;
        }

        private void ClearPrompt(UiSession session)
        {
            session.InputMode = string.Empty;
            session.InputLabel = string.Empty;
            session.InputValue = string.Empty;
        }

        #endregion

        #region GUI rendering

        private sealed class RowInfo
        {
            public string Action = string.Empty;
            public string Label = string.Empty;
            public string Sub = string.Empty;
            public bool Enabled = true;
            public string Icon = string.Empty;
            public string Badge = string.Empty;
            public string Tint = string.Empty;
        }

        private sealed class ButtonInfo
        {
            public string Action = string.Empty;
            public string Label = string.Empty;
            public bool Enabled = true;
            public string Tooltip = string.Empty;
        }

        private string Render(string playerId, UiSession session)
        {
            List<RowInfo> rows = new List<RowInfo>();
            List<ButtonInfo> buttons = new List<ButtonInfo>();
            string subtitle;

            if (string.Equals(session.Screen, "admin", StringComparison.OrdinalIgnoreCase) && IsAdmin(playerId))
            {
                RenderAdmin(playerId, rows, buttons, out subtitle);
            }
            else if (string.Equals(session.Screen, "overview", StringComparison.OrdinalIgnoreCase))
            {
                RenderOverview(playerId, rows, buttons, out subtitle);
            }
            else
            {
                string tierKey = NormalizeTierKey(session.Screen);
                if (string.IsNullOrEmpty(tierKey))
                {
                    session.Screen = "overview";
                    RenderOverview(playerId, rows, buttons, out subtitle);
                }
                else
                {
                    RenderTier(playerId, tierKey, rows, buttons, out subtitle);
                }
            }

            StringBuilder builder = new StringBuilder(4096);
            builder.Append("v2\n");
            builder.Append("protocol=2\n");
            builder.Append("title=").Append(Encode(PluginTitle)).Append('\n');
            builder.Append("subtitle=").Append(Encode(subtitle)).Append('\n');
            builder.Append("status=").Append(Encode(session.Status)).Append('\n');
            builder.Append("background=").Append(Encode(config.PanelIcon)).Append('\n');
            builder.Append("banner=").Append(Encode(config.PanelIcon)).Append('\n');
            builder.Append("input=")
                .Append(Encode(string.IsNullOrEmpty(session.InputMode) ? string.Empty : session.InputLabel))
                .Append('|')
                .Append(string.IsNullOrEmpty(session.InputMode) ? "0" : "1")
                .Append('|')
                .Append(Encode(session.InputValue))
                .Append('\n');

            for (int i = 0; i < rows.Count && i < UiRows; i++)
            {
                RowInfo row = rows[i];
                builder.Append("row=").Append(i).Append('|')
                    .Append(Encode(row.Action)).Append('|')
                    .Append(Encode(row.Label)).Append('|')
                    .Append(Encode(row.Sub)).Append('|')
                    .Append(row.Enabled ? "1" : "0").Append("|0|")
                    .Append(Encode(row.Icon)).Append('|')
                    .Append(Encode(row.Badge)).Append('|')
                    .Append(Encode(row.Tint)).Append('\n');
            }

            for (int i = 0; i < buttons.Count && i < UiButtons; i++)
            {
                ButtonInfo button = buttons[i];
                builder.Append("btn=").Append(i).Append('|')
                    .Append(Encode(button.Action)).Append('|')
                    .Append(Encode(button.Label)).Append('|')
                    .Append(button.Enabled ? "1" : "0").Append('|')
                    .Append(Encode(button.Tooltip)).Append('\n');
            }

            return builder.ToString();
        }

        private void RenderOverview(string playerId, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            PlayerData playerData = GetOrCreatePlayerData(playerId, GetPlayerName(playerId));
            DateTime now = DateTime.Now;
            NormalizeAllTiers(playerData, now);

            for (int i = 0; i < TierKeys.Length; i++)
            {
                string tierKey = TierKeys[i];
                RewardTier tier = GetTier(tierKey);
                if (tier == null)
                {
                    continue;
                }

                bool ready;
                string status = DescribeTier(playerId, playerData, tierKey, now, out ready);
                string badge = GetTierBadge(playerId, playerData, tierKey, now, ready);
                rows.Add(new RowInfo
                {
                    Action = "tier|" + tierKey,
                    Label = tier.DisplayName + " Reward",
                    Sub = status + " | " + DescribeReward(tier),
                    Enabled = true,
                    Icon = GetTierIcon(tierKey, tier),
                    Badge = badge,
                    Tint = StateTint(tierKey, badge)
                });
            }

            TierProgress dailyProgress = GetProgress(playerData, "daily");
            double dailyRequired = RequiredSeconds(config.Daily);
            rows.Add(new RowInfo
            {
                Label = "Playtime",
                Sub = "Total " + FormatDuration(playerData.TotalPlaySeconds) + " | Daily " +
                      FormatDuration(dailyProgress.PeriodSeconds) +
                      (dailyRequired > 0 ? " / " + FormatDuration(dailyRequired) : string.Empty),
                Enabled = false,
                Icon = IconPlaytime,
                Badge = "TIME",
                Tint = "#66CCFF"
            });

            rows.Add(new RowInfo
            {
                Label = "Daily streak",
                Sub = playerData.DailyStreak + " day(s) claimed in a row",
                Enabled = false,
                Icon = IconStreak,
                Badge = "STREAK",
                Tint = "#FFD166"
            });

            if (IsAdmin(playerId))
            {
                rows.Add(new RowInfo
                {
                    Action = "admin",
                    Label = "Admin Tools",
                    Sub = "Grant rewards without using claims, or reset a player's current claim.",
                    Enabled = true,
                    Icon = IconAdmin,
                    Badge = "ADMIN",
                    Tint = "#FFD166"
                });
            }

            for (int i = 0; i < TierKeys.Length; i++)
            {
                RewardTier tier = GetTier(TierKeys[i]);
                if (tier == null)
                {
                    continue;
                }

                buttons.Add(new ButtonInfo
                {
                    Action = "claim|" + TierKeys[i],
                    Label = "Claim " + tier.DisplayName,
                    Enabled = true
                });
            }

            buttons.Add(new ButtonInfo
            {
                Action = "refresh",
                Label = "Refresh"
            });

            if (IsAdmin(playerId))
            {
                buttons.Add(new ButtonInfo
                {
                    Action = "admin",
                    Label = "Admin Tools"
                });
            }

            subtitle = "Playtime rewards | daily requires " +
                       (config.Daily != null && config.Daily.RequiredPlaytimeMinutes > 0
                           ? config.Daily.RequiredPlaytimeMinutes + " minutes"
                           : "no playtime") +
                       " this period";
        }

        private void RenderTier(string playerId, string tierKey, List<RowInfo> rows,
                                List<ButtonInfo> buttons, out string subtitle)
        {
            RewardTier tier = GetTier(tierKey);
            if (tier == null)
            {
                rows.Add(new RowInfo
                {
                    Label = "Unknown tier",
                    Sub = "Return to the overview.",
                    Enabled = false,
                    Icon = IconWarning,
                    Badge = "ERROR",
                    Tint = "#FF6B6B"
                });
                buttons.Add(new ButtonInfo { Action = "overview", Label = "Back" });
                subtitle = "Unknown reward tier";
                return;
            }

            PlayerData playerData = GetOrCreatePlayerData(playerId, GetPlayerName(playerId));
            DateTime now = DateTime.Now;
            NormalizeAllTiers(playerData, now);

            bool ready;
            string status = DescribeTier(playerId, playerData, tierKey, now, out ready);
            string badge = GetTierBadge(playerId, playerData, tierKey, now, ready);
            TierProgress progress = GetProgress(playerData, tierKey);

            rows.Add(new RowInfo
            {
                Label = "Status",
                Sub = status,
                Enabled = false,
                Icon = GetTierIcon(tierKey, tier),
                Badge = badge,
                Tint = StateTint(tierKey, badge)
            });
            rows.Add(new RowInfo
            {
                Label = "Playtime requirement",
                Sub = tier.RequiredPlaytimeMinutes <= 0
                    ? "None"
                    : FormatDuration(progress.PeriodSeconds) + " / " + FormatDuration(RequiredSeconds(tier)) +
                      " this period",
                Enabled = false,
                Icon = IconPlaytime,
                Badge = "TIME",
                Tint = "#66CCFF"
            });
            rows.Add(new RowInfo
            {
                Label = "Next reset",
                Sub = FormatReset(tierKey, now),
                Enabled = false,
                Icon = IconReset,
                Badge = "RESET",
                Tint = "#FFB347"
            });

            if (!string.IsNullOrEmpty(tier.Permission))
            {
                rows.Add(new RowInfo
                {
                    Label = "Required permission",
                    Sub = tier.Permission,
                    Enabled = false,
                    Icon = IconPermissions,
                    Badge = "PERM",
                    Tint = "#66CCFF"
                });
            }

            for (int i = 0; i < tier.Items.Count && rows.Count < 9; i++)
            {
                RewardItem item = tier.Items[i];
                rows.Add(new RowInfo
                {
                    Label = "Reward",
                    Sub = item.Count + "x " + item.Item +
                          (item.Quality > 0 ? " (quality " + item.Quality + ")" : string.Empty),
                    Enabled = false,
                    Icon = GetItemIcon(item.Item),
                    Badge = "ITEM",
                    Tint = "#FFD166"
                });
            }

            if (rows.Count < UiRows)
            {
                List<string> extras = new List<string>();
                if (tier.Commands.Count > 0)
                {
                    extras.Add(tier.Commands.Count + " server command(s)");
                }

                if (tier.Permissions.Count > 0)
                {
                    extras.Add(string.Join(", ", tier.Permissions.ToArray()));
                }

                if (extras.Count > 0)
                {
                    rows.Add(new RowInfo
                    {
                        Label = "Extras",
                        Sub = string.Join(" | ", extras.ToArray()),
                        Enabled = false,
                        Icon = tier.Commands.Count > 0 ? IconCommands : IconPermissions,
                        Badge = "EXTRA",
                        Tint = "#CCCCCC"
                    });
                }
            }

            buttons.Add(new ButtonInfo
            {
                Action = "claim|" + tierKey,
                Label = "Claim " + tier.DisplayName,
                Enabled = true
            });
            buttons.Add(new ButtonInfo { Action = "refresh", Label = "Refresh" });
            buttons.Add(new ButtonInfo { Action = "overview", Label = "Back" });

            subtitle = tier.DisplayName + " reward | " + DescribeReward(tier);
        }

        private void RenderAdmin(string playerId, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            int online = players.Connected.Count();

            rows.Add(new RowInfo
            {
                Label = "Grant a reward",
                Sub = "Gives a reward without using the target's claim. The target must be online and spawned.",
                Enabled = false,
                Icon = IconGrant,
                Badge = "GIVE",
                Tint = "#66FF99"
            });

            rows.Add(new RowInfo
            {
                Label = "Reset a claim",
                Sub = "Clears the current period claim for one stored player.",
                Enabled = false,
                Icon = IconReset,
                Badge = "RESET",
                Tint = "#FFB347"
            });

            rows.Add(new RowInfo
            {
                Label = "Season",
                Sub = "Mode: " + config.SeasonMode + " | Current: " + GetSeasonName(DateTime.Now),
                Enabled = false,
                Icon = IconSeasonSettings,
                Badge = "SEASON",
                Tint = "#A78BFA"
            });

            rows.Add(new RowInfo
            {
                Label = "Online players",
                Sub = online + " connected",
                Enabled = false,
                Icon = IconPlayers,
                Badge = "ONLINE",
                Tint = "#66CCFF"
            });

            for (int i = 0; i < TierKeys.Length; i++)
            {
                RewardTier tier = GetTier(TierKeys[i]);
                if (tier == null)
                {
                    continue;
                }

                buttons.Add(new ButtonInfo
                {
                    Action = "grantprompt|" + TierKeys[i],
                    Label = "Grant " + tier.DisplayName,
                    Tooltip = "Grant without using the target's claim."
                });
            }

            for (int i = 0; i < TierKeys.Length; i++)
            {
                RewardTier tier = GetTier(TierKeys[i]);
                if (tier == null)
                {
                    continue;
                }

                buttons.Add(new ButtonInfo
                {
                    Action = "resetprompt|" + TierKeys[i],
                    Label = "Reset " + tier.DisplayName,
                    Tooltip = "Clear the target's current claim."
                });
            }

            buttons.Add(new ButtonInfo { Action = "overview", Label = "Back" });
            buttons.Add(new ButtonInfo { Action = "refresh", Label = "Refresh" });

            subtitle = "Admin tools | " + online + " player(s) online";
        }

        private string ErrorView(string message)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("v2\n");
            builder.Append("protocol=2\n");
            builder.Append("title=").Append(Encode(PluginTitle)).Append('\n');
            builder.Append("subtitle=").Append(Encode("Rewards unavailable")).Append('\n');
            builder.Append("status=").Append(Encode(message)).Append('\n');
            builder.Append("background=").Append(Encode(config.PanelIcon)).Append('\n');
            builder.Append("banner=").Append(Encode(config.PanelIcon)).Append('\n');
            builder.Append("input=||0|\n");
            builder.Append("row=0||").Append(Encode("No data")).Append('|').Append(Encode(message))
                .Append("|0|0|").Append(Encode(IconWarning)).Append("|ERROR|#FF6B6B\n");
            builder.Append("btn=0|refresh|").Append(Encode("Retry")).Append("|1|\n");
            return builder.ToString();
        }

        private static string Encode(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '%':
                        builder.Append("%25");
                        break;
                    case '|':
                        builder.Append("%7C");
                        break;
                    case '\n':
                        builder.Append("%0A");
                        break;
                    case '\r':
                        break;
                    default:
                        builder.Append(c);
                        break;
                }
            }

            return builder.ToString();
        }

        private static string Decode(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("%0A", "\n").Replace("%7C", "|").Replace("%25", "%");
        }

        #endregion

        #region Utility

        private void SaveDataSoon()
        {
            if (DateTime.UtcNow >= nextSaveUtc)
            {
                SaveData();
            }
        }

        private static long UnixNow()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
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

        private static string Clean(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            value = value.Replace("\r", " ").Replace("\n", " ").Trim();
            if (value.Length > maxLength)
            {
                value = value.Substring(0, maxLength);
            }

            return value;
        }

        #endregion

        #region Data classes

        private sealed class UiSession
        {
            public string Screen = "overview";
            public string Status = string.Empty;
            public string InputMode = string.Empty;
            public string InputLabel = string.Empty;
            public string InputValue = string.Empty;
        }

        private sealed class ActiveSession
        {
            public DateTime LastAccruedUtc = DateTime.UtcNow;
        }

        private sealed class StoredData
        {
            public int Version = 1;
            public Dictionary<string, PlayerData> Players =
                new Dictionary<string, PlayerData>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class PlayerData
        {
            public string Name = string.Empty;
            public double TotalPlaySeconds;
            public long FirstSeenUtc;
            public long LastSeenUtc;
            public int DailyStreak;
            public string LastDailyClaimPeriod = string.Empty;
            public string LastNotifiedDailyPeriod = string.Empty;
            public Dictionary<string, TierProgress> Tiers =
                new Dictionary<string, TierProgress>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class TierProgress
        {
            public string PeriodKey = string.Empty;
            public double PeriodSeconds;
            public string ClaimedPeriod = string.Empty;
            public long LastClaimUtc;
            public int ClaimCount;
        }

        private sealed class RewardItem
        {
            public string Item = string.Empty;
            public int Count = 1;
            public int Quality;
        }

        private sealed class RewardTier
        {
            public bool Enabled = true;
            public string DisplayName = string.Empty;
            public string Icon = string.Empty;
            public int RequiredPlaytimeMinutes;
            public string Permission = string.Empty;
            public List<RewardItem> Items;
            public List<string> Commands;
            public List<string> Permissions;
        }

        private sealed class Configuration
        {
            public string ChatPrefix = "[Rewards]";
            public bool GrantUseToDefaultGroup = true;
            public string HookLabel = "Daily Rewards";
            public string HookDescription = "Claim daily, weekly, monthly and seasonal rewards.";
            public string HookPermission = PermUse;
            public string PanelIcon = DefaultPanelIcon;
            public int PanelOrder = 30;
            public bool NotifyWhenReady = true;
            public int SaveIntervalSeconds = 300;
            public int MaxTickAccrualSeconds = 300;
            public int DailyResetHour;
            public string WeeklyResetDay = "Monday";
            public bool SouthernHemisphere = false;
            public string SeasonMode = "calendar";
            public string ManualSeason = "Season 1";

            public RewardTier Daily;
            public RewardTier Weekly;
            public RewardTier Monthly;
            public RewardTier Seasonal;
        }

        #endregion

        #region Public API

        [HookMethod("GrantReward")]
        public bool GrantReward(string playerId, string tierKey)
        {
            string message;
            return ClaimOrGrantReward(playerId, tierKey, true, out message);
        }

        [HookMethod("ClaimReward")]
        public bool ClaimReward(string playerId, string tierKey)
        {
            string message;
            return ClaimOrGrantReward(playerId, tierKey, false, out message);
        }

        [HookMethod("ResetReward")]
        public bool ResetReward(string playerId, string tierKey)
        {
            tierKey = NormalizeTierKey(tierKey);
            if (string.IsNullOrEmpty(tierKey))
            {
                return false;
            }

            PlayerData playerData;
            if (!TryGetPlayerData(playerId, out playerData))
            {
                return false;
            }

            TierProgress progress = GetProgress(playerData, tierKey);
            progress.ClaimedPeriod = string.Empty;
            progress.LastClaimUtc = 0;
            SaveData();
            return true;
        }

        [HookMethod("GetPlaytimeSeconds")]
        public double GetPlaytimeSeconds(string playerId)
        {
            PlayerData playerData;
            return TryGetPlayerData(playerId, out playerData) ? playerData.TotalPlaySeconds : 0.0;
        }

        [HookMethod("GetPeriodPlaytimeSeconds")]
        public double GetPeriodPlaytimeSeconds(string playerId, string tierKey)
        {
            tierKey = NormalizeTierKey(tierKey);
            if (string.IsNullOrEmpty(tierKey))
            {
                return 0.0;
            }

            PlayerData playerData;
            if (!TryGetPlayerData(playerId, out playerData))
            {
                return 0.0;
            }

            DateTime now = DateTime.Now;
            NormalizeProgress(playerData, tierKey, now);
            return GetProgress(playerData, tierKey).PeriodSeconds;
        }

        [HookMethod("GetRewardStatus")]
        public string GetRewardStatus(string playerId, string tierKey)
        {
            tierKey = NormalizeTierKey(tierKey);
            if (string.IsNullOrEmpty(tierKey))
            {
                return "unknown tier";
            }

            PlayerData playerData;
            if (!TryGetPlayerData(playerId, out playerData))
            {
                return "no data";
            }

            bool ready;
            return DescribeTier(playerId, playerData, tierKey, DateTime.Now, out ready);
        }

        #endregion
    }
}

