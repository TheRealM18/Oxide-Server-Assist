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
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("AdminSuite", "TheRealM18", "1.0.4")]
    [Description("A complete staff toolkit for 7 Days to Die: players, moderation, teleports, inventory tools, world control, audit logs, snapshots, rollback and server administration, all rendered through ServerAssist.")]
    public class AdminSuite : CovalencePlugin
    {
        #region Constants

        private const string PluginTitle = "Admin Suite";
        private const string HookId = "adminsuite";
        private const string ServerAssistPluginName = "ServerAssist";
        private const string DataFileName = "AdminSuite/data";
        private const string SnapshotFileName = "AdminSuite/snapshots";
        private const int UiRows = 10;
        private const int UiButtons = 12;

        private const string PermUse = "adminsuite.use";
        private const string PermAdmin = "adminsuite.admin";
        private const string PermPlayersView = "adminsuite.players.view";
        private const string PermTeleport = "adminsuite.players.teleport";
        private const string PermBring = "adminsuite.players.bring";
        private const string PermSpawnOnTop = "adminsuite.players.spawnontop";
        private const string PermSpectate = "adminsuite.players.spectate";
        private const string PermKick = "adminsuite.players.kick";
        private const string PermBan = "adminsuite.players.ban";
        private const string PermMute = "adminsuite.players.mute";
        private const string PermFreeze = "adminsuite.players.freeze";
        private const string PermKill = "adminsuite.players.kill";
        private const string PermHeal = "adminsuite.players.heal";
        private const string PermFeed = "adminsuite.players.feed";
        private const string PermGod = "adminsuite.players.god";
        private const string PermFly = "adminsuite.players.fly";
        private const string PermInventory = "adminsuite.players.inventory";
        private const string PermGive = "adminsuite.players.give";
        private const string PermClearInventory = "adminsuite.players.clearinventory";
        private const string PermTakeInventory = "adminsuite.players.takeinventory";
        private const string PermSnapshot = "adminsuite.players.snapshot";
        private const string PermRollback = "adminsuite.players.rollback";
        private const string PermWarn = "adminsuite.players.warn";
        private const string PermNotes = "adminsuite.players.notes";
        private const string PermBuffs = "adminsuite.players.buffs";
        private const string PermWorld = "adminsuite.world";
        private const string PermLogs = "adminsuite.logs";
        private const string PermSnapshots = "adminsuite.snapshots";
        private const string PermServer = "adminsuite.server";
        private const string PermReload = "adminsuite.reload";
        private const string PermWarps = "adminsuite.warps";
        private const string PermAdminList = "adminsuite.moderation.adminlist";
        private const string PermWhitelist = "adminsuite.moderation.whitelist";
        private const string PermKickAll = "adminsuite.moderation.kickall";
        private const string PermBanList = "adminsuite.moderation.banlist";

        private static readonly string[] AllPermissions =
        {
            PermUse, PermAdmin,
            PermPlayersView, PermTeleport, PermBring, PermSpawnOnTop, PermSpectate,
            PermKick, PermBan, PermMute, PermFreeze, PermKill, PermHeal, PermFeed, PermGod, PermFly,
            PermInventory, PermGive, PermClearInventory, PermTakeInventory,
            PermSnapshot, PermRollback, PermWarn, PermNotes, PermBuffs,
            PermWorld, PermLogs, PermSnapshots, PermServer, PermReload, PermWarps,
            PermAdminList, PermWhitelist, PermKickAll, PermBanList
        };

        #endregion

        #region Fields

        private Configuration config;
        private StoredData data;
        private SnapshotStore snapshots;
        private readonly Dictionary<string, Session> sessions =
            new Dictionary<string, Session>(StringComparer.OrdinalIgnoreCase);
        private readonly List<LogEntry> memoryLogs = new List<LogEntry>();
        private Timer freezeTimer;
        private DateTime startedUtc = DateTime.UtcNow;
        private bool hookRegistered;
        private bool dataDirty;

        #endregion

        #region Lifecycle

        private void Init()
        {
            for (int i = 0; i < AllPermissions.Length; i++)
            {
                permission.RegisterPermission(AllPermissions[i], this);
            }

            LoadConfig();
            LoadData();
            LoadSnapshots();
            LoadRecentLogFile();

            AddCovalenceCommand(new[] { "adminsuite", "as" }, nameof(CommandAdminSuite), PermUse);

            timer.Once(1f, EnsureDefaultPermissions);
            timer.Once(3f, RegisterServerAssistHook);
            timer.Once(6f, RegisterServerAssistHook);

            AddLog("server", "system", string.Empty, "AdminSuite " + Version + " loaded.");
        }

        private void OnServerInitialized()
        {
            EnsureDefaultPermissions();
            LoadConfigIfNeeded();
            LoadDataIfNeeded();
            LoadSnapshotsIfNeeded();
            ExpireEverything();
            StartFreezeTimer();
            RegisterServerAssistHook();
            AddLog("server", "system", string.Empty,
                "Server initialized. Online players: " + players.Connected.Count() + ".");
        }

        private void OnServerSave()
        {
            ExpireEverything();
            SaveAll();
        }

        private void Unload()
        {
            SaveAll();
            StopFreezeTimer();
            UnregisterServerAssistHook();
            sessions.Clear();
        }

        private void OnPluginUnloaded(Plugin plugin)
        {
            if (plugin != null &&
                string.Equals(plugin.Name, ServerAssistPluginName, StringComparison.OrdinalIgnoreCase))
            {
                hookRegistered = false;
            }
        }

        private void OnServerAssistReady()
        {
            RegisterServerAssistHook();
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

        private void LoadSnapshotsIfNeeded()
        {
            if (snapshots == null)
            {
                LoadSnapshots();
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
                Interface.Oxide.LogWarning("[AdminSuite] config could not be read: " + exception.Message);
                config = null;
            }

            if (config == null)
            {
                config = new Configuration();
            }

            NormalizeConfig();

            if (Config != null)
            {
                SaveConfig();
            }
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

            if (string.IsNullOrEmpty(config.ChatPrefix))
            {
                config.ChatPrefix = "[AdminSuite]";
            }

            config.ChatPrefix = config.ChatPrefix.Trim();
            if (config.ChatPrefix.Length == 0)
            {
                config.ChatPrefix = "[AdminSuite]";
            }

            if (string.IsNullOrEmpty(config.HookLabel))
            {
                config.HookLabel = PluginTitle;
            }

            config.HookLabel = config.HookLabel.Trim();
            if (config.HookLabel.Length == 0)
            {
                config.HookLabel = PluginTitle;
            }

            if (string.IsNullOrEmpty(config.HookDescription))
            {
                config.HookDescription = "The complete staff toolkit: players, moderation, teleports, inventory, world, logs and rollback.";
            }

            config.HookDescription = config.HookDescription.Trim();

            if (string.IsNullOrEmpty(config.HookPermission))
            {
                config.HookPermission = PermUse;
            }

            config.HookPermission = config.HookPermission.Trim();
            if (config.HookPermission.Length == 0)
            {
                config.HookPermission = PermUse;
            }

            config.MaxLogEntries = Clamp(config.MaxLogEntries, 50, 10000);
            config.SnapshotRetentionDays = Clamp(config.SnapshotRetentionDays, 1, 3650);
            config.MaxSnapshotsPerPlayer = Clamp(config.MaxSnapshotsPerPlayer, 1, 500);
            config.DefaultTimeoutMinutes = Clamp(config.DefaultTimeoutMinutes, 1, 525600);
            config.DefaultBanDays = Clamp(config.DefaultBanDays, 1, 36500);
            config.DefaultItemQuality = Clamp(config.DefaultItemQuality, 1, 6);
            config.DefaultSpawnCount = Clamp(config.DefaultSpawnCount, 1, 100);
            config.FreezeCheckSeconds = Clamp(config.FreezeCheckSeconds, 1, 60);
            config.DefaultFreezeMinutes = Clamp(config.DefaultFreezeMinutes, 0, 525600);
            config.MaxWarps = Clamp(config.MaxWarps, 1, 1000);
            config.JailX = ClampFloat(config.JailX, -100000f, 100000f);
            config.JailY = ClampFloat(config.JailY, -100000f, 100000f);
            config.JailZ = ClampFloat(config.JailZ, -100000f, 100000f);

            if (config.ProtectedPlayers == null)
            {
                config.ProtectedPlayers = new List<string>();
            }

            config.ProtectedPlayers = config.ProtectedPlayers
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p.Trim())
                .ToList();
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
                Interface.Oxide.LogWarning("[AdminSuite] data could not be read: " + exception.Message);
                data = null;
            }

            NormalizeData();
        }

        private void SaveData()
        {
            try
            {
                Interface.Oxide.DataFileSystem.WriteObject(DataFileName, data, true);
                dataDirty = false;
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogError("[AdminSuite] data could not be saved: " + exception);
            }
        }

        private void NormalizeData()
        {
            if (data == null)
            {
                data = new StoredData();
            }

            data.Warps = NormalizeWarpMap(data.Warps);
            data.Mutes = NormalizeMuteMap(data.Mutes);
            data.Freezes = NormalizeFreezeMap(data.Freezes);
            data.Notes = NormalizeNoteMap(data.Notes);
            data.Warnings = NormalizeWarningMap(data.Warnings);
            data.Homes = NormalizeHomeMap(data.Homes);
        }

        private static Dictionary<string, WarpEntry> NormalizeWarpMap(Dictionary<string, WarpEntry> source)
        {
            Dictionary<string, WarpEntry> result =
                new Dictionary<string, WarpEntry>(StringComparer.OrdinalIgnoreCase);
            if (source == null)
            {
                return result;
            }

            foreach (KeyValuePair<string, WarpEntry> pair in source)
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Value == null)
                {
                    continue;
                }

                WarpEntry entry = pair.Value;
                entry.Name = SanitizeName(pair.Key, 24);
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                result[entry.Name] = entry;
            }

            return result;
        }

        private static Dictionary<string, MuteEntry> NormalizeMuteMap(Dictionary<string, MuteEntry> source)
        {
            Dictionary<string, MuteEntry> result =
                new Dictionary<string, MuteEntry>(StringComparer.OrdinalIgnoreCase);
            if (source == null)
            {
                return result;
            }

            foreach (KeyValuePair<string, MuteEntry> pair in source)
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Value == null)
                {
                    continue;
                }

                result[pair.Key] = pair.Value;
            }

            return result;
        }

        private static Dictionary<string, FreezeEntry> NormalizeFreezeMap(Dictionary<string, FreezeEntry> source)
        {
            Dictionary<string, FreezeEntry> result =
                new Dictionary<string, FreezeEntry>(StringComparer.OrdinalIgnoreCase);
            if (source == null)
            {
                return result;
            }

            foreach (KeyValuePair<string, FreezeEntry> pair in source)
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Value == null)
                {
                    continue;
                }

                result[pair.Key] = pair.Value;
            }

            return result;
        }

        private static Dictionary<string, PlayerNote> NormalizeNoteMap(Dictionary<string, PlayerNote> source)
        {
            Dictionary<string, PlayerNote> result =
                new Dictionary<string, PlayerNote>(StringComparer.OrdinalIgnoreCase);
            if (source == null)
            {
                return result;
            }

            foreach (KeyValuePair<string, PlayerNote> pair in source)
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Value == null)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(pair.Value.Text))
                {
                    continue;
                }

                result[pair.Key] = pair.Value;
            }

            return result;
        }

        private static Dictionary<string, List<WarningEntry>> NormalizeWarningMap(
            Dictionary<string, List<WarningEntry>> source)
        {
            Dictionary<string, List<WarningEntry>> result =
                new Dictionary<string, List<WarningEntry>>(StringComparer.OrdinalIgnoreCase);
            if (source == null)
            {
                return result;
            }

            foreach (KeyValuePair<string, List<WarningEntry>> pair in source)
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Value == null)
                {
                    continue;
                }

                List<WarningEntry> list = pair.Value.Where(w => w != null).ToList();
                if (list.Count > 0)
                {
                    result[pair.Key] = list;
                }
            }

            return result;
        }

        private static Dictionary<string, HomeEntry> NormalizeHomeMap(Dictionary<string, HomeEntry> source)
        {
            Dictionary<string, HomeEntry> result =
                new Dictionary<string, HomeEntry>(StringComparer.OrdinalIgnoreCase);
            if (source == null)
            {
                return result;
            }

            foreach (KeyValuePair<string, HomeEntry> pair in source)
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Value == null)
                {
                    continue;
                }

                result[pair.Key] = pair.Value;
            }

            return result;
        }

        private void LoadSnapshots()
        {
            try
            {
                snapshots = Interface.Oxide.DataFileSystem.ReadObject<SnapshotStore>(SnapshotFileName);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[AdminSuite] snapshots could not be read: " + exception.Message);
                snapshots = null;
            }

            if (snapshots == null)
            {
                snapshots = new SnapshotStore();
            }

            if (snapshots.Snapshots == null)
            {
                snapshots.Snapshots = new List<Snapshot>();
            }

            TrimSnapshots();
        }

        private void SaveSnapshots()
        {
            try
            {
                Interface.Oxide.DataFileSystem.WriteObject(SnapshotFileName, snapshots, true);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogError("[AdminSuite] snapshots could not be saved: " + exception);
            }
        }

        private void SaveAll()
        {
            if (data != null)
            {
                SaveData();
            }

            if (snapshots != null)
            {
                SaveSnapshots();
            }
        }

        #endregion

        #region Permissions

        private void EnsureDefaultPermissions()
        {
            if (config == null)
            {
                LoadConfig();
            }

            string adminGroup = Interface.Oxide.Config.Options.DefaultGroups.Administrators;
            if (!string.IsNullOrEmpty(adminGroup))
            {
                GrantGroupIfMissing(adminGroup, PermAdmin);
                GrantGroupIfMissing(adminGroup, PermUse);
                for (int i = 0; i < AllPermissions.Length; i++)
                {
                    GrantGroupIfMissing(adminGroup, AllPermissions[i]);
                }
            }

            if (!config.GrantUseToDefaultGroup)
            {
                return;
            }

            string playerGroup = Interface.Oxide.Config.Options.DefaultGroups.Players;
            if (!string.IsNullOrEmpty(playerGroup))
            {
                GrantGroupIfMissing(playerGroup, PermUse);
            }
        }

        private void GrantGroupIfMissing(string group, string permissionName)
        {
            if (string.IsNullOrEmpty(group) || string.IsNullOrEmpty(permissionName))
            {
                return;
            }

            if (!permission.GroupExists(group))
            {
                return;
            }

            if (!permission.GroupHasPermission(group, permissionName))
            {
                permission.GrantGroupPermission(group, permissionName, this);
            }
        }

        private bool IsAdmin(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return false;
            }

            if (string.Equals(playerId, "server_console", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (permission.UserHasPermission(playerId, PermAdmin))
            {
                return true;
            }

            // Treat the game's own admins as admins too. Oxide usually puts them
            // in the administrators group, but this keeps the plugin usable even
            // if that group was renamed or the grant was removed.
            return SafeIsAdmin(ResolvePlayer(playerId));
        }

        private bool CanUse(string playerId)
        {
            return IsAdmin(playerId) || permission.UserHasPermission(playerId, PermUse);
        }

        private bool HasTool(string playerId, string permissionName)
        {
            if (IsAdmin(playerId))
            {
                return true;
            }

            if (string.IsNullOrEmpty(permissionName))
            {
                return true;
            }

            return permission.UserHasPermission(playerId, permissionName);
        }

        private bool RequireTool(IPlayer player, string permissionName, string actionName)
        {
            string playerId = player != null ? player.Id : "server_console";
            if (HasTool(playerId, permissionName))
            {
                return true;
            }

            Reply(player, "You do not have permission to " + actionName + " (" + permissionName + ").");
            return false;
        }

        private static bool IsServerConsole(IPlayer player)
        {
            return player == null ||
                   string.Equals(player.Id, "server_console", StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region ServerAssist integration

        private void RegisterServerAssistHook()
        {
            if (config == null)
            {
                LoadConfig();
            }

            if (data == null)
            {
                LoadData();
            }

            Plugin serverAssist = plugins.Find(ServerAssistPluginName);
            if (serverAssist == null)
            {
                return;
            }

            try
            {
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
                    serverAssist.Call("SetPanelIcon", HookId, "sprite:sa_admin_suite");
                }

                if (registered && !hookRegistered)
                {
                    hookRegistered = true;
                    Interface.Oxide.LogInfo("[AdminSuite] Registered ServerAssist GUI hook '" + HookId + "'.");
                }
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[AdminSuite] could not register ServerAssist hook: " + exception.Message);
            }
        }

        private void UnregisterServerAssistHook()
        {
            Plugin serverAssist = plugins.Find(ServerAssistPluginName);
            if (serverAssist == null)
            {
                return;
            }

            try
            {
                InvokePlugin(
                    serverAssist,
                    "UnregisterGuiHook",
                    new[] { typeof(string) },
                    new object[] { HookId });
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[AdminSuite] could not unregister ServerAssist hook: " + exception.Message);
            }
        }

        private static object InvokePlugin(Plugin plugin, string methodName, Type[] types, object[] args)
        {
            if (plugin == null)
            {
                return null;
            }

            MethodInfo method = plugin.GetType().GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Instance,
                null,
                types,
                null);

            return method != null ? method.Invoke(plugin, args) : null;
        }

        private bool OpenGui(IPlayer player)
        {
            if (player == null)
            {
                Interface.Oxide.LogInfo("[AdminSuite] The GUI can only be opened by an in-game player.");
                return false;
            }

            Plugin serverAssist = plugins.Find(ServerAssistPluginName);
            if (serverAssist == null)
            {
                Reply(player, "The ServerAssist plugin is not loaded on this server. Install ServerAssist.cs first.");
                return false;
            }

            try
            {
                object result = InvokePlugin(
                    serverAssist,
                    "OpenHookForPlayer",
                    new[] { typeof(string), typeof(string), typeof(string) },
                    new object[] { player.Id, HookId, null });

                bool opened = result is bool && (bool)result;
                if (!opened)
                {
                    Reply(player,
                        "The ServerAssist client mod did not respond. Install ServerAssist on this client and try again.");
                }

                return opened;
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[AdminSuite] could not open the GUI: " + exception.Message);
                Reply(player, "Could not open the Admin Suite window. Check the server log.");
                return false;
            }
        }

        private bool OpenGuiWithAction(IPlayer player, string action)
        {
            if (player == null)
            {
                Interface.Oxide.LogInfo("[AdminSuite] The GUI can only be opened by an in-game player.");
                return false;
            }

            Plugin serverAssist = plugins.Find(ServerAssistPluginName);
            if (serverAssist == null)
            {
                Reply(player, "The ServerAssist plugin is not loaded on this server. Install ServerAssist.cs first.");
                return false;
            }

            try
            {
                object result = InvokePlugin(
                    serverAssist,
                    "OpenHookForPlayerWithAction",
                    new[] { typeof(string), typeof(string), typeof(string) },
                    new object[] { player.Id, HookId, action });

                bool opened = result is bool && (bool)result;
                if (!opened)
                {
                    Reply(player,
                        "The ServerAssist client mod did not respond. Install ServerAssist on this client and try again.");
                }

                return opened;
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[AdminSuite] could not open the GUI with an action: " + exception.Message);
                Reply(player, "Could not open the Admin Suite window. Check the server log.");
                return false;
            }
        }

        #endregion

        #region Commands

        private void CommandAdminSuite(IPlayer player, string command, string[] args)
        {
            string actorId = player != null ? player.Id : "server_console";
            if (!CanUse(actorId))
            {
                Reply(player, "You do not have permission to use AdminSuite.");
                return;
            }

            if (args == null || args.Length == 0)
            {
                OpenGui(player);
                return;
            }

            string verb = args[0].ToLowerInvariant();
            switch (verb)
            {
                case "open":
                case "gui":
                case "menu":
                    OpenGui(player);
                    return;

                case "help":
                    CommandHelp(player);
                    return;

                case "list":
                case "players":
                case "online":
                    CommandList(player);
                    return;

                case "who":
                case "info":
                    CommandWho(player, args);
                    return;

                case "nearby":
                    CommandNearby(player, args);
                    return;

                case "find":
                case "search":
                    CommandFind(player, args);
                    return;

                case "tp":
                case "teleport":
                    CommandTeleport(player, args);
                    return;

                case "bring":
                case "summon":
                    CommandBring(player, args);
                    return;

                case "ontop":
                case "spawnontop":
                case "above":
                    CommandOnTop(player, args);
                    return;

                case "spectate":
                case "look":
                    CommandSpectatePlayer(player, args);
                    return;

                case "inv":
                case "inventory":
                case "openinventory":
                    CommandInventory(player, args);
                    return;

                case "jail":
                    CommandJail(player, args);
                    return;

                case "clearmutes":
                    CommandClearMutes(player);
                    return;

                case "clearwarnings":
                    CommandClearWarnings(player);
                    return;

                case "kick":
                    CommandKick(player, args);
                    return;

                case "kickall":
                    CommandKickAll(player, args);
                    return;

                case "ban":
                    CommandBan(player, args);
                    return;

                case "unban":
                    CommandUnban(player, args);
                    return;

                case "banlist":
                    CommandBanList(player);
                    return;

                case "adminlist":
                    CommandAdminList(player);
                    return;

                case "admin":
                    CommandAdminAction(player, args);
                    return;

                case "whitelist":
                    CommandWhitelistAction(player, args);
                    return;

                case "mute":
                    CommandMute(player, args);
                    return;

                case "unmute":
                    CommandUnmute(player, args);
                    return;

                case "timeout":
                    CommandTimeout(player, args);
                    return;

                case "freeze":
                    CommandFreeze(player, args);
                    return;

                case "unfreeze":
                    CommandUnfreeze(player, args);
                    return;

                case "god":
                case "godmode":
                    CommandGod(player, args);
                    return;

                case "fly":
                case "flymode":
                    CommandFly(player, args);
                    return;

                case "heal":
                    CommandHeal(player, args);
                    return;

                case "feed":
                    CommandFeed(player, args);
                    return;

                case "buff":
                    CommandBuff(player, args);
                    return;

                case "debuff":
                    CommandDebuff(player, args);
                    return;

                case "healall":
                    CommandHealAll(player);
                    return;

                case "feedall":
                    CommandFeedAll(player);
                    return;

                case "kill":
                    CommandKill(player, args);
                    return;

                case "give":
                    CommandGive(player, args);
                    return;

                case "creative":
                case "cm":
                case "creativemenu":
                    CommandCreative(player);
                    return;

                case "debug":
                case "debugmenu":
                    CommandDebugMenu(player);
                    return;

                case "spectator":
                case "spec":
                    CommandSpectator(player);
                    return;

                case "playerlist":
                case "plist":
                    CommandPlayerList(player);
                    return;

                case "tppoi":
                    CommandTppoi(player);
                    return;

                case "clear":
                    CommandClear(player, args);
                    return;

                case "take":
                    CommandTake(player, args);
                    return;

                case "repair":
                    CommandRepair(player, args);
                    return;

                case "warn":
                    CommandWarn(player, args);
                    return;

                case "note":
                case "notes":
                    CommandNote(player, args);
                    return;

                case "time":
                case "settime":
                    CommandTime(player, args);
                    return;

                case "weather":
                    CommandWeather(player, args);
                    return;

                case "airdrop":
                    CommandAirdrop(player, args);
                    return;

                case "horde":
                case "spawnhorde":
                    CommandHorde(player, args);
                    return;

                case "scouts":
                    CommandScouts(player, args);
                    return;

                case "spawn":
                case "spawnentity":
                    CommandSpawn(player, args);
                    return;

                case "save":
                case "saveworld":
                    CommandSave(player);
                    return;

                case "shutdown":
                    CommandShutdown(player, args);
                    return;

                case "say":
                case "announce":
                    CommandSay(player, args);
                    return;

                case "warps":
                    CommandWarps(player);
                    return;

                case "warp":
                    CommandWarp(player, args);
                    return;

                case "setwarp":
                    CommandSetWarp(player, args);
                    return;

                case "delwarp":
                case "removewarp":
                    CommandDelWarp(player, args);
                    return;

                case "snapshot":
                    CommandSnapshot(player, args);
                    return;

                case "snapshots":
                    CommandSnapshots(player, args);
                    return;

                case "rollback":
                case "restore":
                    CommandRollback(player, args);
                    return;

                case "logs":
                case "log":
                    CommandLogs(player, args);
                    return;

                case "reload":
                    CommandReload(player);
                    return;

                case "killall":
                case "purge":
                    CommandKillAll(player, args);
                    return;

                default:
                    Reply(player, "Unknown subcommand '" + verb + "'. Use /as help.");
                    return;
            }
        }

        #endregion

        #region Hooks

        private void OnUserConnected(IPlayer player)
        {
            if (player == null)
            {
                return;
            }

            AddLog("join", player.Name, string.Empty,
                "Player connected from " + (string.IsNullOrEmpty(player.Address) ? "unknown address" : player.Address) + ".");

            if (config.NotifyStaffOnJoin && !IsAdmin(player.Id))
            {
                NotifyStaff(player.Name + " joined the server (" + player.Id + ").", player.Id);
            }

            MuteEntry mute = GetActiveMute(player.Id);
            if (mute != null)
            {
                Reply(player, "You are muted. Reason: " + (string.IsNullOrEmpty(mute.Reason) ? "none" : mute.Reason) +
                             " (" + FormatRemaining(mute.UntilUtcTicks) + ")");
            }

            FreezeEntry freeze = GetActiveFreeze(player.Id);
            if (freeze != null)
            {
                Reply(player, "You are frozen and will be returned to your position until an admin releases you.");
            }
        }

        private void OnUserDisconnected(IPlayer player, string reason)
        {
            if (player == null)
            {
                return;
            }

            sessions.Remove(player.Id);
            AddLog("leave", player.Name, string.Empty,
                "Player disconnected: " + (string.IsNullOrEmpty(reason) ? "no reason" : reason) + ".");

            if (dataDirty)
            {
                SaveData();
            }
        }

        private object OnUserChat(IPlayer player, string message)
        {
            if (player == null)
            {
                return null;
            }

            if (config.LogChat)
            {
                AddLog("chat", player.Name, string.Empty, message);
            }

            MuteEntry mute = GetActiveMute(player.Id);
            if (mute == null)
            {
                return null;
            }

            Reply(player, "You are muted. Reason: " + (string.IsNullOrEmpty(mute.Reason) ? "none" : mute.Reason) +
                         " (" + FormatRemaining(mute.UntilUtcTicks) + ")");
            return false;
        }

        private void OnEntityDeath(Entity entity, DamageResponse response)
        {
            if (!config.LogDeaths)
            {
                return;
            }

            EntityPlayer player = entity as EntityPlayer;
            if (player == null)
            {
                return;
            }

            Vector3 position = player.GetPosition();
            string name = player.PlayerDisplayName;
            if (string.IsNullOrEmpty(name))
            {
                name = "Unknown player";
            }

            AddLog("death", name, string.Empty,
                "Died at " + FormatVector(position) + ".");
        }

        private object OnServerCommand(string command, string[] args)
        {
            if (config.LogCommands && !string.IsNullOrEmpty(command))
            {
                string line = command;
                if (args != null && args.Length > 0)
                {
                    line += " " + string.Join(" ", args);
                }

                AddLog("command", "server", string.Empty, line);
            }

            return null;
        }

        #endregion

        #region GUI request entry point

        /// <summary>
        /// Entry point called by ServerAssist's GUI hook system.
        /// Payloads: a|open, a|&lt;action&gt; and t|&lt;encoded text&gt;.
        /// </summary>
        public string HandleGuiRequest(string playerId, string payload)
        {
            try
            {
                if (config == null)
                {
                    LoadConfig();
                }

                if (data == null)
                {
                    LoadData();
                }

                if (snapshots == null)
                {
                    LoadSnapshots();
                }

                if (string.IsNullOrEmpty(playerId))
                {
                    return ErrorView("Could not determine your player id.");
                }

                if (!CanUse(playerId))
                {
                    return ErrorView("You do not have permission to use AdminSuite.");
                }

                Session session = GetSession(playerId);
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

                return ErrorView("Unknown GUI request.");
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogException("[AdminSuite] GUI request failed", exception);
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

        private static string ErrorView(string message)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("v1\n");
            builder.Append("title=").Append(Encode(PluginTitle)).Append('\n');
            builder.Append("subtitle=").Append(Encode("AdminSuite")).Append('\n');
            builder.Append("status=").Append(Encode(message)).Append('\n');
            builder.Append("tab=home\n");
            builder.Append("input=||0|\n");
            builder.Append("row=0||").Append(Encode("No data")).Append('|')
                .Append(Encode(message)).Append("|0|0\n");
            builder.Append("btn=0|refresh|").Append(Encode("Retry")).Append("|1|\n");
            builder.Append("btn=1|").Append(Encode("nav|home")).Append('|')
                .Append(Encode("Home")).Append("|1|\n");
            return builder.ToString();
        }

        #endregion

        #region GUI actions

        private string HandleAction(string playerId, Session session, string action)
        {
            string[] parts = action.Split('|');
            string verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
            session.Status = string.Empty;
            ClearInput(session);

            switch (verb)
            {
                case "open":
                    ResetSession(session);
                    break;

                case "home":
                    session.History.Clear();
                    session.Screen = "home";
                    session.Page = 0;
                    ClearInput(session);
                    break;

                case "refresh":
                    session.Status = "Refreshed.";
                    break;

                case "back":
                    GoBack(session);
                    break;

                case "nav":
                    if (parts.Length > 1)
                    {
                        Navigate(playerId, session, parts[1]);
                    }
                    break;

                case "page":
                    if (parts.Length > 1)
                    {
                        int delta = string.Equals(parts[1], "next", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
                        session.Page += delta;
                    }
                    break;

                case "player":
                    if (parts.Length > 1)
                    {
                        SelectPlayer(playerId, session, parts[1]);
                    }
                    break;

                case "inventory_player":
                    if (parts.Length > 1)
                    {
                        IPlayer inventoryTarget = ResolvePlayer(parts[1]);
                        if (inventoryTarget == null)
                        {
                            session.Status = "Player '" + parts[1] + "' was not found.";
                            break;
                        }

                        if (!HasTool(playerId, PermInventory))
                        {
                            session.Status = "You need " + PermInventory + ".";
                            break;
                        }

                        session.SelectedPlayerId = inventoryTarget.Id;
                        session.SelectedPlayerName = inventoryTarget.Name;
                        session.InventorySection = "backpack";
                        session.InventoryIndex = -1;
                        session.Screen = "inventory";
                        session.Page = 0;
                        session.Status = string.Empty;
                        session.History.Clear();
                        session.History.Add("players");
                    }
                    break;

                case "input":
                    if (parts.Length > 2)
                    {
                        PromptInput(session, parts[1], parts[2], parts.Length > 3 ? parts[3] : string.Empty);
                    }
                    break;

                case "act":
                    if (parts.Length > 1)
                    {
                        HandleAct(playerId, session, parts);
                    }
                    break;

                case "kick":
                    if (parts.Length > 1)
                    {
                        session.SelectedPlayerId = parts[1];
                        PromptInput(session, "kick_reason", "Kick reason (blank = Kicked by staff)", string.Empty);
                    }
                    break;

                case "ban":
                    if (parts.Length > 1)
                    {
                        session.PendingData = parts[1];
                        PromptInput(session, "ban_reason", "Ban reason (blank = Banned by staff)", string.Empty);
                    }
                    break;

                case "mute":
                    if (parts.Length > 1)
                    {
                        session.PendingData = parts[1];
                        PromptInput(session, "mute_reason", "Mute reason (blank = Muted by staff)", string.Empty);
                    }
                    break;

                case "timeout":
                    if (parts.Length > 1)
                    {
                        session.PendingData = parts[1];
                        PromptInput(session, "timeout_reason", "Timeout reason (blank = Timeout by staff)", string.Empty);
                    }
                    break;

                case "warn":
                    if (parts.Length > 1)
                    {
                        session.SelectedPlayerId = parts[1];
                        PromptInput(session, "warn", "Warning message", string.Empty);
                    }
                    break;

                case "note":
                    if (parts.Length > 1)
                    {
                        session.SelectedPlayerId = parts[1];
                        PromptInput(session, "note", "Staff note (blank clears the note)", GetNoteText(parts[1]));
                    }
                    break;

                case "give":
                    if (parts.Length > 1)
                    {
                        session.SelectedPlayerId = parts[1];
                        PromptInput(session, "give_item", "Item [amount] [quality]", string.Empty);
                    }
                    break;

                case "unban":
                    if (parts.Length > 1)
                    {
                        UnbanByIndex(playerId, session, parts[1]);
                    }
                    break;

                case "warp":
                    if (parts.Length > 1)
                    {
                        TeleportToWarp(playerId, session, parts[1]);
                    }
                    break;

                case "close":
                    session.Status = string.Empty;
                    break;

                case "cancel":
                    session.Status = "Input cancelled.";
                    break;

                case "unmute":
                    if (parts.Length > 1)
                    {
                        UnmuteById(playerId, session, parts[1]);
                    }
                    break;

                case "unfreeze":
                    if (parts.Length > 1)
                    {
                        RemoveFreezeById(playerId, session, parts[1]);
                    }
                    break;

                case "give_item_name":
                    if (parts.Length > 1)
                    {
                        GiveItemToSelectedOrSelf(playerId, session, parts[1], 1, config.DefaultItemQuality);
                    }
                    break;

                case "invsection":
                    if (parts.Length > 1)
                    {
                        session.InventorySection = parts[1];
                        session.Screen = "inventory";
                        session.Page = 0;
                        ClearInput(session);
                    }
                    break;

                case "inv_item":
                    if (parts.Length > 2)
                    {
                        OpenInventoryItem(playerId, session, parts[1], ParseInt(parts[2], 0));
                    }
                    break;

                case "snap_view":
                    if (parts.Length > 1)
                    {
                        session.SelectedSnapshotId = parts[1];
                        session.Screen = "snap_view";
                        session.Page = 0;
                        ClearInput(session);
                    }
                    break;

                case "snap_filter":
                    if (parts.Length > 1)
                    {
                        session.SnapshotFilterPlayerId =
                            string.Equals(parts[1], "all", StringComparison.OrdinalIgnoreCase) ? null : parts[1];
                        session.Screen = "snapshots";
                        session.Page = 0;
                        ClearInput(session);
                    }
                    break;

                case "logfilter":
                    if (parts.Length > 1)
                    {
                        session.LogFilter = parts[1];
                        session.Page = 0;
                    }
                    break;

                case "log_detail":
                    if (parts.Length > 1)
                    {
                        session.SelectedLogIndex = ParseInt(parts[1], 0);
                        session.Screen = "log_detail";
                        session.Page = 0;
                        ClearInput(session);
                    }
                    break;

                case "confirm_yes":
                    ExecutePending(playerId, session);
                    break;

                case "confirm_no":
                    CancelPending(session);
                    break;

                default:
                    session.Status = "Unknown action: " + verb;
                    break;
            }

            return Render(playerId, session);
        }

        private void ResetSession(Session session)
        {
            session.Screen = "home";
            session.Page = 0;
            session.SelectedPlayerId = null;
            session.SelectedPlayerName = null;
            session.SelectedSnapshotId = null;
            session.SelectedLogIndex = -1;
            session.InventorySection = "backpack";
            session.InventoryIndex = -1;
            session.SnapshotFilterPlayerId = null;
            session.LogFilter = "all";
            session.Search = string.Empty;
            session.Status = string.Empty;
            session.PendingAction = null;
            session.PendingData = null;
            session.PendingReturnScreen = "home";
            session.PendingTitle = string.Empty;
            session.PendingMessage = string.Empty;
            session.History.Clear();
            ClearInput(session);
        }

        private void ClearInput(Session session)
        {
            session.InputAction = null;
            session.InputLabel = string.Empty;
            session.InputValue = string.Empty;
        }

        private void PromptInput(Session session, string action, string label, string value)
        {
            session.InputAction = action;
            session.InputLabel = label;
            session.InputValue = value ?? string.Empty;
        }

        private void Navigate(string playerId, Session session, string screen)
        {
            if (string.IsNullOrEmpty(screen))
            {
                return;
            }

            if (!CanOpenScreen(playerId, screen))
            {
                session.Status = "You do not have permission to open that screen.";
                return;
            }

            if (!string.Equals(session.Screen, screen, StringComparison.OrdinalIgnoreCase))
            {
                session.History.Add(session.Screen);
            }

            session.Screen = screen;
            session.Page = 0;
            session.Status = string.Empty;
            ClearInput(session);

            if (string.Equals(screen, "inventory", StringComparison.OrdinalIgnoreCase))
            {
                session.InventorySection = "backpack";
                session.InventoryIndex = -1;
            }
        }

        private bool CanOpenScreen(string playerId, string screen)
        {
            switch (screen)
            {
                case "players":
                case "player":
                case "player_more":
                case "player_staff":
                    return HasTool(playerId, PermPlayersView);
                case "buffs":
                    return HasTool(playerId, PermBuffs) || HasTool(playerId, PermFly);
                case "ban":
                    return HasTool(playerId, PermBan);
                case "mute":
                    return HasTool(playerId, PermMute);
                case "timeout":
                    return HasTool(playerId, PermMute) && HasTool(playerId, PermFreeze);
                case "moderation":
                    return HasTool(playerId, PermKick) || HasTool(playerId, PermMute) ||
                           HasTool(playerId, PermFreeze) || HasTool(playerId, PermWarn) ||
                           HasTool(playerId, PermBan) || HasTool(playerId, PermBanList) ||
                           HasTool(playerId, PermAdminList) || HasTool(playerId, PermWhitelist);
                case "mutes":
                    return HasTool(playerId, PermMute);
                case "freezes":
                    return HasTool(playerId, PermFreeze);
                case "warnings":
                    return HasTool(playerId, PermWarn);
                case "banlist":
                    return HasTool(playerId, PermBanList) || HasTool(playerId, PermBan);
                case "adminlist":
                    return HasTool(playerId, PermAdminList);
                case "whitelist":
                    return HasTool(playerId, PermWhitelist);
                case "teleport":
                case "warps":
                    return HasTool(playerId, PermTeleport) || HasTool(playerId, PermWarps);
                case "items":
                case "item_results":
                    return HasTool(playerId, PermGive) || HasTool(playerId, PermInventory);
                case "inventory":
                case "inventory_item":
                    return HasTool(playerId, PermInventory);
                case "world":
                    return HasTool(playerId, PermWorld);
                case "logs":
                case "log_detail":
                    return HasTool(playerId, PermLogs);
                case "snapshots":
                case "snap_view":
                    return HasTool(playerId, PermSnapshots) || HasTool(playerId, PermSnapshot) ||
                           HasTool(playerId, PermRollback);
                case "server":
                    return HasTool(playerId, PermServer);
                case "help":
                case "confirm":
                case "home":
                    return true;
                default:
                    return true;
            }
        }

        private void GoBack(Session session)
        {
            ClearInput(session);
            session.Status = string.Empty;
            session.SelectedLogIndex = -1;
            session.InventoryIndex = -1;

            if (session.History.Count == 0)
            {
                session.Screen = "home";
                session.Page = 0;
                return;
            }

            int last = session.History.Count - 1;
            session.Screen = session.History[last];
            session.History.RemoveAt(last);
            session.Page = 0;
        }

        private void SelectPlayer(string playerId, Session session, string targetId)
        {
            if (!HasTool(playerId, PermPlayersView))
            {
                session.Status = "You need " + PermPlayersView + ".";
                return;
            }

            IPlayer target = ResolvePlayer(targetId);
            if (target == null)
            {
                session.Status = "Player '" + targetId + "' was not found.";
                return;
            }

            if (!string.Equals(session.Screen, "player", StringComparison.OrdinalIgnoreCase))
            {
                session.History.Add(session.Screen);
            }

            session.SelectedPlayerId = target.Id;
            session.SelectedPlayerName = target.Name;
            session.Screen = "player";
            session.Page = 0;
            session.Status = string.Empty;
            ClearInput(session);
        }

        #endregion

        #region Confirmation

        private void PromptConfirm(Session session, string action, string title, string message, string data = null)
        {
            session.PendingAction = action;
            session.PendingData = data;
            session.PendingTitle = title;
            session.PendingMessage = message;
            session.PendingReturnScreen = session.Screen;
            session.Screen = "confirm";
            session.Page = 0;
            ClearInput(session);
        }

        private void ExecutePending(string playerId, Session session)
        {
            string action = session.PendingAction;
            string data = session.PendingData;
            string returnScreen = session.PendingReturnScreen;

            session.PendingAction = null;
            session.PendingData = null;
            session.PendingTitle = string.Empty;
            session.PendingMessage = string.Empty;
            session.Screen = string.IsNullOrEmpty(returnScreen) ? "home" : returnScreen;
            session.Status = string.Empty;

            if (string.IsNullOrEmpty(action))
            {
                session.Status = "Nothing was pending.";
                return;
            }

            switch (action)
            {
                case "kick":
                    KickSelected(playerId, session);
                    break;
                case "kill":
                    KillSelected(playerId, session);
                    break;
                case "clear_inventory":
                    ClearSelectedInventory(playerId, session);
                    break;
                case "take_inventory":
                    TakeSelectedInventory(playerId, session);
                    break;
                case "repair_inventory":
                    RepairSelectedInventory(playerId, session);
                    break;
                case "take_all_inventory":
                    TakeAllSelectedInventory(playerId, session);
                    break;
                case "clear_all_inventory":
                    ClearAllSelectedInventory(playerId, session);
                    break;
                case "repair_all_inventory":
                    RepairAllSelectedInventory(playerId, session);
                    break;
                case "inv_delete":
                    DeleteInventoryItem(playerId, session, data);
                    break;
                case "kickall":
                    KickAll(playerId, session);
                    break;
                case "shutdown":
                    ShutdownServer(playerId, session);
                    break;
                case "regionreset":
                    RegionReset(playerId, session);
                    break;
                case "clearmutes":
                    ClearMutes(playerId, session);
                    break;
                case "clearwarnings":
                    ClearWarnings(playerId, session);
                    break;
                case "delete_snapshot":
                    DeleteSnapshotById(playerId, session, data);
                    break;
                case "restore_snapshot":
                    RestoreSnapshotById(playerId, session, data);
                    break;
                case "jail":
                    JailSelected(playerId, session);
                    break;
                case "snap_cleanup":
                    CleanupSnapshots(playerId, session);
                    break;
                case "chunkreset":
                    ChunkResetNearPlayer(playerId, session);
                    break;
                default:
                    session.Status = "Unknown pending action: " + action;
                    break;
            }
        }

        private void CancelPending(Session session)
        {
            string returnScreen = session.PendingReturnScreen;
            session.PendingAction = null;
            session.PendingData = null;
            session.PendingTitle = string.Empty;
            session.PendingMessage = string.Empty;
            session.Screen = string.IsNullOrEmpty(returnScreen) ? "home" : returnScreen;
            session.Status = "Cancelled.";
        }

        #endregion

        #region Act handler

        private void HandleAct(string playerId, Session session, string[] parts)
        {
            string action = parts.Length > 1 ? parts[1].ToLowerInvariant() : string.Empty;

            switch (action)
            {
                case "tp_to":
                    TeleportToSelected(playerId, session);
                    break;
                case "bring":
                    BringSelected(playerId, session);
                    break;
                case "spawn_on_top":
                    SpawnOnTopSelected(playerId, session);
                    break;
                case "spectate":
                    SpectateSelected(playerId, session);
                    break;
                case "stop_spectate":
                    StopSpectating(playerId, session);
                    break;
                case "heal":
                    HealSelected(playerId, session);
                    break;
                case "feed":
                    FeedSelected(playerId, session);
                    break;
                case "healall":
                    HealAll(playerId, session);
                    break;
                case "feedall":
                    FeedAll(playerId, session);
                    break;
                case "god":
                    ToggleGodSelected(playerId, session);
                    break;
                case "fly":
                case "toggle_fly":
                    ToggleFlySelected(playerId, session);
                    break;
                case "kill":
                    PromptConfirm(session, "kill", "Kill Player",
                        "Kill " + GetSelectedName(session) + "? They will drop their items according to the server rules.");
                    break;
                case "freeze":
                case "toggle_freeze":
                    ToggleFreezeSelected(playerId, session);
                    break;
                case "unfreeze":
                    UnfreezeSelected(playerId, session);
                    break;
                case "snapshot":
                    CaptureSelectedSnapshot(playerId, session, "Manual snapshot");
                    break;
                case "clear_inventory":
                    PromptConfirm(session, "clear_inventory", "Clear Inventory",
                        "Clear every item from " + GetSelectedName(session) + "'s toolbelt, backpack and equipment? A snapshot is taken first.");
                    break;
                case "take_inventory":
                    PromptConfirm(session, "take_inventory", "Take All Items",
                        "Move every item from " + GetSelectedName(session) + " to your inventory? A snapshot is taken first.");
                    break;
                case "repair_inventory":
                    RepairSelectedInventory(playerId, session);
                    break;
                case "take_all_inventory":
                    PromptConfirm(session, "take_all_inventory", "Take All Items",
                        "Move every item from " + GetSelectedName(session) + " to your inventory? A snapshot is taken first.");
                    break;
                case "clear_all_inventory":
                    PromptConfirm(session, "clear_all_inventory", "Clear Inventory",
                        "Clear every item from " + GetSelectedName(session) + "'s toolbelt, backpack and equipment? A snapshot is taken first.");
                    break;
                case "repair_all_inventory":
                    RepairAllSelectedInventory(playerId, session);
                    break;
                case "inv_take":
                    if (parts.Length > 3)
                    {
                        TakeInventoryItem(playerId, session, parts[2], ParseInt(parts[3], 0));
                    }
                    break;
                case "inv_repair":
                    if (parts.Length > 3)
                    {
                        RepairInventoryItem(playerId, session, parts[2], ParseInt(parts[3], 0));
                    }
                    break;
                case "inv_delete":
                    if (parts.Length > 3)
                    {
                        PromptConfirm(session, "inv_delete", "Delete Item",
                            "Delete this item from " + GetSelectedName(session) + "'s inventory? A snapshot is taken first.",
                            parts[2] + "|" + parts[3]);
                    }
                    break;
                case "open_inventory":
                    Navigate(playerId, session, "inventory");
                    break;
                case "kickall":
                    PromptConfirm(session, "kickall", "Kick All Players",
                        "Kick every connected player? They can rejoin immediately.");
                    break;
                case "clearmutes":
                    PromptConfirm(session, "clearmutes", "Clear Mutes",
                        "Remove every active mute?");
                    break;
                case "clearwarnings":
                    PromptConfirm(session, "clearwarnings", "Clear Warnings",
                        "Remove every stored warning?");
                    break;
                case "time_day":
                    SetTime(playerId, session, "day");
                    break;
                case "time_night":
                    SetTime(playerId, session, "night");
                    break;
                case "weather_clear":
                    SetWeather(playerId, session, "clear");
                    break;
                case "weather_rain":
                    SetWeather(playerId, session, "rain");
                    break;
                case "airdrop":
                    SpawnAirdrop(playerId, session);
                    break;
                case "horde":
                    SpawnHorde(playerId, session);
                    break;
                case "scouts":
                    SpawnScouts(playerId, session);
                    break;
                case "save":
                    SaveWorld(playerId, session);
                    break;
                case "regionreset":
                    PromptConfirm(session, "regionreset", "Region Reset",
                        "Run a full region reset of unprotected chunks? This permanently deletes saved chunk data and cannot be undone.");
                    break;
                case "chunkreset":
                    PromptConfirm(session, "chunkreset", "Chunk Reset Near You",
                        "Reset the chunk you are standing in and the surrounding eight chunks? This can cause a hitch.");
                    break;
                case "snap_cleanup":
                    PromptConfirm(session, "snap_cleanup", "Delete Old Snapshots",
                        "Delete snapshots older than the retention period and trim each player to the configured limit?");
                    break;
                case "debugmenu":
                    SendClientCommandToActor(playerId, session, "debugmenu");
                    break;
                case "creative_menu":
                    ToggleCreativeMenu(playerId, session);
                    break;
                case "tppoi":
                    SendClientCommandToActor(playerId, session, "tppoi");
                    break;
                case "open_player_list":
                    SendClientCommandToActor(playerId, session, "xui open players");
                    break;
                case "spectator":
                    SendClientCommandToActor(playerId, session, "spectator");
                    break;
                case "sethome":
                    SetHome(playerId, session);
                    break;
                case "home":
                    GoHome(playerId, session);
                    break;
                case "jail":
                    PromptConfirm(session, "jail", "Jail Player",
                        "Teleport " + GetSelectedName(session) + " to the configured jail and freeze them?");
                    break;
                case "shutdown":
                    PromptConfirm(session, "shutdown", "Shut Down Server",
                        "Shut down the server now? All players will be disconnected.");
                    break;
                case "reload":
                    ReloadAll(playerId, session);
                    break;
                case "open_gui":
                    OpenGui(players.FindPlayerById(playerId));
                    break;
                case "log_clear":
                    ClearLogs(playerId, session);
                    break;
                case "snap_delete":
                    if (parts.Length > 2)
                    {
                        PromptConfirm(session, "delete_snapshot", "Delete Snapshot",
                            "Delete this snapshot permanently?", parts[2]);
                    }
                    break;
                case "snap_restore":
                    if (parts.Length > 2)
                    {
                        PromptConfirm(session, "restore_snapshot", "Restore Snapshot",
                            "Restore this snapshot? The player's current state is captured first.", parts[2]);
                    }
                    break;
                default:
                    session.Status = "Unknown action: " + action;
                    break;
            }
        }

        #endregion

        #region Text submissions

        private string HandleText(string playerId, Session session, string text)
        {
            string action = session.InputAction;
            ClearInput(session);

            if (string.IsNullOrEmpty(action))
            {
                session.Status = "Nothing is waiting for input.";
                return Render(playerId, session);
            }

            text = text == null ? string.Empty : text.Trim();

            switch (action)
            {
                case "search_player":
                    HandleSearchPlayer(playerId, session, text);
                    break;
                case "tp_coords":
                    TeleportSelfToCoords(playerId, session, text);
                    break;
                case "tp_target_coords":
                    TeleportSelectedToCoords(playerId, session, text);
                    break;
                case "tp_target_player":
                    TeleportSelectedToPlayer(playerId, session, text);
                    break;
                case "give_item":
                    GiveItemToSelected(playerId, session, text);
                    break;
                case "give_self":
                    GiveItemToSelf(playerId, session, text);
                    break;
                case "item_search":
                    session.Search = text;
                    session.Screen = "item_results";
                    session.Page = 0;
                    session.Status = string.IsNullOrEmpty(text) ? "Showing all items." : "Search: " + text;
                    break;
                case "spawn_entity":
                    SpawnEntity(playerId, session, text);
                    break;
                case "set_time":
                    SetTime(playerId, session, text);
                    break;
                case "weather_storm":
                    WeatherStorm(playerId, session, text);
                    break;
                case "setpref":
                    SetGamePref(playerId, session, text);
                    break;
                case "announce":
                    Announce(playerId, session, text);
                    break;
                case "restart_warning":
                    RestartWarning(playerId, session, text);
                    break;
                case "set_warp":
                    SetWarp(playerId, session, text);
                    break;
                case "del_warp":
                    DeleteWarp(playerId, session, text);
                    break;
                case "kick_reason":
                    session.PendingData = text;
                    KickSelected(playerId, session);
                    break;
                case "ban_reason":
                    session.PendingData = session.PendingData + "\n" + text;
                    BanSelected(playerId, session);
                    break;
                case "mute_reason":
                    session.PendingData = session.PendingData + "\n" + text;
                    MuteSelected(playerId, session);
                    break;
                case "timeout_reason":
                    session.PendingData = session.PendingData + "\n" + text;
                    TimeoutSelected(playerId, session);
                    break;
                case "ban_custom":
                    BanCustom(playerId, session, text);
                    break;
                case "mute_custom":
                    MuteCustom(playerId, session, text);
                    break;
                case "timeout_custom":
                    TimeoutCustom(playerId, session, text);
                    break;
                case "warn":
                    WarnSelected(playerId, session, text);
                    break;
                case "note":
                    NoteSelected(playerId, session, text);
                    break;
                case "buff_add":
                    ApplyBuff(playerId, session, text);
                    break;
                case "buff_remove":
                    RemoveBuff(playerId, session, text);
                    break;
                case "set_health":
                    SetHealth(playerId, session, text);
                    break;
                case "set_food":
                    SetFoodAndWater(playerId, session, text);
                    break;
                case "snapshot_player":
                    SnapshotPlayerByName(playerId, session, text);
                    break;
                case "whitelist_add":
                    WhitelistAdd(playerId, session, text);
                    break;
                case "whitelist_remove":
                    WhitelistRemove(playerId, session, text);
                    break;
                case "admin_add":
                    AdminAdd(playerId, session, text);
                    break;
                case "admin_remove":
                    AdminRemove(playerId, session, text);
                    break;
                case "warn_any":
                    WarnAny(playerId, session, text);
                    break;
                default:
                    session.Status = "Unknown input action: " + action;
                    break;
            }

            return Render(playerId, session);
        }

        private void HandleSearchPlayer(string playerId, Session session, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                session.Status = "Enter a player name or id.";
                return;
            }

            IPlayer target = ResolvePlayer(text);
            if (target == null)
            {
                session.Status = "No player matched '" + text + "'.";
                return;
            }

            SelectPlayer(playerId, session, target.Id);
            session.Status = "Opened " + target.Name + ".";
        }

        #endregion

        #region Rendering

        private sealed class RowInfo
        {
            public string Action = string.Empty;
            public string Label = string.Empty;
            public string Sub = string.Empty;
            public bool Enabled = true;
            public bool Selected;
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
        }

        private string Render(string playerId, Session session)
        {
            if (!CanOpenScreen(playerId, session.Screen))
            {
                session.Screen = "home";
                session.Status = "You do not have permission to view that screen.";
                ClearInput(session);
            }

            List<RowInfo> rows = new List<RowInfo>();
            List<ButtonInfo> buttons = new List<ButtonInfo>();
            string subtitle = string.Empty;

            switch (session.Screen)
            {
                case "players":
                    RenderPlayers(playerId, session, rows, buttons, out subtitle);
                    break;
                case "player":
                    RenderPlayer(playerId, session, rows, buttons, out subtitle);
                    break;
                case "player_more":
                    RenderPlayerMore(playerId, session, rows, buttons, out subtitle);
                    break;
                case "ban":
                    RenderBan(playerId, session, rows, buttons, out subtitle);
                    break;
                case "mute":
                    RenderMute(playerId, session, rows, buttons, out subtitle);
                    break;
                case "timeout":
                    RenderTimeout(playerId, session, rows, buttons, out subtitle);
                    break;
                case "player_staff":
                    RenderPlayerStaff(playerId, session, rows, buttons, out subtitle);
                    break;
                case "buffs":
                    RenderBuffs(playerId, session, rows, buttons, out subtitle);
                    break;
                case "moderation":
                    RenderModeration(playerId, session, rows, buttons, out subtitle);
                    break;
                case "mutes":
                    RenderMutes(playerId, session, rows, buttons, out subtitle);
                    break;
                case "freezes":
                    RenderFreezes(playerId, session, rows, buttons, out subtitle);
                    break;
                case "warnings":
                    RenderWarnings(playerId, session, rows, buttons, out subtitle);
                    break;
                case "banlist":
                    RenderBanList(playerId, session, rows, buttons, out subtitle);
                    break;
                case "adminlist":
                    RenderAdminList(playerId, session, rows, buttons, out subtitle);
                    break;
                case "whitelist":
                    RenderWhitelist(playerId, session, rows, buttons, out subtitle);
                    break;
                case "teleport":
                    RenderTeleport(playerId, session, rows, buttons, out subtitle);
                    break;
                case "warps":
                    RenderWarps(playerId, session, rows, buttons, out subtitle);
                    break;
                case "items":
                    RenderItems(playerId, session, rows, buttons, out subtitle);
                    break;
                case "item_results":
                    RenderItemResults(playerId, session, rows, buttons, out subtitle);
                    break;
                case "inventory":
                    RenderInventory(playerId, session, rows, buttons, out subtitle);
                    break;
                case "inventory_item":
                    RenderInventoryItem(playerId, session, rows, buttons, out subtitle);
                    break;
                case "world":
                    RenderWorld(playerId, session, rows, buttons, out subtitle);
                    break;
                case "logs":
                    RenderLogs(playerId, session, rows, buttons, out subtitle);
                    break;
                case "log_detail":
                    RenderLogDetail(playerId, session, rows, buttons, out subtitle);
                    break;
                case "snapshots":
                    RenderSnapshots(playerId, session, rows, buttons, out subtitle);
                    break;
                case "snap_view":
                    RenderSnapshotView(playerId, session, rows, buttons, out subtitle);
                    break;
                case "server":
                    RenderServer(playerId, session, rows, buttons, out subtitle);
                    break;
                case "help":
                    RenderHelp(playerId, session, rows, buttons, out subtitle);
                    break;
                case "confirm":
                    RenderConfirm(playerId, session, rows, buttons, out subtitle);
                    break;
                default:
                    session.Screen = "home";
                    RenderHome(playerId, session, rows, buttons, out subtitle);
                    break;
            }

            return BuildView(subtitle, session, rows, buttons);
        }

        private string BuildView(string subtitle, Session session, List<RowInfo> rows, List<ButtonInfo> buttons)
        {
            bool inputVisible = !string.IsNullOrEmpty(session.InputAction);
            string inputLabel = inputVisible
                ? (string.IsNullOrEmpty(session.InputLabel) ? "Input" : session.InputLabel)
                : string.Empty;
            string art = ScreenArt(session.Screen);

            StringBuilder builder = new StringBuilder(4096);
            builder.Append("v1\n");
            builder.Append("title=").Append(Encode(PluginTitle)).Append('\n');
            builder.Append("subtitle=").Append(Encode(subtitle)).Append('\n');
            builder.Append("status=").Append(Encode(session.Status)).Append('\n');
            builder.Append("banner=").Append(Encode(art)).Append('\n');
            builder.Append("background=").Append(Encode(art)).Append('\n');
            builder.Append("input=")
                .Append(Encode(inputLabel))
                .Append('|')
                .Append(inputVisible ? "1" : "0")
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
                    .Append(Encode(button.Tooltip)).Append('\n');
            }

            return builder.ToString();
        }

        private static string ScreenArt(string screen)
        {
            switch (screen)
            {
                case "players":
                case "player":
                case "player_more":
                case "player_staff":
                    return "sprite:sa_nav_players";
                case "buffs":
                    return "sprite:sa_act_godmode";
                case "moderation":
                    return "sprite:sa_nav_moderation";
                case "mutes":
                    return "sprite:sa_act_mute";
                case "freezes":
                    return "sprite:sa_act_freeze";
                case "warnings":
                    return "sprite:sa_act_warn";
                case "banlist":
                case "ban":
                    return "sprite:sa_act_ban";
                case "mute":
                    return "sprite:sa_act_mute";
                case "timeout":
                    return "sprite:sa_act_freeze";
                case "adminlist":
                case "whitelist":
                    return "sprite:sa_nav_moderation";
                case "teleport":
                    return "sprite:sa_nav_teleport";
                case "warps":
                    return "sprite:sa_nav_warps";
                case "items":
                case "item_results":
                case "inventory":
                case "inventory_item":
                    return "sprite:sa_nav_items";
                case "world":
                    return "sprite:sa_nav_world";
                case "logs":
                case "log_detail":
                    return "sprite:sa_nav_logs";
                case "snapshots":
                case "snap_view":
                    return "sprite:sa_nav_snapshots";
                case "server":
                    return "sprite:sa_nav_server";
                case "help":
                    return "sprite:sa_nav_help";
                case "confirm":
                    return "sprite:sa_act_warn";
                default:
                    return "sprite:sa_admin_suite";
            }
        }

        private static void AddRow(List<RowInfo> rows, string action, string label, string sub, bool enabled = true, bool selected = false, string icon = "", string badge = "", string tint = "")
        {
            rows.Add(new RowInfo
            {
                Action = action ?? string.Empty,
                Label = Cut(label, 64),
                Sub = Cut(sub, 150),
                Enabled = enabled,
                Selected = selected,
                Icon = icon ?? string.Empty,
                Badge = Cut(badge, 16),
                Tint = tint ?? string.Empty
            });
        }

        private static void AddButton(List<ButtonInfo> buttons, string action, string label, bool enabled = true, string tooltip = "")
        {
            buttons.Add(new ButtonInfo
            {
                Action = action ?? string.Empty,
                Label = Cut(label, 32),
                Tooltip = Cut(tooltip, 80),
                Enabled = enabled
            });
        }

        private static void AddPager(List<ButtonInfo> buttons, Session session, int count)
        {
            int pages = PageCount(count);
            if (pages <= 1)
            {
                return;
            }

            if (session.Page > 0)
            {
                AddButton(buttons, "page|prev", "< Prev");
            }

            if (session.Page + 1 < pages)
            {
                AddButton(buttons, "page|next", "Next >");
            }
        }

        private void RenderHome(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            int online = players.Connected.Count();
            subtitle = "Staff toolkit  |  " + online + " online  |  " + FormatWorldTime() + "  |  Use the buttons or click a row.";

            AddRow(rows, "nav|players", "Players",
                online + " online - inspect, teleport, bring, heal, moderate", HasTool(playerId, PermPlayersView),
                false, "sprite:sa_nav_players", "ONLINE");
            AddRow(rows, "nav|moderation", "Moderation",
                "Mutes, timeouts, freezes, warnings, bans and kicks",
                HasTool(playerId, PermKick) || HasTool(playerId, PermMute) || HasTool(playerId, PermFreeze) ||
                HasTool(playerId, PermWarn) || HasTool(playerId, PermBan),
                false, "sprite:sa_nav_moderation", "MOD");
            AddRow(rows, "nav|teleport", "Teleport & Movement",
                "Go to players, bring players, coordinates, spawn on top and warps",
                HasTool(playerId, PermTeleport) || HasTool(playerId, PermWarps),
                false, "sprite:sa_nav_teleport", "MOVE");
            AddRow(rows, "nav|items", "Items & Inventory",
                "Give items, inspect inventories, clear, take and repair",
                HasTool(playerId, PermGive) || HasTool(playerId, PermInventory),
                false, "sprite:sa_nav_items", "GEAR");
            AddRow(rows, "nav|world", "World & Weather",
                "Time, weather, hordes, airdrops, chunk resets and game prefs", HasTool(playerId, PermWorld),
                false, "sprite:sa_nav_world", "WORLD");
            AddRow(rows, "nav|logs", "Audit Logs",
                "Chat, joins, deaths, commands and staff actions", HasTool(playerId, PermLogs),
                false, "sprite:sa_nav_logs", "AUDIT");
            AddRow(rows, "nav|snapshots", "Snapshots & Rollback",
                "Capture player state and restore inventories after grief or accidents",
                HasTool(playerId, PermSnapshots) || HasTool(playerId, PermSnapshot) || HasTool(playerId, PermRollback),
                false, "sprite:sa_nav_snapshots", "SNAP");
            AddRow(rows, "nav|server", "Server",
                "Server information, save, reload, announce and shutdown", HasTool(playerId, PermServer),
                false, "sprite:sa_nav_server", "CORE");
            AddRow(rows, "nav|warps", "Warps",
                "Saved staff teleport points", HasTool(playerId, PermWarps),
                false, "sprite:sa_nav_warps", "WARP");
            AddRow(rows, "nav|help", "Help",
                "Permissions, commands and quick tips", true,
                false, "sprite:sa_nav_help", "INFO");

            AddButton(buttons, "nav|players", "Players", HasTool(playerId, PermPlayersView));
            AddButton(buttons, "nav|logs", "Logs", HasTool(playerId, PermLogs));
            AddButton(buttons, "nav|snapshots", "Snapshots",
                HasTool(playerId, PermSnapshots) || HasTool(playerId, PermSnapshot) || HasTool(playerId, PermRollback));
            AddButton(buttons, "nav|server", "Server", HasTool(playerId, PermServer));
            AddButton(buttons, "refresh", "Refresh");
        }

        private void RenderPlayers(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            List<IPlayer> list = players.Connected
                .Where(p => p != null)
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int pages = PageCount(list.Count);
            ClampPage(session, list.Count);
            subtitle = "Online Players  |  " + list.Count + " online  |  page " + (session.Page + 1) + "/" + pages;

            int start = session.Page * UiRows;
            for (int i = 0; i < UiRows && start + i < list.Count; i++)
            {
                IPlayer p = list[start + i];
                EntityPlayer entity = GetEntity(p);
                string sub = SafeId(p) + "  |  ping " + SafePing(p) + "  |  " + HealthSummary(entity);
                if (entity != null)
                {
                    sub += "  |  " + FormatDistanceToActor(playerId, entity);
                }

                string badge = string.Empty;
                string tint = string.Empty;
                if (SafeIsAdmin(p) || permission.UserHasPermission(p.Id, PermAdmin))
                {
                    badge = "ADMIN";
                    tint = "#FFCC66";
                }
                else if (GetActiveMute(p.Id) != null)
                {
                    badge = "MUTED";
                    tint = "#FFAA33";
                }
                else if (GetActiveFreeze(p.Id) != null)
                {
                    badge = "FROZEN";
                    tint = "#66CCFF";
                }

                AddRow(rows, "player|" + SafeId(p), p.Name, sub, true, false,
                    "sprite:sa_nav_players", badge, tint);
            }

            if (list.Count == 0)
            {
                AddRow(rows, string.Empty, "No players are online.", "Check back later or refresh.", false,
                    false, "sprite:sa_nav_players");
            }

            AddBreadcrumb(session, "home");
            buttons.Add(new ButtonInfo
            {
                Action = "input|search_player|Search player name or id|",
                Label = "Search",
                Tooltip = "Find a player by name or id."
            });
            AddButton(buttons, "refresh", "Refresh");
            AddPager(buttons, session, list.Count);
            AddButton(buttons, "back", "Back");
        }

        private void RenderPlayer(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            IPlayer target = GetSelectedPlayer(session);
            EntityPlayer entity = GetEntity(target);
            string name = target != null ? target.Name : GetSelectedName(session);
            subtitle = name + "  |  " + (entity != null ? "online" : (target != null ? "offline" : "not found"));

            if (target == null)
            {
                AddRow(rows, string.Empty, "Player not found.", "The player may have been deleted from the server.", false,
                    false, "sprite:sa_nav_players");
                AddButton(buttons, "nav|players", "Players");
                AddButton(buttons, "back", "Back");
                return;
            }

            Vector3 position = GetEntityPosition(entity);
            string biome = entity != null ? GetBiomeName(position) : "unknown";
            string distance = entity != null ? FormatDistanceToActor(playerId, entity) : "n/a";

            AddRow(rows, string.Empty, "Identity", name + "  |  " + SafeId(target), false,
                false, "sprite:sa_nav_players");
            AddRow(rows, string.Empty, "Connection",
                (entity != null ? "online" : "offline") + "  |  ping " + SafePing(target) + "  |  " +
                SafeAddress(target), false, false, "sprite:sa_nav_server");
            AddRow(rows, string.Empty, "Position",
                (entity != null ? FormatVector(position) : "unknown") + "  |  " + biome + "  |  " + distance,
                false, false, "sprite:sa_nav_teleport");
            AddRow(rows, string.Empty, "Vitals", VitalsSummary(entity), false,
                false, "sprite:sa_act_heal");
            AddRow(rows, string.Empty, "Progress",
                entity != null
                    ? "Game stage " + entity.gameStage + "  |  zombie kills " + entity.KilledZombies +
                      "  |  player kills " + entity.KilledPlayers
                    : "unknown", false, false, "sprite:sa_nav_world");
            AddRow(rows, string.Empty, "Inventory", InventorySummary(entity), false,
                false, "sprite:sa_nav_items");
            AddRow(rows, string.Empty, "Moderation", ModerationSummary(target.Id), false,
                false, "sprite:sa_nav_moderation");
            AddRow(rows, string.Empty, "Staff note", GetNoteText(target.Id) ?? "(none)", false,
                false, "sprite:sa_nav_help");
            AddRow(rows, string.Empty, "Snapshots", CountSnapshots(target.Id) + " stored", false,
                false, "sprite:sa_nav_snapshots");
            AddRow(rows, string.Empty, "Flags",
                (SafeIsAdmin(target) || permission.UserHasPermission(target.Id, PermAdmin) ? "admin" : "player") +
                (entity != null && entity.IsGodMode.Value ? "  |  god mode" : string.Empty) +
                (entity != null && entity.IsSpectator ? "  |  spectator" : string.Empty), false,
                false, "sprite:sa_act_godmode");

            AddButton(buttons, "act|tp_to", "Teleport To", HasTool(playerId, PermTeleport) && entity != null);
            AddButton(buttons, "act|bring", "Bring", HasTool(playerId, PermBring) && entity != null);
            AddButton(buttons, "act|spawn_on_top", "Spawn On Top", HasTool(playerId, PermSpawnOnTop) && entity != null);
            AddButton(buttons, "act|spectate", "Spectate", HasTool(playerId, PermSpectate) && entity != null);
            AddButton(buttons, "act|heal", "Heal", HasTool(playerId, PermHeal) && entity != null);
            AddButton(buttons, "act|feed", "Feed", HasTool(playerId, PermFeed) && entity != null);
            AddButton(buttons, "act|god", "God Mode", HasTool(playerId, PermGod) && entity != null);
            AddButton(buttons, "nav|buffs", "Buffs", HasTool(playerId, PermBuffs) && target != null);
            AddButton(buttons, "nav|player_more", "More...", true);
            AddButton(buttons, "back", "Back");
        }

        private void RenderPlayerMore(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            IPlayer target = GetSelectedPlayer(session);
            EntityPlayer entity = GetEntity(target);
            string name = target != null ? target.Name : GetSelectedName(session);
            subtitle = "Staff actions for " + name;
            bool online = entity != null;
            bool frozen = target != null && GetActiveFreeze(target.Id) != null;

            AddRow(rows, "kick|" + SafeId(target), "Kick", "Disconnect the player with a reason.",
                HasTool(playerId, PermKick) && target != null, false, "sprite:sa_act_kick", "DANGER", "#FF6B6B");
            AddRow(rows, "nav|ban", "Ban", "Ban the player for a duration.",
                HasTool(playerId, PermBan) && target != null, false, "sprite:sa_act_ban", "DANGER", "#FF6B6B");
            AddRow(rows, "nav|mute", "Mute", "Stop the player from chatting.",
                HasTool(playerId, PermMute) && target != null, false, "sprite:sa_act_mute", "MOD", "#FFB347");
            AddRow(rows, "nav|timeout", "Timeout", "Mute and freeze the player for a duration.",
                HasTool(playerId, PermMute) && HasTool(playerId, PermFreeze) && target != null,
                false, "sprite:sa_act_freeze", "MOD", "#FFB347");
            AddRow(rows, "act|toggle_freeze", frozen ? "Unfreeze" : "Freeze",
                frozen ? "Release the player." : "Return the player to their position.",
                HasTool(playerId, PermFreeze) && target != null, false, "sprite:sa_act_freeze", "MOD", "#66CCFF");
            AddRow(rows, "act|kill", "Kill", "Kill the player character.",
                HasTool(playerId, PermKill) && online, false, "sprite:sa_act_ban", "DANGER", "#FF6B6B");
            AddRow(rows, "nav|inventory", "Inventory", "Inspect, take, repair or delete items.",
                HasTool(playerId, PermInventory) && target != null, false, "sprite:sa_nav_items", "GEAR", "#FFD700");
            AddRow(rows, "input|give_item|Item [amount] [quality]|", "Give Item",
                "Give an item directly to this player.", HasTool(playerId, PermGive) && target != null,
                false, "sprite:sa_act_give", "ITEM", "#66CCFF");
            AddRow(rows, "nav|player_staff", "Staff Tools", "Warnings, notes, snapshots, jail and more.", true,
                false, "sprite:sa_nav_moderation", "TOOLS", "#CCCCCC");
            AddRow(rows, "back", "Back", "Return to the player overview.", true,
                false, "sprite:sa_nav_players", "BACK", "#CCCCCC");

            AddButton(buttons, "kick|" + SafeId(target), "Kick", HasTool(playerId, PermKick) && target != null);
            AddButton(buttons, "nav|ban", "Ban", HasTool(playerId, PermBan) && target != null);
            AddButton(buttons, "nav|mute", "Mute", HasTool(playerId, PermMute) && target != null);
            AddButton(buttons, "nav|timeout", "Timeout",
                HasTool(playerId, PermMute) && HasTool(playerId, PermFreeze) && target != null);
            AddButton(buttons, "act|toggle_freeze", frozen ? "Unfreeze" : "Freeze",
                HasTool(playerId, PermFreeze) && target != null);
            AddButton(buttons, "act|kill", "Kill", HasTool(playerId, PermKill) && online);
            AddButton(buttons, "nav|inventory", "Inventory", HasTool(playerId, PermInventory) && target != null);
            AddButton(buttons, "input|give_item|Item [amount] [quality]|", "Give", HasTool(playerId, PermGive) && target != null);
            AddButton(buttons, "nav|player_staff", "Staff Tools");
            AddButton(buttons, "back", "Back");
        }

        private void RenderBan(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            IPlayer target = GetSelectedPlayer(session);
            string name = target != null ? target.Name : GetSelectedName(session);
            subtitle = "Ban " + name + "  |  choose a duration";

            AddRow(rows, string.Empty, "Target", name + "  |  " + (target != null ? SafeId(target) : "unknown"), false,
                false, "sprite:sa_nav_players");
            AddRow(rows, string.Empty, "What happens",
                "The player is banned and kicked immediately. A snapshot is taken first.", false,
                false, "sprite:sa_act_ban", "BAN", "#FF6B6B");
            AddRow(rows, string.Empty, "Presets", "1 hour, 1 day, 7 days, 30 days or permanent.", false,
                false, "sprite:sa_act_time");
            AddRow(rows, string.Empty, "Removal", "Use the ban list later to remove the entry.", false,
                false, "sprite:sa_act_ban", "LIST", "#FFB347");

            AddButton(buttons, "ban|1h", "1 Hour", target != null);
            AddButton(buttons, "ban|1d", "1 Day", target != null);
            AddButton(buttons, "ban|7d", "7 Days", target != null);
            AddButton(buttons, "ban|30d", "30 Days", target != null);
            AddButton(buttons, "ban|perm", "Permanent", target != null);
            buttons.Add(new ButtonInfo
            {
                Action = "input|ban_custom|Duration and reason (example: 7d Cheating)|",
                Label = "Custom",
                Tooltip = "Use a compact duration such as 12h, 7d, 2w or perm.",
                Enabled = target != null
            });
            AddButton(buttons, "back", "Back");
        }

        private void RenderMute(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            IPlayer target = GetSelectedPlayer(session);
            string name = target != null ? target.Name : GetSelectedName(session);
            subtitle = "Mute " + name + "  |  choose a duration";

            AddRow(rows, string.Empty, "Target", name + "  |  " + (target != null ? SafeId(target) : "unknown"), false,
                false, "sprite:sa_nav_players");
            AddRow(rows, string.Empty, "What happens", "The player cannot talk in chat until the mute expires.", false,
                false, "sprite:sa_act_mute", "MUTE", "#FFB347");
            AddRow(rows, string.Empty, "Presets", "5m, 30m, 1h, 6h, 1d or permanent.", false,
                false, "sprite:sa_act_time");
            AddRow(rows, string.Empty, "Removal", "Unmute from the player actions or the mute list.", false,
                false, "sprite:sa_act_mute", "LIST", "#FFD700");

            AddButton(buttons, "mute|5m", "5 Min", target != null);
            AddButton(buttons, "mute|30m", "30 Min", target != null);
            AddButton(buttons, "mute|1h", "1 Hour", target != null);
            AddButton(buttons, "mute|6h", "6 Hours", target != null);
            AddButton(buttons, "mute|1d", "1 Day", target != null);
            AddButton(buttons, "mute|perm", "Permanent", target != null);
            buttons.Add(new ButtonInfo
            {
                Action = "input|mute_custom|Minutes or compact duration and reason (example: 45m Spamming)|",
                Label = "Custom",
                Tooltip = "Use 30m, 2h, 1d or perm.",
                Enabled = target != null
            });
            AddButton(buttons, "back", "Back");
        }

        private void RenderTimeout(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            IPlayer target = GetSelectedPlayer(session);
            string name = target != null ? target.Name : GetSelectedName(session);
            subtitle = "Timeout " + name + "  |  mute + freeze";

            AddRow(rows, string.Empty, "Target", name + "  |  " + (target != null ? SafeId(target) : "unknown"), false,
                false, "sprite:sa_nav_players");
            AddRow(rows, string.Empty, "What happens",
                "The player is muted and frozen at their current position until the timeout expires.", false,
                false, "sprite:sa_act_freeze", "TIMEOUT", "#FFB347");
            AddRow(rows, string.Empty, "Presets", "5m, 15m, 30m, 1h, 6h or 1d.", false,
                false, "sprite:sa_act_time");
            AddRow(rows, string.Empty, "Removal", "Unmute and unfreeze from the player actions or the lists.", false,
                false, "sprite:sa_act_freeze", "LIST", "#66CCFF");

            AddButton(buttons, "timeout|5m", "5 Min", target != null);
            AddButton(buttons, "timeout|15m", "15 Min", target != null);
            AddButton(buttons, "timeout|30m", "30 Min", target != null);
            AddButton(buttons, "timeout|1h", "1 Hour", target != null);
            AddButton(buttons, "timeout|6h", "6 Hours", target != null);
            AddButton(buttons, "timeout|1d", "1 Day", target != null);
            buttons.Add(new ButtonInfo
            {
                Action = "input|timeout_custom|Minutes or compact duration and reason (example: 30m Spamming)|",
                Label = "Custom",
                Tooltip = "Timeouts need a finite duration.",
                Enabled = target != null
            });
            AddButton(buttons, "back", "Back");
        }

        private void RenderPlayerStaff(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            IPlayer target = GetSelectedPlayer(session);
            string name = target != null ? target.Name : GetSelectedName(session);
            subtitle = "Staff tools for " + name;
            MuteEntry mute = target != null ? GetActiveMute(target.Id) : null;
            bool frozen = target != null && GetActiveFreeze(target.Id) != null;

            AddRow(rows, "input|warn|Warning message|", "Warn", "Send and store a warning.",
                HasTool(playerId, PermWarn) && target != null, false, "sprite:sa_act_warn", "WARN", "#FFB347");
            AddRow(rows, "input|note|Staff note|", "Note", "Set a private staff note.",
                HasTool(playerId, PermNotes) && target != null, false, "sprite:sa_nav_help", "NOTE", "#CCCCCC");
            AddRow(rows, "act|snapshot", "Snapshot", "Capture this player's inventory and position.",
                HasTool(playerId, PermSnapshot) && target != null, false, "sprite:sa_nav_snapshots", "SNAP", "#66CCFF");
            AddRow(rows, "snap_filter|" + SafeId(target), "Rollback", "Open this player's snapshots.",
                HasTool(playerId, PermRollback) && target != null, false, "sprite:sa_nav_snapshots", "ROLL", "#66CCFF");
            AddRow(rows, "act|clear_inventory", "Clear Inventory", "Delete every item (snapshot first).",
                HasTool(playerId, PermClearInventory) && target != null, false, "sprite:sa_nav_items", "DANGER", "#FF6B6B");
            AddRow(rows, "act|take_inventory", "Take All Items", "Move every item to your inventory.",
                HasTool(playerId, PermTakeInventory) && target != null, false, "sprite:sa_nav_items", "TAKE", "#FFD700");
            AddRow(rows, "act|repair_inventory", "Repair Items", "Reset all durability to full.",
                HasTool(playerId, PermInventory) && target != null, false, "sprite:sa_act_repair", "FIX", "#66FF99");
            AddRow(rows, "act|jail", "Jail", "Teleport to jail and freeze.",
                HasTool(playerId, PermFreeze) && target != null && config != null && config.EnableJail,
                false, "sprite:sa_act_freeze", "JAIL", "#66CCFF");
            AddRow(rows, "unmute|" + SafeId(target), "Unmute", "Remove the active mute.",
                HasTool(playerId, PermMute) && mute != null, false, "sprite:sa_act_mute", "UNMUTE", "#FFB347");
            AddRow(rows, "unfreeze|" + SafeId(target), "Unfreeze", "Release the player.",
                HasTool(playerId, PermFreeze) && frozen, false, "sprite:sa_act_freeze", "UNFREEZE", "#66CCFF");

            AddButton(buttons, "input|warn|Warning message|", "Warn", HasTool(playerId, PermWarn) && target != null);
            AddButton(buttons, "input|note|Staff note|", "Note", HasTool(playerId, PermNotes) && target != null);
            AddButton(buttons, "act|snapshot", "Snapshot", HasTool(playerId, PermSnapshot) && target != null);
            AddButton(buttons, "snap_filter|" + SafeId(target), "Rollback", HasTool(playerId, PermRollback) && target != null);
            AddButton(buttons, "act|clear_inventory", "Clear", HasTool(playerId, PermClearInventory) && target != null);
            AddButton(buttons, "act|take_inventory", "Take All", HasTool(playerId, PermTakeInventory) && target != null);
            AddButton(buttons, "act|repair_inventory", "Repair", HasTool(playerId, PermInventory) && target != null);
            AddButton(buttons, "act|jail", "Jail", HasTool(playerId, PermFreeze) && target != null && config != null && config.EnableJail);
            AddButton(buttons, mute != null ? "unmute|" + SafeId(target) : "nav|mute",
                mute != null ? "Unmute" : "Mute", HasTool(playerId, PermMute) && target != null);
            AddButton(buttons, "back", "Back");
        }

        private void RenderBuffs(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            IPlayer target = GetSelectedPlayer(session);
            EntityPlayer entity = GetEntity(target);
            string name = target != null ? target.Name : GetSelectedName(session);
            subtitle = "Buffs & Vitals  |  " + name;

            AddRow(rows, string.Empty, "Target",
                name + "  |  " + (entity != null ? "online" : "offline"), false, false, "sprite:sa_nav_players");
            AddRow(rows, string.Empty, "Current vitals", VitalsSummary(entity), false,
                false, "sprite:sa_act_heal");
            AddRow(rows, string.Empty, "Apply buff", "Uses the vanilla buffplayer command.", false,
                false, "sprite:sa_act_godmode", "BUFF", "#CC99FF");
            AddRow(rows, string.Empty, "Remove buff", "Uses the vanilla debuffplayer command.", false,
                false, "sprite:sa_act_godmode", "DEBUFF", "#FFB347");
            AddRow(rows, string.Empty, "Set health", "Any value from 1 to the player's maximum health.", false,
                false, "sprite:sa_act_heal", "HP", "#66FF99");
            AddRow(rows, string.Empty, "Set food/water", "Sets both food and water to the entered value.", false,
                false, "sprite:sa_act_feed", "FOOD", "#FFD700");
            AddRow(rows, string.Empty, "Buff names",
                "Use internal buff names, for example buffGodMode, buffInvisible or buffNearDeathTrauma.", false,
                false, "sprite:sa_nav_help", "INFO", "#CCCCCC");

            buttons.Add(new ButtonInfo
            {
                Action = "input|buff_add|Buff name|",
                Label = "Apply Buff",
                Tooltip = "Apply a buff with buffplayer.",
                Enabled = entity != null && HasTool(playerId, PermBuffs)
            });
            buttons.Add(new ButtonInfo
            {
                Action = "input|buff_remove|Buff name|",
                Label = "Remove Buff",
                Tooltip = "Remove a buff with debuffplayer.",
                Enabled = entity != null && HasTool(playerId, PermBuffs)
            });
            buttons.Add(new ButtonInfo
            {
                Action = "input|set_health|Health value|",
                Label = "Set Health",
                Tooltip = "Set the player's current health.",
                Enabled = entity != null && HasTool(playerId, PermHeal)
            });
            buttons.Add(new ButtonInfo
            {
                Action = "input|set_food|Food and water value|",
                Label = "Set Food/Water",
                Tooltip = "Set food and water to the same value.",
                Enabled = entity != null && HasTool(playerId, PermFeed)
            });
            buttons.Add(new ButtonInfo
            {
                Action = "act|toggle_fly",
                Label = "Toggle Fly",
                Tooltip = "Single-player: toggles fly. Dedicated: opens your debug menu.",
                Enabled = entity != null && HasTool(playerId, PermFly)
            });
            AddButton(buttons, "back", "Back");
        }

        private void RenderModeration(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            int mutes = CountActiveMutes();
            int freezes = CountActiveFreezes();
            int warnings = CountWarnings();
            int bans = GetBanList().Count;
            int admins = GetAdminUsers().Count + GetAdminGroups().Count;
            int whitelist = GetWhitelistUsers().Count + GetWhitelistGroups().Count;
            bool whitelistOn = IsWhitelistEnabled();

            subtitle = "Moderation  |  " + mutes + " mutes  |  " + freezes + " freezes  |  " + bans + " bans  |  " +
                       warnings + " warnings";

            AddRow(rows, "nav|mutes", "Mutes", mutes + " active mute(s)", true,
                false, "sprite:sa_act_mute", "MUTED", "#FFB347");
            AddRow(rows, "nav|freezes", "Freezes", freezes + " active freeze(s)", true,
                false, "sprite:sa_act_freeze", "FROZEN", "#66CCFF");
            AddRow(rows, "nav|warnings", "Warnings", warnings + " stored warning(s)", true,
                false, "sprite:sa_act_warn", "WARN", "#FFD700");
            AddRow(rows, "nav|banlist", "Ban List", bans + " ban entr" + (bans == 1 ? "y" : "ies") + ".",
                HasTool(playerId, PermBanList) || HasTool(playerId, PermBan),
                false, "sprite:sa_act_ban", "BAN", "#FF6B6B");
            AddRow(rows, "nav|adminlist", "Admin List", admins + " server admin entr" + (admins == 1 ? "y" : "ies") + ".",
                HasTool(playerId, PermAdminList), false, "sprite:sa_nav_moderation", "ADMIN", "#FFCC66");
            AddRow(rows, "nav|whitelist", "Whitelist",
                (whitelistOn ? "enabled" : "disabled") + "  |  " + whitelist + " entr" + (whitelist == 1 ? "y" : "ies"),
                HasTool(playerId, PermWhitelist), false, "sprite:sa_nav_moderation", "WHITE", "#99FF99");
            AddRow(rows, "act|kickall", "Kick All", "Kick every connected player.",
                HasTool(playerId, PermKickAll), false, "sprite:sa_act_kick", "DANGER", "#FF6B6B");
            AddRow(rows, "act|clearmutes", "Clear Mutes", "Remove every active mute.",
                HasTool(playerId, PermMute), false, "sprite:sa_act_mute", "CLEAR", "#FFB347");
            AddRow(rows, "act|clearwarnings", "Clear Warnings", "Remove every stored warning.",
                HasTool(playerId, PermWarn), false, "sprite:sa_act_warn", "CLEAR", "#FFD700");
            AddRow(rows, "back", "Back", "Return to the home screen.", true,
                false, "sprite:sa_admin_suite", "BACK", "#CCCCCC");

            AddButton(buttons, "nav|banlist", "Bans", HasTool(playerId, PermBanList) || HasTool(playerId, PermBan));
            AddButton(buttons, "nav|adminlist", "Admins", HasTool(playerId, PermAdminList));
            AddButton(buttons, "nav|whitelist", "Whitelist", HasTool(playerId, PermWhitelist));
            AddButton(buttons, "nav|warnings", "Warnings", true);
            AddButton(buttons, "act|healall", "Heal All", HasTool(playerId, PermHeal));
            AddButton(buttons, "act|feedall", "Feed All", HasTool(playerId, PermFeed));
            AddButton(buttons, "back", "Back");
        }

        private void RenderMutes(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            List<KeyValuePair<string, MuteEntry>> list = data.Mutes
                .Where(p => p.Value != null && IsMuteActive(p.Value))
                .OrderBy(p => p.Value.PlayerName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int pages = PageCount(list.Count);
            ClampPage(session, list.Count);
            subtitle = "Active Mutes  |  " + list.Count + " mute(s)  |  page " + (session.Page + 1) + "/" + pages;

            int start = session.Page * UiRows;
            for (int i = 0; i < UiRows && start + i < list.Count; i++)
            {
                MuteEntry entry = list[start + i].Value;
                string name = string.IsNullOrEmpty(entry.PlayerName) ? list[start + i].Key : entry.PlayerName;
                string sub = (string.IsNullOrEmpty(entry.Reason) ? "no reason" : entry.Reason) +
                             "  |  " + FormatRemaining(entry.UntilUtcTicks) +
                             "  |  by " + (string.IsNullOrEmpty(entry.By) ? "unknown" : entry.By);
                AddRow(rows, "unmute|" + list[start + i].Key, name, sub,
                    HasTool(playerId, PermMute), false, "sprite:sa_act_mute", "MUTED", "#FFB347");
            }

            if (list.Count == 0)
            {
                AddRow(rows, string.Empty, "No active mutes.", "Use a player's Staff Tools to add one.", false,
                    false, "sprite:sa_act_mute");
            }

            AddButton(buttons, "act|clearmutes", "Clear All", HasTool(playerId, PermMute));
            AddButton(buttons, "refresh", "Refresh");
            AddPager(buttons, session, list.Count);
            AddButton(buttons, "back", "Back");
        }

        private void RenderFreezes(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            List<KeyValuePair<string, FreezeEntry>> list = data.Freezes
                .Where(p => p.Value != null && IsFreezeActive(p.Value))
                .OrderBy(p => p.Value.PlayerName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int pages = PageCount(list.Count);
            ClampPage(session, list.Count);
            subtitle = "Active Freezes  |  " + list.Count + " freeze(s)  |  page " + (session.Page + 1) + "/" + pages;

            int start = session.Page * UiRows;
            for (int i = 0; i < UiRows && start + i < list.Count; i++)
            {
                FreezeEntry entry = list[start + i].Value;
                string name = string.IsNullOrEmpty(entry.PlayerName) ? list[start + i].Key : entry.PlayerName;
                string sub = (string.IsNullOrEmpty(entry.Reason) ? "no reason" : entry.Reason) +
                             "  |  " + FormatRemaining(entry.UntilUtcTicks) +
                             "  |  at " + FormatVector(entry.X, entry.Y, entry.Z);
                AddRow(rows, "unfreeze|" + list[start + i].Key, name, sub,
                    HasTool(playerId, PermFreeze), false, "sprite:sa_act_freeze", "FROZEN", "#66CCFF");
            }

            if (list.Count == 0)
            {
                AddRow(rows, string.Empty, "No active freezes.", "Use a player's Staff Tools to add one.", false,
                    false, "sprite:sa_act_freeze");
            }

            AddButton(buttons, "refresh", "Refresh");
            AddPager(buttons, session, list.Count);
            AddButton(buttons, "back", "Back");
        }

        private void RenderWarnings(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            List<KeyValuePair<string, WarningEntry>> list = new List<KeyValuePair<string, WarningEntry>>();
            foreach (KeyValuePair<string, List<WarningEntry>> pair in data.Warnings)
            {
                if (pair.Value == null)
                {
                    continue;
                }

                for (int i = 0; i < pair.Value.Count; i++)
                {
                    if (pair.Value[i] != null)
                    {
                        list.Add(new KeyValuePair<string, WarningEntry>(pair.Key, pair.Value[i]));
                    }
                }
            }

            list = list.OrderByDescending(p => p.Value.CreatedUtcTicks).ToList();
            int pages = PageCount(list.Count);
            ClampPage(session, list.Count);
            subtitle = "Warnings  |  " + list.Count + " stored  |  page " + (session.Page + 1) + "/" + pages;

            int start = session.Page * UiRows;
            for (int i = 0; i < UiRows && start + i < list.Count; i++)
            {
                WarningEntry entry = list[start + i].Value;
                string name = string.IsNullOrEmpty(entry.PlayerName) ? list[start + i].Key : entry.PlayerName;
                AddRow(rows, string.Empty, name,
                    FormatDateTime(entry.CreatedUtcTicks) + "  |  " + entry.Message +
                    "  |  by " + (string.IsNullOrEmpty(entry.By) ? "unknown" : entry.By), false,
                    false, "sprite:sa_act_warn", "WARN", "#FFD700");
            }

            if (list.Count == 0)
            {
                AddRow(rows, string.Empty, "No warnings stored.", "Use a player's Staff Tools to add one.", false,
                    false, "sprite:sa_act_warn");
            }

            buttons.Add(new ButtonInfo
            {
                Action = "input|warn_any|Player name or id ; warning message|",
                Label = "Warn Player",
                Tooltip = "Format: player name or id ; message",
                Enabled = HasTool(playerId, PermWarn)
            });
            AddButton(buttons, "act|clearwarnings", "Clear All", HasTool(playerId, PermWarn));
            AddButton(buttons, "refresh", "Refresh");
            AddPager(buttons, session, list.Count);
            AddButton(buttons, "back", "Back");
        }

        private void RenderBanList(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            List<AdminBlacklist.BannedUser> list = GetBanList();
            int pages = PageCount(list.Count);
            ClampPage(session, list.Count);
            subtitle = "Ban List  |  " + list.Count + " entr" + (list.Count == 1 ? "y" : "ies") +
                       "  |  page " + (session.Page + 1) + "/" + pages;

            int start = session.Page * UiRows;
            for (int i = 0; i < UiRows && start + i < list.Count; i++)
            {
                AdminBlacklist.BannedUser ban = list[start + i];
                string name = string.IsNullOrEmpty(ban.Name) ? "Unknown" : ban.Name;
                string id = ban.UserIdentifier != null ? ban.UserIdentifier.ToString() : "unknown id";
                string until = ban.BannedUntil == DateTime.MaxValue || ban.BannedUntil == DateTime.MinValue
                    ? "permanent"
                    : FormatDateTime(ban.BannedUntil.ToUniversalTime().Ticks);
                AddRow(rows, "unban|" + (start + i), name,
                    id + "  |  until " + until + "  |  " +
                    (string.IsNullOrEmpty(ban.BanReason) ? "no reason" : ban.BanReason),
                    HasTool(playerId, PermBan), false, "sprite:sa_act_ban", "BAN", "#FF6B6B");
            }

            if (list.Count == 0)
            {
                AddRow(rows, string.Empty, "The ban list is empty.", "Click a ban row to remove it.", false,
                    false, "sprite:sa_act_ban");
            }

            AddButton(buttons, "refresh", "Refresh");
            AddPager(buttons, session, list.Count);
            AddButton(buttons, "back", "Back");
        }

        private void RenderAdminList(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            Dictionary<PlatformUserIdentifierAbs, AdminUsers.UserPermission> users = GetAdminUsers();
            Dictionary<string, AdminUsers.GroupPermission> groups = GetAdminGroups();
            subtitle = "Server Admin List  |  " + users.Count + " user(s)  |  " + groups.Count + " group(s)";

            int added = 0;
            foreach (KeyValuePair<PlatformUserIdentifierAbs, AdminUsers.UserPermission> pair in users)
            {
                if (added >= UiRows)
                {
                    break;
                }

                AdminUsers.UserPermission perm = pair.Value;
                string name = string.IsNullOrEmpty(perm.Name) ? "Unknown" : perm.Name;
                string id = pair.Key != null ? pair.Key.ToString() : "unknown id";
                AddRow(rows, string.Empty, name, id + "  |  permission level " + perm.PermissionLevel, false,
                    false, "sprite:sa_nav_moderation", "ADMIN", "#FFCC66");
                added++;
            }

            foreach (KeyValuePair<string, AdminUsers.GroupPermission> pair in groups)
            {
                if (added >= UiRows)
                {
                    break;
                }

                AdminUsers.GroupPermission perm = pair.Value;
                AddRow(rows, string.Empty, "Group: " + (string.IsNullOrEmpty(perm.Name) ? pair.Key : perm.Name),
                    "steam group " + perm.SteamIdGroup + "  |  levels " + perm.PermissionLevelNormal + "/" +
                    perm.PermissionLevelMods, false, false, "sprite:sa_nav_moderation", "GROUP", "#CC99FF");
                added++;
            }

            if (added == 0)
            {
                AddRow(rows, string.Empty, "No server admin entries.", "The server admin file has no users or groups.", false,
                    false, "sprite:sa_nav_moderation");
            }

            buttons.Add(new ButtonInfo
            {
                Action = "input|admin_add|Player name or id and level (example: PlayerName 0)|",
                Label = "Add Admin",
                Tooltip = "Uses the vanilla admin add command.",
                Enabled = HasTool(playerId, PermAdminList)
            });
            buttons.Add(new ButtonInfo
            {
                Action = "input|admin_remove|Player name or id|",
                Label = "Remove Admin",
                Tooltip = "Uses the vanilla admin remove command.",
                Enabled = HasTool(playerId, PermAdminList)
            });
            AddButton(buttons, "refresh", "Refresh");
            AddButton(buttons, "back", "Back");
        }

        private void RenderWhitelist(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            Dictionary<PlatformUserIdentifierAbs, AdminWhitelist.WhitelistUser> users = GetWhitelistUsers();
            Dictionary<string, AdminWhitelist.WhitelistGroup> groups = GetWhitelistGroups();
            subtitle = "Whitelist  |  " + (IsWhitelistEnabled() ? "enabled" : "disabled") + "  |  " +
                       users.Count + " user(s)  |  " + groups.Count + " group(s)";

            int added = 0;
            foreach (KeyValuePair<PlatformUserIdentifierAbs, AdminWhitelist.WhitelistUser> pair in users)
            {
                if (added >= UiRows)
                {
                    break;
                }

                AdminWhitelist.WhitelistUser user = pair.Value;
                string name = string.IsNullOrEmpty(user.Name) ? "Unknown" : user.Name;
                AddRow(rows, string.Empty, name,
                    (pair.Key != null ? pair.Key.ToString() : "unknown id"), false,
                    false, "sprite:sa_nav_moderation", "WHITE", "#99FF99");
                added++;
            }

            foreach (KeyValuePair<string, AdminWhitelist.WhitelistGroup> pair in groups)
            {
                if (added >= UiRows)
                {
                    break;
                }

                AdminWhitelist.WhitelistGroup group = pair.Value;
                AddRow(rows, string.Empty, "Group: " + (string.IsNullOrEmpty(group.Name) ? pair.Key : group.Name),
                    "steam group " + group.SteamIdGroup, false,
                    false, "sprite:sa_nav_moderation", "GROUP", "#CC99FF");
                added++;
            }

            if (added == 0)
            {
                AddRow(rows, string.Empty, "The whitelist is empty.", "Add a user or group below.", false,
                    false, "sprite:sa_nav_moderation");
            }

            buttons.Add(new ButtonInfo
            {
                Action = "input|whitelist_add|Player name or id|",
                Label = "Add User",
                Tooltip = "Uses the vanilla whitelist add command.",
                Enabled = HasTool(playerId, PermWhitelist)
            });
            buttons.Add(new ButtonInfo
            {
                Action = "input|whitelist_remove|Player name or id|",
                Label = "Remove User",
                Tooltip = "Uses the vanilla whitelist remove command.",
                Enabled = HasTool(playerId, PermWhitelist)
            });
            AddButton(buttons, "refresh", "Refresh");
            AddButton(buttons, "back", "Back");
        }

        private void RenderTeleport(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            subtitle = "Teleport & Movement";

            AddRow(rows, "nav|players", "Teleport to Player", "Select a player from the list.",
                HasTool(playerId, PermTeleport), false, "sprite:sa_nav_teleport", "GOTO", "#66CCFF");
            AddRow(rows, "nav|players", "Bring Player", "Select a player, then press Bring.",
                HasTool(playerId, PermBring), false, "sprite:sa_nav_teleport", "BRING", "#66CCFF");
            AddRow(rows, "nav|players", "Spawn on Top", "Select a player, then press Spawn On Top.",
                HasTool(playerId, PermSpawnOnTop), false, "sprite:sa_nav_teleport", "TOP", "#99FF99");
            AddRow(rows, "input|tp_coords|X Y Z|", "Teleport to Coordinates", "Use 2 values for ground level or 3 for exact.",
                HasTool(playerId, PermTeleport), false, "sprite:sa_nav_teleport", "XYZ", "#66CCFF");
            AddRow(rows, "input|tp_target_coords|X Y Z|", "Send Selected to Coordinates",
                "Send the selected player to X Y Z.", HasTool(playerId, PermTeleport) && session.SelectedPlayerId != null,
                false, "sprite:sa_nav_teleport", "SEND", "#FFB347");
            AddRow(rows, "input|tp_target_player|Player name or id|", "Send Selected to Player",
                "Send the selected player to another player.",
                HasTool(playerId, PermTeleport) && session.SelectedPlayerId != null,
                false, "sprite:sa_nav_teleport", "SEND", "#FFB347");
            AddRow(rows, "nav|warps", "Warps", "Saved staff teleport points.", HasTool(playerId, PermWarps),
                false, "sprite:sa_nav_warps", "WARP", "#CC99FF");
            AddRow(rows, "act|sethome", "Set My Home", "Save your current position as home.",
                HasTool(playerId, PermTeleport), false, "sprite:sa_nav_teleport", "SET", "#FFD700");
            AddRow(rows, "act|home", "Go To My Home", "Teleport to your saved home.",
                HasTool(playerId, PermTeleport), false, "sprite:sa_nav_teleport", "HOME", "#FFD700");
            AddRow(rows, "back", "Back", "Return to the home screen.", true,
                false, "sprite:sa_admin_suite");

            AddButton(buttons, "nav|warps", "Warps", HasTool(playerId, PermWarps));
            AddButton(buttons, "act|sethome", "Set Home", HasTool(playerId, PermTeleport));
            AddButton(buttons, "act|home", "Go Home", HasTool(playerId, PermTeleport));
            AddButton(buttons, "back", "Back");
        }

        private void RenderWarps(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            List<WarpEntry> list = data.Warps.Values
                .Where(w => w != null)
                .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int pages = PageCount(list.Count);
            ClampPage(session, list.Count);
            subtitle = "Warps  |  " + list.Count + " saved  |  page " + (session.Page + 1) + "/" + pages;

            int start = session.Page * UiRows;
            for (int i = 0; i < UiRows && start + i < list.Count; i++)
            {
                WarpEntry warp = list[start + i];
                AddRow(rows, "warp|" + warp.Name, warp.Name,
                    FormatVector(warp.X, warp.Y, warp.Z) + "  |  by " +
                    (string.IsNullOrEmpty(warp.CreatedBy) ? "unknown" : warp.CreatedBy),
                    HasTool(playerId, PermWarps) || HasTool(playerId, PermTeleport),
                    false, "sprite:sa_nav_warps", "WARP", "#CC99FF");
            }

            if (list.Count == 0)
            {
                AddRow(rows, string.Empty, "No warps saved.", "Use Set Warp to add one.", false,
                    false, "sprite:sa_nav_warps");
            }

            AddButton(buttons, "input|set_warp|Warp name|", "Set Warp", HasTool(playerId, PermWarps));
            AddButton(buttons, "input|del_warp|Warp name|", "Delete Warp", HasTool(playerId, PermWarps));
            AddButton(buttons, "refresh", "Refresh");
            AddPager(buttons, session, list.Count);
            AddButton(buttons, "back", "Back");
        }

        private void RenderItems(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            subtitle = "Items & Inventory  |  " + GetSelectedName(session);

            AddRow(rows, "input|give_item|Item [amount] [quality]|", "Give Item to Selected",
                "Give an item directly to the selected player.", HasTool(playerId, PermGive) && session.SelectedPlayerId != null,
                false, "sprite:sa_act_give", "GIVE", "#66CCFF");
            AddRow(rows, "input|give_self|Item [amount] [quality]|", "Give Item to Me",
                "Give an item directly to yourself.", HasTool(playerId, PermGive),
                false, "sprite:sa_act_give", "SELF", "#66FF99");
            AddRow(rows, "input|item_search|Item name|", "Search Items",
                "Browse the item catalog by name.", true,
                false, "sprite:sa_nav_items", "SEARCH", "#FFD700");
            AddRow(rows, "input|spawn_entity|Entity name [count]|", "Spawn Entity",
                "Spawn an entity near you.", HasTool(playerId, PermWorld),
                false, "sprite:sa_nav_world", "SPAWN", "#FF6B6B");
            AddRow(rows, "act|creative_menu", "Open Creative Menu",
                "Open the vanilla creative menu on your client.", HasTool(playerId, PermGive),
                false, "sprite:sa_nav_items", "MENU", "#FFD700");
            AddRow(rows, "nav|inventory", "Inspect Selected Inventory",
                "Browse the selected player's toolbelt, backpack and equipment.",
                HasTool(playerId, PermInventory) && session.SelectedPlayerId != null,
                false, "sprite:sa_nav_items", "VIEW", "#66CCFF");
            AddRow(rows, "act|clear_inventory", "Clear Selected Inventory",
                "Delete every item from the selected player.",
                HasTool(playerId, PermClearInventory) && session.SelectedPlayerId != null,
                false, "sprite:sa_nav_items", "DANGER", "#FF6B6B");
            AddRow(rows, "act|take_inventory", "Take Selected Items",
                "Move every item to your inventory.",
                HasTool(playerId, PermTakeInventory) && session.SelectedPlayerId != null,
                false, "sprite:sa_nav_items", "TAKE", "#FFD700");
            AddRow(rows, "act|repair_inventory", "Repair Selected Items",
                "Reset all durability to full.",
                HasTool(playerId, PermInventory) && session.SelectedPlayerId != null,
                false, "sprite:sa_act_repair", "FIX", "#66FF99");
            AddRow(rows, "back", "Back", "Return to the home screen.", true,
                false, "sprite:sa_admin_suite");

            AddButton(buttons, "input|item_search|Item name|", "Search");
            AddButton(buttons, "act|creative_menu", "Creative", HasTool(playerId, PermGive));
            AddButton(buttons, "back", "Back");
        }

        private void RenderItemResults(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            List<ItemClass> results = GetItemSearchResults(session.Search);
            int pages = PageCount(results.Count);
            ClampPage(session, results.Count);
            subtitle = "Item Search  |  '" + (string.IsNullOrEmpty(session.Search) ? "all" : session.Search) + "'  |  " +
                       results.Count + " result(s)  |  page " + (session.Page + 1) + "/" + pages;

            int start = session.Page * UiRows;
            for (int i = 0; i < UiRows && start + i < results.Count; i++)
            {
                ItemClass item = results[start + i];
                string name = ItemDisplayName(item);
                string category = ItemCategory(item);
                string sub = item.Name + "  |  max stack " + GetMaxStack(item);
                AddRow(rows, "give_item_name|" + item.Name, name, sub,
                    HasTool(playerId, PermGive), false, ItemIcon(item, category),
                    ItemCategoryLabel(category), ItemCategoryTint(category));
            }

            if (results.Count == 0)
            {
                AddRow(rows, string.Empty, "No items matched.", "Try a different search term.", false,
                    false, "sprite:sa_nav_items");
            }

            buttons.Add(new ButtonInfo
            {
                Action = "input|item_search|Item name|",
                Label = "New Search",
                Tooltip = "Search the item catalog again."
            });
            AddButton(buttons, "refresh", "Refresh");
            AddPager(buttons, session, results.Count);
            AddButton(buttons, "back", "Back");
        }

        private void RenderInventory(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            IPlayer target = GetSelectedPlayer(session);
            EntityPlayer entity = GetEntity(target);
            string name = target != null ? target.Name : GetSelectedName(session);
            string section = NormalizeInventorySection(session.InventorySection);
            subtitle = "Inventory  |  " + name + "  |  " + SectionLabel(section);

            if (entity == null)
            {
                AddRow(rows, string.Empty, "Player is not online.", "The inventory inspector needs a spawned player.", false,
                    false, "sprite:sa_nav_players");
                AddButton(buttons, "nav|players", "Players");
                AddButton(buttons, "back", "Back");
                return;
            }

            List<InvItem> items = GetInventoryItems(entity, section);
            int pages = PageCount(items.Count);
            ClampPage(session, items.Count);
            subtitle += "  |  " + items.Count + " item(s)  |  page " + (session.Page + 1) + "/" + pages;

            int start = session.Page * UiRows;
            for (int i = 0; i < UiRows && start + i < items.Count; i++)
            {
                InvItem item = items[start + i];
                AddRow(rows, "inv_item|" + section + "|" + item.Index,
                    item.Name,
                    "slot " + item.Index + "  |  x" + item.Count + QualityText(item.Quality) +
                    DurabilityText(item.Percent) + "  |  " + item.InternalName,
                    true, false, item.Icon, ItemCategoryLabel(item.Category), ItemCategoryTint(item.Category));
            }

            if (items.Count == 0)
            {
                AddRow(rows, string.Empty, "This section is empty.", "Use the section buttons to switch.", false,
                    false, "sprite:sa_nav_items");
            }

            AddButton(buttons, "invsection|toolbelt", "Toolbelt", true, "Show the toolbelt.");
            AddButton(buttons, "invsection|backpack", "Backpack", true, "Show the backpack.");
            AddButton(buttons, "invsection|equipment", "Equipment", true, "Show armor and equipment.");
            AddButton(buttons, "act|take_all_inventory", "Take All", HasTool(playerId, PermTakeInventory));
            AddButton(buttons, "act|repair_all_inventory", "Repair", HasTool(playerId, PermInventory));
            AddButton(buttons, "act|clear_all_inventory", "Clear", HasTool(playerId, PermClearInventory));
            AddButton(buttons, "input|give_item|Item [amount] [quality]|", "Give", HasTool(playerId, PermGive));
            AddPager(buttons, session, items.Count);
            AddButton(buttons, "back", "Back");
        }

        private void RenderInventoryItem(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            IPlayer target = GetSelectedPlayer(session);
            EntityPlayer entity = GetEntity(target);
            InvItem item = GetInventoryItem(entity, session.InventorySection, session.InventoryIndex);
            subtitle = item != null
                ? "Item  |  " + item.Name + "  |  " + ItemCategoryLabel(item.Category)
                : "Item";

            if (item == null)
            {
                AddRow(rows, string.Empty, "Item not found.", "It may have moved since the screen was opened.", false,
                    false, "sprite:sa_nav_items");
                AddButton(buttons, "back", "Back");
                return;
            }

            AddRow(rows, string.Empty, "Name", item.Name, false,
                false, item.Icon, ItemCategoryLabel(item.Category), ItemCategoryTint(item.Category));
            AddRow(rows, string.Empty, "Category", ItemCategoryLabel(item.Category), false,
                false, ItemCategoryIcon(item.Category), ItemCategoryLabel(item.Category), ItemCategoryTint(item.Category));
            AddRow(rows, string.Empty, "Internal name", item.InternalName, false,
                false, item.Icon);
            AddRow(rows, string.Empty, "Section / slot", SectionLabel(item.Section) + " / " + item.Index, false,
                false, "sprite:sa_nav_items");
            AddRow(rows, string.Empty, "Count", item.Count.ToString(CultureInfo.InvariantCulture), false,
                false, "sprite:sa_nav_items");
            AddRow(rows, string.Empty, "Quality", item.Quality <= 0 ? "n/a" : item.Quality.ToString(CultureInfo.InvariantCulture), false,
                false, "sprite:sa_act_repair", "Q", "#FFD700");
            AddRow(rows, string.Empty, "Durability", item.Percent < 0f ? "n/a" : (int)(item.Percent * 100f) + "%", false,
                false, "sprite:sa_act_repair", "DUR", "#66FF99");
            AddRow(rows, string.Empty, "Player", target != null ? target.Name : GetSelectedName(session), false,
                false, "sprite:sa_nav_players");

            AddButton(buttons, "act|inv_take|" + item.Section + "|" + item.Index, "Take",
                HasTool(playerId, PermTakeInventory));
            AddButton(buttons, "act|inv_repair|" + item.Section + "|" + item.Index, "Repair",
                HasTool(playerId, PermInventory));
            AddButton(buttons, "act|inv_delete|" + item.Section + "|" + item.Index, "Delete",
                HasTool(playerId, PermClearInventory));
            AddButton(buttons, "back", "Back");
        }

        private void RenderWorld(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            subtitle = "World & Weather  |  " + FormatWorldTime();

            AddRow(rows, "act|time_day", "Set Day", "Jump to noon on the current day.", HasTool(playerId, PermWorld),
                false, "sprite:sa_act_time", "DAY", "#FFD700");
            AddRow(rows, "act|time_night", "Set Night", "Jump to midnight on the next day.", HasTool(playerId, PermWorld),
                false, "sprite:sa_act_time", "NIGHT", "#6666FF");
            AddRow(rows, "input|set_time|day, night, hour, or day hour minute|", "Set Time",
                "Use day, night, a time value, or day hour minute.", HasTool(playerId, PermWorld),
                false, "sprite:sa_act_time", "TIME", "#FFD700");
            AddRow(rows, "act|weather_clear", "Clear Weather", "Clear rain, snow, fog, clouds and wind.",
                HasTool(playerId, PermWorld), false, "sprite:sa_act_weather", "CLEAR", "#FFD700");
            AddRow(rows, "act|weather_rain", "Rain", "Turn rain on.", HasTool(playerId, PermWorld),
                false, "sprite:sa_act_weather", "RAIN", "#66CCFF");
            AddRow(rows, "input|weather_storm|Hours|", "Storm", "Start a storm in your current biome.",
                HasTool(playerId, PermWorld), false, "sprite:sa_act_weather", "STORM", "#FF6B6B");
            AddRow(rows, "act|airdrop", "Airdrop", "Send an airdrop to the world.", HasTool(playerId, PermWorld),
                false, "sprite:sa_act_airdrop", "DROP", "#FFCC66");
            AddRow(rows, "act|horde", "Wandering Horde", "Spawn a wandering horde.", HasTool(playerId, PermWorld),
                false, "sprite:sa_nav_world", "HORDE", "#FF6B6B");
            AddRow(rows, "act|scouts", "Scouts", "Spawn scout zombies.", HasTool(playerId, PermWorld),
                false, "sprite:sa_nav_world", "SCOUT", "#FFB347");
            AddRow(rows, "act|save", "Save World", "Force a world save.", HasTool(playerId, PermServer) || HasTool(playerId, PermWorld),
                false, "sprite:sa_nav_server", "SAVE", "#66FF99");

            AddButton(buttons, "act|save", "Save", HasTool(playerId, PermServer) || HasTool(playerId, PermWorld));
            AddButton(buttons, "act|debugmenu", "Debug Menu", true);
            AddButton(buttons, "input|setpref|PrefName Value|", "Set Pref", IsAdmin(playerId));
            AddButton(buttons, "act|regionreset", "Region Reset", HasTool(playerId, PermWorld));
            AddButton(buttons, "act|chunkreset", "Chunk Reset", HasTool(playerId, PermWorld));
            AddButton(buttons, "back", "Back");
        }

        private void RenderLogs(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            List<LogEntry> list = GetFilteredLogs(session);
            int pages = PageCount(list.Count);
            ClampPage(session, list.Count);
            subtitle = "Audit Logs  |  filter " + session.LogFilter + "  |  " + list.Count +
                       " entr" + (list.Count == 1 ? "y" : "ies") + "  |  page " + (session.Page + 1) + "/" + pages;

            int start = session.Page * UiRows;
            for (int i = 0; i < UiRows && start + i < list.Count; i++)
            {
                LogEntry entry = list[start + i];
                string sub = (string.IsNullOrEmpty(entry.Actor) ? "system" : entry.Actor) +
                             (string.IsNullOrEmpty(entry.Target) ? string.Empty : " -> " + entry.Target) +
                             "  |  " + FormatDate(entry.UtcTicks);
                AddRow(rows, "log_detail|" + (start + i),
                    "[" + entry.Category.ToUpperInvariant() + "] " + entry.Message, sub, true,
                    false, LogIcon(entry.Category),
                    (entry.Category ?? "log").ToUpperInvariant(), LogTint(entry.Category));
            }

            if (list.Count == 0)
            {
                AddRow(rows, string.Empty, "No log entries for this filter.", "Try another filter or generate some activity.", false,
                    false, "sprite:sa_nav_logs");
            }

            AddButton(buttons, "logfilter|all", "All", true);
            AddButton(buttons, "logfilter|chat", "Chat", true);
            AddButton(buttons, "logfilter|join", "Join/Leave", true);
            AddButton(buttons, "logfilter|death", "Deaths", true);
            AddButton(buttons, "logfilter|admin", "Admin", true);
            AddButton(buttons, "logfilter|command", "Commands", true);
            AddButton(buttons, "act|log_clear", "Clear", HasTool(playerId, PermLogs));
            AddPager(buttons, session, list.Count);
            AddButton(buttons, "back", "Back");
        }

        private void RenderLogDetail(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            List<LogEntry> list = GetFilteredLogs(session);
            LogEntry entry = session.SelectedLogIndex >= 0 && session.SelectedLogIndex < list.Count
                ? list[session.SelectedLogIndex]
                : null;

            subtitle = "Log Entry";
            if (entry == null)
            {
                AddRow(rows, string.Empty, "Log entry is no longer available.", "The list changed since it was opened.", false,
                    false, "sprite:sa_nav_logs");
                AddButton(buttons, "back", "Back");
                return;
            }

            AddRow(rows, string.Empty, "Time", FormatDateTime(entry.UtcTicks), false,
                false, "sprite:sa_act_time");
            AddRow(rows, string.Empty, "Category", entry.Category, false,
                false, LogIcon(entry.Category), (entry.Category ?? "log").ToUpperInvariant(), LogTint(entry.Category));
            AddRow(rows, string.Empty, "Actor", string.IsNullOrEmpty(entry.Actor) ? "system" : entry.Actor, false,
                false, "sprite:sa_nav_players");
            AddRow(rows, string.Empty, "Target", string.IsNullOrEmpty(entry.Target) ? "(none)" : entry.Target, false,
                false, "sprite:sa_nav_players");
            AddRow(rows, string.Empty, "Message", entry.Message, false,
                false, "sprite:sa_nav_logs");

            AddButton(buttons, "back", "Back");
        }

        private void RenderSnapshots(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            List<Snapshot> list = GetFilteredSnapshots(session);
            int pages = PageCount(list.Count);
            ClampPage(session, list.Count);
            string filterName = string.IsNullOrEmpty(session.SnapshotFilterPlayerId)
                ? "all players"
                : GetPlayerName(session.SnapshotFilterPlayerId);
            subtitle = "Snapshots  |  " + filterName + "  |  " + list.Count + " snapshot(s)  |  page " +
                       (session.Page + 1) + "/" + pages;

            int start = session.Page * UiRows;
            for (int i = 0; i < UiRows && start + i < list.Count; i++)
            {
                Snapshot snapshot = list[start + i];
                AddRow(rows, "snap_view|" + snapshot.Id,
                    snapshot.PlayerName + "  |  " + FormatDateTime(snapshot.CreatedUtcTicks),
                    snapshot.Reason + "  |  " + SnapshotItemCount(snapshot) + " item(s)", true,
                    false, "sprite:sa_nav_snapshots", "SNAP", "#66CCFF");
            }

            if (list.Count == 0)
            {
                AddRow(rows, string.Empty, "No snapshots stored.", "Open a player and use Snapshot.", false,
                    false, "sprite:sa_nav_snapshots");
            }

            buttons.Add(new ButtonInfo
            {
                Action = "input|snapshot_player|Player name or id|",
                Label = "Capture Player",
                Tooltip = "Take a manual snapshot of an online player.",
                Enabled = HasTool(playerId, PermSnapshot)
            });
            AddButton(buttons, "snap_filter|all", "Show All", true);
            AddButton(buttons, "act|snap_cleanup", "Delete Old", HasTool(playerId, PermSnapshots));
            AddButton(buttons, "refresh", "Refresh");
            AddPager(buttons, session, list.Count);
            AddButton(buttons, "back", "Back");
        }

        private void RenderSnapshotView(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            Snapshot snapshot = FindSnapshot(session.SelectedSnapshotId);
            subtitle = snapshot != null ? "Snapshot  |  " + snapshot.PlayerName : "Snapshot";

            if (snapshot == null)
            {
                AddRow(rows, string.Empty, "Snapshot not found.", "It may have been deleted or trimmed.", false,
                    false, "sprite:sa_nav_snapshots");
                AddButton(buttons, "back", "Back");
                return;
            }

            AddRow(rows, string.Empty, "Player", snapshot.PlayerName + "  |  " + snapshot.PlayerId, false,
                false, "sprite:sa_nav_players");
            AddRow(rows, string.Empty, "Created", FormatDateTime(snapshot.CreatedUtcTicks), false,
                false, "sprite:sa_act_time");
            AddRow(rows, string.Empty, "Reason", snapshot.Reason, false,
                false, "sprite:sa_nav_logs");
            AddRow(rows, string.Empty, "Position", FormatVector(snapshot.X, snapshot.Y, snapshot.Z), false,
                false, "sprite:sa_nav_teleport");
            AddRow(rows, string.Empty, "Vitals",
                "HP " + snapshot.Health + "  |  stamina " + (int)snapshot.Stamina + "  |  water " +
                (int)snapshot.Water + "  |  food " + (int)snapshot.Food, false,
                false, "sprite:sa_act_heal");
            AddRow(rows, string.Empty, "Items",
                SnapshotSectionSummary(snapshot), false,
                false, "sprite:sa_nav_items");

            AddButton(buttons, "act|snap_restore|" + snapshot.Id, "Restore",
                HasTool(playerId, PermRollback));
            AddButton(buttons, "act|snap_delete|" + snapshot.Id, "Delete",
                HasTool(playerId, PermSnapshots));
            AddButton(buttons, "back", "Back");
        }

        private void RenderServer(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            subtitle = "Server  |  " + GetServerName();

            AddRow(rows, string.Empty, "Server", GetServerName(), false,
                false, "sprite:sa_nav_server", "NAME", "#FFCC66");
            AddRow(rows, string.Empty, "World", GetServerWorld(), false,
                false, "sprite:sa_nav_world");
            AddRow(rows, string.Empty, "Mode / difficulty",
                GetGameMode() + "  |  difficulty " + GetDifficultyName(), false,
                false, "sprite:sa_nav_server", "MODE", "#CC99FF");
            AddRow(rows, string.Empty, "Players",
                players.Connected.Count() + " / " + GetMaxPlayers() + " online", false,
                false, "sprite:sa_nav_players", "ONLINE", "#66FF99");
            AddRow(rows, string.Empty, "World time", FormatWorldTime(), false,
                false, "sprite:sa_act_time");
            AddRow(rows, string.Empty, "Blood moon",
                "every " + SafePrefInt(EnumGamePrefs.BloodMoonFrequency) + " day(s)", false,
                false, "sprite:sa_nav_world", "MOON", "#FF6B6B");
            AddRow(rows, string.Empty, "Rates",
                "XP x" + SafePrefFloat(EnumGamePrefs.XPMultiplier) + "  |  loot x" +
                SafePrefFloat(EnumGamePrefs.LootAbundance), false,
                false, "sprite:sa_nav_world");
            AddRow(rows, string.Empty, "Bans / whitelist",
                GetBanList().Count + " bans  |  " + (IsWhitelistEnabled() ? "whitelist on" : "whitelist off"), false,
                false, "sprite:sa_act_ban");
            AddRow(rows, string.Empty, "Uptime", FormatDuration(DateTime.UtcNow - startedUtc), false,
                false, "sprite:sa_nav_server", "UP", "#66CCFF");
            AddRow(rows, string.Empty, "Memory", (GC.GetTotalMemory(false) / 1048576) + " MB managed heap", false,
                false, "sprite:sa_nav_server", "RAM", "#CCCCCC");

            AddButton(buttons, "act|save", "Save", HasTool(playerId, PermServer) || HasTool(playerId, PermWorld));
            AddButton(buttons, "act|reload", "Reload", HasTool(playerId, PermReload));
            AddButton(buttons, "input|announce|Message|", "Announce", HasTool(playerId, PermServer));
            AddButton(buttons, "input|restart_warning|Minutes|", "Warning", HasTool(playerId, PermServer));
            AddButton(buttons, "act|shutdown", "Shutdown", IsAdmin(playerId));
            AddButton(buttons, "back", "Back");
        }

        private void RenderHelp(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            subtitle = "Help  |  AdminSuite " + Version + "  |  Requires ServerAssist 2.0.2+";

            AddRow(rows, string.Empty, "How it works",
                "AdminSuite is an ServerAssist GUI hook. Open it from the [GUI] Admin Suite row or with /as.", false,
                false, "sprite:sa_admin_suite", "START", "#66CCFF");
            AddRow(rows, string.Empty, "Permissions",
                "adminsuite.use to open. adminsuite.admin grants everything. Granular permissions control each tool.", false,
                false, "sprite:sa_nav_moderation", "PERM", "#FFCC66");
            AddRow(rows, string.Empty, "Players",
                "Select a player to teleport, bring, spawn on top, heal, feed, god mode, spectate, kick, ban, mute, freeze, kill or inspect.", false,
                false, "sprite:sa_nav_players", "PLAYER", "#66FF99");
            AddRow(rows, string.Empty, "Inventory",
                "The inspector reads the live toolbelt, backpack and equipment. Take, repair or delete individual items.", false,
                false, "sprite:sa_nav_items", "GEAR", "#FFD700");
            AddRow(rows, string.Empty, "Snapshots",
                "Snapshots store inventory, equipment, position and vitals. Destructive actions take one automatically.", false,
                false, "sprite:sa_nav_snapshots", "SNAP", "#66CCFF");
            AddRow(rows, string.Empty, "Logs",
                "Chat, joins, leaves, deaths, commands and staff actions are kept in memory and in oxide/logs.", false,
                false, "sprite:sa_nav_logs", "LOG", "#CC99FF");
            AddRow(rows, string.Empty, "Commands",
                "/as, /as who <player>, /as tp <player>, /as give <player> <item> [amount] [quality], /as snapshot <player>, /as rollback <player> [id].", false,
                false, "sprite:sa_nav_server", "CMD", "#FFB347");
            AddRow(rows, string.Empty, "More commands",
                "/as time, /as weather, /as airdrop, /as horde, /as spawn <entity> [count], /as warps, /as logs, /as reload.", false,
                false, "sprite:sa_nav_server", "CMD", "#FFB347");
            AddRow(rows, string.Empty, "Safety",
                "Destructive actions ask for confirmation. Snapshots are taken before clear/take/rollback/kick/ban.", false,
                false, "sprite:sa_act_warn", "SAFE", "#FFD700");
            AddRow(rows, string.Empty, "Need help?",
                "Read AdminSuite/README.txt for the complete permission and command reference.", false,
                false, "sprite:sa_nav_help", "INFO", "#CCCCCC");

            AddButton(buttons, "refresh", "Refresh");
            AddButton(buttons, "back", "Back");
        }

        private void RenderConfirm(string playerId, Session session, List<RowInfo> rows, List<ButtonInfo> buttons, out string subtitle)
        {
            subtitle = session.PendingTitle;
            AddRow(rows, string.Empty, "Confirm action", session.PendingTitle, false,
                false, "sprite:sa_act_warn", "CONFIRM", "#FFD700");
            AddRow(rows, string.Empty, "Details", session.PendingMessage, false,
                false, "sprite:sa_nav_help");
            AddRow(rows, string.Empty, "Note",
                "Choose Confirm to continue or Cancel to return.", false,
                false, "sprite:sa_act_warn", "NOTE", "#CCCCCC");

            AddButton(buttons, "confirm_yes", "Confirm");
            AddButton(buttons, "confirm_no", "Cancel");
        }

        private static string LogIcon(string category)
        {
            switch ((category ?? string.Empty).ToLowerInvariant())
            {
                case "chat":
                    return "sprite:sa_nav_logs";
                case "join":
                case "leave":
                    return "sprite:sa_nav_players";
                case "death":
                    return "sprite:sa_act_ban";
                case "admin":
                    return "sprite:sa_nav_moderation";
                case "command":
                    return "sprite:sa_nav_server";
                default:
                    return "sprite:sa_nav_logs";
            }
        }

        private static string LogTint(string category)
        {
            switch ((category ?? string.Empty).ToLowerInvariant())
            {
                case "chat":
                    return "#66CCFF";
                case "join":
                case "leave":
                    return "#66FF99";
                case "death":
                    return "#FF6B6B";
                case "admin":
                    return "#FFCC66";
                case "command":
                    return "#CC99FF";
                default:
                    return "#CCCCCC";
            }
        }

        private static int PageCount(int count)
        {
            if (count <= 0)
            {
                return 1;
            }

            return (count + UiRows - 1) / UiRows;
        }

        private static void ClampPage(Session session, int count)
        {
            int pages = PageCount(count);
            if (session.Page < 0)
            {
                session.Page = 0;
            }
            else if (session.Page >= pages)
            {
                session.Page = pages - 1;
            }
        }

        private static void AddBreadcrumb(Session session, string screen)
        {
            // Reserved for future breadcrumb use.
        }

        #endregion

        #region Player actions

        private void TeleportToSelected(string actorId, Session session)
        {
            if (!HasTool(actorId, PermTeleport))
            {
                session.Status = "You need " + PermTeleport + ".";
                return;
            }

            EntityPlayer actor = GetEntity(actorId);
            EntityPlayer target = GetSelectedEntity(session);
            if (actor == null || target == null)
            {
                session.Status = "Both you and the selected player must be online and spawned.";
                return;
            }

            TeleportPlayerToPlayer(ResolvePlayer(actorId), GetSelectedPlayer(session));
            session.Status = "Teleported to " + GetSelectedName(session) + ".";
            AddLog("admin", GetPlayerName(actorId), GetSelectedName(session), "Teleported to player.");
        }

        private void BringSelected(string actorId, Session session)
        {
            if (!HasTool(actorId, PermBring))
            {
                session.Status = "You need " + PermBring + ".";
                return;
            }

            EntityPlayer actor = GetEntity(actorId);
            EntityPlayer target = GetSelectedEntity(session);
            if (actor == null || target == null)
            {
                session.Status = "Both you and the selected player must be online and spawned.";
                return;
            }

            if (!CanActOn(actorId, GetSelectedPlayer(session), false, out string reason))
            {
                session.Status = reason;
                return;
            }

            TeleportPlayerToPlayer(GetSelectedPlayer(session), ResolvePlayer(actorId));
            session.Status = "Brought " + GetSelectedName(session) + " to you.";
            AddLog("admin", GetPlayerName(actorId), GetSelectedName(session), "Brought player to self.");
        }

        private void SpawnOnTopSelected(string actorId, Session session)
        {
            if (!HasTool(actorId, PermSpawnOnTop))
            {
                session.Status = "You need " + PermSpawnOnTop + ".";
                return;
            }

            EntityPlayer actor = GetEntity(actorId);
            EntityPlayer target = GetSelectedEntity(session);
            if (actor == null || target == null)
            {
                session.Status = "Both you and the selected player must be online and spawned.";
                return;
            }

            Vector3 position = target.GetPosition() + new Vector3(0f, 3f, 0f);
            TeleportPlayerTo(ResolvePlayer(actorId), position, target.rotation.y);
            session.Status = "Teleported on top of " + GetSelectedName(session) + ".";
            AddLog("admin", GetPlayerName(actorId), GetSelectedName(session), "Spawned on top.");
        }

        private void SpectateSelected(string actorId, Session session)
        {
            if (!HasTool(actorId, PermSpectate))
            {
                session.Status = "You need " + PermSpectate + ".";
                return;
            }

            IPlayer actor = ResolvePlayer(actorId);
            EntityPlayer actorEntity = GetEntity(actor);
            EntityPlayer target = GetSelectedEntity(session);
            if (actor == null || actorEntity == null || target == null)
            {
                session.Status = "Both players must be online and spawned.";
                return;
            }

            if (actorEntity.IsSpectator)
            {
                if (SendClientCommand(actor, "spectator"))
                {
                    session.Status = "Stopped spectating.";
                }
                else
                {
                    session.Status = "Could not send the spectator command to your client.";
                }
                return;
            }

            TeleportPlayerTo(actor, target.GetPosition() + new Vector3(0f, 2f, 0f), target.rotation.y);

            if (!SendClientCommand(actor, "spectator"))
            {
                session.Status = "Teleported near the player, but could not enable spectator mode on your client.";
                return;
            }

            session.Status = "Spectating " + GetSelectedName(session) + ". Press the key again to exit spectator mode.";
            AddLog("admin", actor.Name, GetSelectedName(session), "Started spectating.");
        }

        private void StopSpectating(string actorId, Session session)
        {
            IPlayer actor = ResolvePlayer(actorId);
            if (actor == null)
            {
                session.Status = "Player not found.";
                return;
            }

            if (SendClientCommand(actor, "spectator"))
            {
                session.Status = "Toggled spectator mode.";
            }
            else
            {
                session.Status = "Could not send the spectator command to your client.";
            }
        }

        private void HealSelected(string actorId, Session session)
        {
            if (!HasTool(actorId, PermHeal))
            {
                session.Status = "You need " + PermHeal + ".";
                return;
            }

            EntityPlayer target = GetSelectedEntity(session);
            if (target == null)
            {
                session.Status = "The selected player is not online.";
                return;
            }

            target.Health = target.GetMaxHealth();
            target.Stamina = target.GetMaxStamina();
            target.Water = target.GetMaxWater();
            target.Stats.Food.Value = target.Stats.Food.Max;
            SyncStats(target);
            SendToolbeltMessage(GetSelectedPlayer(session), "You were healed by staff.");
            session.Status = "Healed " + GetSelectedName(session) + ".";
            AddLog("admin", GetPlayerName(actorId), GetSelectedName(session), "Healed player.");
        }

        private void FeedSelected(string actorId, Session session)
        {
            if (!HasTool(actorId, PermFeed))
            {
                session.Status = "You need " + PermFeed + ".";
                return;
            }

            EntityPlayer target = GetSelectedEntity(session);
            if (target == null)
            {
                session.Status = "The selected player is not online.";
                return;
            }

            target.Water = target.GetMaxWater();
            target.Stats.Food.Value = target.Stats.Food.Max;
            target.Stamina = target.GetMaxStamina();
            SyncStats(target);
            SendToolbeltMessage(GetSelectedPlayer(session), "You were fed by staff.");
            session.Status = "Fed " + GetSelectedName(session) + ".";
            AddLog("admin", GetPlayerName(actorId), GetSelectedName(session), "Fed player.");
        }

        private void ApplyBuff(string actorId, Session session, string buff)
        {
            if (!HasTool(actorId, PermBuffs))
            {
                session.Status = "You need " + PermBuffs + ".";
                return;
            }

            IPlayer target = GetSelectedPlayer(session);
            EntityPlayer entity = GetEntity(target);
            if (target == null || entity == null)
            {
                session.Status = "The selected player is not online.";
                return;
            }

            if (!CanActOn(actorId, target, false, out string reason))
            {
                session.Status = reason;
                return;
            }

            buff = buff == null ? string.Empty : buff.Trim();
            if (buff.Length == 0)
            {
                session.Status = "Enter a buff name.";
                return;
            }

            ExecuteConsole("buffplayer " + ConsoleTarget(target) + " " + Quote(buff));
            session.Status = "Applied buff '" + buff + "' to " + target.Name + ".";
            AddLog("admin", GetPlayerName(actorId), target.Name, "Applied buff " + buff + ".");
        }

        private void RemoveBuff(string actorId, Session session, string buff)
        {
            if (!HasTool(actorId, PermBuffs))
            {
                session.Status = "You need " + PermBuffs + ".";
                return;
            }

            IPlayer target = GetSelectedPlayer(session);
            EntityPlayer entity = GetEntity(target);
            if (target == null || entity == null)
            {
                session.Status = "The selected player is not online.";
                return;
            }

            if (!CanActOn(actorId, target, false, out string reason))
            {
                session.Status = reason;
                return;
            }

            buff = buff == null ? string.Empty : buff.Trim();
            if (buff.Length == 0)
            {
                session.Status = "Enter a buff name.";
                return;
            }

            ExecuteConsole("debuffplayer " + ConsoleTarget(target) + " " + Quote(buff));
            session.Status = "Removed buff '" + buff + "' from " + target.Name + ".";
            AddLog("admin", GetPlayerName(actorId), target.Name, "Removed buff " + buff + ".");
        }

        private void SetHealth(string actorId, Session session, string text)
        {
            if (!HasTool(actorId, PermHeal))
            {
                session.Status = "You need " + PermHeal + ".";
                return;
            }

            IPlayer target = GetSelectedPlayer(session);
            EntityPlayer entity = GetEntity(target);
            if (target == null || entity == null)
            {
                session.Status = "The selected player is not online.";
                return;
            }

            if (!CanActOn(actorId, target, false, out string reason))
            {
                session.Status = reason;
                return;
            }

            int value = ParseInt(text, -1);
            if (value <= 0)
            {
                session.Status = "Enter a positive health value.";
                return;
            }

            int max = entity.GetMaxHealth();
            value = Clamp(value, 1, max);
            entity.Health = value;
            SyncStats(entity);
            session.Status = "Set " + target.Name + "'s health to " + value + "/" + max + ".";
            AddLog("admin", GetPlayerName(actorId), target.Name, "Set health to " + value + ".");
        }

        private void SetFoodAndWater(string actorId, Session session, string text)
        {
            if (!HasTool(actorId, PermFeed))
            {
                session.Status = "You need " + PermFeed + ".";
                return;
            }

            IPlayer target = GetSelectedPlayer(session);
            EntityPlayer entity = GetEntity(target);
            if (target == null || entity == null)
            {
                session.Status = "The selected player is not online.";
                return;
            }

            if (!CanActOn(actorId, target, false, out string reason))
            {
                session.Status = reason;
                return;
            }

            int value = ParseInt(text, -1);
            if (value < 0)
            {
                session.Status = "Enter a food and water value.";
                return;
            }

            int maxFood = (int)entity.Stats.Food.Max;
            int maxWater = (int)entity.GetMaxWater();
            entity.Stats.Food.Value = Clamp(value, 0, maxFood);
            entity.Stats.Water.Value = Clamp(value, 0, maxWater);
            SyncStats(entity);
            session.Status = "Set " + target.Name + "'s food and water to " + value + ".";
            AddLog("admin", GetPlayerName(actorId), target.Name, "Set food/water to " + value + ".");
        }

        private void HealAll(string actorId, Session session)
        {
            if (!HasTool(actorId, PermHeal))
            {
                session.Status = "You need " + PermHeal + ".";
                return;
            }

            int count = 0;
            foreach (IPlayer player in players.Connected)
            {
                EntityPlayer entity = GetEntity(player);
                if (entity == null)
                {
                    continue;
                }

                entity.Health = entity.GetMaxHealth();
                entity.Stamina = entity.GetMaxStamina();
                entity.Water = entity.GetMaxWater();
                entity.Stats.Food.Value = entity.Stats.Food.Max;
                SyncStats(entity);
                count++;
            }

            session.Status = "Healed " + count + " online player(s).";
            AddLog("admin", GetPlayerName(actorId), "all players", "Healed all (" + count + ").");
        }

        private void FeedAll(string actorId, Session session)
        {
            if (!HasTool(actorId, PermFeed))
            {
                session.Status = "You need " + PermFeed + ".";
                return;
            }

            int count = 0;
            foreach (IPlayer player in players.Connected)
            {
                EntityPlayer entity = GetEntity(player);
                if (entity == null)
                {
                    continue;
                }

                entity.Water = entity.GetMaxWater();
                entity.Stats.Food.Value = entity.Stats.Food.Max;
                entity.Stamina = entity.GetMaxStamina();
                SyncStats(entity);
                count++;
            }

            session.Status = "Fed " + count + " online player(s).";
            AddLog("admin", GetPlayerName(actorId), "all players", "Fed all (" + count + ").");
        }

        private void ToggleGodSelected(string actorId, Session session)
        {
            if (!HasTool(actorId, PermGod))
            {
                session.Status = "You need " + PermGod + ".";
                return;
            }

            EntityPlayer target = GetSelectedEntity(session);
            if (target == null)
            {
                session.Status = "The selected player is not online.";
                return;
            }

            target.IsGodMode.Value = !target.IsGodMode.Value;
            bool synced = SyncEntityFlags(target);
            SendToolbeltMessage(GetSelectedPlayer(session),
                target.IsGodMode.Value ? "God mode enabled by staff." : "God mode disabled by staff.");
            session.Status = "God mode is now " + (target.IsGodMode.Value ? "ON" : "OFF") + " for " +
                             GetSelectedName(session) + "." +
                             (synced ? string.Empty : " The server flag was set, but the client sync failed.");
            AddLog("admin", GetPlayerName(actorId), GetSelectedName(session),
                "Toggled god mode " + (target.IsGodMode.Value ? "on" : "off") + ".");
        }

        private void ToggleFlySelected(string actorId, Session session)
        {
            if (!HasTool(actorId, PermFly))
            {
                session.Status = "You need " + PermFly + ".";
                return;
            }

            IPlayer targetPlayer = GetSelectedPlayer(session);
            EntityPlayer target = GetSelectedEntity(session);
            if (targetPlayer == null || target == null)
            {
                session.Status = "The selected player is not online.";
                return;
            }

            // In single-player / listen-server the server and client share the
            // same EntityPlayerLocal object, so we can toggle it directly.
            EntityPlayerLocal local = target as EntityPlayerLocal;
            if (local != null)
            {
                local.IsFlyMode.Value = !local.IsFlyMode.Value;
                SendToolbeltMessage(targetPlayer,
                    local.IsFlyMode.Value ? "Fly mode enabled by staff." : "Fly mode disabled by staff.");
                session.Status = "Fly mode is now " + (local.IsFlyMode.Value ? "ON" : "OFF") +
                                 " for " + targetPlayer.Name + ".";
                AddLog("admin", GetPlayerName(actorId), targetPlayer.Name,
                    "Toggled fly mode " + (local.IsFlyMode.Value ? "on" : "off") + ".");
                return;
            }

            if (!string.Equals(targetPlayer.Id, actorId, StringComparison.OrdinalIgnoreCase))
            {
                session.Status = "Fly is a client-side debug toggle. A server-only plugin cannot force it on " +
                                 targetPlayer.Name + ". Use the debug menu on their client.";
                return;
            }

            if (SendClientCommand(targetPlayer, "debugmenu"))
            {
                session.Status = "Fly is a client-side debug toggle. Opened the debug menu on your client; " +
                                 "use the Fly switch there.";
            }
            else
            {
                session.Status = "Fly is a client-side debug toggle. Open the debug menu on your client and use " +
                                 "the Fly switch.";
            }
        }

        private void KillSelected(string actorId, Session session)
        {
            if (!HasTool(actorId, PermKill))
            {
                session.Status = "You need " + PermKill + ".";
                return;
            }

            IPlayer target = GetSelectedPlayer(session);
            if (target == null || !target.IsConnected)
            {
                session.Status = "The selected player is not online.";
                return;
            }

            if (!CanActOn(actorId, target, true, out string reason))
            {
                session.Status = reason;
                return;
            }

            CaptureSnapshotIfPossible(GetSelectedEntity(session), "Before kill");
            ExecuteConsole("kill " + ConsoleTarget(target));
            session.Status = "Killed " + target.Name + ".";
            AddLog("admin", GetPlayerName(actorId), target.Name, "Killed player.");
        }

        private void ToggleFreezeSelected(string actorId, Session session)
        {
            if (!HasTool(actorId, PermFreeze))
            {
                session.Status = "You need " + PermFreeze + ".";
                return;
            }

            IPlayer target = GetSelectedPlayer(session);
            if (target == null)
            {
                session.Status = "Player not found.";
                return;
            }

            FreezeEntry existing = GetActiveFreeze(target.Id);
            if (existing != null)
            {
                RemoveFreeze(target.Id, true);
                session.Status = "Unfroze " + target.Name + ".";
                return;
            }

            if (GetEntity(target) == null)
            {
                session.Status = "The selected player is not online.";
                return;
            }

            if (!CanActOn(actorId, target, true, out string reason))
            {
                session.Status = reason;
                return;
            }

            ApplyFreeze(target, config.DefaultFreezeMinutes, "Frozen by staff", GetPlayerName(actorId));
            session.Status = "Froze " + target.Name + ".";
        }

        private void UnfreezeSelected(string actorId, Session session)
        {
            IPlayer target = GetSelectedPlayer(session);
            if (target == null)
            {
                session.Status = "Player not found.";
                return;
            }

            RemoveFreeze(target.Id, true);
            session.Status = "Unfroze " + target.Name + ".";
        }

        private void JailSelected(string actorId, Session session)
        {
            if (!HasTool(actorId, PermFreeze))
            {
                session.Status = "You need " + PermFreeze + ".";
                return;
            }

            if (config == null || !config.EnableJail)
            {
                session.Status = "Jail is disabled. Set EnableJail to true and configure JailX, JailY and JailZ.";
                return;
            }

            IPlayer target = GetSelectedPlayer(session);
            EntityPlayer entity = GetEntity(target);
            if (target == null || entity == null)
            {
                session.Status = "The selected player is not online.";
                return;
            }

            if (!CanActOn(actorId, target, true, out string reason))
            {
                session.Status = reason;
                return;
            }

            TeleportPlayerTo(target, new Vector3(config.JailX, config.JailY, config.JailZ), entity.rotation.y);
            ApplyFreezeAt(target, config.JailX, config.JailY, config.JailZ, entity.rotation.y, config.DefaultFreezeMinutes,
                "Jailed by staff", GetPlayerName(actorId));
            session.Status = "Jailed and froze " + target.Name + ".";
            AddLog("admin", GetPlayerName(actorId), target.Name, "Jailed player.");
        }

        #endregion

        #region Moderation actions

        private void KickSelected(string actorId, Session session)
        {
            if (!HasTool(actorId, PermKick))
            {
                session.Status = "You need " + PermKick + ".";
                return;
            }

            IPlayer target = GetSelectedPlayer(session);
            if (target == null || !target.IsConnected)
            {
                session.Status = "The selected player is not online.";
                return;
            }

            if (!CanActOn(actorId, target, true, out string reason))
            {
                session.Status = reason;
                return;
            }

            string kickReason = ParseReasonFromPending(session, "Kicked by staff");
            CaptureSnapshotIfPossible(GetSelectedEntity(session), "Before kick");
            target.Kick(kickReason);
            session.Status = "Kicked " + target.Name + ".";
            AddLog("kick", GetPlayerName(actorId), target.Name, kickReason);
            NotifyStaff(target.Name + " was kicked by " + GetPlayerName(actorId) + ".", actorId);
        }

        private void BanSelected(string actorId, Session session)
        {
            if (!HasTool(actorId, PermBan))
            {
                session.Status = "You need " + PermBan + ".";
                return;
            }

            IPlayer target = GetSelectedPlayer(session);
            if (target == null)
            {
                session.Status = "Player not found.";
                return;
            }

            if (!CanActOn(actorId, target, true, out string reason))
            {
                session.Status = reason;
                return;
            }

            string[] pending = (session.PendingData ?? string.Empty).Split('\n');
            string spec = pending.Length > 0 ? pending[0] : "1d";
            string banReason = pending.Length > 1 ? pending[1] : string.Empty;
            if (string.IsNullOrEmpty(banReason))
            {
                banReason = "Banned by staff";
            }

            long minutes = ParseDurationSpecMinutes(spec);
            if (minutes == long.MinValue)
            {
                session.Status = "Invalid ban duration '" + spec + "'.";
                return;
            }

            DateTime until = minutes == 0 ? DateTime.MaxValue : DateTime.UtcNow.AddMinutes(minutes);
            CaptureSnapshotIfPossible(GetSelectedEntity(session), "Before ban");
            string error = AddBan(target, until, banReason);
            if (error != null)
            {
                session.Status = error;
                return;
            }

            session.Status = "Banned " + target.Name + " (" + FormatBanDuration(until) + ").";
            AddLog("ban", GetPlayerName(actorId), target.Name,
                FormatBanDuration(until) + " - " + banReason);
            NotifyStaff(target.Name + " was banned by " + GetPlayerName(actorId) + " (" +
                        FormatBanDuration(until) + ").", actorId);
        }

        private void BanCustom(string actorId, Session session, string text)
        {
            string[] parts = (text ?? string.Empty).Split(new[] { ' ' }, 2);
            string spec = parts.Length > 0 ? parts[0] : "1d";
            string reason = parts.Length > 1 ? parts[1] : string.Empty;
            session.PendingData = spec + "\n" + reason;
            BanSelected(actorId, session);
        }

        private void MuteSelected(string actorId, Session session)
        {
            if (!HasTool(actorId, PermMute))
            {
                session.Status = "You need " + PermMute + ".";
                return;
            }

            IPlayer target = GetSelectedPlayer(session);
            if (target == null)
            {
                session.Status = "Player not found.";
                return;
            }

            if (!CanActOn(actorId, target, true, out string reason))
            {
                session.Status = reason;
                return;
            }

            string[] pending = (session.PendingData ?? string.Empty).Split('\n');
            string spec = pending.Length > 0 ? pending[0] : "30m";
            string muteReason = pending.Length > 1 ? pending[1] : string.Empty;
            if (string.IsNullOrEmpty(muteReason))
            {
                muteReason = "Muted by staff";
            }

            long minutes = ParseDurationSpecMinutes(spec);
            if (minutes == long.MinValue)
            {
                session.Status = "Invalid mute duration '" + spec + "'.";
                return;
            }

            ApplyMute(target, minutes, muteReason, GetPlayerName(actorId));
            session.Status = "Muted " + target.Name + " (" + (minutes == 0 ? "permanent" : FormatMinutes(minutes)) + ").";
        }

        private void MuteCustom(string actorId, Session session, string text)
        {
            string[] parts = (text ?? string.Empty).Split(new[] { ' ' }, 2);
            string spec = parts.Length > 0 ? parts[0] : "30m";
            string reason = parts.Length > 1 ? parts[1] : string.Empty;
            session.PendingData = spec + "\n" + reason;
            MuteSelected(actorId, session);
        }

        private void TimeoutSelected(string actorId, Session session)
        {
            if (!HasTool(actorId, PermMute) || !HasTool(actorId, PermFreeze))
            {
                session.Status = "You need " + PermMute + " and " + PermFreeze + ".";
                return;
            }

            IPlayer target = GetSelectedPlayer(session);
            if (target == null)
            {
                session.Status = "Player not found.";
                return;
            }

            if (GetEntity(target) == null)
            {
                session.Status = "The selected player is not online.";
                return;
            }

            if (!CanActOn(actorId, target, true, out string reason))
            {
                session.Status = reason;
                return;
            }

            string[] pending = (session.PendingData ?? string.Empty).Split('\n');
            string spec = pending.Length > 0 ? pending[0] : "30m";
            string timeoutReason = pending.Length > 1 ? pending[1] : string.Empty;
            if (string.IsNullOrEmpty(timeoutReason))
            {
                timeoutReason = "Timeout by staff";
            }

            long minutes = ParseDurationSpecMinutes(spec);
            if (minutes == long.MinValue || minutes == 0)
            {
                session.Status = "Timeouts need a finite duration such as 30m, 1h or 1d.";
                return;
            }

            ApplyMute(target, minutes, timeoutReason, GetPlayerName(actorId));
            ApplyFreeze(target, minutes, timeoutReason, GetPlayerName(actorId));
            session.Status = "Timed out " + target.Name + " for " + FormatMinutes(minutes) + ".";
            AddLog("admin", GetPlayerName(actorId), target.Name,
                "Timed out for " + FormatMinutes(minutes) + ": " + timeoutReason);
        }

        private void TimeoutCustom(string actorId, Session session, string text)
        {
            string[] parts = (text ?? string.Empty).Split(new[] { ' ' }, 2);
            string spec = parts.Length > 0 ? parts[0] : "30m";
            string reason = parts.Length > 1 ? parts[1] : string.Empty;
            session.PendingData = spec + "\n" + reason;
            TimeoutSelected(actorId, session);
        }

        private void WarnSelected(string actorId, Session session, string message)
        {
            if (!HasTool(actorId, PermWarn))
            {
                session.Status = "You need " + PermWarn + ".";
                return;
            }

            IPlayer target = GetSelectedPlayer(session);
            if (target == null)
            {
                session.Status = "Player not found.";
                return;
            }

            if (string.IsNullOrEmpty(message))
            {
                session.Status = "The warning message cannot be empty.";
                return;
            }

            WarningEntry entry = new WarningEntry
            {
                PlayerName = target.Name,
                Message = message,
                By = GetPlayerName(actorId),
                CreatedUtcTicks = DateTime.UtcNow.Ticks
            };

            List<WarningEntry> list;
            if (!data.Warnings.TryGetValue(target.Id, out list) || list == null)
            {
                list = new List<WarningEntry>();
                data.Warnings[target.Id] = list;
            }

            list.Add(entry);
            dataDirty = true;
            SaveData();
            SendToolbeltMessage(target, "Warning: " + message);
            Reply(target, "You received a warning from staff: " + message);
            session.Status = "Warned " + target.Name + ".";
            AddLog("warn", GetPlayerName(actorId), target.Name, message);
        }

        private void WarnAny(string actorId, Session session, string text)
        {
            string[] parts = (text ?? string.Empty).Split(new[] { ';', '|' }, 2);
            if (parts.Length < 2)
            {
                session.Status = "Use: player name or id ; warning message";
                return;
            }

            IPlayer target = ResolvePlayer(parts[0].Trim());
            if (target == null)
            {
                session.Status = "Player not found.";
                return;
            }

            session.SelectedPlayerId = target.Id;
            session.SelectedPlayerName = target.Name;
            WarnSelected(actorId, session, parts[1].Trim());
        }

        private void NoteSelected(string actorId, Session session, string text)
        {
            if (!HasTool(actorId, PermNotes))
            {
                session.Status = "You need " + PermNotes + ".";
                return;
            }

            IPlayer target = GetSelectedPlayer(session);
            if (target == null)
            {
                session.Status = "Player not found.";
                return;
            }

            if (string.IsNullOrEmpty(text))
            {
                data.Notes.Remove(target.Id);
                session.Status = "Cleared the note for " + target.Name + ".";
                AddLog("admin", GetPlayerName(actorId), target.Name, "Cleared staff note.");
            }
            else
            {
                data.Notes[target.Id] = new PlayerNote
                {
                    Text = text,
                    By = GetPlayerName(actorId),
                    UpdatedUtcTicks = DateTime.UtcNow.Ticks
                };
                session.Status = "Saved a note for " + target.Name + ".";
                AddLog("admin", GetPlayerName(actorId), target.Name, "Set staff note: " + text);
            }

            dataDirty = true;
            SaveData();
        }

        private void KickAll(string actorId, Session session)
        {
            if (!HasTool(actorId, PermKickAll))
            {
                session.Status = "You need " + PermKickAll + ".";
                return;
            }

            string reason = ParseReasonFromPending(session, "Kicked by staff");
            ExecuteConsole("kickall " + Quote(reason));
            session.Status = "Kicked all players.";
            AddLog("kick", GetPlayerName(actorId), "all players", reason);
        }

        private void ClearMutes(string actorId, Session session)
        {
            if (!HasTool(actorId, PermMute))
            {
                session.Status = "You need " + PermMute + ".";
                return;
            }

            int count = data.Mutes.Count;
            data.Mutes.Clear();
            SaveData();
            session.Status = "Cleared " + count + " mute(s).";
            AddLog("admin", GetPlayerName(actorId), "all players", "Cleared all mutes.");
        }

        private void ClearWarnings(string actorId, Session session)
        {
            if (!HasTool(actorId, PermWarn))
            {
                session.Status = "You need " + PermWarn + ".";
                return;
            }

            int count = CountWarnings();
            data.Warnings.Clear();
            SaveData();
            session.Status = "Cleared " + count + " warning(s).";
            AddLog("admin", GetPlayerName(actorId), "all players", "Cleared all warnings.");
        }

        private void ApplyMute(IPlayer target, long minutes, string reason, string by)
        {
            if (target == null)
            {
                return;
            }

            MuteEntry entry = new MuteEntry
            {
                PlayerName = target.Name,
                Reason = reason,
                By = by,
                UntilUtcTicks = minutes == 0 ? 0L : DateTime.UtcNow.AddMinutes(minutes).Ticks
            };

            data.Mutes[target.Id] = entry;
            SaveData();
            SendToolbeltMessage(target, "You are muted: " + reason);
            AddLog("mute", by, target.Name,
                (minutes == 0 ? "Permanent" : FormatMinutes(minutes)) + " - " + reason);
        }

        private void ApplyFreeze(IPlayer target, long minutes, string reason, string by)
        {
            EntityPlayer entity = GetEntity(target);
            if (target == null || entity == null)
            {
                return;
            }

            Vector3 position = entity.GetPosition();
            ApplyFreezeAt(target, position.x, position.y, position.z, entity.rotation.y, minutes, reason, by);
        }

        private void ApplyFreezeAt(IPlayer target, float x, float y, float z, float rotY, long minutes, string reason, string by)
        {
            if (target == null)
            {
                return;
            }

            FreezeEntry entry = new FreezeEntry
            {
                PlayerName = target.Name,
                X = x,
                Y = y,
                Z = z,
                RotY = rotY,
                Reason = reason,
                By = by,
                UntilUtcTicks = minutes <= 0 ? 0L : DateTime.UtcNow.AddMinutes(minutes).Ticks
            };

            data.Freezes[target.Id] = entry;
            SaveData();
            StartFreezeTimer();
            SendToolbeltMessage(target, "You are frozen: " + reason);
            AddLog("freeze", by, target.Name,
                (minutes <= 0 ? "Indefinite" : FormatMinutes(minutes)) + " - " + reason);
        }

        private void RemoveMute(string playerId, bool notify)
        {
            MuteEntry entry;
            if (!data.Mutes.TryGetValue(playerId, out entry))
            {
                return;
            }

            data.Mutes.Remove(playerId);
            SaveData();

            if (notify)
            {
                IPlayer player = ResolvePlayer(playerId);
                if (player != null && player.IsConnected)
                {
                    SendToolbeltMessage(player, "Your mute was lifted.");
                    Reply(player, "Your mute was lifted by staff.");
                }
            }
        }

        private void RemoveFreeze(string playerId, bool notify)
        {
            FreezeEntry entry;
            if (!data.Freezes.TryGetValue(playerId, out entry))
            {
                return;
            }

            data.Freezes.Remove(playerId);
            SaveData();

            if (notify)
            {
                IPlayer player = ResolvePlayer(playerId);
                if (player != null && player.IsConnected)
                {
                    SendToolbeltMessage(player, "You were unfrozen.");
                    Reply(player, "You were unfrozen by staff.");
                }
            }
        }

        private void UnmuteById(string actorId, Session session, string playerId)
        {
            if (!HasTool(actorId, PermMute))
            {
                session.Status = "You need " + PermMute + ".";
                return;
            }

            RemoveMute(playerId, true);
            session.Status = "Unmuted " + GetPlayerName(playerId) + ".";
            AddLog("admin", GetPlayerName(actorId), GetPlayerName(playerId), "Removed mute.");
        }

        private void RemoveFreezeById(string actorId, Session session, string playerId)
        {
            if (!HasTool(actorId, PermFreeze))
            {
                session.Status = "You need " + PermFreeze + ".";
                return;
            }

            RemoveFreeze(playerId, true);
            session.Status = "Unfroze " + GetPlayerName(playerId) + ".";
            AddLog("admin", GetPlayerName(actorId), GetPlayerName(playerId), "Removed freeze.");
        }

        private void UnbanByIndex(string actorId, Session session, string indexText)
        {
            if (!HasTool(actorId, PermBan))
            {
                session.Status = "You need " + PermBan + ".";
                return;
            }

            int index = ParseInt(indexText, -1);
            List<AdminBlacklist.BannedUser> list = GetBanList();
            if (index < 0 || index >= list.Count)
            {
                session.Status = "That ban entry no longer exists.";
                return;
            }

            AdminBlacklist.BannedUser ban = list[index];
            if (ban.UserIdentifier == null)
            {
                session.Status = "That ban entry has no usable identifier.";
                return;
            }

            AdminTools tools = GetAdminTools();
            if (tools == null || tools.Blacklist == null)
            {
                session.Status = "The server admin tools are not available.";
                return;
            }

            tools.Blacklist.RemoveBan(ban.UserIdentifier);
            tools.Save();
            session.Status = "Removed ban for " + (string.IsNullOrEmpty(ban.Name) ? ban.UserIdentifier.ToString() : ban.Name) + ".";
            AddLog("unban", GetPlayerName(actorId), ban.Name, "Removed ban.");
        }

        #endregion

        #region Teleport actions

        private void TeleportSelfToCoords(string actorId, Session session, string text)
        {
            if (!HasTool(actorId, PermTeleport))
            {
                session.Status = "You need " + PermTeleport + ".";
                return;
            }

            IPlayer actor = ResolvePlayer(actorId);
            EntityPlayer entity = GetEntity(actor);
            if (entity == null)
            {
                session.Status = "You must be spawned in the world to teleport.";
                return;
            }

            float x;
            float y;
            float z;
            if (!ParseCoordinates(text, entity.GetPosition(), out x, out y, out z))
            {
                session.Status = "Use X Y Z or X Z.";
                return;
            }

            TeleportPlayerTo(ResolvePlayer(actorId), new Vector3(x, y, z), entity.rotation.y);
            session.Status = "Teleported to " + FormatVector(x, y, z) + ".";
            AddLog("admin", actor != null ? actor.Name : actorId, string.Empty,
                "Teleported to " + FormatVector(x, y, z) + ".");
        }

        private void TeleportSelectedToCoords(string actorId, Session session, string text)
        {
            if (!HasTool(actorId, PermTeleport))
            {
                session.Status = "You need " + PermTeleport + ".";
                return;
            }

            EntityPlayer target = GetSelectedEntity(session);
            if (target == null)
            {
                session.Status = "The selected player is not online.";
                return;
            }

            if (!CanActOn(actorId, GetSelectedPlayer(session), false, out string reason))
            {
                session.Status = reason;
                return;
            }

            float x;
            float y;
            float z;
            if (!ParseCoordinates(text, target.GetPosition(), out x, out y, out z))
            {
                session.Status = "Use X Y Z or X Z.";
                return;
            }

            TeleportPlayerTo(GetSelectedPlayer(session), new Vector3(x, y, z), target.rotation.y);
            session.Status = "Sent " + GetSelectedName(session) + " to " + FormatVector(x, y, z) + ".";
            AddLog("admin", GetPlayerName(actorId), GetSelectedName(session),
                "Teleported to " + FormatVector(x, y, z) + ".");
        }

        private void TeleportSelectedToPlayer(string actorId, Session session, string text)
        {
            if (!HasTool(actorId, PermTeleport))
            {
                session.Status = "You need " + PermTeleport + ".";
                return;
            }

            EntityPlayer target = GetSelectedEntity(session);
            IPlayer destinationPlayer = ResolvePlayer(text);
            EntityPlayer destination = GetEntity(destinationPlayer);
            if (target == null || destination == null)
            {
                session.Status = "Both players must be online and spawned.";
                return;
            }

            if (!CanActOn(actorId, GetSelectedPlayer(session), false, out string reason))
            {
                session.Status = reason;
                return;
            }

            TeleportPlayerToPlayer(GetSelectedPlayer(session), destinationPlayer);
            session.Status = "Sent " + GetSelectedName(session) + " to " + destinationPlayer.Name + ".";
            AddLog("admin", GetPlayerName(actorId), GetSelectedName(session),
                "Teleported to player " + destinationPlayer.Name + ".");
        }

        private void SetHome(string actorId, Session session)
        {
            if (!HasTool(actorId, PermTeleport))
            {
                session.Status = "You need " + PermTeleport + ".";
                return;
            }

            EntityPlayer entity = GetEntity(actorId);
            if (entity == null)
            {
                session.Status = "You must be spawned in the world.";
                return;
            }

            Vector3 position = entity.GetPosition();
            data.Homes[actorId] = new HomeEntry
            {
                X = position.x,
                Y = position.y,
                Z = position.z,
                RotY = entity.rotation.y,
                CreatedUtcTicks = DateTime.UtcNow.Ticks
            };
            SaveData();
            session.Status = "Saved your home at " + FormatVector(position) + ".";
        }

        private void GoHome(string actorId, Session session)
        {
            if (!HasTool(actorId, PermTeleport))
            {
                session.Status = "You need " + PermTeleport + ".";
                return;
            }

            HomeEntry home;
            if (!data.Homes.TryGetValue(actorId, out home) || home == null)
            {
                session.Status = "You have not set a home yet.";
                return;
            }

            EntityPlayer entity = GetEntity(actorId);
            if (entity == null)
            {
                session.Status = "You must be spawned in the world.";
                return;
            }

            TeleportPlayerTo(ResolvePlayer(actorId), new Vector3(home.X, home.Y, home.Z), home.RotY);
            session.Status = "Teleported home.";
        }

        private void SetWarp(string actorId, Session session, string text)
        {
            if (!HasTool(actorId, PermWarps))
            {
                session.Status = "You need " + PermWarps + ".";
                return;
            }

            string name = SanitizeName(text, 24);
            if (string.IsNullOrEmpty(name))
            {
                session.Status = "Warp names may only use letters, numbers, underscore and dash.";
                return;
            }

            if (data.Warps.Count >= config.MaxWarps && !data.Warps.ContainsKey(name))
            {
                session.Status = "The warp list is full (" + data.Warps.Count + "/" + config.MaxWarps + ").";
                return;
            }

            EntityPlayer entity = GetEntity(actorId);
            if (entity == null)
            {
                session.Status = "You must be spawned in the world.";
                return;
            }

            Vector3 position = entity.GetPosition();
            data.Warps[name] = new WarpEntry
            {
                Name = name,
                X = position.x,
                Y = position.y,
                Z = position.z,
                CreatedBy = GetPlayerName(actorId),
                CreatedUtcTicks = DateTime.UtcNow.Ticks
            };
            SaveData();
            session.Status = "Saved warp '" + name + "' at " + FormatVector(position) + ".";
            AddLog("admin", GetPlayerName(actorId), string.Empty, "Set warp '" + name + "'.");
        }

        private void DeleteWarp(string actorId, Session session, string text)
        {
            if (!HasTool(actorId, PermWarps))
            {
                session.Status = "You need " + PermWarps + ".";
                return;
            }

            string name = SanitizeName(text, 24);
            if (!data.Warps.Remove(name))
            {
                session.Status = "Warp '" + name + "' was not found.";
                return;
            }

            SaveData();
            session.Status = "Deleted warp '" + name + "'.";
            AddLog("admin", GetPlayerName(actorId), string.Empty, "Deleted warp '" + name + "'.");
        }

        private void TeleportToWarp(string actorId, Session session, string name)
        {
            if (!HasTool(actorId, PermWarps) && !HasTool(actorId, PermTeleport))
            {
                session.Status = "You do not have permission to use warps.";
                return;
            }

            WarpEntry warp;
            if (!data.Warps.TryGetValue(name, out warp) || warp == null)
            {
                session.Status = "Warp '" + name + "' was not found.";
                return;
            }

            EntityPlayer entity = GetEntity(actorId);
            if (entity == null)
            {
                session.Status = "You must be spawned in the world.";
                return;
            }

            TeleportPlayerTo(ResolvePlayer(actorId), new Vector3(warp.X, warp.Y, warp.Z), entity.rotation.y);
            session.Status = "Warped to '" + name + "'.";
            AddLog("admin", GetPlayerName(actorId), string.Empty, "Warped to '" + name + "'.");
        }

        #endregion

        #region Inventory actions

        private void GiveItemToSelected(string actorId, Session session, string text)
        {
            if (!HasTool(actorId, PermGive))
            {
                session.Status = "You need " + PermGive + ".";
                return;
            }

            EntityPlayer target = GetSelectedEntity(session);
            if (target == null)
            {
                session.Status = "The selected player is not online.";
                return;
            }

            GiveItemSpec(target, session, text, GetSelectedName(session));
        }

        private void GiveItemToSelf(string actorId, Session session, string text)
        {
            if (!HasTool(actorId, PermGive))
            {
                session.Status = "You need " + PermGive + ".";
                return;
            }

            EntityPlayer target = GetEntity(actorId);
            if (target == null)
            {
                session.Status = "You must be spawned in the world.";
                return;
            }

            GiveItemSpec(target, session, text, GetPlayerName(actorId));
        }

        private void GiveItemToSelectedOrSelf(string actorId, Session session, string itemName, int count, int quality)
        {
            if (!HasTool(actorId, PermGive))
            {
                session.Status = "You need " + PermGive + ".";
                return;
            }

            EntityPlayer target = GetSelectedEntity(session);
            if (target == null)
            {
                target = GetEntity(actorId);
            }

            if (target == null)
            {
                session.Status = "No online target and you are not spawned.";
                return;
            }

            GiveItemSpec(target, session, itemName + " " + count + " " + quality,
                target.PlayerDisplayName);
        }

        private void GiveItemSpec(EntityPlayer target, Session session, string text, string targetName)
        {
            if (target == null)
            {
                session.Status = "The target player is not online.";
                return;
            }

            string itemName;
            int count;
            int quality;
            if (!ParseGiveSpec(text, out itemName, out count, out quality))
            {
                session.Status = "Use: item [amount] [quality]";
                return;
            }

            count = Clamp(count, 1, 10000);
            if (quality <= 0)
            {
                quality = config.DefaultItemQuality;
            }

            quality = Clamp(quality, 1, 6);

            ItemClass item = FindItemClass(itemName);
            if (item == null)
            {
                session.Status = "Item '" + itemName + "' was not found. Use the item search.";
                return;
            }

            // The client owns its inventory. The reliable way to give an item is to
            // ask the target client to run the same command an admin would type in
            // its local console. This also makes the item appear immediately and
            // syncs it back to the server.
            IPlayer targetPlayer = ResolvePlayer(GetPlayerIdForEntity(target));
            if (targetPlayer != null && targetPlayer.IsConnected)
            {
                bool fits = CanPlayerReceive(target, item, count);
                string command = "giveself " + Quote(item.Name) + " " +
                                 quality.ToString(CultureInfo.InvariantCulture) + " " +
                                 count.ToString(CultureInfo.InvariantCulture) + " " +
                                 (fits ? "true" : "false") + " true";

                if (SendClientCommand(targetPlayer, command))
                {
                    session.Status = fits
                        ? "Gave " + count + "x " + ItemDisplayName(item) + " to " + targetName + "."
                        : "Inventory full: dropped " + count + "x " + ItemDisplayName(item) +
                          " at " + targetName + "'s feet.";

                    SendToolbeltMessage(targetPlayer, fits
                        ? "You received " + count + "x " + ItemDisplayName(item) + " from staff."
                        : "You received " + count + "x " + ItemDisplayName(item) + " from staff (dropped).");
                    AddLog("admin", "staff", targetName,
                        "Gave " + count + "x " + item.Name + (fits ? "." : " (dropped)."));
                    return;
                }
            }

            // Fallback for a client we cannot message (or a missing client mod):
            // use the server-side inventory path below and drop any remainder.
            ItemValue baseValue = ItemClass.CreateItemValue(item.Name, quality, true);
            if (baseValue == null || baseValue.IsEmpty())
            {
                baseValue = new ItemValue(item.Id, true);
            }

            if (item.HasQuality)
            {
                baseValue.Quality = (ushort)quality;
            }

            int maxStack = GetMaxStack(item);
            int remaining = count;
            int addedTotal = 0;

            while (remaining > 0)
            {
                int amount = Math.Min(remaining, maxStack);
                ItemStack chunk = new ItemStack(baseValue.Clone(), amount);
                int added = AddItemToPlayer(target, chunk);
                if (added <= 0)
                {
                    break;
                }

                addedTotal += added;
                remaining -= added;

                if (added < amount)
                {
                    break;
                }
            }

            int missing = count - addedTotal;
            bool dropped = false;
            if (missing > 0)
            {
                IPlayer dropTarget = ResolvePlayer(GetPlayerIdForEntity(target));
                if (dropTarget != null)
                {
                    ExecuteConsole("give " + ConsoleTarget(dropTarget) + " " + Quote(item.Name) + " " +
                                   missing.ToString(CultureInfo.InvariantCulture) + " " +
                                   quality.ToString(CultureInfo.InvariantCulture));
                    dropped = true;
                }
            }

            if (addedTotal <= 0)
            {
                session.Status = dropped
                    ? "Inventory full: dropped " + count + "x " + ItemDisplayName(item) + " at " +
                      targetName + "'s feet."
                    : targetName + " does not have enough inventory space for " +
                      count + "x " + ItemDisplayName(item) + ".";
                if (dropped)
                {
                    SendToolbeltMessage(ResolvePlayer(GetPlayerIdForEntity(target)),
                        "You received " + count + "x " + ItemDisplayName(item) + " from staff (dropped).");
                    AddLog("admin", "staff", targetName, "Dropped " + count + "x " + item.Name + ".");
                }
                return;
            }

            if (missing > 0 && dropped)
            {
                session.Status = "Gave " + addedTotal + "x and dropped " + missing + "x " +
                                 ItemDisplayName(item) + " at " + targetName + "'s feet (inventory full).";
            }
            else if (addedTotal < count)
            {
                session.Status = "Gave " + addedTotal + " of " + count + "x " + ItemDisplayName(item) +
                                 " to " + targetName + " (inventory full).";
            }
            else
            {
                session.Status = "Gave " + count + "x " + ItemDisplayName(item) + " to " + targetName + ".";
            }

            SendToolbeltMessage(ResolvePlayer(GetPlayerIdForEntity(target)),
                "You received " + addedTotal + "x " + ItemDisplayName(item) + " from staff.");
            AddLog("admin", "staff", targetName, "Gave " + addedTotal + "x " + item.Name +
                   (dropped ? " (dropped remaining)" : string.Empty) + ".");
        }

        private static bool ParseGiveSpec(string text, out string itemName, out int count, out int quality)
        {
            itemName = string.Empty;
            count = 1;
            quality = 0;

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }

            string[] parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int length = parts.Length;
            int parsedAmount;
            int parsedQuality;

            if (length >= 3 &&
                int.TryParse(parts[length - 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedQuality) &&
                int.TryParse(parts[length - 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedAmount) &&
                parsedQuality >= 1 && parsedQuality <= 6)
            {
                itemName = string.Join(" ", parts, 0, length - 2);
                count = parsedAmount;
                quality = parsedQuality;
                return itemName.Length > 0;
            }

            if (length >= 2 &&
                int.TryParse(parts[length - 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedAmount))
            {
                itemName = string.Join(" ", parts, 0, length - 1);
                count = parsedAmount;
                return itemName.Length > 0;
            }

            itemName = trimmed;
            return true;
        }

        private void OpenInventoryItem(string actorId, Session session, string section, int index)
        {
            if (!HasTool(actorId, PermInventory))
            {
                session.Status = "You need " + PermInventory + ".";
                return;
            }

            if (!string.Equals(session.Screen, "inventory_item", StringComparison.OrdinalIgnoreCase))
            {
                session.History.Add(session.Screen);
            }

            session.InventorySection = NormalizeInventorySection(section);
            session.InventoryIndex = index;
            session.Screen = "inventory_item";
            session.Page = 0;
            session.Status = string.Empty;
            ClearInput(session);
        }

        private void TakeInventoryItem(string actorId, Session session, string section, int index)
        {
            if (!HasTool(actorId, PermTakeInventory))
            {
                session.Status = "You need " + PermTakeInventory + ".";
                return;
            }

            EntityPlayer actor = GetEntity(actorId);
            EntityPlayer target = GetSelectedEntity(session);
            if (actor == null || target == null)
            {
                session.Status = "Both players must be online and spawned.";
                return;
            }

            if (!CanActOn(actorId, GetSelectedPlayer(session), true, out string reason))
            {
                session.Status = reason;
                return;
            }

            InvItem item = GetInventoryItem(target, section, index);
            if (item == null)
            {
                session.Status = "Item not found.";
                return;
            }

            CaptureSnapshotIfPossible(target, "Before take item");
            ItemStack stack = CreateStackFromInvItem(item);
            if (stack == null)
            {
                session.Status = "Could not read that item.";
                return;
            }

            int added = AddItemToPlayer(actor, stack);
            if (added <= 0)
            {
                session.Status = "Your inventory is full.";
                return;
            }

            string normalized = NormalizeInventorySection(section);
            if (added >= item.Count)
            {
                RemoveInventoryItem(target, normalized, index);
                session.Status = "Took " + item.Name + " x" + item.Count +
                                 " (server-side; the client may need to reopen the inventory or relog).";
            }
            else
            {
                SetInventoryItemCount(target, normalized, index, item.Count - added);
                session.Status = "Took " + added + " of " + item.Count + "x " + item.Name +
                                 " (your inventory is full; server-side, the client may need to reopen the inventory or relog).";
            }

            AddLog("admin", GetPlayerName(actorId), target.PlayerDisplayName,
                "Took " + added + " of " + item.Count + "x " + item.InternalName + ".");
            session.Screen = "inventory";
        }

        private void RepairInventoryItem(string actorId, Session session, string section, int index)
        {
            if (!HasTool(actorId, PermInventory))
            {
                session.Status = "You need " + PermInventory + ".";
                return;
            }

            EntityPlayer target = GetSelectedEntity(session);
            if (target == null)
            {
                session.Status = "The selected player is not online.";
                return;
            }

            RepairInventorySlot(target, NormalizeInventorySection(section), index);
            session.Status = "Repaired the item (server-side; the client may need to reopen the inventory or relog).";
            session.Screen = "inventory";
        }

        private void DeleteInventoryItem(string actorId, Session session, string dataText)
        {
            if (!HasTool(actorId, PermClearInventory))
            {
                session.Status = "You need " + PermClearInventory + ".";
                return;
            }

            string[] parts = (dataText ?? string.Empty).Split('|');
            if (parts.Length < 2)
            {
                session.Status = "Invalid item selection.";
                return;
            }

            EntityPlayer target = GetSelectedEntity(session);
            if (target == null)
            {
                session.Status = "The selected player is not online.";
                return;
            }

            if (!CanActOn(actorId, GetSelectedPlayer(session), true, out string reason))
            {
                session.Status = reason;
                return;
            }

            string section = NormalizeInventorySection(parts[0]);
            int index = ParseInt(parts[1], -1);
            InvItem item = GetInventoryItem(target, section, index);
            if (item == null)
            {
                session.Status = "Item not found.";
                return;
            }

            CaptureSnapshotIfPossible(target, "Before delete item");
            RemoveInventoryItem(target, section, index);
            session.Status = "Deleted " + item.Name + " x" + item.Count +
                             " (server-side; the client may need to reopen the inventory or relog).";
            AddLog("admin", GetPlayerName(actorId), target.PlayerDisplayName,
                "Deleted " + item.InternalName + " x" + item.Count + ".");
            session.Screen = "inventory";
        }

        private void ClearSelectedInventory(string actorId, Session session)
        {
            if (!HasTool(actorId, PermClearInventory))
            {
                session.Status = "You need " + PermClearInventory + ".";
                return;
            }

            EntityPlayer target = GetSelectedEntity(session);
            if (target == null)
            {
                session.Status = "The selected player is not online.";
                return;
            }

            if (!CanActOn(actorId, GetSelectedPlayer(session), true, out string reason))
            {
                session.Status = reason;
                return;
            }

            CaptureSnapshotIfPossible(target, "Before clear inventory");
            int count = ClearAllItems(target);
            session.Status = "Cleared " + count + " item stack(s) from " + target.PlayerDisplayName +
                             " (server-side; the client may need to reopen the inventory or relog).";
            AddLog("admin", GetPlayerName(actorId), target.PlayerDisplayName,
                "Cleared inventory (" + count + " stacks).");
        }

        private void TakeSelectedInventory(string actorId, Session session)
        {
            if (!HasTool(actorId, PermTakeInventory))
            {
                session.Status = "You need " + PermTakeInventory + ".";
                return;
            }

            EntityPlayer actor = GetEntity(actorId);
            EntityPlayer target = GetSelectedEntity(session);
            if (actor == null || target == null)
            {
                session.Status = "Both players must be online and spawned.";
                return;
            }

            if (!CanActOn(actorId, GetSelectedPlayer(session), true, out string reason))
            {
                session.Status = reason;
                return;
            }

            CaptureSnapshotIfPossible(target, "Before take all");
            int count = TakeAllItems(actor, target);
            session.Status = "Moved " + count + " item stack(s) from " + target.PlayerDisplayName +
                             " (server-side; the client may need to reopen the inventory or relog).";
            AddLog("admin", GetPlayerName(actorId), target.PlayerDisplayName,
                "Took all items (" + count + " stacks).");
        }

        private void RepairSelectedInventory(string actorId, Session session)
        {
            if (!HasTool(actorId, PermInventory))
            {
                session.Status = "You need " + PermInventory + ".";
                return;
            }

            EntityPlayer target = GetSelectedEntity(session);
            if (target == null)
            {
                session.Status = "The selected player is not online.";
                return;
            }

            int count = RepairAllItems(target);
            session.Status = "Repaired " + count + " item(s) for " + target.PlayerDisplayName + ".";
            AddLog("admin", GetPlayerName(actorId), target.PlayerDisplayName,
                "Repaired all items (" + count + ").");
        }

        private void TakeAllSelectedInventory(string actorId, Session session)
        {
            TakeSelectedInventory(actorId, session);
        }

        private void ClearAllSelectedInventory(string actorId, Session session)
        {
            ClearSelectedInventory(actorId, session);
        }

        private void RepairAllSelectedInventory(string actorId, Session session)
        {
            RepairSelectedInventory(actorId, session);
        }

        private int ClearAllItems(EntityPlayer player)
        {
            int count = 0;
            if (player == null)
            {
                return 0;
            }

            if (player.inventory != null)
            {
                for (int i = 0; i < player.inventory.SlotCount; i++)
                {
                    ItemStack stack = player.inventory.GetStackAt(i);
                    if (stack != null && !stack.IsEmpty())
                    {
                        count++;
                    }

                    player.inventory.SetStackAt(i, null);
                }
            }

            if (player.bag != null && player.bag.ItemGrid != null)
            {
                for (int i = 0; i < player.bag.ItemGrid.Length; i++)
                {
                    ItemStack stack = player.bag.ItemGrid.GetItem(i);
                    if (stack != null && !stack.IsEmpty())
                    {
                        count++;
                    }

                    player.bag.SetSlot(i, null);
                }
            }

            if (player.equipment != null)
            {
                for (int i = 0; i < player.equipment.GetSlotCount(); i++)
                {
                    ItemValue value = player.equipment.GetSlotItem(i);
                    if (value != null && !value.IsEmpty())
                    {
                        count++;
                    }

                    player.equipment.SetSlotItem(i, null);
                }
            }

            return count;
        }

        private int TakeAllItems(EntityPlayer from, EntityPlayer to)
        {
            int count = 0;
            if (from == null || to == null)
            {
                return 0;
            }

            if (from.inventory != null)
            {
                for (int i = 0; i < from.inventory.SlotCount; i++)
                {
                    ItemStack stack = from.inventory.GetStackAt(i);
                    if (stack == null || stack.IsEmpty())
                    {
                        continue;
                    }

                    int added = AddItemToPlayer(to, stack.Clone());
                    if (added <= 0)
                    {
                        continue;
                    }

                    if (added >= stack.count)
                    {
                        from.inventory.SetStackAt(i, null);
                    }
                    else
                    {
                        stack.count -= added;
                        from.inventory.SetStackAt(i, stack);
                    }

                    count++;
                }
            }

            if (from.bag != null && from.bag.ItemGrid != null)
            {
                for (int i = 0; i < from.bag.ItemGrid.Length; i++)
                {
                    ItemStack stack = from.bag.ItemGrid.GetItem(i);
                    if (stack == null || stack.IsEmpty())
                    {
                        continue;
                    }

                    int added = AddItemToPlayer(to, stack.Clone());
                    if (added <= 0)
                    {
                        continue;
                    }

                    if (added >= stack.count)
                    {
                        from.bag.SetSlot(i, null);
                    }
                    else
                    {
                        stack.count -= added;
                        from.bag.SetSlot(i, stack);
                    }

                    count++;
                }
            }

            if (from.equipment != null)
            {
                for (int i = 0; i < from.equipment.GetSlotCount(); i++)
                {
                    ItemValue value = from.equipment.GetSlotItem(i);
                    if (value == null || value.IsEmpty())
                    {
                        continue;
                    }

                    int added = AddItemToPlayer(to, new ItemStack(value.Clone(), 1));
                    if (added > 0)
                    {
                        from.equipment.SetSlotItem(i, null);
                        count++;
                    }
                }
            }

            return count;
        }

        private int RepairAllItems(EntityPlayer player)
        {
            int count = 0;
            if (player == null)
            {
                return 0;
            }

            if (player.inventory != null)
            {
                for (int i = 0; i < player.inventory.SlotCount; i++)
                {
                    ItemStack stack = player.inventory.GetStackAt(i);
                    if (stack == null || stack.IsEmpty() || stack.itemValue == null)
                    {
                        continue;
                    }

                    stack.itemValue.UseTimes = 0f;
                    player.inventory.SetStackAt(i, stack);
                    count++;
                }
            }

            if (player.bag != null && player.bag.ItemGrid != null)
            {
                for (int i = 0; i < player.bag.ItemGrid.Length; i++)
                {
                    ItemStack stack = player.bag.ItemGrid.GetItem(i);
                    if (stack == null || stack.IsEmpty() || stack.itemValue == null)
                    {
                        continue;
                    }

                    stack.itemValue.UseTimes = 0f;
                    player.bag.SetSlot(i, stack);
                    count++;
                }
            }

            if (player.equipment != null)
            {
                for (int i = 0; i < player.equipment.GetSlotCount(); i++)
                {
                    ItemValue value = player.equipment.GetSlotItem(i);
                    if (value == null || value.IsEmpty())
                    {
                        continue;
                    }

                    value.UseTimes = 0f;
                    player.equipment.SetSlotItem(i, value);
                    count++;
                }
            }

            return count;
        }

        private static bool CanPlayerReceive(EntityPlayer player, ItemClass item, int count)
        {
            if (player == null || item == null || count <= 0)
            {
                return false;
            }

            int maxStack = GetMaxStack(item);
            long space = 0;

            if (player.bag != null)
            {
                space += FreeSpace(player.bag.ItemGrid, item, maxStack);
            }

            if (player.inventory != null)
            {
                space += FreeSpace(player.inventory, item, maxStack);
            }

            return space >= count;
        }

        private static long FreeSpace(ItemStackGrid grid, ItemClass item, int maxStack)
        {
            if (grid == null)
            {
                return 0;
            }

            long space = 0;
            for (int i = 0; i < grid.Length; i++)
            {
                ItemStack stack = grid.GetItem(i);
                if (stack == null || stack.IsEmpty())
                {
                    space += maxStack;
                    continue;
                }

                if (item.CanStack() && stack.itemValue != null &&
                    stack.itemValue.type == item.Id && stack.count < maxStack)
                {
                    space += maxStack - stack.count;
                }
            }

            return space;
        }

        private static long FreeSpace(Inventory inventory, ItemClass item, int maxStack)
        {
            if (inventory == null)
            {
                return 0;
            }

            long space = 0;
            for (int i = 0; i < inventory.Length; i++)
            {
                ItemStack stack = inventory.GetStackAt(i);
                if (stack == null || stack.IsEmpty())
                {
                    space += maxStack;
                    continue;
                }

                if (item.CanStack() && stack.itemValue != null &&
                    stack.itemValue.type == item.Id && stack.count < maxStack)
                {
                    space += maxStack - stack.count;
                }
            }

            return space;
        }

        private static int AddItemToPlayer(EntityPlayer player, ItemStack stack)
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
                Interface.Oxide.LogError("[AdminSuite] could not add an item to " +
                                         (player.PlayerDisplayName ?? "a player") + ": " + exception);
                return 0;
            }
        }

        private static void SetInventoryItemCount(EntityPlayer player, string section, int index, int count)
        {
            if (player == null)
            {
                return;
            }

            if (string.Equals(section, "toolbelt", StringComparison.OrdinalIgnoreCase) && player.inventory != null)
            {
                ItemStack stack = player.inventory.GetStackAt(index);
                if (stack == null)
                {
                    return;
                }

                if (count <= 0)
                {
                    player.inventory.SetStackAt(index, null);
                }
                else
                {
                    stack.count = count;
                    player.inventory.SetStackAt(index, stack);
                }

                return;
            }

            if (string.Equals(section, "backpack", StringComparison.OrdinalIgnoreCase) && player.bag != null &&
                player.bag.ItemGrid != null)
            {
                ItemStack stack = player.bag.ItemGrid.GetItem(index);
                if (stack == null)
                {
                    return;
                }

                if (count <= 0)
                {
                    player.bag.SetSlot(index, null);
                }
                else
                {
                    stack.count = count;
                    player.bag.SetSlot(index, stack);
                }

                return;
            }

            if (string.Equals(section, "equipment", StringComparison.OrdinalIgnoreCase) && player.equipment != null &&
                count <= 0)
            {
                player.equipment.SetSlotItem(index, null);
            }
        }

        private static void RemoveInventoryItem(EntityPlayer player, string section, int index)
        {
            if (player == null)
            {
                return;
            }

            if (string.Equals(section, "toolbelt", StringComparison.OrdinalIgnoreCase) && player.inventory != null)
            {
                player.inventory.SetStackAt(index, null);
                return;
            }

            if (string.Equals(section, "backpack", StringComparison.OrdinalIgnoreCase) && player.bag != null)
            {
                player.bag.SetSlot(index, null);
                return;
            }

            if (string.Equals(section, "equipment", StringComparison.OrdinalIgnoreCase) && player.equipment != null)
            {
                player.equipment.SetSlotItem(index, null);
            }
        }

        private static void RepairInventorySlot(EntityPlayer player, string section, int index)
        {
            if (player == null)
            {
                return;
            }

            if (string.Equals(section, "toolbelt", StringComparison.OrdinalIgnoreCase) && player.inventory != null)
            {
                ItemStack stack = player.inventory.GetStackAt(index);
                if (stack != null && stack.itemValue != null)
                {
                    stack.itemValue.UseTimes = 0f;
                    player.inventory.SetStackAt(index, stack);
                }
                return;
            }

            if (string.Equals(section, "backpack", StringComparison.OrdinalIgnoreCase) && player.bag != null)
            {
                ItemStack stack = player.bag.ItemGrid.GetItem(index);
                if (stack != null && stack.itemValue != null)
                {
                    stack.itemValue.UseTimes = 0f;
                    player.bag.SetSlot(index, stack);
                }
                return;
            }

            if (string.Equals(section, "equipment", StringComparison.OrdinalIgnoreCase) && player.equipment != null)
            {
                ItemValue value = player.equipment.GetSlotItem(index);
                if (value != null)
                {
                    value.UseTimes = 0f;
                    player.equipment.SetSlotItem(index, value);
                }
            }
        }

        private static ItemStack CreateStackFromInvItem(InvItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Data))
            {
                ItemValue value = item != null ? FindItemClass(item.InternalName) != null
                    ? new ItemValue(FindItemClass(item.InternalName).Id, true)
                    : null : null;
                return value != null ? new ItemStack(value, item.Count) : null;
            }

            ItemValue deserialized = DeserializeItemValue(item.Data);
            return deserialized != null ? new ItemStack(deserialized, item.Count) : null;
        }

        #endregion

        #region Snapshot actions

        private void CaptureSelectedSnapshot(string actorId, Session session, string reason)
        {
            if (!HasTool(actorId, PermSnapshot))
            {
                session.Status = "You need " + PermSnapshot + ".";
                return;
            }

            EntityPlayer target = GetSelectedEntity(session);
            if (target == null)
            {
                session.Status = "The selected player is not online.";
                return;
            }

            Snapshot snapshot = CaptureSnapshot(target, reason);
            if (snapshot == null)
            {
                session.Status = "Could not capture the snapshot.";
                return;
            }

            session.Status = "Captured snapshot " + snapshot.Id + " for " + snapshot.PlayerName + ".";
            AddLog("snapshot", GetPlayerName(actorId), snapshot.PlayerName, reason);
        }

        private void SnapshotPlayerByName(string actorId, Session session, string text)
        {
            if (!HasTool(actorId, PermSnapshot))
            {
                session.Status = "You need " + PermSnapshot + ".";
                return;
            }

            IPlayer target = ResolvePlayer(text);
            EntityPlayer entity = GetEntity(target);
            if (target == null || entity == null)
            {
                session.Status = "That player is not online.";
                return;
            }

            Snapshot snapshot = CaptureSnapshot(entity, "Manual snapshot");
            if (snapshot == null)
            {
                session.Status = "Could not capture the snapshot.";
                return;
            }

            session.SelectedPlayerId = target.Id;
            session.SelectedPlayerName = target.Name;
            session.Status = "Captured snapshot " + snapshot.Id + " for " + target.Name + ".";
            AddLog("snapshot", GetPlayerName(actorId), target.Name, "Manual snapshot.");
        }

        private Snapshot CaptureSnapshot(EntityPlayer player, string reason)
        {
            if (player == null)
            {
                return null;
            }

            if (snapshots == null)
            {
                snapshots = new SnapshotStore();
            }

            if (snapshots.Snapshots == null)
            {
                snapshots.Snapshots = new List<Snapshot>();
            }

            try
            {
                Vector3 position = player.GetPosition();
                Snapshot snapshot = new Snapshot
                {
                    Id = DateTime.UtcNow.Ticks.ToString("x", CultureInfo.InvariantCulture) + "-" +
                         Guid.NewGuid().ToString("N").Substring(0, 4),
                    PlayerId = GetPlayerIdForEntity(player),
                    PlayerName = player.PlayerDisplayName,
                    Reason = string.IsNullOrEmpty(reason) ? "Manual snapshot" : reason,
                    CreatedUtcTicks = DateTime.UtcNow.Ticks,
                    X = position.x,
                    Y = position.y,
                    Z = position.z,
                    RotX = player.rotation.x,
                    RotY = player.rotation.y,
                    RotZ = player.rotation.z,
                    Health = player.Health,
                    Stamina = player.Stamina,
                    Water = player.Water,
                    Food = player.Stats.Food.Value,
                    Inventory = CaptureGrid(player.inventory != null ? player.inventory.ItemGrid : null),
                    Bag = CaptureGrid(player.bag != null ? player.bag.ItemGrid : null),
                    Equipment = CaptureGrid(player.equipment != null ? player.equipment.ItemGrid : null)
                };

                snapshots.Snapshots.Add(snapshot);
                TrimSnapshots();
                SaveSnapshots();
                return snapshot;
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogError("[AdminSuite] snapshot failed: " + exception);
                return null;
            }
        }

        private void CaptureSnapshotIfPossible(EntityPlayer player, string reason)
        {
            if (player == null || config == null || !config.SnapshotBeforeDestructive)
            {
                return;
            }

            CaptureSnapshot(player, reason);
        }

        private static List<SnapshotSlot> CaptureGrid(ItemStackGrid grid)
        {
            List<SnapshotSlot> result = new List<SnapshotSlot>();
            if (grid == null)
            {
                return result;
            }

            for (int i = 0; i < grid.Length; i++)
            {
                ItemStack stack = grid.GetItem(i);
                if (stack == null || stack.IsEmpty() || stack.itemValue == null || stack.itemValue.IsEmpty())
                {
                    continue;
                }

                string data = SerializeItemValue(stack.itemValue);
                if (string.IsNullOrEmpty(data))
                {
                    continue;
                }

                result.Add(new SnapshotSlot
                {
                    Index = i,
                    Count = stack.count,
                    Data = data
                });
            }

            return result;
        }

        private Snapshot FindSnapshot(string id)
        {
            if (string.IsNullOrEmpty(id) || snapshots == null || snapshots.Snapshots == null)
            {
                return null;
            }

            return snapshots.Snapshots.FirstOrDefault(s => s != null && string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private void DeleteSnapshotById(string actorId, Session session, string id)
        {
            if (!HasTool(actorId, PermSnapshots))
            {
                session.Status = "You need " + PermSnapshots + ".";
                return;
            }

            Snapshot snapshot = FindSnapshot(id);
            if (snapshot == null)
            {
                session.Status = "Snapshot not found.";
                return;
            }

            snapshots.Snapshots.Remove(snapshot);
            SaveSnapshots();
            session.SelectedSnapshotId = null;
            session.Screen = "snapshots";
            session.Status = "Deleted snapshot " + id + ".";
            AddLog("snapshot", GetPlayerName(actorId), snapshot.PlayerName, "Deleted snapshot " + id + ".");
        }

        private void RestoreSnapshotById(string actorId, Session session, string id)
        {
            if (!HasTool(actorId, PermRollback))
            {
                session.Status = "You need " + PermRollback + ".";
                return;
            }

            Snapshot snapshot = FindSnapshot(id);
            if (snapshot == null)
            {
                session.Status = "Snapshot not found.";
                return;
            }

            IPlayer target = ResolvePlayer(snapshot.PlayerId);
            EntityPlayer entity = GetEntity(target);
            if (target == null || entity == null)
            {
                session.Status = "The snapshot's player is not online. Rollback needs a spawned player.";
                return;
            }

            if (!CanActOn(actorId, target, true, out string reason))
            {
                session.Status = reason;
                return;
            }

            CaptureSnapshotIfPossible(entity, "Before rollback");
            RestoreSnapshot(entity, snapshot);
            session.Status = "Restored snapshot " + id + " for " + target.Name +
                             " (inventory is server-side; the client may need to reopen the inventory or relog).";
            AddLog("rollback", GetPlayerName(actorId), target.Name, "Restored snapshot " + id + ".");
            SendToolbeltMessage(target, "Your inventory and position were restored by staff.");
        }

        private void RestoreSnapshot(EntityPlayer player, Snapshot snapshot)
        {
            if (player == null || snapshot == null)
            {
                return;
            }

            RestoreGrid(player.inventory != null ? player.inventory.ItemGrid : null, snapshot.Inventory);
            RestoreGrid(player.bag != null ? player.bag.ItemGrid : null, snapshot.Bag);
            RestoreGrid(player.equipment != null ? player.equipment.ItemGrid : null, snapshot.Equipment);

            Vector3 position = new Vector3(snapshot.X, snapshot.Y, snapshot.Z);
            TeleportPlayerTo(ResolvePlayer(GetPlayerIdForEntity(player)), position, snapshot.RotY);
            player.Stats.Health.Value = snapshot.Health;
            player.Stats.Stamina.Value = snapshot.Stamina;
            player.Stats.Water.Value = snapshot.Water;
            player.Stats.Food.Value = snapshot.Food;
            SyncStats(player);
        }

        private static void RestoreGrid(ItemStackGrid grid, List<SnapshotSlot> slots)
        {
            if (grid == null)
            {
                return;
            }

            for (int i = 0; i < grid.Length; i++)
            {
                grid.SetItem(i, null);
            }

            if (slots == null)
            {
                return;
            }

            for (int i = 0; i < slots.Count; i++)
            {
                SnapshotSlot slot = slots[i];
                if (slot == null || slot.Index < 0 || slot.Index >= grid.Length)
                {
                    continue;
                }

                ItemValue value = DeserializeItemValue(slot.Data);
                if (value == null || value.IsEmpty())
                {
                    continue;
                }

                grid.SetItem(slot.Index, new ItemStack(value, slot.Count));
            }
        }

        private void TrimSnapshots()
        {
            if (snapshots == null)
            {
                return;
            }

            if (snapshots.Snapshots == null)
            {
                snapshots.Snapshots = new List<Snapshot>();
                return;
            }

            DateTime cutoff = DateTime.UtcNow.AddDays(-(config != null ? config.SnapshotRetentionDays : 14));
            snapshots.Snapshots.RemoveAll(s =>
                s == null || s.CreatedUtcTicks < cutoff.Ticks);

            List<string> playerIds = snapshots.Snapshots
                .Where(s => s != null && !string.IsNullOrEmpty(s.PlayerId))
                .Select(s => s.PlayerId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (int p = 0; p < playerIds.Count; p++)
            {
                string playerId = playerIds[p];
                List<Snapshot> list = snapshots.Snapshots
                    .Where(s => s != null && string.Equals(s.PlayerId, playerId, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(s => s.CreatedUtcTicks)
                    .ToList();

                for (int i = (config != null ? config.MaxSnapshotsPerPlayer : 20); i < list.Count; i++)
                {
                    snapshots.Snapshots.Remove(list[i]);
                }
            }
        }

        private void CleanupSnapshots(string actorId, Session session)
        {
            if (!HasTool(actorId, PermSnapshots))
            {
                session.Status = "You need " + PermSnapshots + ".";
                return;
            }

            int before = snapshots.Snapshots.Count;
            TrimSnapshots();
            SaveSnapshots();
            int after = snapshots.Snapshots.Count;
            session.Status = "Snapshot cleanup removed " + (before - after) + " old snapshot(s).";
        }

        private List<Snapshot> GetFilteredSnapshots(Session session)
        {
            if (snapshots == null || snapshots.Snapshots == null)
            {
                return new List<Snapshot>();
            }

            IEnumerable<Snapshot> query = snapshots.Snapshots.Where(s => s != null);
            if (!string.IsNullOrEmpty(session.SnapshotFilterPlayerId))
            {
                query = query.Where(s => string.Equals(s.PlayerId, session.SnapshotFilterPlayerId,
                    StringComparison.OrdinalIgnoreCase));
            }

            return query.OrderByDescending(s => s.CreatedUtcTicks).ToList();
        }

        private int CountSnapshots(string playerId)
        {
            if (snapshots == null || snapshots.Snapshots == null || string.IsNullOrEmpty(playerId))
            {
                return 0;
            }

            return snapshots.Snapshots.Count(s => s != null &&
                string.Equals(s.PlayerId, playerId, StringComparison.OrdinalIgnoreCase));
        }

        private static int SnapshotItemCount(Snapshot snapshot)
        {
            if (snapshot == null)
            {
                return 0;
            }

            int count = 0;
            if (snapshot.Inventory != null)
            {
                count += snapshot.Inventory.Count;
            }
            if (snapshot.Bag != null)
            {
                count += snapshot.Bag.Count;
            }
            if (snapshot.Equipment != null)
            {
                count += snapshot.Equipment.Count;
            }
            return count;
        }

        private static string SnapshotSectionSummary(Snapshot snapshot)
        {
            if (snapshot == null)
            {
                return "none";
            }

            return "toolbelt " + (snapshot.Inventory != null ? snapshot.Inventory.Count : 0) +
                   "  |  backpack " + (snapshot.Bag != null ? snapshot.Bag.Count : 0) +
                   "  |  equipment " + (snapshot.Equipment != null ? snapshot.Equipment.Count : 0);
        }

        #endregion

        #region World and server actions

        private void SetTime(string actorId, Session session, string text)
        {
            if (!HasTool(actorId, PermWorld))
            {
                session.Status = "You need " + PermWorld + ".";
                return;
            }

            string value = (text ?? string.Empty).Trim();
            string command;
            if (string.Equals(value, "day", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "night", StringComparison.OrdinalIgnoreCase))
            {
                command = "settime " + value.ToLowerInvariant();
            }
            else
            {
                string[] parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    session.Status = "Use day, night, a time value, or day hour minute.";
                    return;
                }

                if (parts.Length == 1)
                {
                    command = "settime " + parts[0];
                }
                else if (parts.Length == 2)
                {
                    command = "settime " + parts[0] + " " + parts[1] + " 0";
                }
                else
                {
                    command = "settime " + parts[0] + " " + parts[1] + " " + parts[2];
                }
            }

            ExecuteConsole(command);
            session.Status = "Set the world time: " + command + ".";
            AddLog("world", GetPlayerName(actorId), string.Empty, command);
        }

        private void SetWeather(string actorId, Session session, string mode)
        {
            if (!HasTool(actorId, PermWorld))
            {
                session.Status = "You need " + PermWorld + ".";
                return;
            }

            if (string.Equals(mode, "clear", StringComparison.OrdinalIgnoreCase))
            {
                ExecuteConsole("weather Rain 0");
                ExecuteConsole("weather SnowFall 0");
                ExecuteConsole("weather Fog 0");
                ExecuteConsole("weather Clouds 0");
                ExecuteConsole("weather Wind 0");
                session.Status = "Cleared the weather.";
            }
            else if (string.Equals(mode, "rain", StringComparison.OrdinalIgnoreCase))
            {
                ExecuteConsole("weather Rain 1");
                session.Status = "Turned rain on.";
            }
            else if (string.Equals(mode, "storm", StringComparison.OrdinalIgnoreCase))
            {
                session.Status = "Use the Storm input to set a duration.";
                return;
            }

            AddLog("world", GetPlayerName(actorId), string.Empty, "Weather: " + mode);
        }

        private void WeatherStorm(string actorId, Session session, string text)
        {
            if (!HasTool(actorId, PermWorld))
            {
                session.Status = "You need " + PermWorld + ".";
                return;
            }

            float hours = 1f;
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out hours);
            if (hours <= 0f)
            {
                hours = 1f;
            }

            EntityPlayer entity = GetEntity(actorId);
            string biome = entity != null ? GetBiomeName(entity.GetPosition()) : "pine_forest";
            ExecuteConsole("weather Storm " + hours.ToString(CultureInfo.InvariantCulture) + " " + Quote(biome));
            session.Status = "Started a " + hours + " hour storm in " + biome + ".";
            AddLog("world", GetPlayerName(actorId), string.Empty, "Storm " + hours + "h in " + biome + ".");
        }

        private void SpawnAirdrop(string actorId, Session session)
        {
            if (!HasTool(actorId, PermWorld))
            {
                session.Status = "You need " + PermWorld + ".";
                return;
            }

            ExecuteConsole("spawnairdrop");
            session.Status = "Airdrop requested.";
            AddLog("world", GetPlayerName(actorId), string.Empty, "Spawned an airdrop.");
        }

        private void SpawnHorde(string actorId, Session session)
        {
            if (!HasTool(actorId, PermWorld))
            {
                session.Status = "You need " + PermWorld + ".";
                return;
            }

            ExecuteConsole("spawnwandering");
            session.Status = "Wandering horde spawned.";
            AddLog("world", GetPlayerName(actorId), string.Empty, "Spawned a wandering horde.");
        }

        private void SpawnScouts(string actorId, Session session)
        {
            if (!HasTool(actorId, PermWorld))
            {
                session.Status = "You need " + PermWorld + ".";
                return;
            }

            ExecuteConsole("spawnscouts");
            session.Status = "Scouts spawned.";
            AddLog("world", GetPlayerName(actorId), string.Empty, "Spawned scouts.");
        }

        private void SpawnEntity(string actorId, Session session, string text)
        {
            if (!HasTool(actorId, PermWorld))
            {
                session.Status = "You need " + PermWorld + ".";
                return;
            }

            string[] parts = (text ?? string.Empty).Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                session.Status = "Use: entity name [count]";
                return;
            }

            string name = parts[0];
            int count = parts.Length > 1 ? ParseInt(parts[1], config.DefaultSpawnCount) : config.DefaultSpawnCount;
            count = Clamp(count, 1, 100);

            EntityPlayer entity = GetEntity(actorId);
            if (entity == null)
            {
                session.Status = "You must be spawned in the world.";
                return;
            }

            Vector3 position = entity.GetPosition();
            ExecuteConsole("spawnentityat " + Quote(name) + " " +
                           position.x.ToString(CultureInfo.InvariantCulture) + " " +
                           (position.y + 1f).ToString(CultureInfo.InvariantCulture) + " " +
                           position.z.ToString(CultureInfo.InvariantCulture) + " " +
                           count.ToString(CultureInfo.InvariantCulture));
            session.Status = "Spawned " + count + "x " + name + ".";
            AddLog("world", GetPlayerName(actorId), string.Empty, "Spawned " + count + "x " + name + ".");
        }

        private void SetGamePref(string actorId, Session session, string text)
        {
            if (!IsAdmin(actorId))
            {
                session.Status = "Only an admin can change game preferences.";
                return;
            }

            string[] parts = (text ?? string.Empty).Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                session.Status = "Use: PrefName Value";
                return;
            }

            ExecuteConsole("setgamepref " + parts[0] + " " + Quote(parts[1]));
            session.Status = "Set " + parts[0] + " to " + parts[1] + ".";
            AddLog("server", GetPlayerName(actorId), string.Empty, "setgamepref " + parts[0] + " " + parts[1]);
        }

        private void SaveWorld(string actorId, Session session)
        {
            if (!HasTool(actorId, PermServer) && !HasTool(actorId, PermWorld))
            {
                session.Status = "You do not have permission to save the world.";
                return;
            }

            ExecuteConsole("saveworld");
            session.Status = "World save requested.";
            AddLog("server", GetPlayerName(actorId), string.Empty, "Saved the world.");
        }

        private void ChunkResetNearPlayer(string actorId, Session session)
        {
            if (!HasTool(actorId, PermWorld))
            {
                session.Status = "You need " + PermWorld + ".";
                return;
            }

            IPlayer actor = ResolvePlayer(actorId);
            if (actor == null)
            {
                session.Status = "You must be in-game to reset chunks near you.";
                return;
            }

            if (!SendClientCommand(actor, "chunkreset f"))
            {
                session.Status = "Could not send the chunk reset command to your client.";
                return;
            }

            session.Status = "Chunk reset requested on your client. This can cause a hitch.";
            AddLog("world", actor.Name, string.Empty, "Chunk reset near self.");
        }

        private void RegionReset(string actorId, Session session)
        {
            if (!HasTool(actorId, PermWorld))
            {
                session.Status = "You need " + PermWorld + ".";
                return;
            }

            ExecuteConsole("regionreset");
            session.Status = "Region reset started. This can take a while.";
            AddLog("world", GetPlayerName(actorId), string.Empty, "Started a region reset.");
        }

        private void Announce(string actorId, Session session, string text)
        {
            if (!HasTool(actorId, PermServer))
            {
                session.Status = "You need " + PermServer + ".";
                return;
            }

            if (string.IsNullOrEmpty(text))
            {
                session.Status = "The message cannot be empty.";
                return;
            }

            ExecuteConsole("say " + Quote(text));
            session.Status = "Announcement sent.";
            AddLog("server", GetPlayerName(actorId), string.Empty, "Announcement: " + text);
        }

        private void RestartWarning(string actorId, Session session, string text)
        {
            if (!HasTool(actorId, PermServer))
            {
                session.Status = "You need " + PermServer + ".";
                return;
            }

            int minutes = ParseInt(text, 5);
            if (minutes <= 0)
            {
                minutes = 5;
            }

            ExecuteConsole("say Server restart in " + minutes + " minute(s).");
            session.Status = "Restart warning sent for " + minutes + " minute(s).";
            AddLog("server", GetPlayerName(actorId), string.Empty, "Restart warning " + minutes + "m.");
        }

        private void ShutdownServer(string actorId, Session session)
        {
            if (!IsAdmin(actorId))
            {
                session.Status = "Only an admin can shut down the server.";
                return;
            }

            ExecuteConsole("say Server shutting down in 5 seconds.");
            AddLog("server", GetPlayerName(actorId), string.Empty, "Server shutdown requested.");
            session.Status = "Server shutdown scheduled in 5 seconds.";
            timer.Once(5f, () => ExecuteConsole("shutdown"));
        }

        private void ReloadAll(string actorId, Session session)
        {
            if (!HasTool(actorId, PermReload))
            {
                session.Status = "You need " + PermReload + ".";
                return;
            }

            SaveAll();
            LoadConfig();
            LoadData();
            LoadSnapshots();
            EnsureDefaultPermissions();
            RegisterServerAssistHook();
            session.Status = "Reloaded config, data, snapshots and the ServerAssist hook.";
            AddLog("server", GetPlayerName(actorId), string.Empty, "Reloaded AdminSuite.");
        }

        private void ClearLogs(string actorId, Session session)
        {
            if (!HasTool(actorId, PermLogs))
            {
                session.Status = "You need " + PermLogs + ".";
                return;
            }

            memoryLogs.Clear();
            session.Status = "Cleared the in-memory log.";
        }

        #endregion

        #region Client commands and console

        private void SendClientCommandToActor(string actorId, Session session, string command)
        {
            IPlayer actor = ResolvePlayer(actorId);
            if (actor == null)
            {
                session.Status = "You must be in-game to use that action.";
                return;
            }

            if (!SendClientCommand(actor, command))
            {
                session.Status = "Could not send that client command. Make sure you are in-game.";
                return;
            }

            session.Status = "Sent client command: " + command;
        }

        private void ToggleCreativeMenu(string actorId, Session session)
        {
            if (!HasTool(actorId, PermGive))
            {
                session.Status = "You need " + PermGive + ".";
                return;
            }

            IPlayer actor = ResolvePlayer(actorId);
            if (actor == null)
            {
                session.Status = "You must be in-game to use the creative menu.";
                return;
            }

            if (!SendClientCommand(actor, "creativemenu"))
            {
                session.Status = "Could not send the creative menu command to your client.";
                return;
            }

            SendToolbeltMessage(actor, "Creative menu toggled. Open your inventory.");
            session.Status = "Toggled the creative menu on your client. Open your inventory to use it.";
            AddLog("admin", GetPlayerName(actorId), string.Empty, "Toggled the creative menu.");
        }

        private bool SyncEntityFlags(EntityPlayer entity)
        {
            if (entity == null)
            {
                return false;
            }

            try
            {
                IPlayer player = ResolvePlayer(GetPlayerIdForEntity(entity));
                ClientInfo client = GetClientInfo(player);
                if (client == null)
                {
                    return false;
                }

                NetPackageEntityAliveFlags package =
                    NetPackageManager.GetPackage<NetPackageEntityAliveFlags>().Setup(entity);
                client.SendPackage(package);
                return true;
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[AdminSuite] could not sync entity flags: " + exception.Message);
                return false;
            }
        }

        private void SyncStats(EntityPlayer entity)
        {
            if (entity == null)
            {
                return;
            }

            try
            {
                IPlayer player = ResolvePlayer(GetPlayerIdForEntity(entity));
                ClientInfo client = GetClientInfo(player);
                if (client == null)
                {
                    return;
                }

                SendStat(client, entity, NetPackageEntityStatChanged.EnumStat.Health);
                SendStat(client, entity, NetPackageEntityStatChanged.EnumStat.Stamina);
                SendStat(client, entity, NetPackageEntityStatChanged.EnumStat.Food);
                SendStat(client, entity, NetPackageEntityStatChanged.EnumStat.Water);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[AdminSuite] could not sync player stats: " + exception.Message);
            }
        }

        private static void SendStat(ClientInfo client, EntityAlive entity, NetPackageEntityStatChanged.EnumStat stat)
        {
            try
            {
                NetPackageEntityStatChanged package =
                    NetPackageManager.GetPackage<NetPackageEntityStatChanged>().Setup(entity, 0, stat);
                client.SendPackage(package);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[AdminSuite] could not sync stat " + stat + ": " + exception.Message);
            }
        }

        private bool SendClientCommand(IPlayer player, string command)
        {
            if (player == null || !player.IsConnected)
            {
                return false;
            }

            try
            {
                ClientInfo client = GetClientInfo(player);
                if (client == null)
                {
                    return false;
                }

                NetPackageConsoleCmdClient package =
                    NetPackageManager.GetPackage<NetPackageConsoleCmdClient>().Setup(command, true);
                client.SendPackage(package);
                return true;
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[AdminSuite] client command failed: " + exception.Message);
                return false;
            }
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
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[AdminSuite] toolbelt message failed: " + exception.Message);
            }
        }

        private void Broadcast(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            ExecuteConsole("say " + Quote(message));
        }

        private List<string> ExecuteConsole(string command)
        {
            if (string.IsNullOrEmpty(command))
            {
                return null;
            }

            try
            {
                SdtdConsole console = SdtdConsole.Instance;
                if (console == null)
                {
                    Interface.Oxide.LogWarning("[AdminSuite] console command failed (no console): " + command);
                    return null;
                }

                return console.ExecuteSync(command, null);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogError("[AdminSuite] console command failed: " + command + " - " + exception);
                return null;
            }
        }

        private ClientInfo GetClientInfo(IPlayer player)
        {
            if (player == null)
            {
                return null;
            }

            try
            {
                EntityPlayer entity = player.Object as EntityPlayer;
                if (entity != null && ConnectionManager.Instance != null &&
                    ConnectionManager.Instance.Clients != null)
                {
                    ClientInfo byEntity = ConnectionManager.Instance.Clients.ForEntityId(entity.entityId);
                    if (byEntity != null)
                    {
                        return byEntity;
                    }
                }

                if (ConnectionManager.Instance == null || ConnectionManager.Instance.Clients == null)
                {
                    return null;
                }

                ClientInfo[] clients = ConnectionManager.Instance.Clients.list.ToArray();
                for (int i = 0; i < clients.Length; i++)
                {
                    ClientInfo client = clients[i];
                    if (client == null || client.PlatformId == null)
                    {
                        continue;
                    }

                    if (string.Equals(client.PlatformId.ToString(), player.Id, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(client.PlatformId.CombinedString, player.Id, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(client.PlatformId.ReadablePlatformUserIdentifier, player.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        return client;
                    }
                }
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[AdminSuite] could not find client info: " + exception.Message);
            }

            return null;
        }

        #endregion

        #region Player and world helpers

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

        private IPlayer GetSelectedPlayer(Session session)
        {
            return session != null ? ResolvePlayer(session.SelectedPlayerId) : null;
        }

        private EntityPlayer GetSelectedEntity(Session session)
        {
            return GetEntity(GetSelectedPlayer(session));
        }

        private string GetSelectedName(Session session)
        {
            IPlayer player = GetSelectedPlayer(session);
            if (player != null)
            {
                return player.Name;
            }

            return session != null && !string.IsNullOrEmpty(session.SelectedPlayerName)
                ? session.SelectedPlayerName
                : "no player selected";
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

        private string GetPlayerIdForEntity(EntityPlayer entity)
        {
            if (entity == null)
            {
                return string.Empty;
            }

            foreach (IPlayer player in players.Connected)
            {
                if (player == null)
                {
                    continue;
                }

                EntityPlayer candidate = player.Object as EntityPlayer;
                if (candidate != null && candidate.entityId == entity.entityId)
                {
                    return player.Id;
                }
            }

            try
            {
                PersistentPlayerData persistent = entity.PersistentPlayerData;
                if (persistent != null && persistent.PrimaryId != null)
                {
                    return persistent.PrimaryId.ToString();
                }
            }
            catch
            {
                // Fall through to the entity id.
            }

            return entity.entityId.ToString(CultureInfo.InvariantCulture);
        }

        private string GetPlayerName(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return "unknown";
            }

            if (string.Equals(playerId, "server_console", StringComparison.OrdinalIgnoreCase))
            {
                return "Console";
            }

            IPlayer player = ResolvePlayer(playerId);
            return player != null ? player.Name : playerId;
        }

        private IPlayer GetPlayerByName(string name)
        {
            return ResolvePlayer(name);
        }

        private bool CanActOn(string actorId, IPlayer target, bool harmful, out string reason)
        {
            return CanActOn(actorId, target, harmful, out reason, true);
        }

        private bool CanActOn(string actorId, IPlayer target, bool harmful, out string reason, bool checkProtection)
        {
            reason = null;
            if (target == null)
            {
                reason = "Player not found.";
                return false;
            }

            if (harmful && string.Equals(actorId, target.Id, StringComparison.OrdinalIgnoreCase) &&
                (config == null || !config.AllowSelfTarget))
            {
                reason = "You cannot target yourself with that action.";
                return false;
            }

            if (checkProtection && IsProtectedPlayer(target))
            {
                reason = "That player is protected from staff actions.";
                return false;
            }

            if ((config == null || config.ProtectAdminsFromStaff) && !IsAdmin(actorId) &&
                (SafeIsAdmin(target) || permission.UserHasPermission(target.Id, PermAdmin)))
            {
                reason = "You cannot target an administrator.";
                return false;
            }

            return true;
        }

        private bool IsProtectedPlayer(IPlayer target)
        {
            if (target == null || config == null || config.ProtectedPlayers == null)
            {
                return false;
            }

            for (int i = 0; i < config.ProtectedPlayers.Count; i++)
            {
                string entry = config.ProtectedPlayers[i];
                if (string.IsNullOrEmpty(entry))
                {
                    continue;
                }

                if (string.Equals(entry, target.Id, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(entry, target.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SafeIsAdmin(IPlayer player)
        {
            try
            {
                return player != null && player.IsAdmin;
            }
            catch
            {
                return false;
            }
        }

        private static Vector3 GetEntityPosition(EntityPlayer entity)
        {
            if (entity == null)
            {
                return Vector3.zero;
            }

            try
            {
                return entity.GetPosition();
            }
            catch
            {
                return Vector3.zero;
            }
        }

        private static bool TryTeleport(EntityPlayer entity, Vector3 position, float direction)
        {
            if (entity == null)
            {
                return false;
            }

            try
            {
                entity.Teleport(position, direction);
                return true;
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[AdminSuite] teleport failed: " + exception.Message);
                return false;
            }
        }

        private void TeleportPlayerTo(IPlayer player, Vector3 position, float direction)
        {
            if (player == null)
            {
                return;
            }

            bool sent = false;
            ClientInfo client = GetClientInfo(player);
            if (client != null)
            {
                try
                {
                    NetPackageTeleportPlayer package =
                        NetPackageManager.GetPackage<NetPackageTeleportPlayer>()
                            .Setup(position, null, false);
                    client.SendPackage(package);
                    sent = true;
                }
                catch (Exception exception)
                {
                    Interface.Oxide.LogWarning("[AdminSuite] could not send teleport package: " + exception.Message);
                }
            }

            if (!sent)
            {
                ExecuteConsole("teleportplayer " + ConsoleTarget(player) + " " +
                               position.x.ToString(CultureInfo.InvariantCulture) + " " +
                               position.y.ToString(CultureInfo.InvariantCulture) + " " +
                               position.z.ToString(CultureInfo.InvariantCulture));
            }

            EntityPlayer entity = GetEntity(player);
            if (entity != null && position.y >= 0f)
            {
                TryTeleport(entity, position, direction);
            }
        }

        private void TeleportPlayerToPlayer(IPlayer mover, IPlayer destination)
        {
            if (mover == null || destination == null)
            {
                return;
            }

            EntityPlayer destinationEntity = GetEntity(destination);
            if (destinationEntity == null)
            {
                return;
            }

            TeleportPlayerTo(mover, destinationEntity.GetPosition(), destinationEntity.rotation.y);
        }

        private static string GetBiomeName(Vector3 position)
        {
            World world = GetWorld();
            if (world == null)
            {
                return "unknown";
            }

            try
            {
                BiomeDefinition biome = world.GetBiome((int)position.x, (int)position.z);
                if (biome == null)
                {
                    return "unknown";
                }

                if (!string.IsNullOrEmpty(biome.m_sBiomeName))
                {
                    return biome.m_sBiomeName;
                }

                if (!string.IsNullOrEmpty(biome.LocalizedName))
                {
                    return biome.LocalizedName;
                }
            }
            catch
            {
                // Ignore biome lookup failures.
            }

            return "unknown";
        }

        private static World GetWorld()
        {
            try
            {
                return GameManager.Instance != null ? GameManager.Instance.World : null;
            }
            catch
            {
                return null;
            }
        }

        private string FormatDistanceToActor(string actorId, EntityPlayer target)
        {
            EntityPlayer actor = GetEntity(actorId);
            if (actor == null || target == null)
            {
                return "n/a";
            }

            try
            {
                float distance = Vector3.Distance(actor.GetPosition(), target.GetPosition());
                return (int)distance + "m";
            }
            catch
            {
                return "n/a";
            }
        }

        private static string FormatVector(Vector3 value)
        {
            return FormatVector(value.x, value.y, value.z);
        }

        private static string FormatVector(float x, float y, float z)
        {
            return x.ToString("F1", CultureInfo.InvariantCulture) + ", " +
                   y.ToString("F1", CultureInfo.InvariantCulture) + ", " +
                   z.ToString("F1", CultureInfo.InvariantCulture);
        }

        private static string FormatWorldTime()
        {
            World world = GetWorld();
            if (world == null)
            {
                return "Day ?";
            }

            try
            {
                ulong time = world.worldTime;
                int hour = (int)((time % 24000UL) / 1000UL);
                int minute = (int)(((time % 1000UL) * 60UL) / 1000UL);
                int day = world.WorldDay;
                if (day <= 0)
                {
                    day = (int)(time / 24000UL) + 1;
                }

                return "Day " + day + " " + hour.ToString("00", CultureInfo.InvariantCulture) + ":" +
                       minute.ToString("00", CultureInfo.InvariantCulture) +
                       (world.IsDaytime() ? " day" : " night");
            }
            catch
            {
                return "Day ?";
            }
        }

        #endregion

        #region Player summaries

        private static int SafePing(IPlayer player)
        {
            try
            {
                return player != null ? player.Ping : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static string SafeAddress(IPlayer player)
        {
            try
            {
                return player != null && !string.IsNullOrEmpty(player.Address) ? player.Address : "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private static string SafeId(IPlayer player)
        {
            return player != null && !string.IsNullOrEmpty(player.Id) ? player.Id : "unknown";
        }

        private string PlayerTag(string actorId, IPlayer player)
        {
            if (player == null)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            if (SafeIsAdmin(player) || permission.UserHasPermission(player.Id, PermAdmin))
            {
                builder.Append("[A] ");
            }

            if (GetActiveMute(player.Id) != null)
            {
                builder.Append("[M] ");
            }

            if (GetActiveFreeze(player.Id) != null)
            {
                builder.Append("[F] ");
            }

            return builder.ToString();
        }

        private static string HealthSummary(EntityPlayer entity)
        {
            if (entity == null)
            {
                return "offline";
            }

            try
            {
                return "HP " + entity.Health + "/" + entity.GetMaxHealth();
            }
            catch
            {
                return "HP ?";
            }
        }

        private static string VitalsSummary(EntityPlayer entity)
        {
            if (entity == null)
            {
                return "unknown";
            }

            try
            {
                return "HP " + entity.Health + "/" + entity.GetMaxHealth() +
                       "  |  stamina " + (int)entity.Stamina + "/" + (int)entity.GetMaxStamina() +
                       "  |  water " + (int)entity.Water + "/" + (int)entity.GetMaxWater() +
                       "  |  food " + (int)entity.Stats.Food.Value + "/" + (int)entity.Stats.Food.Max;
            }
            catch
            {
                return "unknown";
            }
        }

        private static string InventorySummary(EntityPlayer entity)
        {
            if (entity == null)
            {
                return "unknown";
            }

            return "toolbelt " + CountGridItems(entity.inventory != null ? entity.inventory.ItemGrid : null) +
                   "  |  backpack " + CountGridItems(entity.bag != null ? entity.bag.ItemGrid : null) +
                   "  |  equipment " + CountGridItems(entity.equipment != null ? entity.equipment.ItemGrid : null);
        }

        private static int CountGridItems(ItemStackGrid grid)
        {
            if (grid == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < grid.Length; i++)
            {
                ItemStack stack = grid.GetItem(i);
                if (stack != null && !stack.IsEmpty())
                {
                    count++;
                }
            }

            return count;
        }

        private string ModerationSummary(string playerId)
        {
            List<string> parts = new List<string>();
            MuteEntry mute = GetActiveMute(playerId);
            if (mute != null)
            {
                parts.Add("muted (" + FormatRemaining(mute.UntilUtcTicks) + ")");
            }

            FreezeEntry freeze = GetActiveFreeze(playerId);
            if (freeze != null)
            {
                parts.Add("frozen (" + FormatRemaining(freeze.UntilUtcTicks) + ")");
            }

            int warnings = CountWarnings(playerId);
            if (warnings > 0)
            {
                parts.Add(warnings + " warning(s)");
            }

            return parts.Count == 0 ? "clean" : string.Join("  |  ", parts.ToArray());
        }

        private int CountWarnings(string playerId)
        {
            if (data == null || data.Warnings == null || string.IsNullOrEmpty(playerId))
            {
                return 0;
            }

            List<WarningEntry> list;
            return data.Warnings.TryGetValue(playerId, out list) && list != null ? list.Count : 0;
        }

        private string GetNoteText(string playerId)
        {
            if (data == null || data.Notes == null || string.IsNullOrEmpty(playerId))
            {
                return null;
            }

            PlayerNote note;
            return data.Notes.TryGetValue(playerId, out note) && note != null ? note.Text : null;
        }

        #endregion

        #region Duration and state helpers

        private MuteEntry GetActiveMute(string playerId)
        {
            if (data == null || data.Mutes == null || string.IsNullOrEmpty(playerId))
            {
                return null;
            }

            MuteEntry entry;
            if (!data.Mutes.TryGetValue(playerId, out entry) || entry == null)
            {
                return null;
            }

            if (!IsMuteActive(entry))
            {
                data.Mutes.Remove(playerId);
                dataDirty = true;
                return null;
            }

            return entry;
        }

        private FreezeEntry GetActiveFreeze(string playerId)
        {
            if (data == null || data.Freezes == null || string.IsNullOrEmpty(playerId))
            {
                return null;
            }

            FreezeEntry entry;
            if (!data.Freezes.TryGetValue(playerId, out entry) || entry == null)
            {
                return null;
            }

            if (!IsFreezeActive(entry))
            {
                data.Freezes.Remove(playerId);
                dataDirty = true;
                return null;
            }

            return entry;
        }

        private static bool IsMuteActive(MuteEntry entry)
        {
            return entry != null && (entry.UntilUtcTicks <= 0L || entry.UntilUtcTicks > DateTime.UtcNow.Ticks);
        }

        private static bool IsFreezeActive(FreezeEntry entry)
        {
            return entry != null && (entry.UntilUtcTicks <= 0L || entry.UntilUtcTicks > DateTime.UtcNow.Ticks);
        }

        private int CountActiveMutes()
        {
            if (data == null || data.Mutes == null)
            {
                return 0;
            }

            return data.Mutes.Values.Count(IsMuteActive);
        }

        private int CountActiveFreezes()
        {
            if (data == null || data.Freezes == null)
            {
                return 0;
            }

            return data.Freezes.Values.Count(IsFreezeActive);
        }

        private int CountWarnings()
        {
            if (data == null || data.Warnings == null)
            {
                return 0;
            }

            int count = 0;
            foreach (List<WarningEntry> list in data.Warnings.Values)
            {
                if (list != null)
                {
                    count += list.Count;
                }
            }

            return count;
        }

        private static long ParseDurationSpecMinutes(string spec)
        {
            if (string.IsNullOrEmpty(spec))
            {
                return long.MinValue;
            }

            string value = spec.Trim().ToLowerInvariant();
            if (value == "perm" || value == "permanent")
            {
                return 0L;
            }

            int index = 0;
            while (index < value.Length)
            {
                char c = value[index];
                if (!char.IsDigit(c) && c != '.')
                {
                    break;
                }

                index++;
            }

            if (index == 0)
            {
                return long.MinValue;
            }

            long number;
            if (!long.TryParse(value.Substring(0, index), NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ||
                number <= 0)
            {
                return long.MinValue;
            }

            string unit = value.Substring(index).Trim();
            if (unit.Length == 0)
            {
                unit = "m";
            }

            if (unit.StartsWith("mo", StringComparison.Ordinal) || unit.StartsWith("month", StringComparison.Ordinal))
            {
                return number * 43200L;
            }

            switch (unit[0])
            {
                case 'm':
                    return number;
                case 'h':
                    return number * 60L;
                case 'd':
                    return number * 1440L;
                case 'w':
                    return number * 10080L;
                case 'y':
                    return number * 525600L;
                default:
                    return long.MinValue;
            }
        }

        private static string FormatMinutes(long minutes)
        {
            if (minutes <= 0L)
            {
                return "permanent";
            }

            if (minutes >= 525600L)
            {
                return (minutes / 525600L) + "y";
            }

            if (minutes >= 43200L)
            {
                return (minutes / 43200L) + "mo";
            }

            if (minutes >= 10080L)
            {
                return (minutes / 10080L) + "w";
            }

            if (minutes >= 1440L)
            {
                return (minutes / 1440L) + "d";
            }

            if (minutes >= 60L)
            {
                long remainder = minutes % 60L;
                return (minutes / 60L) + "h" + (remainder > 0L ? " " + remainder + "m" : string.Empty);
            }

            return minutes + "m";
        }

        private static string FormatRemaining(long untilUtcTicks)
        {
            if (untilUtcTicks <= 0L)
            {
                return "permanent";
            }

            TimeSpan span = new DateTime(untilUtcTicks, DateTimeKind.Utc) - DateTime.UtcNow;
            if (span.TotalSeconds <= 0)
            {
                return "expired";
            }

            return FormatDuration(span);
        }

        private static string FormatDuration(TimeSpan span)
        {
            if (span.TotalSeconds < 0)
            {
                span = TimeSpan.Zero;
            }

            if (span.TotalDays >= 1.0)
            {
                return (int)span.TotalDays + "d " + span.Hours + "h";
            }

            if (span.TotalHours >= 1.0)
            {
                return (int)span.TotalHours + "h " + span.Minutes + "m";
            }

            if (span.TotalMinutes >= 1.0)
            {
                return (int)span.TotalMinutes + "m " + span.Seconds + "s";
            }

            return Math.Max(0, (int)span.TotalSeconds) + "s";
        }

        private static string FormatBanDuration(DateTime until)
        {
            if (until == DateTime.MaxValue)
            {
                return "permanent";
            }

            return FormatDuration(until - DateTime.UtcNow);
        }

        private static string ParseReasonFromPending(Session session, string fallback)
        {
            if (session == null)
            {
                return fallback;
            }

            string value = session.PendingData;
            session.PendingData = null;
            return string.IsNullOrEmpty(value) ? fallback : value.Trim();
        }

        #endregion

        #region Freeze timer and expiry

        private void StartFreezeTimer()
        {
            if (freezeTimer != null)
            {
                return;
            }

            int seconds = config != null ? config.FreezeCheckSeconds : 1;
            if (seconds < 1)
            {
                seconds = 1;
            }

            freezeTimer = timer.Repeat(seconds, -1, TickFreezes);
        }

        private void StopFreezeTimer()
        {
            if (freezeTimer != null)
            {
                freezeTimer.Destroy();
                freezeTimer = null;
            }
        }

        private void TickFreezes()
        {
            if (data == null || data.Freezes == null || data.Freezes.Count == 0)
            {
                return;
            }

            List<string> remove = new List<string>();
            foreach (KeyValuePair<string, FreezeEntry> pair in data.Freezes.ToList())
            {
                FreezeEntry entry = pair.Value;
                if (entry == null)
                {
                    remove.Add(pair.Key);
                    continue;
                }

                if (!IsFreezeActive(entry))
                {
                    remove.Add(pair.Key);
                    IPlayer player = ResolvePlayer(pair.Key);
                    if (player != null && player.IsConnected)
                    {
                        SendToolbeltMessage(player, "Your freeze expired.");
                        Reply(player, "Your freeze expired.");
                    }
                    continue;
                }

                IPlayer online = ResolvePlayer(pair.Key);
                EntityPlayer entity = GetEntity(online);
                if (entity == null)
                {
                    continue;
                }

                Vector3 target = new Vector3(entry.X, entry.Y, entry.Z);
                Vector3 current = entity.GetPosition();
                if (Vector3.Distance(current, target) > 1.5f)
                {
                    TeleportPlayerTo(online, target, entry.RotY);
                }
            }

            if (remove.Count == 0)
            {
                return;
            }

            for (int i = 0; i < remove.Count; i++)
            {
                data.Freezes.Remove(remove[i]);
            }

            SaveData();
        }

        private void ExpireEverything()
        {
            bool changed = false;
            if (data != null)
            {
                if (data.Mutes != null)
                {
                    foreach (string id in data.Mutes.Where(p => !IsMuteActive(p.Value)).Select(p => p.Key).ToList())
                    {
                        data.Mutes.Remove(id);
                        changed = true;
                    }
                }

                if (data.Freezes != null)
                {
                    foreach (string id in data.Freezes.Where(p => !IsFreezeActive(p.Value)).Select(p => p.Key).ToList())
                    {
                        data.Freezes.Remove(id);
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                SaveData();
            }

            if (snapshots != null)
            {
                int before = snapshots.Snapshots != null ? snapshots.Snapshots.Count : 0;
                TrimSnapshots();
                if (snapshots.Snapshots != null && snapshots.Snapshots.Count != before)
                {
                    SaveSnapshots();
                }
            }
        }

        #endregion

        #region Logs

        private void AddLog(string category, string actor, string target, string message)
        {
            LogEntry entry = new LogEntry
            {
                UtcTicks = DateTime.UtcNow.Ticks,
                Category = string.IsNullOrEmpty(category) ? "info" : category,
                Actor = actor ?? string.Empty,
                Target = target ?? string.Empty,
                Message = message ?? string.Empty
            };

            memoryLogs.Add(entry);
            int max = config != null ? config.MaxLogEntries : 1000;
            while (memoryLogs.Count > max)
            {
                memoryLogs.RemoveAt(0);
            }

            if (config == null || config.WriteLogFile)
            {
                AppendLogFile(entry);
            }
        }

        private static string SanitizeLogField(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        private void AppendLogFile(LogEntry entry)
        {
            try
            {
                string directory = Interface.Oxide.LogDirectory;
                if (string.IsNullOrEmpty(directory))
                {
                    return;
                }

                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string path = Path.Combine(directory,
                    "AdminSuite-" + DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log");
                string line = entry.UtcTicks.ToString(CultureInfo.InvariantCulture) + "\t" +
                              SanitizeLogField(entry.Category) + "\t" +
                              SanitizeLogField(entry.Actor) + "\t" +
                              SanitizeLogField(entry.Target) + "\t" +
                              SanitizeLogField(entry.Message);
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch
            {
                // Logging must never break an admin action.
            }
        }

        private void LoadRecentLogFile()
        {
            try
            {
                string directory = Interface.Oxide.LogDirectory;
                if (string.IsNullOrEmpty(directory))
                {
                    return;
                }

                string path = Path.Combine(directory,
                    "AdminSuite-" + DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log");
                if (!File.Exists(path))
                {
                    return;
                }

                FileInfo info = new FileInfo(path);
                if (info.Length > 8 * 1024 * 1024)
                {
                    return;
                }

                string[] lines = File.ReadAllLines(path);
                int start = Math.Max(0, lines.Length - 200);
                for (int i = start; i < lines.Length; i++)
                {
                    string[] parts = lines[i].Split('\t');
                    if (parts.Length < 5)
                    {
                        continue;
                    }

                    long ticks;
                    if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks))
                    {
                        continue;
                    }

                    memoryLogs.Add(new LogEntry
                    {
                        UtcTicks = ticks,
                        Category = parts[1],
                        Actor = parts[2],
                        Target = parts[3],
                        Message = parts[4]
                    });
                }

                memoryLogs.Sort((a, b) => a.UtcTicks.CompareTo(b.UtcTicks));
                int max = config != null ? config.MaxLogEntries : 1000;
                while (memoryLogs.Count > max)
                {
                    memoryLogs.RemoveAt(0);
                }
            }
            catch
            {
                // A corrupt log file must not stop the plugin.
            }
        }

        private List<LogEntry> GetFilteredLogs(Session session)
        {
            IEnumerable<LogEntry> query = memoryLogs.AsEnumerable().Reverse();
            string filter = session != null && !string.IsNullOrEmpty(session.LogFilter)
                ? session.LogFilter
                : "all";

            if (!string.Equals(filter, "all", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(entry => MatchesFilter(entry, filter));
            }

            return query.ToList();
        }

        private static bool MatchesFilter(LogEntry entry, string filter)
        {
            if (entry == null)
            {
                return false;
            }

            string category = entry.Category ?? string.Empty;
            switch (filter.ToLowerInvariant())
            {
                case "chat":
                    return string.Equals(category, "chat", StringComparison.OrdinalIgnoreCase);
                case "join":
                    return string.Equals(category, "join", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(category, "leave", StringComparison.OrdinalIgnoreCase);
                case "death":
                    return string.Equals(category, "death", StringComparison.OrdinalIgnoreCase);
                case "command":
                    return string.Equals(category, "command", StringComparison.OrdinalIgnoreCase);
                case "admin":
                case "moderation":
                    return string.Equals(category, "admin", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(category, "kick", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(category, "ban", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(category, "unban", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(category, "mute", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(category, "freeze", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(category, "warn", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(category, "snapshot", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(category, "rollback", StringComparison.OrdinalIgnoreCase);
                default:
                    return true;
            }
        }

        private static string FormatDate(long utcTicks)
        {
            if (utcTicks <= 0L)
            {
                return "n/a";
            }

            return new DateTime(utcTicks, DateTimeKind.Utc).ToLocalTime()
                .ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static string FormatDateTime(long utcTicks)
        {
            if (utcTicks <= 0L)
            {
                return "n/a";
            }

            return new DateTime(utcTicks, DateTimeKind.Utc).ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        #endregion

        #region Lists and inventory

        private static AdminTools GetAdminTools()
        {
            try
            {
                return GameManager.Instance != null ? GameManager.Instance.adminTools : null;
            }
            catch
            {
                return null;
            }
        }

        private List<AdminBlacklist.BannedUser> GetBanList()
        {
            AdminTools tools = GetAdminTools();
            if (tools == null || tools.Blacklist == null)
            {
                return new List<AdminBlacklist.BannedUser>();
            }

            try
            {
                List<AdminBlacklist.BannedUser> list = tools.Blacklist.GetBanned();
                return list ?? new List<AdminBlacklist.BannedUser>();
            }
            catch
            {
                return new List<AdminBlacklist.BannedUser>();
            }
        }

        private Dictionary<PlatformUserIdentifierAbs, AdminUsers.UserPermission> GetAdminUsers()
        {
            AdminTools tools = GetAdminTools();
            if (tools == null || tools.Users == null)
            {
                return new Dictionary<PlatformUserIdentifierAbs, AdminUsers.UserPermission>();
            }

            try
            {
                Dictionary<PlatformUserIdentifierAbs, AdminUsers.UserPermission> users = tools.Users.GetUsers();
                return users ?? new Dictionary<PlatformUserIdentifierAbs, AdminUsers.UserPermission>();
            }
            catch
            {
                return new Dictionary<PlatformUserIdentifierAbs, AdminUsers.UserPermission>();
            }
        }

        private Dictionary<string, AdminUsers.GroupPermission> GetAdminGroups()
        {
            AdminTools tools = GetAdminTools();
            if (tools == null || tools.Users == null)
            {
                return new Dictionary<string, AdminUsers.GroupPermission>();
            }

            try
            {
                Dictionary<string, AdminUsers.GroupPermission> groups = tools.Users.GetGroups();
                return groups ?? new Dictionary<string, AdminUsers.GroupPermission>();
            }
            catch
            {
                return new Dictionary<string, AdminUsers.GroupPermission>();
            }
        }

        private Dictionary<PlatformUserIdentifierAbs, AdminWhitelist.WhitelistUser> GetWhitelistUsers()
        {
            AdminTools tools = GetAdminTools();
            if (tools == null || tools.Whitelist == null)
            {
                return new Dictionary<PlatformUserIdentifierAbs, AdminWhitelist.WhitelistUser>();
            }

            try
            {
                Dictionary<PlatformUserIdentifierAbs, AdminWhitelist.WhitelistUser> users = tools.Whitelist.GetUsers();
                return users ?? new Dictionary<PlatformUserIdentifierAbs, AdminWhitelist.WhitelistUser>();
            }
            catch
            {
                return new Dictionary<PlatformUserIdentifierAbs, AdminWhitelist.WhitelistUser>();
            }
        }

        private Dictionary<string, AdminWhitelist.WhitelistGroup> GetWhitelistGroups()
        {
            AdminTools tools = GetAdminTools();
            if (tools == null || tools.Whitelist == null)
            {
                return new Dictionary<string, AdminWhitelist.WhitelistGroup>();
            }

            try
            {
                Dictionary<string, AdminWhitelist.WhitelistGroup> groups = tools.Whitelist.GetGroups();
                return groups ?? new Dictionary<string, AdminWhitelist.WhitelistGroup>();
            }
            catch
            {
                return new Dictionary<string, AdminWhitelist.WhitelistGroup>();
            }
        }

        private bool IsWhitelistEnabled()
        {
            AdminTools tools = GetAdminTools();
            if (tools == null || tools.Whitelist == null)
            {
                return false;
            }

            try
            {
                return tools.Whitelist.IsWhiteListEnabled();
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeInventorySection(string section)
        {
            if (string.Equals(section, "toolbelt", StringComparison.OrdinalIgnoreCase))
            {
                return "toolbelt";
            }

            if (string.Equals(section, "equipment", StringComparison.OrdinalIgnoreCase))
            {
                return "equipment";
            }

            return "backpack";
        }

        private static string SectionLabel(string section)
        {
            if (string.Equals(section, "toolbelt", StringComparison.OrdinalIgnoreCase))
            {
                return "Toolbelt";
            }

            if (string.Equals(section, "equipment", StringComparison.OrdinalIgnoreCase))
            {
                return "Equipment";
            }

            return "Backpack";
        }

        private List<InvItem> GetInventoryItems(EntityPlayer player, string section)
        {
            List<InvItem> result = new List<InvItem>();
            if (player == null)
            {
                return result;
            }

            string normalized = NormalizeInventorySection(section);
            if (normalized == "toolbelt" && player.inventory != null)
            {
                for (int i = 0; i < player.inventory.SlotCount; i++)
                {
                    ItemStack stack = player.inventory.GetStackAt(i);
                    AddInvItem(result, normalized, i, stack, 0);
                }

                return result;
            }

            if (normalized == "backpack" && player.bag != null && player.bag.ItemGrid != null)
            {
                for (int i = 0; i < player.bag.ItemGrid.Length; i++)
                {
                    ItemStack stack = player.bag.ItemGrid.GetItem(i);
                    AddInvItem(result, normalized, i, stack, 0);
                }

                return result;
            }

            if (normalized == "equipment" && player.equipment != null)
            {
                for (int i = 0; i < player.equipment.GetSlotCount(); i++)
                {
                    ItemValue value = player.equipment.GetSlotItem(i);
                    if (value == null || value.IsEmpty())
                    {
                        continue;
                    }

                    result.Add(CreateInvItem(normalized, i, value, 1));
                }
            }

            return result;
        }

        private static void AddInvItem(List<InvItem> list, string section, int index, ItemStack stack, int unused)
        {
            if (stack == null || stack.IsEmpty() || stack.itemValue == null || stack.itemValue.IsEmpty())
            {
                return;
            }

            list.Add(CreateInvItem(section, index, stack.itemValue, stack.count));
        }

        private static InvItem CreateInvItem(string section, int index, ItemValue value, int count)
        {
            ItemClass itemClass = value != null ? value.ItemClass : null;
            string internalName = itemClass != null && !string.IsNullOrEmpty(itemClass.Name) ? itemClass.Name : "unknown";
            string displayName = ItemDisplayName(value);
            string category = ItemCategory(itemClass);
            float percent = -1f;
            try
            {
                if (value != null && value.MaxUseTimes > 0)
                {
                    percent = value.PercentUsesLeft;
                }
            }
            catch
            {
                percent = -1f;
            }

            return new InvItem
            {
                Section = section,
                Index = index,
                Name = displayName,
                InternalName = internalName,
                Category = category,
                Icon = ItemIcon(itemClass, category),
                Count = count,
                Quality = value != null ? value.Quality : (ushort)0,
                Percent = percent,
                Data = SerializeItemValue(value)
            };
        }

        private InvItem GetInventoryItem(EntityPlayer player, string section, int index)
        {
            List<InvItem> items = GetInventoryItems(player, section);
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Index == index)
                {
                    return items[i];
                }
            }

            return null;
        }

        private static string QualityText(int quality)
        {
            return quality > 0 ? "  |  Q" + quality : string.Empty;
        }

        private static string DurabilityText(float percent)
        {
            return percent < 0f ? string.Empty : "  |  " + (int)(percent * 100f) + "%";
        }

        private static string ItemDisplayName(ItemClass itemClass)
        {
            if (itemClass == null)
            {
                return "Unknown item";
            }

            try
            {
                string name = itemClass.GetLocalizedItemName();
                if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }

                name = itemClass.GetItemName();
                if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }
            }
            catch
            {
                // Fall through to the internal name.
            }

            return string.IsNullOrEmpty(itemClass.Name) ? "Unknown item" : itemClass.Name;
        }

        private static string ItemDisplayName(ItemValue value)
        {
            if (value == null || value.IsEmpty())
            {
                return "Unknown item";
            }

            return ItemDisplayName(value.ItemClass);
        }

        private static string ItemIcon(ItemClass item, string category)
        {
            if (item != null)
            {
                try
                {
                    string iconName = item.GetIconName();
                    if (!string.IsNullOrEmpty(iconName) && !string.IsNullOrEmpty(item.Name))
                    {
                        // Let ServerAssist resolve the live item sprite from the client's
                        // item classes and loaded mod atlases.
                        return "item:" + item.Name;
                    }
                }
                catch
                {
                    // Fall back to the category art below.
                }
            }

            return ItemCategoryIcon(category);
        }

        private static string ItemCategory(ItemClass item)
        {
            if (item == null)
            {
                return "misc";
            }

            try
            {
                if (item.IsBlock())
                {
                    return "block";
                }
            }
            catch
            {
                // Ignore and keep classifying.
            }

            string groups = string.Empty;
            try
            {
                string[] values = item.Groups;
                if (values != null && values.Length > 0)
                {
                    groups = string.Join(",", values);
                }
            }
            catch
            {
                groups = string.Empty;
            }

            try
            {
                groups += "," + item.ItemTags.ToString();
            }
            catch
            {
                // Tags are optional classification hints.
            }

            string lower = groups.ToLowerInvariant();
            if (lower.Contains("ranged weapons") || lower.Contains("melee weapons") || lower.Contains("robotics"))
            {
                return "weapon";
            }

            if (lower.Contains("ammo"))
            {
                return "ammo";
            }

            if (lower.Contains("food") || lower.Contains("drink"))
            {
                return "food";
            }

            if (lower.Contains("medical"))
            {
                return "medical";
            }

            if (lower.Contains("armor") || lower.Contains("clothing"))
            {
                return "armor";
            }

            if (lower.Contains("tools") || lower.Contains("traps"))
            {
                return "tool";
            }

            if (lower.Contains("mods"))
            {
                return "mod";
            }

            if (lower.Contains("vehicle"))
            {
                return "vehicle";
            }

            if (lower.Contains("books") || lower.Contains("schematics"))
            {
                return "book";
            }

            if (lower.Contains("resources") || lower.Contains("chemicals"))
            {
                return "resource";
            }

            if (lower.Contains("decor"))
            {
                return "decor";
            }

            try
            {
                if (item.IsGun())
                {
                    return "weapon";
                }
            }
            catch
            {
                // Ignore and keep classifying.
            }

            try
            {
                if (item.HasQuality)
                {
                    return "weapon";
                }
            }
            catch
            {
                // Ignore and fall through to misc.
            }

            return "misc";
        }

        private static string ItemCategoryLabel(string category)
        {
            switch (category)
            {
                case "weapon":
                    return "WEAPON";
                case "ammo":
                    return "AMMO";
                case "food":
                    return "FOOD";
                case "medical":
                    return "MED";
                case "armor":
                    return "ARMOR";
                case "tool":
                    return "TOOL";
                case "mod":
                    return "MOD";
                case "vehicle":
                    return "VEHICLE";
                case "book":
                    return "BOOK";
                case "resource":
                    return "RES";
                case "block":
                    return "BLOCK";
                case "decor":
                    return "DECOR";
                default:
                    return "ITEM";
            }
        }

        private static string ItemCategoryIcon(string category)
        {
            switch (category)
            {
                case "food":
                    return "sprite:sa_act_feed";
                case "medical":
                    return "sprite:sa_act_heal";
                case "armor":
                case "mod":
                    return "sprite:sa_act_repair";
                case "tool":
                    return "sprite:sa_act_repair";
                case "block":
                case "decor":
                    return "sprite:sa_nav_world";
                case "book":
                    return "sprite:sa_nav_help";
                case "vehicle":
                    return "sprite:sa_nav_teleport";
                case "weapon":
                case "ammo":
                    return "sprite:sa_act_give";
                default:
                    return "sprite:sa_nav_items";
            }
        }

        private static string ItemCategoryTint(string category)
        {
            switch (category)
            {
                case "weapon":
                    return "#FF8C66";
                case "ammo":
                    return "#FFCC66";
                case "food":
                    return "#FFD700";
                case "medical":
                    return "#FF6B6B";
                case "armor":
                    return "#66CCFF";
                case "tool":
                    return "#66FF99";
                case "mod":
                    return "#99FFFF";
                case "vehicle":
                    return "#66CCFF";
                case "book":
                    return "#CC99FF";
                case "resource":
                    return "#C0C0C0";
                case "block":
                    return "#B0A080";
                case "decor":
                    return "#CCCCCC";
                default:
                    return "#CCCCCC";
            }
        }

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

        private List<ItemClass> GetItemSearchResults(string query)
        {
            List<ItemClass> result = new List<ItemClass>();
            if (ItemClass.list == null)
            {
                return result;
            }

            string needle = string.IsNullOrEmpty(query) ? string.Empty : query.Trim().ToLowerInvariant();
            for (int i = 0; i < ItemClass.list.Length && result.Count < 200; i++)
            {
                ItemClass item = ItemClass.list[i];
                if (item == null)
                {
                    continue;
                }

                if (needle.Length == 0 ||
                    (!string.IsNullOrEmpty(item.Name) && item.Name.ToLowerInvariant().IndexOf(needle, StringComparison.Ordinal) >= 0) ||
                    ItemDisplayName(item).ToLowerInvariant().IndexOf(needle, StringComparison.Ordinal) >= 0)
                {
                    result.Add(item);
                }
            }

            return result.OrderBy(ItemDisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        #endregion

        #region Item serialization

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
                Interface.Oxide.LogError("[AdminSuite] could not serialize an item: " + exception);
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
                Interface.Oxide.LogError("[AdminSuite] could not deserialize an item: " + exception);
                return null;
            }
        }

        #endregion

        #region Server preferences

        private static string SafePrefString(EnumGamePrefs pref)
        {
            try
            {
                return GamePrefs.GetString(pref);
            }
            catch
            {
                return "unknown";
            }
        }

        private static int SafePrefInt(EnumGamePrefs pref)
        {
            try
            {
                return GamePrefs.GetInt(pref);
            }
            catch
            {
                return 0;
            }
        }

        private static float SafePrefFloat(EnumGamePrefs pref)
        {
            try
            {
                return GamePrefs.GetFloat(pref);
            }
            catch
            {
                return 0f;
            }
        }

        private static string GetServerName()
        {
            string name = SafePrefString(EnumGamePrefs.ServerName);
            if (string.IsNullOrEmpty(name) || string.Equals(name, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                name = SafePrefString(EnumGamePrefs.GameName);
            }

            return string.IsNullOrEmpty(name) ? "7 Days to Die" : name;
        }

        private static string GetServerWorld()
        {
            string world = SafePrefString(EnumGamePrefs.GameWorld);
            string name = SafePrefString(EnumGamePrefs.GameName);
            return (string.IsNullOrEmpty(world) ? "unknown" : world) + " / " +
                   (string.IsNullOrEmpty(name) ? "unknown" : name);
        }

        private static string GetGameMode()
        {
            return SafePrefString(EnumGamePrefs.GameMode);
        }

        private static string GetDifficultyName()
        {
            int difficulty = SafePrefInt(EnumGamePrefs.GameDifficulty);
            switch (difficulty)
            {
                case 0:
                    return "Scavenger";
                case 1:
                    return "Adventurer";
                case 2:
                    return "Nomad";
                case 3:
                    return "Warrior";
                case 4:
                    return "Survivalist";
                case 5:
                    return "Insane";
                default:
                    return difficulty.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static int GetMaxPlayers()
        {
            return SafePrefInt(EnumGamePrefs.ServerMaxPlayerCount);
        }

        #endregion

        #region Formatting and parsing

        private static int ParseInt(string value, int fallback)
        {
            int result;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
                ? result
                : fallback;
        }

        private static float ParseFloat(string value, float fallback)
        {
            float result;
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                ? result
                : fallback;
        }

        private static bool ParseCoordinates(string text, Vector3 fallback, out float x, out float y, out float z)
        {
            x = fallback.x;
            y = fallback.y;
            z = fallback.z;

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string[] parts = text.Trim().Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                    !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
                {
                    return false;
                }

                y = -1f;
                return true;
            }

            if (parts.Length == 3)
            {
                if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                    !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) ||
                    !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
                {
                    return false;
                }

                if (y < 0f)
                {
                    y = fallback.y;
                }

                return true;
            }

            return false;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        private static float ClampFloat(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        private static string SanitizeName(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            string trimmed = value.Trim();
            for (int i = 0; i < trimmed.Length && builder.Length < maxLength; i++)
            {
                char c = trimmed[i];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                {
                    builder.Append(c);
                }
                else if ((c == ' ' || c == '.') && builder.Length > 0 && builder[builder.Length - 1] != '-')
                {
                    builder.Append('-');
                }
            }

            return builder.ToString().Trim('-');
        }

        private static string Cut(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            value = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return value.Length > maxLength ? value.Substring(0, maxLength) : value;
        }

        private static string Quote(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "\"\"";
            }

            return "\"" + value.Replace("\"", "'").Replace("\r", " ").Replace("\n", " ") + "\"";
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

        private static string ConsoleTarget(IPlayer player)
        {
            EntityPlayer entity = GetEntity(player);
            if (entity != null)
            {
                return entity.entityId.ToString(CultureInfo.InvariantCulture);
            }

            return Quote(player != null ? player.Name : "server_console");
        }

        private static string TargetArgs(IPlayer player, string fallbackName)
        {
            EntityPlayer entity = GetEntity(player);
            if (entity != null)
            {
                return entity.entityId.ToString(CultureInfo.InvariantCulture);
            }

            if (player != null && !string.IsNullOrEmpty(player.Id))
            {
                return GuessPlatform(player.Id) + " " + Quote(player.Id);
            }

            return Quote(string.IsNullOrEmpty(fallbackName) ? "unknown" : fallbackName.Trim());
        }

        private void Reply(IPlayer player, string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            string prefix = config != null && !string.IsNullOrEmpty(config.ChatPrefix)
                ? config.ChatPrefix
                : "[AdminSuite]";
            string line = prefix + " " + message;

            if (player != null && player.IsConnected)
            {
                player.Reply(line);
            }
            else
            {
                Interface.Oxide.LogInfo(line);
            }
        }

        private bool IsStaff(string playerId)
        {
            return IsAdmin(playerId) ||
                   permission.UserHasPermission(playerId, PermKick) ||
                   permission.UserHasPermission(playerId, PermBan) ||
                   permission.UserHasPermission(playerId, PermMute) ||
                   permission.UserHasPermission(playerId, PermFreeze);
        }

        private void NotifyStaff(string message, string exceptPlayerId)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            foreach (IPlayer player in players.Connected)
            {
                if (player == null || string.Equals(player.Id, exceptPlayerId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (IsStaff(player.Id))
                {
                    Reply(player, message);
                }
            }
        }

        #endregion

        #region Ban, whitelist and admin commands

        private static PlatformUserIdentifierAbs GetIdentifier(IPlayer player)
        {
            if (player == null)
            {
                return null;
            }

            try
            {
                ClientInfo client = GetClientInfoStatic(player);
                if (client != null && client.PlatformId != null)
                {
                    return client.PlatformId;
                }
            }
            catch
            {
                // Fall through to the platform/id heuristic for offline players.
            }

            try
            {
                string platform = GuessPlatform(player.Id);
                return PlatformUserIdentifierAbs.FromPlatformAndId(platform, player.Id, false);
            }
            catch
            {
                return null;
            }
        }

        private static string GuessPlatform(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return "Steam";
            }

            bool digitsOnly = true;
            for (int i = 0; i < playerId.Length; i++)
            {
                if (!char.IsDigit(playerId[i]))
                {
                    digitsOnly = false;
                    break;
                }
            }

            if (digitsOnly && playerId.Length == 17)
            {
                return "Steam";
            }

            if (playerId.StartsWith("0002", StringComparison.OrdinalIgnoreCase))
            {
                return "EOS";
            }

            return "Steam";
        }

        private static ClientInfo GetClientInfoStatic(IPlayer player)
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

                if (ConnectionManager.Instance == null || ConnectionManager.Instance.Clients == null)
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
                        string.Equals(client.PlatformId.ReadablePlatformUserIdentifier, player.Id, StringComparison.OrdinalIgnoreCase))
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

        private string AddBan(IPlayer target, DateTime until, string reason)
        {
            if (target == null)
            {
                return "Player not found.";
            }

            AdminTools tools = GetAdminTools();
            PlatformUserIdentifierAbs identifier = GetIdentifier(target);
            if (tools != null && tools.Blacklist != null && identifier != null)
            {
                try
                {
                    tools.Blacklist.AddBan(target.Name, identifier, until, reason);
                    tools.Save();
                    if (target.IsConnected)
                    {
                        target.Kick(reason);
                    }

                    return null;
                }
                catch (Exception exception)
                {
                    Interface.Oxide.LogWarning("[AdminSuite] direct ban failed: " + exception.Message);
                }
            }

            long minutes = until == DateTime.MaxValue
                ? 52560000L
                : (long)Math.Max(1.0, (until - DateTime.UtcNow).TotalMinutes);

            string value;
            string unit;
            if (minutes >= 525600L)
            {
                value = (minutes / 525600L).ToString(CultureInfo.InvariantCulture);
                unit = "year";
            }
            else if (minutes >= 43200L)
            {
                value = (minutes / 43200L).ToString(CultureInfo.InvariantCulture);
                unit = "month";
            }
            else if (minutes >= 10080L)
            {
                value = (minutes / 10080L).ToString(CultureInfo.InvariantCulture);
                unit = "week";
            }
            else if (minutes >= 1440L)
            {
                value = (minutes / 1440L).ToString(CultureInfo.InvariantCulture);
                unit = "day";
            }
            else if (minutes >= 60L)
            {
                value = (minutes / 60L).ToString(CultureInfo.InvariantCulture);
                unit = "hour";
            }
            else
            {
                value = Math.Max(1L, minutes).ToString(CultureInfo.InvariantCulture);
                unit = "minute";
            }

            ExecuteConsole("ban add " + GuessPlatform(target.Id) + " " + Quote(target.Id) + " " +
                           value + " " + unit + " " + Quote(reason));
            if (target.IsConnected)
            {
                target.Kick(reason);
            }

            return null;
        }

        private void WhitelistAdd(string actorId, Session session, string text)
        {
            if (!HasTool(actorId, PermWhitelist))
            {
                session.Status = "You need " + PermWhitelist + ".";
                return;
            }

            if (string.IsNullOrEmpty(text))
            {
                session.Status = "Enter a player name or id.";
                return;
            }

            IPlayer target = ResolvePlayer(text);
            ExecuteConsole("whitelist add " + TargetArgs(target, text));
            session.Status = "Whitelist add requested for " + text + ".";
            AddLog("admin", GetPlayerName(actorId), text, "whitelist add");
        }

        private void WhitelistRemove(string actorId, Session session, string text)
        {
            if (!HasTool(actorId, PermWhitelist))
            {
                session.Status = "You need " + PermWhitelist + ".";
                return;
            }

            if (string.IsNullOrEmpty(text))
            {
                session.Status = "Enter a player name or id.";
                return;
            }

            IPlayer target = ResolvePlayer(text);
            ExecuteConsole("whitelist remove " + TargetArgs(target, text));
            session.Status = "Whitelist remove requested for " + text + ".";
            AddLog("admin", GetPlayerName(actorId), text, "whitelist remove");
        }

        private void AdminAdd(string actorId, Session session, string text)
        {
            if (!HasTool(actorId, PermAdminList))
            {
                session.Status = "You need " + PermAdminList + ".";
                return;
            }

            string[] parts = (text ?? string.Empty).Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                session.Status = "Use: player name or id [level]";
                return;
            }

            string namePart = parts[0];
            string level = parts.Length > 1 ? parts[1] : "0";
            IPlayer target = ResolvePlayer(namePart);
            ExecuteConsole("admin add " + TargetArgs(target, namePart) + " " + level);
            session.Status = "Admin add requested for " + namePart + " at level " + level + ".";
            AddLog("admin", GetPlayerName(actorId), namePart, "admin add level " + level);
        }

        private void AdminRemove(string actorId, Session session, string text)
        {
            if (!HasTool(actorId, PermAdminList))
            {
                session.Status = "You need " + PermAdminList + ".";
                return;
            }

            if (string.IsNullOrEmpty(text))
            {
                session.Status = "Enter a player name or id.";
                return;
            }

            IPlayer target = ResolvePlayer(text);
            ExecuteConsole("admin remove " + TargetArgs(target, text));
            session.Status = "Admin remove requested for " + text + ".";
            AddLog("admin", GetPlayerName(actorId), text, "admin remove");
        }

        #endregion

        #region Command implementations

        private void CommandHelp(IPlayer player)
        {
            Reply(player, "AdminSuite " + Version + " - staff toolkit");
            Reply(player, "/as - open the GUI (or /adminsuite)");
            Reply(player, "/as help - this list");
            Reply(player, "/as list - list online players");
            Reply(player, "/as who <player> - detailed player information");
            Reply(player, "/as nearby [radius] - players near you");
            Reply(player, "/as find <item> - search the item catalog");
            Reply(player, "/as tp <player|x y z> - teleport yourself");
            Reply(player, "/as bring <player> - teleport a player to you");
            Reply(player, "/as ontop <player> - spawn on top of a player");
            Reply(player, "/as spectate <player> - teleport near and spectate a player");
            Reply(player, "/as inv <player> - open the inventory inspector for a player");
            Reply(player, "/as jail <player> - teleport a player to jail and freeze them");
            Reply(player, "/as kick <player> [reason] - kick a player");
            Reply(player, "/as kickall <reason> confirm - kick everyone");
            Reply(player, "/as ban <player> <duration> [reason] confirm - ban a player");
            Reply(player, "/as unban <player|id> - remove a ban");
            Reply(player, "/as banlist | /as adminlist | /as whitelist - server lists");
            Reply(player, "/as admin add <player> [level] | /as admin remove <player>");
            Reply(player, "/as whitelist add <player> | /as whitelist remove <player>");
            Reply(player, "/as clearmutes | /as clearwarnings - clear moderation lists");
            Reply(player, "/as mute <player> <duration> [reason] - mute chat");
            Reply(player, "/as unmute <player> - remove a mute");
            Reply(player, "/as timeout <player> <duration> [reason] - mute and freeze");
            Reply(player, "/as freeze <player> [duration] - freeze a player");
            Reply(player, "/as unfreeze <player> - release a player");
            Reply(player, "/as god <player> - toggle god mode");
            Reply(player, "/as fly [player] - fly in single-player; on a dedicated server this opens your debug menu");
            Reply(player, "/as heal <player> | /as feed <player> | /as kill <player>");
            Reply(player, "/as healall | /as feedall - heal or feed every online player");
            Reply(player, "/as buff <player> <buff> | /as debuff <player> <buff> - manage buffs");
            Reply(player, "/as give <player> <item> [amount] [quality] - give an item");
            Reply(player, "/as clear <player> confirm - clear inventory");
            Reply(player, "/as take <player> confirm - take all items");
            Reply(player, "/as repair <player> - repair all items");
            Reply(player, "/as warn <player> <message> | /as note <player> [message]");
            Reply(player, "/as time <day|night|day hour minute> - set world time");
            Reply(player, "/as weather <clear|rain|storm> [hours] - weather control");
            Reply(player, "/as airdrop | /as horde | /as scouts - spawn events");
            Reply(player, "/as spawn <entity> [count] - spawn an entity");
            Reply(player, "/as save - save the world");
            Reply(player, "/as say <message> - broadcast");
            Reply(player, "/as warps | /as warp <name> | /as setwarp <name> | /as delwarp <name>");
            Reply(player, "/as snapshot <player> [reason] - capture a snapshot");
            Reply(player, "/as snapshots [player] - list snapshots");
            Reply(player, "/as rollback <player> [snapshotId] confirm - restore a snapshot");
            Reply(player, "/as logs [count] [filter] - show recent audit logs");
            Reply(player, "/as creative - toggle the creative menu on your client");
            Reply(player, "/as debug - toggle the debug menu (admin or world permission)");
            Reply(player, "/as spectator - toggle spectator mode");
            Reply(player, "/as playerlist - open the vanilla player list");
            Reply(player, "/as tppoi - open the POI teleporter");
            Reply(player, "/as killall confirm - kill all non-player entities");
            Reply(player, "/as reload - reload config, data, snapshots and the hook");
            Reply(player, "Durations: 30m, 1h, 7d, 2w, 1mo, 1y, permanent.");
        }

        private void CommandList(IPlayer player)
        {
            if (!HasTool(player != null ? player.Id : "server_console", PermPlayersView))
            {
                Reply(player, "You need " + PermPlayersView + " to list players.");
                return;
            }

            List<IPlayer> online = players.Connected
                .Where(p => p != null)
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Reply(player, "Online players: " + online.Count);
            for (int i = 0; i < online.Count && i < 30; i++)
            {
                IPlayer p = online[i];
                EntityPlayer entity = GetEntity(p);
                Reply(player, PlayerTag(player != null ? player.Id : "server_console", p) + p.Name +
                             " (" + SafeId(p) + ")  ping " + SafePing(p) + "  " + HealthSummary(entity));
            }

            if (online.Count > 30)
            {
                Reply(player, "...and " + (online.Count - 30) + " more. Use the GUI player list.");
            }
        }

        private void CommandWho(IPlayer player, string[] args)
        {
            if (!HasTool(player != null ? player.Id : "server_console", PermPlayersView))
            {
                Reply(player, "You need " + PermPlayersView + " to inspect players.");
                return;
            }

            if (args.Length < 2)
            {
                Reply(player, "Usage: /as who <player>");
                return;
            }

            IPlayer target = ResolvePlayer(args[1]);
            if (target == null)
            {
                Reply(player, "Player '" + args[1] + "' was not found.");
                return;
            }

            EntityPlayer entity = GetEntity(target);
            Reply(player, target.Name + " (" + SafeId(target) + ")");
            Reply(player, "Online: " + target.IsConnected +
                         "  |  admin: " + (SafeIsAdmin(target) || permission.UserHasPermission(target.Id, PermAdmin)) +
                         "  |  ping: " + SafePing(target));
            Reply(player, "Address: " + SafeAddress(target) + "  |  health: " + HealthSummary(entity));
            if (entity != null)
            {
                Vector3 position = entity.GetPosition();
                Reply(player, "Position: " + FormatVector(position) + "  |  biome: " + GetBiomeName(position));
                Reply(player, "Game stage: " + entity.gameStage + "  |  zombie kills: " + entity.KilledZombies +
                             "  |  player kills: " + entity.KilledPlayers);
                Reply(player, "Inventory: " + InventorySummary(entity));
                Reply(player, "Vitals: " + VitalsSummary(entity));
            }

            Reply(player, "Moderation: " + ModerationSummary(target.Id));
            string note = GetNoteText(target.Id);
            if (!string.IsNullOrEmpty(note))
            {
                Reply(player, "Staff note: " + note);
            }
        }

        private void CommandNearby(IPlayer player, string[] args)
        {
            if (!HasTool(player != null ? player.Id : "server_console", PermPlayersView))
            {
                Reply(player, "You need " + PermPlayersView + " to list nearby players.");
                return;
            }

            EntityPlayer actor = GetEntity(player);
            if (actor == null)
            {
                Reply(player, "You must be spawned in the world.");
                return;
            }

            float radius = args.Length > 1 ? ParseFloat(args[1], 100f) : 100f;
            if (radius <= 0f)
            {
                radius = 100f;
            }

            Vector3 origin = actor.GetPosition();
            List<IPlayer> nearby = new List<IPlayer>();
            foreach (IPlayer p in players.Connected)
            {
                EntityPlayer other = GetEntity(p);
                if (p == null || other == null || other.entityId == actor.entityId)
                {
                    continue;
                }

                if (Vector3.Distance(origin, other.GetPosition()) <= radius)
                {
                    nearby.Add(p);
                }
            }

            nearby = nearby
                .OrderBy(p => Vector3.Distance(origin, GetEntityPosition(GetEntity(p))))
                .ToList();

            Reply(player, "Players within " + (int)radius + "m: " + nearby.Count);
            for (int i = 0; i < nearby.Count; i++)
            {
                IPlayer p = nearby[i];
                EntityPlayer other = GetEntity(p);
                Reply(player, p.Name + " (" + SafeId(p) + ")  " +
                             (int)Vector3.Distance(origin, GetEntityPosition(other)) + "m");
            }
        }

        private void CommandFind(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /as find <item>");
                return;
            }

            string query = JoinArgs(args, 1);
            List<ItemClass> results = GetItemSearchResults(query);
            Reply(player, "Item results for '" + query + "': " + results.Count);
            for (int i = 0; i < results.Count && i < 20; i++)
            {
                ItemClass item = results[i];
                Reply(player, ItemDisplayName(item) + "  |  " + item.Name +
                             "  |  max stack " + GetMaxStack(item));
            }

            if (results.Count > 20)
            {
                Reply(player, "...and " + (results.Count - 20) + " more. Use the GUI item search for the full list.");
            }
        }

        private void CommandTeleport(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /as tp <player | x y z | x z>");
                return;
            }

            string actorId = player != null ? player.Id : "server_console";
            IPlayer target = ResolvePlayer(args[1]);
            if (target != null)
            {
                Session session = new Session();
                session.SelectedPlayerId = target.Id;
                session.SelectedPlayerName = target.Name;
                TeleportToSelected(actorId, session);
                Reply(player, session.Status);
                return;
            }

            Session coordinateSession = new Session();
            TeleportSelfToCoords(actorId, coordinateSession, JoinArgs(args, 1));
            Reply(player, coordinateSession.Status);
        }

        private void CommandBring(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /as bring <player>");
                return;
            }

            IPlayer target = ResolvePlayer(args[1]);
            if (target == null)
            {
                Reply(player, "Player '" + args[1] + "' was not found.");
                return;
            }

            Session session = new Session();
            session.SelectedPlayerId = target.Id;
            session.SelectedPlayerName = target.Name;
            BringSelected(player != null ? player.Id : "server_console", session);
            Reply(player, session.Status);
        }

        private void CommandOnTop(IPlayer player, string[] args)
        {
            if (player == null)
            {
                Reply(player, "Spawn-on-top can only be used in-game.");
                return;
            }

            if (args.Length < 2)
            {
                Reply(player, "Usage: /as ontop <player>");
                return;
            }

            if (!HasTool(player.Id, PermSpawnOnTop))
            {
                Reply(player, "You need " + PermSpawnOnTop + ".");
                return;
            }

            IPlayer target = ResolvePlayer(args[1]);
            EntityPlayer actor = GetEntity(player);
            EntityPlayer targetEntity = GetEntity(target);
            if (target == null || actor == null || targetEntity == null)
            {
                Reply(player, "Both players must be online and spawned.");
                return;
            }

            if (!CanActOn(player.Id, target, false, out string reason))
            {
                Reply(player, reason);
                return;
            }

            TeleportPlayerTo(player, targetEntity.GetPosition() + new Vector3(0f, 3f, 0f), targetEntity.rotation.y);
            Reply(player, "Spawned on top of " + target.Name + ".");
            AddLog("admin", player.Name, target.Name, "Spawned on top.");
        }

        private void CommandSpectatePlayer(IPlayer player, string[] args)
        {
            if (player == null)
            {
                Reply(player, "Spectating can only be used in-game.");
                return;
            }

            if (args.Length < 2)
            {
                Reply(player, "Usage: /as spectate <player>");
                return;
            }

            if (!HasTool(player.Id, PermSpectate))
            {
                Reply(player, "You need " + PermSpectate + ".");
                return;
            }

            IPlayer target = ResolvePlayer(args[1]);
            if (target == null)
            {
                Reply(player, "Player '" + args[1] + "' was not found.");
                return;
            }

            Session session = new Session();
            session.SelectedPlayerId = target.Id;
            session.SelectedPlayerName = target.Name;
            SpectateSelected(player.Id, session);
            Reply(player, session.Status);
        }

        private void CommandInventory(IPlayer player, string[] args)
        {
            if (player == null)
            {
                Reply(player, "The inventory viewer can only be opened by an in-game player.");
                return;
            }

            if (args.Length < 2)
            {
                Reply(player, "Usage: /as inv <player>");
                return;
            }

            if (!HasTool(player.Id, PermInventory))
            {
                Reply(player, "You need " + PermInventory + ".");
                return;
            }

            IPlayer target = ResolvePlayer(args[1]);
            if (target == null)
            {
                Reply(player, "Player '" + args[1] + "' was not found.");
                return;
            }

            if (!OpenGuiWithAction(player, "a|inventory_player|" + target.Id))
            {
                Reply(player, "Could not open the inventory viewer through ServerAssist.");
            }
        }

        private void CommandJail(IPlayer player, string[] args)
        {
            if (player == null)
            {
                Reply(player, "Jail can only be used in-game.");
                return;
            }

            if (args.Length < 2)
            {
                Reply(player, "Usage: /as jail <player>");
                return;
            }

            if (!HasTool(player.Id, PermFreeze))
            {
                Reply(player, "You need " + PermFreeze + ".");
                return;
            }

            IPlayer target = ResolvePlayer(args[1]);
            if (target == null)
            {
                Reply(player, "Player '" + args[1] + "' was not found.");
                return;
            }

            Session session = new Session();
            session.SelectedPlayerId = target.Id;
            session.SelectedPlayerName = target.Name;
            JailSelected(player.Id, session);
            Reply(player, session.Status);
        }

        private void CommandClearMutes(IPlayer player)
        {
            Session session = new Session();
            ClearMutes(player != null ? player.Id : "server_console", session);
            Reply(player, session.Status);
        }

        private void CommandClearWarnings(IPlayer player)
        {
            Session session = new Session();
            ClearWarnings(player != null ? player.Id : "server_console", session);
            Reply(player, session.Status);
        }

        private void CommandAdminAction(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /as admin add <player> [level] | /as admin remove <player> | /as admin list");
                return;
            }

            string sub = args[1].ToLowerInvariant();
            if (sub == "list")
            {
                CommandAdminList(player);
                return;
            }

            if (sub == "add")
            {
                if (args.Length < 3)
                {
                    Reply(player, "Usage: /as admin add <player> [level]");
                    return;
                }

                Session session = new Session();
                AdminAdd(player != null ? player.Id : "server_console", session, JoinArgs(args, 2));
                Reply(player, session.Status);
                return;
            }

            if (sub == "remove" || sub == "del")
            {
                if (args.Length < 3)
                {
                    Reply(player, "Usage: /as admin remove <player>");
                    return;
                }

                Session session = new Session();
                AdminRemove(player != null ? player.Id : "server_console", session, JoinArgs(args, 2));
                Reply(player, session.Status);
                return;
            }

            Reply(player, "Usage: /as admin add <player> [level] | /as admin remove <player> | /as admin list");
        }

        private void CommandWhitelistAction(IPlayer player, string[] args)
        {
            if (args.Length >= 2 && string.Equals(args[1], "add", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length < 3)
                {
                    Reply(player, "Usage: /as whitelist add <player>");
                    return;
                }

                Session session = new Session();
                WhitelistAdd(player != null ? player.Id : "server_console", session, JoinArgs(args, 2));
                Reply(player, session.Status);
                return;
            }

            if (args.Length >= 2 &&
                (string.Equals(args[1], "remove", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(args[1], "del", StringComparison.OrdinalIgnoreCase)))
            {
                if (args.Length < 3)
                {
                    Reply(player, "Usage: /as whitelist remove <player>");
                    return;
                }

                Session session = new Session();
                WhitelistRemove(player != null ? player.Id : "server_console", session, JoinArgs(args, 2));
                Reply(player, session.Status);
                return;
            }

            CommandWhitelist(player);
        }

        private void CommandKick(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /as kick <player> [reason]");
                return;
            }

            IPlayer target = ResolvePlayer(args[1]);
            if (target == null)
            {
                Reply(player, "Player '" + args[1] + "' was not found.");
                return;
            }

            Session session = new Session();
            session.SelectedPlayerId = target.Id;
            session.SelectedPlayerName = target.Name;
            session.PendingData = args.Length > 2 ? JoinArgs(args, 2) : "Kicked by staff";
            KickSelected(player != null ? player.Id : "server_console", session);
            Reply(player, session.Status);
        }

        private void CommandKickAll(IPlayer player, string[] args)
        {
            if (!HasConfirm(args))
            {
                Reply(player, "This kicks every player. Run /as kickall <reason> confirm.");
                return;
            }

            string reason = JoinArgsWithoutConfirm(args, 1);
            if (string.IsNullOrEmpty(reason))
            {
                reason = "Kicked by staff";
            }

            Session session = new Session();
            session.PendingData = reason;
            KickAll(player != null ? player.Id : "server_console", session);
            Reply(player, session.Status);
        }

        private void CommandBan(IPlayer player, string[] args)
        {
            if (args.Length < 3 || !HasConfirm(args))
            {
                Reply(player, "Usage: /as ban <player> <duration> [reason] confirm");
                return;
            }

            IPlayer target = ResolvePlayer(args[1]);
            if (target == null)
            {
                Reply(player, "Player '" + args[1] + "' was not found.");
                return;
            }

            string spec = args[2];
            string reason = JoinArgsWithoutConfirm(args, 3);
            if (string.IsNullOrEmpty(reason))
            {
                reason = "Banned by staff";
            }

            Session session = new Session();
            session.SelectedPlayerId = target.Id;
            session.SelectedPlayerName = target.Name;
            session.PendingData = spec + "\n" + reason;
            BanSelected(player != null ? player.Id : "server_console", session);
            Reply(player, session.Status);
        }

        private void CommandUnban(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /as unban <player name or id>");
                return;
            }

            if (!HasTool(player != null ? player.Id : "server_console", PermBan))
            {
                Reply(player, "You do not have permission to unban players.");
                return;
            }

            string value = args[1];
            List<AdminBlacklist.BannedUser> list = GetBanList();
            AdminBlacklist.BannedUser found = list.FirstOrDefault(ban =>
                string.Equals(ban.Name, value, StringComparison.OrdinalIgnoreCase) ||
                (ban.UserIdentifier != null &&
                 (string.Equals(ban.UserIdentifier.ToString(), value, StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(ban.UserIdentifier.ReadablePlatformUserIdentifier, value, StringComparison.OrdinalIgnoreCase))));

            if (found.UserIdentifier == null)
            {
                Reply(player, "No matching ban entry was found.");
                return;
            }

            AdminTools tools = GetAdminTools();
            if (tools == null || tools.Blacklist == null)
            {
                Reply(player, "The server admin tools are not available.");
                return;
            }

            tools.Blacklist.RemoveBan(found.UserIdentifier);
            tools.Save();
            Reply(player, "Removed the ban for " + (string.IsNullOrEmpty(found.Name) ? value : found.Name) + ".");
            AddLog("unban", player != null ? player.Name : "Console", found.Name, "Removed ban.");
        }

        private void CommandBanList(IPlayer player)
        {
            string actorId = player != null ? player.Id : "server_console";
            if (!HasTool(actorId, PermBanList) && !HasTool(actorId, PermBan))
            {
                Reply(player, "You need " + PermBanList + " or " + PermBan + ".");
                return;
            }

            List<AdminBlacklist.BannedUser> list = GetBanList();
            Reply(player, "Ban list: " + list.Count + " entr" + (list.Count == 1 ? "y" : "ies") + ".");
            for (int i = 0; i < list.Count && i < 30; i++)
            {
                AdminBlacklist.BannedUser ban = list[i];
                string until = ban.BannedUntil == DateTime.MaxValue || ban.BannedUntil == DateTime.MinValue
                    ? "permanent"
                    : FormatDateTime(ban.BannedUntil.ToUniversalTime().Ticks);
                Reply(player, (string.IsNullOrEmpty(ban.Name) ? "Unknown" : ban.Name) + "  |  " +
                             (ban.UserIdentifier != null ? ban.UserIdentifier.ToString() : "no id") +
                             "  |  " + until + "  |  " + ban.BanReason);
            }

            if (list.Count > 30)
            {
                Reply(player, "...and " + (list.Count - 30) + " more. Use the GUI ban list.");
            }
        }

        private void CommandAdminList(IPlayer player)
        {
            if (!HasTool(player != null ? player.Id : "server_console", PermAdminList))
            {
                Reply(player, "You need " + PermAdminList + ".");
                return;
            }

            Dictionary<PlatformUserIdentifierAbs, AdminUsers.UserPermission> users = GetAdminUsers();
            Dictionary<string, AdminUsers.GroupPermission> groups = GetAdminGroups();
            Reply(player, "Server admin users: " + users.Count + "  |  groups: " + groups.Count);
            foreach (KeyValuePair<PlatformUserIdentifierAbs, AdminUsers.UserPermission> pair in users)
            {
                Reply(player, (string.IsNullOrEmpty(pair.Value.Name) ? "Unknown" : pair.Value.Name) +
                             "  |  " + (pair.Key != null ? pair.Key.ToString() : "no id") +
                             "  |  level " + pair.Value.PermissionLevel);
            }

            foreach (KeyValuePair<string, AdminUsers.GroupPermission> pair in groups)
            {
                Reply(player, "Group " + (string.IsNullOrEmpty(pair.Value.Name) ? pair.Key : pair.Value.Name) +
                             "  |  " + pair.Value.SteamIdGroup +
                             "  |  levels " + pair.Value.PermissionLevelNormal + "/" + pair.Value.PermissionLevelMods);
            }
        }

        private void CommandWhitelist(IPlayer player)
        {
            if (!HasTool(player != null ? player.Id : "server_console", PermWhitelist))
            {
                Reply(player, "You need " + PermWhitelist + ".");
                return;
            }

            Dictionary<PlatformUserIdentifierAbs, AdminWhitelist.WhitelistUser> users = GetWhitelistUsers();
            Dictionary<string, AdminWhitelist.WhitelistGroup> groups = GetWhitelistGroups();
            Reply(player, "Whitelist is " + (IsWhitelistEnabled() ? "enabled" : "disabled") +
                         ". Users: " + users.Count + "  |  groups: " + groups.Count);
            foreach (KeyValuePair<PlatformUserIdentifierAbs, AdminWhitelist.WhitelistUser> pair in users)
            {
                Reply(player, (string.IsNullOrEmpty(pair.Value.Name) ? "Unknown" : pair.Value.Name) +
                             "  |  " + (pair.Key != null ? pair.Key.ToString() : "no id"));
            }

            foreach (KeyValuePair<string, AdminWhitelist.WhitelistGroup> pair in groups)
            {
                Reply(player, "Group " + (string.IsNullOrEmpty(pair.Value.Name) ? pair.Key : pair.Value.Name) +
                             "  |  " + pair.Value.SteamIdGroup);
            }
        }

        private void CommandMute(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /as mute <player> [duration] [reason]");
                return;
            }

            IPlayer target = ResolvePlayer(args[1]);
            if (target == null)
            {
                Reply(player, "Player '" + args[1] + "' was not found.");
                return;
            }

            string spec = args.Length > 2 ? args[2] : "30m";
            string reason = args.Length > 3 ? JoinArgs(args, 3) : "Muted by staff";
            Session session = new Session();
            session.SelectedPlayerId = target.Id;
            session.SelectedPlayerName = target.Name;
            session.PendingData = spec + "\n" + reason;
            MuteSelected(player != null ? player.Id : "server_console", session);
            Reply(player, session.Status);
        }

        private void CommandUnmute(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /as unmute <player>");
                return;
            }

            IPlayer target = ResolvePlayer(args[1]);
            if (target == null)
            {
                Reply(player, "Player '" + args[1] + "' was not found.");
                return;
            }

            if (!HasTool(player != null ? player.Id : "server_console", PermMute))
            {
                Reply(player, "You do not have permission to unmute players.");
                return;
            }

            RemoveMute(target.Id, true);
            Reply(player, "Unmuted " + target.Name + ".");
            AddLog("admin", player != null ? player.Name : "Console", target.Name, "Removed mute.");
        }

        private void CommandTimeout(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /as timeout <player> [duration] [reason]");
                return;
            }

            IPlayer target = ResolvePlayer(args[1]);
            if (target == null)
            {
                Reply(player, "Player '" + args[1] + "' was not found.");
                return;
            }

            string spec = args.Length > 2 ? args[2] : "30m";
            string reason = args.Length > 3 ? JoinArgs(args, 3) : "Timeout by staff";
            Session session = new Session();
            session.SelectedPlayerId = target.Id;
            session.SelectedPlayerName = target.Name;
            session.PendingData = spec + "\n" + reason;
            TimeoutSelected(player != null ? player.Id : "server_console", session);
            Reply(player, session.Status);
        }

        private void CommandFreeze(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /as freeze <player> [duration] [reason]");
                return;
            }

            IPlayer target = ResolvePlayer(args[1]);
            if (target == null)
            {
                Reply(player, "Player '" + args[1] + "' was not found.");
                return;
            }

            if (GetEntity(target) == null)
            {
                Reply(player, "That player is not online and spawned.");
                return;
            }

            if (!HasTool(player != null ? player.Id : "server_console", PermFreeze))
            {
                Reply(player, "You do not have permission to freeze players.");
                return;
            }

            long minutes = args.Length > 2 ? ParseDurationSpecMinutes(args[2]) : config.DefaultFreezeMinutes;
            if (minutes == long.MinValue)
            {
                minutes = config.DefaultFreezeMinutes;
            }

            string reason = args.Length > 3 ? JoinArgs(args, 3) : "Frozen by staff";
            ApplyFreeze(target, minutes, reason, player != null ? player.Name : "Console");
            Reply(player, "Froze " + target.Name + " (" + (minutes <= 0 ? "indefinite" : FormatMinutes(minutes)) + ").");
        }

        private void CommandUnfreeze(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /as unfreeze <player>");
                return;
            }

            IPlayer target = ResolvePlayer(args[1]);
            if (target == null)
            {
                Reply(player, "Player '" + args[1] + "' was not found.");
                return;
            }

            if (!HasTool(player != null ? player.Id : "server_console", PermFreeze))
            {
                Reply(player, "You do not have permission to unfreeze players.");
                return;
            }

            RemoveFreeze(target.Id, true);
            Reply(player, "Unfroze " + target.Name + ".");
            AddLog("admin", player != null ? player.Name : "Console", target.Name, "Removed freeze.");
        }

        private void CommandGod(IPlayer player, string[] args)
        {
            IPlayer target = args.Length > 1 ? ResolvePlayer(args[1]) : player;
            if (target == null)
            {
                Reply(player, "Usage: /as god <player>");
                return;
            }

            Session session = new Session();
            session.SelectedPlayerId = target.Id;
            session.SelectedPlayerName = target.Name;
            ToggleGodSelected(player != null ? player.Id : "server_console", session);
            Reply(player, session.Status);
        }

        private void CommandFly(IPlayer player, string[] args)
        {
            if (player == null)
            {
                Reply(player, "Fly mode can only be toggled by an in-game player.");
                return;
            }

            if (!HasTool(player.Id, PermFly))
            {
                Reply(player, "You need " + PermFly + ".");
                return;
            }

            IPlayer target = args.Length > 1 ? ResolvePlayer(args[1]) : player;
            if (target == null)
            {
                Reply(player, "Player '" + args[1] + "' was not found.");
                return;
            }

            Session session = new Session();
            session.SelectedPlayerId = target.Id;
            session.SelectedPlayerName = target.Name;
            ToggleFlySelected(player.Id, session);
            Reply(player, session.Status);
        }

        private void CommandHeal(IPlayer player, string[] args)
        {
            IPlayer target = args.Length > 1 ? ResolvePlayer(args[1]) : player;
            if (target == null)
            {
                Reply(player, "Usage: /as heal <player>");
                return;
            }

            Session session = new Session();
            session.SelectedPlayerId = target.Id;
            session.SelectedPlayerName = target.Name;
            HealSelected(player != null ? player.Id : "server_console", session);
            Reply(player, session.Status);
        }

        private void CommandFeed(IPlayer player, string[] args)
        {
            IPlayer target = args.Length > 1 ? ResolvePlayer(args[1]) : player;
            if (target == null)
            {
                Reply(player, "Usage: /as feed <player>");
                return;
            }

            Session session = new Session();
            session.SelectedPlayerId = target.Id;
            session.SelectedPlayerName = target.Name;
            FeedSelected(player != null ? player.Id : "server_console", session);
            Reply(player, session.Status);
        }

        private void CommandBuff(IPlayer player, string[] args)
        {
            if (args.Length < 3)
            {
                Reply(player, "Usage: /as buff <player> <buff name>");
                return;
            }

            IPlayer target = ResolvePlayer(args[1]);
            if (target == null)
            {
                Reply(player, "Player '" + args[1] + "' was not found.");
                return;
            }

            Session session = new Session();
            session.SelectedPlayerId = target.Id;
            session.SelectedPlayerName = target.Name;
            ApplyBuff(player != null ? player.Id : "server_console", session, JoinArgs(args, 2));
            Reply(player, session.Status);
        }

        private void CommandDebuff(IPlayer player, string[] args)
        {
            if (args.Length < 3)
            {
                Reply(player, "Usage: /as debuff <player> <buff name>");
                return;
            }

            IPlayer target = ResolvePlayer(args[1]);
            if (target == null)
            {
                Reply(player, "Player '" + args[1] + "' was not found.");
                return;
            }

            Session session = new Session();
            session.SelectedPlayerId = target.Id;
            session.SelectedPlayerName = target.Name;
            RemoveBuff(player != null ? player.Id : "server_console", session, JoinArgs(args, 2));
            Reply(player, session.Status);
        }

        private void CommandHealAll(IPlayer player)
        {
            Session session = new Session();
            HealAll(player != null ? player.Id : "server_console", session);
            Reply(player, session.Status);
        }

        private void CommandFeedAll(IPlayer player)
        {
            Session session = new Session();
            FeedAll(player != null ? player.Id : "server_console", session);
            Reply(player, session.Status);
        }

        private void CommandCreative(IPlayer player)
        {
            if (player == null)
            {
                Reply(player, "The creative menu can only be toggled by an in-game player.");
                return;
            }

            Session session = new Session();
            ToggleCreativeMenu(player.Id, session);
            Reply(player, session.Status);
        }

        private void CommandDebugMenu(IPlayer player)
        {
            if (player == null)
            {
                Reply(player, "The debug menu can only be toggled by an in-game player.");
                return;
            }

            if (!IsAdmin(player.Id) && !HasTool(player.Id, PermWorld))
            {
                Reply(player, "You need " + PermWorld + " or administrator access.");
                return;
            }

            if (!SendClientCommand(player, "debugmenu"))
            {
                Reply(player, "Could not send the debug menu command to your client.");
                return;
            }

            Reply(player, "Toggled the debug menu on your client.");
        }

        private void CommandSpectator(IPlayer player)
        {
            if (player == null)
            {
                Reply(player, "Spectator mode can only be toggled by an in-game player.");
                return;
            }

            if (!HasTool(player.Id, PermSpectate))
            {
                Reply(player, "You need " + PermSpectate + ".");
                return;
            }

            if (!SendClientCommand(player, "spectator"))
            {
                Reply(player, "Could not send the spectator command to your client.");
                return;
            }

            Reply(player, "Toggled spectator mode on your client.");
        }

        private void CommandPlayerList(IPlayer player)
        {
            if (player == null)
            {
                Reply(player, "The player list can only be opened by an in-game player.");
                return;
            }

            if (!HasTool(player.Id, PermPlayersView))
            {
                Reply(player, "You need " + PermPlayersView + ".");
                return;
            }

            if (!SendClientCommand(player, "xui open players"))
            {
                Reply(player, "Could not open the vanilla player list on your client.");
                return;
            }

            Reply(player, "Opened the vanilla player list on your client.");
        }

        private void CommandTppoi(IPlayer player)
        {
            if (player == null)
            {
                Reply(player, "The POI teleporter can only be opened by an in-game player.");
                return;
            }

            if (!HasTool(player.Id, PermTeleport))
            {
                Reply(player, "You need " + PermTeleport + ".");
                return;
            }

            if (!SendClientCommand(player, "tppoi"))
            {
                Reply(player, "Could not open the POI teleporter on your client.");
                return;
            }

            Reply(player, "Opened the POI teleporter on your client.");
        }

        private void CommandKill(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /as kill <player>");
                return;
            }

            IPlayer target = ResolvePlayer(args[1]);
            if (target == null)
            {
                Reply(player, "Player '" + args[1] + "' was not found.");
                return;
            }

            Session session = new Session();
            session.SelectedPlayerId = target.Id;
            session.SelectedPlayerName = target.Name;
            KillSelected(player != null ? player.Id : "server_console", session);
            Reply(player, session.Status);
        }

        private void CommandGive(IPlayer player, string[] args)
        {
            if (args.Length < 3)
            {
                Reply(player, "Usage: /as give <player> <item> [amount] [quality]");
                return;
            }

            IPlayer target = ResolvePlayer(args[1]);
            if (target == null)
            {
                Reply(player, "Player '" + args[1] + "' was not found.");
                return;
            }

            string amount = args.Length > 3 ? args[3] : "1";
            string quality = args.Length > 4 ? args[4] : config.DefaultItemQuality.ToString(CultureInfo.InvariantCulture);
            Session session = new Session();
            session.SelectedPlayerId = target.Id;
            session.SelectedPlayerName = target.Name;
            GiveItemToSelected(player != null ? player.Id : "server_console", session,
                args[2] + " " + amount + " " + quality);
            Reply(player, session.Status);
        }

        private void CommandClear(IPlayer player, string[] args)
        {
            if (args.Length < 2 || !HasConfirm(args))
            {
                Reply(player, "Usage: /as clear <player> confirm");
                return;
            }

            IPlayer target = ResolvePlayer(args[1]);
            if (target == null)
            {
                Reply(player, "Player '" + args[1] + "' was not found.");
                return;
            }

            Session session = new Session();
            session.SelectedPlayerId = target.Id;
            session.SelectedPlayerName = target.Name;
            ClearSelectedInventory(player != null ? player.Id : "server_console", session);
            Reply(player, session.Status);
        }

        private void CommandTake(IPlayer player, string[] args)
        {
            if (args.Length < 2 || !HasConfirm(args))
            {
                Reply(player, "Usage: /as take <player> confirm");
                return;
            }

            IPlayer target = ResolvePlayer(args[1]);
            if (target == null)
            {
                Reply(player, "Player '" + args[1] + "' was not found.");
                return;
            }

            Session session = new Session();
            session.SelectedPlayerId = target.Id;
            session.SelectedPlayerName = target.Name;
            TakeSelectedInventory(player != null ? player.Id : "server_console", session);
            Reply(player, session.Status);
        }

        private void CommandRepair(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /as repair <player>");
                return;
            }

            IPlayer target = ResolvePlayer(args[1]);
            if (target == null)
            {
                Reply(player, "Player '" + args[1] + "' was not found.");
                return;
            }

            Session session = new Session();
            session.SelectedPlayerId = target.Id;
            session.SelectedPlayerName = target.Name;
            RepairSelectedInventory(player != null ? player.Id : "server_console", session);
            Reply(player, session.Status);
        }

        private void CommandWarn(IPlayer player, string[] args)
        {
            if (args.Length < 3)
            {
                Reply(player, "Usage: /as warn <player> <message>");
                return;
            }

            IPlayer target = ResolvePlayer(args[1]);
            if (target == null)
            {
                Reply(player, "Player '" + args[1] + "' was not found.");
                return;
            }

            Session session = new Session();
            session.SelectedPlayerId = target.Id;
            session.SelectedPlayerName = target.Name;
            WarnSelected(player != null ? player.Id : "server_console", session, JoinArgs(args, 2));
            Reply(player, session.Status);
        }

        private void CommandNote(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /as note <player> [message]");
                return;
            }

            IPlayer target = ResolvePlayer(args[1]);
            if (target == null)
            {
                Reply(player, "Player '" + args[1] + "' was not found.");
                return;
            }

            Session session = new Session();
            session.SelectedPlayerId = target.Id;
            session.SelectedPlayerName = target.Name;
            NoteSelected(player != null ? player.Id : "server_console", session,
                args.Length > 2 ? JoinArgs(args, 2) : string.Empty);
            Reply(player, session.Status);
        }

        private void CommandTime(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /as time <day|night|hour|day hour minute>");
                return;
            }

            Session session = new Session();
            SetTime(player != null ? player.Id : "server_console", session, JoinArgs(args, 1));
            Reply(player, session.Status);
        }

        private void CommandWeather(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /as weather <clear|rain|storm> [hours]");
                return;
            }

            string mode = args[1].ToLowerInvariant();
            Session session = new Session();
            if (mode == "storm")
            {
                WeatherStorm(player != null ? player.Id : "server_console", session,
                    args.Length > 2 ? args[2] : "1");
            }
            else
            {
                SetWeather(player != null ? player.Id : "server_console", session, mode);
            }

            Reply(player, session.Status);
        }

        private void CommandAirdrop(IPlayer player, string[] args)
        {
            Session session = new Session();
            SpawnAirdrop(player != null ? player.Id : "server_console", session);
            Reply(player, session.Status);
        }

        private void CommandHorde(IPlayer player, string[] args)
        {
            Session session = new Session();
            SpawnHorde(player != null ? player.Id : "server_console", session);
            Reply(player, session.Status);
        }

        private void CommandScouts(IPlayer player, string[] args)
        {
            Session session = new Session();
            SpawnScouts(player != null ? player.Id : "server_console", session);
            Reply(player, session.Status);
        }

        private void CommandSpawn(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /as spawn <entity> [count]");
                return;
            }

            Session session = new Session();
            SpawnEntity(player != null ? player.Id : "server_console", session,
                args[1] + (args.Length > 2 ? " " + args[2] : string.Empty));
            Reply(player, session.Status);
        }

        private void CommandSave(IPlayer player)
        {
            Session session = new Session();
            SaveWorld(player != null ? player.Id : "server_console", session);
            Reply(player, session.Status);
        }

        private void CommandShutdown(IPlayer player, string[] args)
        {
            if (!HasConfirm(args))
            {
                Reply(player, "This shuts down the server. Run /as shutdown confirm.");
                return;
            }

            Session session = new Session();
            ShutdownServer(player != null ? player.Id : "server_console", session);
            Reply(player, session.Status);
        }

        private void CommandSay(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /as say <message>");
                return;
            }

            Session session = new Session();
            Announce(player != null ? player.Id : "server_console", session, JoinArgs(args, 1));
            Reply(player, session.Status);
        }

        private void CommandWarps(IPlayer player)
        {
            string actorId = player != null ? player.Id : "server_console";
            if (!HasTool(actorId, PermWarps) && !HasTool(actorId, PermTeleport))
            {
                Reply(player, "You need " + PermWarps + " or " + PermTeleport + ".");
                return;
            }

            List<WarpEntry> list = data.Warps.Values
                .Where(w => w != null)
                .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Reply(player, "Warps: " + list.Count);
            for (int i = 0; i < list.Count && i < 30; i++)
            {
                WarpEntry warp = list[i];
                Reply(player, warp.Name + "  |  " + FormatVector(warp.X, warp.Y, warp.Z) +
                             "  |  by " + warp.CreatedBy);
            }
        }

        private void CommandWarp(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /as warp <name>");
                return;
            }

            Session session = new Session();
            TeleportToWarp(player != null ? player.Id : "server_console", session, args[1]);
            Reply(player, session.Status);
        }

        private void CommandSetWarp(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /as setwarp <name>");
                return;
            }

            Session session = new Session();
            SetWarp(player != null ? player.Id : "server_console", session, args[1]);
            Reply(player, session.Status);
        }

        private void CommandDelWarp(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /as delwarp <name>");
                return;
            }

            Session session = new Session();
            DeleteWarp(player != null ? player.Id : "server_console", session, args[1]);
            Reply(player, session.Status);
        }

        private void CommandSnapshot(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /as snapshot <player> [reason]");
                return;
            }

            if (!HasTool(player != null ? player.Id : "server_console", PermSnapshot))
            {
                Reply(player, "You do not have permission to take snapshots.");
                return;
            }

            IPlayer target = ResolvePlayer(args[1]);
            EntityPlayer entity = GetEntity(target);
            if (target == null || entity == null)
            {
                Reply(player, "That player is not online.");
                return;
            }

            string reason = args.Length > 2 ? JoinArgs(args, 2) : "Manual snapshot";
            Snapshot snapshot = CaptureSnapshot(entity, reason);
            Reply(player, snapshot != null
                ? "Captured snapshot " + snapshot.Id + " for " + target.Name + "."
                : "Could not capture the snapshot.");
        }

        private void CommandSnapshots(IPlayer player, string[] args)
        {
            string actorId = player != null ? player.Id : "server_console";
            if (!HasTool(actorId, PermSnapshots) && !HasTool(actorId, PermSnapshot) &&
                !HasTool(actorId, PermRollback))
            {
                Reply(player, "You need " + PermSnapshots + ", " + PermSnapshot + " or " + PermRollback + ".");
                return;
            }

            string playerId = null;
            if (args.Length > 1)
            {
                IPlayer target = ResolvePlayer(args[1]);
                if (target != null)
                {
                    playerId = target.Id;
                }
            }

            List<Snapshot> list = snapshots.Snapshots
                .Where(s => s != null && (playerId == null ||
                    string.Equals(s.PlayerId, playerId, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(s => s.CreatedUtcTicks)
                .ToList();

            Reply(player, "Snapshots: " + list.Count +
                         (playerId != null ? " for " + GetPlayerName(playerId) : string.Empty));
            for (int i = 0; i < list.Count && i < 20; i++)
            {
                Snapshot snapshot = list[i];
                Reply(player, snapshot.Id + "  |  " + snapshot.PlayerName + "  |  " +
                             FormatDateTime(snapshot.CreatedUtcTicks) + "  |  " + snapshot.Reason +
                             "  |  " + SnapshotItemCount(snapshot) + " items");
            }
        }

        private void CommandRollback(IPlayer player, string[] args)
        {
            if (args.Length < 2 || !HasConfirm(args))
            {
                Reply(player, "Usage: /as rollback <player> [snapshotId] confirm");
                return;
            }

            if (!HasTool(player != null ? player.Id : "server_console", PermRollback))
            {
                Reply(player, "You do not have permission to roll back players.");
                return;
            }

            IPlayer target = ResolvePlayer(args[1]);
            if (target == null)
            {
                Reply(player, "Player '" + args[1] + "' was not found.");
                return;
            }

            string snapshotId = args.Length > 2 && !string.Equals(args[2], "confirm", StringComparison.OrdinalIgnoreCase)
                ? args[2]
                : null;
            if (string.IsNullOrEmpty(snapshotId))
            {
                Snapshot latest = snapshots.Snapshots
                    .Where(s => s != null && string.Equals(s.PlayerId, target.Id, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(s => s.CreatedUtcTicks)
                    .FirstOrDefault();
                if (latest == null)
                {
                    Reply(player, "No snapshots are stored for " + target.Name + ".");
                    return;
                }

                snapshotId = latest.Id;
            }

            Session session = new Session();
            session.SelectedPlayerId = target.Id;
            session.SelectedPlayerName = target.Name;
            RestoreSnapshotById(player != null ? player.Id : "server_console", session, snapshotId);
            Reply(player, session.Status);
        }

        private void CommandLogs(IPlayer player, string[] args)
        {
            if (!HasTool(player != null ? player.Id : "server_console", PermLogs))
            {
                Reply(player, "You need " + PermLogs + ".");
                return;
            }

            int count = 25;
            string filter = "all";
            if (args.Length > 1)
            {
                int parsed;
                if (int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    count = Clamp(parsed, 1, 200);
                    if (args.Length > 2)
                    {
                        filter = args[2];
                    }
                }
                else
                {
                    filter = args[1];
                }
            }

            Session session = new Session();
            session.LogFilter = filter;
            List<LogEntry> list = GetFilteredLogs(session);
            Reply(player, "Logs (" + filter + "): " + list.Count + " entries, showing " + Math.Min(count, list.Count));
            for (int i = 0; i < list.Count && i < count; i++)
            {
                LogEntry entry = list[i];
                Reply(player, "[" + entry.Category + "] " + FormatDate(entry.UtcTicks) + "  " +
                             (string.IsNullOrEmpty(entry.Actor) ? "system" : entry.Actor) + ": " + entry.Message);
            }
        }

        private void CommandReload(IPlayer player)
        {
            Session session = new Session();
            ReloadAll(player != null ? player.Id : "server_console", session);
            Reply(player, session.Status);
        }

        private void CommandKillAll(IPlayer player, string[] args)
        {
            if (!HasConfirm(args))
            {
                Reply(player, "This kills all non-player entities. Run /as killall confirm.");
                return;
            }

            if (!HasTool(player != null ? player.Id : "server_console", PermKill))
            {
                Reply(player, "You do not have permission to kill all entities.");
                return;
            }

            ExecuteConsole("killall all");
            Reply(player, "Killed all non-player entities.");
            AddLog("world", player != null ? player.Name : "Console", string.Empty, "killall all");
        }

        private static bool HasConfirm(string[] args)
        {
            return args != null && args.Length > 0 &&
                   string.Equals(args[args.Length - 1], "confirm", StringComparison.OrdinalIgnoreCase);
        }

        private static string JoinArgs(string[] args, int start)
        {
            if (args == null || start >= args.Length)
            {
                return string.Empty;
            }

            return string.Join(" ", args.Skip(start).ToArray());
        }

        private static string JoinArgsWithoutConfirm(string[] args, int start)
        {
            if (args == null)
            {
                return string.Empty;
            }

            int end = args.Length;
            if (end > start && string.Equals(args[end - 1], "confirm", StringComparison.OrdinalIgnoreCase))
            {
                end--;
            }

            if (start >= end)
            {
                return string.Empty;
            }

            return string.Join(" ", args.Skip(start).Take(end - start).ToArray());
        }

        #endregion

        #region Data classes

        private sealed class Session
        {
            public string Screen = "home";
            public int Page;
            public string SelectedPlayerId;
            public string SelectedPlayerName;
            public string SelectedSnapshotId;
            public int SelectedLogIndex = -1;
            public string InventorySection = "backpack";
            public int InventoryIndex = -1;
            public string SnapshotFilterPlayerId;
            public string LogFilter = "all";
            public string Search = string.Empty;
            public string Status = string.Empty;
            public string InputAction;
            public string InputLabel = string.Empty;
            public string InputValue = string.Empty;
            public string PendingAction;
            public string PendingData;
            public string PendingReturnScreen = "home";
            public string PendingTitle = string.Empty;
            public string PendingMessage = string.Empty;
            public readonly List<string> History = new List<string>();
        }

        private sealed class LogEntry
        {
            public long UtcTicks;
            public string Category = "info";
            public string Actor = string.Empty;
            public string Target = string.Empty;
            public string Message = string.Empty;
        }

        private sealed class MuteEntry
        {
            public string PlayerName = string.Empty;
            public string Reason = string.Empty;
            public string By = string.Empty;
            public long UntilUtcTicks;
        }

        private sealed class FreezeEntry
        {
            public string PlayerName = string.Empty;
            public float X;
            public float Y;
            public float Z;
            public float RotY;
            public string Reason = string.Empty;
            public string By = string.Empty;
            public long UntilUtcTicks;
        }

        private sealed class WarningEntry
        {
            public string PlayerName = string.Empty;
            public string Message = string.Empty;
            public string By = string.Empty;
            public long CreatedUtcTicks;
        }

        private sealed class PlayerNote
        {
            public string Text = string.Empty;
            public string By = string.Empty;
            public long UpdatedUtcTicks;
        }

        private sealed class HomeEntry
        {
            public float X;
            public float Y;
            public float Z;
            public float RotY;
            public long CreatedUtcTicks;
        }

        private sealed class WarpEntry
        {
            public string Name = string.Empty;
            public float X;
            public float Y;
            public float Z;
            public string CreatedBy = string.Empty;
            public long CreatedUtcTicks;
        }

        private sealed class SnapshotStore
        {
            public List<Snapshot> Snapshots = new List<Snapshot>();
        }

        private sealed class Snapshot
        {
            public string Id = string.Empty;
            public string PlayerId = string.Empty;
            public string PlayerName = string.Empty;
            public string Reason = string.Empty;
            public long CreatedUtcTicks;
            public float X;
            public float Y;
            public float Z;
            public float RotX;
            public float RotY;
            public float RotZ;
            public int Health;
            public float Stamina;
            public float Water;
            public float Food;
            public List<SnapshotSlot> Inventory = new List<SnapshotSlot>();
            public List<SnapshotSlot> Bag = new List<SnapshotSlot>();
            public List<SnapshotSlot> Equipment = new List<SnapshotSlot>();
        }

        private sealed class SnapshotSlot
        {
            public int Index;
            public int Count;
            public string Data = string.Empty;
        }

        private sealed class InvItem
        {
            public string Section = "backpack";
            public int Index;
            public string Name = string.Empty;
            public string InternalName = string.Empty;
            public string Category = "misc";
            public string Icon = "sprite:sa_nav_items";
            public int Count;
            public ushort Quality;
            public float Percent = -1f;
            public string Data = string.Empty;
        }

        private sealed class StoredData
        {
            public Dictionary<string, WarpEntry> Warps =
                new Dictionary<string, WarpEntry>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, MuteEntry> Mutes =
                new Dictionary<string, MuteEntry>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, FreezeEntry> Freezes =
                new Dictionary<string, FreezeEntry>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, PlayerNote> Notes =
                new Dictionary<string, PlayerNote>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, List<WarningEntry>> Warnings =
                new Dictionary<string, List<WarningEntry>>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, HomeEntry> Homes =
                new Dictionary<string, HomeEntry>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class Configuration
        {
            public string ChatPrefix = "[AdminSuite]";
            public string HookLabel = "Admin Suite";
            public string HookDescription =
                "The complete staff toolkit: players, moderation, teleports, inventory, world, logs and rollback.";
            public string HookPermission = PermUse;

            public bool GrantUseToDefaultGroup = false;
            public bool ProtectAdminsFromStaff = true;
            public bool AllowSelfTarget = false;
            public bool NotifyStaffOnJoin = true;

            public bool LogChat = true;
            public bool LogCommands = false;
            public bool LogDeaths = true;
            public bool WriteLogFile = true;
            public int MaxLogEntries = 1000;

            public bool SnapshotBeforeDestructive = true;
            public int SnapshotRetentionDays = 14;
            public int MaxSnapshotsPerPlayer = 20;

            public int DefaultBanDays = 1;
            public int DefaultTimeoutMinutes = 30;
            public int DefaultFreezeMinutes = 60;
            public int FreezeCheckSeconds = 1;
            public int DefaultItemQuality = 1;
            public int DefaultSpawnCount = 1;
            public int MaxWarps = 50;

            public bool EnableJail = false;
            public float JailX = 0f;
            public float JailY = 0f;
            public float JailZ = 0f;

            public List<string> ProtectedPlayers = new List<string>();
        }

        #endregion
    }
}

