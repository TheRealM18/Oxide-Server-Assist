using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("SentinelAC", "Korvarix", "1.0.2")]
    [Description("Statistical server-side anti-cheat, anti-exploit and anti-grief for 7 Days to Die. No kernel or client code required.")]
    public class SentinelAC : CovalencePlugin
    {
        #region Constants

        private const string PermAdmin = "sentinelac.admin";
        private const string PermAlerts = "sentinelac.alerts";
        private const string PermExempt = "sentinelac.exempt";
        private const string PermUse = "sentinelac.use";
        private const string ServerAssistHookId = "sentinelac";
        private const string DataFileName = "SentinelAC/profiles";

        // Existing ServerAssistArt sprites are used until the dedicated
        // sa_ac_* set is rendered. See ServerAssistArt/REQUESTS_SentinelAC.md.
        private const string IconPanel = "sprite:sa_nav_moderation";
        private const string IconRisk = "sprite:sa_act_warn";
        private const string IconEvidence = "sprite:sa_nav_logs";
        private const string IconModules = "sprite:sa_nav_server";
        private const string IconPlayer = "sprite:sa_nav_players";
        private const string IconWatch = "sprite:sa_act_spectate";
        private const string IconClear = "sprite:sa_nav_logs";
        private const string IconExport = "sprite:sa_nav_snapshots";
        private const string IconKick = "sprite:sa_act_kick";
        private const string IconBan = "sprite:sa_act_ban";
        private const string IconRefresh = "sprite:sa_plugin_hot_reload";
        private const string IconPrev = "sprite:sa_act_prev";
        private const string IconNext = "sprite:sa_act_next";
        private const string IconClose = "";
        private const string IconHome = "sprite:sa_nav_server";
        private const string IconMovement = "sprite:sa_nav_teleport";
        private const string IconCombat = "sprite:sa_nav_moderation";
        private const string IconInventory = "sprite:sa_nav_items";
        private const string IconGrief = "sprite:sa_nav_world";
        private const string IconExploit = "sprite:sa_act_godmode";
        private const string IconProgression = "sprite:sa_nav_snapshots";

        private const string ModuleMovement = "movement";
        private const string ModuleCombat = "combat";
        private const string ModuleInventory = "inventory";
        private const string ModuleGrief = "grief";
        private const string ModuleExploit = "exploit";
        private const string ModuleProgression = "progression";

        #endregion

        #region Fields

        private Configuration config;
        private Timer tickTimer;
        private Timer saveTimer;
        private bool serverReady;
        private DateTime lastSaveUtc = DateTime.MinValue;
        private string lastRuntimeError = String.Empty;

        private readonly Dictionary<string, PlayerState> states =
            new Dictionary<string, PlayerState>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, RunningStat> globalStats =
            new Dictionary<string, RunningStat>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, GuiSession> guiSessions =
            new Dictionary<string, GuiSession>(StringComparer.OrdinalIgnoreCase);

        private StoredData storedData;
        private bool serverAssistProviderMode;
        private bool serverAssistRegistered;
        private bool serverAssistWarningLogged;

        #endregion

        #region Lifecycle

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            permission.RegisterPermission(PermAlerts, this);
            permission.RegisterPermission(PermExempt, this);
            permission.RegisterPermission(PermUse, this);

            LoadConfig();
            LoadData();

            if (!string.IsNullOrEmpty(config.ExemptPermission))
            {
                permission.RegisterPermission(config.ExemptPermission, this);
            }

            AddCovalenceCommand(new[] { "ac", "sentinel", "sentinelac" }, nameof(CommandSentinel));

            timer.Once(1f, TryStart);
            timer.Once(5f, RegisterServerAssist);
        }

        private void OnServerInitialized(bool serverInitialized)
        {
            serverReady = true;
            EnsureDefaultPermissions();
            RegisterServerAssist();
            StartTimers();
        }

        private void EnsureDefaultPermissions()
        {
            try
            {
                string adminGroup = Interface.Oxide.Config.Options.DefaultGroups.Administrators;
                if (string.IsNullOrEmpty(adminGroup) || !permission.GroupExists(adminGroup))
                {
                    return;
                }

                if (!permission.GroupHasPermission(adminGroup, PermAdmin))
                {
                    permission.GrantGroupPermission(adminGroup, PermAdmin, this);
                }

                if (!permission.GroupHasPermission(adminGroup, PermAlerts))
                {
                    permission.GrantGroupPermission(adminGroup, PermAlerts, this);
                }
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[SentinelAC] could not grant default admin permissions: " + exception.Message);
            }
        }

        private void OnServerAssistReady()
        {
            RegisterServerAssist();
        }

        private void TryStart()
        {
            if (!serverReady)
            {
                serverReady = true;
            }

            EnsureDefaultPermissions();
            StartTimers();
            RegisterServerAssist();
        }

        private void StartTimers()
        {
            if (tickTimer != null)
            {
                tickTimer.Destroy();
                tickTimer = null;
            }

            if (saveTimer != null)
            {
                saveTimer.Destroy();
                saveTimer = null;
            }

            double interval = config == null ? 1.0 : Clamp(config.ScanIntervalSeconds, 0.5, 10.0);
            tickTimer = timer.Repeat((float)interval, -1, Tick);
            saveTimer = timer.Repeat(300f, -1, SaveData);
        }

        private void Unload()
        {
            SaveData();

            if (tickTimer != null)
            {
                tickTimer.Destroy();
                tickTimer = null;
            }

            if (saveTimer != null)
            {
                saveTimer.Destroy();
                saveTimer = null;
            }

            UnregisterServerAssist();
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
                Interface.Oxide.LogWarning("[SentinelAC] config could not be read: " + exception.Message);
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

        private void NormalizeConfig()
        {
            if (config.Movement == null) config.Movement = new MovementSettings();
            if (config.Combat == null) config.Combat = new CombatSettings();
            if (config.Inventory == null) config.Inventory = new InventorySettings();
            if (config.Grief == null) config.Grief = new GriefSettings();
            if (config.Exploit == null) config.Exploit = new ExploitSettings();
            if (config.Progression == null) config.Progression = new ProgressionSettings();
            if (config.Webhook == null) config.Webhook = new WebhookSettings();
            if (config.ServerAssist == null) config.ServerAssist = new ServerAssistSettings();

            config.ScanIntervalSeconds = Clamp(config.ScanIntervalSeconds, 0.5, 10.0);
            config.MinSamples = Math.Max(5, config.MinSamples);
            config.MinPlayMinutes = Clamp(config.MinPlayMinutes, 0.0, 1440.0);
            config.RiskDecayHalfLifeMinutes = Clamp(config.RiskDecayHalfLifeMinutes, 1.0, 10080.0);
            config.Sensitivity = Clamp(config.Sensitivity, 0.1, 5.0);
            config.MaxContributionNormal = Clamp(config.MaxContributionNormal, 1.0, 100.0);
            config.MaxContributionCritical = Clamp(config.MaxContributionCritical, 1.0, 100.0);
            config.CriticalScoreThreshold = Clamp(config.CriticalScoreThreshold, 50.0, 100.0);
            config.AlertThreshold = Clamp(config.AlertThreshold, 1.0, 100.0);
            config.WatchThreshold = Clamp(config.WatchThreshold, 1.0, 100.0);
            config.KickThreshold = Clamp(config.KickThreshold, 1.0, 100.0);
            config.BanThreshold = Clamp(config.BanThreshold, 1.0, 100.0);
            config.ActionSustainSeconds = Clamp(config.ActionSustainSeconds, 1.0, 3600.0);
            config.ActionCooldownMinutes = Clamp(config.ActionCooldownMinutes, 1.0, 10080.0);
            config.AlertCooldownSeconds = Clamp(config.AlertCooldownSeconds, 1.0, 3600.0);
            config.EvidenceRetentionMinutes = Clamp(config.EvidenceRetentionMinutes, 5.0, 10080.0);
            config.EvidenceCooldownSeconds = Clamp(config.EvidenceCooldownSeconds, 1.0, 600.0);
            config.DefaultBanHours = Clamp(config.DefaultBanHours, 1.0, 8760.0);

            config.Movement.SampleSeconds = Clamp(config.Movement.SampleSeconds, 0.25, 30.0);
            config.Movement.ZThreshold = Clamp(config.Movement.ZThreshold, 2.0, 20.0);
            config.Movement.HardSpeed = Clamp(config.Movement.HardSpeed, 5.0, 100.0);
            config.Movement.MaxUpwardSpeed = Clamp(config.Movement.MaxUpwardSpeed, 1.0, 100.0);
            config.Movement.MaxAirSeconds = Clamp(config.Movement.MaxAirSeconds, 1.0, 120.0);
            config.Movement.MaxTeleportDistance = Clamp(config.Movement.MaxTeleportDistance, 10.0, 1000.0);
            config.Movement.MaxTeleportsPerWindow = Math.Max(1, config.Movement.MaxTeleportsPerWindow);
            config.Movement.TeleportWindowMinutes = Clamp(config.Movement.TeleportWindowMinutes, 1.0, 1440.0);
            config.Movement.SpawnGraceSeconds = Clamp(config.Movement.SpawnGraceSeconds, 0.0, 120.0);
            config.Movement.NoClipSamples = Math.Max(1, config.Movement.NoClipSamples);
            config.Movement.MinWorldY = Clamp(config.Movement.MinWorldY, -1000.0, 50.0);

            config.Combat.MaxMeleeReach = Clamp(config.Combat.MaxMeleeReach, 1.0, 20.0);
            config.Combat.MaxHitsPerSecond = Clamp(config.Combat.MaxHitsPerSecond, 1.0, 100.0);
            config.Combat.HeadshotRatioThreshold = Clamp(config.Combat.HeadshotRatioThreshold, 0.1, 1.0);
            config.Combat.MinHeadshots = Math.Max(5, config.Combat.MinHeadshots);
            config.Combat.ThroughWallDistance = Clamp(config.Combat.ThroughWallDistance, 1.0, 50.0);
            config.Combat.KillRateZThreshold = Clamp(config.Combat.KillRateZThreshold, 2.0, 20.0);
            config.Combat.GlobalZThreshold = Clamp(config.Combat.GlobalZThreshold, 2.0, 30.0);
            config.Combat.MinKillsForRate = Math.Max(3, config.Combat.MinKillsForRate);

            config.Inventory.SampleSeconds = Clamp(config.Inventory.SampleSeconds, 0.5, 120.0);
            config.Inventory.MaxLegalQuality = Math.Max(1, config.Inventory.MaxLegalQuality);
            config.Inventory.MaxHighValueGainPerMinute = Clamp(config.Inventory.MaxHighValueGainPerMinute, 1.0, 1000.0);
            config.Inventory.WealthZThreshold = Clamp(config.Inventory.WealthZThreshold, 2.0, 20.0);
            config.Inventory.GlobalZThreshold = Clamp(config.Inventory.GlobalZThreshold, 2.0, 30.0);
            config.Inventory.MaxIdenticalSignatures = Math.Max(2, config.Inventory.MaxIdenticalSignatures);

            config.Grief.SampleSeconds = Clamp(config.Grief.SampleSeconds, 0.5, 60.0);
            config.Grief.Radius = Clamp(config.Grief.Radius, 1, 10);
            config.Grief.VerticalRadius = Clamp(config.Grief.VerticalRadius, 1, 10);
            config.Grief.AttributionDistance = Clamp(config.Grief.AttributionDistance, 2.0, 20.0);
            config.Grief.MaxChangesPerMinute = Clamp(config.Grief.MaxChangesPerMinute, 10.0, 10000.0);
            config.Grief.MaxChangesPerSample = Math.Max(3, config.Grief.MaxChangesPerSample);
            config.Grief.ExplosionChangeThreshold = Math.Max(3, config.Grief.ExplosionChangeThreshold);

            config.Exploit.SampleSeconds = Clamp(config.Exploit.SampleSeconds, 0.5, 60.0);
            config.Exploit.StatOverflowFactor = Clamp(config.Exploit.StatOverflowFactor, 1.0, 5.0);
            config.Exploit.MaxVehicleSpeed = Clamp(config.Exploit.MaxVehicleSpeed, 10.0, 200.0);
            config.Exploit.DamageImmunityHits = Math.Max(3, config.Exploit.DamageImmunityHits);

            config.Progression.MaxXpPerMinute = Clamp(config.Progression.MaxXpPerMinute, 100.0, 1000000.0);
            config.Progression.XpZThreshold = Clamp(config.Progression.XpZThreshold, 2.0, 30.0);
            config.Progression.GlobalZThreshold = Clamp(config.Progression.GlobalZThreshold, 2.0, 30.0);
            config.Progression.MinXpPerMinute = Clamp(config.Progression.MinXpPerMinute, 0.0, 1000000.0);

            config.Webhook.MinIntervalSeconds = Clamp(config.Webhook.MinIntervalSeconds, 1.0, 3600.0);
            config.Webhook.MinRisk = Clamp(config.Webhook.MinRisk, 1.0, 100.0);
            config.ServerAssist.Order = Math.Max(0, config.ServerAssist.Order);
            if (string.IsNullOrEmpty(config.ServerAssist.PanelId))
            {
                config.ServerAssist.PanelId = ServerAssistHookId;
            }

            // Migrate the pre-menu default to the ServerAssistArt placeholder.
            if (string.Equals(config.ServerAssist.Icon, "sprite:ui_game_symbol_hammer",
                    StringComparison.OrdinalIgnoreCase))
            {
                config.ServerAssist.Icon = IconPanel;
            }
        }

        private void LoadData()
        {
            try
            {
                storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(DataFileName);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[SentinelAC] data could not be read: " + exception.Message);
                storedData = null;
            }

            if (storedData == null)
            {
                storedData = new StoredData();
            }

            if (storedData.Players == null)
            {
                storedData.Players = new Dictionary<string, StoredProfile>(StringComparer.OrdinalIgnoreCase);
            }

            if (storedData.GlobalStats == null)
            {
                storedData.GlobalStats = new Dictionary<string, RunningStat>(StringComparer.OrdinalIgnoreCase);
            }

            globalStats.Clear();
            foreach (KeyValuePair<string, RunningStat> pair in storedData.GlobalStats)
            {
                if (!string.IsNullOrEmpty(pair.Key) && pair.Value != null)
                {
                    globalStats[pair.Key] = pair.Value;
                }
            }
        }

        private void SaveData()
        {
            try
            {
                if (storedData == null)
                {
                    storedData = new StoredData();
                }

                if (storedData.Players == null)
                {
                    storedData.Players = new Dictionary<string, StoredProfile>(StringComparer.OrdinalIgnoreCase);
                }

                foreach (PlayerState player in states.Values)
                {
                    if (player == null || string.IsNullOrEmpty(player.Id))
                    {
                        continue;
                    }

                    StoredProfile profile;
                    if (!storedData.Players.TryGetValue(player.Id, out profile) || profile == null)
                    {
                        profile = new StoredProfile();
                        storedData.Players[player.Id] = profile;
                    }

                    profile.Name = player.Name ?? String.Empty;
                    profile.LastSeenUtc = player.LastSeenUtc;
                    profile.PeakRisk = Math.Max(profile.PeakRisk, player.PeakRisk);
                    profile.Flags = Math.Max(profile.Flags, player.Flags);

                    if (profile.Stats == null)
                    {
                        profile.Stats = new Dictionary<string, RunningStat>(StringComparer.OrdinalIgnoreCase);
                    }

                    profile.Stats["speed"] = player.SpeedStat;
                    profile.Stats["vertical"] = player.VerticalStat;
                    profile.Stats["killrate"] = player.KillRateStat;
                    profile.Stats["xprate"] = player.XpRateStat;
                    profile.Stats["wealth"] = player.WealthStat;
                }

                storedData.GlobalStats = new Dictionary<string, RunningStat>(globalStats, StringComparer.OrdinalIgnoreCase);
                Interface.Oxide.DataFileSystem.WriteObject(DataFileName, storedData, true);
                lastSaveUtc = DateTime.UtcNow;
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[SentinelAC] data could not be saved: " + exception.Message);
            }
        }

        #endregion

        #region Hooks

        private void OnPlayerConnected(ClientInfo clientInfo)
        {
            try
            {
                if (clientInfo == null || config == null)
                {
                    return;
                }

                PlayerState player = GetOrCreateState(clientInfo);
                if (player == null)
                {
                    return;
                }

                DateTime now = DateTime.UtcNow;
                player.Name = clientInfo.playerName ?? player.Name;
                player.EntityId = clientInfo.entityId;
                player.Online = true;
                player.JoinUtc = now;
                player.LastSeenUtc = now;
                player.SpawnGraceUntilUtc = now.AddSeconds(config.Movement.SpawnGraceSeconds);
                player.SessionRisk = 0.0;
                player.HighRiskSinceUtc = DateTime.MinValue;
                player.Exempt = IsExempt(player, clientInfo);
                player.PositionHistory.Clear();
                player.BlockSnapshot.Clear();
                player.InventoryItems.Clear();
                player.XpWindow.Clear();
                player.KillWindow.Clear();
                player.DamageWindow.Clear();
                player.DamageTakenWindow.Clear();
                player.GriefWindow.Clear();
                player.VehicleSpawnWindow.Clear();
                player.LastEvidenceAt.Clear();
                player.LastMovementSampleUtc = DateTime.MinValue;
                player.LastProgressionSampleUtc = DateTime.MinValue;
                player.LastInventorySampleUtc = DateTime.MinValue;
                player.LastGriefSampleUtc = DateTime.MinValue;
                player.LastExploitSampleUtc = DateTime.MinValue;
                player.LastLevel = 0;
                player.ConsecutiveAir = 0;
                player.ConsecutiveHighSpeed = 0;
                player.SolidTicks = 0;
                player.TeleportCount = 0;
                player.TeleportWindowStartUtc = DateTime.MinValue;
                player.Falling = false;
                player.LastHealth = 0f;
                player.LastWeapon = String.Empty;
                player.LastHitUtc = DateTime.MinValue;
                player.LastVictimId = -1;
            }
            catch (Exception exception)
            {
                LogHookError("OnPlayerConnected", exception);
            }
        }

        private void OnPlayerSpawned(ClientInfo clientInfo)
        {
            try
            {
                if (clientInfo == null || config == null)
                {
                    return;
                }

                PlayerState player = GetOrCreateState(clientInfo);
                if (player == null)
                {
                    return;
                }

                player.EntityId = clientInfo.entityId;
                player.Online = true;
                player.LastSeenUtc = DateTime.UtcNow;
                player.SpawnGraceUntilUtc = DateTime.UtcNow.AddSeconds(config.Movement.SpawnGraceSeconds);
                player.PositionHistory.Clear();
                player.InventoryItems.Clear();
                player.LastWealth = 0.0;
                player.LastWealthChangeUtc = DateTime.MinValue;
                player.Falling = false;
                player.ConsecutiveAir = 0;
                player.SolidTicks = 0;
                player.LastLevel = 0;
            }
            catch (Exception exception)
            {
                LogHookError("OnPlayerSpawned", exception);
            }
        }

        private void OnPlayerDisconnected(ClientInfo clientInfo)
        {
            try
            {
                if (clientInfo == null)
                {
                    return;
                }

                string id = GetPlayerId(clientInfo);
                PlayerState player;
                if (string.IsNullOrEmpty(id) || !states.TryGetValue(id, out player) || player == null)
                {
                    return;
                }

                MarkPlayerOffline(player);
                SaveData();
            }
            catch (Exception exception)
            {
                LogHookError("OnPlayerDisconnected", exception);
            }
        }

        private void OnEntityTakeDamage(EntityAlive victim, DamageSource damageSource)
        {
            try
            {
                if (victim == null || damageSource == null || config == null)
                {
                    return;
                }

                HandleDamage(victim, damageSource);
            }
            catch (Exception exception)
            {
                LogHookError("OnEntityTakeDamage", exception);
            }
        }

        private void OnEntityDeath(EntityAlive victim, DamageResponse damageResponse)
        {
            try
            {
                if (victim == null || config == null)
                {
                    return;
                }

                HandleDeath(victim, damageResponse.Source);
            }
            catch (Exception exception)
            {
                LogHookError("OnEntityDeath", exception);
            }
        }

        private void OnEntitySpawned(Entity entity)
        {
            try
            {
                if (entity == null || config == null)
                {
                    return;
                }

                HandleEntitySpawned(entity);
            }
            catch (Exception exception)
            {
                LogHookError("OnEntitySpawned", exception);
            }
        }

        private void OnExperienceGained(EntityAlive entity, int exp)
        {
            try
            {
                EntityPlayer entityPlayer = entity as EntityPlayer;
                if (entityPlayer == null || exp <= 0)
                {
                    return;
                }

                PlayerState player = GetPlayerState(entityPlayer);
                if (player == null)
                {
                    return;
                }

                player.XpWindow.Add(exp, DateTime.UtcNow);
            }
            catch (Exception exception)
            {
                LogHookError("OnExperienceGained", exception);
            }
        }

        private void OnAirdrop(object sender, Vector3 position)
        {
            if (config != null && config.Debug)
            {
                Interface.Oxide.LogInfo("[SentinelAC] Airdrop at " + position.ToString());
            }
        }

        private void OnServerSave()
        {
            if ((DateTime.UtcNow - lastSaveUtc).TotalSeconds >= 30.0)
            {
                SaveData();
            }
        }

        private void OnServerShutdown()
        {
            SaveData();
        }

        #endregion

        #region Tick

        private void Tick()
        {
            if (!serverReady || config == null)
            {
                return;
            }

            try
            {
                World world = GetWorld();
                if (world == null)
                {
                    return;
                }

                DateTime now = DateTime.UtcNow;
                List<EntityPlayer> players = world.Players != null ? world.Players.list : null;
                if (players == null || players.Count == 0)
                {
                    MarkMissingPlayersOffline(null);
                    DecayRisk(now);
                    return;
                }

                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (int i = players.Count - 1; i >= 0; i--)
                {
                    EntityPlayer entity = players[i];
                    if (entity == null)
                    {
                        continue;
                    }

                    PlayerState player = GetPlayerState(entity);
                    if (player == null)
                    {
                        continue;
                    }

                    try
                    {
                        if (player.LastTickUtc != DateTime.MinValue)
                        {
                            double delta = (now - player.LastTickUtc).TotalSeconds;
                            if (delta > 0.0 && delta < 10.0)
                            {
                                player.PlaySeconds += delta;
                            }
                        }

                        player.LastTickUtc = now;
                        player.LastSeenUtc = now;
                        player.Online = true;
                        player.EntityId = entity.entityId;
                        player.Samples++;
                        if (!string.IsNullOrEmpty(player.Id))
                        {
                            seen.Add(player.Id);
                        }
                        player.Exempt = IsExempt(player, GetClientInfo(entity));

                        SampleMovement(player, entity, world, now);
                        SampleExploit(player, entity, world, now);
                        SampleProgression(player, entity, world, now);
                        SampleInventory(player, entity, world, now);
                        SampleGrief(player, entity, world, now);
                    }
                    catch (Exception exception)
                    {
                        LogRuntimeError("tick " + player.Name, exception);
                    }
                }

                MarkMissingPlayersOffline(seen);
                DecayRisk(now);
                EvaluateActions(now);
            }
            catch (Exception exception)
            {
                LogRuntimeError("tick", exception);
            }
        }

        private void MarkMissingPlayersOffline(HashSet<string> seen)
        {
            foreach (PlayerState player in states.Values)
            {
                if (player == null || !player.Online)
                {
                    continue;
                }

                if (seen == null || !seen.Contains(player.Id))
                {
                    MarkPlayerOffline(player);
                }
            }
        }

        private void MarkPlayerOffline(PlayerState player)
        {
            if (player == null)
            {
                return;
            }

            player.Online = false;
            player.EntityId = -1;
            player.LastSeenUtc = DateTime.UtcNow;
            guiSessions.Remove(player.Id);
            player.PositionHistory.Clear();
            player.BlockSnapshot.Clear();
            player.InventoryItems.Clear();
            player.Deaths.Clear();
            player.XpWindow.Clear();
            player.KillWindow.Clear();
            player.DamageWindow.Clear();
            player.DamageTakenWindow.Clear();
            player.GriefWindow.Clear();
            player.VehicleSpawnWindow.Clear();
            player.LastEvidenceAt.Clear();

            if (player.Evidence.Count > 50)
            {
                player.Evidence.RemoveRange(0, player.Evidence.Count - 50);
            }
        }

        #endregion

        #region Detection: movement

        private void SampleMovement(PlayerState player, EntityPlayer entity, World world, DateTime now)
        {
            if (!config.Movement.Enabled)
            {
                return;
            }

            if ((now - player.LastMovementSampleUtc).TotalSeconds < config.Movement.SampleSeconds)
            {
                return;
            }

            player.LastMovementSampleUtc = now;

            if (entity.IsDead())
            {
                player.PositionHistory.Clear();
                return;
            }

            Vector3 position = entity.position;
            if (player.PositionHistory.Count == 0)
            {
                player.PositionHistory.Add(new PositionSample
                {
                    Utc = now,
                    Position = position,
                    OnGround = entity.onGround,
                    Health = entity.Stats != null ? entity.Stats.Health.Value : 0f
                });
                return;
            }

            PositionSample previous = player.PositionHistory[player.PositionHistory.Count - 1];
            double seconds = (now - previous.Utc).TotalSeconds;
            if (seconds < 0.2)
            {
                return;
            }

            float dx = position.x - previous.Position.x;
            float dy = position.y - previous.Position.y;
            float dz = position.z - previous.Position.z;
            double horizontalSpeed = Math.Sqrt(dx * dx + dz * dz) / seconds;
            double verticalSpeed = dy / seconds;

            player.PositionHistory.Add(new PositionSample
            {
                Utc = now,
                Position = position,
                OnGround = entity.onGround,
                Health = entity.Stats != null ? entity.Stats.Health.Value : 0f
            });

            if (player.PositionHistory.Count > 32)
            {
                player.PositionHistory.RemoveAt(0);
            }

            bool inVehicle = entity.AttachedToEntity != null;
            bool jetpack = entity.bJetpackActive || entity.bJetpackWearing;
            bool climbing = entity.bClimbing;
            bool spectator = entity.IsSpectator || entity.isSpectator;
            bool trader = world.IsWithinTraderArea(entity.GetBlockPosition());
            bool grace = now < player.SpawnGraceUntilUtc;

            if (inVehicle && !grace && config.Exploit.Enabled && config.Exploit.DetectVehicleSpeed &&
                horizontalSpeed > config.Exploit.MaxVehicleSpeed)
            {
                AddEvidence(player, ModuleExploit, "EXP_VEHICLE_SPEED_POS",
                    65.0 + Math.Min(20.0, horizontalSpeed - config.Exploit.MaxVehicleSpeed),
                    0.7,
                    "Vehicle moved at " + horizontalSpeed.ToString("0.0", CultureInfo.InvariantCulture) + " m/s");
            }

            if (inVehicle || jetpack || climbing || spectator || trader)
            {
                player.ConsecutiveAir = 0;
                player.ConsecutiveHighSpeed = 0;
                player.SolidTicks = 0;
                player.Falling = false;
                return;
            }

            CheckNoClip(player, entity, world, now, grace);

            if (!grace && position.y < config.Movement.MinWorldY)
            {
                AddEvidence(player, ModuleMovement, "MOV_UNDERMAP", 75.0, 0.8,
                    "Player is below the world at y=" + position.y.ToString("0.0", CultureInfo.InvariantCulture));
            }

            if (horizontalSpeed > config.Movement.MaxTeleportDistance)
            {
                HandleTeleport(player, horizontalSpeed, now);
                player.Falling = false;
                player.ConsecutiveAir = 0;
                return;
            }

            double speedZ = player.SpeedStat.Z(horizontalSpeed);
            double speedEwmaZ = player.SpeedStat.EwmaZ(horizontalSpeed);

            if (player.Samples >= config.MinSamples)
            {
                if (horizontalSpeed > config.Movement.HardSpeed && !grace)
                {
                    player.ConsecutiveHighSpeed++;
                    if (player.ConsecutiveHighSpeed >= 2)
                    {
                        AddEvidence(player, ModuleMovement, "MOV_SPEED_HARD",
                            80.0 + Math.Min(20.0, (horizontalSpeed - config.Movement.HardSpeed) * 2.0),
                            0.92,
                            "Speed " + horizontalSpeed.ToString("0.0", CultureInfo.InvariantCulture) +
                            " m/s (hard limit " + config.Movement.HardSpeed.ToString("0.0", CultureInfo.InvariantCulture) + ")");
                    }
                }
                else
                {
                    player.ConsecutiveHighSpeed = 0;
                }

                if (!grace && horizontalSpeed > config.Movement.HardSpeed * 0.55 &&
                    (speedZ > config.Movement.ZThreshold || speedEwmaZ > config.Movement.ZThreshold))
                {
                    double z = Math.Max(speedZ, speedEwmaZ);
                    AddEvidence(player, ModuleMovement, "MOV_SPEED_STAT",
                        Math.Min(80.0, 25.0 + (z - config.Movement.ZThreshold) * 10.0),
                        Math.Min(0.9, 0.45 + (z - config.Movement.ZThreshold) * 0.08),
                        "Speed " + horizontalSpeed.ToString("0.0", CultureInfo.InvariantCulture) +
                        " m/s, z=" + z.ToString("0.0", CultureInfo.InvariantCulture));
                }
            }

            player.SpeedStat.Add(horizontalSpeed);
            player.VerticalStat.Add(verticalSpeed);
            GlobalStat("speed").Add(horizontalSpeed);

            if (!grace && verticalSpeed > config.Movement.MaxUpwardSpeed)
            {
                AddEvidence(player, ModuleMovement, "MOV_UPWARD",
                    55.0 + Math.Min(35.0, (verticalSpeed - config.Movement.MaxUpwardSpeed) * 5.0),
                    0.75,
                    "Upward speed " + verticalSpeed.ToString("0.0", CultureInfo.InvariantCulture) + " m/s");
            }

            if (!entity.onGround)
            {
                player.ConsecutiveAir++;
                double airSeconds = player.ConsecutiveAir * config.Movement.SampleSeconds;
                if (!grace && airSeconds > config.Movement.MaxAirSeconds &&
                    Math.Abs(verticalSpeed) < 1.5)
                {
                    AddEvidence(player, ModuleMovement, "MOV_HOVER",
                        Math.Min(80.0, 35.0 + airSeconds * 2.0),
                        0.65,
                        "Airborne " + airSeconds.ToString("0.0", CultureInfo.InvariantCulture) +
                        "s with vertical speed " + verticalSpeed.ToString("0.0", CultureInfo.InvariantCulture));
                }
            }
            else
            {
                player.ConsecutiveAir = 0;
            }

            if (config.Movement.DetectFallBypass)
            {
                if (!entity.onGround && verticalSpeed < -1.0)
                {
                    if (!player.Falling)
                    {
                        player.Falling = true;
                        player.FallStartY = previous.Position.y;
                    }
                }
                else if (entity.onGround && player.Falling)
                {
                    player.Falling = false;
                    double fallDistance = player.FallStartY - position.y;
                    if (fallDistance > config.Movement.MinFallDistance)
                    {
                        float healthNow = entity.Stats != null ? entity.Stats.Health.Value : 0f;
                        float healthBefore = previous.Health;
                        double loss = healthBefore - healthNow;
                        if (loss < config.Movement.MinFallDamageLoss)
                        {
                            AddEvidence(player, ModuleMovement, "MOV_FALL_BYPASS",
                                55.0, 0.45,
                                "Fell " + fallDistance.ToString("0.0", CultureInfo.InvariantCulture) +
                                "m with health change " + loss.ToString("0.0", CultureInfo.InvariantCulture));
                        }
                    }
                }
            }
        }

        private void CheckNoClip(PlayerState player, EntityPlayer entity, World world, DateTime now, bool grace)
        {
            if (!config.Movement.DetectNoClip || grace)
            {
                player.SolidTicks = 0;
                return;
            }

            bool feetSolid = IsSolidBlock(world.GetBlock(entity.GetBlockPosition()));
            bool headSolid = IsSolidBlock(world.GetBlock(World.worldToBlockPos(entity.getHeadPosition())));
            if (feetSolid && headSolid)
            {
                player.SolidTicks++;
                if (player.SolidTicks >= config.Movement.NoClipSamples)
                {
                    AddEvidence(player, ModuleMovement, "MOV_NOCLIP", 55.0, 0.5,
                        "Feet and head are inside collidable blocks at " + entity.GetBlockPosition().ToString());
                    player.SolidTicks = 0;
                }
            }
            else
            {
                player.SolidTicks = 0;
            }
        }

        private static bool IsSolidBlock(BlockValue value)
        {
            if (value.isair || value.Block == null)
            {
                return false;
            }

            Block block = value.Block;
            if (block.bIsPlant || block.blockMaterial == null || !block.blockMaterial.IsCollidable ||
                block.blockMaterial.IsGroundCover)
            {
                return false;
            }

            string name = block.blockName ?? String.Empty;
            if (name.IndexOf("door", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("hatch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("gate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("elevator", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("ladder", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("stairs", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return true;
        }

        private void HandleTeleport(PlayerState player, double distance, DateTime now)
        {
            if (now < player.SpawnGraceUntilUtc)
            {
                return;
            }

            if (player.TeleportWindowStartUtc == DateTime.MinValue ||
                (now - player.TeleportWindowStartUtc).TotalMinutes > config.Movement.TeleportWindowMinutes)
            {
                player.TeleportWindowStartUtc = now;
                player.TeleportCount = 0;
            }

            player.TeleportCount++;
            if (player.TeleportCount > config.Movement.MaxTeleportsPerWindow)
            {
                AddEvidence(player, ModuleMovement, "MOV_TELEPORT_CHAIN",
                    Math.Min(70.0, 30.0 + player.TeleportCount * 5.0),
                    0.6,
                    "Teleport jump " + distance.ToString("0", CultureInfo.InvariantCulture) +
                    "m (" + player.TeleportCount + " in window)");
            }
        }

        #endregion

        #region Detection: exploit

        private void SampleExploit(PlayerState player, EntityPlayer entity, World world, DateTime now)
        {
            if (!config.Exploit.Enabled)
            {
                return;
            }

            if ((now - player.LastExploitSampleUtc).TotalSeconds < config.Exploit.SampleSeconds)
            {
                return;
            }

            player.LastExploitSampleUtc = now;

            if (config.Exploit.DetectGodMode && entity.IsGodMode.Value && !player.Exempt)
            {
                AddEvidence(player, ModuleExploit, "EXP_GODMODE", 100.0, 0.95,
                    "God mode is enabled on a non-exempt player");
            }

            if (config.Exploit.DetectStatOverflow && entity.Stats != null)
            {
                CheckStat(player, "health", entity.Stats.Health);
                CheckStat(player, "stamina", entity.Stats.Stamina);
                CheckStat(player, "water", entity.Stats.Water);
                CheckStat(player, "food", entity.Stats.Food);
            }

            if (config.Exploit.DetectJetpack && entity.bJetpackActive && !entity.bJetpackWearing)
            {
                AddEvidence(player, ModuleExploit, "EXP_JETPACK", 90.0, 0.9,
                    "Jetpack active without a worn jetpack");
            }

            if (config.Exploit.DetectVehicleSpeed && entity.AttachedToEntity != null)
            {
                EntityVehicle vehicle = entity.AttachedToEntity as EntityVehicle;
                if (vehicle != null)
                {
                    float speed = vehicle.GetVelocityPerSecond().magnitude;
                    if (speed > config.Exploit.MaxVehicleSpeed)
                    {
                        AddEvidence(player, ModuleExploit, "EXP_VEHICLE_SPEED",
                            60.0 + Math.Min(30.0, speed - config.Exploit.MaxVehicleSpeed),
                            0.7,
                            "Vehicle speed " + speed.ToString("0.0", CultureInfo.InvariantCulture) + " m/s");
                    }
                }
            }

            float health = entity.Stats != null ? entity.Stats.Health.Value : 0f;
            int damageEvents = player.DamageTakenWindow.Count(now);
            if (config.Exploit.DetectDamageImmunity && player.LastHealth > 0f &&
                damageEvents >= config.Exploit.DamageImmunityHits &&
                health >= player.LastHealth - 0.5f)
            {
                AddEvidence(player, ModuleExploit, "EXP_DAMAGE_IMMUNITY",
                    Math.Min(75.0, 35.0 + damageEvents * 2.0),
                    0.55,
                    "Took " + damageEvents + " damage events with no health loss");
            }

            player.LastHealth = health;
            player.LastHealthUtc = now;
        }

        private void CheckStat(PlayerState player, string name, Stat stat)
        {
            if (stat == null)
            {
                return;
            }

            double max = stat.ModifiedMax;
            if (max <= 0.0)
            {
                max = stat.Max;
            }

            if (max > 0.0 && stat.Value > max * config.Exploit.StatOverflowFactor + 5.0)
            {
                AddEvidence(player, ModuleExploit, "EXP_STAT_OVERFLOW",
                    80.0, 0.8,
                    "Stat " + name + "=" + stat.Value.ToString("0.0", CultureInfo.InvariantCulture) +
                    " above modified max " + max.ToString("0.0", CultureInfo.InvariantCulture));
            }
        }

        #endregion

        #region Detection: progression

        private void SampleProgression(PlayerState player, EntityPlayer entity, World world, DateTime now)
        {
            if (!config.Progression.Enabled)
            {
                return;
            }

            if ((now - player.LastProgressionSampleUtc).TotalSeconds < 10.0)
            {
                return;
            }

            player.LastProgressionSampleUtc = now;

            if (entity.Progression != null)
            {
                int level = entity.Progression.Level;
                if (player.LastLevel <= 0)
                {
                    player.LastLevel = level;
                }
                else if (level > player.LastLevel + 1)
                {
                    AddEvidence(player, ModuleProgression, "PRG_LEVEL_JUMP",
                        Math.Min(70.0, 30.0 + (level - player.LastLevel) * 8.0),
                        0.65,
                        "Level jumped from " + player.LastLevel + " to " + level);
                }

                player.LastLevel = level;
            }

            double xp = player.XpWindow.Sum(now);
            double perMinute = xp / 5.0;

            if (perMinute > 0.0 && player.PlaySeconds >= 120.0)
            {
                if (perMinute > config.Progression.MaxXpPerMinute)
                {
                    AddEvidence(player, ModuleProgression, "PRG_XP_HARD",
                        Math.Min(85.0, 40.0 + (perMinute / config.Progression.MaxXpPerMinute) * 10.0),
                        0.85,
                        "XP " + perMinute.ToString("0", CultureInfo.InvariantCulture) + "/min");
                }

                if (player.XpRateStat.Count >= 5 && perMinute >= config.Progression.MinXpPerMinute)
                {
                    double z = player.XpRateStat.Z(perMinute);
                    if (z > config.Progression.XpZThreshold)
                    {
                        AddEvidence(player, ModuleProgression, "PRG_XP_STAT",
                            Math.Min(75.0, 25.0 + (z - config.Progression.XpZThreshold) * 8.0),
                            Math.Min(0.85, 0.4 + (z - config.Progression.XpZThreshold) * 0.08),
                            "XP " + perMinute.ToString("0", CultureInfo.InvariantCulture) +
                            "/min, z=" + z.ToString("0.0", CultureInfo.InvariantCulture));
                    }
                }

                double xpGlobalZ = GlobalZ("xprate", perMinute);
                if (xpGlobalZ > config.Progression.GlobalZThreshold && perMinute >= config.Progression.MinXpPerMinute)
                {
                    AddEvidence(player, ModuleProgression, "PRG_XP_GLOBAL",
                        Math.Min(65.0, 30.0 + (xpGlobalZ - config.Progression.GlobalZThreshold) * 6.0),
                        0.5,
                        "XP " + perMinute.ToString("0", CultureInfo.InvariantCulture) +
                        "/min, server z=" + xpGlobalZ.ToString("0.0", CultureInfo.InvariantCulture));
                }

                player.XpRateStat.Add(perMinute);
                GlobalStat("xprate").Add(perMinute);
            }

            double killsPerMinute = player.KillWindow.Sum(now);
            if (player.KillWindow.Count(now) > 0)
            {
                double killGlobalZ = GlobalZ("killrate", killsPerMinute);
                if (killGlobalZ > config.Combat.GlobalZThreshold && killsPerMinute >= config.Combat.MinKillsForRate)
                {
                    AddEvidence(player, ModuleCombat, "CMB_KILL_GLOBAL",
                        Math.Min(65.0, 30.0 + (killGlobalZ - config.Combat.GlobalZThreshold) * 6.0),
                        0.5,
                        "Player kill rate " + killsPerMinute.ToString("0.0", CultureInfo.InvariantCulture) +
                        "/min, server z=" + killGlobalZ.ToString("0.0", CultureInfo.InvariantCulture));
                }

                player.KillRateStat.Add(killsPerMinute);
                GlobalStat("killrate").Add(killsPerMinute);
            }
        }

        #endregion

        #region Detection: inventory

        private void SampleInventory(PlayerState player, EntityPlayer entity, World world, DateTime now)
        {
            if (!config.Inventory.Enabled)
            {
                return;
            }

            if ((now - player.LastInventorySampleUtc).TotalSeconds < config.Inventory.SampleSeconds)
            {
                return;
            }

            player.LastInventorySampleUtc = now;

            Dictionary<int, InventoryItem> current = new Dictionary<int, InventoryItem>();
            Dictionary<string, int> signatures = new Dictionary<string, int>(StringComparer.Ordinal);
            double wealth = 0.0;
            int totalItems = 0;

            ScanGrid(player, entity.inventory != null ? entity.inventory.ItemGrid : null, current, signatures, ref wealth, ref totalItems);
            ScanGrid(player, entity.bag != null ? entity.bag.ItemGrid : null, current, signatures, ref wealth, ref totalItems);
            ScanGrid(player, entity.equipment != null ? entity.equipment.ItemGrid : null, current, signatures, ref wealth, ref totalItems);

            if (player.InventoryItems.Count == 0)
            {
                player.InventoryItems = current;
                player.LastWealth = wealth;
                player.LastWealthChangeUtc = now;
                player.WealthStat.Add(wealth);
                GlobalStat("wealth").Add(wealth);
                return;
            }

            double gainedWealth = 0.0;
            int highValueGained = 0;

            foreach (KeyValuePair<int, InventoryItem> pair in current)
            {
                InventoryItem nowItem = pair.Value;
                InventoryItem before;
                int beforeCount = player.InventoryItems.TryGetValue(pair.Key, out before) && before != null ? before.Count : 0;
                int delta = nowItem.Count - beforeCount;
                if (delta <= 0)
                {
                    continue;
                }

                ItemClass itemClass = ItemClass.GetForId(pair.Key);
                if (itemClass == null)
                {
                    continue;
                }

                gainedWealth += delta * nowItem.ValueFactor;
                if (itemClass.HasQuality && nowItem.Quality >= 4)
                {
                    highValueGained += delta;
                }
            }

            double sampleMinutes = Math.Max(0.02, (now - player.LastWealthChangeUtc).TotalMinutes);
            double highValueRate = highValueGained / sampleMinutes;
            if (highValueRate > config.Inventory.MaxHighValueGainPerMinute)
            {
                AddEvidence(player, ModuleInventory, "ECO_HIGH_VALUE_GAIN",
                    Math.Min(75.0, 35.0 + highValueRate * 2.0),
                    0.7,
                    "Gained " + highValueGained + " high-quality items (" +
                    highValueRate.ToString("0.0", CultureInfo.InvariantCulture) + "/min)");
            }

            double wealthDelta = wealth - player.LastWealth;
            double minutes = Math.Max(0.02, (now - player.LastWealthChangeUtc).TotalMinutes);
            double wealthRate = wealthDelta / minutes;

            if (wealthDelta > 0.0 && player.WealthStat.Count >= 5)
            {
                double z = player.WealthStat.Z(wealth);
                if (z > config.Inventory.WealthZThreshold)
                {
                    AddEvidence(player, ModuleInventory, "ECO_WEALTH_STAT",
                        Math.Min(70.0, 25.0 + (z - config.Inventory.WealthZThreshold) * 8.0),
                        Math.Min(0.8, 0.35 + (z - config.Inventory.WealthZThreshold) * 0.08),
                        "Wealth " + wealth.ToString("0", CultureInfo.InvariantCulture) +
                        ", z=" + z.ToString("0.0", CultureInfo.InvariantCulture));
                }
            }

            if (wealthRate > 0.0 && player.Samples >= config.MinSamples)
            {
                double globalZ = GlobalZ("wealthrate", wealthRate);
                if (globalZ > config.Inventory.GlobalZThreshold && wealthRate > 100.0)
                {
                    AddEvidence(player, ModuleInventory, "ECO_WEALTH_GLOBAL",
                        Math.Min(65.0, 30.0 + (globalZ - config.Inventory.GlobalZThreshold) * 6.0),
                        0.5,
                        "Wealth rate " + wealthRate.ToString("0", CultureInfo.InvariantCulture) +
                        "/min, server z=" + globalZ.ToString("0.0", CultureInfo.InvariantCulture));
                }

                GlobalStat("wealthrate").Add(wealthRate);
            }

            foreach (KeyValuePair<string, int> pair in signatures)
            {
                if (pair.Value > config.Inventory.MaxIdenticalSignatures)
                {
                    AddEvidence(player, ModuleInventory, "ECO_DUPLICATE_SIGNATURE",
                        45.0, 0.45,
                        "Item signature " + pair.Key + " appears " + pair.Value + " times");
                }
            }

            player.InventoryItems = current;
            player.LastWealth = wealth;
            player.LastWealthChangeUtc = now;
            player.WealthStat.Add(wealth);
            GlobalStat("wealth").Add(wealth);
        }

        private void ScanGrid(PlayerState player, ItemStackGrid grid, Dictionary<int, InventoryItem> current,
            Dictionary<string, int> signatures, ref double wealth, ref int totalItems)
        {
            if (grid == null || grid.items == null)
            {
                return;
            }

            for (int i = 0; i < grid.Length; i++)
            {
                ItemStack stack = grid[i];
                if (stack == null || stack.IsEmpty())
                {
                    continue;
                }

                ItemValue value = stack.itemValue;
                if (value == null)
                {
                    continue;
                }

                int count = stack.count;
                if (count <= 0)
                {
                    continue;
                }

                int type = value.type;
                ItemClass itemClass = ItemClass.GetForId(type);
                if (itemClass == null)
                {
                    if (config.Inventory.DetectInvalidItems)
                    {
                        AddEvidence(player, ModuleInventory, "EXP_INVALID_ITEM", 90.0, 0.95,
                            "Inventory contains an unknown item id " + type);
                    }
                    continue;
                }

                totalItems += count;

                if (config.Inventory.DetectStackOverflow && itemClass.MaxCount > 0 && count > itemClass.MaxCount)
                {
                    AddEvidence(player, ModuleInventory, "EXP_STACK_OVERFLOW", 95.0, 0.98,
                        "Stack of " + (itemClass.GetItemName() ?? type.ToString()) + " has " + count +
                        " items (max " + itemClass.MaxCount + ")");
                }

                int maxQuality = Math.Max(config.Inventory.MaxLegalQuality, ItemClass.MaxQualityTier);
                if (config.Inventory.DetectQualityOverflow && itemClass.HasQuality &&
                    value.Quality > maxQuality)
                {
                    AddEvidence(player, ModuleInventory, "EXP_QUALITY_OVERFLOW", 95.0, 0.98,
                        (itemClass.GetItemName() ?? type.ToString()) + " quality " + value.Quality +
                        " above legal max " + maxQuality);
                }

                InventoryItem item;
                if (!current.TryGetValue(type, out item) || item == null)
                {
                    item = new InventoryItem
                    {
                        Type = type,
                        Name = itemClass.GetItemName() ?? type.ToString(),
                        Quality = value.Quality
                    };
                    current[type] = item;
                }

                item.Count += count;
                if (value.Quality > item.Quality)
                {
                    item.Quality = value.Quality;
                }

                double factor = 1.0 + Math.Min(20.0, Math.Max(0.0, itemClass.EconomicValue) / 100.0);
                factor *= 1.0 + Math.Min(10.0, value.Quality);
                wealth += count * factor;

                if (value.Quality > 0 || value.Meta != 0 || value.Seed != 0)
                {
                    string signature = type.ToString(CultureInfo.InvariantCulture) + "|" +
                                       value.Quality.ToString(CultureInfo.InvariantCulture) + "|" +
                                       value.Meta.ToString(CultureInfo.InvariantCulture) + "|" +
                                       value.Seed.ToString(CultureInfo.InvariantCulture);
                    int signatureCount;
                    signatures.TryGetValue(signature, out signatureCount);
                    signatures[signature] = signatureCount + 1;
                }
            }
        }

        #endregion

        #region Detection: grief

        private void SampleGrief(PlayerState player, EntityPlayer entity, World world, DateTime now)
        {
            if (!config.Grief.Enabled)
            {
                return;
            }

            if ((now - player.LastGriefSampleUtc).TotalSeconds < config.Grief.SampleSeconds)
            {
                return;
            }

            player.LastGriefSampleUtc = now;

            if (entity.IsDead())
            {
                player.BlockSnapshot.Clear();
                return;
            }

            Vector3i center = entity.GetBlockPosition();
            int radius = Math.Max(1, config.Grief.Radius);
            int vertical = Math.Max(1, config.Grief.VerticalRadius);
            Dictionary<long, BlockSnapshot> current = new Dictionary<long, BlockSnapshot>((radius * 2 + 1) * (radius * 2 + 1) * (vertical * 2 + 1));
            int changesThisSample = 0;

            for (int x = -radius; x <= radius; x++)
            {
                for (int z = -radius; z <= radius; z++)
                {
                    for (int y = -vertical; y <= vertical; y++)
                    {
                        Vector3i position = new Vector3i(center.x + x, center.y + y, center.z + z);
                        BlockValue block = world.GetBlock(position);
                        long key = PackPosition(position);
                        BlockSnapshot snapshot = new BlockSnapshot
                        {
                            Type = (ushort)block.type,
                            Damage = block.damage,
                            IsAir = block.isair
                        };
                        current[key] = snapshot;

                        BlockSnapshot previous;
                        if (!player.BlockSnapshot.TryGetValue(key, out previous))
                        {
                            continue;
                        }

                        if (previous.Type == snapshot.Type && previous.Damage == snapshot.Damage)
                        {
                            continue;
                        }

                        if (previous.IsAir == snapshot.IsAir && previous.Type == snapshot.Type)
                        {
                            // Damage-only changes are normal while mining and are not grief.
                            continue;
                        }

                        changesThisSample++;
                        HandleBlockChange(player, entity, world, position, previous, block, now);
                    }
                }
            }

            player.BlockSnapshot = current;

            if (changesThisSample > 0)
            {
                player.GriefWindow.Add(changesThisSample, now);
                double perMinute = player.GriefWindow.Sum(now) / 5.0;
                if (perMinute > config.Grief.MaxChangesPerMinute)
                {
                    AddEvidence(player, ModuleGrief, "GRF_BLOCK_RATE",
                        Math.Min(75.0, 30.0 + (perMinute / config.Grief.MaxChangesPerMinute) * 10.0),
                        0.65,
                        "Block changes " + perMinute.ToString("0", CultureInfo.InvariantCulture) + "/min");
                }

                if (changesThisSample >= config.Grief.MaxChangesPerSample)
                {
                    AddEvidence(player, ModuleGrief, "GRF_CLUSTER",
                        Math.Min(80.0, 35.0 + changesThisSample),
                        0.7,
                        changesThisSample + " blocks changed in one sample");
                }

                if (changesThisSample >= config.Grief.ExplosionChangeThreshold)
                {
                    AddEvidence(player, ModuleGrief, "GRF_EXPLOSION",
                        Math.Min(85.0, 45.0 + changesThisSample * 0.5),
                        0.65,
                        changesThisSample + " blocks changed in one sample (explosion-like)");
                }
            }
        }

        private void HandleBlockChange(PlayerState player, EntityPlayer entity, World world, Vector3i position,
            BlockSnapshot previous, BlockValue current, DateTime now)
        {
            float distance = Vector3.Distance(entity.position, new Vector3(position.x + 0.5f, position.y + 0.5f, position.z + 0.5f));
            if (distance > config.Grief.AttributionDistance)
            {
                return;
            }

            if (config.Grief.RequireLineOfSight &&
                LineBlocked(entity.getChestPosition(), new Vector3(position.x + 0.5f, position.y + 0.5f, position.z + 0.5f)))
            {
                return;
            }

            if (HasCloserPlayer(entity, position))
            {
                return;
            }

            bool removed = !previous.IsAir && current.isair;
            bool placed = previous.IsAir && !current.isair;
            bool replaced = !previous.IsAir && !current.isair && previous.Type != (ushort)current.type;

            if (!removed && !placed && !replaced)
            {
                return;
            }

            PersistentPlayerData playerData = entity.PersistentPlayerData;
            EnumLandClaimOwner claim = playerData != null
                ? world.GetLandClaimOwner(position, playerData)
                : EnumLandClaimOwner.None;

            if (config.Grief.ProtectLandClaimBlocks && removed && IsLandClaimBlock(previous) &&
                claim == EnumLandClaimOwner.Other)
            {
                AddEvidence(player, ModuleGrief, "GRF_LANDCLAIM", 95.0, 0.95,
                    "Removed another player's land claim block at " + position.ToString());
                return;
            }

            if (config.Grief.ProtectClaims && claim == EnumLandClaimOwner.Other)
            {
                AddEvidence(player, ModuleGrief, "GRF_CLAIM",
                    removed ? 90.0 : 80.0, 0.9,
                    "Changed a block in another player's claim at " + position.ToString());
            }

            if (config.Grief.FlagTraderArea && world.IsWithinTraderArea(position))
            {
                AddEvidence(player, ModuleGrief, "GRF_TRADER", 65.0, 0.75,
                    "Changed a block inside a trader area at " + position.ToString());
            }
        }

        private static bool IsLandClaimBlock(BlockSnapshot snapshot)
        {
            if (snapshot == null || snapshot.IsAir || Block.list == null ||
                snapshot.Type >= Block.list.Length)
            {
                return false;
            }

            return IsLandClaimBlock(Block.list[snapshot.Type]);
        }

        private static bool IsLandClaimBlock(BlockValue value)
        {
            return !value.isair && IsLandClaimBlock(value.Block);
        }

        private static bool IsLandClaimBlock(Block block)
        {
            if (block == null || string.IsNullOrEmpty(block.blockName))
            {
                return false;
            }

            string name = block.blockName.ToLowerInvariant();
            return name.Contains("landclaim") || name.Contains("land_claim") || name.Contains("land claim");
        }

        private bool HasCloserPlayer(EntityPlayer self, Vector3i position)
        {
            World world = GetWorld();
            if (world == null || world.Players == null)
            {
                return false;
            }

            Vector3 target = new Vector3(position.x + 0.5f, position.y + 0.5f, position.z + 0.5f);
            float best = Vector3.Distance(self.position, target);
            List<EntityPlayer> players = world.Players.list;

            for (int i = 0; i < players.Count; i++)
            {
                EntityPlayer other = players[i];
                if (other == null || other == self || other.IsDead())
                {
                    continue;
                }

                float distance = Vector3.Distance(other.position, target);
                if (distance + 0.75f < best)
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region Combat handling

        private void HandleDamage(EntityAlive victim, DamageSource source)
        {
            if (!config.Combat.Enabled)
            {
                return;
            }

            EntityAlive attacker = null;
            if (source.getEntityId() != -1)
            {
                World world = GetWorld();
                if (world != null)
                {
                    attacker = world.GetEntity(source.getEntityId()) as EntityAlive;
                }
            }

            EntityPlayer attackerPlayer = attacker as EntityPlayer;
            if (attackerPlayer != null)
            {
                PlayerState player = GetPlayerState(attackerPlayer);
                if (player != null)
                {
                    DateTime now = DateTime.UtcNow;
                    player.DamageWindow.Add(1.0, now);
                    player.HitCountWindow++;
                    if (IsHeadshot(source, victim))
                    {
                        player.HeadshotCountWindow++;
                    }

                    ItemValue attackingItem = source.AttackingItem;
                    player.LastWeapon = GetItemName(attackingItem);

                    float distance = Vector3.Distance(attackerPlayer.position, victim.position);

                    if (IsMeleeWeapon(attackingItem) && distance > config.Combat.MaxMeleeReach)
                    {
                        AddEvidence(player, ModuleCombat, "CMB_REACH",
                            Math.Min(80.0, 45.0 + (distance - config.Combat.MaxMeleeReach) * 8.0),
                            0.85,
                            "Melee hit at " + distance.ToString("0.0", CultureInfo.InvariantCulture) + "m");
                    }

                    if (config.Combat.DetectEmptyMagazineShots &&
                        source.damageType == EnumDamageTypes.Piercing &&
                        distance > 2.5f &&
                        IsMagazineWeapon(attackingItem) &&
                        attackingItem.Meta <= 0)
                    {
                        AddEvidence(player, ModuleCombat, "EXP_EMPTY_MAG", 55.0, 0.45,
                            "Ranged hit at " + distance.ToString("0.0", CultureInfo.InvariantCulture) +
                            "m with an empty magazine");
                    }

                    if ((victim is EntityPlayer || victim is EntityZombie) &&
                        distance <= config.Combat.ThroughWallDistance &&
                        LineBlocked(attackerPlayer.getChestPosition(), victim.getChestPosition()))
                    {
                        AddEvidence(player, ModuleCombat, "CMB_WALLBANG",
                            70.0, 0.7,
                            "Hit " + (victim is EntityPlayer ? "player" : "zombie") +
                            " through a solid block at " + distance.ToString("0.0", CultureInfo.InvariantCulture) + "m");
                    }

                    if ((victim is EntityPlayer || victim is EntityZombie) &&
                        player.LastHitUtc != DateTime.MinValue && player.LastVictimId != victim.entityId &&
                        (now - player.LastHitUtc).TotalSeconds < 0.12)
                    {
                        AddEvidence(player, ModuleCombat, "CMB_TARGET_SWITCH", 45.0, 0.4,
                            "Switched damage targets in " +
                            ((now - player.LastHitUtc).TotalMilliseconds).ToString("0", CultureInfo.InvariantCulture) + "ms");
                    }

                    if (victim is EntityPlayer || victim is EntityZombie)
                    {
                        player.LastHitUtc = now;
                        player.LastVictimId = victim.entityId;
                    }

                    double hitsPerSecond = player.DamageWindow.Count(now);
                    if (hitsPerSecond > config.Combat.MaxHitsPerSecond)
                    {
                        AddEvidence(player, ModuleCombat, "CMB_HIT_RATE",
                            Math.Min(75.0, 35.0 + (hitsPerSecond - config.Combat.MaxHitsPerSecond) * 4.0),
                            0.65,
                            "Hit rate " + hitsPerSecond.ToString("0.0", CultureInfo.InvariantCulture) + "/s");
                    }

                    if (player.HitCountWindow >= config.Combat.MinHeadshots)
                    {
                        double ratio = player.HeadshotCountWindow / (double)player.HitCountWindow;
                        if (ratio > config.Combat.HeadshotRatioThreshold)
                        {
                            AddEvidence(player, ModuleCombat, "CMB_HEADSHOT_RATIO",
                                Math.Min(65.0, 25.0 + (ratio - config.Combat.HeadshotRatioThreshold) * 100.0),
                                0.55,
                                "Headshot ratio " + (ratio * 100.0).ToString("0", CultureInfo.InvariantCulture) +
                                "% over " + player.HitCountWindow + " hits");
                        }

                        if (player.HitCountWindow >= config.Combat.MinHeadshots * 2)
                        {
                            player.HitCountWindow = 0;
                            player.HeadshotCountWindow = 0;
                        }
                    }
                }
            }

            EntityPlayer victimPlayer = victim as EntityPlayer;
            if (victimPlayer != null)
            {
                PlayerState victimState = GetPlayerState(victimPlayer);
                if (victimState != null)
                {
                    victimState.DamageTakenWindow.Add(1.0, DateTime.UtcNow);
                    if (attackerPlayer != null)
                    {
                        ClientInfo attackerInfo = GetClientInfo(attackerPlayer);
                        if (attackerInfo != null)
                        {
                            victimState.LastAttackerId = GetPlayerId(attackerInfo);
                        }
                    }
                }
            }
        }

        private void HandleDeath(EntityAlive victim, DamageSource source)
        {
            if (!config.Combat.Enabled)
            {
                return;
            }

            EntityAlive killer = null;
            if (source != null && source.getEntityId() != -1)
            {
                World world = GetWorld();
                if (world != null)
                {
                    killer = world.GetEntity(source.getEntityId()) as EntityAlive;
                }
            }

            EntityPlayer killerPlayer = killer as EntityPlayer;
            if (killerPlayer != null)
            {
                PlayerState player = GetPlayerState(killerPlayer);
                if (player != null)
                {
                    DateTime now = DateTime.UtcNow;
                    string victimType = victim is EntityPlayer ? "player" : victim is EntityZombie ? "zombie" : "other";
                    int count;
                    player.KillCounts.TryGetValue(victimType, out count);
                    player.KillCounts[victimType] = count + 1;

                    // Zombie kill rate is dominated by horde nights and is not a
                    // useful anti-cheat signal. The rate check is PvP-focused.
                    if (victim is EntityPlayer)
                    {
                        player.KillWindow.Add(1.0, now);
                    }

                    double perMinute = player.KillWindow.Sum(now);
                    if (player.Samples >= config.MinSamples && perMinute >= config.Combat.MinKillsForRate)
                    {
                        double z = player.KillRateStat.Z(perMinute);
                        if (z > config.Combat.KillRateZThreshold)
                        {
                            AddEvidence(player, ModuleCombat, "CMB_KILL_RATE",
                                Math.Min(70.0, 25.0 + (z - config.Combat.KillRateZThreshold) * 8.0),
                                Math.Min(0.8, 0.35 + (z - config.Combat.KillRateZThreshold) * 0.08),
                                "Player kill rate " + perMinute.ToString("0.0", CultureInfo.InvariantCulture) +
                                "/min, z=" + z.ToString("0.0", CultureInfo.InvariantCulture));
                        }
                    }

                }
            }

            EntityPlayer victimPlayer = victim as EntityPlayer;
            if (victimPlayer != null)
            {
                PlayerState victimState = GetPlayerState(victimPlayer);
                if (victimState != null)
                {
                    DateTime now = DateTime.UtcNow;
                    ClientInfo killerInfo = killerPlayer != null ? GetClientInfo(killerPlayer) : null;
                    string killerId = killerInfo != null ? GetPlayerId(killerInfo) : String.Empty;
                    int repeated = 0;

                    for (int i = victimState.Deaths.Count - 1; i >= 0; i--)
                    {
                        DeathRecord record = victimState.Deaths[i];
                        if ((now - record.Utc).TotalMinutes > 10.0)
                        {
                            victimState.Deaths.RemoveAt(i);
                            continue;
                        }

                        if (!string.IsNullOrEmpty(killerId) &&
                            string.Equals(record.KillerId, killerId, StringComparison.OrdinalIgnoreCase))
                        {
                            repeated++;
                        }
                    }

                    victimState.Deaths.Add(new DeathRecord
                    {
                        Utc = now,
                        KillerId = killerId,
                        Position = victimPlayer.GetBlockPosition()
                    });

                    if (repeated >= 2 && !string.IsNullOrEmpty(killerId))
                    {
                        PlayerState killerState;
                        if (states.TryGetValue(killerId, out killerState) && killerState != null)
                        {
                            AddEvidence(killerState, ModuleGrief, "GRF_SPAWN_CAMP",
                                65.0, 0.6,
                                "Killed " + victimState.Name + " " + (repeated + 1) + " times in 10 minutes");
                        }
                    }
                }
            }
        }

        private void HandleEntitySpawned(Entity entity)
        {
            if (!config.Grief.Enabled || entity == null)
            {
                return;
            }

            EntityVehicle vehicle = entity as EntityVehicle;
            if (vehicle == null)
            {
                return;
            }

            World world = GetWorld();
            if (world == null || world.Players == null)
            {
                return;
            }

            List<EntityPlayer> players = world.Players.list;
            for (int i = 0; i < players.Count; i++)
            {
                EntityPlayer player = players[i];
                if (player == null || player.IsDead())
                {
                    continue;
                }

                if (Vector3.Distance(player.position, vehicle.position) > 40f)
                {
                    continue;
                }

                PlayerState state = GetPlayerState(player);
                if (state == null)
                {
                    continue;
                }

                state.VehicleSpawnWindow.Add(1.0, DateTime.UtcNow);
                if (state.VehicleSpawnWindow.Count(DateTime.UtcNow) > 3)
                {
                    AddEvidence(state, ModuleGrief, "GRF_VEHICLE_SPAWN", 55.0, 0.5,
                        "Multiple vehicles spawned nearby in a short window");
                }
            }
        }

        #endregion

        #region Risk engine

        private void AddEvidence(PlayerState player, string module, string code, double score, double confidence, string detail)
        {
            if (player == null || player.Exempt || config == null)
            {
                return;
            }

            if (score < 90.0 && player.PlaySeconds < config.MinPlayMinutes * 60.0)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            DateTime last;
            if (score < 90.0 && player.LastEvidenceAt.TryGetValue(code, out last) &&
                (now - last).TotalSeconds < config.EvidenceCooldownSeconds)
            {
                return;
            }

            player.LastEvidenceAt[code] = now;
            score = Clamp(score, 0.0, 100.0);
            confidence = Clamp(confidence, 0.0, 1.0);

            double moduleRisk;
            player.ModuleRisk.TryGetValue(module, out moduleRisk);
            double suppression = 1.0 / (1.0 + moduleRisk / 60.0);
            double contribution = score * confidence * ModuleWeight(module) * suppression * config.Sensitivity;
            double cap = score >= config.CriticalScoreThreshold
                ? config.MaxContributionCritical
                : config.MaxContributionNormal;
            contribution = Math.Min(cap, contribution);

            double before = player.Risk;
            player.Risk = Math.Min(100.0, player.Risk + contribution * (1.0 - player.Risk / 110.0));
            player.SessionRisk = Math.Min(100.0, player.SessionRisk + contribution * 0.5 * (1.0 - player.SessionRisk / 110.0));
            player.PeakRisk = Math.Max(player.PeakRisk, player.Risk);
            player.ModuleRisk[module] = moduleRisk + contribution;
            player.Flags++;

            Evidence evidence = new Evidence
            {
                Module = module,
                Code = code,
                Score = score,
                Confidence = confidence,
                Detail = detail ?? String.Empty,
                TimeUtc = now,
                RiskAfter = player.Risk
            };

            player.Evidence.Add(evidence);
            if (player.Evidence.Count > 200)
            {
                player.Evidence.RemoveRange(0, player.Evidence.Count - 200);
            }

            if (config.Debug)
            {
                Interface.Oxide.LogInfo("[SentinelAC] " + player.Name + " +" + contribution.ToString("0.0", CultureInfo.InvariantCulture) +
                                        " risk=" + player.Risk.ToString("0.0", CultureInfo.InvariantCulture) +
                                        " " + code + " " + detail);
            }

            if (before < config.AlertThreshold && player.Risk >= config.AlertThreshold)
            {
                SendAlert(player, "crossed the alert threshold", evidence);
            }
            else if (player.Risk >= config.WatchThreshold && !player.Watched && config.AutoWatch)
            {
                player.Watched = true;
                SendAlert(player, "auto-watched", evidence);
            }
        }

        private void DecayRisk(DateTime now)
        {
            double halfLife = Math.Max(60.0, config.RiskDecayHalfLifeMinutes * 60.0);
            double factor = Math.Exp(-config.ScanIntervalSeconds / halfLife);
            double retention = Math.Max(60.0, config.EvidenceRetentionMinutes * 60.0);

            foreach (PlayerState player in states.Values)
            {
                if (player == null)
                {
                    continue;
                }

                player.Risk *= factor;
                player.SessionRisk *= factor;
                if (player.Risk < 0.5)
                {
                    player.Risk = 0.0;
                }
                if (player.SessionRisk < 0.25)
                {
                    player.SessionRisk = 0.0;
                }

                if (player.ModuleRisk.Count > 0)
                {
                    List<string> keys = new List<string>(player.ModuleRisk.Keys);
                    for (int i = 0; i < keys.Count; i++)
                    {
                        string key = keys[i];
                        double value = player.ModuleRisk[key] * factor;
                        if (value < 0.25)
                        {
                            player.ModuleRisk.Remove(key);
                        }
                        else
                        {
                            player.ModuleRisk[key] = value;
                        }
                    }
                }

                for (int i = player.Evidence.Count - 1; i >= 0; i--)
                {
                    if ((now - player.Evidence[i].TimeUtc).TotalSeconds > retention)
                    {
                        player.Evidence.RemoveAt(i);
                    }
                }

                if (player.Risk < config.AlertThreshold * 0.5)
                {
                    player.LastAlertUtc = DateTime.MinValue;
                }
            }
        }

        private static double ModuleWeight(string module)
        {
            switch (module)
            {
                case ModuleGrief: return 1.5;
                case ModuleExploit: return 1.5;
                case ModuleInventory: return 1.3;
                case ModuleCombat: return 1.2;
                case ModuleMovement: return 1.0;
                case ModuleProgression: return 0.8;
                default: return 1.0;
            }
        }

        private RunningStat GlobalStat(string name)
        {
            RunningStat stat;
            if (!globalStats.TryGetValue(name, out stat) || stat == null)
            {
                stat = new RunningStat();
                globalStats[name] = stat;
            }
            return stat;
        }

        private double GlobalZ(string name, double value)
        {
            RunningStat stat;
            return globalStats.TryGetValue(name, out stat) && stat != null ? stat.Z(value) : 0.0;
        }

        #endregion

        #region Alerts and actions

        private void SendAlert(PlayerState player, string reason, Evidence evidence)
        {
            if (player == null)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            double cooldown = player.Risk >= config.KickThreshold
                ? Math.Min(5.0, config.AlertCooldownSeconds)
                : config.AlertCooldownSeconds;
            if (now < player.LastAlertUtc.AddSeconds(cooldown))
            {
                return;
            }

            player.LastAlertUtc = now;
            string message = player.Name + " (" + player.Id + ") risk " +
                             player.Risk.ToString("0", CultureInfo.InvariantCulture) + " - " + reason;
            if (evidence != null)
            {
                message += " [" + evidence.Code + ": " + evidence.Detail + "]";
            }

            Interface.Oxide.LogWarning("[SentinelAC] " + message);
            BroadcastAdmins(message);
            SendWebhook(player, message);
        }

        private void BroadcastAdmins(string message)
        {
            foreach (IPlayer player in players.Connected)
            {
                if (player == null)
                {
                    continue;
                }

                if (IsAdmin(player) || player.HasPermission(PermAlerts))
                {
                    player.Reply(Format(message));
                }
            }
        }

        private void EvaluateActions(DateTime now)
        {
            if (config.DryRun)
            {
                return;
            }

            foreach (PlayerState player in states.Values)
            {
                if (player == null || !player.Online || player.Exempt)
                {
                    continue;
                }

                bool kick = config.AutoKick && player.Risk >= config.KickThreshold;
                bool ban = config.AutoBan && player.Risk >= config.BanThreshold;
                if (!kick && !ban)
                {
                    player.HighRiskSinceUtc = DateTime.MinValue;
                    continue;
                }

                if (player.HighRiskSinceUtc == DateTime.MinValue)
                {
                    player.HighRiskSinceUtc = now;
                }

                if ((now - player.HighRiskSinceUtc).TotalSeconds < config.ActionSustainSeconds)
                {
                    continue;
                }

                if (now < player.NextActionUtc)
                {
                    continue;
                }

                IPlayer target = players.FindPlayerById(player.Id);
                if (target == null || !target.IsConnected)
                {
                    continue;
                }

                player.NextActionUtc = now.AddMinutes(config.ActionCooldownMinutes);

                if (ban)
                {
                    string reason = "SentinelAC: statistical risk " + player.Risk.ToString("0", CultureInfo.InvariantCulture);
                    Interface.Oxide.LogWarning("[SentinelAC] Auto-ban " + player.Name + " (" + player.Id + ") risk " +
                                               player.Risk.ToString("0.0", CultureInfo.InvariantCulture));
                    target.Ban(reason, TimeSpan.FromHours(config.DefaultBanHours));
                }
                else if (kick)
                {
                    string reason = "SentinelAC: statistical risk " + player.Risk.ToString("0", CultureInfo.InvariantCulture);
                    Interface.Oxide.LogWarning("[SentinelAC] Auto-kick " + player.Name + " (" + player.Id + ") risk " +
                                               player.Risk.ToString("0.0", CultureInfo.InvariantCulture));
                    target.Kick(reason);
                }
            }
        }

        private void SendWebhook(PlayerState player, string message)
        {
            if (!config.Webhook.Enabled || string.IsNullOrEmpty(config.Webhook.Url) || player == null)
            {
                return;
            }

            if (player.Risk < config.Webhook.MinRisk)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            if (now < player.NextWebhookUtc)
            {
                return;
            }

            player.NextWebhookUtc = now.AddSeconds(config.Webhook.MinIntervalSeconds);

            StringBuilder content = new StringBuilder(512);
            content.Append("**SentinelAC** risk **");
            content.Append(player.Risk.ToString("0", CultureInfo.InvariantCulture));
            content.Append("** - ");
            content.Append(player.Name);
            content.Append(" (`");
            content.Append(player.Id);
            content.Append("`)");
            content.Append('\n');
            content.Append(message);

            if (config.Webhook.IncludeEvidence)
            {
                int start = Math.Max(0, player.Evidence.Count - 5);
                for (int i = start; i < player.Evidence.Count; i++)
                {
                    Evidence evidence = player.Evidence[i];
                    content.Append("\n- ");
                    content.Append(evidence.Code);
                    content.Append(" (");
                    content.Append(evidence.Score.ToString("0", CultureInfo.InvariantCulture));
                    content.Append("): ");
                    content.Append(evidence.Detail);
                }
            }

            string text = content.ToString();
            if (text.Length > 1900)
            {
                text = text.Substring(0, 1900);
            }

            string json = "{\"username\":\"" + JsonEscape(config.Webhook.Username) +
                          "\",\"content\":\"" + JsonEscape(text) + "\"}";

            try
            {
                webrequest.Enqueue(config.Webhook.Url, json, (code, response) =>
                {
                    if (code >= 400)
                    {
                        Interface.Oxide.LogWarning("[SentinelAC] webhook returned HTTP " + code);
                    }
                }, this, RequestMethod.POST,
                    new Dictionary<string, string> { { "Content-Type", "application/json" } }, 10);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[SentinelAC] webhook failed: " + exception.Message);
            }
        }

        #endregion

        #region Commands

        private void CommandSentinel(IPlayer player, string command, string[] args)
        {
            if (player != null && !player.IsServer && !IsAdmin(player))
            {
                Reply(player, "You need the '" + PermAdmin + "' permission.");
                return;
            }

            string sub = args.Length == 0 ? "status" : args[0].ToLowerInvariant();

            switch (sub)
            {
                case "help":
                    ShowHelp(player);
                    break;
                case "status":
                case "info":
                    ShowStatus(player, args.Length > 1 ? args[1] : null);
                    break;
                case "top":
                    ShowTop(player);
                    break;
                case "flags":
                case "evidence":
                    ShowFlags(player, args.Length > 1 ? args[1] : null);
                    break;
                case "watch":
                    ToggleWatch(player, args.Length > 1 ? args[1] : null);
                    break;
                case "clear":
                    ClearPlayer(player, args.Length > 1 ? args[1] : null);
                    break;
                case "kick":
                    KickPlayer(player, args);
                    break;
                case "ban":
                    BanPlayer(player, args);
                    break;
                case "export":
                    ExportPlayer(player, args.Length > 1 ? args[1] : null);
                    break;
                case "reload":
                    LoadConfig();
                    if (!string.IsNullOrEmpty(config.ExemptPermission))
                    {
                        permission.RegisterPermission(config.ExemptPermission, this);
                    }
                    StartTimers();
                    RegisterServerAssist();
                    Reply(player, "Configuration reloaded.");
                    break;
                case "gui":
                case "panel":
                    OpenServerAssistGui(player);
                    break;
                case "modules":
                    ShowModules(player, args.Length > 1 ? args[1] : null);
                    break;
                default:
                    ShowHelp(player);
                    break;
            }
        }

        private void ShowHelp(IPlayer player)
        {
            Reply(player, "/ac status [player] - risk, flags and playtime");
            Reply(player, "/ac top - highest-risk online players");
            Reply(player, "/ac flags <player> - recent evidence");
            Reply(player, "/ac modules <player> - risk by detection module");
            Reply(player, "/ac watch <player> - toggle staff watch");
            Reply(player, "/ac clear <player> - clear evidence and risk");
            Reply(player, "/ac kick <player> [reason] - kick a player");
            Reply(player, "/ac ban <player> [hours] [reason] - ban a player");
            Reply(player, "/ac export <player> - write an evidence report");
            Reply(player, "/ac gui - open the ServerAssist panel");
            Reply(player, "/ac reload - reload configuration");
        }

        private void ShowStatus(IPlayer player, string target)
        {
            PlayerState state = ResolveState(target);
            if (state == null)
            {
                Reply(player, target == null ? "No player state found." : "Player not found: " + target);
                return;
            }

            Reply(player, state.Name + " (" + state.Id + ")");
            Reply(player, "Risk: " + state.Risk.ToString("0.0", CultureInfo.InvariantCulture) +
                          " (p~" + (RiskProbability(state.Risk) * 100.0).ToString("0", CultureInfo.InvariantCulture) + "%)" +
                          " | session " + state.SessionRisk.ToString("0.0", CultureInfo.InvariantCulture) +
                          " | peak " + state.PeakRisk.ToString("0.0", CultureInfo.InvariantCulture) +
                          " | flags " + state.Flags);
            Reply(player, "Online: " + state.Online + " | play " + FormatMinutes(state.PlaySeconds / 60.0) +
                          " | samples " + state.Samples + (state.Exempt ? " | EXEMPT" : String.Empty) +
                          (state.Watched ? " | WATCHED" : String.Empty));
            Reply(player, "Kills: player " + GetKillCount(state, "player") + ", zombie " + GetKillCount(state, "zombie") +
                          " | last weapon: " + (string.IsNullOrEmpty(state.LastWeapon) ? "unknown" : state.LastWeapon));

            Evidence latest = state.Evidence.Count > 0 ? state.Evidence[state.Evidence.Count - 1] : null;
            if (latest != null)
            {
                Reply(player, "Latest: " + latest.Code + " (" + latest.Score.ToString("0", CultureInfo.InvariantCulture) +
                              ") " + latest.Detail);
            }
        }

        private void ShowTop(IPlayer player)
        {
            List<PlayerState> list = states.Values.Where(s => s != null && s.Online)
                .OrderByDescending(s => s.Risk).Take(10).ToList();

            if (list.Count == 0)
            {
                Reply(player, "No online players.");
                return;
            }

            Reply(player, "Highest-risk online players:");
            for (int i = 0; i < list.Count; i++)
            {
                PlayerState state = list[i];
                Reply(player, (i + 1) + ". " + state.Name + " - " +
                              state.Risk.ToString("0.0", CultureInfo.InvariantCulture) +
                              " (" + state.Flags + " flags)");
            }
        }

        private void ShowFlags(IPlayer player, string target)
        {
            PlayerState state = ResolveState(target);
            if (state == null)
            {
                Reply(player, target == null ? "No player state found." : "Player not found: " + target);
                return;
            }

            if (state.Evidence.Count == 0)
            {
                Reply(player, state.Name + " has no evidence.");
                return;
            }

            Reply(player, "Evidence for " + state.Name + ":");
            int start = Math.Max(0, state.Evidence.Count - 12);
            for (int i = state.Evidence.Count - 1; i >= start; i--)
            {
                Evidence evidence = state.Evidence[i];
                Reply(player, evidence.TimeUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) +
                              " " + evidence.Module + "/" + evidence.Code +
                              " score=" + evidence.Score.ToString("0", CultureInfo.InvariantCulture) +
                              " conf=" + (evidence.Confidence * 100.0).ToString("0", CultureInfo.InvariantCulture) +
                              "% " + evidence.Detail);
            }
        }

        private void ShowModules(IPlayer player, string target)
        {
            PlayerState state = ResolveState(target);
            if (state == null)
            {
                Reply(player, target == null ? "No player state found." : "Player not found: " + target);
                return;
            }

            Reply(player, "Risk by module for " + state.Name + ":");
            if (state.ModuleRisk.Count == 0)
            {
                Reply(player, "No active module risk.");
                return;
            }

            foreach (KeyValuePair<string, double> pair in state.ModuleRisk.OrderByDescending(p => p.Value))
            {
                Reply(player, pair.Key + ": " + pair.Value.ToString("0.0", CultureInfo.InvariantCulture));
            }
        }

        private void ToggleWatch(IPlayer player, string target)
        {
            PlayerState state = ResolveState(target);
            if (state == null)
            {
                Reply(player, target == null ? "No player state found." : "Player not found: " + target);
                return;
            }

            state.Watched = !state.Watched;
            Reply(player, state.Name + " is now " + (state.Watched ? "watched." : "no longer watched."));
        }

        private void ClearPlayer(IPlayer player, string target)
        {
            PlayerState state = ResolveState(target);
            if (state == null)
            {
                Reply(player, target == null ? "No player state found." : "Player not found: " + target);
                return;
            }

            ClearState(state);
            Reply(player, "Cleared risk and evidence for " + state.Name + ".");
        }

        private void ClearState(PlayerState state)
        {
            if (state == null)
            {
                return;
            }

            state.Risk = 0.0;
            state.SessionRisk = 0.0;
            state.PeakRisk = 0.0;
            state.ModuleRisk.Clear();
            state.Evidence.Clear();
            state.LastEvidenceAt.Clear();
            state.Flags = 0;
            state.HighRiskSinceUtc = DateTime.MinValue;
            state.LastAlertUtc = DateTime.MinValue;

            StoredProfile profile;
            if (storedData != null && storedData.Players != null &&
                storedData.Players.TryGetValue(state.Id, out profile) && profile != null)
            {
                profile.PeakRisk = 0.0;
                profile.Flags = 0;
            }
        }

        private void KickPlayer(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /ac kick <player> [reason]");
                return;
            }

            IPlayer target = players.FindPlayer(args[1]);
            if (target == null || !target.IsConnected)
            {
                Reply(player, "Player not found or offline: " + args[1]);
                return;
            }

            string reason = args.Length > 2 ? string.Join(" ", args.Skip(2).ToArray()) : "Kicked by staff";
            Interface.Oxide.LogWarning("[SentinelAC] " + (player != null ? player.Name : "console") +
                                       " kicked " + target.Name + ": " + reason);
            target.Kick(reason);
            Reply(player, "Kicked " + target.Name + ".");
        }

        private void BanPlayer(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /ac ban <player> [hours] [reason]");
                return;
            }

            IPlayer target = players.FindPlayer(args[1]);
            if (target == null)
            {
                Reply(player, "Player not found: " + args[1]);
                return;
            }

            double hours = config.DefaultBanHours;
            int reasonStart = 2;
            if (args.Length > 2)
            {
                double parsed;
                if (double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) && parsed > 0.0)
                {
                    hours = parsed;
                    reasonStart = 3;
                }
            }

            string reason = args.Length > reasonStart
                ? string.Join(" ", args.Skip(reasonStart).ToArray())
                : "Banned by staff";

            Interface.Oxide.LogWarning("[SentinelAC] " + (player != null ? player.Name : "console") +
                                       " banned " + target.Name + " for " + hours.ToString("0.#", CultureInfo.InvariantCulture) +
                                       "h: " + reason);
            target.Ban(reason, TimeSpan.FromHours(hours));
            Reply(player, "Banned " + target.Name + " for " + hours.ToString("0.#", CultureInfo.InvariantCulture) + "h.");
        }

        private void ExportPlayer(IPlayer player, string target)
        {
            PlayerState state = ResolveState(target);
            if (state == null)
            {
                Reply(player, target == null ? "No player state found." : "Player not found: " + target);
                return;
            }

            string file;
            string error;
            if (TryExportState(state, out file, out error))
            {
                Reply(player, "Exported evidence for " + state.Name + " to oxide/data/" + file + ".json");
            }
            else
            {
                Reply(player, "Export failed: " + error);
            }
        }

        private bool TryExportState(PlayerState state, out string file, out string error)
        {
            file = String.Empty;
            error = String.Empty;

            if (state == null)
            {
                error = "No player state.";
                return false;
            }

            PlayerExport export = new PlayerExport
            {
                Id = state.Id,
                Name = state.Name,
                Risk = state.Risk,
                SessionRisk = state.SessionRisk,
                PeakRisk = state.PeakRisk,
                Flags = state.Flags,
                PlayMinutes = state.PlaySeconds / 60.0,
                Kills = new Dictionary<string, int>(state.KillCounts),
                Modules = new Dictionary<string, double>(state.ModuleRisk),
                Evidence = new List<Evidence>(state.Evidence)
            };

            file = "SentinelAC/export-" + SafeFileName(state.Id);
            try
            {
                Interface.Oxide.DataFileSystem.WriteObject(file, export, true);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private PlayerState ResolveState(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return null;
            }

            PlayerState state;
            if (states.TryGetValue(input, out state))
            {
                return state;
            }

            foreach (PlayerState candidate in states.Values)
            {
                if (candidate != null && string.Equals(candidate.Name, input, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            IPlayer player = players.FindPlayer(input);
            if (player != null && states.TryGetValue(player.Id, out state))
            {
                return state;
            }

            return null;
        }

        private static int GetKillCount(PlayerState state, string type)
        {
            int count;
            return state != null && state.KillCounts.TryGetValue(type, out count) ? count : 0;
        }

        #endregion

        #region ServerAssist

        private void RegisterServerAssist()
        {
            if (config == null || config.ServerAssist == null || !config.ServerAssist.Enabled)
            {
                return;
            }

            Plugin serverAssist = plugins.Find("ServerAssist");
            if (serverAssist == null || !serverAssist.IsLoaded)
            {
                return;
            }

            string panelId = config.ServerAssist.PanelId;
            string icon = string.IsNullOrEmpty(config.ServerAssist.Icon) ? IconPanel : config.ServerAssist.Icon;
            string pluginName = string.IsNullOrEmpty(Name) ? GetType().Name : Name;

            // Provider mode gives SentinelAC the real three-tab Server Assist
            // chrome. Older ServerAssist builds fall back to a normal home panel.
            try
            {
                object support = serverAssist.Call("GetProviderHomeSupport");
                bool providerSupported = support is bool && (bool)support;
                if (providerSupported)
                {
                    object providerResult = serverAssist.Call(
                        "RegisterGuiProvider",
                        panelId,
                        pluginName,
                        nameof(HandleServerAssistRequest),
                        "Sentinel AC",
                        "Risk",
                        "Evidence",
                        "Modules");

                    if (providerResult is bool && (bool)providerResult)
                    {
                        serverAssist.Call("SetProviderIcon", panelId, icon);
                        serverAssist.Call("UnregisterPanel", panelId);
                        serverAssistProviderMode = true;
                        serverAssistRegistered = true;
                        return;
                    }
                }
            }
            catch (MissingMethodException)
            {
                // Provider API is unavailable. Panel fallback below.
            }
            catch (Exception exception)
            {
                if (!serverAssistWarningLogged)
                {
                    serverAssistWarningLogged = true;
                    Interface.Oxide.LogWarning("[SentinelAC] ServerAssist provider registration failed: " + exception.Message);
                }
            }

            try
            {
                object panelResult = serverAssist.Call(
                    "RegisterPanel",
                    panelId,
                    "Sentinel AC",
                    "Statistical anti-cheat, anti-exploit and anti-grief evidence.",
                    PermAdmin,
                    pluginName,
                    nameof(HandleServerAssistRequest),
                    icon,
                    config.ServerAssist.Order);

                serverAssistRegistered = panelResult is bool && (bool)panelResult;
                serverAssistProviderMode = false;

                if (serverAssistRegistered)
                {
                    try
                    {
                        serverAssist.Call("UnregisterGuiProvider", panelId);
                    }
                    catch
                    {
                        // Older ServerAssist builds do not have providers.
                    }
                }
            }
            catch (Exception exception)
            {
                serverAssistRegistered = false;
                serverAssistProviderMode = false;
                if (!serverAssistWarningLogged)
                {
                    serverAssistWarningLogged = true;
                    Interface.Oxide.LogWarning("[SentinelAC] could not register the ServerAssist panel: " + exception.Message);
                }
            }
        }

        private void UnregisterServerAssist()
        {
            Plugin serverAssist = plugins.Find("ServerAssist");
            if (serverAssist != null && config != null && config.ServerAssist != null)
            {
                try
                {
                    serverAssist.Call("UnregisterGuiProvider", config.ServerAssist.PanelId);
                }
                catch
                {
                    // ServerAssist may already be unloading.
                }

                try
                {
                    serverAssist.Call("UnregisterPanel", config.ServerAssist.PanelId);
                }
                catch
                {
                    // ServerAssist may already be unloading.
                }
            }

            serverAssistRegistered = false;
            serverAssistProviderMode = false;
        }

        private void OpenServerAssistGui(IPlayer player)
        {
            if (player == null || player.IsServer || config == null || config.ServerAssist == null)
            {
                return;
            }

            if (!config.ServerAssist.Enabled)
            {
                Reply(player, "The ServerAssist menu is disabled in the SentinelAC config.");
                return;
            }

            if (!serverAssistRegistered)
            {
                RegisterServerAssist();
            }

            Plugin serverAssist = plugins.Find("ServerAssist");
            if (serverAssist == null)
            {
                Reply(player, "ServerAssist is not loaded on this server.");
                return;
            }

            if (!serverAssistRegistered)
            {
                Reply(player, "The ServerAssist menu could not be registered. Check the Oxide log.");
                return;
            }

            try
            {
                object result = serverAssistProviderMode
                    ? serverAssist.Call("OpenGuiProvider", player.Id, config.ServerAssist.PanelId, "a|open")
                    : serverAssist.Call("OpenPanel", player.Id, config.ServerAssist.PanelId, "a|open");

                if ((!(result is bool) || !(bool)result) && serverAssistProviderMode)
                {
                    // A client with an older ServerAssist mod may not be provider
                    // capable. Register the normal panel fallback and try again.
                    serverAssistProviderMode = false;
                    string icon = string.IsNullOrEmpty(config.ServerAssist.Icon) ? IconPanel : config.ServerAssist.Icon;
                    string pluginName = string.IsNullOrEmpty(Name) ? GetType().Name : Name;

                    try
                    {
                        serverAssist.Call("UnregisterGuiProvider", config.ServerAssist.PanelId);
                    }
                    catch
                    {
                        // Older builds do not have providers.
                    }

                    object panelResult = serverAssist.Call(
                        "RegisterPanel",
                        config.ServerAssist.PanelId,
                        "Sentinel AC",
                        "Statistical anti-cheat, anti-exploit and anti-grief evidence.",
                        PermAdmin,
                        pluginName,
                        nameof(HandleServerAssistRequest),
                        icon,
                        config.ServerAssist.Order);

                    if (panelResult is bool && (bool)panelResult)
                    {
                        result = serverAssist.Call("OpenPanel", player.Id, config.ServerAssist.PanelId, "a|open");
                    }
                }

                if (!(result is bool) || !(bool)result)
                {
                    Reply(player, "The ServerAssist menu could not be opened. Make sure the ServerAssist client mod is installed.");
                }
            }
            catch (Exception exception)
            {
                Reply(player, "Could not open the ServerAssist menu: " + exception.Message);
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

                if (!IsAdmin(player))
                {
                    return BuildServerAssistErrorView("You do not have permission to use Sentinel AC.");
                }

                GuiSession session = GetGuiSession(playerId);
                string action = String.Empty;

                if (!string.IsNullOrEmpty(payload))
                {
                    if (payload.StartsWith("t|", StringComparison.Ordinal))
                    {
                        session.Search = SanitizeSearch(Decode(payload.Substring(2)));
                        session.Page = 0;
                        session.Tab = "risk";
                        return BuildCurrentServerAssistView(player, session,
                            string.IsNullOrEmpty(session.Search) ? "Search cleared." : "Filter: " + session.Search);
                    }

                    if (payload.StartsWith("a|", StringComparison.Ordinal))
                    {
                        action = Decode(payload.Substring(2));
                    }
                    else
                    {
                        action = Decode(payload);
                    }
                }

                if (string.IsNullOrEmpty(action) || String.Equals(action, "open", StringComparison.OrdinalIgnoreCase))
                {
                    session.Tab = "risk";
                    session.Page = 0;
                    session.Search = String.Empty;
                    return BuildCurrentServerAssistView(player, session, String.Empty);
                }

                if (action.StartsWith("tab|", StringComparison.OrdinalIgnoreCase))
                {
                    SwitchGuiTab(session, action.Substring(4));
                    return BuildCurrentServerAssistView(player, session, String.Empty);
                }

                if (action.StartsWith("select|", StringComparison.OrdinalIgnoreCase))
                {
                    string selectedId = action.Substring(7);
                    PlayerState selected = ResolveState(selectedId);
                    if (selected == null)
                    {
                        return BuildCurrentServerAssistView(player, session, "Player not found.");
                    }

                    session.SelectedId = selected.Id;
                    session.Tab = "evidence";
                    return BuildCurrentServerAssistView(player, session, "Selected " + selected.Name + ".");
                }

                if (action.StartsWith("page|", StringComparison.OrdinalIgnoreCase))
                {
                    string pageAction = action.Substring(5);
                    if (String.Equals(pageAction, "prev", StringComparison.OrdinalIgnoreCase))
                    {
                        session.Page = Math.Max(0, session.Page - 1);
                    }
                    else if (String.Equals(pageAction, "next", StringComparison.OrdinalIgnoreCase))
                    {
                        session.Page++;
                    }
                    else if (String.Equals(pageAction, "first", StringComparison.OrdinalIgnoreCase))
                    {
                        session.Page = 0;
                    }
                    else if (String.Equals(pageAction, "last", StringComparison.OrdinalIgnoreCase))
                    {
                        session.Page = Int32.MaxValue;
                    }
                    else
                    {
                        int page;
                        if (Int32.TryParse(pageAction, NumberStyles.Integer, CultureInfo.InvariantCulture, out page))
                        {
                            session.Page = Math.Max(0, page);
                        }
                    }

                    return BuildCurrentServerAssistView(player, session, String.Empty);
                }

                if (action.StartsWith("watch|", StringComparison.OrdinalIgnoreCase))
                {
                    PlayerState state = ResolveState(action.Substring(6));
                    if (state != null)
                    {
                        state.Watched = !state.Watched;
                    }
                    return BuildCurrentServerAssistView(player, session,
                        state == null ? "Player not found." : "Watch " + (state.Watched ? "enabled." : "disabled."));
                }

                if (action.StartsWith("clear|", StringComparison.OrdinalIgnoreCase))
                {
                    PlayerState state = ResolveState(action.Substring(6));
                    if (state != null)
                    {
                        ClearState(state);
                    }
                    return BuildCurrentServerAssistView(player, session,
                        state == null ? "Player not found." : "Evidence cleared.");
                }

                if (action.StartsWith("export|", StringComparison.OrdinalIgnoreCase))
                {
                    PlayerState state = ResolveState(action.Substring(7));
                    string file;
                    string error;
                    if (state == null)
                    {
                        return BuildCurrentServerAssistView(player, session, "Player not found.");
                    }

                    return BuildCurrentServerAssistView(player, session,
                        TryExportState(state, out file, out error)
                            ? "Exported to oxide/data/" + file + ".json"
                            : "Export failed: " + error);
                }

                if (action.StartsWith("kick|", StringComparison.OrdinalIgnoreCase))
                {
                    if (!config.AllowGuiKick)
                    {
                        return BuildCurrentServerAssistView(player, session, "GUI kick is disabled in the config.");
                    }

                    PlayerState state = ResolveState(action.Substring(5));
                    IPlayer target = state != null ? players.FindPlayerById(state.Id) : null;
                    if (target != null && target.IsConnected)
                    {
                        target.Kick("SentinelAC: staff action");
                        return BuildCurrentServerAssistView(player, session, "Kicked " + target.Name + ".");
                    }
                    return BuildCurrentServerAssistView(player, session, "Player not online.");
                }

                if (action.StartsWith("ban|", StringComparison.OrdinalIgnoreCase))
                {
                    if (!config.AllowGuiBan)
                    {
                        return BuildCurrentServerAssistView(player, session, "GUI ban is disabled in the config.");
                    }

                    PlayerState state = ResolveState(action.Substring(4));
                    IPlayer target = state != null ? players.FindPlayerById(state.Id) : null;
                    if (target != null)
                    {
                        target.Ban("SentinelAC: staff action", TimeSpan.FromHours(config.DefaultBanHours));
                        return BuildCurrentServerAssistView(player, session, "Banned " + target.Name + ".");
                    }
                    return BuildCurrentServerAssistView(player, session, "Player not found.");
                }

                if (String.Equals(action, "home", StringComparison.OrdinalIgnoreCase))
                {
                    // Server Assist normally consumes a|home before forwarding it.
                    // If a build forwards it to the provider, an empty result clears
                    // the active provider and returns the player to the board.
                    return serverAssistProviderMode
                        ? String.Empty
                        : BuildCurrentServerAssistView(player, session, String.Empty);
                }

                if (String.Equals(action, "refresh", StringComparison.OrdinalIgnoreCase))
                {
                    return BuildCurrentServerAssistView(player, session, "Refreshed.");
                }

                return BuildCurrentServerAssistView(player, session, "Unknown action: " + action);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogException("[SentinelAC] ServerAssist request failed", exception);
                return BuildServerAssistErrorView("Internal error: " + exception.Message);
            }
        }

        private GuiSession GetGuiSession(string playerId)
        {
            string key = playerId ?? String.Empty;
            GuiSession session;
            if (!guiSessions.TryGetValue(key, out session) || session == null)
            {
                session = new GuiSession();
                guiSessions[key] = session;
            }
            return session;
        }

        private static void SwitchGuiTab(GuiSession session, string tab)
        {
            if (session == null)
            {
                return;
            }

            string value = (tab ?? String.Empty).Trim().ToLowerInvariant();
            if (value == "2" || value == "evidence" || value == "shared")
            {
                session.Tab = "evidence";
            }
            else if (value == "3" || value == "modules" || value == "mine")
            {
                session.Tab = "modules";
            }
            else
            {
                session.Tab = "risk";
            }
        }

        private static string SanitizeSearch(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return String.Empty;
            }

            value = value.Replace("|", " ").Replace("\r", " ").Replace("\n", " ").Trim();
            return value.Length > 48 ? value.Substring(0, 48) : value;
        }

        private string BuildCurrentServerAssistView(IPlayer player, GuiSession session, string status)
        {
            if (session == null)
            {
                return BuildServerAssistErrorView("Session not found.");
            }

            if (String.Equals(session.Tab, "evidence", StringComparison.OrdinalIgnoreCase))
            {
                return BuildEvidenceServerAssistView(player, session, status);
            }

            if (String.Equals(session.Tab, "modules", StringComparison.OrdinalIgnoreCase))
            {
                return BuildModulesServerAssistView(player, session, status);
            }

            return BuildRiskServerAssistView(player, session, status);
        }

        private StringBuilder BeginServerAssistView(GuiSession session, string title, string subtitle, string status, bool showSearch)
        {
            string icon = config != null && config.ServerAssist != null && !string.IsNullOrEmpty(config.ServerAssist.Icon)
                ? config.ServerAssist.Icon
                : IconPanel;

            string tabValue = "home";
            if (session != null && String.Equals(session.Tab, "evidence", StringComparison.OrdinalIgnoreCase))
            {
                tabValue = "shared";
            }
            else if (session != null && String.Equals(session.Tab, "modules", StringComparison.OrdinalIgnoreCase))
            {
                tabValue = "mine";
            }

            StringBuilder builder = new StringBuilder(4096);
            builder.Append("v2\n");
            builder.Append("protocol=2\n");
            builder.Append("title=").Append(ServerAssistEncode(title)).Append('\n');
            builder.Append("subtitle=").Append(ServerAssistEncode(subtitle)).Append('\n');
            builder.Append("status=").Append(ServerAssistEncode(status)).Append('\n');
            builder.Append("tab=").Append(ServerAssistEncode(tabValue)).Append('\n');
            builder.Append("tab1label=").Append(ServerAssistEncode("Risk")).Append('\n');
            builder.Append("tab2label=").Append(ServerAssistEncode("Evidence")).Append('\n');
            builder.Append("tab3label=").Append(ServerAssistEncode("Modules")).Append('\n');
            builder.Append("background=").Append(ServerAssistEncode(icon)).Append('\n');
            builder.Append("banner=").Append(ServerAssistEncode(icon)).Append('\n');
            builder.Append("input=");
            if (showSearch)
            {
                builder.Append(ServerAssistEncode("Search player")).Append("|1|")
                    .Append(ServerAssistEncode(session != null ? session.Search : String.Empty));
            }
            else
            {
                builder.Append("|0|");
            }
            builder.Append('\n');
            return builder;
        }

        private static void AppendServerAssistRow(StringBuilder builder, int index, string action, string label,
            string sub, bool enabled, bool selected, string icon, string badge, string tint)
        {
            builder.Append("row=").Append(index).Append('|')
                .Append(ServerAssistEncode(action)).Append('|')
                .Append(ServerAssistEncode(label)).Append('|')
                .Append(ServerAssistEncode(sub)).Append('|')
                .Append(enabled ? "1" : "0").Append('|')
                .Append(selected ? "1" : "0").Append('|')
                .Append(ServerAssistEncode(icon)).Append('|')
                .Append(ServerAssistEncode(badge)).Append('|')
                .Append(ServerAssistEncode(tint)).Append('\n');
        }

        private static void AppendServerAssistButton(StringBuilder builder, int index, string action, string label,
            bool enabled, string tooltip, string icon, string tint)
        {
            builder.Append("btn=").Append(index).Append('|')
                .Append(ServerAssistEncode(action)).Append('|')
                .Append(ServerAssistEncode(label)).Append('|')
                .Append(enabled ? "1" : "0").Append('|')
                .Append(ServerAssistEncode(tooltip));

            if (!string.IsNullOrEmpty(icon) || !string.IsNullOrEmpty(tint))
            {
                builder.Append('|').Append(ServerAssistEncode(icon)).Append('|').Append(ServerAssistEncode(tint));
            }

            builder.Append('\n');
        }

        private List<PlayerState> GetFilteredStates(string search)
        {
            List<PlayerState> list = new List<PlayerState>();
            foreach (PlayerState state in states.Values)
            {
                if (state == null || !state.Online)
                {
                    continue;
                }

                if (!MatchesSearch(state, search))
                {
                    continue;
                }

                list.Add(state);
            }

            list.Sort((left, right) =>
            {
                int risk = right.Risk.CompareTo(left.Risk);
                return risk != 0 ? risk : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            });

            return list;
        }

        private static bool MatchesSearch(PlayerState state, string search)
        {
            if (state == null || string.IsNullOrEmpty(search))
            {
                return true;
            }

            return (!string.IsNullOrEmpty(state.Name) &&
                    state.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (!string.IsNullOrEmpty(state.Id) &&
                    state.Id.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private PlayerState GetSelectedState(GuiSession session)
        {
            return session == null || string.IsNullOrEmpty(session.SelectedId)
                ? null
                : ResolveState(session.SelectedId);
        }

        private string BuildRiskServerAssistView(IPlayer player, GuiSession session, string status)
        {
            List<PlayerState> list = GetFilteredStates(session.Search);
            const int pageSize = 8;
            int totalPages = Math.Max(1, (list.Count + pageSize - 1) / pageSize);
            session.Page = Clamp(session.Page, 0, totalPages - 1);

            string pageStatus = status ?? String.Empty;
            if (pageStatus.Length > 0)
            {
                pageStatus += " | ";
            }
            pageStatus += list.Count + (list.Count == 1 ? " player" : " players") +
                          " | page " + (session.Page + 1) + "/" + totalPages;
            if (!string.IsNullOrEmpty(session.Search))
            {
                pageStatus += " | filter: " + session.Search;
            }

            StringBuilder builder = BeginServerAssistView(session, "Sentinel AC",
                "Risk board - online players by statistical risk", pageStatus, true);

            int row = 0;
            int start = session.Page * pageSize;
            for (int i = start; i < list.Count && row < pageSize; i++)
            {
                PlayerState state = list[i];
                string icon = state.Risk >= config.KickThreshold ? IconBan :
                              state.Risk >= config.WatchThreshold ? IconRisk : IconPlayer;
                string sub = "Risk " + state.Risk.ToString("0.0", CultureInfo.InvariantCulture) +
                             " | flags " + state.Flags +
                             " | " + FormatMinutes(state.PlaySeconds / 60.0) +
                             (state.Watched ? " | WATCHED" : String.Empty) +
                             (state.Exempt ? " | EXEMPT" : String.Empty);

                AppendServerAssistRow(builder, row++, "select|" + state.Id, state.Name, sub, true,
                    string.Equals(session.SelectedId, state.Id, StringComparison.OrdinalIgnoreCase),
                    icon, state.Risk.ToString("0", CultureInfo.InvariantCulture), RiskTint(state.Risk));
            }

            if (row == 0)
            {
                AppendServerAssistRow(builder, row++, String.Empty, "No players match",
                    string.IsNullOrEmpty(session.Search) ? "No online players." : "Try a different search.",
                    false, false, IconPlayer, "NONE", "#8CE99A");
            }

            bool hasSelection = !string.IsNullOrEmpty(session.SelectedId);
            AppendServerAssistButton(builder, 0, "refresh", "Refresh", true, "Reload the risk board", IconRefresh, String.Empty);
            AppendServerAssistButton(builder, 1, "tab|evidence", "Evidence", hasSelection,
                "Open the selected player's evidence", IconEvidence, hasSelection ? String.Empty : "#555555");
            AppendServerAssistButton(builder, 2, "tab|modules", "Modules", hasSelection,
                "Open the selected player's module breakdown", IconModules, hasSelection ? String.Empty : "#555555");
            AppendServerAssistButton(builder, 3, "page|prev", "Prev", session.Page > 0,
                "Previous page", IconPrev, session.Page > 0 ? String.Empty : "#555555");
            AppendServerAssistButton(builder, 4, "page|next", "Next", session.Page < totalPages - 1,
                "Next page", IconNext, session.Page < totalPages - 1 ? String.Empty : "#555555");
            AppendServerAssistButton(builder, 5, "home", "Plugins", true,
                "Return to the Server Assist plugin page", IconHome, String.Empty);
            AppendServerAssistButton(builder, 11, "close", "Close", true,
                "Close the Server Assist window", IconClose, String.Empty);

            return builder.ToString();
        }

        private string BuildEvidenceServerAssistView(IPlayer player, GuiSession session, string status)
        {
            PlayerState state = GetSelectedState(session);
            if (state == null)
            {
                return BuildNoSelectionServerAssistView(session, "evidence", status);
            }

            string pageStatus = status ?? String.Empty;
            if (pageStatus.Length > 0)
            {
                pageStatus += " | ";
            }
            pageStatus += state.Online ? "online" : "offline";
            pageStatus += " | " + state.Evidence.Count + (state.Evidence.Count == 1 ? " evidence record" : " evidence records");

            StringBuilder builder = BeginServerAssistView(session, "Evidence: " + state.Name,
                state.Id, pageStatus, false);

            int row = 0;
            AppendServerAssistRow(builder, row++, String.Empty, "Risk",
                state.Risk.ToString("0.0", CultureInfo.InvariantCulture) +
                " | p~" + (RiskProbability(state.Risk) * 100.0).ToString("0", CultureInfo.InvariantCulture) + "%" +
                " | session " + state.SessionRisk.ToString("0.0", CultureInfo.InvariantCulture) +
                " | peak " + state.PeakRisk.ToString("0.0", CultureInfo.InvariantCulture) +
                " | flags " + state.Flags,
                false, false, IconRisk, state.Risk.ToString("0", CultureInfo.InvariantCulture), RiskTint(state.Risk));

            AppendServerAssistRow(builder, row++, String.Empty, "Player",
                (state.Online ? "Online" : "Offline") +
                " | " + FormatMinutes(state.PlaySeconds / 60.0) +
                " | " + state.Samples + " samples" +
                (state.Watched ? " | WATCHED" : String.Empty) +
                (state.Exempt ? " | EXEMPT" : String.Empty),
                false, false, IconPlayer, state.Watched ? "WATCH" : "INFO", state.Watched ? "#FFD166" : "#8CE99A");

            int shown = 0;
            for (int i = state.Evidence.Count - 1; i >= 0 && row < 8; i--)
            {
                Evidence evidence = state.Evidence[i];
                AppendServerAssistRow(builder, row++, String.Empty,
                    evidence.Module + "/" + evidence.Code,
                    Shorten(evidence.Detail, 120),
                    false, false, ModuleIcon(evidence.Module),
                    evidence.Score.ToString("0", CultureInfo.InvariantCulture) + "%",
                    EvidenceTint(evidence));
                shown++;
            }

            if (shown == 0)
            {
                AppendServerAssistRow(builder, row++, String.Empty, "No evidence",
                    "No findings for this player.", false, false, IconEvidence, "EMPTY", "#8CE99A");
            }

            AppendEvidenceButtons(builder, state);
            return builder.ToString();
        }

        private string BuildModulesServerAssistView(IPlayer player, GuiSession session, string status)
        {
            PlayerState state = GetSelectedState(session);
            if (state == null)
            {
                return BuildNoSelectionServerAssistView(session, "modules", status);
            }

            string pageStatus = status ?? String.Empty;
            if (pageStatus.Length > 0)
            {
                pageStatus += " | ";
            }
            pageStatus += "risk " + state.Risk.ToString("0.0", CultureInfo.InvariantCulture) +
                          " | flags " + state.Flags;

            StringBuilder builder = BeginServerAssistView(session, "Modules: " + state.Name,
                state.Id, pageStatus, false);

            int row = 0;
            AppendServerAssistRow(builder, row++, String.Empty, "Risk",
                state.Risk.ToString("0.0", CultureInfo.InvariantCulture) +
                " | session " + state.SessionRisk.ToString("0.0", CultureInfo.InvariantCulture) +
                " | peak " + state.PeakRisk.ToString("0.0", CultureInfo.InvariantCulture),
                false, false, IconRisk, state.Risk.ToString("0", CultureInfo.InvariantCulture), RiskTint(state.Risk));

            AppendServerAssistRow(builder, row++, String.Empty, "Player",
                (state.Online ? "Online" : "Offline") +
                " | " + FormatMinutes(state.PlaySeconds / 60.0) +
                " | " + state.Flags + " flags" +
                (state.Watched ? " | WATCHED" : String.Empty) +
                (state.Exempt ? " | EXEMPT" : String.Empty),
                false, false, IconPlayer, state.Watched ? "WATCH" : "INFO", state.Watched ? "#FFD166" : "#8CE99A");

            string[] modules = { ModuleMovement, ModuleCombat, ModuleInventory, ModuleGrief, ModuleExploit, ModuleProgression };
            for (int i = 0; i < modules.Length && row < 8; i++)
            {
                string module = modules[i];
                double value;
                state.ModuleRisk.TryGetValue(module, out value);
                AppendServerAssistRow(builder, row++, String.Empty, ModuleLabel(module),
                    value > 0.0 ? "Active risk " + value.ToString("0.0", CultureInfo.InvariantCulture) : "No active risk",
                    false, false, ModuleIcon(module),
                    value.ToString("0.0", CultureInfo.InvariantCulture), ModuleTint(value));
            }

            AppendEvidenceButtons(builder, state);
            return builder.ToString();
        }

        private void AppendEvidenceButtons(StringBuilder builder, PlayerState state)
        {
            AppendServerAssistButton(builder, 0, "refresh", "Refresh", true, "Reload this view", IconRefresh, String.Empty);
            AppendServerAssistButton(builder, 1, "tab|risk", "Risk", true, "Return to the risk board", IconRisk, String.Empty);
            AppendServerAssistButton(builder, 2, "tab|modules", "Modules", true,
                "Open the module breakdown", IconModules, String.Empty);
            AppendServerAssistButton(builder, 3, "watch|" + state.Id, state.Watched ? "Unwatch" : "Watch", true,
                "Toggle staff watch", IconWatch, state.Watched ? "#FFD166" : String.Empty);
            AppendServerAssistButton(builder, 4, "clear|" + state.Id, "Clear", true,
                "Clear risk and evidence", IconClear, "#FFD166");
            AppendServerAssistButton(builder, 5, "export|" + state.Id, "Export", true,
                "Write an evidence report", IconExport, String.Empty);

            if (config.AllowGuiKick)
            {
                AppendServerAssistButton(builder, 6, "kick|" + state.Id, "Kick", state.Online,
                    "Kick this player", IconKick, state.Online ? String.Empty : "#555555");
            }

            if (config.AllowGuiBan)
            {
                AppendServerAssistButton(builder, 7, "ban|" + state.Id, "Ban", state.Online,
                    "Ban this player", IconBan, state.Online ? "#FF6B6B" : "#555555");
            }

            AppendServerAssistButton(builder, 10, "home", "Plugins", true,
                "Return to the Server Assist plugin page", IconHome, String.Empty);
            AppendServerAssistButton(builder, 11, "close", "Close", true,
                "Close the Server Assist window", IconClose, String.Empty);
        }

        private string BuildNoSelectionServerAssistView(GuiSession session, string tab, string status)
        {
            string title = String.Equals(tab, "modules", StringComparison.OrdinalIgnoreCase)
                ? "Modules"
                : "Evidence";
            StringBuilder builder = BeginServerAssistView(session, title, "No player selected", status, false);
            AppendServerAssistRow(builder, 0, "tab|risk", "Select a player",
                "Open the Risk tab and click a player card.", true, false, IconPlayer, "NONE", "#FFD166");
            AppendServerAssistButton(builder, 0, "refresh", "Refresh", true, "Reload this view", IconRefresh, String.Empty);
            AppendServerAssistButton(builder, 1, "tab|risk", "Risk", true, "Return to the risk board", IconRisk, String.Empty);
            AppendServerAssistButton(builder, 5, "home", "Plugins", true,
                "Return to the Server Assist plugin page", IconHome, String.Empty);
            AppendServerAssistButton(builder, 11, "close", "Close", true,
                "Close the Server Assist window", IconClose, String.Empty);
            return builder.ToString();
        }

        private string RiskTint(double risk)
        {
            if (risk >= config.KickThreshold) return "#FF6B6B";
            if (risk >= config.WatchThreshold) return "#FFD166";
            if (risk >= config.AlertThreshold) return "#FFA94D";
            return "#8CE99A";
        }

        private static string EvidenceTint(Evidence evidence)
        {
            double weighted = evidence == null ? 0.0 : evidence.Score * evidence.Confidence;
            if (weighted >= 60.0) return "#FF6B6B";
            if (weighted >= 35.0) return "#FFD166";
            return "#8CE99A";
        }

        private static string ModuleTint(double risk)
        {
            if (risk >= 60.0) return "#FF6B6B";
            if (risk >= 30.0) return "#FFD166";
            if (risk > 0.0) return "#FFA94D";
            return "#8CE99A";
        }

        private static string ModuleIcon(string module)
        {
            switch (module)
            {
                case ModuleMovement: return IconMovement;
                case ModuleCombat: return IconCombat;
                case ModuleInventory: return IconInventory;
                case ModuleGrief: return IconGrief;
                case ModuleExploit: return IconExploit;
                case ModuleProgression: return IconProgression;
                default: return IconEvidence;
            }
        }

        private static string ModuleLabel(string module)
        {
            switch (module)
            {
                case ModuleMovement: return "Movement";
                case ModuleCombat: return "Combat";
                case ModuleInventory: return "Inventory / economy";
                case ModuleGrief: return "Grief";
                case ModuleExploit: return "Exploit";
                case ModuleProgression: return "Progression";
                default: return module;
            }
        }

        private static string Shorten(string value, int max)
        {
            if (string.IsNullOrEmpty(value))
            {
                return String.Empty;
            }

            string text = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= max ? text : text.Substring(0, Math.Max(0, max - 1)) + "...";
        }

        private string BuildServerAssistErrorView(string message)
        {
            StringBuilder builder = new StringBuilder(512);
            builder.Append("v2\n");
            builder.Append("title=").Append(ServerAssistEncode("Sentinel AC")).Append('\n');
            builder.Append("subtitle=").Append(ServerAssistEncode("Unavailable")).Append('\n');
            builder.Append("status=").Append(ServerAssistEncode(message)).Append('\n');
            builder.Append("background=").Append(ServerAssistEncode(config != null && config.ServerAssist != null &&
                !string.IsNullOrEmpty(config.ServerAssist.Icon) ? config.ServerAssist.Icon : IconPanel)).Append('\n');
            builder.Append("row=0||No data|").Append(ServerAssistEncode(message)).Append("|0|0|")
                .Append(ServerAssistEncode(IconRisk)).Append("|ERROR|#FF6B6B\n");
            builder.Append("btn=0|refresh|Refresh|1|Reload this panel|").Append(ServerAssistEncode(IconRefresh)).Append("|\n");
            builder.Append("btn=1|home|Plugins|1|Return to the Server Assist plugin page|")
                .Append(ServerAssistEncode(IconHome)).Append("|\n");
            builder.Append("btn=2|close|Close|1|Close the Server Assist window\n");
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

        private static string Decode(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return String.Empty;
            }

            return value.Replace("%0A", "\n").Replace("%7C", "|").Replace("%25", "%");
        }

        #endregion

        #region Helpers

        private static World GetWorld()
        {
            GameManager manager = GameManager.Instance;
            return manager == null ? null : manager.World;
        }

        private PlayerState GetOrCreateState(ClientInfo clientInfo)
        {
            string id = GetPlayerId(clientInfo);
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            PlayerState state;
            if (!states.TryGetValue(id, out state) || state == null)
            {
                state = new PlayerState
                {
                    Id = id,
                    FirstSeenUtc = DateTime.UtcNow
                };
                states[id] = state;

                StoredProfile profile;
                if (storedData != null && storedData.Players != null &&
                    storedData.Players.TryGetValue(id, out profile) && profile != null)
                {
                    ApplyStoredProfile(state, profile);
                }
            }

            if (clientInfo != null && !string.IsNullOrEmpty(clientInfo.playerName))
            {
                state.Name = clientInfo.playerName;
            }

            return state;
        }

        private PlayerState GetPlayerState(EntityPlayer entity)
        {
            if (entity == null)
            {
                return null;
            }

            ClientInfo clientInfo = GetClientInfo(entity);
            if (clientInfo != null)
            {
                return GetOrCreateState(clientInfo);
            }

            string id = entity.PersistentPlayerData != null && entity.PersistentPlayerData.PrimaryId != null
                ? entity.PersistentPlayerData.PrimaryId.ReadablePlatformUserIdentifier
                : null;

            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            PlayerState state;
            if (!states.TryGetValue(id, out state) || state == null)
            {
                state = new PlayerState
                {
                    Id = id,
                    Name = entity.PlayerDisplayName ?? "unknown",
                    FirstSeenUtc = DateTime.UtcNow
                };
                states[id] = state;
            }

            return state;
        }

        private static string GetPlayerId(ClientInfo clientInfo)
        {
            if (clientInfo == null)
            {
                return String.Empty;
            }

            try
            {
                if (clientInfo.PlatformId != null)
                {
                    string id = clientInfo.PlatformId.ReadablePlatformUserIdentifier;
                    if (!string.IsNullOrEmpty(id))
                    {
                        return id;
                    }
                }

                if (clientInfo.InternalId != null)
                {
                    string id = clientInfo.InternalId.ReadablePlatformUserIdentifier;
                    if (!string.IsNullOrEmpty(id))
                    {
                        return id;
                    }
                }
            }
            catch
            {
                // A client can be partially initialized during login.
            }

            return clientInfo.playerName ?? String.Empty;
        }

        private static ClientInfo GetClientInfo(EntityPlayer entity)
        {
            if (entity == null || ConnectionManager.Instance == null || ConnectionManager.Instance.Clients == null)
            {
                return null;
            }

            return ConnectionManager.Instance.Clients.ForEntityId(entity.entityId);
        }

        private bool IsExempt(PlayerState player, ClientInfo clientInfo)
        {
            if (player == null)
            {
                return true;
            }

            string exemptPermission = config == null || string.IsNullOrEmpty(config.ExemptPermission)
                ? PermExempt
                : config.ExemptPermission;

            if (permission.UserHasPermission(player.Id, PermExempt))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(exemptPermission) &&
                !string.Equals(exemptPermission, PermExempt, StringComparison.OrdinalIgnoreCase) &&
                permission.UserHasPermission(player.Id, exemptPermission))
            {
                return true;
            }

            if (config.ExemptAdmins && clientInfo != null)
            {
                try
                {
                    GameManager manager = GameManager.Instance;
                    if (manager != null && manager.adminTools != null && manager.adminTools.Users != null &&
                        manager.adminTools.Users.HasEntry(clientInfo))
                    {
                        return true;
                    }
                }
                catch
                {
                    // The admin tool list can be partially initialized during login.
                }
            }

            return false;
        }

        private bool IsAdmin(IPlayer player)
        {
            return player != null && (player.IsAdmin || player.HasPermission(PermAdmin));
        }

        private static bool IsHeadshot(DamageSource source, EntityAlive victim)
        {
            if (source == null)
            {
                return false;
            }

            if (source.bodyParts != EnumBodyPartHit.None && (source.bodyParts & EnumBodyPartHit.Head) != 0)
            {
                return true;
            }

            try
            {
                return victim != null && source.GetEntityDamageBodyPart(victim) == EnumBodyPartHit.Head;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsMeleeWeapon(ItemValue itemValue)
        {
            if (itemValue == null)
            {
                return false;
            }

            try
            {
                ItemClass itemClass = itemValue.ItemClass;
                if (itemClass == null || itemClass.Actions == null)
                {
                    return false;
                }

                for (int i = 0; i < itemClass.Actions.Length; i++)
                {
                    if (itemClass.Actions[i] is ItemActionDynamicMelee)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static bool IsMagazineWeapon(ItemValue itemValue)
        {
            if (itemValue == null)
            {
                return false;
            }

            try
            {
                ItemClass itemClass = itemValue.ItemClass;
                if (itemClass == null || itemClass.Actions == null)
                {
                    return false;
                }

                for (int i = 0; i < itemClass.Actions.Length; i++)
                {
                    ItemActionAttack attack = itemClass.Actions[i] as ItemActionAttack;
                    if (attack != null && attack.UsesMagazines)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static string GetItemName(ItemValue itemValue)
        {
            if (itemValue == null)
            {
                return String.Empty;
            }

            try
            {
                ItemClass itemClass = itemValue.ItemClass;
                return itemClass == null ? String.Empty : itemClass.GetItemName();
            }
            catch
            {
                return String.Empty;
            }
        }

        private static bool LineBlocked(Vector3 from, Vector3 to)
        {
            World world = GetWorld();
            if (world == null)
            {
                return false;
            }

            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.2f)
            {
                return false;
            }

            Vector3 direction = delta / distance;
            for (float travelled = 0.6f; travelled < distance - 0.5f; travelled += 0.6f)
            {
                Vector3 point = from + direction * travelled;
                BlockValue value = world.GetBlock(World.worldToBlockPos(point));
                if (value.isair || value.Block == null)
                {
                    continue;
                }

                Block block = value.Block;
                if (block.bIsPlant || block.blockMaterial == null || !block.blockMaterial.IsCollidable ||
                    block.blockMaterial.IsGroundCover)
                {
                    continue;
                }

                string name = block.blockName ?? String.Empty;
                if (name.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("window", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("bars", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("door", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("hatch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("gate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("elevator", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("ladder", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("stairs", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("fence", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("railing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("truss", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("grate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("chainlink", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private void ApplyStoredProfile(PlayerState player, StoredProfile profile)
        {
            if (player == null || profile == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(player.Name))
            {
                player.Name = profile.Name;
            }

            player.PeakRisk = Math.Max(player.PeakRisk, profile.PeakRisk);
            player.Flags = Math.Max(player.Flags, profile.Flags);

            if (profile.Stats == null)
            {
                return;
            }

            MergeStat(player.SpeedStat, profile.Stats, "speed");
            MergeStat(player.VerticalStat, profile.Stats, "vertical");
            MergeStat(player.KillRateStat, profile.Stats, "killrate");
            MergeStat(player.XpRateStat, profile.Stats, "xprate");
            MergeStat(player.WealthStat, profile.Stats, "wealth");
        }

        private static void MergeStat(RunningStat target, Dictionary<string, RunningStat> source, string key)
        {
            if (target == null || source == null || target.Count > 0)
            {
                return;
            }

            RunningStat value;
            if (!source.TryGetValue(key, out value) || value == null)
            {
                return;
            }

            target.Count = value.Count;
            target.Mean = value.Mean;
            target.M2 = value.M2;
            target.Min = value.Min;
            target.Max = value.Max;
            target.EwmaMean = value.EwmaMean;
            target.EwmaVar = value.EwmaVar;
            target.HasEwma = value.HasEwma;
        }

        private void Reply(IPlayer player, string message)
        {
            if (player == null || player.IsServer)
            {
                Interface.Oxide.LogInfo("[SentinelAC] " + message);
                return;
            }

            player.Reply(Format(message));
        }

        private string Format(string message)
        {
            return config == null || string.IsNullOrEmpty(config.ChatPrefix) ? message : config.ChatPrefix + " " + message;
        }

        private void LogHookError(string hook, Exception exception)
        {
            LogRuntimeError("hook " + hook, exception);
        }

        private void LogRuntimeError(string context, Exception exception)
        {
            string message = exception == null ? "unknown error" : exception.GetType().Name + ": " + exception.Message;
            if (config != null && config.Debug)
            {
                Interface.Oxide.LogException("[SentinelAC] " + context, exception);
                return;
            }

            if (!string.Equals(lastRuntimeError, message, StringComparison.Ordinal))
            {
                lastRuntimeError = message;
                Interface.Oxide.LogWarning("[SentinelAC] " + context + " failed: " + message);
            }
        }

        private static long PackPosition(Vector3i position)
        {
            return ((long)(position.x & 0x1FFFFF) << 42) |
                   ((long)(position.y & 0x1FFFFF) << 21) |
                   (long)(position.z & 0x1FFFFF);
        }

        private static string SafeFileName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "unknown";
            }

            StringBuilder builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')
                {
                    builder.Append(c);
                }
                else
                {
                    builder.Append('_');
                }
            }
            return builder.ToString();
        }

        private static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return String.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length + 16);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 32)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }
            return builder.ToString();
        }

        private static string FormatMinutes(double minutes)
        {
            if (minutes < 1.0)
            {
                return "<1m";
            }

            if (minutes < 60.0)
            {
                return minutes.ToString("0", CultureInfo.InvariantCulture) + "m";
            }

            double hours = minutes / 60.0;
            return hours.ToString("0.#", CultureInfo.InvariantCulture) + "h";
        }

        private static double RiskProbability(double risk)
        {
            return 1.0 / (1.0 + Math.Exp(-(risk - 60.0) / 12.0));
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        #endregion

        #region Public API

        public double GetRisk(string playerId)
        {
            PlayerState state = ResolveState(playerId);
            return state == null ? 0.0 : state.Risk;
        }

        public int GetFlags(string playerId)
        {
            PlayerState state = ResolveState(playerId);
            return state == null ? 0 : state.Flags;
        }

        public bool IsWatched(string playerId)
        {
            PlayerState state = ResolveState(playerId);
            return state != null && state.Watched;
        }

        public string GetEvidenceSummary(string playerId)
        {
            PlayerState state = ResolveState(playerId);
            if (state == null)
            {
                return String.Empty;
            }

            StringBuilder builder = new StringBuilder(1024);
            builder.Append(state.Name).Append(" risk=").Append(state.Risk.ToString("0.0", CultureInfo.InvariantCulture));
            int start = Math.Max(0, state.Evidence.Count - 10);
            for (int i = start; i < state.Evidence.Count; i++)
            {
                Evidence evidence = state.Evidence[i];
                builder.Append("\n").Append(evidence.Code).Append(" (")
                    .Append(evidence.Score.ToString("0", CultureInfo.InvariantCulture)).Append("): ")
                    .Append(evidence.Detail);
            }
            return builder.ToString();
        }

        public void AddExternalEvidence(string playerId, string module, string code, double score, double confidence, string detail)
        {
            PlayerState state = ResolveState(playerId);
            if (state == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(module))
            {
                module = "external";
            }
            if (string.IsNullOrEmpty(code))
            {
                code = "EXT_EVIDENCE";
            }

            AddEvidence(state, module, code, score, confidence, detail);
        }

        #endregion

        #region Data classes

        private sealed class RunningStat
        {
            public long Count;
            public double Mean;
            public double M2;
            public double Min = double.MaxValue;
            public double Max = double.MinValue;
            public double EwmaMean;
            public double EwmaVar;
            public bool HasEwma;

            public double StdDev
            {
                get { return Count > 1 ? Math.Sqrt(M2 / (Count - 1)) : 0.0; }
            }

            public void Add(double value)
            {
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    return;
                }

                Count++;
                double delta = value - Mean;
                Mean += delta / Count;
                M2 += delta * (value - Mean);

                if (value < Min) Min = value;
                if (value > Max) Max = value;

                if (!HasEwma)
                {
                    EwmaMean = value;
                    EwmaVar = 0.0;
                    HasEwma = true;
                }
                else
                {
                    const double alpha = 0.1;
                    double difference = value - EwmaMean;
                    EwmaMean += alpha * difference;
                    EwmaVar = (1.0 - alpha) * (EwmaVar + alpha * difference * difference);
                }
            }

            public double Z(double value)
            {
                double deviation = StdDev;
                return deviation > 0.000001 ? (value - Mean) / deviation : 0.0;
            }

            public double EwmaZ(double value)
            {
                double deviation = Math.Sqrt(Math.Max(0.0, EwmaVar));
                return deviation > 0.000001 ? (value - EwmaMean) / deviation : 0.0;
            }
        }

        private sealed class RateWindow
        {
            private readonly double windowSeconds;
            private readonly Queue<RateEntry> entries = new Queue<RateEntry>();

            public RateWindow(double windowSeconds)
            {
                this.windowSeconds = Math.Max(1.0, windowSeconds);
            }

            public void Add(double value, DateTime utc)
            {
                entries.Enqueue(new RateEntry { Value = value, Utc = utc });
                Trim(utc);
            }

            public double Sum(DateTime utc)
            {
                Trim(utc);
                double sum = 0.0;
                foreach (RateEntry entry in entries)
                {
                    sum += entry.Value;
                }
                return sum;
            }

            public int Count(DateTime utc)
            {
                Trim(utc);
                return entries.Count;
            }

            public void Clear()
            {
                entries.Clear();
            }

            private void Trim(DateTime utc)
            {
                while (entries.Count > 0 && (utc - entries.Peek().Utc).TotalSeconds > windowSeconds)
                {
                    entries.Dequeue();
                }
            }
        }

        private sealed class RateEntry
        {
            public double Value;
            public DateTime Utc;
        }

        private sealed class PositionSample
        {
            public DateTime Utc;
            public Vector3 Position;
            public bool OnGround;
            public float Health;
        }

        private sealed class BlockSnapshot
        {
            public ushort Type;
            public int Damage;
            public bool IsAir;
        }

        private sealed class InventoryItem
        {
            public int Type;
            public string Name;
            public int Count;
            public ushort Quality;
            public double ValueFactor = 1.0;
        }

        private sealed class DeathRecord
        {
            public DateTime Utc;
            public string KillerId;
            public Vector3i Position;
        }

        private sealed class Evidence
        {
            public string Module;
            public string Code;
            public double Score;
            public double Confidence;
            public string Detail;
            public DateTime TimeUtc;
            public double RiskAfter;
        }

        private sealed class GuiSession
        {
            public string Tab = "risk";
            public int Page;
            public string Search = String.Empty;
            public string SelectedId = String.Empty;
        }

        private sealed class PlayerState
        {
            public string Id;
            public string Name;
            public int EntityId = -1;
            public DateTime FirstSeenUtc;
            public DateTime LastSeenUtc;
            public DateTime JoinUtc;
            public DateTime LastTickUtc;
            public DateTime LastMovementSampleUtc;
            public DateTime LastProgressionSampleUtc;
            public DateTime LastInventorySampleUtc;
            public DateTime LastGriefSampleUtc;
            public DateTime LastExploitSampleUtc;
            public DateTime SpawnGraceUntilUtc;
            public DateTime LastAlertUtc;
            public DateTime NextWebhookUtc;
            public DateTime NextActionUtc;
            public DateTime HighRiskSinceUtc;
            public DateTime LastHealthUtc;
            public DateTime LastWealthChangeUtc;
            public DateTime TeleportWindowStartUtc;
            public double Risk;
            public double SessionRisk;
            public double PeakRisk;
            public double PlaySeconds;
            public long Samples;
            public int Flags;
            public bool Online;
            public bool Exempt;
            public bool Watched;
            public int TeleportCount;
            public int ConsecutiveHighSpeed;
            public int ConsecutiveAir;
            public int SolidTicks;
            public bool Falling;
            public double FallStartY;
            public float LastHealth;
            public string LastWeapon = String.Empty;
            public string LastAttackerId = String.Empty;
            public DateTime LastHitUtc;
            public int LastVictimId = -1;
            public int LastLevel;
            public int HitCountWindow;
            public int HeadshotCountWindow;
            public double LastWealth;
            public RunningStat SpeedStat = new RunningStat();
            public RunningStat VerticalStat = new RunningStat();
            public RunningStat KillRateStat = new RunningStat();
            public RunningStat XpRateStat = new RunningStat();
            public RunningStat WealthStat = new RunningStat();
            public readonly List<PositionSample> PositionHistory = new List<PositionSample>();
            public readonly List<Evidence> Evidence = new List<Evidence>();
            public readonly List<DeathRecord> Deaths = new List<DeathRecord>();
            public readonly Dictionary<string, DateTime> LastEvidenceAt = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, double> ModuleRisk = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, int> KillCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<long, BlockSnapshot> BlockSnapshot = new Dictionary<long, BlockSnapshot>();
            public Dictionary<int, InventoryItem> InventoryItems = new Dictionary<int, InventoryItem>();
            public readonly RateWindow XpWindow = new RateWindow(300.0);
            public readonly RateWindow KillWindow = new RateWindow(60.0);
            public readonly RateWindow DamageWindow = new RateWindow(1.0);
            public readonly RateWindow DamageTakenWindow = new RateWindow(10.0);
            public readonly RateWindow GriefWindow = new RateWindow(300.0);
            public readonly RateWindow VehicleSpawnWindow = new RateWindow(30.0);
        }

        private sealed class StoredData
        {
            public Dictionary<string, StoredProfile> Players = new Dictionary<string, StoredProfile>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, RunningStat> GlobalStats = new Dictionary<string, RunningStat>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class StoredProfile
        {
            public string Name = String.Empty;
            public DateTime LastSeenUtc;
            public double PeakRisk;
            public int Flags;
            public Dictionary<string, RunningStat> Stats = new Dictionary<string, RunningStat>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class PlayerExport
        {
            public string Id;
            public string Name;
            public double Risk;
            public double SessionRisk;
            public double PeakRisk;
            public int Flags;
            public double PlayMinutes;
            public Dictionary<string, int> Kills;
            public Dictionary<string, double> Modules;
            public List<Evidence> Evidence;
        }

        private sealed class Configuration
        {
            public string ChatPrefix = "[Sentinel]";
            public bool Debug = false;
            public bool DryRun = true;
            public bool ExemptAdmins = true;
            public string ExemptPermission = "sentinelac.exempt";
            public double ScanIntervalSeconds = 1.0;
            public int MinSamples = 15;
            public double MinPlayMinutes = 5.0;
            public double Sensitivity = 1.0;
            public double MaxContributionNormal = 40.0;
            public double MaxContributionCritical = 70.0;
            public double CriticalScoreThreshold = 95.0;
            public double RiskDecayHalfLifeMinutes = 30.0;
            public double AlertThreshold = 40.0;
            public double WatchThreshold = 60.0;
            public double KickThreshold = 90.0;
            public double BanThreshold = 97.0;
            public bool AutoWatch = true;
            public bool AutoKick = false;
            public bool AutoBan = false;
            public double ActionSustainSeconds = 30.0;
            public double ActionCooldownMinutes = 15.0;
            public double AlertCooldownSeconds = 15.0;
            public double EvidenceRetentionMinutes = 120.0;
            public double EvidenceCooldownSeconds = 10.0;
            public double DefaultBanHours = 24.0;
            public bool AllowGuiKick = false;
            public bool AllowGuiBan = false;

            public MovementSettings Movement = new MovementSettings();
            public CombatSettings Combat = new CombatSettings();
            public InventorySettings Inventory = new InventorySettings();
            public GriefSettings Grief = new GriefSettings();
            public ExploitSettings Exploit = new ExploitSettings();
            public ProgressionSettings Progression = new ProgressionSettings();
            public WebhookSettings Webhook = new WebhookSettings();
            public ServerAssistSettings ServerAssist = new ServerAssistSettings();
        }

        private sealed class MovementSettings
        {
            public bool Enabled = true;
            public double SampleSeconds = 1.0;
            public double ZThreshold = 5.0;
            public double HardSpeed = 18.0;
            public double MaxUpwardSpeed = 8.0;
            public double MaxAirSeconds = 5.0;
            public double MaxTeleportDistance = 60.0;
            public int MaxTeleportsPerWindow = 6;
            public double TeleportWindowMinutes = 10.0;
            public double SpawnGraceSeconds = 10.0;
            public bool DetectNoClip = true;
            public int NoClipSamples = 3;
            public double MinWorldY = -5.0;
            public bool DetectFallBypass = false;
            public double MinFallDistance = 12.0;
            public double MinFallDamageLoss = 2.0;
        }

        private sealed class CombatSettings
        {
            public bool Enabled = true;
            public double MaxMeleeReach = 5.0;
            public double MaxHitsPerSecond = 15.0;
            public double HeadshotRatioThreshold = 0.9;
            public int MinHeadshots = 30;
            public double ThroughWallDistance = 10.0;
            public bool DetectEmptyMagazineShots = true;
            public double KillRateZThreshold = 5.0;
            public double GlobalZThreshold = 7.0;
            public int MinKillsForRate = 5;
        }

        private sealed class InventorySettings
        {
            public bool Enabled = true;
            public double SampleSeconds = 5.0;
            public int MaxLegalQuality = 6;
            public double MaxHighValueGainPerMinute = 6.0;
            public double WealthZThreshold = 5.0;
            public double GlobalZThreshold = 7.0;
            public int MaxIdenticalSignatures = 3;
            public bool DetectInvalidItems = true;
            public bool DetectStackOverflow = true;
            public bool DetectQualityOverflow = true;
        }

        private sealed class GriefSettings
        {
            public bool Enabled = true;
            public double SampleSeconds = 2.0;
            public int Radius = 5;
            public int VerticalRadius = 4;
            public double AttributionDistance = 8.0;
            public double MaxChangesPerMinute = 90.0;
            public int MaxChangesPerSample = 20;
            public int ExplosionChangeThreshold = 40;
            public bool ProtectClaims = true;
            public bool ProtectLandClaimBlocks = true;
            public bool FlagTraderArea = true;
            public bool RequireLineOfSight = true;
        }

        private sealed class ExploitSettings
        {
            public bool Enabled = true;
            public double SampleSeconds = 2.0;
            public bool DetectGodMode = true;
            public bool DetectStatOverflow = true;
            public bool DetectJetpack = true;
            public bool DetectVehicleSpeed = true;
            public bool DetectDamageImmunity = true;
            public int DamageImmunityHits = 12;
            public double StatOverflowFactor = 1.25;
            public double MaxVehicleSpeed = 60.0;
        }

        private sealed class ProgressionSettings
        {
            public bool Enabled = true;
            public double MaxXpPerMinute = 20000.0;
            public double XpZThreshold = 5.0;
            public double GlobalZThreshold = 7.0;
            public double MinXpPerMinute = 500.0;
        }

        private sealed class WebhookSettings
        {
            public bool Enabled = false;
            public string Url = String.Empty;
            public string Username = "SentinelAC";
            public double MinRisk = 55.0;
            public double MinIntervalSeconds = 60.0;
            public bool IncludeEvidence = true;
        }

        private sealed class ServerAssistSettings
        {
            public bool Enabled = true;
            public string PanelId = "sentinelac";
            public string Icon = "sprite:sa_nav_moderation";
            public int Order = 30;
        }

        #endregion
    }
}
