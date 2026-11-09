using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("AdminKits", "TheRealM18", "1.1.0")]
    [Description("Create, manage and collect item kits through the ServerAssist GUI.")]
    public class AdminKits : CovalencePlugin
    {
        #region Constants

        private const string PluginTitle = "Admin Kits";
        private const string HookId = "adminkits";
        private const string ServerAssistPluginName = "ServerAssist";
        private const string DataFileName = "AdminKits/kits";

        private const string PermUse = "adminkits.use";
        private const string PermAdmin = "adminkits.admin";
        private const string PermGive = "adminkits.give";
        private const string PermKitPrefix = "adminkits.kit.";

        private const int UiRows = 10;
        private const int UiButtons = 10;
        private const int MaxItemCount = 100000;

        /// <summary>
        /// Sprite keys from the AAZ_ServerAssistArt pack. The art is already
        /// colored, so tints are only used for status states.
        /// </summary>
        private static class KitIcons
        {
            public const string Panel = "sprite:sa_admin_kits";
            public const string Kit = "sprite:sa_admin_kits";
            public const string Collect = "sprite:sa_nav_items";
            public const string Items = "sprite:sa_nav_items";
            public const string Manage = "sprite:sa_nav_logs";
            public const string Create = "sprite:sa_admin_kits";
            public const string FromInventory = "sprite:sa_dynamic_backpack";
            public const string Edit = "sprite:sa_act_repair";
            public const string Delete = "sprite:sa_act_ban";
            public const string Warning = "sprite:sa_act_warn";
            public const string Permission = "sprite:sa_permission_gui";
            public const string Time = "sprite:sa_act_time";
            public const string Claims = "sprite:sa_daily_rewards";
            public const string Players = "sprite:sa_nav_players";
            public const string Give = "sprite:sa_act_give";
            public const string Show = "sprite:sa_act_spectate";
            public const string Enabled = "sprite:sa_act_godmode";
            public const string Disabled = "sprite:sa_act_freeze";
            public const string Help = "sprite:sa_nav_help";
            public const string Refresh = "sprite:sa_plugin_hot_reload";
            public const string Log = "sprite:sa_nav_logs";

            public const string TintLocked = "#FFD166";
            public const string TintWait = "#66CCFF";
            public const string TintLimit = "#FF6B6B";
            public const string TintOff = "#9AA0A6";
            public const string TintHidden = "#C79BFF";
        }

        #endregion

        #region Fields

        private Configuration config;
        private StoredData data;
        private readonly Dictionary<string, Session> sessions =
            new Dictionary<string, Session>(StringComparer.OrdinalIgnoreCase);
        private bool hookRegistered;

        #endregion

        #region Lifecycle

        private void Init()
        {
            permission.RegisterPermission(PermUse, this);
            permission.RegisterPermission(PermAdmin, this);
            permission.RegisterPermission(PermGive, this);

            LoadConfig();
            LoadData();
            RegisterKitPermissions();

            AddCovalenceCommand(new[] { "kits", "kit" }, nameof(CommandKits));
            AddCovalenceCommand("kitadmin", nameof(CommandKitAdmin));

            timer.Once(1f, EnsureDefaultPermissions);
            timer.Once(2f, RegisterServerAssistHook);
            timer.Once(5f, RegisterServerAssistHook);
        }

        private void OnServerInitialized()
        {
            EnsureDefaultPermissions();
            LoadConfigIfNeeded();
            LoadDataIfNeeded();
            RegisterKitPermissions();
            RegisterServerAssistHook();
        }

        private void OnServerAssistReady()
        {
            RegisterServerAssistHook();
        }

        private void Unload()
        {
            UnregisterServerAssistHook();
            sessions.Clear();
        }

        private void OnUserDisconnected(IPlayer player)
        {
            if (player != null)
            {
                sessions.Remove(player.Id);
            }
        }

        private void LoadConfigIfNeeded()
        {
            if (config == null)
            {
                LoadConfig();
            }
        }

        private void LoadDataIfNeeded()
        {
            if (data == null)
            {
                LoadData();
            }
        }

        #endregion

        #region Configuration

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
                Interface.Oxide.LogWarning("[AdminKits] config could not be read: " + exception.Message);
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
            if (config != null && Config != null)
            {
                Config.WriteObject(config, true);
            }
        }

        private void NormalizeConfig()
        {
            if (config == null)
            {
                config = new Configuration();
            }

            config.ChatPrefix = Clean(config.ChatPrefix, 24);
            if (config.ChatPrefix.Length == 0)
            {
                config.ChatPrefix = "[Kits]";
            }

            config.HookLabel = Clean(config.HookLabel, 48);
            if (config.HookLabel.Length == 0)
            {
                config.HookLabel = PluginTitle;
            }

            config.HookDescription = Clean(config.HookDescription, 160);
            if (config.HookDescription.Length == 0)
            {
                config.HookDescription = "Create, manage and collect server kits.";
            }

            config.HookPermission = Clean(config.HookPermission, 96);
            if (config.HookPermission.Length == 0)
            {
                config.HookPermission = PermUse;
            }

            config.DefaultCooldownMinutes = Clamp(config.DefaultCooldownMinutes, 0, 525600);
            config.DefaultMaxUses = Clamp(config.DefaultMaxUses, 0, 1000000);
            config.DefaultMaxPerPlayer = Clamp(config.DefaultMaxPerPlayer, 0, 1000000);
            config.DefaultItemQuality = Clamp(config.DefaultItemQuality, 1, 6);
            config.MaxKits = Clamp(config.MaxKits, 1, 5000);
            config.MaxItemsPerKit = Clamp(config.MaxItemsPerKit, 1, 500);
        }

        private void EnsureDefaultPermissions()
        {
            try
            {
                string adminGroup = Interface.Oxide.Config.Options.DefaultGroups.Administrators;
                if (!string.IsNullOrEmpty(adminGroup))
                {
                    GrantGroupIfMissing(adminGroup, PermAdmin);
                    GrantGroupIfMissing(adminGroup, PermUse);
                    GrantGroupIfMissing(adminGroup, PermGive);
                }

                if (config != null && config.GrantUseToDefaultGroup)
                {
                    string playerGroup = Interface.Oxide.Config.Options.DefaultGroups.Players;
                    if (!string.IsNullOrEmpty(playerGroup))
                    {
                        GrantGroupIfMissing(playerGroup, PermUse);
                    }
                }
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[AdminKits] default permissions could not be granted: " + exception.Message);
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

        #region Data

        private void LoadData()
        {
            try
            {
                data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(DataFileName);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[AdminKits] data could not be read: " + exception.Message);
                data = null;
            }

            NormalizeData();
        }

        private void SaveData()
        {
            if (data == null)
            {
                return;
            }

            try
            {
                Interface.Oxide.DataFileSystem.WriteObject(DataFileName, data, true);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogError("[AdminKits] data could not be saved: " + exception);
            }
        }

        private void NormalizeData()
        {
            if (data == null)
            {
                data = new StoredData();
            }

            if (data.Kits == null)
            {
                data.Kits = new Dictionary<string, KitDefinition>(StringComparer.OrdinalIgnoreCase);
            }

            if (data.Claims == null)
            {
                data.Claims = new Dictionary<string, Dictionary<string, ClaimRecord>>(StringComparer.OrdinalIgnoreCase);
            }

            Dictionary<string, KitDefinition> rebuilt = new Dictionary<string, KitDefinition>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, KitDefinition> pair in data.Kits)
            {
                if (pair.Value == null)
                {
                    continue;
                }

                KitDefinition kit = pair.Value;
                string sourceId = !string.IsNullOrEmpty(kit.Id) ? kit.Id : pair.Key;
                kit.Id = MakeKitId(sourceId, usedIds);

                kit.Name = Clean(kit.Name, 48);
                if (kit.Name.Length == 0)
                {
                    kit.Name = kit.Id;
                }

                kit.Description = Clean(kit.Description, 160);
                kit.Permission = Clean(kit.Permission, 96);
                if (kit.Permission.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    kit.Permission = PermKitPrefix + kit.Id;
                }

                kit.CreatedBy = Clean(kit.CreatedBy, 96);
                kit.CooldownMinutes = Clamp(kit.CooldownMinutes, 0, 525600);
                kit.MaxUses = Clamp(kit.MaxUses, 0, 1000000);
                kit.MaxPerPlayer = Clamp(kit.MaxPerPlayer, 0, 1000000);
                kit.UseCount = Clamp(kit.UseCount, 0, 100000000);

                if (kit.Items == null)
                {
                    kit.Items = new List<KitItem>();
                }

                List<KitItem> items = new List<KitItem>();
                for (int i = 0; i < kit.Items.Count; i++)
                {
                    KitItem item = kit.Items[i];
                    if (item == null)
                    {
                        continue;
                    }

                    item.Item = Clean(item.Item, 96);
                    item.Count = Clamp(item.Count, 1, MaxItemCount);
                    item.Data = item.Data == null ? string.Empty : item.Data.Trim();
                    if (item.Item.Length == 0 && item.Data.Length == 0)
                    {
                        continue;
                    }

                    items.Add(item);
                }

                kit.Items = items;
                rebuilt[kit.Id] = kit;
            }

            data.Kits = rebuilt;

            List<string> emptyPlayers = new List<string>();
            foreach (KeyValuePair<string, Dictionary<string, ClaimRecord>> player in data.Claims)
            {
                if (string.IsNullOrEmpty(player.Key) || player.Value == null || player.Value.Count == 0)
                {
                    emptyPlayers.Add(player.Key);
                    continue;
                }

                List<string> badRecords = new List<string>();
                foreach (KeyValuePair<string, ClaimRecord> record in player.Value)
                {
                    if (string.IsNullOrEmpty(record.Key) || record.Value == null || !data.Kits.ContainsKey(record.Key))
                    {
                        badRecords.Add(record.Key);
                        continue;
                    }

                    record.Value.Count = Clamp(record.Value.Count, 0, 100000000);
                    if (record.Value.LastUtcTicks < 0)
                    {
                        record.Value.LastUtcTicks = 0;
                    }
                }

                for (int i = 0; i < badRecords.Count; i++)
                {
                    if (badRecords[i] != null)
                    {
                        player.Value.Remove(badRecords[i]);
                    }
                }

                if (player.Value.Count == 0)
                {
                    emptyPlayers.Add(player.Key);
                }
            }

            for (int i = 0; i < emptyPlayers.Count; i++)
            {
                if (emptyPlayers[i] != null)
                {
                    data.Claims.Remove(emptyPlayers[i]);
                }
            }
        }

        private void RegisterKitPermissions()
        {
            if (data == null || data.Kits == null)
            {
                return;
            }

            foreach (KitDefinition kit in data.Kits.Values)
            {
                if (kit == null || string.IsNullOrEmpty(kit.Permission))
                {
                    continue;
                }

                if (!kit.Permission.StartsWith(PermKitPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    if (!permission.PermissionExists(kit.Permission))
                    {
                        permission.RegisterPermission(kit.Permission, this);
                    }
                }
                catch (Exception exception)
                {
                    Interface.Oxide.LogWarning(
                        "[AdminKits] permission '" + kit.Permission + "' could not be registered: " +
                        exception.Message);
                }
            }
        }

        #endregion

        #region ServerAssist integration

        private void RegisterServerAssistHook()
        {
            LoadConfigIfNeeded();
            LoadDataIfNeeded();

            Plugin serverAssist = plugins.Find(ServerAssistPluginName);
            if (serverAssist == null)
            {
                return;
            }

            object result = InvokePlugin(
                serverAssist,
                "RegisterGuiHook",
                new[]
                {
                    typeof(string), typeof(string), typeof(string),
                    typeof(string), typeof(string), typeof(string)
                },
                new object[]
                {
                    HookId,
                    config.HookLabel,
                    config.HookDescription,
                    config.HookPermission,
                    Name,
                    nameof(HandleGuiRequest)
                });

            bool registered = result is bool && (bool)result;
            if (registered)
            {
                serverAssist.Call("SetPanelIcon", HookId, KitIcons.Panel);
            }

            if (registered && !hookRegistered)
            {
                hookRegistered = true;
                Interface.Oxide.LogInfo("[AdminKits] registered ServerAssist GUI hook '" + HookId + "'.");
            }
            else if (!registered && !hookRegistered)
            {
                Interface.Oxide.LogWarning("[AdminKits] ServerAssist is loaded but the GUI hook could not be registered.");
            }
        }

        private void UnregisterServerAssistHook()
        {
            Plugin serverAssist = plugins.Find(ServerAssistPluginName);
            if (serverAssist == null)
            {
                return;
            }

            InvokePlugin(
                serverAssist,
                "UnregisterGuiHook",
                new[] { typeof(string) },
                new object[] { HookId });
        }

        private bool OpenKitsForPlayer(string playerId, string subAction)
        {
            Plugin serverAssist = plugins.Find(ServerAssistPluginName);
            if (serverAssist == null)
            {
                return false;
            }

            object result = InvokePlugin(
                serverAssist,
                "OpenHookForPlayer",
                new[] { typeof(string), typeof(string), typeof(string) },
                new object[] { playerId, HookId, subAction });

            return result is bool && (bool)result;
        }

        private object InvokePlugin(Plugin plugin, string methodName, Type[] parameterTypes, object[] args)
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
                    Interface.Oxide.LogWarning("[AdminKits] " + plugin.Name + " does not expose " + methodName + ".");
                    return null;
                }

                return method.Invoke(plugin, args);
            }
            catch (TargetInvocationException exception)
            {
                Exception inner = exception.InnerException ?? exception;
                Interface.Oxide.LogError("[AdminKits] " + plugin.Name + "." + methodName + " failed: " + inner);
                return null;
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogError("[AdminKits] " + plugin.Name + "." + methodName + " failed: " + exception);
                return null;
            }
        }

        #endregion

        #region GUI entry point

        /// <summary>
        /// Called by ServerAssist while the Admin Kits hook is active.
        /// Returns the ServerAssist line protocol.
        /// </summary>
        public string HandleGuiRequest(string playerId, string payload)
        {
            try
            {
                LoadConfigIfNeeded();
                LoadDataIfNeeded();

                if (!permission.IsLoaded)
                {
                    return ErrorView("The Oxide permission system is not loaded.");
                }

                if (string.IsNullOrEmpty(playerId))
                {
                    return ErrorView("Could not determine your player id.");
                }

                if (!CanUse(playerId))
                {
                    return ErrorView("You do not have permission to use Admin Kits.");
                }

                Session session = GetSession(playerId);

                if (string.IsNullOrEmpty(payload) || payload == "open")
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

                return ErrorView("Unknown kit request.");
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogException("[AdminKits] GUI request failed", exception);
                return ErrorView("Internal error: " + exception.Message);
            }
        }

        private Session GetSession(string playerId)
        {
            Session session;
            if (!sessions.TryGetValue(playerId, out session))
            {
                session = new Session();
                sessions[playerId] = session;
            }

            return session;
        }

        private void ResetSession(Session session)
        {
            session.Screen = "home";
            session.ReturnScreen = "home";
            session.SelectedKitId = null;
            session.SelectedItemIndex = -1;
            session.Page = 0;
            session.Status = string.Empty;
            ClearPrompt(session);
        }

        private void ClearPrompt(Session session)
        {
            session.InputMode = null;
            session.InputLabel = string.Empty;
            session.InputValue = string.Empty;
        }

        private void PromptInput(Session session, string mode, string label, string value)
        {
            session.InputMode = mode;
            session.InputLabel = label ?? string.Empty;
            session.InputValue = value ?? string.Empty;
        }

        private string ErrorView(string message)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("v1\n");
            builder.Append("title=").Append(Encode(PluginTitle)).Append('\n');
            builder.Append("subtitle=").Append(Encode("Kits unavailable")).Append('\n');
            builder.Append("status=").Append(Encode(message)).Append('\n');
            builder.Append("banner=").Append(Encode(KitIcons.Warning)).Append('\n');
            builder.Append("background=").Append(Encode(KitIcons.Warning)).Append('\n');
            builder.Append("input=||0|\n");
            builder.Append("row=0||").Append(Encode("No data")).Append('|').Append(Encode(message))
                .Append("|0|0|").Append(Encode(KitIcons.Warning)).Append("|ERROR|")
                .Append(Encode(KitIcons.TintLimit)).Append('\n');
            builder.Append("btn=0|refresh|").Append(Encode("Retry")).Append("|1||")
                .Append(Encode(KitIcons.Refresh)).Append("|\n");
            return builder.ToString();
        }

        #endregion

        #region Actions

        private string HandleAction(string playerId, Session session, string action)
        {
            string[] parts = action.Split('|');
            string command = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
            session.Status = string.Empty;
            ClearPrompt(session);

            bool admin = HasAdminPermission(playerId);

            switch (command)
            {
                case "open":
                    ResetSession(session);
                    break;

                case "home":
                    session.Screen = "home";
                    session.ReturnScreen = "home";
                    session.SelectedKitId = null;
                    session.SelectedItemIndex = -1;
                    session.Page = 0;
                    break;

                case "help":
                    session.Screen = "help";
                    session.Page = 0;
                    break;

                case "list":
                    session.Screen = "list";
                    session.Page = 0;
                    break;

                case "manage":
                    if (!admin)
                    {
                        session.Status = "You need " + PermAdmin + " to manage kits.";
                        break;
                    }

                    session.Screen = "manage";
                    session.Page = 0;
                    break;

                case "detail":
                    if (parts.Length > 1)
                    {
                        KitDefinition kit = FindKit(parts[1]);
                        if (kit == null)
                        {
                            session.Status = "That kit no longer exists.";
                            break;
                        }

                        if (!admin && !IsKitVisible(playerId, kit))
                        {
                            session.Status = "That kit is not available to you.";
                            break;
                        }

                        session.ReturnScreen = session.Screen == "manage" ? "manage" : "list";
                        session.Screen = "detail";
                        session.SelectedKitId = kit.Id;
                        session.SelectedItemIndex = -1;
                        session.Page = 0;
                    }
                    break;

                case "collect":
                    if (parts.Length > 1)
                    {
                        KitDefinition kit = FindKit(parts[1]);
                        string message = null;
                        if (kit != null && CollectKit(playerId, kit, playerId, false, out message))
                        {
                            session.Status = message;
                        }
                        else
                        {
                            session.Status = message ?? "That kit is not available.";
                        }

                        session.Screen = "detail";
                        session.SelectedKitId = kit != null ? kit.Id : session.SelectedKitId;
                    }
                    break;

                case "give":
                    if (!admin)
                    {
                        session.Status = "You need " + PermAdmin + " to give kits.";
                        break;
                    }

                    if (!HasGivePermission(playerId))
                    {
                        session.Status = "You need " + PermGive + " to give kits.";
                        break;
                    }

                    if (parts.Length > 1)
                    {
                        KitDefinition kit = FindKit(parts[1]);
                        if (kit == null)
                        {
                            session.Status = "That kit no longer exists.";
                            break;
                        }

                        session.SelectedKitId = kit.Id;
                        session.Screen = "detail";
                        PromptInput(session, "give", "Player name or id to receive this kit", string.Empty);
                    }
                    break;

                case "edit":
                    if (!RequireAdmin(playerId, session))
                    {
                        break;
                    }

                    if (parts.Length > 1)
                    {
                        KitDefinition kit = FindKit(parts[1]);
                        if (kit == null)
                        {
                            session.Status = "That kit no longer exists.";
                            break;
                        }

                        session.SelectedKitId = kit.Id;
                        session.Screen = "edit";
                    }
                    break;

                case "limits":
                    if (!RequireAdmin(playerId, session))
                    {
                        break;
                    }

                    session.Screen = "limits";
                    break;

                case "items":
                    if (!RequireAdmin(playerId, session))
                    {
                        break;
                    }

                    if (parts.Length > 1)
                    {
                        KitDefinition kit = FindKit(parts[1]);
                        if (kit != null)
                        {
                            session.SelectedKitId = kit.Id;
                        }
                    }

                    session.Screen = "items";
                    session.Page = 0;
                    break;

                case "item":
                    if (!RequireAdmin(playerId, session))
                    {
                        break;
                    }

                    if (parts.Length > 2)
                    {
                        int index;
                        if (int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
                        {
                            session.SelectedItemIndex = index;
                            session.Screen = "itemdetail";
                        }
                    }
                    break;

                case "itemremove":
                    if (!RequireAdmin(playerId, session))
                    {
                        break;
                    }

                    if (parts.Length > 2)
                    {
                        int index;
                        if (int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
                        {
                            RemoveKitItem(playerId, session, index);
                        }
                    }
                    break;

                case "clearitems":
                    if (!RequireAdmin(playerId, session))
                    {
                        break;
                    }

                    session.Screen = "confirmclearitems";
                    break;

                case "confirmclearitems":
                    if (!RequireAdmin(playerId, session))
                    {
                        break;
                    }

                    ClearKitItems(playerId, session);
                    break;

                case "additem":
                    if (!RequireAdmin(playerId, session))
                    {
                        break;
                    }

                    PromptInput(session, "additem", "Item [amount] [quality], e.g. 9mmBullet 100 6", string.Empty);
                    break;

                case "addheld":
                    if (!RequireAdmin(playerId, session))
                    {
                        break;
                    }

                    AddHeldItem(playerId, session);
                    break;

                case "create":
                    if (!RequireAdmin(playerId, session))
                    {
                        break;
                    }

                    PromptInput(session, "create", "New kit name", string.Empty);
                    break;

                case "frominv":
                    if (!RequireAdmin(playerId, session))
                    {
                        break;
                    }

                    PromptInput(session, "frominv", "New kit name (copies your toolbelt and backpack)", string.Empty);
                    break;

                case "delete":
                    if (!RequireAdmin(playerId, session))
                    {
                        break;
                    }

                    session.Screen = "confirmdelete";
                    break;

                case "confirmdelete":
                    if (!RequireAdmin(playerId, session))
                    {
                        break;
                    }

                    DeleteSelectedKit(playerId, session);
                    break;

                case "toggle":
                    if (!RequireAdmin(playerId, session))
                    {
                        break;
                    }

                    if (parts.Length > 2)
                    {
                        ToggleKitFlag(playerId, session, parts[2]);
                    }
                    break;

                case "setname":
                case "setperm":
                case "setcooldown":
                case "setmaxuses":
                case "setmaxper":
                case "setdesc":
                    if (!RequireAdmin(playerId, session))
                    {
                        break;
                    }

                    PromptForField(session, command);
                    break;

                case "page":
                    if (parts.Length > 1)
                    {
                        session.Page += parts[1].Equals("next", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
                        if (session.Page < 0)
                        {
                            session.Page = 0;
                        }
                    }
                    break;

                case "refresh":
                    session.Status = "Refreshed.";
                    break;

                case "cancel":
                    session.Status = "Input cancelled.";
                    break;

                case "back":
                    GoBack(session);
                    break;

                default:
                    session.Status = "Unknown action: " + command;
                    break;
            }

            return Render(playerId, session);
        }

        private void GoBack(Session session)
        {
            switch (session.Screen)
            {
                case "itemdetail":
                    session.Screen = "items";
                    break;
                case "items":
                    session.Screen = "detail";
                    break;
                case "limits":
                    session.Screen = "edit";
                    break;
                case "edit":
                    session.Screen = "detail";
                    break;
                case "confirmdelete":
                    session.Screen = "edit";
                    break;
                case "confirmclearitems":
                    session.Screen = "items";
                    break;
                case "detail":
                    session.Screen = string.IsNullOrEmpty(session.ReturnScreen) ? "list" : session.ReturnScreen;
                    break;
                case "list":
                case "manage":
                case "help":
                    session.Screen = "home";
                    break;
                default:
                    session.Screen = "home";
                    break;
            }
        }

        private string HandleText(string playerId, Session session, string value)
        {
            string mode = session.InputMode;
            string text = value ?? string.Empty;
            ClearPrompt(session);

            if (string.IsNullOrEmpty(mode))
            {
                session.Status = "Nothing is waiting for text input.";
                return Render(playerId, session);
            }

            bool admin = HasAdminPermission(playerId);

            switch (mode)
            {
                case "create":
                    HandleCreateKit(playerId, session, text, false);
                    break;

                case "frominv":
                    HandleCreateKit(playerId, session, text, true);
                    break;

                case "rename":
                    HandleRename(playerId, session, text);
                    break;

                case "setperm":
                    HandleSetPermission(playerId, session, text);
                    break;

                case "setcooldown":
                    HandleSetCooldown(playerId, session, text);
                    break;

                case "setmaxuses":
                    HandleSetMaxUses(playerId, session, text);
                    break;

                case "setmaxper":
                    HandleSetMaxPerPlayer(playerId, session, text);
                    break;

                case "setdesc":
                    HandleSetDescription(playerId, session, text);
                    break;

                case "additem":
                    HandleAddItem(playerId, session, text);
                    break;

                case "give":
                    HandleGive(playerId, session, text);
                    break;

                default:
                    session.Status = "Unknown input action.";
                    break;
            }

            if (!admin)
            {
                session.Screen = session.Screen == "edit" || session.Screen == "items" ||
                                 session.Screen == "limits" || session.Screen == "manage"
                    ? "home"
                    : session.Screen;
            }

            return Render(playerId, session);
        }

        private void PromptForField(Session session, string action)
        {
            KitDefinition kit = GetSelectedKit(session);
            if (kit == null)
            {
                session.Status = "That kit no longer exists.";
                return;
            }

            switch (action)
            {
                case "setname":
                    PromptInput(session, "rename", "Kit name", kit.Name);
                    break;
                case "setperm":
                    PromptInput(session, "setperm",
                        "Permission (blank = everyone, auto = " + PermKitPrefix + kit.Id + ")", kit.Permission);
                    break;
                case "setcooldown":
                    PromptInput(session, "setcooldown", "Cooldown minutes (0 = none)",
                        kit.CooldownMinutes.ToString(CultureInfo.InvariantCulture));
                    break;
                case "setmaxuses":
                    PromptInput(session, "setmaxuses", "Max total uses (0 = unlimited)",
                        kit.MaxUses.ToString(CultureInfo.InvariantCulture));
                    break;
                case "setmaxper":
                    PromptInput(session, "setmaxper", "Max uses per player (0 = unlimited)",
                        kit.MaxPerPlayer.ToString(CultureInfo.InvariantCulture));
                    break;
                case "setdesc":
                    PromptInput(session, "setdesc", "Kit description", kit.Description);
                    break;
            }
        }

        #endregion

        #region Kit actions

        private void HandleCreateKit(string playerId, Session session, string text, bool fromInventory)
        {
            if (!HasAdminPermission(playerId))
            {
                session.Status = "You need " + PermAdmin + " to create kits.";
                return;
            }

            string name = Clean(text, 48);
            if (name.Length == 0)
            {
                session.Status = "Type a kit name.";
                return;
            }

            List<KitItem> items = null;
            if (fromInventory)
            {
                EntityPlayer player = GetEntity(playerId);
                if (player == null)
                {
                    session.Status = "You must be spawned in the world to copy your inventory.";
                    return;
                }

                int stacks;
                int units;
                items = CaptureInventory(player, out stacks, out units);
                if (items.Count == 0)
                {
                    session.Status = "Your toolbelt and backpack are empty.";
                    return;
                }
            }

            string error;
            KitDefinition kit = CreateKit(name, playerId, items, out error);
            if (kit == null)
            {
                session.Status = error ?? "Could not create that kit.";
                return;
            }

            if (fromInventory && config.ClearInventoryOnCapture)
            {
                EntityPlayer player = GetEntity(playerId);
                if (player != null)
                {
                    ClearInventory(player);
                }
            }

            session.SelectedKitId = kit.Id;
            session.Screen = "edit";
            session.Status = "Created kit '" + kit.Name + "' with " + (kit.Items.Count) + " item stack" +
                             (kit.Items.Count == 1 ? string.Empty : "s") + ".";
        }

        private void HandleRename(string playerId, Session session, string text)
        {
            KitDefinition kit = GetSelectedKit(session);
            if (kit == null)
            {
                session.Status = "That kit no longer exists.";
                return;
            }

            string name = Clean(text, 48);
            if (name.Length == 0)
            {
                session.Status = "The kit name cannot be blank.";
                return;
            }

            for (int i = 0; i < data.Kits.Count; i++)
            {
                KitDefinition other = data.Kits.ElementAt(i).Value;
                if (other != null && other != kit && string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    session.Status = "Another kit already uses that name.";
                    return;
                }
            }

            kit.Name = name;
            SaveData();
            session.Status = "Renamed the kit to '" + name + "'.";
        }

        private void HandleSetPermission(string playerId, Session session, string text)
        {
            KitDefinition kit = GetSelectedKit(session);
            if (kit == null)
            {
                session.Status = "That kit no longer exists.";
                return;
            }

            string value = Clean(text, 96);
            if (value.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                value = PermKitPrefix + kit.Id;
            }

            if (value.StartsWith(PermKitPrefix, StringComparison.OrdinalIgnoreCase) &&
                !permission.PermissionExists(value))
            {
                permission.RegisterPermission(value, this);
            }

            kit.Permission = value;
            SaveData();
            session.Status = value.Length == 0
                ? "This kit is now available to everyone with " + PermUse + "."
                : "This kit now requires '" + value + "'.";
        }

        private void HandleSetCooldown(string playerId, Session session, string text)
        {
            KitDefinition kit = GetSelectedKit(session);
            if (kit == null)
            {
                session.Status = "That kit no longer exists.";
                return;
            }

            int value;
            if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < 0)
            {
                session.Status = "Type a whole number of minutes (0 = no cooldown).";
                return;
            }

            kit.CooldownMinutes = Clamp(value, 0, 525600);
            SaveData();
            session.Status = kit.CooldownMinutes == 0
                ? "Cooldown removed."
                : "Cooldown set to " + kit.CooldownMinutes + " minute(s).";
        }

        private void HandleSetMaxUses(string playerId, Session session, string text)
        {
            KitDefinition kit = GetSelectedKit(session);
            if (kit == null)
            {
                session.Status = "That kit no longer exists.";
                return;
            }

            int value;
            if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < 0)
            {
                session.Status = "Type a whole number (0 = unlimited).";
                return;
            }

            kit.MaxUses = Clamp(value, 0, 1000000);
            SaveData();
            session.Status = kit.MaxUses == 0
                ? "Total uses are now unlimited."
                : "Total uses limited to " + kit.MaxUses + ".";
        }

        private void HandleSetMaxPerPlayer(string playerId, Session session, string text)
        {
            KitDefinition kit = GetSelectedKit(session);
            if (kit == null)
            {
                session.Status = "That kit no longer exists.";
                return;
            }

            int value;
            if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < 0)
            {
                session.Status = "Type a whole number (0 = unlimited).";
                return;
            }

            kit.MaxPerPlayer = Clamp(value, 0, 1000000);
            SaveData();
            session.Status = kit.MaxPerPlayer == 0
                ? "Per-player uses are now unlimited."
                : "Each player may collect this kit " + kit.MaxPerPlayer + " time(s).";
        }

        private void HandleSetDescription(string playerId, Session session, string text)
        {
            KitDefinition kit = GetSelectedKit(session);
            if (kit == null)
            {
                session.Status = "That kit no longer exists.";
                return;
            }

            kit.Description = Clean(text, 160);
            SaveData();
            session.Status = kit.Description.Length == 0 ? "Description cleared." : "Description updated.";
        }

        private void HandleAddItem(string playerId, Session session, string text)
        {
            KitDefinition kit = GetSelectedKit(session);
            if (kit == null)
            {
                session.Status = "That kit no longer exists.";
                return;
            }

            string message;
            if (!AddItemToKit(kit, text, out message))
            {
                session.Status = message;
                PromptInput(session, "additem", "Item [amount] [quality], e.g. 9mmBullet 100 6", text);
                return;
            }

            SaveData();
            session.Screen = "items";
            session.Status = message;
        }

        private void AddHeldItem(string playerId, Session session)
        {
            KitDefinition kit = GetSelectedKit(session);
            if (kit == null)
            {
                session.Status = "That kit no longer exists.";
                return;
            }

            if (kit.Items.Count >= config.MaxItemsPerKit)
            {
                session.Status = "This kit already has the maximum of " + config.MaxItemsPerKit + " item stacks.";
                return;
            }

            EntityPlayer player = GetEntity(playerId);
            if (player == null || player.inventory == null)
            {
                session.Status = "You must be spawned in the world.";
                return;
            }

            ItemStack held = null;
            try
            {
                held = player.inventory.holdingItemStack;
            }
            catch
            {
                held = null;
            }

            if (held == null || held.IsEmpty() || held.itemValue == null || held.itemValue.IsEmpty())
            {
                session.Status = "You are not holding an item.";
                return;
            }

            string dataString = SerializeItemValue(held.itemValue);
            if (dataString.Length == 0)
            {
                session.Status = "Could not serialize the held item.";
                return;
            }

            kit.Items.Add(new KitItem
            {
                Item = held.itemValue.ItemClass != null ? held.itemValue.ItemClass.Name : string.Empty,
                Count = Math.Max(1, held.count),
                Data = dataString
            });

            SaveData();
            session.Screen = "items";
            session.Status = "Added your held item (" + (held.itemValue.ItemClass != null
                ? ItemDisplayName(held.itemValue.ItemClass)
                : "unknown") + " x" + Math.Max(1, held.count) + ") to the kit.";
        }

        private void RemoveKitItem(string playerId, Session session, int index)
        {
            KitDefinition kit = GetSelectedKit(session);
            if (kit == null)
            {
                session.Status = "That kit no longer exists.";
                return;
            }

            if (index < 0 || index >= kit.Items.Count)
            {
                session.Status = "That item is no longer in the kit.";
                return;
            }

            KitItem removed = kit.Items[index];
            kit.Items.RemoveAt(index);
            session.SelectedItemIndex = -1;
            session.Screen = "items";
            SaveData();
            session.Status = "Removed " + GetItemLabel(removed) + " from the kit.";
        }

        private void ClearKitItems(string playerId, Session session)
        {
            KitDefinition kit = GetSelectedKit(session);
            if (kit == null)
            {
                session.Status = "That kit no longer exists.";
                return;
            }

            int count = kit.Items.Count;
            kit.Items.Clear();
            session.Screen = "items";
            session.Page = 0;
            SaveData();
            session.Status = "Removed all " + count + " item stack(s) from the kit.";
        }

        private void DeleteSelectedKit(string playerId, Session session)
        {
            KitDefinition kit = GetSelectedKit(session);
            if (kit == null)
            {
                session.Status = "That kit no longer exists.";
                return;
            }

            string name = kit.Name;
            data.Kits.Remove(kit.Id);

            foreach (Dictionary<string, ClaimRecord> claims in data.Claims.Values)
            {
                if (claims != null)
                {
                    claims.Remove(kit.Id);
                }
            }

            session.SelectedKitId = null;
            session.Screen = "manage";
            session.Page = 0;
            SaveData();
            session.Status = "Deleted kit '" + name + "'.";
            Interface.Oxide.LogInfo("[AdminKits] " + GetPlayerName(playerId) + " deleted kit '" + name + "'.");
        }

        private void ToggleKitFlag(string playerId, Session session, string flag)
        {
            KitDefinition kit = GetSelectedKit(session);
            if (kit == null)
            {
                session.Status = "That kit no longer exists.";
                return;
            }

            if (flag.Equals("enabled", StringComparison.OrdinalIgnoreCase))
            {
                kit.Enabled = !kit.Enabled;
                session.Status = kit.Enabled ? "Kit enabled." : "Kit disabled.";
            }
            else if (flag.Equals("hidden", StringComparison.OrdinalIgnoreCase))
            {
                kit.Hidden = !kit.Hidden;
                session.Status = kit.Hidden ? "Kit hidden from players." : "Kit visible to players.";
            }
            else
            {
                session.Status = "Unknown kit flag.";
                return;
            }

            SaveData();
        }

        private void HandleGive(string playerId, Session session, string text)
        {
            if (!HasAdminPermission(playerId) || !HasGivePermission(playerId))
            {
                session.Status = "You do not have permission to give kits.";
                return;
            }

            KitDefinition kit = GetSelectedKit(session);
            if (kit == null)
            {
                session.Status = "That kit no longer exists.";
                return;
            }

            IPlayer target = ResolvePlayer(text);
            if (target == null || !target.IsConnected)
            {
                session.Status = "That player is not online. Try again.";
                PromptInput(session, "give", "Player name or id to receive this kit", text);
                return;
            }

            string message;
            if (CollectKit(playerId, kit, target.Id, true, out message))
            {
                session.Status = message;
            }
            else
            {
                session.Status = message ?? "Could not give that kit.";
            }

            session.Screen = "detail";
            session.SelectedKitId = kit.Id;
        }

        #endregion

        #region Kit model helpers

        private KitDefinition GetSelectedKit(Session session)
        {
            return session != null ? FindKit(session.SelectedKitId) : null;
        }

        private KitDefinition FindKit(string key)
        {
            if (string.IsNullOrEmpty(key) || data == null || data.Kits == null)
            {
                return null;
            }

            KitDefinition kit;
            if (data.Kits.TryGetValue(key, out kit))
            {
                return kit;
            }

            string needle = key.Trim();
            for (int i = 0; i < data.Kits.Count; i++)
            {
                KitDefinition candidate = data.Kits.ElementAt(i).Value;
                if (candidate != null && string.Equals(candidate.Name, needle, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return null;
        }

        private KitDefinition CreateKit(string name, string creator, List<KitItem> items, out string error)
        {
            error = null;

            if (data == null)
            {
                data = new StoredData();
            }

            if (data.Kits.Count >= config.MaxKits)
            {
                error = "The maximum number of kits (" + config.MaxKits + ") has been reached.";
                return null;
            }

            string cleanName = Clean(name, 48);
            if (cleanName.Length == 0)
            {
                error = "Type a kit name.";
                return null;
            }

            for (int i = 0; i < data.Kits.Count; i++)
            {
                KitDefinition other = data.Kits.ElementAt(i).Value;
                if (other != null && string.Equals(other.Name, cleanName, StringComparison.OrdinalIgnoreCase))
                {
                    error = "A kit named '" + cleanName + "' already exists.";
                    return null;
                }
            }

            HashSet<string> usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in data.Kits.Keys)
            {
                usedIds.Add(id);
            }

            KitDefinition kit = new KitDefinition
            {
                Id = MakeKitId(cleanName, usedIds),
                Name = cleanName,
                Description = string.Empty,
                Permission = string.Empty,
                Enabled = true,
                Hidden = false,
                CooldownMinutes = config.DefaultCooldownMinutes,
                MaxUses = config.DefaultMaxUses,
                MaxPerPlayer = config.DefaultMaxPerPlayer,
                UseCount = 0,
                CreatedUtcTicks = DateTime.UtcNow.Ticks,
                CreatedBy = creator ?? string.Empty,
                Items = items ?? new List<KitItem>()
            };

            data.Kits[kit.Id] = kit;
            SaveData();

            Interface.Oxide.LogInfo(
                "[AdminKits] " + GetPlayerName(creator) + " created kit '" + kit.Name + "' with " +
                kit.Items.Count + " item stack(s).");

            return kit;
        }

        private static string MakeKitId(string source, HashSet<string> usedIds)
        {
            StringBuilder builder = new StringBuilder();
            string text = source == null ? string.Empty : source.ToLowerInvariant();
            for (int i = 0; i < text.Length && builder.Length < 40; i++)
            {
                char c = text[i];
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    builder.Append(c);
                }
                else if (c == ' ' || c == '_' || c == '-' || c == '.')
                {
                    if (builder.Length > 0 && builder[builder.Length - 1] != '-')
                    {
                        builder.Append('-');
                    }
                }
            }

            string baseId = builder.ToString().Trim(new[] { '-' });
            if (baseId.Length == 0)
            {
                baseId = "kit";
            }

            string id = baseId;
            int suffix = 2;
            while (usedIds.Contains(id))
            {
                id = baseId + "-" + suffix;
                suffix++;
            }

            usedIds.Add(id);
            return id;
        }

        private bool AddItemToKit(KitDefinition kit, string spec, out string message)
        {
            message = null;
            if (kit == null)
            {
                message = "That kit no longer exists.";
                return false;
            }

            if (kit.Items.Count >= config.MaxItemsPerKit)
            {
                message = "This kit already has the maximum of " + config.MaxItemsPerKit + " item stacks.";
                return false;
            }

            string itemName;
            int count;
            int quality;
            if (!ParseItemSpec(spec, out itemName, out count, out quality))
            {
                message = "Use: item [amount] [quality]. Example: 9mmBullet 100 6";
                return false;
            }

            ItemClass itemClass = FindItemClass(itemName);
            if (itemClass == null)
            {
                message = "Item '" + itemName + "' was not found. Use /kitadmin list or the game item name.";
                return false;
            }

            count = Clamp(count, 1, MaxItemCount);
            quality = Clamp(quality <= 0 ? config.DefaultItemQuality : quality, 1, 6);

            ItemValue value = CreateItemValue(itemClass, quality);
            if (value == null || value.IsEmpty())
            {
                message = "Could not create an item value for '" + itemName + "'.";
                return false;
            }

            string dataString = SerializeItemValue(value);
            if (dataString.Length == 0)
            {
                message = "Could not serialize '" + itemName + "'.";
                return false;
            }

            kit.Items.Add(new KitItem
            {
                Item = itemClass.Name,
                Count = count,
                Data = dataString
            });

            message = "Added " + count + "x " + ItemDisplayName(itemClass) + " to the kit.";
            return true;
        }

        private bool ParseItemSpec(string spec, out string itemName, out int count, out int quality)
        {
            itemName = string.Empty;
            count = 1;
            quality = config != null ? config.DefaultItemQuality : 1;

            if (string.IsNullOrEmpty(spec))
            {
                return false;
            }

            string text = spec.Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length == 0)
            {
                return false;
            }

            string[] tokens = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                return false;
            }

            int parsedQuality;
            int parsedCount;
            if (tokens.Length >= 3 &&
                int.TryParse(tokens[tokens.Length - 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedQuality) &&
                int.TryParse(tokens[tokens.Length - 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedCount))
            {
                quality = parsedQuality;
                count = parsedCount;
                itemName = string.Join(" ", tokens, 0, tokens.Length - 2);
            }
            else if (tokens.Length >= 2 &&
                     int.TryParse(tokens[tokens.Length - 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedCount))
            {
                count = parsedCount;
                itemName = string.Join(" ", tokens, 0, tokens.Length - 1);
            }
            else
            {
                itemName = text;
            }

            itemName = itemName.Trim();
            return itemName.Length > 0;
        }

        #endregion

        #region Collection

        private bool CollectKit(string actorId, KitDefinition kit, string targetId, bool adminGive, out string message)
        {
            message = null;

            IPlayer target = ResolvePlayer(targetId);
            EntityPlayer entity = GetEntity(target);
            if (entity == null)
            {
                message = "That player must be online and spawned in the world.";
                return false;
            }

            string reason;
            if (!CanClaim(targetId, kit, adminGive, out reason))
            {
                message = reason;
                return false;
            }

            int stacks = 0;
            int units = 0;
            int failed = 0;

            for (int i = 0; i < kit.Items.Count; i++)
            {
                KitItem item = kit.Items[i];
                int added;
                int addedStacks;
                if (GiveKitItem(entity, item, out added, out addedStacks))
                {
                    units += added;
                    stacks += addedStacks;
                    if (added < item.Count)
                    {
                        failed++;
                    }
                }
                else
                {
                    failed++;
                }
            }

            if (units == 0)
            {
                message = "No kit items could be added. Check the server log and the kit item data.";
                return false;
            }

            kit.UseCount++;
            if (!adminGive)
            {
                ClaimRecord record = GetOrCreateClaim(targetId, kit.Id);
                record.Count++;
                record.LastUtcTicks = DateTime.UtcNow.Ticks;
            }

            SaveData();

            string targetName = target != null ? target.Name : targetId;
            if (adminGive)
            {
                message = "Gave kit '" + kit.Name + "' to " + targetName + " (" + stacks + " stack" +
                          (stacks == 1 ? string.Empty : "s") + ", " + FormatNumber(units) + " item" +
                          (units == 1 ? string.Empty : "s") + ").";
                SendToolbeltMessage(target, "You received kit '" + kit.Name + "' from staff.");
                Interface.Oxide.LogInfo(
                    "[AdminKits] " + GetPlayerName(actorId) + " gave kit '" + kit.Name + "' to " +
                    targetName + " (" + targetId + "), " + units + " item(s).");
            }
            else
            {
                message = "Collected kit '" + kit.Name + "' (" + stacks + " stack" +
                          (stacks == 1 ? string.Empty : "s") + ", " + FormatNumber(units) + " item" +
                          (units == 1 ? string.Empty : "s") + ").";
                SendToolbeltMessage(target, "Collected kit '" + kit.Name + "'.");
                if (config.LogClaims)
                {
                    Interface.Oxide.LogInfo(
                        "[AdminKits] " + targetName + " (" + targetId + ") collected kit '" + kit.Name +
                        "', " + units + " item(s).");
                }
            }

            if (failed > 0)
            {
                message += " " + failed + " stack(s) could not be added; the inventory filled up.";
            }

            return true;
        }

        private bool CanClaim(string playerId, KitDefinition kit, bool adminGive, out string reason)
        {
            reason = null;

            if (kit == null)
            {
                reason = "That kit no longer exists.";
                return false;
            }

            if (kit.Items == null || kit.Items.Count == 0)
            {
                reason = "This kit has no items yet.";
                return false;
            }

            EntityPlayer entity = GetEntity(playerId);
            if (entity == null)
            {
                reason = "You must be online and spawned in the world.";
                return false;
            }

            bool admin = HasAdminPermission(playerId);

            if (!adminGive && !admin)
            {
                if (!kit.Enabled)
                {
                    reason = "This kit is disabled.";
                    return false;
                }

                if (!HasKitPermission(playerId, kit))
                {
                    reason = "You need the '" + kit.Permission + "' permission to collect this kit.";
                    return false;
                }

                if (kit.MaxUses > 0 && kit.UseCount >= kit.MaxUses)
                {
                    reason = "This kit has reached its total use limit.";
                    return false;
                }

                ClaimRecord record = GetClaim(playerId, kit.Id);
                int claimed = record != null ? record.Count : 0;
                if (kit.MaxPerPlayer > 0 && claimed >= kit.MaxPerPlayer)
                {
                    reason = "You have already collected this kit the maximum number of times.";
                    return false;
                }

                if (kit.CooldownMinutes > 0 && record != null && record.LastUtcTicks > 0)
                {
                    DateTime last = new DateTime(record.LastUtcTicks, DateTimeKind.Utc);
                    DateTime ready = last.AddMinutes(kit.CooldownMinutes);
                    DateTime now = DateTime.UtcNow;
                    if (ready > now)
                    {
                        reason = "This kit is on cooldown for " + FormatDuration(ready - now) + ".";
                        return false;
                    }
                }
            }

            if (!CanFitKit(entity, kit))
            {
                reason = "There is not enough inventory space for this kit.";
                return false;
            }

            return true;
        }

        private ClaimRecord GetClaim(string playerId, string kitId)
        {
            if (data == null || data.Claims == null || string.IsNullOrEmpty(playerId) ||
                string.IsNullOrEmpty(kitId))
            {
                return null;
            }

            Dictionary<string, ClaimRecord> records;
            if (!data.Claims.TryGetValue(playerId, out records) || records == null)
            {
                return null;
            }

            ClaimRecord record;
            return records.TryGetValue(kitId, out record) ? record : null;
        }

        private ClaimRecord GetOrCreateClaim(string playerId, string kitId)
        {
            if (data.Claims == null)
            {
                data.Claims = new Dictionary<string, Dictionary<string, ClaimRecord>>(StringComparer.OrdinalIgnoreCase);
            }

            Dictionary<string, ClaimRecord> records;
            if (!data.Claims.TryGetValue(playerId, out records) || records == null)
            {
                records = new Dictionary<string, ClaimRecord>(StringComparer.OrdinalIgnoreCase);
                data.Claims[playerId] = records;
            }

            ClaimRecord record;
            if (!records.TryGetValue(kitId, out record) || record == null)
            {
                record = new ClaimRecord();
                records[kitId] = record;
            }

            return record;
        }

        private bool HasKitPermission(string playerId, KitDefinition kit)
        {
            if (kit == null || string.IsNullOrEmpty(kit.Permission))
            {
                return true;
            }

            return permission.UserHasPermission(playerId, kit.Permission);
        }

        private bool IsKitVisible(string playerId, KitDefinition kit)
        {
            if (kit == null)
            {
                return false;
            }

            if (HasAdminPermission(playerId))
            {
                return kit.Enabled;
            }

            return kit.Enabled && !kit.Hidden && HasKitPermission(playerId, kit);
        }

        private bool GiveKitItem(EntityPlayer player, KitItem item, out int added, out int addedStacks)
        {
            added = 0;
            addedStacks = 0;

            if (player == null || item == null || item.Count <= 0)
            {
                return false;
            }

            ItemValue value = DeserializeItemValue(item.Data);
            ItemClass itemClass = null;
            if (value == null || value.IsEmpty())
            {
                itemClass = FindItemClass(item.Item);
                if (itemClass == null)
                {
                    Interface.Oxide.LogWarning("[AdminKits] kit item '" + item.Item + "' could not be resolved.");
                    return false;
                }

                value = CreateItemValue(itemClass, config.DefaultItemQuality);
            }
            else
            {
                itemClass = value.ItemClass;
            }

            if (value == null || value.IsEmpty())
            {
                return false;
            }

            int maxStack = GetMaxStack(itemClass);
            int remaining = item.Count;

            while (remaining > 0)
            {
                int amount = Math.Min(remaining, maxStack);
                ItemStack stack = new ItemStack(value.Clone(), amount);
                int stackAdded = AddToPlayer(player, stack);
                if (stackAdded <= 0)
                {
                    break;
                }

                added += stackAdded;
                addedStacks++;
                remaining -= stackAdded;
                if (stackAdded < amount)
                {
                    break;
                }
            }

            return added > 0;
        }

        private bool CanFitKit(EntityPlayer player, KitDefinition kit)
        {
            if (player == null || kit == null)
            {
                return false;
            }

            int emptySlots = 0;
            Dictionary<int, int> partial = new Dictionary<int, int>();
            ScanGrid(player.bag != null ? player.bag.ItemGrid : null, ref emptySlots, partial);
            ScanGrid(player.inventory != null ? player.inventory.ItemGrid : null, ref emptySlots, partial);

            long slotsNeeded = 0;

            for (int i = 0; i < kit.Items.Count; i++)
            {
                KitItem item = kit.Items[i];
                if (item == null || item.Count <= 0)
                {
                    continue;
                }

                ItemClass itemClass = ResolveItemClass(item);
                int maxStack = GetMaxStack(itemClass);

                if (maxStack <= 1)
                {
                    slotsNeeded += item.Count;
                    continue;
                }

                int typeId = itemClass != null ? itemClass.Id : -1;
                int capacity = 0;
                if (typeId >= 0)
                {
                    partial.TryGetValue(typeId, out capacity);
                }

                if (item.Count <= capacity)
                {
                    if (typeId >= 0)
                    {
                        partial[typeId] = capacity - item.Count;
                    }

                    continue;
                }

                int remaining = item.Count - capacity;
                if (typeId >= 0)
                {
                    partial[typeId] = 0;
                }

                slotsNeeded += (remaining + maxStack - 1) / maxStack;
            }

            return slotsNeeded <= emptySlots;
        }

        private static void ScanGrid(ItemStackGrid grid, ref int emptySlots, Dictionary<int, int> partial)
        {
            if (grid == null)
            {
                return;
            }

            for (int i = 0; i < grid.Length; i++)
            {
                ItemStack stack = grid.GetItem(i);
                if (stack == null || stack.IsEmpty() || stack.itemValue == null)
                {
                    emptySlots++;
                    continue;
                }

                ItemClass itemClass = stack.itemValue.ItemClass;
                int maxStack = GetMaxStack(itemClass);
                if (maxStack > 1 && stack.count < maxStack)
                {
                    int typeId = stack.itemValue.type;
                    int current;
                    partial.TryGetValue(typeId, out current);
                    partial[typeId] = current + (maxStack - stack.count);
                }
            }
        }

        private static ItemClass ResolveItemClass(KitItem item)
        {
            if (item == null)
            {
                return null;
            }

            ItemValue value = DeserializeItemValue(item.Data);
            if (value != null && !value.IsEmpty() && value.ItemClass != null)
            {
                return value.ItemClass;
            }

            return FindItemClass(item.Item);
        }

        private int AddToPlayer(EntityPlayer player, ItemStack stack)
        {
            if (player == null || stack == null)
            {
                return 0;
            }

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

        private List<KitItem> CaptureInventory(EntityPlayer player, out int stacks, out int units)
        {
            stacks = 0;
            units = 0;
            List<KitItem> result = new List<KitItem>();

            if (player == null)
            {
                return result;
            }

            CaptureGrid(player.inventory != null ? player.inventory.ItemGrid : null, result, ref stacks, ref units);
            CaptureGrid(player.bag != null ? player.bag.ItemGrid : null, result, ref stacks, ref units);

            if (config.CaptureEquipment && player.equipment != null)
            {
                for (int i = 0; i < player.equipment.GetSlotCount(); i++)
                {
                    ItemValue value = player.equipment.GetSlotItem(i);
                    if (value == null || value.IsEmpty())
                    {
                        continue;
                    }

                    string dataString = SerializeItemValue(value);
                    if (dataString.Length == 0)
                    {
                        continue;
                    }

                    result.Add(new KitItem
                    {
                        Item = value.ItemClass != null ? value.ItemClass.Name : string.Empty,
                        Count = 1,
                        Data = dataString
                    });
                    stacks++;
                    units++;
                }
            }

            return result;
        }

        private void CaptureGrid(ItemStackGrid grid, List<KitItem> result, ref int stacks, ref int units)
        {
            if (grid == null)
            {
                return;
            }

            for (int i = 0; i < grid.Length; i++)
            {
                ItemStack stack = grid.GetItem(i);
                if (stack == null || stack.IsEmpty() || stack.itemValue == null || stack.itemValue.IsEmpty())
                {
                    continue;
                }

                string dataString = SerializeItemValue(stack.itemValue);
                if (dataString.Length == 0)
                {
                    continue;
                }

                result.Add(new KitItem
                {
                    Item = stack.itemValue.ItemClass != null ? stack.itemValue.ItemClass.Name : string.Empty,
                    Count = stack.count,
                    Data = dataString
                });
                stacks++;
                units += stack.count;
            }
        }

        private void ClearInventory(EntityPlayer player)
        {
            if (player == null)
            {
                return;
            }

            if (player.inventory != null)
            {
                for (int i = 0; i < player.inventory.SlotCount; i++)
                {
                    player.inventory.SetStackAt(i, null);
                }
            }

            if (player.bag != null && player.bag.ItemGrid != null)
            {
                for (int i = 0; i < player.bag.ItemGrid.Length; i++)
                {
                    player.bag.SetSlot(i, null);
                }
            }

            if (player.equipment != null)
            {
                for (int i = 0; i < player.equipment.GetSlotCount(); i++)
                {
                    player.equipment.SetSlotItem(i, null);
                }
            }
        }

        #endregion

        #region Item helpers

        private static ItemClass FindItemClass(string name)
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
                // Fall through to a partial search.
            }

            string needle = name.Trim().ToLowerInvariant();
            string normalizedNeedle = NormalizeItemKey(name);
            if (normalizedNeedle.Length == 0)
            {
                return null;
            }

            for (int i = 0; i < ItemClass.list.Length; i++)
            {
                ItemClass item = ItemClass.list[i];
                if (item == null)
                {
                    continue;
                }

                string internalName = item.Name ?? string.Empty;
                string displayName = ItemDisplayName(item);
                string itemName = string.Empty;
                try
                {
                    itemName = item.GetItemName() ?? string.Empty;
                }
                catch
                {
                    itemName = string.Empty;
                }

                if (internalName.ToLowerInvariant().IndexOf(needle, StringComparison.Ordinal) >= 0 ||
                    displayName.ToLowerInvariant().IndexOf(needle, StringComparison.Ordinal) >= 0 ||
                    NormalizeItemKey(internalName).IndexOf(normalizedNeedle, StringComparison.Ordinal) >= 0 ||
                    NormalizeItemKey(displayName).IndexOf(normalizedNeedle, StringComparison.Ordinal) >= 0 ||
                    NormalizeItemKey(itemName).IndexOf(normalizedNeedle, StringComparison.Ordinal) >= 0)
                {
                    return item;
                }
            }

            return null;
        }

        private static string NormalizeItemKey(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = char.ToLowerInvariant(value[i]);
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        private static string ItemDisplayName(ItemClass item)
        {
            if (item == null)
            {
                return "Unknown item";
            }

            try
            {
                string name = item.GetItemName();
                if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }
            }
            catch
            {
                // Fall back to the internal name below.
            }

            return string.IsNullOrEmpty(item.Name) ? "Unknown item" : item.Name;
        }

        private static int GetMaxStack(ItemClass item)
        {
            if (item == null)
            {
                return 1;
            }

            try
            {
                if (item.CanStack())
                {
                    return Math.Max(1, item.MaxCount);
                }
            }
            catch
            {
                // Fall back to the raw stack number below.
            }

            if (item.Stacknumber == null)
            {
                return 1;
            }

            return Math.Max(1, item.Stacknumber.Value);
        }

        private static ItemValue CreateItemValue(ItemClass itemClass, int quality)
        {
            if (itemClass == null)
            {
                return null;
            }

            quality = Clamp(quality, 1, 6);

            ItemValue value = null;
            try
            {
                value = ItemClass.CreateItemValue(itemClass.Name, quality, true);
            }
            catch
            {
                value = null;
            }

            if (value == null || value.IsEmpty())
            {
                value = new ItemValue(itemClass.Id, true);
            }

            if (itemClass.HasQuality)
            {
                value.Quality = (ushort)quality;
            }

            return value;
        }

        private static string SerializeItemValue(ItemValue value)
        {
            if (value == null || value.IsEmpty())
            {
                return string.Empty;
            }

            try
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    PooledBinaryWriter writer = new PooledBinaryWriter();
                    writer.SetBaseStream(stream);
                    value.Write(writer);
                    writer.Flush();
                    return Convert.ToBase64String(stream.ToArray());
                }
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogError("[AdminKits] could not serialize an item: " + exception);
                return string.Empty;
            }
        }

        private static ItemValue DeserializeItemValue(string data)
        {
            if (string.IsNullOrEmpty(data) || ItemClass.list == null)
            {
                return null;
            }

            try
            {
                byte[] bytes = Convert.FromBase64String(data);
                using (MemoryStream stream = new MemoryStream(bytes))
                {
                    PooledBinaryReader reader = new PooledBinaryReader();
                    reader.SetBaseStream(stream);
                    ItemValue value = new ItemValue();
                    value.Read(reader);
                    return value;
                }
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogError("[AdminKits] could not deserialize an item: " + exception);
                return null;
            }
        }

        private string GetItemLabel(KitItem item)
        {
            if (item == null)
            {
                return "an unknown item";
            }

            ItemValue value = DeserializeItemValue(item.Data);
            ItemClass itemClass = value != null && !value.IsEmpty() ? value.ItemClass : FindItemClass(item.Item);
            string name = ItemDisplayName(itemClass);
            string label = name;
            if (itemClass != null && itemClass.HasQuality && value != null)
            {
                label += " QL" + value.Quality;
            }

            return label + " x" + item.Count;
        }

        private string GetItemSubLabel(KitItem item)
        {
            if (item == null)
            {
                return "Unknown item";
            }

            ItemValue value = DeserializeItemValue(item.Data);
            ItemClass itemClass = value != null && !value.IsEmpty() ? value.ItemClass : FindItemClass(item.Item);
            string sub = "x" + FormatNumber(item.Count);
            if (itemClass != null && itemClass.HasQuality && value != null)
            {
                sub += "  |  QL" + value.Quality;
            }

            if (value != null && !value.IsEmpty() && value.UseTimes > 0f)
            {
                sub += "  |  durability used " + value.UseTimes.ToString("0.#", CultureInfo.InvariantCulture);
            }

            return sub;
        }

        #endregion

        #region Entity and permission helpers

        private bool CanUse(string playerId)
        {
            return HasAdminPermission(playerId) || permission.UserHasPermission(playerId, PermUse);
        }

        private bool HasAdminPermission(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return false;
            }

            if (playerId.Equals("server_console", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (permission.UserHasPermission(playerId, PermAdmin))
            {
                return true;
            }

            try
            {
                IPlayer player = players.FindPlayerById(playerId);
                return player != null && player.IsAdmin;
            }
            catch
            {
                return false;
            }
        }

        private bool HasGivePermission(string playerId)
        {
            return HasAdminPermission(playerId) || permission.UserHasPermission(playerId, PermGive);
        }

        private bool RequireAdmin(string playerId, Session session)
        {
            if (HasAdminPermission(playerId))
            {
                return true;
            }

            session.Status = "You need " + PermAdmin + " to manage kits.";
            return false;
        }

        private EntityPlayer GetEntity(string playerId)
        {
            return GetEntity(ResolvePlayer(playerId));
        }

        private static EntityPlayer GetEntity(IPlayer player)
        {
            if (player == null || !player.IsConnected)
            {
                return null;
            }

            try
            {
                return player.Object as EntityPlayer;
            }
            catch
            {
                return null;
            }
        }

        private IPlayer ResolvePlayer(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return null;
            }

            string value = input.Trim();
            if (value.Length == 0)
            {
                return null;
            }

            try
            {
                IPlayer byId = players.FindPlayerById(value);
                if (byId != null)
                {
                    return byId;
                }

                List<IPlayer> matches = players.FindPlayers(value).ToList();
                if (matches.Count == 1)
                {
                    return matches[0];
                }

                if (matches.Count > 1)
                {
                    IPlayer exact = matches.FirstOrDefault(m =>
                        m != null && string.Equals(m.Name, value, StringComparison.OrdinalIgnoreCase));
                    return exact ?? matches[0];
                }

                foreach (IPlayer candidate in players.All)
                {
                    if (candidate == null)
                    {
                        continue;
                    }

                    if (string.Equals(candidate.Name, value, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(candidate.Id, value, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }
            catch
            {
                // Ignore malformed searches.
            }

            return null;
        }

        private string GetPlayerName(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return "server";
            }

            IPlayer player = players.FindPlayerById(playerId);
            return player != null && !string.IsNullOrEmpty(player.Name) ? player.Name : playerId;
        }

        private static ClientInfo GetClientInfo(IPlayer player)
        {
            try
            {
                EntityPlayer entity = player != null ? player.Object as EntityPlayer : null;
                if (entity != null && ConnectionManager.Instance != null &&
                    ConnectionManager.Instance.Clients != null)
                {
                    ClientInfo byEntity = ConnectionManager.Instance.Clients.ForEntityId(entity.entityId);
                    if (byEntity != null)
                    {
                        return byEntity;
                    }
                }

                if (player == null || ConnectionManager.Instance == null ||
                    ConnectionManager.Instance.Clients == null)
                {
                    return null;
                }

                foreach (ClientInfo client in ConnectionManager.Instance.Clients.list)
                {
                    if (client == null || client.PlatformId == null)
                    {
                        continue;
                    }

                    if (string.Equals(client.PlatformId.ToString(), player.Id, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(client.PlatformId.CombinedString, player.Id, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(client.PlatformId.ReadablePlatformUserIdentifier, player.Id,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return client;
                    }
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private void SendToolbeltMessage(IPlayer player, string message)
        {
            if (player == null || !player.IsConnected || string.IsNullOrEmpty(message))
            {
                return;
            }

            try
            {
                ClientInfo client = GetClientInfo(player);
                if (client == null)
                {
                    return;
                }

                NetPackageShowToolbeltMessage package =
                    NetPackageManager.GetPackage<NetPackageShowToolbeltMessage>().Setup(message, string.Empty);
                client.SendPackage(package);
            }
            catch
            {
                // Toolbelt messages are a convenience; never fail a collection for one.
            }
        }

        private void Reply(IPlayer player, string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            if (player == null || !player.IsConnected)
            {
                Interface.Oxide.LogInfo("[AdminKits] " + message);
                return;
            }

            player.Reply((config != null ? config.ChatPrefix + " " : string.Empty) + message);
        }

        #endregion

        #region Rendering

        private sealed class RowInfo
        {
            public string Action = string.Empty;
            public string Label = string.Empty;
            public string Sub = string.Empty;
            public bool Enabled = true;
            public bool Selected = false;
            public string Icon = string.Empty;
            public string Badge = string.Empty;
            public string Tint = string.Empty;
        }

        private sealed class ButtonInfo
        {
            public string Action = string.Empty;
            public string Label = string.Empty;
            public string Tooltip = string.Empty;
            public bool Enabled = true;
            public string Icon = string.Empty;
            public string IconTint = string.Empty;
        }

        private string Render(string playerId, Session session)
        {
            List<RowInfo> rows = new List<RowInfo>();
            List<ButtonInfo> buttons = new List<ButtonInfo>();
            string subtitle;

            switch (session.Screen)
            {
                case "list":
                    RenderList(playerId, session, rows, buttons, out subtitle);
                    break;
                case "manage":
                    RenderManage(playerId, session, rows, buttons, out subtitle);
                    break;
                case "detail":
                    RenderDetail(playerId, session, rows, buttons, out subtitle);
                    break;
                case "edit":
                    RenderEdit(playerId, session, rows, buttons, out subtitle);
                    break;
                case "limits":
                    RenderLimits(playerId, session, rows, buttons, out subtitle);
                    break;
                case "items":
                    RenderItems(playerId, session, rows, buttons, out subtitle);
                    break;
                case "itemdetail":
                    RenderItemDetail(playerId, session, rows, buttons, out subtitle);
                    break;
                case "confirmdelete":
                    RenderConfirmDelete(playerId, session, rows, buttons, out subtitle);
                    break;
                case "confirmclearitems":
                    RenderConfirmClear(playerId, session, rows, buttons, out subtitle);
                    break;
                case "help":
                    RenderHelp(rows, buttons, out subtitle);
                    break;
                default:
                    session.Screen = "home";
                    RenderHome(playerId, session, rows, buttons, out subtitle);
                    break;
            }

            ApplyDefaultIcons(session, rows, buttons);
            string art = ScreenArt(session);

            StringBuilder builder = new StringBuilder(4096);
            builder.Append("v1\n");
            builder.Append("title=").Append(Encode(PluginTitle)).Append('\n');
            builder.Append("subtitle=").Append(Encode(subtitle)).Append('\n');
            builder.Append("status=").Append(Encode(session.Status)).Append('\n');
            builder.Append("banner=").Append(Encode(art)).Append('\n');
            builder.Append("background=").Append(Encode(art)).Append('\n');
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
                    .Append(Encode(button.IconTint)).Append('\n');
            }

            return builder.ToString();
        }

        private string ScreenArt(Session session)
        {
            switch (session.Screen)
            {
                case "list":
                    return KitIcons.Collect;
                case "manage":
                    return KitIcons.Manage;
                case "detail":
                    return KitIcons.Kit;
                case "edit":
                    return KitIcons.Edit;
                case "limits":
                    return KitIcons.Time;
                case "items":
                    return KitIcons.Items;
                case "itemdetail":
                    return SelectedItemIcon(session);
                case "confirmdelete":
                    return KitIcons.Delete;
                case "confirmclearitems":
                    return KitIcons.Warning;
                case "help":
                    return KitIcons.Help;
                default:
                    return KitIcons.Panel;
            }
        }

        private string SelectedItemIcon(Session session)
        {
            KitDefinition kit = GetSelectedKit(session);
            if (kit != null && session.SelectedItemIndex >= 0 && session.SelectedItemIndex < kit.Items.Count)
            {
                return ItemIcon(kit.Items[session.SelectedItemIndex]);
            }

            return KitIcons.Items;
        }

        private string ItemIcon(KitItem item)
        {
            if (item == null)
            {
                return KitIcons.Items;
            }

            ItemValue value = DeserializeItemValue(item.Data);
            ItemClass itemClass = value != null && !value.IsEmpty() ? value.ItemClass : FindItemClass(item.Item);
            string name = itemClass != null ? itemClass.Name : item.Item;
            return string.IsNullOrEmpty(name) ? KitIcons.Items : "item:" + name;
        }

        private void ApplyDefaultIcons(Session session, List<RowInfo> rows, List<ButtonInfo> buttons)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                RowInfo row = rows[i];
                if (string.IsNullOrEmpty(row.Icon))
                {
                    row.Icon = DefaultRowIcon(session, row);
                }
            }

            for (int i = 0; i < buttons.Count; i++)
            {
                ButtonInfo button = buttons[i];
                if (string.IsNullOrEmpty(button.Icon))
                {
                    button.Icon = DefaultButtonIcon(button.Action);
                }
            }
        }

        private string DefaultRowIcon(Session session, RowInfo row)
        {
            switch (CommandOf(row.Action))
            {
                case "list":
                    return KitIcons.Collect;
                case "manage":
                    return KitIcons.Manage;
                case "create":
                    return KitIcons.Create;
                case "frominv":
                    return KitIcons.FromInventory;
                case "help":
                    return KitIcons.Help;
                case "detail":
                    return KitIcons.Kit;
                case "item":
                    return KitIcons.Items;
            }

            switch (session.Screen)
            {
                case "detail":
                    return DetailRowIcon(row.Label);
                case "edit":
                    return EditRowIcon(row.Label);
                case "limits":
                    return LimitsRowIcon(row.Label);
                case "itemdetail":
                    return ItemDetailRowIcon(row.Label);
                case "confirmdelete":
                case "confirmclearitems":
                    return ConfirmRowIcon(row.Label);
                default:
                    return string.Empty;
            }
        }

        private static string DetailRowIcon(string label)
        {
            switch (label)
            {
                case "Description": return KitIcons.Log;
                case "Items": return KitIcons.Items;
                case "Permission": return KitIcons.Permission;
                case "Cooldown": return KitIcons.Time;
                case "Uses": return KitIcons.Kit;
                case "Your Claims": return KitIcons.Claims;
                case "Cooldown Status": return KitIcons.Time;
                case "State": return KitIcons.Enabled;
                default: return string.Empty;
            }
        }

        private static string EditRowIcon(string label)
        {
            switch (label)
            {
                case "Name": return KitIcons.Log;
                case "Permission": return KitIcons.Permission;
                case "Cooldown": return KitIcons.Time;
                case "Limits": return KitIcons.Warning;
                case "Description": return KitIcons.Log;
                case "Items": return KitIcons.Items;
                case "State": return KitIcons.Enabled;
                default: return string.Empty;
            }
        }

        private static string LimitsRowIcon(string label)
        {
            switch (label)
            {
                case "Total Uses": return KitIcons.Kit;
                case "Uses So Far": return KitIcons.Claims;
                case "Per Player": return KitIcons.Players;
                case "Cooldown": return KitIcons.Time;
                default: return string.Empty;
            }
        }

        private static string ItemDetailRowIcon(string label)
        {
            switch (label)
            {
                case "Item": return KitIcons.Items;
                case "Internal Name": return KitIcons.Log;
                case "Amount": return KitIcons.Kit;
                case "Quality": return KitIcons.Enabled;
                case "Durability Used": return KitIcons.Edit;
                case "Mods / Metadata": return KitIcons.Items;
                default: return string.Empty;
            }
        }

        private static string ConfirmRowIcon(string label)
        {
            switch (label)
            {
                case "Kit": return KitIcons.Kit;
                case "Items": return KitIcons.Items;
                case "Warning": return KitIcons.Warning;
                default: return string.Empty;
            }
        }

        private static string DefaultButtonIcon(string action)
        {
            switch (CommandOf(action))
            {
                case "collect":
                    return KitIcons.Collect;
                case "give":
                    return KitIcons.Give;
                case "items":
                    return KitIcons.Items;
                case "edit":
                    return KitIcons.Edit;
                case "create":
                    return KitIcons.Create;
                case "frominv":
                    return KitIcons.FromInventory;
                case "additem":
                case "addheld":
                    return KitIcons.Give;
                case "clearitems":
                    return KitIcons.Delete;
                case "setname":
                case "setdesc":
                    return KitIcons.Log;
                case "setperm":
                    return KitIcons.Permission;
                case "setcooldown":
                    return KitIcons.Time;
                case "limits":
                case "setmaxuses":
                    return KitIcons.Warning;
                case "setmaxper":
                    return KitIcons.Players;
                case "delete":
                case "confirmdelete":
                case "itemremove":
                case "confirmclearitems":
                    return KitIcons.Delete;
                case "help":
                    return KitIcons.Help;
                case "refresh":
                    return KitIcons.Refresh;
                default:
                    return string.Empty;
            }
        }

        private static string CommandOf(string action)
        {
            if (string.IsNullOrEmpty(action))
            {
                return string.Empty;
            }

            int separator = action.IndexOf('|');
            return (separator >= 0 ? action.Substring(0, separator) : action).ToLowerInvariant();
        }

        private string GetKitStateIcon(string playerId, KitDefinition kit)
        {
            string badge = GetKitStateBadge(playerId, kit);
            switch (badge)
            {
                case "OFF": return KitIcons.Disabled;
                case "HIDDEN": return KitIcons.Show;
                case "LOCKED":
                case "PERM": return KitIcons.Permission;
                case "LIMIT": return KitIcons.Warning;
                case "WAIT": return KitIcons.Time;
                default: return KitIcons.Kit;
            }
        }

        private string GetKitStateBadge(string playerId, KitDefinition kit)
        {
            if (kit == null)
            {
                return string.Empty;
            }

            if (!kit.Enabled)
            {
                return "OFF";
            }

            if (kit.Hidden)
            {
                return "HIDDEN";
            }

            if (!string.IsNullOrEmpty(kit.Permission))
            {
                if (!HasAdminPermission(playerId) && !HasKitPermission(playerId, kit))
                {
                    return "LOCKED";
                }

                return "PERM";
            }

            if (kit.MaxUses > 0 && kit.UseCount >= kit.MaxUses)
            {
                return "LIMIT";
            }

            ClaimRecord record = GetClaim(playerId, kit.Id);
            if (kit.MaxPerPlayer > 0 && record != null && record.Count >= kit.MaxPerPlayer)
            {
                return "LIMIT";
            }

            if (kit.CooldownMinutes > 0 && record != null && record.LastUtcTicks > 0)
            {
                DateTime ready = new DateTime(record.LastUtcTicks, DateTimeKind.Utc)
                    .AddMinutes(kit.CooldownMinutes);
                if (ready > DateTime.UtcNow)
                {
                    return "WAIT";
                }
            }

            return string.Empty;
        }

        private string GetKitStateTint(string playerId, KitDefinition kit)
        {
            switch (GetKitStateBadge(playerId, kit))
            {
                case "OFF": return KitIcons.TintOff;
                case "HIDDEN": return KitIcons.TintHidden;
                case "LOCKED": return KitIcons.TintLocked;
                case "LIMIT": return KitIcons.TintLimit;
                case "WAIT": return KitIcons.TintWait;
                default: return string.Empty;
            }
        }

        private void RenderHome(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons,
            out string subtitle)
        {
            bool admin = HasAdminPermission(playerId);
            int visible = CountVisibleKits(playerId);
            int total = data != null && data.Kits != null ? data.Kits.Count : 0;

            subtitle = visible + " kit" + (visible == 1 ? string.Empty : "s") + " available";
            if (admin)
            {
                subtitle += "  |  " + total + " total";
            }

            rows.Add(new RowInfo
            {
                Action = "list",
                Label = "Collect a Kit",
                Sub = visible + " kit" + (visible == 1 ? string.Empty : "s") + " available to you",
                Enabled = visible > 0,
                Icon = KitIcons.Collect,
                Badge = "TAKE"
            });

            if (admin)
            {
                rows.Add(new RowInfo
                {
                    Action = "manage",
                    Label = "Manage Kits",
                    Sub = total + " kit" + (total == 1 ? string.Empty : "s") + " defined",
                    Enabled = true,
                    Icon = KitIcons.Manage,
                    Badge = "LIST"
                });

                rows.Add(new RowInfo
                {
                    Action = "create",
                    Label = "Create Kit",
                    Sub = "Start an empty kit and add items",
                    Enabled = true,
                    Icon = KitIcons.Create,
                    Badge = "NEW"
                });

                rows.Add(new RowInfo
                {
                    Action = "frominv",
                    Label = "Create From Inventory",
                    Sub = "Collect your toolbelt and backpack into a new kit",
                    Enabled = true,
                    Icon = KitIcons.FromInventory,
                    Badge = "COPY"
                });
            }

            rows.Add(new RowInfo
            {
                Action = "help",
                Label = "Help",
                Sub = "Commands, permissions and kit tips",
                Enabled = true,
                Icon = KitIcons.Help,
                Badge = "INFO"
            });

            buttons.Add(new ButtonInfo { Action = "refresh", Label = "Refresh", Tooltip = "Redraw this screen." });
            buttons.Add(new ButtonInfo { Action = "help", Label = "Help", Tooltip = "Show kit help." });
        }

        private void RenderList(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons,
            out string subtitle)
        {
            List<KitDefinition> list = GetVisibleKits(playerId);
            int pages = Math.Max(1, (list.Count + UiRows - 1) / UiRows);
            session.Page = Clamp(session.Page, 0, pages - 1);

            subtitle = "Available Kits  |  " + list.Count + " kit" + (list.Count == 1 ? string.Empty : "s") +
                       "  |  page " + (session.Page + 1) + "/" + pages;

            int start = session.Page * UiRows;
            for (int i = 0; i < UiRows && start + i < list.Count; i++)
            {
                KitDefinition kit = list[start + i];
                rows.Add(new RowInfo
                {
                    Action = "detail|" + kit.Id,
                    Label = kit.Name,
                    Sub = GetKitListSub(playerId, kit),
                    Enabled = true,
                    Icon = GetKitStateIcon(playerId, kit),
                    Badge = GetKitStateBadge(playerId, kit),
                    Tint = GetKitStateTint(playerId, kit)
                });
            }

            if (list.Count == 0)
            {
                rows.Add(new RowInfo
                {
                    Label = "No kits available.",
                    Sub = HasAdminPermission(playerId)
                        ? "Use Manage Kits to create your first kit."
                        : "Ask an admin to add one.",
                    Enabled = false,
                    Icon = KitIcons.Warning,
                    Tint = KitIcons.TintOff
                });
            }

            buttons.Add(new ButtonInfo { Action = "back", Label = "Back", Tooltip = "Return to the kit menu." });
            buttons.Add(new ButtonInfo { Action = "home", Label = "Home", Tooltip = "Return to the kit menu." });

            if (session.Page > 0)
            {
                buttons.Add(new ButtonInfo { Action = "page|prev", Label = "< Prev", Tooltip = "Previous page." });
            }

            if (session.Page < pages - 1)
            {
                buttons.Add(new ButtonInfo { Action = "page|next", Label = "Next >", Tooltip = "Next page." });
            }

            buttons.Add(new ButtonInfo { Action = "refresh", Label = "Refresh", Tooltip = "Redraw this list." });
        }

        private void RenderManage(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons,
            out string subtitle)
        {
            List<KitDefinition> list = GetAllKits();
            int pages = Math.Max(1, (list.Count + UiRows - 1) / UiRows);
            session.Page = Clamp(session.Page, 0, pages - 1);

            subtitle = "Manage Kits  |  " + list.Count + " kit" + (list.Count == 1 ? string.Empty : "s") +
                       "  |  page " + (session.Page + 1) + "/" + pages;

            int start = session.Page * UiRows;
            for (int i = 0; i < UiRows && start + i < list.Count; i++)
            {
                KitDefinition kit = list[start + i];
                rows.Add(new RowInfo
                {
                    Action = "detail|" + kit.Id,
                    Label = kit.Name,
                    Sub = GetKitManageSub(playerId, kit),
                    Enabled = true,
                    Icon = GetKitStateIcon(playerId, kit),
                    Badge = GetKitStateBadge(playerId, kit),
                    Tint = GetKitStateTint(playerId, kit)
                });
            }

            if (list.Count == 0)
            {
                rows.Add(new RowInfo
                {
                    Label = "No kits defined yet.",
                    Sub = "Use Create Kit or Create From Inventory.",
                    Enabled = false,
                    Icon = KitIcons.Warning,
                    Tint = KitIcons.TintOff
                });
            }

            buttons.Add(new ButtonInfo { Action = "create", Label = "Create Kit", Tooltip = "Start an empty kit." });
            buttons.Add(new ButtonInfo
            {
                Action = "frominv",
                Label = "From Inventory",
                Tooltip = "Copy your toolbelt and backpack into a new kit."
            });
            buttons.Add(new ButtonInfo { Action = "back", Label = "Back", Tooltip = "Return to the kit menu." });
            buttons.Add(new ButtonInfo { Action = "home", Label = "Home", Tooltip = "Return to the kit menu." });

            if (session.Page > 0)
            {
                buttons.Add(new ButtonInfo { Action = "page|prev", Label = "< Prev", Tooltip = "Previous page." });
            }

            if (session.Page < pages - 1)
            {
                buttons.Add(new ButtonInfo { Action = "page|next", Label = "Next >", Tooltip = "Next page." });
            }

            buttons.Add(new ButtonInfo { Action = "refresh", Label = "Refresh", Tooltip = "Redraw this list." });
        }

        private void RenderDetail(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons,
            out string subtitle)
        {
            KitDefinition kit = GetSelectedKit(session);
            if (kit == null)
            {
                session.Screen = "list";
                RenderList(playerId, session, rows, buttons, out subtitle);
                return;
            }

            bool admin = HasAdminPermission(playerId);
            if (!admin && !IsKitVisible(playerId, kit))
            {
                session.Screen = "list";
                RenderList(playerId, session, rows, buttons, out subtitle);
                return;
            }

            int stacks = kit.Items.Count;
            int units = GetKitUnitCount(kit);

            subtitle = kit.Name + "  |  " + stacks + " stack" + (stacks == 1 ? string.Empty : "s") +
                       "  |  " + FormatNumber(units) + " item" + (units == 1 ? string.Empty : "s");

            rows.Add(new RowInfo
            {
                Label = "Description",
                Sub = kit.Description.Length == 0 ? "No description" : kit.Description,
                Enabled = false
            });
            rows.Add(new RowInfo
            {
                Label = "Items",
                Sub = stacks + " stack" + (stacks == 1 ? string.Empty : "s") + ", " +
                      FormatNumber(units) + " item" + (units == 1 ? string.Empty : "s"),
                Enabled = false
            });
            rows.Add(new RowInfo
            {
                Label = "Permission",
                Sub = kit.Permission.Length == 0 ? "Everyone with " + PermUse : kit.Permission,
                Enabled = false
            });
            rows.Add(new RowInfo
            {
                Label = "Cooldown",
                Sub = kit.CooldownMinutes == 0 ? "None" : kit.CooldownMinutes + " minute(s)",
                Enabled = false
            });
            rows.Add(new RowInfo
            {
                Label = "Uses",
                Sub = kit.MaxUses == 0
                    ? FormatNumber(kit.UseCount) + " / unlimited"
                    : FormatNumber(kit.UseCount) + " / " + FormatNumber(kit.MaxUses),
                Enabled = false
            });

            ClaimRecord record = GetClaim(playerId, kit.Id);
            int claimed = record != null ? record.Count : 0;
            rows.Add(new RowInfo
            {
                Label = "Your Claims",
                Sub = kit.MaxPerPlayer == 0
                    ? claimed + " / unlimited"
                    : claimed + " / " + kit.MaxPerPlayer,
                Enabled = false
            });

            string cooldownSub = "Ready";
            if (kit.CooldownMinutes > 0 && record != null && record.LastUtcTicks > 0)
            {
                DateTime ready = new DateTime(record.LastUtcTicks, DateTimeKind.Utc)
                    .AddMinutes(kit.CooldownMinutes);
                DateTime now = DateTime.UtcNow;
                if (ready > now)
                {
                    cooldownSub = "Wait " + FormatDuration(ready - now);
                }
            }

            rows.Add(new RowInfo { Label = "Cooldown Status", Sub = cooldownSub, Enabled = false });

            if (admin)
            {
                rows.Add(new RowInfo
                {
                    Label = "State",
                    Sub = (kit.Enabled ? "Enabled" : "Disabled") + (kit.Hidden ? ", hidden" : string.Empty),
                    Enabled = false,
                    Icon = kit.Enabled ? (kit.Hidden ? KitIcons.Show : KitIcons.Enabled) : KitIcons.Disabled,
                    Tint = kit.Enabled ? (kit.Hidden ? KitIcons.TintHidden : string.Empty) : KitIcons.TintOff
                });
            }

            string reason;
            bool canClaim = CanClaim(playerId, kit, false, out reason);

            buttons.Add(new ButtonInfo
            {
                Action = "collect|" + kit.Id,
                Label = "Collect",
                Enabled = canClaim,
                Tooltip = canClaim ? "Add this kit to your inventory." : reason
            });

            if (admin)
            {
                buttons.Add(new ButtonInfo
                {
                    Action = "give|" + kit.Id,
                    Label = "Give To",
                    Enabled = HasGivePermission(playerId),
                    Tooltip = "Give this kit to another online player."
                });
                buttons.Add(new ButtonInfo
                {
                    Action = "items|" + kit.Id,
                    Label = "Items",
                    Tooltip = "View and edit the kit's items."
                });
                buttons.Add(new ButtonInfo
                {
                    Action = "edit|" + kit.Id,
                    Label = "Edit",
                    Tooltip = "Rename, set permissions and limits."
                });
            }

            buttons.Add(new ButtonInfo { Action = "back", Label = "Back", Tooltip = "Return to the kit list." });
            buttons.Add(new ButtonInfo { Action = "home", Label = "Home", Tooltip = "Return to the kit menu." });
        }

        private void RenderEdit(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons,
            out string subtitle)
        {
            KitDefinition kit = GetSelectedKit(session);
            if (kit == null)
            {
                session.Screen = "manage";
                RenderManage(playerId, session, rows, buttons, out subtitle);
                return;
            }

            subtitle = "Editing '" + kit.Name + "'";

            rows.Add(new RowInfo { Label = "Name", Sub = kit.Name, Enabled = false });
            rows.Add(new RowInfo
            {
                Label = "Permission",
                Sub = kit.Permission.Length == 0 ? "Everyone" : kit.Permission,
                Enabled = false
            });
            rows.Add(new RowInfo
            {
                Label = "Cooldown",
                Sub = kit.CooldownMinutes == 0 ? "None" : kit.CooldownMinutes + " minute(s)",
                Enabled = false
            });
            rows.Add(new RowInfo
            {
                Label = "Limits",
                Sub = "Total " + (kit.MaxUses == 0 ? "unlimited" : kit.MaxUses.ToString(CultureInfo.InvariantCulture)) +
                      "  |  Per player " +
                      (kit.MaxPerPlayer == 0 ? "unlimited" : kit.MaxPerPlayer.ToString(CultureInfo.InvariantCulture)),
                Enabled = false
            });
            rows.Add(new RowInfo
            {
                Label = "Description",
                Sub = kit.Description.Length == 0 ? "No description" : kit.Description,
                Enabled = false
            });
            rows.Add(new RowInfo
            {
                Label = "Items",
                Sub = kit.Items.Count + " stack" + (kit.Items.Count == 1 ? string.Empty : "s") + ", " +
                      FormatNumber(GetKitUnitCount(kit)) + " item(s)",
                Enabled = false
            });
            rows.Add(new RowInfo
            {
                Label = "State",
                Sub = (kit.Enabled ? "Enabled" : "Disabled") + (kit.Hidden ? ", hidden from players" : string.Empty),
                Enabled = false,
                Icon = kit.Enabled ? (kit.Hidden ? KitIcons.Show : KitIcons.Enabled) : KitIcons.Disabled,
                Tint = kit.Enabled ? (kit.Hidden ? KitIcons.TintHidden : string.Empty) : KitIcons.TintOff
            });

            buttons.Add(new ButtonInfo { Action = "setname", Label = "Name", Tooltip = "Rename this kit." });
            buttons.Add(new ButtonInfo { Action = "setperm", Label = "Permission", Tooltip = "Set the collect permission." });
            buttons.Add(new ButtonInfo { Action = "setcooldown", Label = "Cooldown", Tooltip = "Set the cooldown in minutes." });
            buttons.Add(new ButtonInfo { Action = "limits", Label = "Limits", Tooltip = "Set total and per-player limits." });
            buttons.Add(new ButtonInfo { Action = "setdesc", Label = "Description", Tooltip = "Set the kit description." });
            buttons.Add(new ButtonInfo { Action = "items|" + kit.Id, Label = "Items", Tooltip = "Add or remove kit items." });
            buttons.Add(new ButtonInfo
            {
                Action = "toggle|" + kit.Id + "|enabled",
                Label = kit.Enabled ? "Disable" : "Enable",
                Tooltip = kit.Enabled ? "Hide this kit from collection." : "Allow players to collect this kit.",
                Icon = kit.Enabled ? KitIcons.Disabled : KitIcons.Enabled,
                IconTint = kit.Enabled ? KitIcons.TintOff : string.Empty
            });
            buttons.Add(new ButtonInfo
            {
                Action = "toggle|" + kit.Id + "|hidden",
                Label = kit.Hidden ? "Show" : "Hide",
                Tooltip = kit.Hidden ? "Show this kit in the collect list." : "Hide this kit from players.",
                Icon = KitIcons.Show,
                IconTint = kit.Hidden ? KitIcons.TintHidden : string.Empty
            });
            buttons.Add(new ButtonInfo { Action = "delete|" + kit.Id, Label = "Delete", Tooltip = "Delete this kit." });
            buttons.Add(new ButtonInfo { Action = "back", Label = "Back", Tooltip = "Return to the kit details." });
        }

        private void RenderLimits(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons,
            out string subtitle)
        {
            KitDefinition kit = GetSelectedKit(session);
            if (kit == null)
            {
                session.Screen = "manage";
                RenderManage(playerId, session, rows, buttons, out subtitle);
                return;
            }

            subtitle = "Limits for '" + kit.Name + "'";

            rows.Add(new RowInfo
            {
                Label = "Total Uses",
                Sub = kit.MaxUses == 0 ? "Unlimited" : FormatNumber(kit.MaxUses),
                Enabled = false
            });
            rows.Add(new RowInfo
            {
                Label = "Uses So Far",
                Sub = FormatNumber(kit.UseCount),
                Enabled = false
            });
            rows.Add(new RowInfo
            {
                Label = "Per Player",
                Sub = kit.MaxPerPlayer == 0 ? "Unlimited" : FormatNumber(kit.MaxPerPlayer),
                Enabled = false
            });
            rows.Add(new RowInfo
            {
                Label = "Cooldown",
                Sub = kit.CooldownMinutes == 0 ? "None" : kit.CooldownMinutes + " minute(s)",
                Enabled = false
            });

            buttons.Add(new ButtonInfo { Action = "setmaxuses", Label = "Max Uses", Tooltip = "Set the total use limit." });
            buttons.Add(new ButtonInfo { Action = "setmaxper", Label = "Max Per Player", Tooltip = "Set the per-player limit." });
            buttons.Add(new ButtonInfo { Action = "back", Label = "Back", Tooltip = "Return to the kit editor." });
            buttons.Add(new ButtonInfo { Action = "home", Label = "Home", Tooltip = "Return to the kit menu." });
        }

        private void RenderItems(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons,
            out string subtitle)
        {
            KitDefinition kit = GetSelectedKit(session);
            if (kit == null)
            {
                session.Screen = "manage";
                RenderManage(playerId, session, rows, buttons, out subtitle);
                return;
            }

            List<KitItem> list = kit.Items;
            int pages = Math.Max(1, (list.Count + UiRows - 1) / UiRows);
            session.Page = Clamp(session.Page, 0, pages - 1);

            subtitle = "Items in '" + kit.Name + "'  |  " + list.Count + " stack" +
                       (list.Count == 1 ? string.Empty : "s") + "  |  page " + (session.Page + 1) + "/" + pages;

            int start = session.Page * UiRows;
            for (int i = 0; i < UiRows && start + i < list.Count; i++)
            {
                KitItem item = list[start + i];
                rows.Add(new RowInfo
                {
                    Action = "item|" + kit.Id + "|" + (start + i),
                    Label = GetItemLabel(item),
                    Sub = GetItemSubLabel(item),
                    Enabled = true,
                    Icon = ItemIcon(item)
                });
            }

            if (list.Count == 0)
            {
                rows.Add(new RowInfo
                {
                    Label = "This kit has no items.",
                    Sub = "Use Add Item or create a kit from your inventory.",
                    Enabled = false,
                    Icon = KitIcons.Warning,
                    Tint = KitIcons.TintOff
                });
            }

            buttons.Add(new ButtonInfo
            {
                Action = "additem",
                Label = "Add Item",
                Tooltip = "Add an item by name, amount and quality."
            });
            buttons.Add(new ButtonInfo
            {
                Action = "addheld",
                Label = "Add Held",
                Tooltip = "Add the item currently in your hand."
            });
            buttons.Add(new ButtonInfo
            {
                Action = "clearitems",
                Label = "Clear Items",
                Enabled = list.Count > 0,
                Tooltip = "Remove every item from this kit."
            });
            buttons.Add(new ButtonInfo { Action = "back", Label = "Back", Tooltip = "Return to the kit details." });
            buttons.Add(new ButtonInfo { Action = "home", Label = "Home", Tooltip = "Return to the kit menu." });

            if (session.Page > 0)
            {
                buttons.Add(new ButtonInfo { Action = "page|prev", Label = "< Prev", Tooltip = "Previous page." });
            }

            if (session.Page < pages - 1)
            {
                buttons.Add(new ButtonInfo { Action = "page|next", Label = "Next >", Tooltip = "Next page." });
            }
        }

        private void RenderItemDetail(string playerId, Session session, List<RowInfo> rows,
            List<ButtonInfo> buttons, out string subtitle)
        {
            KitDefinition kit = GetSelectedKit(session);
            KitItem item = kit != null && session.SelectedItemIndex >= 0 &&
                           session.SelectedItemIndex < kit.Items.Count
                ? kit.Items[session.SelectedItemIndex]
                : null;

            if (kit == null || item == null)
            {
                session.Screen = "items";
                RenderItems(playerId, session, rows, buttons, out subtitle);
                return;
            }

            ItemValue value = DeserializeItemValue(item.Data);
            ItemClass itemClass = value != null && !value.IsEmpty() ? value.ItemClass : FindItemClass(item.Item);
            string name = ItemDisplayName(itemClass);

            subtitle = name + "  |  x" + FormatNumber(item.Count);

            rows.Add(new RowInfo { Label = "Item", Sub = name, Enabled = false, Icon = ItemIcon(item) });
            rows.Add(new RowInfo { Label = "Internal Name", Sub = item.Item.Length == 0 ? "(unknown)" : item.Item, Enabled = false });
            rows.Add(new RowInfo { Label = "Amount", Sub = FormatNumber(item.Count), Enabled = false });
            rows.Add(new RowInfo
            {
                Label = "Quality",
                Sub = itemClass != null && itemClass.HasQuality && value != null ? "QL" + value.Quality : "not quality-based",
                Enabled = false
            });
            rows.Add(new RowInfo
            {
                Label = "Durability Used",
                Sub = value != null ? value.UseTimes.ToString("0.##", CultureInfo.InvariantCulture) : "unknown",
                Enabled = false
            });
            rows.Add(new RowInfo
            {
                Label = "Mods / Metadata",
                Sub = item.Data.Length == 0 ? "none stored" : "stored with the item",
                Enabled = false
            });

            buttons.Add(new ButtonInfo
            {
                Action = "itemremove|" + kit.Id + "|" + session.SelectedItemIndex,
                Label = "Remove",
                Tooltip = "Remove this item stack from the kit."
            });
            buttons.Add(new ButtonInfo { Action = "back", Label = "Back", Tooltip = "Return to the item list." });
        }

        private void RenderConfirmDelete(string playerId, Session session, List<RowInfo> rows,
            List<ButtonInfo> buttons, out string subtitle)
        {
            KitDefinition kit = GetSelectedKit(session);
            if (kit == null)
            {
                session.Screen = "manage";
                RenderManage(playerId, session, rows, buttons, out subtitle);
                return;
            }

            subtitle = "Confirm delete";

            rows.Add(new RowInfo { Label = "Kit", Sub = kit.Name, Enabled = false });
            rows.Add(new RowInfo { Label = "Items", Sub = kit.Items.Count + " stack(s)", Enabled = false });
            rows.Add(new RowInfo
            {
                Label = "Warning",
                Sub = "This permanently deletes the kit and its claim records.",
                Enabled = false,
                Icon = KitIcons.Warning,
                Tint = KitIcons.TintLimit
            });

            buttons.Add(new ButtonInfo
            {
                Action = "confirmdelete",
                Label = "Delete Forever",
                Tooltip = "Delete this kit permanently."
            });
            buttons.Add(new ButtonInfo { Action = "back", Label = "Cancel", Tooltip = "Keep this kit." });
        }

        private void RenderConfirmClear(string playerId, Session session, List<RowInfo> rows,
            List<ButtonInfo> buttons, out string subtitle)
        {
            KitDefinition kit = GetSelectedKit(session);
            if (kit == null)
            {
                session.Screen = "manage";
                RenderManage(playerId, session, rows, buttons, out subtitle);
                return;
            }

            subtitle = "Confirm clear items";

            rows.Add(new RowInfo { Label = "Kit", Sub = kit.Name, Enabled = false });
            rows.Add(new RowInfo { Label = "Items", Sub = kit.Items.Count + " stack(s)", Enabled = false });
            rows.Add(new RowInfo
            {
                Label = "Warning",
                Sub = "This removes every item from the kit.",
                Enabled = false,
                Icon = KitIcons.Warning,
                Tint = KitIcons.TintLimit
            });

            buttons.Add(new ButtonInfo
            {
                Action = "confirmclearitems",
                Label = "Clear All Items",
                Tooltip = "Remove every item stack from this kit."
            });
            buttons.Add(new ButtonInfo { Action = "back", Label = "Cancel", Tooltip = "Keep the kit items." });
        }

        private void RenderHelp(List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            subtitle = "Commands, permissions and tips";

            rows.Add(new RowInfo
            {
                Label = "/kits or /kit",
                Sub = "Open the kit window.",
                Enabled = false,
                Icon = KitIcons.Kit
            });
            rows.Add(new RowInfo
            {
                Label = "/kit <name>",
                Sub = "Collect a kit directly in chat.",
                Enabled = false,
                Icon = KitIcons.Collect
            });
            rows.Add(new RowInfo
            {
                Label = "/kit list",
                Sub = "List the kits you can collect.",
                Enabled = false,
                Icon = KitIcons.Collect
            });
            rows.Add(new RowInfo
            {
                Label = "/kitadmin create <name>",
                Sub = "Create an empty kit.",
                Enabled = false,
                Icon = KitIcons.Create
            });
            rows.Add(new RowInfo
            {
                Label = "/kitadmin frominv <name>",
                Sub = "Create a kit from your toolbelt and backpack.",
                Enabled = false,
                Icon = KitIcons.FromInventory
            });
            rows.Add(new RowInfo
            {
                Label = "/kitadmin additem <kit> <item> [n] [q]",
                Sub = "Add an item. Example: /kitadmin additem starter 9mmBullet 100 6",
                Enabled = false,
                Icon = KitIcons.Give
            });
            rows.Add(new RowInfo
            {
                Label = "/kitadmin give <player> <kit>",
                Sub = "Give a kit to an online player.",
                Enabled = false,
                Icon = KitIcons.Give
            });
            rows.Add(new RowInfo
            {
                Label = "Permissions",
                Sub = PermUse + " collect  |  " + PermAdmin + " manage  |  " + PermGive + " give",
                Enabled = false,
                Icon = KitIcons.Permission
            });
            rows.Add(new RowInfo
            {
                Label = "Per-kit permissions",
                Sub = "Set a permission to auto to create " + PermKitPrefix + "<id>",
                Enabled = false,
                Icon = KitIcons.Permission
            });

            buttons.Add(new ButtonInfo { Action = "back", Label = "Back", Tooltip = "Return to the kit menu." });
            buttons.Add(new ButtonInfo { Action = "home", Label = "Home", Tooltip = "Return to the kit menu." });
        }

        private List<KitDefinition> GetAllKits()
        {
            List<KitDefinition> result = new List<KitDefinition>();
            if (data == null || data.Kits == null)
            {
                return result;
            }

            foreach (KitDefinition kit in data.Kits.Values)
            {
                if (kit != null)
                {
                    result.Add(kit);
                }
            }

            return result.OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private List<KitDefinition> GetVisibleKits(string playerId)
        {
            List<KitDefinition> result = new List<KitDefinition>();
            if (data == null || data.Kits == null)
            {
                return result;
            }

            bool admin = HasAdminPermission(playerId);
            foreach (KitDefinition kit in data.Kits.Values)
            {
                if (kit == null || !kit.Enabled)
                {
                    continue;
                }

                if (admin || (!kit.Hidden && HasKitPermission(playerId, kit)))
                {
                    result.Add(kit);
                }
            }

            return result.OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private int CountVisibleKits(string playerId)
        {
            return GetVisibleKits(playerId).Count;
        }

        private string GetKitListSub(string playerId, KitDefinition kit)
        {
            int stacks = kit.Items.Count;
            string sub = stacks + " stack" + (stacks == 1 ? string.Empty : "s");

            if (kit.CooldownMinutes > 0)
            {
                sub += "  |  cooldown " + kit.CooldownMinutes + "m";
            }

            if (kit.Permission.Length > 0)
            {
                sub += "  |  " + kit.Permission;
            }

            ClaimRecord record = GetClaim(playerId, kit.Id);
            if (kit.MaxPerPlayer > 0 && record != null)
            {
                sub += "  |  claimed " + record.Count + "/" + kit.MaxPerPlayer;
            }

            return sub;
        }

        private string GetKitManageSub(string playerId, KitDefinition kit)
        {
            string sub = kit.Items.Count + " stack" + (kit.Items.Count == 1 ? string.Empty : "s");
            sub += kit.Enabled ? "  |  enabled" : "  |  DISABLED";
            if (kit.Hidden)
            {
                sub += ", hidden";
            }

            sub += "  |  uses " + FormatNumber(kit.UseCount) +
                   (kit.MaxUses == 0 ? "/unlimited" : "/" + FormatNumber(kit.MaxUses));
            if (kit.Permission.Length > 0)
            {
                sub += "  |  " + kit.Permission;
            }

            return sub;
        }

        private static int GetKitUnitCount(KitDefinition kit)
        {
            if (kit == null || kit.Items == null)
            {
                return 0;
            }

            long units = 0;
            for (int i = 0; i < kit.Items.Count; i++)
            {
                if (kit.Items[i] != null)
                {
                    units += kit.Items[i].Count;
                }
            }

            return units > int.MaxValue ? int.MaxValue : (int)units;
        }

        #endregion

        #region Commands

        private void CommandKits(IPlayer player, string command, string[] args)
        {
            if (player == null)
            {
                return;
            }

            if (!CanUse(player.Id))
            {
                Reply(player, "You do not have permission to use kits.");
                return;
            }

            if (args == null || args.Length == 0)
            {
                if (!OpenKitsForPlayer(player.Id, "a|open"))
                {
                    Reply(player, "Open the ServerAssist window and choose Admin Kits, or install ServerAssist.");
                }

                return;
            }

            string sub = args[0].ToLowerInvariant();
            if (sub == "help")
            {
                Reply(player, "/kit <name> collects a kit. /kit list shows what you can collect.");
                Reply(player, "Admins: /kitadmin help for management commands.");
                return;
            }

            if (sub == "list")
            {
                List<KitDefinition> kits = GetVisibleKits(player.Id);
                if (kits.Count == 0)
                {
                    Reply(player, "There are no kits available to you.");
                    return;
                }

                Reply(player, "Available kits: " + string.Join(", ", kits.Select(k => k.Name).ToArray()));
                return;
            }

            string name = string.Join(" ", args);
            KitDefinition kit = FindKit(name);
            if (kit == null)
            {
                Reply(player, "Kit '" + name + "' was not found. Use /kit list.");
                return;
            }

            string message;
            if (CollectKit(player.Id, kit, player.Id, false, out message))
            {
                Reply(player, message);
            }
            else
            {
                Reply(player, message ?? "Could not collect that kit.");
            }
        }

        private void CommandKitAdmin(IPlayer player, string command, string[] args)
        {
            if (player == null)
            {
                return;
            }

            if (!HasAdminPermission(player.Id) && !HasGivePermission(player.Id))
            {
                Reply(player, "You do not have permission to manage kits.");
                return;
            }

            if (args == null || args.Length == 0 || args[0].Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                Reply(player, "Kit admin commands:");
                Reply(player, "/kitadmin list");
                Reply(player, "/kitadmin create <name>");
                Reply(player, "/kitadmin frominv <name>");
                Reply(player, "/kitadmin delete <name> confirm");
                Reply(player, "/kitadmin enable|disable <kit>");
                Reply(player, "/kitadmin additem <kit> <item> [amount] [quality]");
                Reply(player, "/kitadmin removeitem <kit> <index>");
                Reply(player, "/kitadmin give <player> <kit>");
                Reply(player, "/kitadmin reset <player> <kit>");
                Reply(player, "/kitadmin reload");
                return;
            }

            string sub = args[0].ToLowerInvariant();

            if (!HasAdminPermission(player.Id) && sub != "give")
            {
                Reply(player, "You need " + PermAdmin + " for that command.");
                return;
            }

            switch (sub)
            {
                case "list":
                    CommandAdminList(player);
                    break;

                case "create":
                    CommandAdminCreate(player, args, false);
                    break;

                case "frominv":
                    CommandAdminCreate(player, args, true);
                    break;

                case "delete":
                    CommandAdminDelete(player, args);
                    break;

                case "enable":
                case "disable":
                    CommandAdminToggle(player, sub, args);
                    break;

                case "additem":
                    CommandAdminAddItem(player, args);
                    break;

                case "removeitem":
                    CommandAdminRemoveItem(player, args);
                    break;

                case "give":
                    CommandAdminGive(player, args);
                    break;

                case "reset":
                    CommandAdminReset(player, args);
                    break;

                case "reload":
                    LoadConfig();
                    LoadData();
                    RegisterKitPermissions();
                    RegisterServerAssistHook();
                    Reply(player, "Reloaded kits, config and the ServerAssist hook.");
                    break;

                default:
                    Reply(player, "Unknown kit admin command. Use /kitadmin help.");
                    break;
            }
        }

        private void CommandAdminList(IPlayer player)
        {
            List<KitDefinition> kits = GetAllKits();
            if (kits.Count == 0)
            {
                Reply(player, "No kits are defined yet.");
                return;
            }

            Reply(player, kits.Count + " kit(s):");
            for (int i = 0; i < kits.Count; i++)
            {
                KitDefinition kit = kits[i];
                string line = "  " + kit.Id + " - " + kit.Name + " [" + kit.Items.Count + " stacks, " +
                              (kit.Enabled ? "enabled" : "disabled") +
                              (kit.Hidden ? ", hidden" : string.Empty) +
                              ", uses " + kit.UseCount + (kit.MaxUses == 0 ? "/unlimited" : "/" + kit.MaxUses) +
                              "]";
                if (kit.Permission.Length > 0)
                {
                    line += " perm " + kit.Permission;
                }

                Reply(player, line);
            }
        }

        private void CommandAdminCreate(IPlayer player, string[] args, bool fromInventory)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /kitadmin " + (fromInventory ? "frominv" : "create") + " <name>");
                return;
            }

            string name = string.Join(" ", args, 1, args.Length - 1);
            List<KitItem> items = null;

            if (fromInventory)
            {
                EntityPlayer entity = GetEntity(player.Id);
                if (entity == null)
                {
                    Reply(player, "You must be spawned in the world to copy your inventory.");
                    return;
                }

                int stacks;
                int units;
                items = CaptureInventory(entity, out stacks, out units);
                if (items.Count == 0)
                {
                    Reply(player, "Your toolbelt and backpack are empty.");
                    return;
                }
            }

            string error;
            KitDefinition kit = CreateKit(name, player.Id, items, out error);
            if (kit == null)
            {
                Reply(player, error ?? "Could not create that kit.");
                return;
            }

            if (fromInventory && config.ClearInventoryOnCapture)
            {
                EntityPlayer entity = GetEntity(player.Id);
                if (entity != null)
                {
                    ClearInventory(entity);
                }
            }

            Reply(player, "Created kit '" + kit.Name + "' (" + kit.Id + ") with " + kit.Items.Count +
                          " item stack(s).");
        }

        private void CommandAdminDelete(IPlayer player, string[] args)
        {
            if (args.Length < 3 || !args[args.Length - 1].Equals("confirm", StringComparison.OrdinalIgnoreCase))
            {
                Reply(player, "Usage: /kitadmin delete <name> confirm");
                return;
            }

            string name = string.Join(" ", args, 1, args.Length - 2);
            KitDefinition kit = FindKit(name);
            if (kit == null)
            {
                Reply(player, "Kit '" + name + "' was not found.");
                return;
            }

            data.Kits.Remove(kit.Id);
            foreach (Dictionary<string, ClaimRecord> claims in data.Claims.Values)
            {
                if (claims != null)
                {
                    claims.Remove(kit.Id);
                }
            }

            SaveData();
            Reply(player, "Deleted kit '" + kit.Name + "'.");
        }

        private void CommandAdminToggle(IPlayer player, string sub, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /kitadmin " + sub + " <id or name>");
                return;
            }

            int consumed;
            KitDefinition kit = ResolveKitFromArgs(args, 1, out consumed);
            if (kit == null)
            {
                Reply(player, "Kit '" + string.Join(" ", args, 1, args.Length - 1) + "' was not found.");
                return;
            }

            kit.Enabled = sub == "enable";
            SaveData();
            Reply(player, "Kit '" + kit.Name + "' is now " + (kit.Enabled ? "enabled" : "disabled") + ".");
        }

        private void CommandAdminAddItem(IPlayer player, string[] args)
        {
            if (args.Length < 3)
            {
                Reply(player, "Usage: /kitadmin additem <id or name> <item> [amount] [quality]");
                return;
            }

            int consumed;
            KitDefinition kit = ResolveKitFromArgs(args, 1, out consumed);
            if (kit == null || 1 + consumed >= args.Length)
            {
                Reply(player, "Kit '" + args[1] + "' was not found, or no item was provided. Use /kitadmin list.");
                return;
            }

            string spec = string.Join(" ", args, 1 + consumed, args.Length - 1 - consumed);
            string message;
            if (!AddItemToKit(kit, spec, out message))
            {
                Reply(player, message);
                return;
            }

            SaveData();
            Reply(player, message);
        }

        private void CommandAdminRemoveItem(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /kitadmin removeitem <id or name> <index>");
                return;
            }

            int consumed;
            KitDefinition kit = ResolveKitFromArgs(args, 1, out consumed);
            if (kit == null)
            {
                Reply(player, "Kit '" + args[1] + "' was not found.");
                return;
            }

            if (1 + consumed >= args.Length)
            {
                Reply(player, "Usage: /kitadmin removeitem <id or name> <index>");
                return;
            }

            int index;
            if (!int.TryParse(args[1 + consumed], NumberStyles.Integer, CultureInfo.InvariantCulture, out index) ||
                index < 0 || index >= kit.Items.Count)
            {
                Reply(player, "Index must be between 0 and " + Math.Max(0, kit.Items.Count - 1) + ".");
                return;
            }

            string label = GetItemLabel(kit.Items[index]);
            kit.Items.RemoveAt(index);
            SaveData();
            Reply(player, "Removed " + label + " from kit '" + kit.Name + "'.");
        }

        private void CommandAdminGive(IPlayer player, string[] args)
        {
            if (!HasGivePermission(player.Id))
            {
                Reply(player, "You need " + PermGive + " to give kits.");
                return;
            }

            if (args.Length < 3)
            {
                Reply(player, "Usage: /kitadmin give <player> <kit id or name>");
                return;
            }

            IPlayer target = ResolvePlayer(args[1]);
            if (target == null || !target.IsConnected)
            {
                Reply(player, "That player is not online.");
                return;
            }

            int consumed;
            KitDefinition kit = ResolveKitFromArgs(args, 2, out consumed);
            if (kit == null)
            {
                Reply(player, "Kit '" + string.Join(" ", args, 2, args.Length - 2) + "' was not found.");
                return;
            }

            string message;
            if (CollectKit(player.Id, kit, target.Id, true, out message))
            {
                Reply(player, message);
            }
            else
            {
                Reply(player, message ?? "Could not give that kit.");
            }
        }

        private void CommandAdminReset(IPlayer player, string[] args)
        {
            if (args.Length < 3)
            {
                Reply(player, "Usage: /kitadmin reset <player> <kit id or name>");
                return;
            }

            IPlayer target = ResolvePlayer(args[1]);
            if (target == null)
            {
                Reply(player, "That player was not found.");
                return;
            }

            int consumed;
            KitDefinition kit = ResolveKitFromArgs(args, 2, out consumed);
            if (kit == null)
            {
                Reply(player, "Kit '" + string.Join(" ", args, 2, args.Length - 2) + "' was not found.");
                return;
            }

            Dictionary<string, ClaimRecord> records;
            if (data.Claims.TryGetValue(target.Id, out records) && records != null)
            {
                records.Remove(kit.Id);
                if (records.Count == 0)
                {
                    data.Claims.Remove(target.Id);
                }

                SaveData();
            }

            Reply(player, "Reset '" + kit.Name + "' claims for " + target.Name + ".");
        }

        /// <summary>
        /// Finds a kit from a command argument list, preferring the longest
        /// name that matches. This lets kit names contain spaces without
        /// forcing admins to switch to the slug id.
        /// </summary>
        private KitDefinition ResolveKitFromArgs(string[] args, int startIndex, out int consumed)
        {
            consumed = 0;
            if (args == null || startIndex < 0 || startIndex >= args.Length)
            {
                return null;
            }

            int max = Math.Min(8, args.Length - startIndex);
            for (int length = max; length >= 1; length--)
            {
                string candidate = string.Join(" ", args, startIndex, length);
                KitDefinition kit = FindKit(candidate);
                if (kit != null)
                {
                    consumed = length;
                    return kit;
                }
            }

            return null;
        }

        #endregion

        #region Protocol and helpers

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
                return string.Empty;
            }

            return value.Replace("%0A", "\n").Replace("%7C", "|").Replace("%25", "%");
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

        private static string FormatNumber(long value)
        {
            return value.ToString("N0", CultureInfo.InvariantCulture);
        }

        private static string FormatDuration(TimeSpan span)
        {
            if (span.TotalDays >= 1)
            {
                return ((int)span.TotalDays) + "d " + span.Hours + "h";
            }

            if (span.TotalHours >= 1)
            {
                return ((int)span.TotalHours) + "h " + span.Minutes + "m";
            }

            int minutes = (int)Math.Ceiling(span.TotalMinutes);
            return Math.Max(1, minutes) + "m";
        }

        #endregion

        #region Data classes

        private sealed class Session
        {
            public string Screen = "home";
            public string ReturnScreen = "home";
            public string SelectedKitId;
            public int SelectedItemIndex = -1;
            public int Page;
            public string InputMode;
            public string InputLabel = string.Empty;
            public string InputValue = string.Empty;
            public string Status = string.Empty;
        }

        private sealed class StoredData
        {
            public Dictionary<string, KitDefinition> Kits =
                new Dictionary<string, KitDefinition>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, Dictionary<string, ClaimRecord>> Claims =
                new Dictionary<string, Dictionary<string, ClaimRecord>>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class KitDefinition
        {
            public string Id = string.Empty;
            public string Name = string.Empty;
            public string Description = string.Empty;
            public string Permission = string.Empty;
            public bool Enabled = true;
            public bool Hidden = false;
            public int CooldownMinutes = 0;
            public int MaxUses = 0;
            public int MaxPerPlayer = 0;
            public int UseCount = 0;
            public long CreatedUtcTicks = 0;
            public string CreatedBy = string.Empty;
            public List<KitItem> Items = new List<KitItem>();
        }

        private sealed class KitItem
        {
            public string Item = string.Empty;
            public int Count = 1;
            public string Data = string.Empty;
        }

        private sealed class ClaimRecord
        {
            public int Count = 0;
            public long LastUtcTicks = 0;
        }

        private sealed class Configuration
        {
            public string ChatPrefix = "[Kits]";
            public string HookLabel = "Admin Kits";
            public string HookDescription = "Create, manage and collect server kits.";
            public string HookPermission = "adminkits.use";
            public bool GrantUseToDefaultGroup = true;
            public int DefaultCooldownMinutes = 0;
            public int DefaultMaxUses = 0;
            public int DefaultMaxPerPlayer = 0;
            public int DefaultItemQuality = 1;
            public bool ClearInventoryOnCapture = false;
            public bool CaptureEquipment = true;
            public bool LogClaims = true;
            public int MaxKits = 100;
            public int MaxItemsPerKit = 50;
        }

        #endregion
    }
}

