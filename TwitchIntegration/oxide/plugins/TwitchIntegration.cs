using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("TwitchIntegration", "AAZ", "0.2.1")]
    [Description("Twitch channel linking, permission tiers, and streamer-picked reward shortlists rendered through Server Assist.")]
    public class TwitchIntegration : CovalencePlugin
    {
        #region Constants

        private const string ServerAssistPluginName = "ServerAssist";
        private const string PanelId = "twitch";
        private const string DataFileName = "TwitchIntegration/players";
        private const int UiRows = 10;
        private const int UiButtons = 12;
        private const int ProcessedHistoryLimit = 2000;
        private const int AuditLimit = 200;

        private const string PermUse = "twitch.use";
        private const string PermStreamer = "twitch.streamer";
        private const string PermAdmin = "twitch.admin";

        // Logical icon roles -> ServerAssistArt sprite keys. Staff can remap
        // any of these in the "Icons" section of the config without rebuilding.
        private static readonly Dictionary<string, string> DefaultIconMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Panel", "sprite:sa_stream_panel" },
                { "Home", "sprite:sa_stream_home" },
                { "Tier", "ui_game_symbol_star" },
                { "Tier1", "sprite:sa_stream_tier_1" },
                { "Tier2", "sprite:sa_stream_tier_2" },
                { "Tier3", "sprite:sa_stream_tier_3" },
                { "Buff", "ui_game_symbol_twitch_buffs" },
                { "Debuff", "ui_game_symbol_twitch_debuffs" },
                { "Reward", "sprite:sa_stream_reward" },
                { "Loadout", "sprite:sa_stream_loadout" },
                { "LoadoutArmed", "sprite:sa_stream_loadout_armed" },
                { "LoadoutEmpty", "sprite:sa_stream_loadout_empty" },
                { "LoadoutLocked", "ui_game_symbol_lock" },
                { "Catalog", "sprite:sa_stream_catalog" },
                { "Info", "sprite:sa_nav_help" },
                { "Link", "sprite:sa_stream_link" },
                { "LinkPending", "sprite:sa_stream_link_pending" },
                { "Linked", "sprite:sa_stream_linked" },
                { "Unlink", "sprite:sa_stream_unlink" },
                { "LinkCancel", "sprite:sa_stream_link_cancel" },
                { "SlotBuff", "sprite:sa_stream_slot_buff" },
                { "SlotDebuff", "sprite:sa_stream_slot_debuff" },
                { "SlotReward", "sprite:sa_stream_slot_reward" },
                { "Admin", "sprite:sa_admin_suite" },
                { "AdminReload", "sprite:sa_plugin_hot_reload" },
                { "AdminReconcile", "sprite:sa_plugin_hot_reload" },
                { "AdminLinks", "sprite:sa_permission_gui" },
                { "AdminPlayers", "sprite:sa_nav_players" },
                { "Audit", "sprite:sa_nav_logs" },
                { "Relay", "sprite:sa_nav_server" },
                { "RelayOnline", "sprite:sa_nav_server" },
                { "RelayOffline", "sprite:sa_nav_server" },
                { "RelayPending", "sprite:sa_nav_server" },
                { "Test", "sprite:sa_announce_test" },
                { "TestBits", "item:casinoCoin" },
                { "TestSub", "ui_game_symbol_star" },
                { "TestPoints", "item:casinoCoin" },
                { "StatusArmed", "ui_game_symbol_check" },
                { "StatusPaused", "ui_game_symbol_clock" },
                { "StatusOptedOut", "ui_game_symbol_x" },
                { "TriggerSupport", "sprite:sa_stream_trigger_support" },
                { "TriggerSub", "sprite:sa_stream_trigger_sub" },
                { "TriggerResub", "sprite:sa_stream_trigger_resub" },
                { "TriggerGiftSub", "sprite:sa_stream_trigger_gift_sub" },
                { "TriggerPoints", "sprite:sa_stream_trigger_points" },
                { "TriggerAutoReward", "sprite:sa_stream_trigger_auto_reward" },
                { "TriggerRaid", "sprite:sa_stream_trigger_raid" },
                { "TriggerFollow", "sprite:sa_stream_trigger_follow" },
                { "TriggerMomentum", "sprite:sa_stream_trigger_momentum" },
                { "TriggerChat", "sprite:sa_stream_trigger_chat" },
                { "TriggerAny", "sprite:sa_stream_trigger_any" },
                { "TriggerCustom", "sprite:sa_stream_trigger_custom" },
                { "Check", "ui_game_symbol_check" },
                { "Cross", "ui_game_symbol_x" },
                { "Lock", "ui_game_symbol_lock" },
                { "Help", "sprite:sa_nav_help" },
                { "Prev", "ui_game_symbol_arrow_left" },
                { "Next", "ui_game_symbol_arrow_right" },
                { "Refresh", "ui_game_symbol_refresh" },
                { "Back", "ui_game_symbol_arrow_left" }
            };

        // Roles that prefer a live icon from another Server Assist panel or
        // provider. If the plugin/panel is not loaded, DefaultIconMap is used.
        private static readonly Dictionary<string, string> DefaultLiveIconMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Admin", "plugin:AdminSuite" },
                { "AdminReload", "plugin:PluginHotReload" },
                { "AdminLinks", "plugin:PermissionGui" }
            };

        // Streaming-specific role values used by the 0.2.0 defaults. ConfigVersion 3
        // moves these roles to the live Server Assist hooks or existing mod art.
        private static readonly Dictionary<string, string> LegacyRoleIcons =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Admin", "sprite:sa_stream_admin" },
                { "AdminReload", "sprite:sa_stream_admin_reload" },
                { "AdminReconcile", "sprite:sa_stream_admin_reconcile" },
                { "AdminLinks", "sprite:sa_stream_admin_links" },
                { "AdminPlayers", "sprite:sa_stream_admin_players" },
                { "Audit", "sprite:sa_stream_audit" },
                { "Relay", "sprite:sa_stream_relay" },
                { "RelayOnline", "sprite:sa_stream_relay_online" },
                { "RelayOffline", "sprite:sa_stream_relay_offline" },
                { "RelayPending", "sprite:sa_stream_relay_pending" },
                { "Tier", "sprite:sa_stream_tier" },
                { "Buff", "sprite:sa_stream_buff" },
                { "Debuff", "sprite:sa_stream_debuff" },
                { "Info", "sprite:sa_stream_info" },
                { "Help", "sprite:sa_stream_info" },
                { "LoadoutLocked", "sprite:sa_stream_loadout_locked" },
                { "Check", "sprite:sa_stream_status_armed" },
                { "Cross", "sprite:sa_stream_status_opted_out" },
                { "Lock", "sprite:sa_stream_loadout_locked" },
                { "StatusArmed", "sprite:sa_stream_status_armed" },
                { "StatusPaused", "sprite:sa_stream_status_paused" },
                { "StatusOptedOut", "sprite:sa_stream_status_opted_out" },
                { "Test", "sprite:sa_stream_test" },
                { "TestBits", "sprite:sa_stream_test_bits" },
                { "TestSub", "sprite:sa_stream_test_sub" },
                { "TestPoints", "sprite:sa_stream_test_points" }
            };

        // One-time migration map for the original example effect IDs. Only
        // legacy placeholder icons are replaced; custom icons are preserved.
        private static readonly Dictionary<string, string> LegacyEffectIcons =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "fast", "sprite:sa_stream_buff_quick_feet" },
                { "regen", "sprite:sa_stream_buff_regen" },
                { "looter", "sprite:sa_stream_buff_looter" },
                { "melee_boost", "sprite:sa_stream_buff_melee_surge" },
                { "range_boost", "sprite:sa_stream_buff_ranged_surge" },
                { "infinite_ammo", "sprite:sa_stream_buff_infinite_ammo" },
                { "toughness", "sprite:sa_stream_buff_toughness" },
                { "xp_gain", "sprite:sa_stream_buff_xp_gain" },
                { "pack_mule", "sprite:sa_stream_buff_pack_mule" },
                { "slow", "sprite:sa_stream_debuff_heavy_legs" },
                { "blur", "sprite:sa_stream_debuff_blurred_vision" },
                { "no_run", "sprite:sa_stream_debuff_no_running" },
                { "no_melee", "sprite:sa_stream_debuff_no_melee" },
                { "no_ranged", "sprite:sa_stream_debuff_no_ranged" },
                { "encumbered", "sprite:sa_stream_debuff_encumbered" },
                { "tougher_enemies", "sprite:sa_stream_debuff_tougher_enemies" },
                { "double_spawns", "sprite:sa_stream_debuff_double_spawns" },
                { "no_healing", "sprite:sa_stream_debuff_no_healing" }
            };

        // Reward art used by the 0.2.0 defaults. ConfigVersion 3 migrates these
        // back to the base-game/mod item icon wherever one exists.
        private static readonly Dictionary<string, string> LegacyStreamRewardIcons =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "dukes_100", "sprite:sa_stream_reward_dukes" },
                { "bandages_3", "sprite:sa_stream_reward_bandages" },
                { "ammo_9mm_30", "sprite:sa_stream_reward_ammo_9mm" },
                { "steel_50", "sprite:sa_stream_reward_forged_steel" },
                { "steel_200", "sprite:sa_stream_reward_forged_steel" },
                { "ammo_762_40", "sprite:sa_stream_reward_ammo_762" },
                { "mega_crush_2", "sprite:sa_stream_reward_mega_crush" },
                { "ammo_762_ap_60", "sprite:sa_stream_reward_ammo_762_ap" },
                { "medkit_3", "sprite:sa_stream_reward_medkit" }
            };

        // Original item icons used by the example rewards. Only these exact
        // values are migrated; a custom item icon is preserved.
        private static readonly Dictionary<string, string> LegacyItemIcons =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "dukes_100", "item:casinoCoin" },
                { "bandages_3", "item:medicalFirstAidBandage" },
                { "ammo_9mm_30", "item:ammo9mmBulletBall" },
                { "steel_50", "item:resourceForgedSteel" },
                { "ammo_762_40", "item:ammo762mmBulletBall" },
                { "mega_crush_2", "item:drinkCanMegaCrush" },
                { "steel_200", "item:resourceForgedSteel" },
                { "ammo_762_ap_60", "item:ammo762mmBulletAP" },
                { "medkit_3", "item:medicalFirstAidKit" }
            };

        #endregion

        #region Fields

        private Configuration config;
        private StoredData data;
        private Plugin serverAssist;
        private Timer pollTimer;
        private Timer linkTimer;

        private readonly Dictionary<string, PanelSession> panelSessions =
            new Dictionary<string, PanelSession>(StringComparer.Ordinal);

        private readonly HashSet<string> processedEventIds =
            new HashSet<string>(StringComparer.Ordinal);

        private readonly List<string> processedOrder = new List<string>();
        private readonly HashSet<string> linkPolling = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> warnedPermissions = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<long> recentEffects = new List<long>();
        private readonly System.Random random = new System.Random(Environment.TickCount);

        private bool pollingEvents;
        private bool relayAvailable;
        private string lastRelayError = string.Empty;
        private int pendingEventCount;
        private long lastRelayWarningUtc;
        private long lastPollStartedUtc;

        #endregion

        #region Lifecycle

        private void Init()
        {
            LoadConfig();
            LoadData();
            RegisterConfiguredPermissions();
            TryRegisterServerAssistPanel();

            // Server Assist may load after this plugin; this is a harmless fallback.
            timer.Once(2f, TryRegisterServerAssistPanel);
        }

        private void OnServerInitialized()
        {
            GrantDefaultPermissions();

            if (pollTimer == null)
            {
                pollTimer = timer.Every(Math.Max(1f, config.PollIntervalSeconds), PollRelayEvents);
            }

            if (linkTimer == null)
            {
                linkTimer = timer.Every(Math.Max(2f, config.LinkPollIntervalSeconds), PollLinkSessions);
            }

            foreach (IPlayer player in players.Connected)
            {
                ApplyPendingEffects(player);
            }

            if (!string.IsNullOrEmpty(config.ServerKey))
            {
                Puts("Relay polling enabled for server '" + config.ServerId + "'.");
            }
            else
            {
                PrintWarning("ServerKey is empty. Set it in oxide/config/TwitchIntegration.json to match the relay.");
            }
        }

        private void Unload()
        {
            if (pollTimer != null)
            {
                timer.Destroy(ref pollTimer);
            }

            if (linkTimer != null)
            {
                timer.Destroy(ref linkTimer);
            }

            SaveData();
            RemoveServerAssistPanel();
            panelSessions.Clear();
            linkPolling.Clear();
        }

        private void OnServerAssistReady()
        {
            TryRegisterServerAssistPanel();
        }

        private void OnUserConnected(IPlayer player)
        {
            if (player == null || string.IsNullOrEmpty(player.Id) || player.IsServer)
            {
                return;
            }

            ApplyPendingEffects(player);
        }

        private void OnUserDisconnected(IPlayer player, string reason)
        {
            if (player != null && !string.IsNullOrEmpty(player.Id))
            {
                panelSessions.Remove(player.Id);
            }

            SaveData();
        }

        #endregion

        #region Configuration

        private sealed class Configuration
        {
            public int ConfigVersion = 0;
            public string ChatPrefix = "[Twitch]";

            public bool GrantUseToDefaultGroup = true;
            public bool GrantAdminToAdminGroup = true;
            public bool GrantStreamerOnLink = true;
            public bool RevokeStreamerOnUnlink = true;

            public string StreamerGroup = "twitch";
            public string StreamerGroupTitle = "Twitch Streamer";
            public int StreamerGroupRank = 0;

            public string HookLabel = "Twitch";
            public string HookDescription = "Link Twitch, choose reward shortlists, and see what can trigger.";
            public string HookPermission = PermUse;
            public string PanelIcon = "sprite:sa_stream_panel";
            public int PanelOrder = 40;

            // Remappable logical icon roles. Missing keys are filled from
            // DefaultIconMap on load, so new art can be added without a build.
            public Dictionary<string, string> Icons =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public string RelayUrl = "http://127.0.0.1:8090";
            public string ServerId = "main";
            public string ServerKey = "";
            public float PollIntervalSeconds = 3f;
            public float LinkPollIntervalSeconds = 3f;
            public float RequestTimeoutSeconds = 20f;

            public string TierPermissionPrefix = "twitch.tier.";
            public string DefaultTierId = "tier1";
            public bool RequireAllCategories = true;

            public bool AllowBuffs = true;
            public bool AllowDebuffs = true;
            public bool AllowRewards = true;
            public bool AllowAnonymousCheers = true;

            public int MaxEventAgeMinutes = 15;
            public int GlobalCooldownSeconds = 15;
            public int PerViewerCooldownSeconds = 60;
            public int MaxEffectsPerMinute = 10;
            public bool QueueWhileOffline = false;
            public int PendingEffectTtlMinutes = 30;

            public bool AnnounceEffects = true;
            public bool FallbackToDefaultTier = true;

            public List<string> FallbackEventTypes = new List<string>
            {
                "bits", "sub", "gift_sub", "resub", "raid"
            };

            public List<string> AllowedCommandPrefixes = new List<string>
            {
                "buffplayer", "debuffplayer", "give", "chaos", "weather", "spawn", "say"
            };

            public List<TierDefinition> Tiers = new List<TierDefinition>();
            public List<TriggerDefinition> Triggers = new List<TriggerDefinition>();
        }

        private sealed class TierDefinition
        {
            public string Id = "tier1";
            public string DisplayName = "Tier 1";
            public string Description = "";
            public string Permission = "";
            public bool Enabled = true;
            public int Order = 10;
            public string Icon = "ui_game_symbol_star";
            public int MinPicks = 1;
            public int MaxPicks = 3;

            public List<EffectDefinition> Buffs = new List<EffectDefinition>();
            public List<EffectDefinition> Debuffs = new List<EffectDefinition>();
            public List<EffectDefinition> Rewards = new List<EffectDefinition>();
        }

        private sealed class EffectDefinition
        {
            public string Id = "";
            public string Label = "";
            public string Description = "";
            public string Icon = "";
            public int Weight = 1;
            public List<EffectAction> Actions = new List<EffectAction>();
        }

        private sealed class EffectAction
        {
            public string Type = "buff";
            public string Value = "";
            public int Count = 1;
            public int Quality = 0;
            public float DelaySeconds = 0f;
        }

        private sealed class TriggerDefinition
        {
            public string Id = "";
            public string Event = "bits";
            public bool Enabled = true;
            public string TierId = "tier1";
            public int MinAmount = 0;
            public string RewardId = "";
            public string RewardTitle = "";
            public bool MatchAnyReward = false;
            public string Keyword = "";
            public int SubTier = 0;
            public bool RequireGift = false;
            public bool RequireNotGift = false;
            public string Phase = "";
            public int Priority = 0;
            public int CooldownSeconds = 0;
            public string Description = "";
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
                PrintWarning("Could not read config: " + exception.Message);
                config = new Configuration();
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
            if (config == null)
            {
                config = new Configuration();
            }

            MergeDefaultIcons();

            config.ChatPrefix = Clean(config.ChatPrefix, 32);
            if (string.IsNullOrEmpty(config.ChatPrefix))
            {
                config.ChatPrefix = "[Twitch]";
            }

            config.StreamerGroup = SanitizeId(config.StreamerGroup);
            config.StreamerGroupTitle = Clean(config.StreamerGroupTitle, 48);
            config.StreamerGroupRank = Math.Max(0, config.StreamerGroupRank);

            config.HookLabel = Clean(config.HookLabel, 48);
            if (string.IsNullOrEmpty(config.HookLabel))
            {
                config.HookLabel = "Twitch";
            }

            config.HookDescription = Clean(config.HookDescription, 160);
            config.HookPermission = Clean(config.HookPermission, 96);
            if (string.IsNullOrEmpty(config.HookPermission))
            {
                config.HookPermission = PermUse;
            }

            config.PanelIcon = Clean(config.PanelIcon, 120);
            if (string.IsNullOrEmpty(config.PanelIcon))
            {
                config.PanelIcon = Icon("Panel");
            }

            config.PanelOrder = Math.Max(0, config.PanelOrder);

            config.RelayUrl = Clean(config.RelayUrl, 256).TrimEnd('/');
            if (!string.IsNullOrEmpty(config.RelayUrl) &&
                !config.RelayUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !config.RelayUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                config.RelayUrl = "http://" + config.RelayUrl;
            }

            config.ServerId = SanitizeId(config.ServerId);
            if (string.IsNullOrEmpty(config.ServerId))
            {
                config.ServerId = "main";
            }

            config.ServerKey = Clean(config.ServerKey, 256);

            config.PollIntervalSeconds = Clamp(config.PollIntervalSeconds, 1f, 60f);
            config.LinkPollIntervalSeconds = Clamp(config.LinkPollIntervalSeconds, 2f, 60f);
            config.RequestTimeoutSeconds = Clamp(config.RequestTimeoutSeconds, 5f, 120f);

            config.TierPermissionPrefix = Clean(config.TierPermissionPrefix, 64);
            if (string.IsNullOrEmpty(config.TierPermissionPrefix))
            {
                config.TierPermissionPrefix = "twitch.tier.";
            }

            config.MaxEventAgeMinutes = Math.Max(1, Math.Min(1440, config.MaxEventAgeMinutes));
            config.GlobalCooldownSeconds = Math.Max(0, Math.Min(3600, config.GlobalCooldownSeconds));
            config.PerViewerCooldownSeconds = Math.Max(0, Math.Min(3600, config.PerViewerCooldownSeconds));
            config.MaxEffectsPerMinute = Math.Max(1, Math.Min(120, config.MaxEffectsPerMinute));
            config.PendingEffectTtlMinutes = Math.Max(1, Math.Min(1440, config.PendingEffectTtlMinutes));

            if (config.AllowedCommandPrefixes == null)
            {
                config.AllowedCommandPrefixes = new List<string>();
            }

            config.AllowedCommandPrefixes = config.AllowedCommandPrefixes
                .Select(value => SanitizeCommandPrefix(value))
                .Where(value => !string.IsNullOrEmpty(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (config.FallbackEventTypes == null)
            {
                config.FallbackEventTypes = new List<string>();
            }

            config.FallbackEventTypes = config.FallbackEventTypes
                .Select(value => Clean(value, 32).ToLowerInvariant())
                .Where(value => !string.IsNullOrEmpty(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (config.Tiers == null || config.Tiers.Count == 0)
            {
                config.Tiers = CreateDefaultTiers();
            }

            if (config.Triggers == null || config.Triggers.Count == 0)
            {
                config.Triggers = CreateDefaultTriggers();
            }

            config.Tiers = config.Tiers.Where(tier => tier != null).ToList();
            config.Triggers = config.Triggers.Where(trigger => trigger != null).ToList();

            HashSet<string> tierIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TierDefinition tier in config.Tiers)
            {
                NormalizeTier(tier);
                if (!tierIds.Add(tier.Id))
                {
                    tier.Id = tier.Id + "_" + (tierIds.Count + 1);
                    tierIds.Add(tier.Id);
                }
            }

            if (!tierIds.Contains(config.DefaultTierId))
            {
                config.DefaultTierId = config.Tiers[0].Id;
            }

            HashSet<string> triggerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TriggerDefinition trigger in config.Triggers)
            {
                NormalizeTrigger(trigger, tierIds);
                if (!triggerIds.Add(trigger.Id))
                {
                    trigger.Id = trigger.Id + "_" + (triggerIds.Count + 1);
                    triggerIds.Add(trigger.Id);
                }
            }

            if (config.ConfigVersion < 2)
            {
                UpgradeLegacyIcons();
                config.ConfigVersion = 2;
            }

            if (config.ConfigVersion < 3)
            {
                UpgradeDynamicIcons();
                config.ConfigVersion = 3;
            }

            config.PanelIcon = Icon("Panel");
        }

        private void MergeDefaultIcons()
        {
            if (config.Icons == null)
            {
                config.Icons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                config.Icons = new Dictionary<string, string>(config.Icons, StringComparer.OrdinalIgnoreCase);
            }

            if (!config.Icons.ContainsKey("Panel") &&
                !string.IsNullOrEmpty(config.PanelIcon) &&
                !IsLegacyPlaceholder(config.PanelIcon))
            {
                config.Icons["Panel"] = config.PanelIcon;
            }

            foreach (KeyValuePair<string, string> pair in DefaultIconMap)
            {
                if (!config.Icons.ContainsKey(pair.Key))
                {
                    string live;
                    config.Icons[pair.Key] = DefaultLiveIconMap.TryGetValue(pair.Key, out live)
                        ? live
                        : pair.Value;
                }
            }
        }

        private string Icon(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            string value;
            if (config != null &&
                config.Icons != null &&
                config.Icons.TryGetValue(key, out value) &&
                !string.IsNullOrEmpty(value))
            {
                if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
                {
                    return string.Empty;
                }

                string resolved = ResolveDynamicIcon(value);
                if (!string.IsNullOrEmpty(resolved))
                {
                    return resolved;
                }

                if (!IsDynamicIcon(value))
                {
                    return value;
                }
            }

            string fallback;
            return DefaultIconMap.TryGetValue(key, out fallback) ? fallback : "ui_game_symbol_hammer";
        }

        private string ResolveDynamicIcon(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            string method;
            string argument;

            if (value.StartsWith("plugin:", StringComparison.OrdinalIgnoreCase))
            {
                method = "GetPluginIcon";
                argument = value.Substring("plugin:".Length).Trim();
            }
            else if (value.StartsWith("panel:", StringComparison.OrdinalIgnoreCase))
            {
                method = "GetPanelIcon";
                argument = value.Substring("panel:".Length).Trim();
            }
            else
            {
                return null;
            }

            if (string.IsNullOrEmpty(argument))
            {
                return null;
            }

            if (serverAssist == null)
            {
                serverAssist = plugins.Find(ServerAssistPluginName);
            }

            if (serverAssist == null)
            {
                return null;
            }

            try
            {
                object result = serverAssist.Call(method, argument);
                return result as string;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsDynamicIcon(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   (value.StartsWith("plugin:", StringComparison.OrdinalIgnoreCase) ||
                    value.StartsWith("panel:", StringComparison.OrdinalIgnoreCase));
        }

        private void UpgradeLegacyIcons()
        {
            if (string.IsNullOrEmpty(config.PanelIcon) || IsLegacyPlaceholder(config.PanelIcon))
            {
                config.PanelIcon = Icon("Panel");
            }

            foreach (TierDefinition tier in config.Tiers)
            {
                if (tier == null)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(tier.Icon) || IsLegacyPlaceholder(tier.Icon))
                {
                    switch (tier.Id)
                    {
                        case "tier1":
                            tier.Icon = "sprite:sa_stream_tier_1";
                            break;
                        case "tier2":
                            tier.Icon = "sprite:sa_stream_tier_2";
                            break;
                        case "tier3":
                            tier.Icon = "sprite:sa_stream_tier_3";
                            break;
                        default:
                            tier.Icon = Icon("Tier");
                            break;
                    }
                }

                foreach (EffectDefinition effect in tier.Buffs.Concat(tier.Debuffs).Concat(tier.Rewards))
                {
                    if (effect == null)
                    {
                        continue;
                    }

                    string replacement;
                    if (!LegacyEffectIcons.TryGetValue(effect.Id, out replacement))
                    {
                        continue;
                    }

                    string originalItem;
                    bool isOriginalItem = LegacyItemIcons.TryGetValue(effect.Id, out originalItem) &&
                                          string.Equals(effect.Icon, originalItem, StringComparison.OrdinalIgnoreCase);

                    if (string.IsNullOrEmpty(effect.Icon) ||
                        IsLegacyPlaceholder(effect.Icon) ||
                        isOriginalItem)
                    {
                        effect.Icon = replacement;
                    }
                }
            }
        }

        private void UpgradeDynamicIcons()
        {
            foreach (KeyValuePair<string, string> pair in LegacyRoleIcons)
            {
                string current;
                if (config.Icons.TryGetValue(pair.Key, out current) &&
                    !string.IsNullOrEmpty(current) &&
                    !string.Equals(current, pair.Value, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string replacement;
                if (DefaultLiveIconMap.TryGetValue(pair.Key, out replacement) ||
                    DefaultIconMap.TryGetValue(pair.Key, out replacement))
                {
                    config.Icons[pair.Key] = replacement;
                }
            }

            foreach (TierDefinition tier in config.Tiers)
            {
                if (tier == null)
                {
                    continue;
                }

                foreach (EffectDefinition effect in tier.Buffs.Concat(tier.Debuffs).Concat(tier.Rewards))
                {
                    if (effect == null)
                    {
                        continue;
                    }

                    string oldArt;
                    if (!LegacyStreamRewardIcons.TryGetValue(effect.Id, out oldArt) ||
                        !string.Equals(effect.Icon, oldArt, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string itemIcon;
                    if (LegacyItemIcons.TryGetValue(effect.Id, out itemIcon))
                    {
                        effect.Icon = itemIcon;
                    }
                }
            }
        }

        private static bool IsLegacyPlaceholder(string icon)
        {
            if (string.IsNullOrEmpty(icon))
            {
                return true;
            }

            return string.Equals(icon, "ui_game_symbol_twitch_buffs", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(icon, "ui_game_symbol_twitch_debuffs", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(icon, "ui_game_symbol_gift", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(icon, "ui_game_symbol_star", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(icon, "ui_game_symbol_map", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(icon, "ui_game_symbol_hammer", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(icon, "ui_game_symbol_chat", StringComparison.OrdinalIgnoreCase);
        }

        private void NormalizeTier(TierDefinition tier)
        {
            if (tier == null)
            {
                return;
            }

            tier.Id = SanitizeId(tier.Id);
            if (string.IsNullOrEmpty(tier.Id))
            {
                tier.Id = "tier";
            }

            tier.DisplayName = Clean(tier.DisplayName, 48);
            if (string.IsNullOrEmpty(tier.DisplayName))
            {
                tier.DisplayName = tier.Id;
            }

            tier.Description = Clean(tier.Description, 160);
            tier.Permission = Clean(tier.Permission, 96);
            if (string.IsNullOrEmpty(tier.Permission))
            {
                tier.Permission = config.TierPermissionPrefix + tier.Id;
            }

            tier.Order = Math.Max(0, tier.Order);
            tier.Icon = Clean(tier.Icon, 120);
            if (string.IsNullOrEmpty(tier.Icon))
            {
                tier.Icon = "ui_game_symbol_star";
            }

            tier.MinPicks = Math.Max(1, Math.Min(10, tier.MinPicks));
            tier.MaxPicks = Math.Max(tier.MinPicks, Math.Min(10, tier.MaxPicks));

            tier.Buffs = NormalizeEffects(tier.Buffs);
            tier.Debuffs = NormalizeEffects(tier.Debuffs);
            tier.Rewards = NormalizeEffects(tier.Rewards);
        }

        private List<EffectDefinition> NormalizeEffects(List<EffectDefinition> effects)
        {
            if (effects == null)
            {
                return new List<EffectDefinition>();
            }

            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<EffectDefinition> result = new List<EffectDefinition>();

            foreach (EffectDefinition effect in effects)
            {
                if (effect == null)
                {
                    continue;
                }

                effect.Id = SanitizeId(effect.Id);
                if (string.IsNullOrEmpty(effect.Id))
                {
                    effect.Id = SanitizeId(effect.Label);
                }

                if (string.IsNullOrEmpty(effect.Id))
                {
                    effect.Id = "effect";
                }

                if (!ids.Add(effect.Id))
                {
                    effect.Id = effect.Id + "_" + (ids.Count + 1);
                    ids.Add(effect.Id);
                }

                effect.Label = Clean(effect.Label, 64);
                if (string.IsNullOrEmpty(effect.Label))
                {
                    effect.Label = effect.Id;
                }

                effect.Description = Clean(effect.Description, 200);
                effect.Icon = Clean(effect.Icon, 120);
                effect.Weight = Math.Max(1, Math.Min(1000, effect.Weight));

                if (effect.Actions == null)
                {
                    effect.Actions = new List<EffectAction>();
                }

                foreach (EffectAction action in effect.Actions)
                {
                    if (action == null)
                    {
                        continue;
                    }

                    action.Type = Clean(action.Type, 16).ToLowerInvariant();
                    if (action.Type != "buff" &&
                        action.Type != "debuff" &&
                        action.Type != "item" &&
                        action.Type != "command" &&
                        action.Type != "hook")
                    {
                        action.Type = "command";
                    }

                    action.Value = Clean(action.Value, 512);
                    action.Count = Math.Max(1, Math.Min(10000, action.Count));
                    action.Quality = Math.Max(0, Math.Min(6, action.Quality));
                    action.DelaySeconds = Clamp(action.DelaySeconds, 0f, 120f);
                }

                result.Add(effect);
            }

            return result;
        }

        private void NormalizeTrigger(TriggerDefinition trigger, HashSet<string> tierIds)
        {
            if (trigger == null)
            {
                return;
            }

            trigger.Id = SanitizeId(trigger.Id);
            if (string.IsNullOrEmpty(trigger.Id))
            {
                trigger.Id = "trigger";
            }

            trigger.Event = Clean(trigger.Event, 32).ToLowerInvariant();
            if (string.IsNullOrEmpty(trigger.Event))
            {
                trigger.Event = "bits";
            }

            trigger.TierId = SanitizeId(trigger.TierId);
            if (!tierIds.Contains(trigger.TierId))
            {
                trigger.TierId = config.DefaultTierId;
            }

            trigger.MinAmount = Math.Max(0, trigger.MinAmount);
            trigger.RewardId = Clean(trigger.RewardId, 96);
            trigger.RewardTitle = Clean(trigger.RewardTitle, 96);
            trigger.Keyword = Clean(trigger.Keyword, 64);
            trigger.SubTier = Math.Max(0, trigger.SubTier);
            trigger.Phase = Clean(trigger.Phase, 16).ToLowerInvariant();
            trigger.Priority = Math.Max(-1000, Math.Min(1000, trigger.Priority));
            trigger.CooldownSeconds = Math.Max(0, Math.Min(86400, trigger.CooldownSeconds));
            trigger.Description = Clean(trigger.Description, 160);
        }

        #endregion

        #region Defaults

        private static List<TierDefinition> CreateDefaultTiers()
        {
            return new List<TierDefinition>
            {
                new TierDefinition
                {
                    Id = "tier1",
                    DisplayName = "Tier 1 - Spark",
                    Description = "Small buffs, mild chaos, and starter supplies.",
                    Order = 10,
                    Icon = "sprite:sa_stream_tier_1",
                    MinPicks = 1,
                    MaxPicks = 3,
                    Buffs = new List<EffectDefinition>
                    {
                        MakeBuff("fast", "Quick Feet", "twitch_buffFast", "sprite:sa_stream_buff_quick_feet", "A short burst of movement speed."),
                        MakeBuff("regen", "Regeneration", "twitch_regen", "sprite:sa_stream_buff_regen", "Slow health regeneration."),
                        MakeBuff("looter", "Looter's Luck", "twitch_buffLooter", "sprite:sa_stream_buff_looter", "Improved looting for a while.")
                    },
                    Debuffs = new List<EffectDefinition>
                    {
                        MakeDebuff("slow", "Heavy Legs", "twitch_buffSlow", "sprite:sa_stream_debuff_heavy_legs", "Movement speed is reduced."),
                        MakeDebuff("blur", "Blurred Vision", "twitch_buffBlur", "sprite:sa_stream_debuff_blurred_vision", "The screen blurs."),
                        MakeDebuff("no_run", "No Running", "twitch_buffNoRunning", "sprite:sa_stream_debuff_no_running", "Sprinting is disabled.")
                    },
                    Rewards = new List<EffectDefinition>
                    {
                        MakeItem("dukes_100", "100 Dukes", "casinoCoin", 100, "item:casinoCoin", "A small stack of Dukes."),
                        MakeItem("bandages_3", "3 First Aid Bandages", "medicalFirstAidBandage", 3, "item:medicalFirstAidBandage", "Basic medical supplies."),
                        MakeItem("ammo_9mm_30", "30 9mm Rounds", "ammo9mmBulletBall", 30, "item:ammo9mmBulletBall", "A handful of 9mm ammunition.")
                    }
                },
                new TierDefinition
                {
                    Id = "tier2",
                    DisplayName = "Tier 2 - Surge",
                    Description = "Stronger buffs, harsher debuffs, and better supplies.",
                    Order = 20,
                    Icon = "sprite:sa_stream_tier_2",
                    MinPicks = 1,
                    MaxPicks = 3,
                    Buffs = new List<EffectDefinition>
                    {
                        MakeBuff("melee_boost", "Melee Surge", "twitch_buffBoostMelee", "sprite:sa_stream_buff_melee_surge", "Melee damage is boosted."),
                        MakeBuff("range_boost", "Ranged Surge", "twitch_buffBoostRange", "sprite:sa_stream_buff_ranged_surge", "Ranged damage is boosted."),
                        MakeBuff("infinite_ammo", "Infinite Ammo", "twitch_buffInfiniteAmmo", "sprite:sa_stream_buff_infinite_ammo", "Ammunition is not consumed.")
                    },
                    Debuffs = new List<EffectDefinition>
                    {
                        MakeDebuff("no_melee", "No Melee", "twitch_buffNoMelee", "sprite:sa_stream_debuff_no_melee", "Melee attacks are disabled."),
                        MakeDebuff("no_ranged", "No Ranged", "twitch_buffNoRanged", "sprite:sa_stream_debuff_no_ranged", "Ranged attacks are disabled."),
                        MakeDebuff("encumbered", "Encumbered", "twitch_buffEncumbered", "sprite:sa_stream_debuff_encumbered", "Carrying capacity is reduced.")
                    },
                    Rewards = new List<EffectDefinition>
                    {
                        MakeItem("steel_50", "50 Forged Steel", "resourceForgedSteel", 50, "item:resourceForgedSteel", "A useful building material."),
                        MakeItem("ammo_762_40", "40 7.62mm Rounds", "ammo762mmBulletBall", 40, "item:ammo762mmBulletBall", "Rifle ammunition."),
                        MakeItem("mega_crush_2", "2 Mega Crush", "drinkCanMegaCrush", 2, "item:drinkCanMegaCrush", "Hydration and a little stamina.")
                    }
                },
                new TierDefinition
                {
                    Id = "tier3",
                    DisplayName = "Tier 3 - Overload",
                    Description = "High-impact buffs, dangerous debuffs, and premium rewards.",
                    Order = 30,
                    Icon = "sprite:sa_stream_tier_3",
                    MinPicks = 1,
                    MaxPicks = 3,
                    Buffs = new List<EffectDefinition>
                    {
                        MakeBuff("toughness", "Toughness", "twitch_buffToughness", "sprite:sa_stream_buff_toughness", "Damage resistance is increased."),
                        MakeBuff("xp_gain", "XP Gain", "twitch_buffXPGain", "sprite:sa_stream_buff_xp_gain", "Experience gain is increased."),
                        MakeBuff("pack_mule", "Pack Mule", "twitch_buffPackMule", "sprite:sa_stream_buff_pack_mule", "Carrying capacity is increased.")
                    },
                    Debuffs = new List<EffectDefinition>
                    {
                        MakeDebuff("tougher_enemies", "Tougher Enemies", "twitch_buffTougherEnemies", "sprite:sa_stream_debuff_tougher_enemies", "Enemies are harder to kill."),
                        MakeDebuff("double_spawns", "Double Spawns", "twitch_buffDoubleSpawns", "sprite:sa_stream_debuff_double_spawns", "More enemies spawn."),
                        MakeDebuff("no_healing", "No Healing", "twitch_buffNoHealingManager", "sprite:sa_stream_debuff_no_healing", "Healing is restricted.")
                    },
                    Rewards = new List<EffectDefinition>
                    {
                        MakeItem("steel_200", "200 Forged Steel", "resourceForgedSteel", 200, "item:resourceForgedSteel", "A large building payout."),
                        MakeItem("ammo_762_ap_60", "60 7.62mm AP Rounds", "ammo762mmBulletAP", 60, "item:ammo762mmBulletAP", "Armor-piercing rifle ammunition."),
                        MakeItem("medkit_3", "3 First Aid Kits", "medicalFirstAidKit", 3, "item:medicalFirstAidKit", "Strong medical supplies.")
                    }
                }
            };
        }

        private static List<TriggerDefinition> CreateDefaultTriggers()
        {
            return new List<TriggerDefinition>
            {
                new TriggerDefinition { Id = "bits_100", Event = "bits", TierId = "tier1", MinAmount = 100, Description = "100 Bits" },
                new TriggerDefinition { Id = "bits_500", Event = "bits", TierId = "tier2", MinAmount = 500, Description = "500 Bits" },
                new TriggerDefinition { Id = "bits_1000", Event = "bits", TierId = "tier3", MinAmount = 1000, Description = "1000 Bits" },

                new TriggerDefinition { Id = "sub_t1", Event = "sub", TierId = "tier1", SubTier = 1000, RequireNotGift = true, Description = "Tier 1 sub" },
                new TriggerDefinition { Id = "sub_t2", Event = "sub", TierId = "tier2", SubTier = 2000, RequireNotGift = true, Description = "Tier 2 sub" },
                new TriggerDefinition { Id = "sub_t3", Event = "sub", TierId = "tier3", SubTier = 3000, RequireNotGift = true, Description = "Tier 3 sub" },

                new TriggerDefinition { Id = "resub_1", Event = "resub", TierId = "tier1", MinAmount = 1, Description = "Resub" },
                new TriggerDefinition { Id = "resub_6", Event = "resub", TierId = "tier2", MinAmount = 6, Description = "6-month resub" },
                new TriggerDefinition { Id = "resub_12", Event = "resub", TierId = "tier3", MinAmount = 12, Description = "12-month resub" },

                new TriggerDefinition { Id = "gift_1", Event = "gift_sub", TierId = "tier1", MinAmount = 1, Description = "Gift sub" },
                new TriggerDefinition { Id = "gift_5", Event = "gift_sub", TierId = "tier2", MinAmount = 5, Description = "5 gift subs" },
                new TriggerDefinition { Id = "gift_20", Event = "gift_sub", TierId = "tier3", MinAmount = 20, Description = "20 gift subs" },

                new TriggerDefinition { Id = "cp_any", Event = "channel_points", TierId = "tier1", MatchAnyReward = true, Priority = -10, Description = "Any channel point reward (specific reward triggers with priority 0+ win)" },
                new TriggerDefinition { Id = "auto_any", Event = "automatic_reward", TierId = "tier1", MatchAnyReward = true, Priority = -10, Description = "Any automatic reward (specific triggers win)" },

                new TriggerDefinition { Id = "raid_10", Event = "raid", TierId = "tier1", MinAmount = 10, Description = "Raid with 10+ viewers" },
                new TriggerDefinition { Id = "raid_50", Event = "raid", TierId = "tier2", MinAmount = 50, Description = "Raid with 50+ viewers" },
                new TriggerDefinition { Id = "raid_100", Event = "raid", TierId = "tier3", MinAmount = 100, Description = "Raid with 100+ viewers" },

                new TriggerDefinition { Id = "follow_any", Event = "follow", TierId = "tier1", Enabled = false, Description = "New follower (disabled by default)" },

                new TriggerDefinition { Id = "hype_1", Event = "hype_train", TierId = "tier1", MinAmount = 1, Phase = "end", Description = "Hype train level 1+" },
                new TriggerDefinition { Id = "hype_3", Event = "hype_train", TierId = "tier2", MinAmount = 3, Phase = "end", Description = "Hype train level 3+" },
                new TriggerDefinition { Id = "hype_5", Event = "hype_train", TierId = "tier3", MinAmount = 5, Phase = "end", Description = "Hype train level 5+" }
            };
        }

        private static EffectDefinition MakeBuff(string id, string label, string buff, string icon, string description)
        {
            return new EffectDefinition
            {
                Id = id,
                Label = label,
                Description = description,
                Icon = icon,
                Actions = new List<EffectAction>
                {
                    new EffectAction { Type = "buff", Value = buff }
                }
            };
        }

        private static EffectDefinition MakeDebuff(string id, string label, string buff, string icon, string description)
        {
            return new EffectDefinition
            {
                Id = id,
                Label = label,
                Description = description,
                Icon = icon,
                Actions = new List<EffectAction>
                {
                    new EffectAction { Type = "debuff", Value = buff }
                }
            };
        }

        private static EffectDefinition MakeItem(string id, string label, string item, int count, string icon, string description)
        {
            return new EffectDefinition
            {
                Id = id,
                Label = label,
                Description = description,
                Icon = icon,
                Actions = new List<EffectAction>
                {
                    new EffectAction { Type = "item", Value = item, Count = count }
                }
            };
        }

        #endregion

        #region Permissions

        private void RegisterConfiguredPermissions()
        {
            permission.RegisterPermission(PermUse, this);
            permission.RegisterPermission(PermStreamer, this);
            permission.RegisterPermission(PermAdmin, this);

            if (!string.IsNullOrEmpty(config.HookPermission) &&
                !string.Equals(config.HookPermission, PermUse, StringComparison.OrdinalIgnoreCase))
            {
                permission.RegisterPermission(config.HookPermission, this);
            }

            foreach (TierDefinition tier in config.Tiers)
            {
                if (!string.IsNullOrEmpty(tier.Permission))
                {
                    permission.RegisterPermission(tier.Permission, this);
                }
            }

            EnsureStreamerGroup();
        }

        private void EnsureStreamerGroup()
        {
            if (string.IsNullOrEmpty(config.StreamerGroup))
            {
                return;
            }

            if (!permission.GroupExists(config.StreamerGroup))
            {
                permission.CreateGroup(
                    config.StreamerGroup,
                    string.IsNullOrEmpty(config.StreamerGroupTitle) ? "Twitch Streamer" : config.StreamerGroupTitle,
                    config.StreamerGroupRank);
            }

            if (!permission.GroupHasPermission(config.StreamerGroup, PermStreamer))
            {
                permission.GrantGroupPermission(config.StreamerGroup, PermStreamer, this);
            }
        }

        private void GrantDefaultPermissions()
        {
            string playersGroup = Interface.Oxide.Config.Options.DefaultGroups.Players;
            if (config.GrantUseToDefaultGroup)
            {
                GrantGroupIfMissing(playersGroup, PermUse);
            }

            string adminGroup = Interface.Oxide.Config.Options.DefaultGroups.Administrators;
            if (config.GrantAdminToAdminGroup)
            {
                GrantGroupIfMissing(adminGroup, PermAdmin);
                GrantGroupIfMissing(adminGroup, PermUse);
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

        private bool IsAdmin(IPlayer player)
        {
            return player != null &&
                   (player.IsAdmin || permission.UserHasPermission(player.Id, PermAdmin));
        }

        private bool HasTierPermission(IPlayer player, TierDefinition tier)
        {
            return player != null &&
                   (IsAdmin(player) || permission.UserHasPermission(player.Id, tier.Permission));
        }

        private void GrantStreamer(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return;
            }

            if (!string.IsNullOrEmpty(config.StreamerGroup))
            {
                EnsureStreamerGroup();
                permission.AddUserGroup(playerId, config.StreamerGroup);
            }
            else
            {
                permission.GrantUserPermission(playerId, PermStreamer, this);
            }
        }

        private void RevokeStreamer(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return;
            }

            if (!string.IsNullOrEmpty(config.StreamerGroup))
            {
                permission.RemoveUserGroup(playerId, config.StreamerGroup);
            }
            else
            {
                permission.RevokeUserPermission(playerId, PermStreamer);
            }
        }

        #endregion

        #region Data

        private sealed class StoredData
        {
            public Dictionary<string, PlayerProfile> Players =
                new Dictionary<string, PlayerProfile>(StringComparer.Ordinal);

            public Dictionary<string, LinkSessionData> LinkSessions =
                new Dictionary<string, LinkSessionData>(StringComparer.Ordinal);

            public List<string> ProcessedEventIds = new List<string>();
            public List<string> Audit = new List<string>();
        }

        private sealed class PlayerProfile
        {
            public string SteamId = "";
            public string PlayerName = "";
            public string TwitchUserId = "";
            public string TwitchLogin = "";
            public string TwitchDisplayName = "";
            public long LinkedAtUtc;

            public bool OptedIn = false;
            public bool Paused = false;
            public bool AllowBuffs = true;
            public bool AllowDebuffs = true;
            public bool AllowRewards = true;

            public Dictionary<string, TierLoadout> Loadouts =
                new Dictionary<string, TierLoadout>(StringComparer.Ordinal);

            public Dictionary<string, long> ViewerCooldowns =
                new Dictionary<string, long>(StringComparer.Ordinal);

            public Dictionary<string, long> TriggerCooldowns =
                new Dictionary<string, long>(StringComparer.Ordinal);

            public List<PendingEffect> PendingEffects = new List<PendingEffect>();
            public long LastEffectUtc;
        }

        private sealed class TierLoadout
        {
            public List<string> Buffs = new List<string>();
            public List<string> Debuffs = new List<string>();
            public List<string> Rewards = new List<string>();
            public long UpdatedAtUtc;
        }

        private sealed class PendingEffect
        {
            public string EventId = "";
            public string TierId = "";
            public string Category = "";
            public string EffectId = "";
            public string EventType = "";
            public string ViewerLogin = "";
            public string ViewerName = "";
            public long Amount;
            public long ExpiresAtUtc;
        }

        private sealed class LinkSessionData
        {
            public string SessionId = "";
            public string SteamId = "";
            public string UserCode = "";
            public string VerificationUri = "";
            public int Interval = 5;
            public long CreatedAtUtc;
            public long ExpiresAtUtc;
            public long NextPollUtc;
            public string Status = "pending";
            public string Error = "";
        }

        private void LoadData()
        {
            try
            {
                data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(DataFileName);
            }
            catch (Exception exception)
            {
                PrintWarning("Could not read data: " + exception.Message);
                data = new StoredData();
            }

            if (data == null)
            {
                data = new StoredData();
            }

            if (data.Players == null)
            {
                data.Players = new Dictionary<string, PlayerProfile>(StringComparer.Ordinal);
            }
            else
            {
                data.Players = new Dictionary<string, PlayerProfile>(data.Players, StringComparer.Ordinal);
            }

            foreach (string key in data.Players.Where(pair => pair.Value == null).Select(pair => pair.Key).ToList())
            {
                data.Players.Remove(key);
            }

            foreach (PlayerProfile profile in data.Players.Values)
            {
                NormalizeProfile(profile);
            }

            if (data.LinkSessions == null)
            {
                data.LinkSessions = new Dictionary<string, LinkSessionData>(StringComparer.Ordinal);
            }
            else
            {
                data.LinkSessions = new Dictionary<string, LinkSessionData>(data.LinkSessions, StringComparer.Ordinal);
            }

            if (data.ProcessedEventIds == null)
            {
                data.ProcessedEventIds = new List<string>();
            }

            processedEventIds.Clear();
            processedOrder.Clear();
            foreach (string id in data.ProcessedEventIds)
            {
                if (!string.IsNullOrEmpty(id) && processedEventIds.Add(id))
                {
                    processedOrder.Add(id);
                }
            }

            if (data.Audit == null)
            {
                data.Audit = new List<string>();
            }

            long now = NowUnix();
            foreach (string key in data.LinkSessions.Keys.ToList())
            {
                LinkSessionData session = data.LinkSessions[key];
                if (session == null || session.ExpiresAtUtc + 300 < now)
                {
                    data.LinkSessions.Remove(key);
                }
            }
        }

        private void NormalizeProfile(PlayerProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            profile.SteamId = Clean(profile.SteamId, 64);
            profile.PlayerName = Clean(profile.PlayerName, 64);
            profile.TwitchUserId = Clean(profile.TwitchUserId, 64);
            profile.TwitchLogin = Clean(profile.TwitchLogin, 64);
            profile.TwitchDisplayName = Clean(profile.TwitchDisplayName, 64);

            if (profile.Loadouts == null)
            {
                profile.Loadouts = new Dictionary<string, TierLoadout>(StringComparer.Ordinal);
            }
            else
            {
                profile.Loadouts = new Dictionary<string, TierLoadout>(profile.Loadouts, StringComparer.Ordinal);
            }

            foreach (TierLoadout loadout in profile.Loadouts.Values)
            {
                if (loadout == null)
                {
                    continue;
                }

                loadout.Buffs = loadout.Buffs ?? new List<string>();
                loadout.Debuffs = loadout.Debuffs ?? new List<string>();
                loadout.Rewards = loadout.Rewards ?? new List<string>();
            }

            if (profile.ViewerCooldowns == null)
            {
                profile.ViewerCooldowns = new Dictionary<string, long>(StringComparer.Ordinal);
            }
            else
            {
                profile.ViewerCooldowns = new Dictionary<string, long>(profile.ViewerCooldowns, StringComparer.Ordinal);
            }

            if (profile.TriggerCooldowns == null)
            {
                profile.TriggerCooldowns = new Dictionary<string, long>(StringComparer.Ordinal);
            }
            else
            {
                profile.TriggerCooldowns = new Dictionary<string, long>(profile.TriggerCooldowns, StringComparer.Ordinal);
            }

            if (profile.PendingEffects == null)
            {
                profile.PendingEffects = new List<PendingEffect>();
            }

            long now = NowUnix();
            profile.PendingEffects.RemoveAll(effect => effect == null || effect.ExpiresAtUtc < now);
        }

        private void SaveData()
        {
            if (data == null)
            {
                return;
            }

            try
            {
                data.ProcessedEventIds = new List<string>(processedOrder);
                Interface.Oxide.DataFileSystem.WriteObject(DataFileName, data);
            }
            catch (Exception exception)
            {
                PrintWarning("Could not save data: " + exception.Message);
            }
        }

        private PlayerProfile GetProfile(string steamId)
        {
            if (string.IsNullOrEmpty(steamId))
            {
                return null;
            }

            PlayerProfile profile;
            if (!data.Players.TryGetValue(steamId, out profile))
            {
                profile = new PlayerProfile { SteamId = steamId };
                data.Players[steamId] = profile;
            }

            NormalizeProfile(profile);
            IPlayer player = players.FindPlayerById(steamId);
            if (player != null && !string.IsNullOrEmpty(player.Name))
            {
                profile.PlayerName = player.Name;
            }

            return profile;
        }

        private TierLoadout GetLoadout(PlayerProfile profile, string tierId)
        {
            if (profile == null || string.IsNullOrEmpty(tierId))
            {
                return new TierLoadout();
            }

            TierLoadout loadout;
            if (!profile.Loadouts.TryGetValue(tierId, out loadout) || loadout == null)
            {
                loadout = new TierLoadout();
                profile.Loadouts[tierId] = loadout;
            }

            return loadout;
        }

        private void AddAudit(string message)
        {
            if (data == null || string.IsNullOrEmpty(message))
            {
                return;
            }

            if (data.Audit == null)
            {
                data.Audit = new List<string>();
            }

            data.Audit.Insert(0, DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + message);
            if (data.Audit.Count > AuditLimit)
            {
                data.Audit.RemoveRange(AuditLimit, data.Audit.Count - AuditLimit);
            }
        }

        #endregion

        #region Commands

        [Command("twitch")]
        private void TwitchCommand(IPlayer player, string command, string[] args)
        {
            if (player == null)
            {
                return;
            }

            string sub = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "open";

            switch (sub)
            {
                case "open":
                case "panel":
                    OpenPanel(player);
                    return;

                case "link":
                    StartLink(player);
                    return;

                case "unlink":
                    Unlink(player, null);
                    return;

                case "on":
                    SetOptIn(player, true);
                    return;

                case "off":
                    SetOptIn(player, false);
                    return;

                case "pause":
                    SetPaused(player, true);
                    return;

                case "resume":
                    SetPaused(player, false);
                    return;

                case "status":
                    ShowStatus(player);
                    return;

                case "loadout":
                    OpenPanelWithAction(player, "a|tiers");
                    return;

                case "catalog":
                    OpenPanelWithAction(player, "a|catalog");
                    return;

                case "help":
                    ShowHelp(player);
                    return;

                case "reload":
                    if (!IsAdmin(player))
                    {
                        Reply(player, "You need " + PermAdmin + ".");
                        return;
                    }

                    LoadConfig();
                    RegisterConfiguredPermissions();
                    TryRegisterServerAssistPanel();
                    Reply(player, "Config reloaded.");
                    return;

                case "reconcile":
                    if (!IsAdmin(player))
                    {
                        Reply(player, "You need " + PermAdmin + ".");
                        return;
                    }

                    ReconcileRelay(player);
                    return;

                case "relaytest":
                    if (!IsAdmin(player))
                    {
                        Reply(player, "You need " + PermAdmin + ".");
                        return;
                    }

                    TestRelayConnection(player);
                    return;

                case "connect":
                    if (!IsAdmin(player))
                    {
                        Reply(player, "You need " + PermAdmin + ".");
                        return;
                    }

                    if (args.Length < 3)
                    {
                        Reply(player, "Usage: /twitch connect <relayUrl> <serverKey> [serverId]");
                        return;
                    }

                    if (!IsValidRelayUrl(args[1]))
                    {
                        Reply(player, "Relay URL must start with http:// or https://.");
                        return;
                    }

                    config.RelayUrl = args[1].TrimEnd('/');
                    config.ServerKey = Clean(args[2], 256);
                    if (args.Length > 3)
                    {
                        config.ServerId = SanitizeId(args[3]);
                    }

                    NormalizeConfig();
                    SaveConfig();
                    relayAvailable = false;
                    lastRelayError = string.Empty;
                    Reply(player, "Relay destination saved. For production, use the Admin panel so the key does not appear in chat logs.");
                    TestRelayConnection(player);
                    return;

                case "test":
                    if (!IsAdmin(player))
                    {
                        Reply(player, "You need " + PermAdmin + ".");
                        return;
                    }

                    string testType = args.Length > 1 ? args[1].ToLowerInvariant() : "bits";
                    long testAmount = 100;
                    if (args.Length > 2)
                    {
                        long.TryParse(args[2], out testAmount);
                    }

                    SimulateEvent(player, testType, testAmount);
                    return;

                case "list":
                    if (!IsAdmin(player))
                    {
                        Reply(player, "You need " + PermAdmin + ".");
                        return;
                    }

                    ListLinks(player);
                    return;

                case "who":
                    if (!IsAdmin(player) || args.Length < 2)
                    {
                        Reply(player, "Usage: /twitch who <player>");
                        return;
                    }

                    ShowProfile(player, args[1]);
                    return;

                case "tier":
                    if (!IsAdmin(player) || args.Length < 3)
                    {
                        Reply(player, "Usage: /twitch tier <player> <tierId>");
                        return;
                    }

                    SetTierPermission(player, args[1], args[2], true);
                    return;

                case "untier":
                    if (!IsAdmin(player) || args.Length < 3)
                    {
                        Reply(player, "Usage: /twitch untier <player> <tierId>");
                        return;
                    }

                    SetTierPermission(player, args[1], args[2], false);
                    return;

                case "icons":
                    if (!IsAdmin(player))
                    {
                        Reply(player, "You need " + PermAdmin + ".");
                        return;
                    }

                    ListIconMap(player, args.Length > 1 ? args[1] : "1");
                    return;

                case "iconset":
                    if (!IsAdmin(player) || args.Length < 3)
                    {
                        Reply(player, "Usage: /twitch iconset <role> <sprite:name|item:name|atlas:name:sprite|plugin:PluginName|panel:panelId|none>");
                        return;
                    }

                    SetIconMapping(player, args[1], string.Join(" ", args.Skip(2).ToArray()));
                    return;

                case "forcelink":
                    if (!IsAdmin(player) || args.Length < 3)
                    {
                        Reply(player, "Usage: /twitch forcelink <player> <twitchLogin>");
                        return;
                    }

                    ForceLink(player, args[1], args[2]);
                    return;

                default:
                    ShowHelp(player);
                    return;
            }
        }

        private void ShowHelp(IPlayer player)
        {
            Reply(player, "/twitch - open the panel");
            Reply(player, "/twitch link | unlink | on | off | pause | resume | status");
            Reply(player, "/twitch loadout | catalog");
            if (IsAdmin(player))
            {
                Reply(player, "/twitch connect <relayUrl> <serverKey> [serverId] | relaytest");
                Reply(player, "/twitch reload | reconcile | test <type> [amount] | list | who <player>");
                Reply(player, "/twitch tier <player> <tierId> | untier <player> <tierId>");
                Reply(player, "/twitch icons [page] | iconset <role> <sprite:name|item:name|plugin:Name|panel:id|none>");
            }
        }

        private void ShowStatus(IPlayer player)
        {
            PlayerProfile profile = GetProfile(player.Id);
            if (profile == null || string.IsNullOrEmpty(profile.TwitchUserId))
            {
                Reply(player, "Not linked. Run /twitch link.");
                return;
            }

            Reply(player, "Twitch: " + profile.TwitchDisplayName + " (" + profile.TwitchLogin + ")");
            Reply(player, "Opt-in: " + (profile.OptedIn ? "on" : "off") + " | Paused: " + (profile.Paused ? "yes" : "no"));

            foreach (TierDefinition tier in config.Tiers.OrderBy(t => t.Order))
            {
                TierLoadout loadout = GetLoadout(profile, tier.Id);
                Reply(player,
                    tier.DisplayName + ": buffs " + loadout.Buffs.Count + "/" + tier.MaxPicks +
                    ", debuffs " + loadout.Debuffs.Count + "/" + tier.MaxPicks +
                    ", rewards " + loadout.Rewards.Count + "/" + tier.MaxPicks +
                    (HasTierPermission(player, tier) ? "" : " (locked)")
                );
            }
        }

        private void SetOptIn(IPlayer player, bool value)
        {
            PlayerProfile profile = GetProfile(player.Id);
            if (profile == null || string.IsNullOrEmpty(profile.TwitchUserId))
            {
                Reply(player, "Link Twitch first with /twitch link.");
                return;
            }

            profile.OptedIn = value;
            SaveData();
            Reply(player, "Twitch effects are now " + (value ? "ON" : "OFF") + ".");
            RefreshPanel(player);
        }

        private void SetPaused(IPlayer player, bool value)
        {
            PlayerProfile profile = GetProfile(player.Id);
            if (profile == null)
            {
                return;
            }

            profile.Paused = value;
            SaveData();
            Reply(player, "Twitch effects are now " + (value ? "paused" : "resumed") + ".");
            RefreshPanel(player);
        }

        private void ListLinks(IPlayer admin)
        {
            List<PlayerProfile> linked = data.Players.Values
                .Where(profile => !string.IsNullOrEmpty(profile.TwitchUserId))
                .OrderBy(profile => profile.TwitchLogin, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (linked.Count == 0)
            {
                Reply(admin, "No linked Twitch accounts.");
                return;
            }

            foreach (PlayerProfile profile in linked)
            {
                Reply(admin, profile.PlayerName + " (" + profile.SteamId + ") -> " + profile.TwitchLogin +
                             " | opt-in " + (profile.OptedIn ? "on" : "off"));
            }
        }

        private void ShowProfile(IPlayer admin, string nameOrId)
        {
            IPlayer target = FindPlayer(nameOrId);
            if (target == null)
            {
                Reply(admin, "Player not found.");
                return;
            }

            PlayerProfile profile = GetProfile(target.Id);
            Reply(admin, target.Name + " (" + target.Id + ")");
            Reply(admin, "Twitch: " + (string.IsNullOrEmpty(profile.TwitchLogin) ? "not linked" : profile.TwitchLogin));
            Reply(admin, "Opt-in: " + profile.OptedIn + " | Paused: " + profile.Paused);
            Reply(admin, "Pending effects: " + profile.PendingEffects.Count);
        }

        private void SetTierPermission(IPlayer admin, string nameOrId, string tierId, bool grant)
        {
            IPlayer target = FindPlayer(nameOrId);
            if (target == null)
            {
                Reply(admin, "Player not found.");
                return;
            }

            TierDefinition tier = FindTier(tierId);
            if (tier == null)
            {
                Reply(admin, "Unknown tier. Use " + string.Join(", ", config.Tiers.Select(t => t.Id).ToArray()));
                return;
            }

            if (grant)
            {
                permission.GrantUserPermission(target.Id, tier.Permission, this);
                Reply(admin, "Granted " + tier.Permission + " to " + target.Name + ".");
            }
            else
            {
                permission.RevokeUserPermission(target.Id, tier.Permission);
                Reply(admin, "Revoked " + tier.Permission + " from " + target.Name + ".");
            }
        }

        private void ListIconMap(IPlayer admin, string pageText)
        {
            MergeDefaultIcons();

            List<KeyValuePair<string, string>> entries = config.Icons
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int pageSize = 10;
            int maxPage = Math.Max(1, (entries.Count + pageSize - 1) / pageSize);
            int page;
            if (!int.TryParse(pageText, out page))
            {
                page = 1;
            }

            page = Math.Max(1, Math.Min(maxPage, page));
            Reply(admin, "Icon map page " + page + "/" + maxPage + " (use /twitch icons <page>)");

            int start = (page - 1) * pageSize;
            for (int i = start; i < entries.Count && i < start + pageSize; i++)
            {
                Reply(admin, entries[i].Key + " = " + entries[i].Value);
            }

            Reply(admin, "Remap with: /twitch iconset <role> <sprite:name|item:name|atlas:name:sprite|plugin:PluginName|panel:panelId|none>");
        }

        private void SetIconMapping(IPlayer admin, string role, string value)
        {
            MergeDefaultIcons();

            string canonical = DefaultIconMap.Keys
                .FirstOrDefault(key => string.Equals(key, role, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(canonical))
            {
                Reply(admin, "Unknown icon role '" + role + "'. Use /twitch icons to list roles.");
                return;
            }

            value = Clean(value, 160);
            if (string.IsNullOrEmpty(value))
            {
                Reply(admin, "Usage: /twitch iconset " + canonical + " <sprite:name|item:name|atlas:name:sprite|plugin:PluginName|panel:panelId|none>");
                return;
            }

            config.Icons[canonical] = value;
            if (string.Equals(canonical, "Panel", StringComparison.OrdinalIgnoreCase))
            {
                config.PanelIcon = value;
            }

            SaveConfig();
            Reply(admin, "Icon role '" + canonical + "' is now '" + value + "'.");
        }

        private void ForceLink(IPlayer admin, string nameOrId, string twitchLogin)
        {
            IPlayer target = FindPlayer(nameOrId);
            if (target == null)
            {
                Reply(admin, "Player not found.");
                return;
            }

            PlayerProfile profile = GetProfile(target.Id);
            profile.TwitchLogin = Clean(twitchLogin, 64);
            profile.TwitchDisplayName = profile.TwitchLogin;
            profile.TwitchUserId = "manual:" + profile.TwitchLogin.ToLowerInvariant();
            profile.LinkedAtUtc = NowUnix();
            profile.OptedIn = true;
            GrantStreamer(target.Id);
            SaveData();
            Reply(admin, "Force-linked " + target.Name + " to " + profile.TwitchLogin + " (manual, relay will not send events).");
        }

        #endregion

        #region Server Assist panel

        private void TryRegisterServerAssistPanel()
        {
            serverAssist = plugins.Find(ServerAssistPluginName);
            if (serverAssist == null)
            {
                return;
            }

            string panelIcon = Icon("Panel");

            try
            {
                object result = serverAssist.Call(
                    "RegisterPanel",
                    PanelId,
                    config.HookLabel,
                    config.HookDescription,
                    config.HookPermission,
                    Name,
                    nameof(HandleGuiRequest),
                    panelIcon,
                    config.PanelOrder);

                bool registered = result is bool && (bool)result;
                if (!registered)
                {
                    result = serverAssist.Call(
                        "RegisterGuiHook",
                        PanelId,
                        config.HookLabel,
                        config.HookDescription,
                        config.HookPermission,
                        Name,
                        nameof(HandleGuiRequest));
                    registered = result is bool && (bool)result;
                }

                if (registered)
                {
                    serverAssist.Call("SetPanelIcon", PanelId, panelIcon);
                }
            }
            catch (Exception exception)
            {
                PrintWarning("Could not register the Server Assist panel: " + exception.Message);
            }
        }

        private void RemoveServerAssistPanel()
        {
            if (serverAssist == null)
            {
                return;
            }

            serverAssist.Call("UnregisterPanel", PanelId);
        }

        private void OpenPanel(IPlayer player)
        {
            OpenPanelWithAction(player, "a|open");
        }

        private void OpenPanelWithAction(IPlayer player, string action)
        {
            if (player == null)
            {
                return;
            }

            if (!IsAdmin(player) && !permission.UserHasPermission(player.Id, config.HookPermission))
            {
                Reply(player, "You need " + config.HookPermission + ".");
                return;
            }

            serverAssist = plugins.Find(ServerAssistPluginName);
            if (serverAssist == null)
            {
                Reply(player, "Server Assist is not loaded.");
                return;
            }

            object opened = serverAssist.Call("OpenPanel", player.Id, PanelId, action);
            if (!(opened is bool && (bool)opened))
            {
                Reply(player, "Open Server Assist first, then try again.");
            }
        }

        private void RefreshPanel(IPlayer player)
        {
            if (player == null || serverAssist == null)
            {
                return;
            }

            serverAssist.Call("OpenPanel", player.Id, PanelId, "a|refresh");
        }

        public string HandleGuiRequest(string playerId, string payload)
        {
            try
            {
                return HandleGuiRequestInternal(playerId, payload);
            }
            catch (Exception exception)
            {
                PrintWarning("Panel request failed: " + exception);
                return BuildErrorView("The Twitch panel hit an error. Check the server log.");
            }
        }

        private string HandleGuiRequestInternal(string playerId, string payload)
        {
            IPlayer player = players.FindPlayerById(playerId);
            if (player == null)
            {
                return BuildErrorView("Player not found.");
            }

            if (!IsAdmin(player) && !permission.UserHasPermission(playerId, config.HookPermission))
            {
                return BuildErrorView("You need " + config.HookPermission + " to open this panel.");
            }

            PanelSession session = GetPanelSession(playerId);
            PlayerProfile profile = GetProfile(playerId);

            if (string.IsNullOrEmpty(payload) || payload == "a|open")
            {
                session.Page = "home";
                session.Status = string.Empty;
                ClearInput(session);
                return RenderView(player, profile, session);
            }

            if (payload.StartsWith("t|", StringComparison.Ordinal))
            {
                return HandleTextInput(player, profile, session, Decode(payload.Substring(2)));
            }

            string[] parts = payload.Split('|');
            if (parts.Length < 2 || parts[0] != "a")
            {
                return RenderView(player, profile, session);
            }

            string action = parts[1];
            if (action != "admininput")
            {
                ClearInput(session);
            }

            switch (action)
            {
                case "refresh":
                case "noop":
                    break;

                case "link":
                    StartLink(player);
                    session.Page = "home";
                    break;

                case "linkcancel":
                    CancelLink(player);
                    session.Page = "home";
                    break;

                case "unlink":
                    Unlink(player, player);
                    session.Page = "home";
                    break;

                case "toggle":
                    if (parts.Length >= 3)
                    {
                        ToggleSetting(player, profile, parts[2]);
                    }
                    session.Page = "home";
                    break;

                case "tiers":
                    session.Page = "tiers";
                    session.TierId = string.Empty;
                    session.Category = string.Empty;
                    session.PageIndex = 0;
                    break;

                case "tier":
                    if (parts.Length >= 3 && FindTier(parts[2]) != null)
                    {
                        session.Page = "tier";
                        session.TierId = parts[2];
                        session.Category = string.Empty;
                        session.PageIndex = 0;
                    }
                    break;

                case "cat":
                    if (parts.Length >= 4 && FindTier(parts[2]) != null)
                    {
                        session.Page = "category";
                        session.TierId = parts[2];
                        session.Category = NormalizeCategory(parts[3]);
                        session.PageIndex = 0;
                    }
                    break;

                case "pick":
                    if (parts.Length >= 5)
                    {
                        TogglePick(player, profile, parts[2], parts[3], parts[4]);
                        session.Page = "category";
                        session.TierId = parts[2];
                        session.Category = NormalizeCategory(parts[3]);
                    }
                    break;

                case "clear":
                    if (parts.Length >= 4)
                    {
                        ClearCategory(profile, parts[2], parts[3]);
                        session.Status = "Category cleared.";
                        session.Page = "category";
                        session.TierId = parts[2];
                        session.Category = NormalizeCategory(parts[3]);
                    }
                    break;

                case "clearall":
                    if (parts.Length >= 3)
                    {
                        ClearAll(profile, parts[2]);
                        session.Status = "Loadout cleared.";
                        session.Page = "tier";
                        session.TierId = parts[2];
                    }
                    break;

                case "save":
                    session.Status = "Saved. The server randomly picks one of your shortlisted effects when the tier fires.";
                    break;

                case "catalog":
                    session.Page = "catalog";
                    session.TierId = string.Empty;
                    session.Category = parts.Length >= 3 ? NormalizeCategory(parts[2]) : string.Empty;
                    session.PageIndex = 0;
                    break;

                case "catalogtier":
                    if (parts.Length >= 3 && FindTier(parts[2]) != null)
                    {
                        session.Page = "catalog";
                        session.TierId = parts[2];
                        session.Category = parts.Length >= 4 ? NormalizeCategory(parts[3]) : string.Empty;
                        session.PageIndex = 0;
                    }
                    break;

                case "admin":
                    if (IsAdmin(player))
                    {
                        session.Page = "admin";
                    }
                    break;

                case "admininput":
                    if (IsAdmin(player) && parts.Length >= 3)
                    {
                        string field = parts[2].ToLowerInvariant();
                        if (field == "relayurl" || field == "serverid" || field == "serverkey")
                        {
                            session.Page = "admin";
                            session.InputMode = field;
                            session.InputLabel = field == "relayurl"
                                ? "Relay URL (https://relay.example.com)"
                                : field == "serverid"
                                    ? "Server ID"
                                    : "Relay API key (paste, then submit)";
                            session.InputValue = field == "relayurl"
                                ? config.RelayUrl
                                : field == "serverid"
                                    ? config.ServerId
                                    : string.Empty;
                            session.Status = field == "serverkey"
                                ? "Paste the key from relay.json. It will not be displayed again."
                                : "Edit the value and submit.";
                        }
                    }
                    break;

                case "adminrelaytest":
                    if (IsAdmin(player))
                    {
                        session.Page = "admin";
                        session.Status = "Testing relay connection...";
                        TestRelayConnection(player);
                    }
                    break;

                case "adminreload":
                    if (IsAdmin(player))
                    {
                        LoadConfig();
                        RegisterConfiguredPermissions();
                        TryRegisterServerAssistPanel();
                        session.Status = "Config reloaded.";
                        session.Page = "admin";
                    }
                    break;

                case "adminreconcile":
                    if (IsAdmin(player))
                    {
                        ReconcileRelay(player);
                        session.Status = "Reconcile requested.";
                        session.Page = "admin";
                    }
                    break;

                case "admintest":
                    if (IsAdmin(player))
                    {
                        string type = parts.Length >= 3 ? parts[2] : "bits";
                        long amount = 100;
                        if (parts.Length >= 4)
                        {
                            long.TryParse(parts[3], out amount);
                        }
                        SimulateEvent(player, type, amount);
                        session.Status = "Test event fired: " + type + " " + amount + ".";
                        session.Page = "admin";
                    }
                    break;

                case "page":
                    if (parts.Length >= 3)
                    {
                        int page;
                        if (int.TryParse(parts[2], out page))
                        {
                            session.PageIndex = Math.Max(0, page);
                        }
                    }
                    break;

                case "back":
                    GoBack(session);
                    break;

                default:
                    session.Status = "Unknown action: " + action;
                    break;
            }

            return RenderView(player, profile, session);
        }

        private PanelSession GetPanelSession(string playerId)
        {
            PanelSession session;
            if (!panelSessions.TryGetValue(playerId, out session) || session == null)
            {
                session = new PanelSession();
                panelSessions[playerId] = session;
            }

            return session;
        }

        private void GoBack(PanelSession session)
        {
            if (session.Page == "category")
            {
                session.Page = "tier";
            }
            else if (session.Page == "tier")
            {
                session.Page = "tiers";
            }
            else if (session.Page == "catalog" && !string.IsNullOrEmpty(session.TierId))
            {
                session.TierId = string.Empty;
                session.Category = string.Empty;
            }
            else
            {
                session.Page = "home";
            }
        }

        private void ClearInput(PanelSession session)
        {
            if (session == null)
            {
                return;
            }

            session.InputMode = string.Empty;
            session.InputLabel = string.Empty;
            session.InputValue = string.Empty;
        }

        private string HandleTextInput(IPlayer player, PlayerProfile profile, PanelSession session, string text)
        {
            if (session == null || string.IsNullOrEmpty(session.InputMode))
            {
                session.Status = "Nothing is waiting for text input.";
                return RenderView(player, profile, session);
            }

            if (!IsAdmin(player))
            {
                ClearInput(session);
                session.Status = "Only admins can change the relay destination.";
                return RenderView(player, profile, session);
            }

            string mode = session.InputMode;
            string value = (text ?? string.Empty).Trim();
            ClearInput(session);
            session.Page = "admin";

            if (value.Length > 512)
            {
                value = value.Substring(0, 512);
            }

            switch (mode)
            {
                case "relayurl":
                    if (!IsValidRelayUrl(value))
                    {
                        session.Status = "Use a full http:// or https:// URL.";
                        break;
                    }

                    config.RelayUrl = value.TrimEnd('/');
                    NormalizeConfig();
                    SaveConfig();
                    relayAvailable = false;
                    lastRelayError = string.Empty;
                    session.Status = IsLoopbackRelayUrl(config.RelayUrl)
                        ? "Relay URL saved."
                        : config.RelayUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                            ? "Relay URL saved. Warning: use HTTPS or a VPN over the public internet."
                            : "Relay URL saved.";
                    break;

                case "serverid":
                    value = SanitizeId(value);
                    if (string.IsNullOrEmpty(value))
                    {
                        session.Status = "Server ID cannot be empty.";
                        break;
                    }

                    config.ServerId = value;
                    NormalizeConfig();
                    SaveConfig();
                    relayAvailable = false;
                    lastRelayError = string.Empty;
                    session.Status = "Server ID saved.";
                    break;

                case "serverkey":
                    if (string.IsNullOrEmpty(value))
                    {
                        session.Status = "Key unchanged.";
                        break;
                    }

                    config.ServerKey = Clean(value, 256);
                    NormalizeConfig();
                    SaveConfig();
                    relayAvailable = false;
                    lastRelayError = string.Empty;
                    session.Status = "Server key saved. It will not be displayed again.";
                    break;

                default:
                    session.Status = "Unknown input field.";
                    break;
            }

            return RenderView(player, profile, session);
        }

        private static bool IsValidRelayUrl(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            Uri uri;
            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out uri))
            {
                return false;
            }

            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        }

        private static bool IsLoopbackRelayUrl(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.IsLoopback;
        }

        private void TestRelayConnection(IPlayer player)
        {
            if (!IsAdmin(player))
            {
                return;
            }

            if (string.IsNullOrEmpty(config.RelayUrl) || string.IsNullOrEmpty(config.ServerKey))
            {
                Reply(player, "Set the relay URL and server key first.");
                return;
            }

            SendGet("/api/status", (code, response) =>
            {
                PanelSession session;
                bool hadSession = panelSessions.TryGetValue(player.Id, out session) && session != null;

                if (code == 200)
                {
                    relayAvailable = true;
                    lastRelayError = string.Empty;

                    RelayStatusResponse status = null;
                    try
                    {
                        status = JsonConvert.DeserializeObject<RelayStatusResponse>(response);
                    }
                    catch
                    {
                        // Keep the generic success message.
                    }

                    string summary = status == null
                        ? "Relay OK."
                        : "Relay OK: server=" + status.Server +
                          " | EventSub " + (status.Connected ? "connected" : "disconnected") +
                          " | subscriptions=" + status.Subscriptions +
                          " | pending=" + status.PendingEvents +
                          " | links=" + status.Links;

                    Reply(player, summary);
                    if (hadSession)
                    {
                        session.Status = summary;
                        RefreshPanel(player);
                    }
                    return;
                }

                string message = code == 401
                    ? "Relay rejected the key (401). Check ServerId and ServerKey."
                    : "Relay test failed (" + code + "): " + ExtractError(response);

                relayAvailable = false;
                lastRelayError = message;
                Reply(player, message);
                if (hadSession)
                {
                    session.Status = message;
                    RefreshPanel(player);
                }
            });
        }

        private static string Decode(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("%0A", "\n")
                .Replace("%0D", "\r")
                .Replace("%7C", "|")
                .Replace("%25", "%");
        }

        private string RenderView(IPlayer player, PlayerProfile profile, PanelSession session)
        {
            List<RowInfo> rows = new List<RowInfo>();
            List<ButtonInfo> buttons = new List<ButtonInfo>();
            string subtitle = config.HookLabel + " | link, shortlist, and control your rewards";

            switch (session.Page)
            {
                case "tiers":
                    RenderTiers(player, profile, session, rows, buttons, out subtitle);
                    break;

                case "tier":
                    RenderTier(player, profile, session, rows, buttons, out subtitle);
                    break;

                case "category":
                    RenderCategory(player, profile, session, rows, buttons, out subtitle);
                    break;

                case "catalog":
                    RenderCatalog(player, profile, session, rows, buttons, out subtitle);
                    break;

                case "admin":
                    RenderAdmin(player, profile, session, rows, buttons, out subtitle);
                    break;

                default:
                    RenderHome(player, profile, session, rows, buttons, out subtitle);
                    break;
            }

            return BuildView(session, subtitle, rows, buttons);
        }

        private void RenderHome(IPlayer player, PlayerProfile profile, PanelSession session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            subtitle = "Link your Twitch channel and choose what can trigger on your character.";

            if (profile == null || string.IsNullOrEmpty(profile.TwitchUserId))
            {
                LinkSessionData link;
                if (profile != null && data.LinkSessions.TryGetValue(player.Id, out link) && link != null && link.Status == "pending")
                {
                    long seconds = Math.Max(0, link.ExpiresAtUtc - NowUnix());
                    rows.Add(new RowInfo
                    {
                        Action = "refresh",
                        Label = "Link pending",
                        Sub = "Code " + link.UserCode + " at " + link.VerificationUri + " | " + (seconds / 60) + " min left",
                        Enabled = true,
                        Icon = Icon("LinkPending"),
                        Badge = "WAIT",
                        Tint = "#FFCC66"
                    });
                    rows.Add(new RowInfo
                    {
                        Action = "linkcancel",
                        Label = "Cancel link",
                        Sub = "Stop waiting for this code.",
                        Enabled = true,
                        Icon = Icon("LinkCancel"),
                        Badge = "CANCEL",
                        Tint = "#FF8888"
                    });
                    buttons.Add(new ButtonInfo { Action = "linkcancel", Label = "Cancel", Tooltip = "Cancel the pending Twitch link", Icon = Icon("LinkCancel"), Tint = "#FF8888" });
                }
                else
                {
                    rows.Add(new RowInfo
                    {
                        Action = "link",
                        Label = "Link Twitch",
                        Sub = "Get a code, approve it on twitch.tv/activate. No stream key is used.",
                        Enabled = true,
                        Icon = Icon("Link"),
                        Badge = "LINK",
                        Tint = "#66FF99"
                    });
                    buttons.Add(new ButtonInfo { Action = "link", Label = "Link Twitch", Tooltip = "Start the Twitch device-code link", Icon = Icon("Link"), Tint = "#66FF99" });
                }

                rows.Add(new RowInfo
                {
                    Action = "noop",
                    Label = "How it works",
                    Sub = "Staff define tiers. You pick a shortlist for buffs, debuffs, and rewards. One is rolled when a viewer triggers it.",
                    Enabled = false,
                    Icon = Icon("Info"),
                    Badge = "INFO"
                });
            }
            else
            {
                rows.Add(new RowInfo
                {
                    Action = "unlink",
                    Label = "Linked as " + profile.TwitchDisplayName,
                    Sub = profile.TwitchLogin + " | Twitch ID " + profile.TwitchUserId + " | click to unlink",
                    Enabled = true,
                    Icon = Icon("Linked"),
                    Badge = "LINKED",
                    Tint = "#66FF99"
                });

                AddToggleRow(rows, "Opt-in", "optin", profile.OptedIn, "Master switch for Twitch effects on your character.");
                AddToggleRow(rows, "Pause all", "paused", profile.Paused, "Temporarily stop all Twitch effects.");
                AddToggleRow(rows, "Allow buffs", "buffs", profile.AllowBuffs, "Let your buff shortlist apply.");
                AddToggleRow(rows, "Allow debuffs", "debuffs", profile.AllowDebuffs, "Let your debuff shortlist apply.");
                AddToggleRow(rows, "Allow rewards", "rewards", profile.AllowRewards, "Let your reward shortlist apply.");

                int unlocked = config.Tiers.Count(tier => HasTierPermission(player, tier));
                int armed = config.Tiers.Count(tier => HasTierPermission(player, tier) && IsTierArmed(profile, tier));

                rows.Add(new RowInfo
                {
                    Action = "tiers",
                    Label = "My tier loadouts",
                    Sub = unlocked + " unlocked | " + armed + " armed | choose shortlists per tier",
                    Enabled = true,
                    Icon = Icon("Loadout"),
                    Badge = "LOADOUT",
                    Tint = "#66CCFF"
                });

                rows.Add(new RowInfo
                {
                    Action = "catalog",
                    Label = "Reward catalog",
                    Sub = "See every buff, debuff, and reward staff have defined for your tiers.",
                    Enabled = true,
                    Icon = Icon("Catalog"),
                    Badge = "BROWSE",
                    Tint = "#CCCCFF"
                });

                if (IsAdmin(player))
                {
                    rows.Add(new RowInfo
                    {
                        Action = "admin",
                        Label = "Admin",
                        Sub = "Relay status, audit trail, test events.",
                        Enabled = true,
                        Icon = Icon("Admin"),
                        Badge = "ADMIN",
                        Tint = "#FFCC66"
                    });
                }
            }

            buttons.Add(new ButtonInfo { Action = "refresh", Label = "Refresh", Tooltip = "Reload this view", Icon = Icon("Refresh") });
            if (!string.IsNullOrEmpty(profile == null ? string.Empty : profile.TwitchUserId))
            {
                buttons.Add(new ButtonInfo { Action = "tiers", Label = "Loadouts", Tooltip = "Choose shortlists", Icon = Icon("Loadout"), Tint = "#66CCFF" });
                buttons.Add(new ButtonInfo { Action = "catalog", Label = "Catalog", Tooltip = "See all effects", Icon = Icon("Catalog"), Tint = "#CCCCFF" });
            }

            if (IsAdmin(player))
            {
                buttons.Add(new ButtonInfo { Action = "admin", Label = "Admin", Tooltip = "Relay and audit", Icon = Icon("Admin"), Tint = "#FFCC66" });
            }

            buttons.Add(new ButtonInfo { Action = "hook|back", Label = "Back", Tooltip = "Return to Server Assist", Icon = Icon("Back") });
        }

        private void AddToggleRow(List<RowInfo> rows, string label, string key, bool value, string description)
        {
            string iconOn;
            string iconOff;

            if (key == "optin")
            {
                iconOn = Icon("StatusArmed");
                iconOff = Icon("StatusOptedOut");
            }
            else if (key == "paused")
            {
                iconOn = Icon("StatusPaused");
                iconOff = Icon("Check");
            }
            else
            {
                iconOn = Icon("Check");
                iconOff = Icon("Cross");
            }

            rows.Add(new RowInfo
            {
                Action = "toggle|" + key,
                Label = label + ": " + (value ? "ON" : "OFF"),
                Sub = description,
                Enabled = true,
                Selected = value,
                Icon = value ? iconOn : iconOff,
                Badge = value ? "ON" : "OFF",
                Tint = value ? "#66FF99" : "#FF8888"
            });
        }

        private void RenderTiers(IPlayer player, PlayerProfile profile, PanelSession session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            subtitle = "Tier loadouts | pick a tier to choose its shortlist";

            int count = 0;
            foreach (TierDefinition tier in config.Tiers.OrderBy(tier => tier.Order))
            {
                if (count >= UiRows)
                {
                    break;
                }

                bool allowed = HasTierPermission(player, tier);
                TierLoadout loadout = GetLoadout(profile, tier.Id);
                bool armed = IsTierArmed(profile, tier);

                rows.Add(new RowInfo
                {
                    Action = "tier|" + tier.Id,
                    Label = tier.DisplayName,
                    Sub = "Buffs " + ValidPickCount(tier.Buffs, loadout.Buffs) + " | Debuffs " + ValidPickCount(tier.Debuffs, loadout.Debuffs) +
                          " | Rewards " + ValidPickCount(tier.Rewards, loadout.Rewards) +
                          (armed ? " | ARMED" : " | not armed"),
                    Enabled = allowed,
                    Icon = string.IsNullOrEmpty(tier.Icon) ? Icon("Tier") : tier.Icon,
                    Badge = allowed ? (armed ? "ARMED" : "OPEN") : "LOCKED",
                    Tint = allowed ? (armed ? "#66FF99" : "#FFCC66") : "#888888"
                });
                count++;
            }

            if (count == 0)
            {
                rows.Add(new RowInfo
                {
                    Action = "noop",
                    Label = "No tiers configured",
                    Sub = "Staff must add tiers to oxide/config/TwitchIntegration.json.",
                    Enabled = false,
                    Icon = Icon("Info"),
                    Badge = "EMPTY"
                });
            }

            buttons.Add(new ButtonInfo { Action = "back", Label = "Back", Tooltip = "Return to the Twitch home page", Icon = Icon("Back") });
            buttons.Add(new ButtonInfo { Action = "catalog", Label = "Catalog", Tooltip = "See all effects", Icon = Icon("Catalog") });
            buttons.Add(new ButtonInfo { Action = "refresh", Label = "Refresh", Tooltip = "Reload this view", Icon = Icon("Refresh") });
            buttons.Add(new ButtonInfo { Action = "hook|back", Label = "Server Assist", Tooltip = "Return to Server Assist", Icon = Icon("Home") });
        }

        private void RenderTier(IPlayer player, PlayerProfile profile, PanelSession session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            TierDefinition tier = FindTier(session.TierId);
            if (tier == null)
            {
                session.Page = "tiers";
                RenderTiers(player, profile, session, rows, buttons, out subtitle);
                return;
            }

            if (!HasTierPermission(player, tier))
            {
                subtitle = "This tier is locked by permission " + tier.Permission + ".";
                rows.Add(new RowInfo
                {
                    Action = "noop",
                    Label = "Locked",
                    Sub = "Ask staff for " + tier.Permission + ".",
                    Enabled = false,
                    Icon = Icon("LoadoutLocked"),
                    Badge = "LOCKED",
                    Tint = "#888888"
                });
                buttons.Add(new ButtonInfo { Action = "back", Label = "Back", Tooltip = "Return to tiers", Icon = Icon("Back") });
                return;
            }

            TierLoadout loadout = GetLoadout(profile, tier.Id);
            subtitle = tier.DisplayName + " | " + (IsTierArmed(profile, tier) ? "ARMED" : "fill every category to arm") +
                       " | " + tier.Permission;

            rows.Add(new RowInfo
            {
                Action = "cat|" + tier.Id + "|buff",
                Label = "Buffs",
                Sub = ValidPickCount(tier.Buffs, loadout.Buffs) + " selected | shortlist up to " + tier.MaxPicks,
                Enabled = tier.Buffs.Count > 0,
                Selected = ValidPickCount(tier.Buffs, loadout.Buffs) > 0,
                Icon = Icon("Buff"),
                Badge = tier.Buffs.Count > 0 ? "BUFF" : "NONE"
            });

            rows.Add(new RowInfo
            {
                Action = "cat|" + tier.Id + "|debuff",
                Label = "Debuffs",
                Sub = ValidPickCount(tier.Debuffs, loadout.Debuffs) + " selected | shortlist up to " + tier.MaxPicks,
                Enabled = tier.Debuffs.Count > 0,
                Selected = ValidPickCount(tier.Debuffs, loadout.Debuffs) > 0,
                Icon = Icon("Debuff"),
                Badge = tier.Debuffs.Count > 0 ? "DEBUFF" : "NONE"
            });

            rows.Add(new RowInfo
            {
                Action = "cat|" + tier.Id + "|reward",
                Label = "Rewards",
                Sub = ValidPickCount(tier.Rewards, loadout.Rewards) + " selected | shortlist up to " + tier.MaxPicks,
                Enabled = tier.Rewards.Count > 0,
                Selected = ValidPickCount(tier.Rewards, loadout.Rewards) > 0,
                Icon = Icon("Reward"),
                Badge = tier.Rewards.Count > 0 ? "REWARD" : "NONE"
            });

            rows.Add(new RowInfo
            {
                Action = "save|" + tier.Id,
                Label = "How the roll works",
                Sub = "When a matching viewer event fires, the server picks one buff, one debuff, and one reward from your shortlists.",
                Enabled = false,
                Icon = Icon("Info"),
                Badge = "INFO"
            });

            rows.Add(new RowInfo
            {
                Action = "clearall|" + tier.Id,
                Label = "Clear this loadout",
                Sub = "Remove all picks for this tier.",
                Enabled = true,
                Icon = Icon("Cross"),
                Badge = "RESET",
                Tint = "#FF8888"
            });

            buttons.Add(new ButtonInfo { Action = "back", Label = "Back", Tooltip = "Return to tiers", Icon = Icon("Back") });
            buttons.Add(new ButtonInfo { Action = "cat|" + tier.Id + "|buff", Label = "Buffs", Tooltip = "Pick buff candidates", Icon = Icon("Buff") });
            buttons.Add(new ButtonInfo { Action = "cat|" + tier.Id + "|debuff", Label = "Debuffs", Tooltip = "Pick debuff candidates", Icon = Icon("Debuff") });
            buttons.Add(new ButtonInfo { Action = "cat|" + tier.Id + "|reward", Label = "Rewards", Tooltip = "Pick reward candidates", Icon = Icon("Reward") });
            buttons.Add(new ButtonInfo { Action = "catalogtier|" + tier.Id, Label = "Preview", Tooltip = "See all staff effects for this tier", Icon = Icon("Catalog") });
            buttons.Add(new ButtonInfo { Action = "refresh", Label = "Refresh", Tooltip = "Reload this view", Icon = Icon("Refresh") });
            buttons.Add(new ButtonInfo { Action = "hook|back", Label = "Server Assist", Tooltip = "Return to Server Assist", Icon = Icon("Home") });
        }

        private void RenderCategory(IPlayer player, PlayerProfile profile, PanelSession session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            TierDefinition tier = FindTier(session.TierId);
            List<EffectDefinition> pool = GetPool(tier, session.Category);
            if (tier == null || pool == null)
            {
                session.Page = "tiers";
                RenderTiers(player, profile, session, rows, buttons, out subtitle);
                return;
            }

            if (!HasTierPermission(player, tier))
            {
                subtitle = "This tier is locked.";
                buttons.Add(new ButtonInfo { Action = "back", Label = "Back", Tooltip = "Return" });
                return;
            }

            TierLoadout loadout = GetLoadout(profile, tier.Id);
            List<string> selected = GetSelected(loadout, session.Category);

            int pageSize = UiRows - 1;
            int maxPage = Math.Max(0, (pool.Count - 1) / pageSize);
            session.PageIndex = Math.Max(0, Math.Min(maxPage, session.PageIndex));

            subtitle = tier.DisplayName + " | " + CategoryLabel(session.Category) + " | " +
                       selected.Count + " picked | max " + tier.MaxPicks;

            int start = session.PageIndex * pageSize;
            for (int i = start; i < pool.Count && rows.Count < pageSize; i++)
            {
                EffectDefinition effect = pool[i];
                bool picked = selected.Contains(effect.Id);
                rows.Add(new RowInfo
                {
                    Action = "pick|" + tier.Id + "|" + session.Category + "|" + effect.Id,
                    Label = effect.Label,
                    Sub = effect.Description + (effect.Weight > 1 ? " | weight " + effect.Weight : string.Empty),
                    Enabled = true,
                    Selected = picked,
                    Icon = string.IsNullOrEmpty(effect.Icon) ? CategoryIcon(session.Category) : effect.Icon,
                    Badge = picked ? "PICKED" : string.Empty,
                    Tint = picked ? "#66FF99" : string.Empty
                });
            }

            if (pool.Count == 0)
            {
                rows.Add(new RowInfo
                {
                    Action = "noop",
                    Label = "No effects configured",
                    Sub = "Staff have not added any " + CategoryLabel(session.Category).ToLowerInvariant() + " for this tier.",
                    Enabled = false,
                    Icon = Icon("Info"),
                    Badge = "EMPTY"
                });
            }

            buttons.Add(new ButtonInfo { Action = "back", Label = "Back", Tooltip = "Return to the tier", Icon = Icon("Back") });
            buttons.Add(new ButtonInfo { Action = "clear|" + tier.Id + "|" + session.Category, Label = "Clear", Tooltip = "Clear this category", Icon = Icon("Cross"), Tint = "#FF8888" });
            if (session.PageIndex > 0)
            {
                buttons.Add(new ButtonInfo { Action = "page|" + (session.PageIndex - 1), Label = "Prev", Tooltip = "Previous page", Icon = Icon("Prev") });
            }
            if (session.PageIndex < maxPage)
            {
                buttons.Add(new ButtonInfo { Action = "page|" + (session.PageIndex + 1), Label = "Next", Tooltip = "Next page", Icon = Icon("Next") });
            }
            buttons.Add(new ButtonInfo { Action = "save|" + tier.Id, Label = "Save", Tooltip = "Picks save automatically", Icon = Icon("Check"), Tint = "#66FF99" });
            buttons.Add(new ButtonInfo { Action = "refresh", Label = "Refresh", Tooltip = "Reload this view", Icon = Icon("Refresh") });
            buttons.Add(new ButtonInfo { Action = "hook|back", Label = "Server Assist", Tooltip = "Return to Server Assist", Icon = Icon("Home") });
        }

        private void RenderCatalog(IPlayer player, PlayerProfile profile, PanelSession session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            if (string.IsNullOrEmpty(session.TierId))
            {
                subtitle = "Reward catalog | choose a tier";
                foreach (TierDefinition tier in config.Tiers.OrderBy(tier => tier.Order).Take(UiRows))
                {
                    bool allowed = HasTierPermission(player, tier);
                    rows.Add(new RowInfo
                    {
                        Action = "catalogtier|" + tier.Id,
                        Label = tier.DisplayName,
                        Sub = tier.Description + " | " + tier.Buffs.Count + " buffs, " + tier.Debuffs.Count + " debuffs, " + tier.Rewards.Count + " rewards",
                        Enabled = allowed,
                        Icon = string.IsNullOrEmpty(tier.Icon) ? Icon("Tier") : tier.Icon,
                        Badge = allowed ? "VIEW" : "LOCKED",
                        Tint = allowed ? "#CCCCFF" : "#888888"
                    });
                }

                buttons.Add(new ButtonInfo { Action = "back", Label = "Back", Tooltip = "Return home", Icon = Icon("Back") });
                buttons.Add(new ButtonInfo { Action = "refresh", Label = "Refresh", Tooltip = "Reload this view", Icon = Icon("Refresh") });
                buttons.Add(new ButtonInfo { Action = "hook|back", Label = "Server Assist", Tooltip = "Return to Server Assist", Icon = Icon("Home") });
                return;
            }

            TierDefinition selectedTier = FindTier(session.TierId);
            if (selectedTier == null)
            {
                session.TierId = string.Empty;
                RenderCatalog(player, profile, session, rows, buttons, out subtitle);
                return;
            }

            List<EffectDefinition> pool = new List<EffectDefinition>();
            if (string.IsNullOrEmpty(session.Category) || session.Category == "buff")
            {
                pool.AddRange(selectedTier.Buffs);
            }
            if (string.IsNullOrEmpty(session.Category) || session.Category == "debuff")
            {
                pool.AddRange(selectedTier.Debuffs);
            }
            if (string.IsNullOrEmpty(session.Category) || session.Category == "reward")
            {
                pool.AddRange(selectedTier.Rewards);
            }

            int pageSize = UiRows;
            int maxPage = Math.Max(0, (pool.Count - 1) / pageSize);
            session.PageIndex = Math.Max(0, Math.Min(maxPage, session.PageIndex));

            subtitle = selectedTier.DisplayName + " catalog | " + pool.Count + " effects";
            int start = session.PageIndex * pageSize;
            for (int i = start; i < pool.Count && rows.Count < pageSize; i++)
            {
                EffectDefinition effect = pool[i];
                rows.Add(new RowInfo
                {
                    Action = "noop",
                    Label = effect.Label,
                    Sub = effect.Description + " | weight " + effect.Weight,
                    Enabled = false,
                    Icon = string.IsNullOrEmpty(effect.Icon) ? Icon("Catalog") : effect.Icon,
                    Badge = "POOL"
                });
            }

            buttons.Add(new ButtonInfo { Action = "back", Label = "Back", Tooltip = "Return to catalog tiers", Icon = Icon("Back") });
            buttons.Add(new ButtonInfo { Action = "catalogtier|" + selectedTier.Id + "|buff", Label = "Buffs", Tooltip = "Show buffs", Icon = Icon("Buff") });
            buttons.Add(new ButtonInfo { Action = "catalogtier|" + selectedTier.Id + "|debuff", Label = "Debuffs", Tooltip = "Show debuffs", Icon = Icon("Debuff") });
            buttons.Add(new ButtonInfo { Action = "catalogtier|" + selectedTier.Id + "|reward", Label = "Rewards", Tooltip = "Show rewards", Icon = Icon("Reward") });
            buttons.Add(new ButtonInfo { Action = "catalogtier|" + selectedTier.Id, Label = "All", Tooltip = "Show everything", Icon = Icon("Catalog") });
            if (session.PageIndex > 0)
            {
                buttons.Add(new ButtonInfo { Action = "page|" + (session.PageIndex - 1), Label = "Prev", Tooltip = "Previous page", Icon = Icon("Prev") });
            }
            if (session.PageIndex < maxPage)
            {
                buttons.Add(new ButtonInfo { Action = "page|" + (session.PageIndex + 1), Label = "Next", Tooltip = "Next page", Icon = Icon("Next") });
            }
            buttons.Add(new ButtonInfo { Action = "hook|back", Label = "Server Assist", Tooltip = "Return to Server Assist", Icon = Icon("Home") });
        }

        private void RenderAdmin(IPlayer player, PlayerProfile profile, PanelSession session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            subtitle = "Admin | relay destination, status, and recent audit";
            int linked = data.Players.Values.Count(p => !string.IsNullOrEmpty(p.TwitchUserId));
            int armed = data.Players.Values.Count(p => config.Tiers.Any(t => IsTierArmed(p, t)));

            rows.Add(new RowInfo
            {
                Action = "noop",
                Label = "Relay: " + (relayAvailable ? "online" : "offline"),
                Sub = string.IsNullOrEmpty(lastRelayError) ? config.RelayUrl : lastRelayError,
                Enabled = false,
                Icon = Icon(!relayAvailable ? "RelayOffline" : pendingEventCount > 0 ? "RelayPending" : "RelayOnline"),
                Badge = relayAvailable ? "UP" : "DOWN",
                Tint = relayAvailable ? "#66FF99" : "#FF8888"
            });

            rows.Add(new RowInfo
            {
                Action = "admininput|relayurl",
                Label = "Relay URL: " + (string.IsNullOrEmpty(config.RelayUrl) ? "not set" : config.RelayUrl),
                Sub = "Tap to edit the destination. Use https:// for a remote relay.",
                Enabled = true,
                Icon = Icon("Relay"),
                Badge = "EDIT",
                Tint = "#66CCFF"
            });

            rows.Add(new RowInfo
            {
                Action = "admininput|serverid",
                Label = "Server ID: " + config.ServerId,
                Sub = "Must match the relay's Servers entry.",
                Enabled = true,
                Icon = Icon("AdminLinks"),
                Badge = "EDIT"
            });

            rows.Add(new RowInfo
            {
                Action = "admininput|serverkey",
                Label = "Server Key: " + (string.IsNullOrEmpty(config.ServerKey) ? "not set" : "configured"),
                Sub = "Tap to paste the API key from relay.json. It is never shown.",
                Enabled = true,
                Icon = Icon("Lock"),
                Badge = "KEY",
                Tint = string.IsNullOrEmpty(config.ServerKey) ? "#FF8888" : "#66FF99"
            });

            rows.Add(new RowInfo
            {
                Action = "adminrelaytest",
                Label = "Test relay connection",
                Sub = "Checks the URL, key, and EventSub state.",
                Enabled = true,
                Icon = Icon("Test"),
                Badge = "TEST",
                Tint = "#FFCC66"
            });

            rows.Add(new RowInfo
            {
                Action = "noop",
                Label = "Links: " + linked + " | armed players: " + armed,
                Sub = "Pending relay events: " + pendingEventCount + " | server " + config.ServerId,
                Enabled = false,
                Icon = Icon("AdminPlayers"),
                Badge = "STATS"
            });

            int shown = 0;
            foreach (string line in data.Audit)
            {
                if (shown >= 4 || rows.Count >= UiRows)
                {
                    break;
                }

                rows.Add(new RowInfo
                {
                    Action = "noop",
                    Label = "Audit",
                    Sub = line,
                    Enabled = false,
                    Icon = Icon("Audit"),
                    Badge = "LOG"
                });
                shown++;
            }

            buttons.Add(new ButtonInfo { Action = "adminreload", Label = "Reload", Tooltip = "Reload config and permissions", Icon = Icon("AdminReload") });
            buttons.Add(new ButtonInfo { Action = "adminrelaytest", Label = "Test Relay", Tooltip = "Test the relay URL and key", Icon = Icon("Test") });
            buttons.Add(new ButtonInfo { Action = "adminreconcile", Label = "Reconcile", Tooltip = "Ask the relay to recreate EventSub subscriptions", Icon = Icon("AdminReconcile") });
            buttons.Add(new ButtonInfo { Action = "admintest|bits|100", Label = "Test Bits", Tooltip = "Fire a 100-bit test", Icon = Icon("TestBits") });
            buttons.Add(new ButtonInfo { Action = "admintest|sub|1", Label = "Test Sub", Tooltip = "Fire a subscription test", Icon = Icon("TestSub") });
            buttons.Add(new ButtonInfo { Action = "admintest|channel_points|0", Label = "Test Points", Tooltip = "Fire a channel point test", Icon = Icon("TestPoints") });
            buttons.Add(new ButtonInfo { Action = "refresh", Label = "Refresh", Tooltip = "Reload this view", Icon = Icon("Refresh") });
            buttons.Add(new ButtonInfo { Action = "back", Label = "Back", Tooltip = "Return home", Icon = Icon("Back") });
        }

        private string BuildView(PanelSession session, string subtitle, List<RowInfo> rows, List<ButtonInfo> buttons)
        {
            StringBuilder builder = new StringBuilder(4096);
            builder.Append("v2\n");
            builder.Append("protocol=2\n");
            builder.Append("title=").Append(Encode(config.HookLabel)).Append('\n');
            builder.Append("subtitle=").Append(Encode(subtitle)).Append('\n');
            builder.Append("status=").Append(Encode(session.Status)).Append('\n');
            string panelIcon = Icon("Panel");
            builder.Append("background=").Append(Encode(panelIcon)).Append('\n');
            builder.Append("banner=").Append(Encode(panelIcon)).Append('\n');
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
                    .Append(row.Enabled ? "1" : "0").Append('|')
                    .Append(row.Selected ? "1" : "0").Append('|')
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
                    .Append(Encode(button.Tooltip)).Append('|')
                    .Append(Encode(button.Icon)).Append('|')
                    .Append(Encode(button.Tint)).Append('\n');
            }

            return builder.ToString();
        }

        private string BuildErrorView(string message)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("v2\n");
            builder.Append("protocol=2\n");
            builder.Append("title=").Append(Encode(config == null ? "Twitch" : config.HookLabel)).Append('\n');
            builder.Append("subtitle=").Append(Encode("Unavailable")).Append('\n');
            builder.Append("status=").Append(Encode(message)).Append('\n');
            builder.Append("input=||0|\n");
            builder.Append("row=0|noop|").Append(Encode("Twitch Integration")).Append('|').Append(Encode(message))
                .Append("|0|0|").Append(Encode(config == null ? "ui_game_symbol_x" : Icon("Cross"))).Append("|ERROR|#FF6B6B\n");
            builder.Append("btn=0|refresh|").Append(Encode("Retry")).Append("|1||").Append(Encode(config == null ? string.Empty : Icon("Refresh"))).Append("|\n");
            return builder.ToString();
        }

        private sealed class RowInfo
        {
            public string Action = "";
            public string Label = "";
            public string Sub = "";
            public bool Enabled = true;
            public bool Selected = false;
            public string Icon = "";
            public string Badge = "";
            public string Tint = "";
        }

        private sealed class ButtonInfo
        {
            public string Action = "";
            public string Label = "";
            public bool Enabled = true;
            public string Tooltip = "";
            public string Icon = "";
            public string Tint = "";
        }

        private sealed class PanelSession
        {
            public string Page = "home";
            public string TierId = "";
            public string Category = "";
            public int PageIndex = 0;
            public string Status = "";
            public string InputMode = "";
            public string InputLabel = "";
            public string InputValue = "";
        }

        #endregion

        #region Loadout logic

        private void ToggleSetting(IPlayer player, PlayerProfile profile, string key)
        {
            if (profile == null)
            {
                return;
            }

            switch (key)
            {
                case "optin":
                    profile.OptedIn = !profile.OptedIn;
                    break;
                case "paused":
                    profile.Paused = !profile.Paused;
                    break;
                case "buffs":
                    profile.AllowBuffs = !profile.AllowBuffs;
                    break;
                case "debuffs":
                    profile.AllowDebuffs = !profile.AllowDebuffs;
                    break;
                case "rewards":
                    profile.AllowRewards = !profile.AllowRewards;
                    break;
            }

            SaveData();
        }

        private void TogglePick(IPlayer player, PlayerProfile profile, string tierId, string category, string effectId)
        {
            TierDefinition tier = FindTier(tierId);
            if (tier == null || !HasTierPermission(player, tier))
            {
                return;
            }

            List<EffectDefinition> pool = GetPool(tier, NormalizeCategory(category));
            if (pool == null || pool.All(effect => effect.Id != effectId))
            {
                return;
            }

            TierLoadout loadout = GetLoadout(profile, tier.Id);
            List<string> selected = GetSelected(loadout, NormalizeCategory(category));
            if (selected == null)
            {
                return;
            }

            if (selected.Contains(effectId))
            {
                selected.Remove(effectId);
            }
            else
            {
                if (selected.Count >= tier.MaxPicks)
                {
                    PanelSession session = GetPanelSession(player.Id);
                    session.Status = "You can shortlist at most " + tier.MaxPicks + " " + CategoryLabel(category) + ". Deselect one first.";
                    return;
                }

                selected.Add(effectId);
            }

            loadout.UpdatedAtUtc = NowUnix();
            ProfileCleanup(profile, tier);
            SaveData();
        }

        private void ClearCategory(PlayerProfile profile, string tierId, string category)
        {
            TierDefinition tier = FindTier(tierId);
            if (tier == null)
            {
                return;
            }

            TierLoadout loadout = GetLoadout(profile, tier.Id);
            List<string> selected = GetSelected(loadout, NormalizeCategory(category));
            if (selected != null)
            {
                selected.Clear();
            }

            loadout.UpdatedAtUtc = NowUnix();
            SaveData();
        }

        private void ClearAll(PlayerProfile profile, string tierId)
        {
            TierDefinition tier = FindTier(tierId);
            if (tier == null)
            {
                return;
            }

            TierLoadout loadout = GetLoadout(profile, tier.Id);
            loadout.Buffs.Clear();
            loadout.Debuffs.Clear();
            loadout.Rewards.Clear();
            loadout.UpdatedAtUtc = NowUnix();
            SaveData();
        }

        private void ProfileCleanup(PlayerProfile profile, TierDefinition tier)
        {
            TierLoadout loadout = GetLoadout(profile, tier.Id);
            loadout.Buffs.RemoveAll(id => tier.Buffs.All(effect => effect.Id != id));
            loadout.Debuffs.RemoveAll(id => tier.Debuffs.All(effect => effect.Id != id));
            loadout.Rewards.RemoveAll(id => tier.Rewards.All(effect => effect.Id != id));
        }

        private bool IsTierArmed(PlayerProfile profile, TierDefinition tier)
        {
            if (profile == null || tier == null || !tier.Enabled)
            {
                return false;
            }

            TierLoadout loadout = GetLoadout(profile, tier.Id);
            int min = Math.Max(1, tier.MinPicks);

            bool buffsOk = tier.Buffs.Count == 0 || ValidPickCount(tier.Buffs, loadout.Buffs) >= min;
            bool debuffsOk = tier.Debuffs.Count == 0 || ValidPickCount(tier.Debuffs, loadout.Debuffs) >= min;
            bool rewardsOk = tier.Rewards.Count == 0 || ValidPickCount(tier.Rewards, loadout.Rewards) >= min;

            if (config.RequireAllCategories)
            {
                return buffsOk && debuffsOk && rewardsOk;
            }

            return buffsOk || debuffsOk || rewardsOk;
        }

        private int ValidPickCount(List<EffectDefinition> pool, List<string> selected)
        {
            if (pool == null || selected == null)
            {
                return 0;
            }

            return selected.Count(id => pool.Any(effect => effect.Id == id));
        }

        private List<EffectDefinition> GetPool(TierDefinition tier, string category)
        {
            if (tier == null)
            {
                return null;
            }

            switch (NormalizeCategory(category))
            {
                case "buff":
                    return tier.Buffs;
                case "debuff":
                    return tier.Debuffs;
                case "reward":
                    return tier.Rewards;
                default:
                    return null;
            }
        }

        private List<string> GetSelected(TierLoadout loadout, string category)
        {
            if (loadout == null)
            {
                return null;
            }

            switch (NormalizeCategory(category))
            {
                case "buff":
                    return loadout.Buffs;
                case "debuff":
                    return loadout.Debuffs;
                case "reward":
                    return loadout.Rewards;
                default:
                    return null;
            }
        }

        private static string NormalizeCategory(string category)
        {
            string value = (category ?? string.Empty).Trim().ToLowerInvariant();
            if (value == "buffs")
            {
                return "buff";
            }
            if (value == "debuffs")
            {
                return "debuff";
            }
            if (value == "rewards")
            {
                return "reward";
            }
            return value;
        }

        private static string CategoryLabel(string category)
        {
            switch (NormalizeCategory(category))
            {
                case "buff":
                    return "Buffs";
                case "debuff":
                    return "Debuffs";
                case "reward":
                    return "Rewards";
                default:
                    return "Effects";
            }
        }

        private string CategoryIcon(string category)
        {
            switch (NormalizeCategory(category))
            {
                case "buff":
                    return Icon("Buff");
                case "debuff":
                    return Icon("Debuff");
                case "reward":
                    return Icon("Reward");
                default:
                    return Icon("Catalog");
            }
        }

        private TierDefinition FindTier(string tierId)
        {
            if (string.IsNullOrEmpty(tierId))
            {
                return null;
            }

            return config.Tiers.FirstOrDefault(tier => string.Equals(tier.Id, tierId, StringComparison.OrdinalIgnoreCase));
        }

        #endregion

        #region Linking

        private void StartLink(IPlayer player)
        {
            if (player == null || player.IsServer)
            {
                return;
            }

            if (!IsAdmin(player) && !permission.UserHasPermission(player.Id, config.HookPermission))
            {
                Reply(player, "You need " + config.HookPermission + ".");
                return;
            }

            if (string.IsNullOrEmpty(config.RelayUrl) || string.IsNullOrEmpty(config.ServerKey))
            {
                Reply(player, "The Twitch relay is not configured. Ask staff to set RelayUrl and ServerKey.");
                return;
            }

            PlayerProfile profile = GetProfile(player.Id);
            if (profile != null && !string.IsNullOrEmpty(profile.TwitchUserId))
            {
                Reply(player, "You are already linked. Use /twitch unlink first.");
                return;
            }

            LinkSessionData existing;
            if (data.LinkSessions.TryGetValue(player.Id, out existing) && existing != null && existing.Status == "pending" && existing.ExpiresAtUtc > NowUnix())
            {
                Reply(player, "A link is already pending. Code " + existing.UserCode + " at " + existing.VerificationUri + ".");
                return;
            }

            string body = JsonConvert.SerializeObject(new
            {
                steamId = player.Id,
                playerName = player.Name
            });

            Reply(player, "Requesting a Twitch link code...");

            SendPost("/api/link/start", body, (code, response) =>
            {
                if (code != 200)
                {
                    Reply(player, "Could not start the Twitch link: " + ExtractError(response));
                    return;
                }

                LinkStartResponse link = JsonConvert.DeserializeObject<LinkStartResponse>(response);
                if (link == null || string.IsNullOrEmpty(link.SessionId) || string.IsNullOrEmpty(link.UserCode))
                {
                    Reply(player, "The relay returned an invalid link response.");
                    return;
                }

                data.LinkSessions[player.Id] = new LinkSessionData
                {
                    SessionId = link.SessionId,
                    SteamId = player.Id,
                    UserCode = link.UserCode,
                    VerificationUri = link.VerificationUri,
                    Interval = Math.Max(2, link.Interval),
                    CreatedAtUtc = NowUnix(),
                    ExpiresAtUtc = NowUnix() + Math.Max(60, link.ExpiresIn),
                    NextPollUtc = NowUnix() + Math.Max(2, link.Interval),
                    Status = "pending"
                };
                SaveData();

                Reply(player, "Go to " + link.VerificationUri + " and enter code " + link.UserCode + ". Expires in " + Math.Max(1, link.ExpiresIn / 60) + " minute(s).");
                Reply(player, "No stream key is used. You can revoke access later at twitch.tv/settings/connections.");
                RefreshPanel(player);
            });
        }

        private void CancelLink(IPlayer player)
        {
            LinkSessionData session;
            if (player == null || !data.LinkSessions.TryGetValue(player.Id, out session) || session == null)
            {
                Reply(player, "No link is pending.");
                return;
            }

            string path = "/api/link/cancel?sessionId=" + Uri.EscapeDataString(session.SessionId);
            SendPost(path, "{}", (code, response) =>
            {
                data.LinkSessions.Remove(player.Id);
                SaveData();
                Reply(player, "Twitch link canceled.");
                RefreshPanel(player);
            });
        }

        private void PollLinkSessions()
        {
            if (data == null || data.LinkSessions == null || data.LinkSessions.Count == 0)
            {
                return;
            }

            if (string.IsNullOrEmpty(config.RelayUrl) || string.IsNullOrEmpty(config.ServerKey))
            {
                return;
            }

            long now = NowUnix();
            foreach (KeyValuePair<string, LinkSessionData> pair in data.LinkSessions.ToList())
            {
                string steamId = pair.Key;
                LinkSessionData session = pair.Value;
                if (session == null)
                {
                    data.LinkSessions.Remove(steamId);
                    continue;
                }

                if (session.Status != "pending")
                {
                    if (session.ExpiresAtUtc + 300 < now)
                    {
                        data.LinkSessions.Remove(steamId);
                    }
                    continue;
                }

                if (session.ExpiresAtUtc < now)
                {
                    session.Status = "expired";
                    session.Error = "The Twitch link code expired.";
                    IPlayer expired = players.FindPlayerById(steamId);
                    if (expired != null)
                    {
                        Reply(expired, "The Twitch link code expired. Run /twitch link again.");
                    }
                    SaveData();
                    continue;
                }

                if (linkPolling.Contains(steamId))
                {
                    if (session.NextPollUtc + 30 < now)
                    {
                        linkPolling.Remove(steamId);
                    }
                    else
                    {
                        continue;
                    }
                }

                if (session.NextPollUtc > now)
                {
                    continue;
                }

                session.NextPollUtc = now + Math.Max(2, session.Interval);
                linkPolling.Add(steamId);

                string path = "/api/link/status?sessionId=" + Uri.EscapeDataString(session.SessionId);
                SendGet(path, (code, response) =>
                {
                    linkPolling.Remove(steamId);
                    HandleLinkStatus(steamId, code, response);
                });
            }
        }

        private void HandleLinkStatus(string steamId, int code, string response)
        {
            if (!data.LinkSessions.TryGetValue(steamId, out LinkSessionData session) || session == null)
            {
                return;
            }

            if (code != 200)
            {
                session.Error = "Relay unreachable: " + ExtractError(response);
                return;
            }

            LinkStatusResponse status = JsonConvert.DeserializeObject<LinkStatusResponse>(response);
            if (status == null)
            {
                return;
            }

            IPlayer player = players.FindPlayerById(steamId);

            if (status.Status == "linked")
            {
                if (string.IsNullOrEmpty(status.TwitchUserId))
                {
                    session.Status = "failed";
                    session.Error = "The relay returned a link without a Twitch user id.";
                    SaveData();
                    return;
                }

                PlayerProfile profile = GetProfile(steamId);
                profile.TwitchUserId = status.TwitchUserId;
                profile.TwitchLogin = status.Login;
                profile.TwitchDisplayName = string.IsNullOrEmpty(status.DisplayName) ? status.Login : status.DisplayName;
                profile.LinkedAtUtc = NowUnix();
                profile.OptedIn = true;

                data.LinkSessions.Remove(steamId);
                if (config.GrantStreamerOnLink)
                {
                    GrantStreamer(steamId);
                }
                AddAudit("linked " + profile.PlayerName + " (" + steamId + ") to Twitch " + status.Login);
                SaveData();

                if (player != null)
                {
                    Reply(player, "Twitch linked: " + profile.TwitchDisplayName + ". You are opted in; choose your loadouts with /twitch loadout.");
                    RefreshPanel(player);
                }
                return;
            }

            if (status.Status == "expired" || status.Status == "failed" || status.Status == "canceled" || status.Status == "unknown")
            {
                session.Status = status.Status;
                session.Error = status.Error;
                SaveData();

                if (player != null)
                {
                    Reply(player, "Twitch link " + status.Status + ": " + status.Error);
                }
                return;
            }

            session.Status = "pending";
        }

        private void Unlink(IPlayer player, IPlayer target)
        {
            if (player == null)
            {
                return;
            }

            IPlayer subject = target ?? player;
            PlayerProfile profile = GetProfile(subject.Id);
            if (profile == null || string.IsNullOrEmpty(profile.TwitchUserId))
            {
                Reply(player, subject.Name + " is not linked to Twitch.");
                return;
            }

            string body = JsonConvert.SerializeObject(new { steamId = profile.SteamId });
            SendPost("/api/unlink", body, (code, response) =>
            {
                if (code != 200)
                {
                    Reply(player, "Could not unlink: " + ExtractError(response));
                    return;
                }

                bool removed = false;
                try
                {
                    JObject json = JObject.Parse(response);
                    removed = json["removed"] != null && json["removed"].Type == JTokenType.Boolean && json["removed"].Value<bool>();
                }
                catch
                {
                    // Fall through.
                }

                if (!removed)
                {
                    Reply(player, "The relay did not find that link. Clearing it locally.");
                }

                AddAudit("unlinked " + profile.PlayerName + " (" + profile.SteamId + ") from Twitch " + profile.TwitchLogin);
                profile.TwitchUserId = string.Empty;
                profile.TwitchLogin = string.Empty;
                profile.TwitchDisplayName = string.Empty;
                profile.OptedIn = false;
                profile.LastEffectUtc = 0;

                IPlayer online = players.FindPlayerById(profile.SteamId);
                if (config.RevokeStreamerOnUnlink)
                {
                    RevokeStreamer(profile.SteamId);
                }

                SaveData();
                Reply(player, "Twitch link removed.");

                if (online != null)
                {
                    RefreshPanel(online);
                }
            });
        }

        private void ReconcileRelay(IPlayer player)
        {
            if (string.IsNullOrEmpty(config.RelayUrl) || string.IsNullOrEmpty(config.ServerKey))
            {
                Reply(player, "Relay is not configured.");
                return;
            }

            SendPost("/api/reconcile", "{}", (code, response) =>
            {
                Reply(player, code == 200 ? "Reconcile requested." : "Reconcile failed: " + ExtractError(response));
            });
        }

        #endregion

        #region Event processing

        private void PollRelayEvents()
        {
            if (data == null)
            {
                return;
            }

            if (pollingEvents)
            {
                // Recover if a webrequest callback never fired.
                if (lastPollStartedUtc + (long)config.RequestTimeoutSeconds + 15 > NowUnix())
                {
                    return;
                }

                pollingEvents = false;
            }

            if (string.IsNullOrEmpty(config.RelayUrl) || string.IsNullOrEmpty(config.ServerKey))
            {
                return;
            }

            pollingEvents = true;
            lastPollStartedUtc = NowUnix();
            SendGet("/api/events/pending?limit=100", (code, response) =>
            {
                pollingEvents = false;

                if (code != 200)
                {
                    relayAvailable = false;
                    lastRelayError = code + " " + ExtractError(response);
                    long now = NowUnix();
                    if (now - lastRelayWarningUtc > 60)
                    {
                        lastRelayWarningUtc = now;
                        PrintWarning("Relay poll failed: " + lastRelayError);
                    }
                    return;
                }

                relayAvailable = true;
                lastRelayError = string.Empty;

                PendingEventsResponse pending = null;
                try
                {
                    pending = JsonConvert.DeserializeObject<PendingEventsResponse>(response);
                }
                catch (Exception exception)
                {
                    PrintWarning("Could not parse relay events: " + exception.Message);
                    return;
                }

                if (pending == null || pending.Events == null || pending.Events.Count == 0)
                {
                    pendingEventCount = 0;
                    return;
                }

                pendingEventCount = pending.Events.Count;
                List<string> acknowledged = new List<string>();

                foreach (TwitchEvent evt in pending.Events)
                {
                    if (evt == null || string.IsNullOrEmpty(evt.Id))
                    {
                        continue;
                    }

                    try
                    {
                        HandleTwitchEvent(evt);
                    }
                    catch (Exception exception)
                    {
                        PrintWarning("Event " + evt.Id + " (" + evt.Type + ") failed: " + exception.Message);
                    }

                    acknowledged.Add(evt.Id);
                }

                if (acknowledged.Count > 0)
                {
                    AcknowledgeEvents(acknowledged);
                    SaveData();
                }
            });
        }

        private void AcknowledgeEvents(List<string> ids)
        {
            string body = JsonConvert.SerializeObject(new { ids = ids });
            SendPost("/api/events/ack", body, (code, response) =>
            {
                if (code != 200)
                {
                    PrintWarning("Could not acknowledge relay events: " + ExtractError(response));
                }
            });
        }

        private void HandleTwitchEvent(TwitchEvent evt)
        {
            if (evt == null || string.IsNullOrEmpty(evt.Id))
            {
                return;
            }

            if (!MarkProcessed(evt.Id))
            {
                return;
            }

            if (evt.Type == "channel_points_update")
            {
                return;
            }

            if ((evt.Type == "channel_points" || evt.Type == "automatic_reward") &&
                string.Equals(evt.Status, "canceled", StringComparison.OrdinalIgnoreCase))
            {
                AddAudit("ignored canceled redemption " + evt.Id);
                return;
            }

            if (evt.Type == "bits" && evt.IsAnonymous && !config.AllowAnonymousCheers)
            {
                AddAudit("ignored anonymous cheer " + evt.Id);
                return;
            }

            if (evt.ReceivedAt != default(DateTimeOffset) &&
                (DateTimeOffset.UtcNow - evt.ReceivedAt).TotalMinutes > config.MaxEventAgeMinutes)
            {
                AddAudit("ignored stale " + evt.Type + " event " + evt.Id);
                return;
            }

            PlayerProfile profile = FindProfileForEvent(evt);
            if (profile == null)
            {
                AddAudit("ignored " + evt.Type + " from " + evt.ViewerLogin + ": no linked streamer for broadcaster " + evt.BroadcasterUserId);
                return;
            }

            if (!profile.OptedIn || profile.Paused)
            {
                AddAudit("ignored " + evt.Type + " from " + evt.ViewerLogin + ": " + profile.TwitchLogin + " is " + (profile.Paused ? "paused" : "opted out"));
                return;
            }

            IPlayer player = players.FindPlayerById(profile.SteamId);
            bool online = player != null && player.IsConnected;

            TriggerDefinition trigger = FindTrigger(evt, profile);
            if (trigger == null)
            {
                AddAudit("ignored " + evt.Type + " from " + evt.ViewerLogin + ": no matching trigger");
                return;
            }

            TierDefinition tier = FindTier(trigger.TierId);
            if (tier == null || !tier.Enabled)
            {
                AddAudit("ignored " + evt.Type + ": tier " + trigger.TierId + " is missing or disabled");
                return;
            }

            if (!permission.UserHasPermission(profile.SteamId, tier.Permission))
            {
                AddAudit("ignored " + evt.Type + " from " + evt.ViewerLogin + ": " + profile.PlayerName + " lacks " + tier.Permission);
                if (player != null && evt.Type != "test")
                {
                    Reply(player, "A Twitch " + evt.Type + " event was ignored because you do not have " + tier.Permission + ".");
                }
                return;
            }

            TierLoadout loadout = GetLoadout(profile, tier.Id);
            ProfileCleanup(profile, tier);

            if (!IsTierArmed(profile, tier))
            {
                AddAudit("ignored " + evt.Type + " from " + evt.ViewerLogin + ": " + tier.Id + " is not armed");
                if (player != null && evt.Type != "test")
                {
                    Reply(player, "A Twitch " + evt.Type + " event was ignored because your " + tier.DisplayName + " loadout is not armed.");
                }
                return;
            }

            long now = NowUnix();
            if (!PassesCooldowns(profile, trigger, evt, now))
            {
                return;
            }

            EffectDefinition buff = PickEffect(tier.Buffs, loadout.Buffs);
            EffectDefinition debuff = PickEffect(tier.Debuffs, loadout.Debuffs);
            EffectDefinition reward = PickEffect(tier.Rewards, loadout.Rewards);

            if (config.RequireAllCategories &&
                ((tier.Buffs.Count > 0 && buff == null) ||
                 (tier.Debuffs.Count > 0 && debuff == null) ||
                 (tier.Rewards.Count > 0 && reward == null)))
            {
                AddAudit("ignored " + evt.Type + ": loadout for " + tier.Id + " was not fully pickable");
                return;
            }

            bool anyEnabled =
                (config.AllowBuffs && profile.AllowBuffs && buff != null) ||
                (config.AllowDebuffs && profile.AllowDebuffs && debuff != null) ||
                (config.AllowRewards && profile.AllowRewards && reward != null);

            if (!anyEnabled)
            {
                AddAudit("ignored " + evt.Type + ": all effect categories are disabled for " + profile.PlayerName);
                return;
            }

            if (!online)
            {
                if (config.QueueWhileOffline)
                {
                    QueuePendingEffect(profile, tier, "buff", buff, evt);
                    QueuePendingEffect(profile, tier, "debuff", debuff, evt);
                    QueuePendingEffect(profile, tier, "reward", reward, evt);
                    SaveData();
                    AddAudit("queued " + evt.Type + " from " + evt.ViewerLogin + " for offline " + profile.PlayerName);
                }
                else
                {
                    AddAudit("ignored " + evt.Type + " from " + evt.ViewerLogin + ": " + profile.PlayerName + " is offline");
                }
                return;
            }

            profile.LastEffectUtc = now;
            profile.ViewerCooldowns[ViewerKey(evt)] = now;
            profile.TriggerCooldowns[trigger.Id] = now;
            recentEffects.Add(now);

            List<string> applied = new List<string>();

            if (config.AllowBuffs && profile.AllowBuffs)
            {
                ApplyEffect(player, profile, tier, "buff", buff, evt);
                if (buff != null)
                {
                    applied.Add("buff " + buff.Label);
                }
            }

            if (config.AllowDebuffs && profile.AllowDebuffs)
            {
                ApplyEffect(player, profile, tier, "debuff", debuff, evt);
                if (debuff != null)
                {
                    applied.Add("debuff " + debuff.Label);
                }
            }

            if (config.AllowRewards && profile.AllowRewards)
            {
                ApplyEffect(player, profile, tier, "reward", reward, evt);
                if (reward != null)
                {
                    applied.Add("reward " + reward.Label);
                }
            }

            if (config.AnnounceEffects && applied.Count > 0)
            {
                string viewer = string.IsNullOrEmpty(evt.ViewerLogin) ? "Twitch" : evt.ViewerLogin;
                player.Reply(config.ChatPrefix + " " + viewer + " triggered " + tier.DisplayName + ": " + string.Join(", ", applied.ToArray()));
            }

            AddAudit(
                evt.Type + " " + evt.Amount + " from " + evt.ViewerLogin + " -> " + profile.PlayerName + " / " + tier.Id +
                " | buff=" + (buff == null ? "-" : buff.Id) +
                " debuff=" + (debuff == null ? "-" : debuff.Id) +
                " reward=" + (reward == null ? "-" : reward.Id));

            SaveData();
        }

        private PlayerProfile FindProfileForEvent(TwitchEvent evt)
        {
            if (evt == null)
            {
                return null;
            }

            string steamId;
            if (evt.Extra != null &&
                evt.Extra.TryGetValue("steamId", out steamId) &&
                !string.IsNullOrEmpty(steamId))
            {
                return GetProfile(steamId);
            }

            if (string.IsNullOrEmpty(evt.BroadcasterUserId))
            {
                return null;
            }

            return data.Players.Values.FirstOrDefault(profile =>
                string.Equals(profile.TwitchUserId, evt.BroadcasterUserId, StringComparison.Ordinal));
        }

        private TriggerDefinition FindTrigger(TwitchEvent evt, PlayerProfile profile)
        {
            TriggerDefinition best = null;
            long bestAmount = -1;

            foreach (TriggerDefinition trigger in config.Triggers)
            {
                if (trigger == null || !trigger.Enabled || !TriggerMatches(trigger, evt))
                {
                    continue;
                }

                if (evt.Amount < trigger.MinAmount)
                {
                    continue;
                }

                long last;
                if (trigger.CooldownSeconds > 0 &&
                    profile.TriggerCooldowns.TryGetValue(trigger.Id, out last) &&
                    last + trigger.CooldownSeconds > NowUnix())
                {
                    continue;
                }

                if (best == null ||
                    trigger.Priority > best.Priority ||
                    (trigger.Priority == best.Priority && trigger.MinAmount > bestAmount))
                {
                    best = trigger;
                    bestAmount = trigger.MinAmount;
                }
            }

            if (best == null &&
                config.FallbackToDefaultTier &&
                config.FallbackEventTypes.Contains(evt.Type, StringComparer.OrdinalIgnoreCase))
            {
                best = new TriggerDefinition
                {
                    Id = "fallback_" + evt.Type,
                    Event = evt.Type,
                    TierId = config.DefaultTierId,
                    Enabled = true
                };
            }

            return best;
        }

        private bool TriggerMatches(TriggerDefinition trigger, TwitchEvent evt)
        {
            if (trigger == null || evt == null)
            {
                return false;
            }

            string eventType = (trigger.Event ?? string.Empty).ToLowerInvariant();
            string actual = (evt.Type ?? string.Empty).ToLowerInvariant();

            if (eventType == "any")
            {
                return true;
            }

            if (eventType == "channel_points" || eventType == "automatic_reward")
            {
                if (actual != eventType)
                {
                    return false;
                }

                if (trigger.MatchAnyReward)
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(trigger.RewardId) &&
                    string.Equals(trigger.RewardId, evt.RewardId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(trigger.RewardTitle) &&
                    string.Equals(trigger.RewardTitle, evt.RewardTitle, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return false;
            }

            if (eventType != actual)
            {
                return false;
            }

            if (eventType == "sub" || eventType == "resub")
            {
                if (trigger.RequireGift && !evt.IsGift)
                {
                    return false;
                }

                if (trigger.RequireNotGift && evt.IsGift)
                {
                    return false;
                }

                if (trigger.SubTier > 0 && ParseSubTier(evt.SubTier) != trigger.SubTier)
                {
                    return false;
                }
            }

            if (eventType == "hype_train" &&
                !string.IsNullOrEmpty(trigger.Phase) &&
                evt.Extra != null &&
                evt.Extra.TryGetValue("phase", out string phase) &&
                !string.Equals(phase, trigger.Phase, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (eventType == "chat")
            {
                // Chat has no amount and would otherwise fire on every message.
                // Staff must provide a keyword, and the message must contain it.
                if (string.IsNullOrEmpty(trigger.Keyword))
                {
                    return false;
                }

                string message;
                if (evt.Extra == null || !evt.Extra.TryGetValue("message", out message))
                {
                    return false;
                }

                return message.IndexOf(trigger.Keyword, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return true;
        }

        private bool PassesCooldowns(PlayerProfile profile, TriggerDefinition trigger, TwitchEvent evt, long now)
        {
            if (config.GlobalCooldownSeconds > 0 &&
                profile.LastEffectUtc + config.GlobalCooldownSeconds > now)
            {
                return false;
            }

            if (config.PerViewerCooldownSeconds > 0)
            {
                string viewer = ViewerKey(evt);
                long last;
                if (profile.ViewerCooldowns.TryGetValue(viewer, out last) &&
                    last + config.PerViewerCooldownSeconds > now)
                {
                    return false;
                }
            }

            recentEffects.RemoveAll(value => value < now - 60);
            if (recentEffects.Count >= config.MaxEffectsPerMinute)
            {
                AddAudit("rate limited " + evt.Type + " from " + evt.ViewerLogin);
                return false;
            }

            if (profile.ViewerCooldowns.Count > 500)
            {
                foreach (string key in profile.ViewerCooldowns.Where(pair => pair.Value < now - 86400).Select(pair => pair.Key).ToList())
                {
                    profile.ViewerCooldowns.Remove(key);
                }
            }

            return true;
        }

        private static string ViewerKey(TwitchEvent evt)
        {
            if (!string.IsNullOrEmpty(evt.ViewerUserId))
            {
                return evt.ViewerUserId;
            }

            if (!string.IsNullOrEmpty(evt.ViewerLogin))
            {
                return evt.ViewerLogin;
            }

            return "anonymous";
        }

        private static int ParseSubTier(string tier)
        {
            if (string.IsNullOrEmpty(tier))
            {
                return 0;
            }

            if (tier.Equals("Prime", StringComparison.OrdinalIgnoreCase))
            {
                return 1000;
            }

            int value;
            return int.TryParse(tier, out value) ? value : 0;
        }

        private EffectDefinition PickEffect(List<EffectDefinition> pool, List<string> selected)
        {
            if (pool == null || selected == null || selected.Count == 0)
            {
                return null;
            }

            List<EffectDefinition> candidates = pool
                .Where(effect => selected.Contains(effect.Id))
                .ToList();

            if (candidates.Count == 0)
            {
                return null;
            }

            int totalWeight = candidates.Sum(effect => Math.Max(1, effect.Weight));
            int roll = random.Next(1, totalWeight + 1);
            int running = 0;

            foreach (EffectDefinition candidate in candidates)
            {
                running += Math.Max(1, candidate.Weight);
                if (roll <= running)
                {
                    return candidate;
                }
            }

            return candidates[candidates.Count - 1];
        }

        private bool MarkProcessed(string eventId)
        {
            if (string.IsNullOrEmpty(eventId) || !processedEventIds.Add(eventId))
            {
                return false;
            }

            processedOrder.Add(eventId);
            while (processedOrder.Count > ProcessedHistoryLimit)
            {
                string oldest = processedOrder[0];
                processedOrder.RemoveAt(0);
                processedEventIds.Remove(oldest);
            }

            return true;
        }

        #endregion

        #region Effects

        private void QueuePendingEffect(PlayerProfile profile, TierDefinition tier, string category, EffectDefinition effect, TwitchEvent evt)
        {
            if (profile == null || tier == null || effect == null)
            {
                return;
            }

            profile.PendingEffects.Add(new PendingEffect
            {
                EventId = evt.Id,
                TierId = tier.Id,
                Category = category,
                EffectId = effect.Id,
                EventType = evt.Type,
                ViewerLogin = evt.ViewerLogin,
                ViewerName = evt.ViewerName,
                Amount = evt.Amount,
                ExpiresAtUtc = NowUnix() + config.PendingEffectTtlMinutes * 60
            });
        }

        private void ApplyPendingEffects(IPlayer player)
        {
            if (player == null || data == null)
            {
                return;
            }

            PlayerProfile profile = GetProfile(player.Id);
            if (profile == null || profile.PendingEffects == null || profile.PendingEffects.Count == 0)
            {
                return;
            }

            if (!profile.OptedIn || profile.Paused)
            {
                profile.PendingEffects.Clear();
                SaveData();
                return;
            }

            long now = NowUnix();
            List<PendingEffect> apply = profile.PendingEffects
                .Where(effect => effect != null && effect.ExpiresAtUtc >= now)
                .ToList();

            profile.PendingEffects.RemoveAll(effect => effect == null || effect.ExpiresAtUtc < now);

            foreach (PendingEffect pending in apply)
            {
                TierDefinition tier = FindTier(pending.TierId);
                List<EffectDefinition> pool = GetPool(tier, pending.Category);
                EffectDefinition effect = pool == null ? null : pool.FirstOrDefault(item => item.Id == pending.EffectId);
                if (tier == null || effect == null)
                {
                    continue;
                }

                if (!permission.UserHasPermission(profile.SteamId, tier.Permission))
                {
                    continue;
                }

                if (pending.Category == "buff" && (!config.AllowBuffs || !profile.AllowBuffs))
                {
                    continue;
                }

                if (pending.Category == "debuff" && (!config.AllowDebuffs || !profile.AllowDebuffs))
                {
                    continue;
                }

                if (pending.Category == "reward" && (!config.AllowRewards || !profile.AllowRewards))
                {
                    continue;
                }

                TwitchEvent evt = new TwitchEvent
                {
                    Id = pending.EventId,
                    Type = pending.EventType,
                    ViewerLogin = pending.ViewerLogin,
                    ViewerName = pending.ViewerName,
                    Amount = pending.Amount,
                    ReceivedAt = DateTimeOffset.UtcNow
                };

                ApplyEffect(player, profile, tier, pending.Category, effect, evt);

                if (config.AnnounceEffects)
                {
                    string viewer = string.IsNullOrEmpty(pending.ViewerLogin) ? "Twitch" : pending.ViewerLogin;
                    player.Reply(config.ChatPrefix + " " + viewer + " triggered a queued " + tier.DisplayName + " " +
                                 CategoryLabel(pending.Category) + ": " + effect.Label);
                }
            }

            profile.PendingEffects.Clear();
            SaveData();
        }

        private void ApplyEffect(IPlayer player, PlayerProfile profile, TierDefinition tier, string category, EffectDefinition effect, TwitchEvent evt)
        {
            if (player == null || profile == null || tier == null || effect == null)
            {
                return;
            }

            if (effect.Actions == null || effect.Actions.Count == 0)
            {
                return;
            }

            foreach (EffectAction action in effect.Actions)
            {
                if (action == null)
                {
                    continue;
                }

                EffectAction captured = action;
                if (captured.DelaySeconds > 0f)
                {
                    timer.Once(captured.DelaySeconds, () =>
                    {
                        IPlayer online = players.FindPlayerById(profile.SteamId);
                        if (online != null && online.IsConnected)
                        {
                            ExecuteAction(online, profile, tier, category, effect, captured, evt);
                        }
                    });
                }
                else
                {
                    ExecuteAction(player, profile, tier, category, effect, captured, evt);
                }
            }

            plugins.CallHook(
                "OnTwitchEffect",
                player.Id,
                effect.Id,
                category,
                tier.Id,
                evt == null ? string.Empty : evt.Type,
                evt == null ? string.Empty : evt.ViewerLogin,
                evt == null ? 0L : evt.Amount);
        }

        private void ExecuteAction(IPlayer player, PlayerProfile profile, TierDefinition tier, string category, EffectDefinition effect, EffectAction action, TwitchEvent evt)
        {
            switch (action.Type)
            {
                case "buff":
                {
                    string buff = SanitizeToken(action.Value, true);
                    if (!string.IsNullOrEmpty(buff))
                    {
                        ExecuteConsole("buffplayer " + ConsoleTarget(player) + " " + Quote(buff));
                    }
                    break;
                }

                case "debuff":
                {
                    string buff = SanitizeToken(action.Value, true);
                    if (!string.IsNullOrEmpty(buff))
                    {
                        ExecuteConsole("debuffplayer " + ConsoleTarget(player) + " " + Quote(buff));
                    }
                    break;
                }

                case "item":
                {
                    string item = SanitizeToken(action.Value, false);
                    if (!string.IsNullOrEmpty(item))
                    {
                        string command = "give " + ConsoleTarget(player) + " " + Quote(item) + " " + Math.Max(1, action.Count);
                        if (action.Quality > 0)
                        {
                            command += " " + action.Quality;
                        }
                        ExecuteConsole(command);
                    }
                    break;
                }

                case "command":
                {
                    string command = ExpandCommand(action.Value, player, profile, tier, category, effect, evt);
                    if (!string.IsNullOrEmpty(command) && IsAllowedCommand(command))
                    {
                        ExecuteConsole(command);
                    }
                    break;
                }

                case "hook":
                {
                    plugins.CallHook(
                        "OnTwitchEffectAction",
                        player.Id,
                        effect.Id,
                        action.Value,
                        category,
                        tier.Id,
                        evt == null ? string.Empty : evt.ViewerLogin,
                        evt == null ? 0L : evt.Amount);
                    break;
                }
            }
        }

        private string ExpandCommand(string template, IPlayer player, PlayerProfile profile, TierDefinition tier, string category, EffectDefinition effect, TwitchEvent evt)
        {
            if (string.IsNullOrEmpty(template) ||
                template.IndexOf('\n') >= 0 ||
                template.IndexOf('\r') >= 0 ||
                template.IndexOf(';') >= 0)
            {
                return null;
            }

            string command = template
                .Replace("{player}", player == null ? string.Empty : SanitizePlayerName(player.Name))
                .Replace("{steamid}", player == null ? string.Empty : player.Id)
                .Replace("{twitch}", profile == null ? string.Empty : SanitizeToken(profile.TwitchLogin, true))
                .Replace("{viewer}", evt == null ? string.Empty : SanitizeToken(evt.ViewerLogin, true))
                .Replace("{amount}", evt == null ? "0" : evt.Amount.ToString(CultureInfo.InvariantCulture))
                .Replace("{tier}", tier == null ? string.Empty : tier.Id)
                .Replace("{effect}", effect == null ? string.Empty : effect.Id)
                .Replace("{reward}", evt == null ? string.Empty : SanitizeToken(evt.RewardTitle, true))
                .Replace("{category}", CategoryLabel(category));

            return command.Trim();
        }

        private static string SanitizePlayerName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ')
                {
                    builder.Append(c);
                }
            }

            return builder.ToString().Trim();
        }

        private bool IsAllowedCommand(string command)
        {
            if (string.IsNullOrEmpty(command))
            {
                return false;
            }

            command = command.Trim();
            if (command.StartsWith("/", StringComparison.Ordinal))
            {
                command = command.Substring(1);
            }

            int space = command.IndexOf(' ');
            string prefix = (space < 0 ? command : command.Substring(0, space)).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(prefix))
            {
                return false;
            }

            return config.AllowedCommandPrefixes.Contains(prefix, StringComparer.OrdinalIgnoreCase);
        }

        private void ExecuteConsole(string command)
        {
            if (string.IsNullOrEmpty(command))
            {
                return;
            }

            try
            {
                SdtdConsole console = SdtdConsole.Instance;
                if (console == null)
                {
                    PrintWarning("Console unavailable for: " + command);
                    return;
                }

                console.ExecuteSync(command, null);
                AddAudit("console: " + command);
            }
            catch (Exception exception)
            {
                PrintWarning("Console command failed: " + command + " - " + exception.Message);
            }
        }

        private static string ConsoleTarget(IPlayer player)
        {
            if (player == null)
            {
                return "server_console";
            }

            EntityPlayer entity = player.Object as EntityPlayer;
            if (entity != null)
            {
                return entity.entityId.ToString(CultureInfo.InvariantCulture);
            }

            return Quote(player.Name);
        }

        private static string Quote(string value)
        {
            if (value == null)
            {
                return "\"\"";
            }

            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string SanitizeToken(string value, bool buffName)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || (!buffName && (c == '-' || c == '.')))
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        #endregion

        #region Relay client

        private void SendGet(string path, Action<int, string> callback)
        {
            if (string.IsNullOrEmpty(config.RelayUrl) || string.IsNullOrEmpty(config.ServerKey))
            {
                return;
            }

            string url = config.RelayUrl + path;
            webrequest.Enqueue(
                url,
                null,
                (code, body) => SafeRelayCallback(callback, code, body),
                this,
                RequestMethod.GET,
                BuildAuthHeaders("GET", string.Empty),
                config.RequestTimeoutSeconds);
        }

        private void SendPost(string path, string body, Action<int, string> callback)
        {
            if (string.IsNullOrEmpty(config.RelayUrl) || string.IsNullOrEmpty(config.ServerKey))
            {
                return;
            }

            string url = config.RelayUrl + path;
            string payload = body ?? string.Empty;
            Dictionary<string, string> headers = BuildAuthHeaders("POST", payload);
            headers["Content-Type"] = "application/json";

            webrequest.Enqueue(
                url,
                payload,
                (code, response) => SafeRelayCallback(callback, code, response),
                this,
                RequestMethod.POST,
                headers,
                config.RequestTimeoutSeconds);
        }

        private Dictionary<string, string> BuildAuthHeaders(string method, string body)
        {
            long timestamp = NowUnix();
            string nonce = Guid.NewGuid().ToString("N");
            string canonical = timestamp.ToString(CultureInfo.InvariantCulture) + "\n" + nonce + "\n" + (body ?? string.Empty);
            string signature = HmacSha256(config.ServerKey, canonical);

            return new Dictionary<string, string>
            {
                ["X-Server-Id"] = config.ServerId,
                ["X-Timestamp"] = timestamp.ToString(CultureInfo.InvariantCulture),
                ["X-Nonce"] = nonce,
                ["X-Signature"] = signature,
                ["Accept"] = "application/json"
            };
        }

        private static string HmacSha256(string secret, string value)
        {
            using (HMACSHA256 hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret ?? string.Empty)))
            {
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                return Convert.ToBase64String(hash);
            }
        }

        private void SafeRelayCallback(Action<int, string> callback, int code, string body)
        {
            try
            {
                callback(code, body ?? string.Empty);
            }
            catch (Exception exception)
            {
                PrintWarning("Relay callback failed: " + exception.Message);
            }
        }

        private static string ExtractError(string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return "no response";
            }

            try
            {
                JObject json = JObject.Parse(body);
                JToken error = json["error"];
                if (error != null)
                {
                    return error.ToString();
                }
            }
            catch
            {
                // Not JSON.
            }

            return body.Length > 200 ? body.Substring(0, 200) : body;
        }

        private void SimulateEvent(IPlayer player, string type, long amount)
        {
            if (player == null)
            {
                return;
            }

            PlayerProfile profile = GetProfile(player.Id);
            if (profile == null || string.IsNullOrEmpty(profile.TwitchUserId))
            {
                Reply(player, "Link Twitch first, or use /twitch forcelink <player> <twitchLogin> for a local test.");
                return;
            }

            string broadcaster = profile.TwitchUserId;

            TwitchEvent evt = new TwitchEvent
            {
                Id = "local-" + Guid.NewGuid().ToString("N"),
                Type = string.IsNullOrEmpty(type) ? "bits" : type.ToLowerInvariant(),
                RawType = "local.test",
                BroadcasterUserId = broadcaster,
                BroadcasterLogin = profile == null ? "test" : profile.TwitchLogin,
                ViewerUserId = "test-viewer",
                ViewerLogin = "test_viewer",
                ViewerName = "Test Viewer",
                Amount = amount,
                RewardId = "test-reward",
                RewardTitle = "Test Reward",
                SubTier = "1000",
                Status = "unfulfilled",
                OccurredAt = DateTimeOffset.UtcNow,
                ReceivedAt = DateTimeOffset.UtcNow,
                Extra = new Dictionary<string, string> { ["steamId"] = player.Id }
            };

            HandleTwitchEvent(evt);
            Reply(player, "Test event fired: " + evt.Type + " " + amount + ".");
        }

        #endregion

        #region HTTP DTOs

        private sealed class LinkStartResponse
        {
            public string SessionId { get; set; }
            public string UserCode { get; set; }
            public string VerificationUri { get; set; }
            public int ExpiresIn { get; set; }
            public int Interval { get; set; }
            public string Status { get; set; }
        }

        private sealed class LinkStatusResponse
        {
            public string Status { get; set; }
            public string TwitchUserId { get; set; }
            public string Login { get; set; }
            public string DisplayName { get; set; }
            public string Error { get; set; }
        }

        private sealed class PendingEventsResponse
        {
            public List<TwitchEvent> Events { get; set; }
        }

        private sealed class RelayStatusResponse
        {
            public string Server { get; set; }
            public bool Connected { get; set; }
            public string Session { get; set; }
            public int Subscriptions { get; set; }
            public int PendingEvents { get; set; }
            public int Links { get; set; }
        }

        private sealed class TwitchEvent
        {
            public string Id { get; set; }
            public string Type { get; set; }
            public string RawType { get; set; }
            public string BroadcasterUserId { get; set; }
            public string BroadcasterLogin { get; set; }
            public string ViewerUserId { get; set; }
            public string ViewerLogin { get; set; }
            public string ViewerName { get; set; }
            public long Amount { get; set; }
            public string RewardId { get; set; }
            public string RewardTitle { get; set; }
            public string RedemptionId { get; set; }
            public string Status { get; set; }
            public string SubTier { get; set; }
            public bool IsGift { get; set; }
            public bool IsAnonymous { get; set; }
            public DateTimeOffset OccurredAt { get; set; }
            public DateTimeOffset ReceivedAt { get; set; }
            public Dictionary<string, string> Extra { get; set; }
        }

        #endregion

        #region Utilities

        private static string Encode(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
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
                        builder.Append("%0D");
                        break;
                    default:
                        builder.Append(c);
                        break;
                }
            }

            return builder.ToString();
        }

        private static string Clean(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            value = value.Replace('\n', ' ').Replace('\r', ' ').Replace('|', '/').Trim();
            return value.Length > maxLength ? value.Substring(0, maxLength) : value;
        }

        private static string SanitizeId(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);
            foreach (char c in value.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                {
                    builder.Append(c);
                }
                else if (c == ' ' || c == '.')
                {
                    builder.Append('_');
                }
            }

            return builder.ToString().Trim('_', '-');
        }

        private static string SanitizeCommandPrefix(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            int space = value.IndexOf(' ');
            string prefix = (space < 0 ? value : value.Substring(0, space)).Trim().ToLowerInvariant();
            StringBuilder builder = new StringBuilder(prefix.Length);
            foreach (char c in prefix)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.')
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        private static float Clamp(float value, float min, float max)
        {
            return value < min ? min : value > max ? max : value;
        }

        private static long NowUnix()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private void Reply(IPlayer player, string message)
        {
            if (player == null || string.IsNullOrEmpty(message))
            {
                return;
            }

            player.Reply(string.IsNullOrEmpty(config.ChatPrefix) ? message : config.ChatPrefix + " " + message);
        }

        private IPlayer FindPlayer(string nameOrId)
        {
            if (string.IsNullOrEmpty(nameOrId))
            {
                return null;
            }

            IPlayer player = players.FindPlayerById(nameOrId);
            if (player != null)
            {
                return player;
            }

            return players.FindPlayer(nameOrId);
        }

        #endregion
    }
}
