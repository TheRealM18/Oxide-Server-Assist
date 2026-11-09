using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("PermissionGui", "TheRealM18", "3.5.0")]
    [Description("Oxide-only permission manager: config-driven defaults, groups, users, tracks and a searchable permission browser.")]
    public class PermissionGui : CovalencePlugin
    {
        private const string PermUse = "permissiongui.use";
        private const string PermAdmin = "permissiongui.admin";
        private const string LegacyDataFileName = "PermissionGui/data";
        private const string StateDataFileName = "PermissionGui/state";
        private const int UiRows = 10;
        private const int UiButtons = 10;

        private Configuration config;
        private StateData stateData;
        private readonly Dictionary<string, Session> sessions = new Dictionary<string, Session>(StringComparer.OrdinalIgnoreCase);

        private bool configReadFailed;
        private string lastError = string.Empty;
        private DateTime lastAppliedUtc = DateTime.MinValue;
        private DateTime lastSavedUtc = DateTime.MinValue;

        #region Lifecycle

        private void Init()
        {
            permission.RegisterPermission(PermUse, this);
            permission.RegisterPermission(PermAdmin, this);

            LoadConfig();
            LoadState();

            AddCovalenceCommand(new[] { "permgui", "pgui" }, nameof(CommandPermGui));
            AddCovalenceCommand(new[] { "perm" }, nameof(CommandPerm));

            timer.Once(1f, () =>
            {
                if (config.ApplyDefaultsOnLoad)
                {
                    ApplyConfiguredDefaults(false);
                }
            });

            timer.Once(2f, PruneExpiredTempPermissions);
            timer.Repeat(30f, 0, ExpireTempPermissions);

            timer.Once(3f, RegisterServerAssistHook);

            if (Interface.Oxide.DataFileSystem.ExistsDatafile(LegacyDataFileName))
            {
                Interface.Oxide.LogWarning("[PermissionGui] oxide/data/PermissionGui/data.json is legacy and is no longer read or written. Run '/perm cleanup' to delete it.");
            }
        }

        private void OnServerInitialized(bool initial)
        {
            if (config.ApplyDefaultsOnLoad)
            {
                EnsureDefaultsInitialized();
                ApplyConfiguredDefaults(false);
            }

            PruneExpiredTempPermissions();
            RegisterServerAssistHook();
        }

        private void OnServerAssistReady()
        {
            RegisterServerAssistHook();
        }

        private void OnUserConnected(IPlayer player)
        {
            if (config.ApplyDefaultsOnPlayerConnect)
            {
                ApplyDefaultsToPlayer(player, true, false);
            }
        }

        private void OnUserDisconnected(IPlayer player, string reason)
        {
            if (player != null)
            {
                sessions.Remove(player.Id);
            }

            SavePermissionData();
        }

        private void OnServerSave()
        {
            SavePermissionData();
            SaveState();
        }

        private void Unload()
        {
            SavePermissionData();
            SaveState();
            sessions.Clear();
            UnregisterServerAssistHook();
        }

        #endregion

        #region Config

        protected override void LoadDefaultConfig()
        {
            config = new Configuration();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            configReadFailed = false;
            try
            {
                config = Config.ReadObject<Configuration>();
            }
            catch (Exception exception)
            {
                configReadFailed = true;
                Interface.Oxide.LogWarning("[PermissionGui] config could not be read: " + exception.Message);
                Interface.Oxide.LogWarning("[PermissionGui] DefaultPermissions, AutoGrantUse, AutoGrantAdmin and track arrays must be JSON arrays.");
                config = null;
            }

            if (config == null)
            {
                config = new Configuration();
            }

            try
            {
                Config.Load();

                if (Config["Groups"] != null || Config["Users"] != null)
                {
                    Interface.Oxide.LogWarning("[PermissionGui] PermissionGui.json contains Groups/Users. Those belong in Oxide's data files and are ignored here.");
                }

                if (Config["DefaultTrackGroups"] != null)
                {
                    Interface.Oxide.LogWarning("[PermissionGui] DefaultTrackGroups is a legacy setting. Use the Tracks object instead.");
                }

                if (Config["GrantAdminPermissions"] != null && Config["GrantAdminPermissionsToAdminGroup"] == null)
                {
                    bool oldValue;
                    if (bool.TryParse(Convert.ToString(Config["GrantAdminPermissions"]), out oldValue))
                    {
                        config.GrantAdminPermissionsToAdminGroup = oldValue;
                    }
                }
            }
            catch
            {
                // The typed reader above already reported the useful error.
            }

            config.ChatPrefix = string.IsNullOrEmpty(config.ChatPrefix) ? "Permissions:" : config.ChatPrefix;
            config.DefaultGroup = config.DefaultGroup == null ? "default" : config.DefaultGroup.Trim();
            config.AdminWildcard = string.IsNullOrEmpty(config.AdminWildcard) ? "oxide.*" : config.AdminWildcard.Trim();
            if (string.Equals(config.AdminWildcard, "*.*", StringComparison.OrdinalIgnoreCase))
            {
                config.AdminWildcard = "oxide.*";
                Interface.Oxide.LogInfo("[PermissionGui] migrated AdminWildcard from *.* to oxide.*.");
            }

            config.DefaultPermissions = NormalizeList(config.DefaultPermissions);
            config.AutoGrantUse = NormalizeList(config.AutoGrantUse);
            config.AutoGrantAdmin = NormalizeList(config.AutoGrantAdmin);
            config.Tracks = NormalizeTracks(config.Tracks);
            config.MaxRows = Math.Max(1, Math.Min(UiRows, config.MaxRows));

            if (configReadFailed)
            {
                Interface.Oxide.LogError("[PermissionGui] PermissionGui.json was NOT rewritten because it could not be read. Fix the JSON and run 'oxide.reload PermissionGui'.");
            }
            else
            {
                SaveConfig();
            }
        }

        protected override void SaveConfig()
        {
            if (config != null)
            {
                Config.WriteObject(config, true);
            }
        }

        private static List<string> NormalizeList(List<string> values)
        {
            List<string> result = new List<string>();
            if (values == null)
            {
                return result;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < values.Count; i++)
            {
                string value = values[i];
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                value = value.Trim();
                if (value.Length > 0 && seen.Add(value))
                {
                    result.Add(value);
                }
            }

            return result;
        }

        private static Dictionary<string, List<string>> NormalizeTracks(Dictionary<string, List<string>> tracks)
        {
            Dictionary<string, List<string>> result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (tracks == null)
            {
                return result;
            }

            foreach (KeyValuePair<string, List<string>> pair in tracks)
            {
                string name = pair.Key == null ? string.Empty : pair.Key.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                result[name] = NormalizeList(pair.Value);
            }

            return result;
        }

        #endregion

        #region Default permissions and admin grants

        private string ResolveDefaultGroup()
        {
            if (!string.IsNullOrEmpty(config.DefaultGroup))
            {
                return config.DefaultGroup.Trim();
            }

            try
            {
                return Interface.Oxide.Config.Options.DefaultGroups.Players;
            }
            catch
            {
                return "default";
            }
        }

        private string EnsureDefaultGroup()
        {
            string group = ResolveDefaultGroup();
            if (string.IsNullOrEmpty(group))
            {
                Interface.Oxide.LogError("[PermissionGui] DefaultGroup is empty and Oxide has no default player group.");
                return null;
            }

            if (!permission.GroupExists(group) && config.CreateDefaultGroup)
            {
                permission.CreateGroup(group, group, 0);
            }

            if (!permission.GroupExists(group))
            {
                Interface.Oxide.LogError("[PermissionGui] Default group '" + group + "' does not exist and could not be created.");
                return null;
            }

            return group;
        }

        private void ApplyConfiguredDefaults(bool force)
        {
            if (!permission.IsLoaded)
            {
                timer.Once(5f, () => ApplyConfiguredDefaults(force));
                return;
            }

            string group = EnsureDefaultGroup();
            if (group == null)
            {
                return;
            }

            EnsureDefaultsInitialized();

            int granted = 0;
            List<string> missing = new List<string>();

            for (int i = 0; i < config.DefaultPermissions.Count; i++)
            {
                string permissionName = config.DefaultPermissions[i];
                if (!permission.PermissionExists(permissionName))
                {
                    missing.Add(permissionName);
                    continue;
                }

                permission.GrantGroupPermission(group, permissionName, null);
                granted++;
            }

            EnsureAdminGroupPermissions();
            EnsureAutoGrantUsers();

            IPlayer[] connected = players.Connected.ToArray();
            for (int i = 0; i < connected.Length; i++)
            {
                ApplyDefaultsToPlayer(connected[i], false, force);
            }

            SavePermissionData();
            SaveState();
            lastAppliedUtc = DateTime.UtcNow;

            Interface.Oxide.LogInfo("[PermissionGui] Applied " + granted + " default permission(s) to group '" + group + "'. Online players updated: " + connected.Length + ".");

            if (missing.Count > 0)
            {
                Interface.Oxide.LogWarning("[PermissionGui] These default permissions are not registered yet and were skipped: " + string.Join(", ", missing));
            }
        }

        private void ApplyDefaultsToPlayer(IPlayer player, bool save, bool force)
        {
            if (player == null || string.IsNullOrEmpty(player.Id) || !permission.IsLoaded)
            {
                return;
            }

            if (config.ApplyDefaultsOncePerPlayer && !force && HasDefaultsApplied(player.Id))
            {
                ApplyAutoGrantsForPlayer(player.Id);
                if (save)
                {
                    SavePermissionData();
                }

                return;
            }

            string group = EnsureDefaultGroup();
            if (!string.IsNullOrEmpty(group))
            {
                if (!config.AddDefaultGroupForTrackUsers &&
                    UserIsInAnyTrack(player.Id) &&
                    !DefaultGroupIsInAnyTrackLadder())
                {
                    permission.RemoveUserGroup(player.Id, group);
                }
                else
                {
                    permission.AddUserGroup(player.Id, group);
                }
            }

            if (config.GrantDefaultsDirectlyToPlayer)
            {
                for (int i = 0; i < config.DefaultPermissions.Count; i++)
                {
                    string permissionName = config.DefaultPermissions[i];
                    if (permission.PermissionExists(permissionName))
                    {
                        permission.GrantUserPermission(player.Id, permissionName, null);
                        ForgetTempPermission(player.Id, permissionName);
                    }
                }
            }

            MarkDefaultsApplied(player.Id, save);
            ApplyAutoGrantsForPlayer(player.Id);

            if (save)
            {
                SavePermissionData();
            }
        }

        private void LoadState()
        {
            stateData = null;

            try
            {
                if (Interface.Oxide.DataFileSystem.ExistsDatafile(StateDataFileName))
                {
                    stateData = Interface.Oxide.DataFileSystem.ReadObject<StateData>(StateDataFileName);
                }
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[PermissionGui] state could not be read: " + exception.Message);
            }

            if (stateData == null)
            {
                stateData = new StateData();
            }

            NormalizeState();
        }

        private void NormalizeState()
        {
            if (stateData.DefaultsApplied == null)
            {
                stateData.DefaultsApplied = new List<string>();
            }

            if (stateData.TempPermissions == null)
            {
                stateData.TempPermissions = new List<TempPermissionEntry>();
            }

            stateData.DefaultsApplied.RemoveAll(string.IsNullOrEmpty);
            stateData.TempPermissions.RemoveAll(entry =>
                entry == null ||
                string.IsNullOrEmpty(entry.UserId) ||
                string.IsNullOrEmpty(entry.Permission) ||
                entry.ExpiresUtcTicks <= 0);
        }

        private void SaveState()
        {
            if (stateData == null)
            {
                return;
            }

            try
            {
                Interface.Oxide.DataFileSystem.WriteObject(StateDataFileName, stateData, true);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[PermissionGui] state could not be saved: " + exception.Message);
            }
        }

        private bool HasDefaultsApplied(string userId)
        {
            return stateData != null &&
                   stateData.DefaultsApplied.Any(id => string.Equals(id, userId, StringComparison.OrdinalIgnoreCase));
        }

        private void EnsureDefaultsInitialized()
        {
            if (stateData == null || stateData.DefaultsInitialized || !permission.IsLoaded)
            {
                return;
            }

            Dictionary<string, string> known = CollectUsers();
            foreach (string id in known.Keys)
            {
                MarkDefaultsApplied(id, false);
            }

            stateData.DefaultsInitialized = true;
            SaveState();

            if (known.Count > 0)
            {
                Interface.Oxide.LogInfo("[PermissionGui] marked " + known.Count +
                                        " existing player(s) as already receiving defaults.");
            }
        }

        private void MarkDefaultsApplied(string userId, bool save)
        {
            if (string.IsNullOrEmpty(userId) || HasDefaultsApplied(userId))
            {
                return;
            }

            stateData.DefaultsApplied.Add(userId);
            if (save)
            {
                SaveState();
            }
        }

        private int ClearDefaultsApplied(string userId)
        {
            if (stateData == null || stateData.DefaultsApplied == null)
            {
                return 0;
            }

            int removed;
            if (string.IsNullOrEmpty(userId) || userId.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                removed = stateData.DefaultsApplied.Count;
                stateData.DefaultsApplied.Clear();
            }
            else
            {
                removed = stateData.DefaultsApplied.RemoveAll(
                    id => string.Equals(id, userId, StringComparison.OrdinalIgnoreCase));
            }

            if (removed > 0)
            {
                SaveState();
            }

            return removed;
        }

        private string AdminWildcard
        {
            get { return string.IsNullOrEmpty(config.AdminWildcard) ? "oxide.*" : config.AdminWildcard.Trim(); }
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

            if (!permission.UserHasPermission(playerId, PermAdmin))
            {
                return false;
            }

            return !config.RequireWildcardForAdmin || permission.UserHasPermission(playerId, AdminWildcard);
        }

        private bool CanUse(string playerId)
        {
            return IsAdmin(playerId) || permission.UserHasPermission(playerId, PermUse);
        }

        private void EnsureAdminGroupPermissions()
        {
            if (!config.GrantAdminPermissionsToAdminGroup)
            {
                return;
            }

            string adminGroup = Interface.Oxide.Config.Options.DefaultGroups.Administrators;
            if (string.IsNullOrEmpty(adminGroup) || !permission.GroupExists(adminGroup))
            {
                return;
            }

            GrantAdminAccessToGroup(adminGroup);
        }

        private void GrantAdminAccessToGroup(string group)
        {
            if (string.IsNullOrEmpty(group) || !permission.GroupExists(group))
            {
                return;
            }

            permission.GrantGroupPermission(group, PermAdmin, null);
            permission.GrantGroupPermission(group, PermUse, null);
            AddRawGroupPermission(group, AdminWildcard);

            if (config.GrantAllPermissionsToAdmin)
            {
                permission.GrantGroupPermission(group, "*", null);
            }
        }

        private void GrantAdminAccessToUser(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return;
            }

            permission.GrantUserPermission(playerId, PermAdmin, null);
            permission.GrantUserPermission(playerId, PermUse, null);
            AddRawUserPermission(playerId, AdminWildcard);
            ForgetTempPermission(playerId, PermAdmin);
            ForgetTempPermission(playerId, PermUse);
            ForgetTempPermission(playerId, AdminWildcard);

            if (config.GrantAllPermissionsToAdmin)
            {
                permission.GrantUserPermission(playerId, "*", null);
                ForgetTempPermission(playerId, "*");
            }
        }

        private void AddRawGroupPermission(string group, string permissionName)
        {
            if (string.IsNullOrEmpty(permissionName))
            {
                return;
            }

            GroupData groupData = permission.GetGroupData(group);
            if (groupData != null && groupData.Perms != null)
            {
                groupData.Perms.Add(permissionName);
            }
        }

        private void AddRawUserPermission(string playerId, string permissionName)
        {
            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(permissionName))
            {
                return;
            }

            UserData userData = permission.GetUserData(playerId);
            if (userData != null && userData.Perms != null)
            {
                userData.Perms.Add(permissionName);
            }
        }

        private void EnsureAutoGrantUsers()
        {
            int adminCount = 0;
            int useCount = 0;

            for (int i = 0; i < config.AutoGrantAdmin.Count; i++)
            {
                string id = config.AutoGrantAdmin[i];
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                GrantAdminAccessToUser(id);
                adminCount++;
            }

            for (int i = 0; i < config.AutoGrantUse.Count; i++)
            {
                string id = config.AutoGrantUse[i];
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                permission.GrantUserPermission(id, PermUse, null);
                useCount++;
            }

            if (adminCount > 0 || useCount > 0)
            {
                Interface.Oxide.LogInfo("[PermissionGui] AutoGrant applied: AutoGrantAdmin=" + adminCount + ", AutoGrantUse=" + useCount + ".");
            }
        }

        private void ApplyAutoGrantsForPlayer(string playerId)
        {
            if (ContainsId(config.AutoGrantAdmin, playerId))
            {
                GrantAdminAccessToUser(playerId);
            }
            else if (ContainsId(config.AutoGrantUse, playerId))
            {
                permission.GrantUserPermission(playerId, PermUse, null);
                ForgetTempPermission(playerId, PermUse);
            }
        }

        private static bool ContainsId(List<string> ids, string playerId)
        {
            if (ids == null || string.IsNullOrEmpty(playerId))
            {
                return false;
            }

            for (int i = 0; i < ids.Count; i++)
            {
                if (string.Equals(ids[i], playerId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool SavePermissionData()
        {
            if (!permission.IsLoaded)
            {
                return false;
            }

            try
            {
                permission.SaveData();
                lastSavedUtc = DateTime.UtcNow;
                lastError = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                lastError = exception.Message;
                Interface.Oxide.LogError("[PermissionGui] Oxide permission data could not be saved: " + exception);
                return false;
            }
        }

        #endregion

        #region Permission helpers

        private HashSet<string> EffectiveGroupPermissions(string group)
        {
            HashSet<string> permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string current = group;

            while (!string.IsNullOrEmpty(current) && visited.Add(current) && permission.GroupExists(current))
            {
                permissions.UnionWith(permission.GetGroupPermissions(current, false));
                current = permission.GetGroupParent(current);
            }

            return permissions;
        }

        private HashSet<string> EffectiveUserPermissions(string playerId)
        {
            HashSet<string> permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            UserData user = permission.GetUserData(playerId);
            if (user != null && user.Perms != null)
            {
                permissions.UnionWith(user.Perms);
            }

            foreach (string group in permission.GetUserGroups(playerId))
            {
                permissions.UnionWith(EffectiveGroupPermissions(group));
            }

            return permissions;
        }

        private bool GroupHasDirect(string group, string permissionName)
        {
            return permission.GetGroupPermissions(group, false).Any(p => p.Equals(permissionName, StringComparison.OrdinalIgnoreCase));
        }

        private bool UserHasDirect(string playerId, string permissionName)
        {
            UserData user = permission.GetUserData(playerId);
            return user != null && user.Perms != null && user.Perms.Any(p => p.Equals(permissionName, StringComparison.OrdinalIgnoreCase));
        }

        private void ToggleGroupPermission(string group, string permissionName)
        {
            if (GroupHasDirect(group, permissionName))
            {
                permission.RevokeGroupPermission(group, permissionName);
            }
            else
            {
                permission.GrantGroupPermission(group, permissionName, null);
            }

            SavePermissionData();
        }

        private void ToggleUserPermission(string playerId, string permissionName)
        {
            if (UserHasDirect(playerId, permissionName))
            {
                permission.RevokeUserPermission(playerId, permissionName);
                ForgetTempPermission(playerId, permissionName);
            }
            else
            {
                permission.GrantUserPermission(playerId, permissionName, null);
                ForgetTempPermission(playerId, permissionName);
            }

            SavePermissionData();
        }

        private HashSet<string> AllPermissionsForGroup(string group)
        {
            HashSet<string> all = new HashSet<string>(permission.GetPermissions(), StringComparer.OrdinalIgnoreCase);
            all.UnionWith(EffectiveGroupPermissions(group));
            return all;
        }

        private HashSet<string> AllPermissionsForUser(string playerId)
        {
            HashSet<string> all = new HashSet<string>(permission.GetPermissions(), StringComparer.OrdinalIgnoreCase);
            all.UnionWith(EffectiveUserPermissions(playerId));
            return all;
        }

        private Dictionary<string, string> CollectUsers()
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (IPlayer player in players.All)
            {
                if (player != null && !string.IsNullOrEmpty(player.Id))
                {
                    result[player.Id] = player.Name ?? player.Id;
                }
            }

            foreach (string group in permission.GetGroups())
            {
                foreach (string entry in permission.GetUsersInGroup(group))
                {
                    string id = ExtractId(entry);
                    if (!string.IsNullOrEmpty(id) && !result.ContainsKey(id))
                    {
                        result[id] = PlayerName(id);
                    }
                }
            }

            foreach (string permissionName in permission.GetPermissions())
            {
                foreach (string entry in permission.GetPermissionUsers(permissionName))
                {
                    string id = ExtractId(entry);
                    if (!string.IsNullOrEmpty(id) && !result.ContainsKey(id))
                    {
                        result[id] = PlayerName(id);
                    }
                }
            }

            return result;
        }

        private string PlayerName(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return "(unknown)";
            }

            IPlayer player = players.FindPlayerById(playerId);
            if (player != null && !string.IsNullOrEmpty(player.Name))
            {
                return player.Name;
            }

            UserData user = permission.GetUserData(playerId);
            if (user != null && !string.IsNullOrEmpty(user.LastSeenNickname) && user.LastSeenNickname != "Unnamed")
            {
                return user.LastSeenNickname;
            }

            return playerId;
        }

        private static string ExtractId(string userEntry)
        {
            if (string.IsNullOrEmpty(userEntry))
            {
                return string.Empty;
            }

            int index = userEntry.IndexOf('(');
            return index > 0 ? userEntry.Substring(0, index).Trim() : userEntry.Trim();
        }

        private string ResolveUserId(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return null;
            }

            input = input.Trim();
            if (permission.UserExists(input))
            {
                return input;
            }

            try
            {
                IPlayer player = players.FindPlayers(input).FirstOrDefault();
                if (player != null && !string.IsNullOrEmpty(player.Id))
                {
                    return player.Id;
                }
            }
            catch
            {
                // Fall through to the slower name scan.
            }

            foreach (IPlayer player in players.All)
            {
                if (player != null && string.Equals(player.Name, input, StringComparison.OrdinalIgnoreCase))
                {
                    return player.Id;
                }
            }

            foreach (string group in permission.GetGroups())
            {
                foreach (string entry in permission.GetUsersInGroup(group))
                {
                    string id = ExtractId(entry);
                    if (string.Equals(id, input, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(PlayerName(id), input, StringComparison.OrdinalIgnoreCase))
                    {
                        return id;
                    }
                }
            }

            if (input.Length >= 15 && input.All(char.IsDigit))
            {
                return input;
            }

            return null;
        }

        private static bool ValidName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 48)
            {
                return false;
            }

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-' && c != '.')
                {
                    return false;
                }
            }

            return true;
        }

        #endregion

        #region Group operations

        private void CreateGroup(string name, Session session)
        {
            if (!ValidName(name))
            {
                session.Status = "Use only letters, numbers, '_', '-' or '.' (max 48).";
                return;
            }

            if (permission.GroupExists(name))
            {
                session.Status = "Group already exists.";
                return;
            }

            if (!permission.CreateGroup(name, name, 0))
            {
                session.Status = "Group could not be created.";
                return;
            }

            session.Group = name;
            session.Mode = "group";
            session.Status = "Group '" + name + "' created.";
            SavePermissionData();
        }

        private void RenameGroup(string oldName, string newName, Session session)
        {
            if (!CanEditConfig(session))
            {
                return;
            }

            if (!ValidName(newName))
            {
                session.Status = "Use only letters, numbers, '_', '-' or '.' (max 48).";
                return;
            }

            if (!permission.GroupExists(oldName))
            {
                session.Status = "Group not found.";
                return;
            }

            if (permission.GroupExists(newName))
            {
                session.Status = "A group with that name already exists.";
                return;
            }

            string title = permission.GetGroupTitle(oldName);
            int rank = permission.GetGroupRank(oldName);
            string parent = permission.GetGroupParent(oldName);
            string[] direct = permission.GetGroupPermissions(oldName, false);
            string[] users = permission.GetUsersInGroup(oldName);
            string[] children = permission.GetGroups()
                .Where(g => string.Equals(permission.GetGroupParent(g), oldName, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (!permission.CreateGroup(newName, title, rank))
            {
                session.Status = "Group could not be renamed.";
                return;
            }

            foreach (string permissionName in direct)
            {
                permission.GrantGroupPermission(newName, permissionName, null);
            }

            if (!string.IsNullOrEmpty(parent))
            {
                permission.SetGroupParent(newName, parent);
            }

            foreach (string entry in users)
            {
                string id = ExtractId(entry);
                permission.AddUserGroup(id, newName);
                permission.RemoveUserGroup(id, oldName);
            }

            foreach (string child in children)
            {
                permission.SetGroupParent(child, newName);
            }

            foreach (List<string> ladder in config.Tracks.Values)
            {
                for (int i = 0; i < ladder.Count; i++)
                {
                    if (ladder[i].Equals(oldName, StringComparison.OrdinalIgnoreCase))
                    {
                        ladder[i] = newName;
                    }
                }
            }

            permission.RemoveGroup(oldName);
            SaveConfig();
            SavePermissionData();

            session.Group = newName;
            session.Status = "Group renamed to '" + newName + "'.";
        }

        private void DeleteGroup(string group, Session session)
        {
            if (!CanEditConfig(session))
            {
                return;
            }

            if (string.IsNullOrEmpty(group) || !permission.GroupExists(group))
            {
                session.Status = "Group not found.";
                return;
            }

            if (group.Equals(ResolveDefaultGroup(), StringComparison.OrdinalIgnoreCase))
            {
                session.Status = "The default group cannot be deleted.";
                return;
            }

            permission.RemoveGroup(group);

            foreach (List<string> ladder in config.Tracks.Values)
            {
                ladder.RemoveAll(g => g.Equals(group, StringComparison.OrdinalIgnoreCase));
            }

            SaveConfig();
            SavePermissionData();

            session.Group = null;
            session.Mode = "groups";
            session.Page = 0;
            session.Status = "Group '" + group + "' deleted.";
        }

        private void AddGroupMember(string group, string input, Session session)
        {
            string userId = ResolveUserId(input);
            if (userId == null)
            {
                session.Status = "Player not found. Use a name or 17-digit id.";
                return;
            }

            permission.AddUserGroup(userId, group);
            SavePermissionData();
            session.Status = "Added " + PlayerName(userId) + " to '" + group + "'.";
        }

        #endregion

        #region Track operations

        private List<string> GetTrack(string trackName)
        {
            List<string> track;
            return !string.IsNullOrEmpty(trackName) && config.Tracks.TryGetValue(trackName, out track) ? track : null;
        }

        private List<string> ExistingTrackGroups(string trackName)
        {
            List<string> track = GetTrack(trackName);
            if (track == null)
            {
                return new List<string>();
            }

            return track.Where(permission.GroupExists).ToList();
        }

        private void CreateTrack(string name, Session session)
        {
            if (!CanEditConfig(session))
            {
                return;
            }

            if (!ValidName(name))
            {
                session.Status = "Use only letters, numbers, '_', '-' or '.' (max 48).";
                return;
            }

            if (config.Tracks.ContainsKey(name))
            {
                session.Status = "Track already exists.";
                return;
            }

            config.Tracks[name] = new List<string>();
            SaveConfig();
            session.Track = name;
            session.Mode = "track";
            session.Page = 0;
            session.Status = "Track '" + name + "' created.";
        }

        private void DeleteTrack(string name, Session session)
        {
            if (!CanEditConfig(session))
            {
                return;
            }

            if (string.IsNullOrEmpty(name) || !config.Tracks.Remove(name))
            {
                session.Status = "Track not found.";
                return;
            }

            SaveConfig();
            session.Track = null;
            session.Mode = "tracks";
            session.Page = 0;
            session.Status = "Track '" + name + "' deleted.";
        }

        private void AddTrackGroup(string trackName, string group, Session session)
        {
            if (!CanEditConfig(session))
            {
                return;
            }

            List<string> track = GetTrack(trackName);
            if (track == null)
            {
                session.Status = "Track not found.";
                return;
            }

            if (!permission.GroupExists(group))
            {
                session.Status = "Group not found.";
                return;
            }

            if (track.Any(g => g.Equals(group, StringComparison.OrdinalIgnoreCase)))
            {
                session.Status = "That group is already in the track.";
                return;
            }

            track.Add(group);
            SaveConfig();
            session.Status = "Added '" + group + "' to track '" + trackName + "'.";
        }

        private void RemoveTrackGroup(string trackName, string group, Session session)
        {
            if (!CanEditConfig(session))
            {
                return;
            }

            List<string> track = GetTrack(trackName);
            if (track == null)
            {
                session.Status = "Track not found.";
                return;
            }

            track.RemoveAll(g => g.Equals(group, StringComparison.OrdinalIgnoreCase));
            SaveConfig();
            session.Status = "Removed '" + group + "' from track '" + trackName + "'.";
        }

        private void EnrollUserInTrack(string trackName, string userId, Session session)
        {
            List<string> ladder = ExistingTrackGroups(trackName);
            if (ladder.Count == 0)
            {
                session.Status = "This track has no existing groups.";
                return;
            }

            RemoveUserFromTrack(trackName, userId);
            permission.AddUserGroup(userId, ladder[0]);
            ApplyDefaultGroupForTrackUser(userId, ladder, true);
            SavePermissionData();
            session.Status = "Enrolled " + PlayerName(userId) + " in track '" + trackName + "' at '" + ladder[0] + "'.";
        }

        private void MoveUserInTrack(string trackName, string userId, bool promote, Session session)
        {
            List<string> ladder = ExistingTrackGroups(trackName);
            if (ladder.Count == 0)
            {
                session.Status = "This track has no existing groups.";
                return;
            }

            string[] groups = permission.GetUserGroups(userId);
            int current = -1;
            for (int i = 0; i < ladder.Count; i++)
            {
                if (groups.Any(g => g.Equals(ladder[i], StringComparison.OrdinalIgnoreCase)))
                {
                    current = i;
                }
            }

            int next;
            if (promote)
            {
                next = current < 0 ? 0 : current + 1;
                if (next >= ladder.Count)
                {
                    session.Status = PlayerName(userId) + " is already at the top of '" + trackName + "'.";
                    return;
                }
            }
            else
            {
                if (current < 0)
                {
                    session.Status = PlayerName(userId) + " is not in track '" + trackName + "'.";
                    return;
                }

                next = current - 1;
            }

            RemoveUserFromTrack(trackName, userId);
            ApplyDefaultGroupForTrackUser(userId, ladder, next >= 0);

            if (next >= 0)
            {
                permission.AddUserGroup(userId, ladder[next]);
                session.Status = (promote ? "Promoted " : "Demoted ") + PlayerName(userId) + " to '" + ladder[next] + "'.";
            }
            else
            {
                session.Status = "Removed " + PlayerName(userId) + " from track '" + trackName + "'.";
            }

            SavePermissionData();
        }

        private void RemoveUserFromTrack(string trackName, string userId)
        {
            List<string> track = GetTrack(trackName);
            if (track == null)
            {
                return;
            }

            foreach (string group in track)
            {
                permission.RemoveUserGroup(userId, group);
            }
        }

        private void ApplyDefaultGroupForTrackUser(string userId, List<string> ladder, bool inTrack)
        {
            string defaultGroup = ResolveDefaultGroup();
            if (string.IsNullOrEmpty(defaultGroup) || !permission.GroupExists(defaultGroup))
            {
                return;
            }

            bool ladderHasDefault = ladder.Any(g => g.Equals(defaultGroup, StringComparison.OrdinalIgnoreCase));
            if (ladderHasDefault)
            {
                return;
            }

            if (!inTrack || config.AddDefaultGroupForTrackUsers)
            {
                permission.AddUserGroup(userId, defaultGroup);
            }
            else
            {
                permission.RemoveUserGroup(userId, defaultGroup);
            }
        }

        private bool UserIsInAnyTrack(string userId)
        {
            if (string.IsNullOrEmpty(userId) || config.Tracks == null || config.Tracks.Count == 0)
            {
                return false;
            }

            string[] groups = permission.GetUserGroups(userId);
            if (groups.Length == 0)
            {
                return false;
            }

            foreach (List<string> ladder in config.Tracks.Values)
            {
                if (ladder == null)
                {
                    continue;
                }

                for (int i = 0; i < groups.Length; i++)
                {
                    if (ladder.Any(group => group.Equals(groups[i], StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool DefaultGroupIsInAnyTrackLadder()
        {
            string defaultGroup = ResolveDefaultGroup();
            if (string.IsNullOrEmpty(defaultGroup) || config.Tracks == null)
            {
                return false;
            }

            return config.Tracks.Values.Any(ladder =>
                ladder != null && ladder.Any(group => group.Equals(defaultGroup, StringComparison.OrdinalIgnoreCase)));
        }

        private string UserTrackGroup(string trackName, string userId)
        {
            List<string> track = GetTrack(trackName);
            if (track == null)
            {
                return null;
            }

            string[] groups = permission.GetUserGroups(userId);
            for (int i = track.Count - 1; i >= 0; i--)
            {
                if (groups.Any(g => g.Equals(track[i], StringComparison.OrdinalIgnoreCase)))
                {
                    return track[i];
                }
            }

            return null;
        }

        #endregion

        #region Temporary permissions

        private static bool TryParseDuration(string input, out TimeSpan duration)
        {
            duration = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            string text = input.Trim().ToLowerInvariant().Replace(" ", string.Empty);
            int index = 0;
            bool parsed = false;

            while (index < text.Length)
            {
                int start = index;
                while (index < text.Length && char.IsDigit(text[index]))
                {
                    index++;
                }

                if (start == index)
                {
                    return false;
                }

                int amount;
                if (!int.TryParse(text.Substring(start, index - start), out amount) || amount < 0)
                {
                    return false;
                }

                long multiplier = 60;
                if (index < text.Length)
                {
                    char unit = text[index++];
                    if (unit == 's')
                    {
                        multiplier = 1;
                    }
                    else if (unit == 'm')
                    {
                        multiplier = 60;
                    }
                    else if (unit == 'h')
                    {
                        multiplier = 3600;
                    }
                    else if (unit == 'd')
                    {
                        multiplier = 86400;
                    }
                    else if (unit == 'w')
                    {
                        multiplier = 604800;
                    }
                    else
                    {
                        return false;
                    }
                }

                double seconds = (double)amount * multiplier;
                if (seconds > TimeSpan.FromDays(3650).TotalSeconds)
                {
                    return false;
                }

                duration += TimeSpan.FromSeconds(seconds);
                parsed = true;
            }

            return parsed && duration > TimeSpan.Zero && duration <= TimeSpan.FromDays(3650);
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration < TimeSpan.Zero)
            {
                duration = TimeSpan.Zero;
            }

            int days = (int)duration.TotalDays;
            if (days > 0)
            {
                return duration.Hours > 0 ? days + "d " + duration.Hours + "h" : days + "d";
            }

            if (duration.Hours > 0)
            {
                return duration.Minutes > 0
                    ? duration.Hours + "h " + duration.Minutes + "m"
                    : duration.Hours + "h";
            }

            if (duration.Minutes > 0)
            {
                return duration.Minutes + "m";
            }

            return Math.Max(1, duration.Seconds) + "s";
        }

        private TempPermissionEntry GetTempPermissionEntry(string userId, string permissionName)
        {
            if (stateData == null || stateData.TempPermissions == null || string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(permissionName))
            {
                return null;
            }

            return stateData.TempPermissions.FirstOrDefault(entry =>
                entry != null &&
                string.Equals(entry.UserId, userId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.Permission, permissionName, StringComparison.OrdinalIgnoreCase) &&
                entry.ExpiresUtcTicks > DateTime.UtcNow.Ticks);
        }

        private List<TempPermissionEntry> TempPermissionsForUser(string userId)
        {
            if (stateData == null || stateData.TempPermissions == null || string.IsNullOrEmpty(userId))
            {
                return new List<TempPermissionEntry>();
            }

            DateTime now = DateTime.UtcNow;
            return stateData.TempPermissions
                .Where(entry => entry != null &&
                                string.Equals(entry.UserId, userId, StringComparison.OrdinalIgnoreCase) &&
                                entry.ExpiresUtcTicks > now.Ticks)
                .OrderBy(entry => entry.ExpiresUtcTicks)
                .ToList();
        }

        private string TempPermissionRemaining(string userId, string permissionName)
        {
            TempPermissionEntry entry = GetTempPermissionEntry(userId, permissionName);
            if (entry == null)
            {
                return string.Empty;
            }

            DateTime expires = new DateTime(entry.ExpiresUtcTicks, DateTimeKind.Utc);
            return FormatDuration(expires - DateTime.UtcNow);
        }

        private void GrantTempPermission(string userId, string permissionName, string durationText, Session session)
        {
            if (string.IsNullOrEmpty(userId))
            {
                session.Status = "No user selected.";
                return;
            }

            if (string.IsNullOrEmpty(permissionName))
            {
                session.Status = "Permission cannot be empty.";
                return;
            }

            if (!permission.PermissionExists(permissionName))
            {
                session.Status = "That permission is not registered by any loaded plugin.";
                return;
            }

            TimeSpan duration;
            if (!TryParseDuration(durationText, out duration))
            {
                session.Status = "Use a duration such as 30m, 2h, 7d or 1d12h.";
                return;
            }

            if (UserHasDirect(userId, permissionName))
            {
                TempPermissionEntry active = GetTempPermissionEntry(userId, permissionName);
                if (active == null)
                {
                    session.Status = PlayerName(userId) + " already has '" + permissionName + "' directly; a temporary grant is not needed.";
                    return;
                }
            }

            ExpireTempPermissions();

            long expiresTicks = DateTime.UtcNow.Add(duration).Ticks;
            TempPermissionEntry entry = GetTempPermissionEntry(userId, permissionName);

            if (entry == null)
            {
                stateData.TempPermissions.Add(new TempPermissionEntry
                {
                    UserId = userId,
                    Permission = permissionName,
                    ExpiresUtcTicks = expiresTicks,
                    WasDirect = false
                });
            }
            else
            {
                entry.ExpiresUtcTicks = Math.Max(entry.ExpiresUtcTicks, expiresTicks);
            }

            permission.GrantUserPermission(userId, permissionName, null);
            SaveState();
            SavePermissionData();
            session.Status = "Granted temporary '" + permissionName + "' to " + PlayerName(userId) +
                             " for " + FormatDuration(duration) + ".";
        }

        private void RemoveTempPermission(string userId, string permissionName, Session session)
        {
            TempPermissionEntry entry = GetTempPermissionEntry(userId, permissionName);
            if (entry == null)
            {
                session.Status = "No active temporary permission '" + permissionName + "' for " + PlayerName(userId) + ".";
                return;
            }

            stateData.TempPermissions.Remove(entry);
            if (!entry.WasDirect)
            {
                permission.RevokeUserPermission(userId, permissionName);
            }

            SaveState();
            SavePermissionData();
            session.Status = "Removed temporary '" + permissionName + "' from " + PlayerName(userId) + ".";
        }

        private void ForgetTempPermission(string userId, string permissionName)
        {
            if (stateData == null || stateData.TempPermissions == null)
            {
                return;
            }

            int removed = stateData.TempPermissions.RemoveAll(entry =>
                entry != null &&
                string.Equals(entry.UserId, userId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.Permission, permissionName, StringComparison.OrdinalIgnoreCase));

            if (removed > 0)
            {
                SaveState();
            }
        }

        private void ExpireTempPermissions()
        {
            if (stateData == null || stateData.TempPermissions == null || stateData.TempPermissions.Count == 0)
            {
                return;
            }

            long now = DateTime.UtcNow.Ticks;
            List<TempPermissionEntry> expired = stateData.TempPermissions
                .Where(entry => entry != null && entry.ExpiresUtcTicks > 0 && entry.ExpiresUtcTicks <= now)
                .ToList();

            if (expired.Count == 0)
            {
                return;
            }

            for (int i = 0; i < expired.Count; i++)
            {
                TempPermissionEntry entry = expired[i];
                stateData.TempPermissions.Remove(entry);
                if (!entry.WasDirect)
                {
                    permission.RevokeUserPermission(entry.UserId, entry.Permission);
                }
            }

            SaveState();
            SavePermissionData();
            Interface.Oxide.LogInfo("[PermissionGui] expired " + expired.Count + " temporary permission(s).");
        }

        private void PruneExpiredTempPermissions()
        {
            ExpireTempPermissions();
        }

        #endregion

        #region ServerAssist hook

        private void RegisterServerAssistHook()
        {
            try
            {
                Plugin serverAssist = plugins.Find("ServerAssist");
                if (serverAssist == null)
                {
                    return;
                }

                serverAssist.Call(
                    "RegisterGuiHook",
                    "permissions",
                    "Permission Manager",
                    "Groups, users, tracks, defaults and a searchable permission browser.",
                    PermUse,
                    "PermissionGui",
                    nameof(HandleServerAssistRequest));

                serverAssist.Call("SetPanelIcon", "permissions", "sprite:sa_permission_gui");
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[PermissionGui] could not register the ServerAssist hook: " + exception.Message);
            }
        }

        private void UnregisterServerAssistHook()
        {
            try
            {
                Plugin serverAssist = plugins.Find("ServerAssist");
                if (serverAssist != null)
                {
                    serverAssist.Call("UnregisterGuiHook", "permissions");
                }
            }
            catch
            {
                // ServerAssist may already be unloading.
            }
        }

        public string HandleServerAssistRequest(string playerId, string payload)
        {
            return HandleGuiRequest(playerId, payload);
        }

        private bool TryOpenServerAssistHook(IPlayer player, string subAction)
        {
            if (player == null || string.IsNullOrEmpty(player.Id))
            {
                return false;
            }

            Plugin serverAssist = plugins.Find("ServerAssist");
            if (serverAssist == null)
            {
                return false;
            }

            try
            {
                object result = serverAssist.Call(
                    "OpenHookForPlayerWithAction",
                    player.Id,
                    "permissions",
                    subAction ?? string.Empty);

                return result is bool && (bool)result;
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[PermissionGui] could not open the ServerAssist hook: " + exception.Message);
                return false;
            }
        }

        #endregion

        #region Session and view model

        private sealed class Session
        {
            public string Mode = "home";
            public string Group;
            public string User;
            public string Track;
            public string SelectedPermission;
            public string SelectedEntryKey;
            public int Page;
            public string Filter = string.Empty;
            public string InputAction;
            public string InputLabel = string.Empty;
            public string InputValue = string.Empty;
            public string Status = string.Empty;
            public string PendingTempPermission = string.Empty;
        }

        private sealed class RowInfo
        {
            public string Action = string.Empty;
            public string Label = string.Empty;
            public string Sub = string.Empty;
            public bool Enabled = true;
            public bool Selected;
            public string Permission = string.Empty;
            public string Icon = string.Empty;
            public string IconTint = string.Empty;
        }

        private sealed class ButtonInfo
        {
            public string Action = string.Empty;
            public string Label = string.Empty;
            public bool Enabled = true;
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

        public string HandleGuiRequest(string playerId, string payload)
        {
            try
            {
                if (!permission.IsLoaded)
                {
                    return ErrorView("The Oxide permission system is not loaded.");
                }

                if (string.IsNullOrEmpty(playerId) || !CanUse(playerId))
                {
                    return ErrorView("You do not have permission to use the Permission Manager.");
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

                return ErrorView("Unknown Permission Manager request.");
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogException("[PermissionGui] GUI request failed", exception);
                return ErrorView("Internal error: " + exception.Message);
            }
        }

        private bool IsSearchable(string mode)
        {
            return mode == "groups" || mode == "users" || mode == "tracks" ||
                   mode == "groupPerms" || mode == "userPerms" || mode == "permissions";
        }

        private string SearchLabel(string mode)
        {
            if (mode == "groups") return "Filter groups";
            if (mode == "users") return "Filter users";
            if (mode == "tracks") return "Filter tracks";
            return "Search permissions/commands";
        }

        private string Render(string playerId, Session session)
        {
            bool admin = IsAdmin(playerId);
            List<RowInfo> rows = new List<RowInfo>();
            List<ButtonInfo> buttons = new List<ButtonInfo>();
            string subtitle = string.Empty;

            switch (session.Mode)
            {
                case "groups":
                    RenderGroups(session, rows, buttons, admin, out subtitle);
                    break;
                case "group":
                    RenderGroup(session, rows, buttons, admin, out subtitle);
                    break;
                case "groupPerms":
                    RenderGroupPerms(session, rows, buttons, admin, out subtitle);
                    break;
                case "groupMembers":
                    RenderGroupMembers(session, rows, buttons, admin, out subtitle);
                    break;
                case "users":
                    RenderUsers(session, rows, buttons, admin, out subtitle);
                    break;
                case "user":
                    RenderUser(session, rows, buttons, admin, out subtitle);
                    break;
                case "userPerms":
                    RenderUserPerms(session, rows, buttons, admin, out subtitle);
                    break;
                case "userTracks":
                    RenderUserTracks(session, rows, buttons, admin, out subtitle);
                    break;
                case "userTemps":
                    RenderUserTemps(session, rows, buttons, admin, out subtitle);
                    break;
                case "tracks":
                    RenderTracks(session, rows, buttons, admin, out subtitle);
                    break;
                case "track":
                    RenderTrack(session, rows, buttons, admin, out subtitle);
                    break;
                case "permissions":
                    RenderPermissions(session, rows, buttons, admin, out subtitle);
                    break;
                case "defaults":
                    RenderDefaults(session, rows, buttons, admin, out subtitle);
                    break;
                default:
                    session.Mode = "home";
                    RenderHome(session, rows, buttons, admin, out subtitle);
                    break;
            }

            bool promptVisible = !string.IsNullOrEmpty(session.InputAction);
            bool searchVisible = !promptVisible && IsSearchable(session.Mode);
            string inputLabel = promptVisible ? session.InputLabel : (searchVisible ? SearchLabel(session.Mode) : string.Empty);
            string inputValue = promptVisible ? session.InputValue : (searchVisible ? session.Filter : string.Empty);

            ApplyModIcons(rows);

            StringBuilder builder = new StringBuilder(4096);
            builder.Append("v1\n");
            builder.Append("title=").Append(Encode("Permission Manager")).Append('\n');
            builder.Append("tab=").Append(Encode(session.Mode)).Append('\n');
            builder.Append("subtitle=").Append(Encode(subtitle)).Append('\n');
            builder.Append("status=").Append(Encode(session.Status)).Append('\n');
            builder.Append("input=")
                .Append(Encode(inputLabel))
                .Append('|')
                .Append(promptVisible || searchVisible ? "1" : "0")
                .Append('|')
                .Append(Encode(inputValue))
                .Append('\n');

            for (int i = 0; i < rows.Count && i < UiRows; i++)
            {
                RowInfo row = rows[i];
                builder.Append("row=").Append(i).Append('|')
                    .Append(Encode(row.Action)).Append('|')
                    .Append(Encode(row.Label)).Append('|')
                    .Append(Encode(row.Sub)).Append('|')
                    .Append(row.Enabled ? "1" : "0").Append('|')
                    .Append(row.Selected ? "1" : "0");

                if (!string.IsNullOrEmpty(row.Icon))
                {
                    builder.Append('|')
                        .Append(Encode(row.Icon)).Append("||")
                        .Append(Encode(row.IconTint));
                }

                builder.Append('\n');
            }

            for (int i = 0; i < buttons.Count && i < UiButtons; i++)
            {
                ButtonInfo button = buttons[i];
                builder.Append("btn=").Append(i).Append('|')
                    .Append(Encode(button.Action)).Append('|')
                    .Append(Encode(button.Label)).Append('|')
                    .Append(button.Enabled ? "1" : "0").Append('\n');
            }

            return builder.ToString();
        }

        private static string UiIcon(string action, string label)
        {
            string a = action ?? string.Empty;
            string l = label ?? string.Empty;

            if (a.StartsWith("nav|groups", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_players";
            if (a.StartsWith("nav|users", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_players";
            if (a.StartsWith("nav|tracks", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_warps";
            if (a.StartsWith("nav|permissions", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_permission_gui";
            if (a.StartsWith("nav|defaults", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_server";
            if (a.StartsWith("nav|home", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_permission_gui";

            if (a.StartsWith("defaults|reload", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_plugin_hot_reload";
            if (a.StartsWith("defaults|apply", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_permission_gui";
            if (a.StartsWith("defaults|add", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_act_give";
            if (a.StartsWith("defaults|group", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_players";

            if (a.StartsWith("group|select", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_players";
            if (a.StartsWith("group|members", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_players";
            if (a.StartsWith("group|perms", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_permission_gui";
            if (a.StartsWith("group|new", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_players";

            if (a.StartsWith("gm|add", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_players";

            if (a.StartsWith("user|select", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_players";
            if (a.StartsWith("user|groupadd", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_players";
            if (a.StartsWith("user|groupremove", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_players";
            if (a.StartsWith("user|permadd", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_permission_gui";
            if (a.StartsWith("user|perms", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_permission_gui";
            if (a.StartsWith("user|tracks", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_warps";

            if (a.StartsWith("gp|addcustom", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_permission_gui";
            if (a.StartsWith("up|addcustom", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_permission_gui";

            if (a.StartsWith("track|select", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_warps";
            if (a.StartsWith("track|new", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_warps";
            if (a.StartsWith("track|addgroup", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_warps";
            if (a.StartsWith("track|enroll", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_warps";
            if (a.StartsWith("track|promote", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_warps";
            if (a.StartsWith("track|demote", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_warps";
            if (a.StartsWith("track|removegroup", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_players";

            if (a.StartsWith("ut|select", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_warps";
            if (a.StartsWith("ut|enroll", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_warps";
            if (a.StartsWith("ut|promote", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_warps";
            if (a.StartsWith("ut|demote", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_warps";

            if (a.StartsWith("perm|cmd|oxidecmd", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_server";
            if (a.StartsWith("perm|cmd|consolecmd", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_world";
            if (a.StartsWith("perm|cmd|configcmd", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_world";
            if (a.StartsWith("perm|grant", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_act_give";
            if (a.StartsWith("perm|select", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_plugin_hot_reload";

            if (l.StartsWith("Groups:", StringComparison.OrdinalIgnoreCase) ||
                l.StartsWith("Known users:", StringComparison.OrdinalIgnoreCase) ||
                l.StartsWith("Default group:", StringComparison.OrdinalIgnoreCase))
            {
                return "sprite:sa_nav_players";
            }

            if (l.StartsWith("Tracks:", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_warps";
            if (l.StartsWith("Admin wildcard:", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_moderation";
            if (l.StartsWith("No ", StringComparison.OrdinalIgnoreCase)) return "sprite:sa_nav_help";

            return string.Empty;
        }

        private static void AddRow(List<RowInfo> rows, string action, string label, string sub, bool enabled = true, bool selected = false, string permissionName = null)
        {
            rows.Add(new RowInfo
            {
                Action = action,
                Label = label,
                Sub = sub,
                Enabled = enabled,
                Selected = selected,
                Permission = permissionName ?? string.Empty,
                Icon = UiIcon(action, label)
            });
        }

        private static void AddButton(List<ButtonInfo> buttons, string action, string label, bool enabled = true)
        {
            buttons.Add(new ButtonInfo
            {
                Action = action,
                Label = label,
                Enabled = enabled
            });
        }

        private static void AddPaging(Session session, int pages, List<ButtonInfo> buttons)
        {
            if (session.Page > 0)
            {
                AddButton(buttons, "page|prev", "Previous");
            }

            if (session.Page + 1 < pages)
            {
                AddButton(buttons, "page|next", "Next");
            }
        }

        private int RowsPerPage
        {
            get { return Math.Max(1, Math.Min(UiRows, config.MaxRows)); }
        }

        private int Pages(int count)
        {
            return Math.Max(1, (count + RowsPerPage - 1) / RowsPerPage);
        }

        private void ClampPage(Session session, int count)
        {
            int pages = Pages(count);
            if (session.Page < 0)
            {
                session.Page = 0;
            }
            else if (session.Page >= pages)
            {
                session.Page = pages - 1;
            }
        }

        private string YesNo(bool value)
        {
            return value ? "yes" : "no";
        }

        private string Encode(string value)
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

        private string Decode(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("%0A", "\n").Replace("%7C", "|").Replace("%25", "%");
        }

        private string ErrorView(string message)
        {
            return "v1\n" +
                   "title=" + Encode("Permission Manager") + "\n" +
                   "tab=home\n" +
                   "subtitle=" + Encode("Permission data unavailable") + "\n" +
                   "status=" + Encode(message) + "\n" +
                   "input=||0\n" +
                   "row=0||" + Encode("No data") + "|" + Encode(message) + "|0|0|" + Encode("sprite:sa_nav_help") + "||\n" +
                   "btn=0|refresh|" + Encode("Retry") + "|1\n";
        }

        #endregion

        #region Render pages

        private void RenderHome(Session session, List<RowInfo> rows, List<ButtonInfo> buttons, bool admin, out string subtitle)
        {
            string defaultGroup = ResolveDefaultGroup();
            Dictionary<string, string> users = CollectUsers();

            subtitle = "Oxide-only permission manager | groups: " + permission.GetGroups().Length +
                       " | users: " + users.Count +
                       " | tracks: " + config.Tracks.Count +
                       " | admin wildcard: " + AdminWildcard;

            AddRow(rows, string.Empty, "Groups: " + permission.GetGroups().Length, "create, delete, permissions, members", false);
            AddRow(rows, string.Empty, "Known users: " + users.Count, "groups, direct permissions, tracks", false);
            AddRow(rows, string.Empty, "Tracks: " + config.Tracks.Count, "ladders, enroll, promote, demote", false);
            AddRow(rows, string.Empty, "Default group: " + (string.IsNullOrEmpty(defaultGroup) ? "(none)" : defaultGroup),
                "default permissions: " + config.DefaultPermissions.Count, false);
            AddRow(rows, string.Empty, "Admin wildcard: " + AdminWildcard,
                "require: " + YesNo(config.RequireWildcardForAdmin) + " | grant all perms: " + YesNo(config.GrantAllPermissionsToAdmin), false);

            AddButton(buttons, "nav|groups", "Groups");
            AddButton(buttons, "nav|users", "Users");
            AddButton(buttons, "nav|tracks", "Tracks");
            AddButton(buttons, "nav|permissions", "Permission Search");
            AddButton(buttons, "nav|defaults", "Defaults");
            AddButton(buttons, "defaults|apply", "Apply Defaults", admin);
            AddButton(buttons, "defaults|reload", "Reload Config", admin);
        }

        private void RenderGroups(Session session, List<RowInfo> rows, List<ButtonInfo> buttons, bool admin, out string subtitle)
        {
            List<string> groups = permission.GetGroups()
                .Where(g => string.IsNullOrEmpty(session.Filter) || g.IndexOf(session.Filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(g => permission.GetGroupRank(g))
                .ThenBy(g => g, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ClampPage(session, groups.Count);
            int pages = Pages(groups.Count);
            subtitle = "Groups | " + groups.Count + " shown | page " + (session.Page + 1) + "/" + pages;

            int start = session.Page * RowsPerPage;
            for (int i = 0; i < RowsPerPage && start + i < groups.Count; i++)
            {
                string group = groups[start + i];
                string parent = permission.GetGroupParent(group);
                AddRow(rows, "group|select|" + group, group,
                    "rank " + permission.GetGroupRank(group) +
                    " | " + permission.GetUsersInGroup(group).Length + " member(s)" +
                    (string.IsNullOrEmpty(parent) ? string.Empty : " | parent " + parent));
            }

            if (groups.Count == 0)
            {
                AddRow(rows, string.Empty, "No groups match.", string.Empty, false);
            }

            AddPaging(session, pages, buttons);
            if (admin)
            {
                AddButton(buttons, "group|new", "New Group");
            }
            if (!string.IsNullOrEmpty(session.Filter))
            {
                AddButton(buttons, "clearfilter", "Clear Filter");
            }
            AddButton(buttons, "nav|home", "Home");
        }

        private void RenderGroup(Session session, List<RowInfo> rows, List<ButtonInfo> buttons, bool admin, out string subtitle)
        {
            string group = session.Group;
            if (string.IsNullOrEmpty(group) || !permission.GroupExists(group))
            {
                session.Mode = "groups";
                RenderGroups(session, rows, buttons, admin, out subtitle);
                return;
            }

            string parent = permission.GetGroupParent(group);
            HashSet<string> direct = new HashSet<string>(permission.GetGroupPermissions(group, false), StringComparer.OrdinalIgnoreCase);
            HashSet<string> effective = EffectiveGroupPermissions(group);
            string[] members = permission.GetUsersInGroup(group);

            subtitle = "Group: " + group +
                       " | title: " + (string.IsNullOrEmpty(permission.GetGroupTitle(group)) ? "(none)" : permission.GetGroupTitle(group)) +
                       " | rank " + permission.GetGroupRank(group) +
                       " | parent: " + (string.IsNullOrEmpty(parent) ? "none" : parent) +
                       " | " + direct.Count + " direct / " + effective.Count + " effective";

            ClampPage(session, members.Length);
            int start = session.Page * RowsPerPage;
            for (int i = 0; i < RowsPerPage && start + i < members.Length; i++)
            {
                string id = ExtractId(members[start + i]);
                AddRow(rows, "user|select|" + id, PlayerName(id), id);
            }

            if (members.Length == 0)
            {
                AddRow(rows, string.Empty, "No members.", "Use Members to add one.", false);
            }

            AddPaging(session, Pages(members.Length), buttons);
            AddButton(buttons, "group|perms|" + group, "Permissions");
            AddButton(buttons, "group|members|" + group, "Members");
            if (admin)
            {
                AddButton(buttons, "group|prompt|" + group + "|title", "Set Title");
                AddButton(buttons, "group|prompt|" + group + "|rank", "Set Rank");
                AddButton(buttons, "group|prompt|" + group + "|parent", "Set Parent");
                AddButton(buttons, "group|prompt|" + group + "|rename", "Rename");
                AddButton(buttons, "group|confirmdelete|" + group, "Delete");
            }
            AddButton(buttons, "nav|groups", "Back");
        }

        private void RenderGroupPerms(Session session, List<RowInfo> rows, List<ButtonInfo> buttons, bool admin, out string subtitle)
        {
            string group = session.Group;
            if (string.IsNullOrEmpty(group) || !permission.GroupExists(group))
            {
                session.Mode = "groups";
                RenderGroups(session, rows, buttons, admin, out subtitle);
                return;
            }

            HashSet<string> direct = new HashSet<string>(permission.GetGroupPermissions(group, false), StringComparer.OrdinalIgnoreCase);
            HashSet<string> effective = EffectiveGroupPermissions(group);
            HashSet<string> inherited = new HashSet<string>(effective, StringComparer.OrdinalIgnoreCase);
            inherited.ExceptWith(direct);

            List<string> permissions = AllPermissionsForGroup(group)
                .Where(p => string.IsNullOrEmpty(session.Filter) || p.IndexOf(session.Filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ClampPage(session, permissions.Count);
            int pages = Pages(permissions.Count);
            subtitle = "Permissions for " + group + " | " + direct.Count + " direct | " + inherited.Count + " inherited" +
                       (string.IsNullOrEmpty(session.Filter) ? string.Empty : " | search: " + session.Filter) +
                       " | page " + (session.Page + 1) + "/" + pages;

            int start = session.Page * RowsPerPage;
            for (int i = 0; i < RowsPerPage && start + i < permissions.Count; i++)
            {
                string permissionName = permissions[start + i];
                bool isDirect = direct.Contains(permissionName);
                bool isInherited = inherited.Contains(permissionName);
                AddRow(rows, "gp|toggle|" + group + "|" + permissionName,
                    (isDirect ? "X " : isInherited ? "I " : "- ") + permissionName,
                    isDirect ? "direct" : isInherited ? "inherited" : "not granted",
                    admin, false, permissionName);
            }

            if (permissions.Count == 0)
            {
                AddRow(rows, string.Empty, "No permissions match.", string.Empty, false);
            }

            AddPaging(session, pages, buttons);
            if (admin)
            {
                AddButton(buttons, "gp|addcustom|" + group, "Add Custom");
            }
            if (!string.IsNullOrEmpty(session.Filter))
            {
                AddButton(buttons, "clearfilter", "Clear Search");
            }
            AddButton(buttons, "group|select|" + group, "Back");
        }

        private void RenderGroupMembers(Session session, List<RowInfo> rows, List<ButtonInfo> buttons, bool admin, out string subtitle)
        {
            string group = session.Group;
            if (string.IsNullOrEmpty(group) || !permission.GroupExists(group))
            {
                session.Mode = "groups";
                RenderGroups(session, rows, buttons, admin, out subtitle);
                return;
            }

            string[] members = permission.GetUsersInGroup(group);
            ClampPage(session, members.Length);
            int pages = Pages(members.Length);
            subtitle = "Members of " + group + " | " + members.Length + " | page " + (session.Page + 1) + "/" + pages;

            int start = session.Page * RowsPerPage;
            for (int i = 0; i < RowsPerPage && start + i < members.Length; i++)
            {
                string id = ExtractId(members[start + i]);
                AddRow(rows, "user|select|" + id, PlayerName(id), id);
            }

            if (members.Length == 0)
            {
                AddRow(rows, string.Empty, "No members.", string.Empty, false);
            }

            AddPaging(session, pages, buttons);
            if (admin)
            {
                AddButton(buttons, "gm|add|" + group, "Add Member");
            }
            AddButton(buttons, "group|select|" + group, "Back");
        }

        private void RenderUsers(Session session, List<RowInfo> rows, List<ButtonInfo> buttons, bool admin, out string subtitle)
        {
            Dictionary<string, string> users = CollectUsers();
            List<KeyValuePair<string, string>> list = users
                .Where(pair => string.IsNullOrEmpty(session.Filter) ||
                               pair.Key.IndexOf(session.Filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                               (pair.Value ?? string.Empty).IndexOf(session.Filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
                .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ClampPage(session, list.Count);
            int pages = Pages(list.Count);
            subtitle = "Users | " + users.Count + " known | page " + (session.Page + 1) + "/" + pages +
                       (string.IsNullOrEmpty(session.Filter) ? string.Empty : " | filter: " + session.Filter);

            int start = session.Page * RowsPerPage;
            for (int i = 0; i < RowsPerPage && start + i < list.Count; i++)
            {
                string id = list[start + i].Key;
                string[] groups = permission.GetUserGroups(id);
                AddRow(rows, "user|select|" + id, list[start + i].Value,
                    id + " | " + (groups.Length == 0 ? "no groups" : string.Join(", ", groups)));
            }

            if (list.Count == 0)
            {
                AddRow(rows, string.Empty, "No users match.", string.Empty, false);
            }

            AddPaging(session, pages, buttons);
            if (!string.IsNullOrEmpty(session.Filter))
            {
                AddButton(buttons, "clearfilter", "Clear Filter");
            }
            AddButton(buttons, "nav|home", "Home");
        }

        private void RenderUser(Session session, List<RowInfo> rows, List<ButtonInfo> buttons, bool admin, out string subtitle)
        {
            string userId = session.User;
            if (string.IsNullOrEmpty(userId))
            {
                session.Mode = "users";
                RenderUsers(session, rows, buttons, admin, out subtitle);
                return;
            }

            string[] groups = permission.GetUserGroups(userId);
            UserData user = permission.GetUserData(userId);
            HashSet<string> direct = new HashSet<string>(
                user != null && user.Perms != null ? user.Perms : new string[0],
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> effective = EffectiveUserPermissions(userId);
            List<TempPermissionEntry> temps = TempPermissionsForUser(userId);

            List<RowInfo> combined = new List<RowInfo>();
            foreach (string group in groups.OrderBy(g => g, StringComparer.OrdinalIgnoreCase))
            {
                combined.Add(new RowInfo
                {
                    Action = "user|groupremove|" + userId + "|" + group,
                    Label = "Group: " + group,
                    Sub = admin ? "click to remove" : "group",
                    Enabled = admin,
                    Icon = UiIcon("user|groupremove|" + userId + "|" + group, "Group: " + group)
                });
            }

            foreach (string permissionName in direct.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string remaining = TempPermissionRemaining(userId, permissionName);
                string sub = admin
                    ? (string.IsNullOrEmpty(remaining) ? "click to revoke" : "temp " + remaining + " | click to revoke")
                    : (string.IsNullOrEmpty(remaining) ? "direct permission" : "temp " + remaining);

                combined.Add(new RowInfo
                {
                    Action = "user|permrevoke|" + userId + "|" + permissionName,
                    Label = "Direct: " + permissionName,
                    Sub = sub,
                    Enabled = admin,
                    Permission = permissionName,
                    Icon = UiIcon("user|permrevoke|" + userId + "|" + permissionName, "Direct: " + permissionName)
                });
            }

            ClampPage(session, combined.Count);
            int pages = Pages(combined.Count);
            subtitle = "User: " + PlayerName(userId) + " (" + userId + ")" +
                       " | " + groups.Length + " group(s) | " + direct.Count + " direct / " + effective.Count + " effective" +
                       (temps.Count == 0 ? string.Empty : " | " + temps.Count + " temp");

            int start = session.Page * RowsPerPage;
            for (int i = 0; i < RowsPerPage && start + i < combined.Count; i++)
            {
                rows.Add(combined[start + i]);
            }

            if (combined.Count == 0)
            {
                AddRow(rows, string.Empty, "No groups or direct permissions.", string.Empty, false);
            }

            AddPaging(session, pages, buttons);
            if (admin)
            {
                AddButton(buttons, "user|groupadd|" + userId, "Add Group");
                AddButton(buttons, "user|permadd|" + userId, "Add Permission");
            }
            AddButton(buttons, "user|perms|" + userId, "Permissions");
            AddButton(buttons, "user|tracks|" + userId, "Tracks");
            AddButton(buttons, "utemp|list|" + userId, "Temp Perms");
            AddButton(buttons, "nav|users", "Back");
        }

        private void RenderUserPerms(Session session, List<RowInfo> rows, List<ButtonInfo> buttons, bool admin, out string subtitle)
        {
            string userId = session.User;
            if (string.IsNullOrEmpty(userId))
            {
                session.Mode = "users";
                RenderUsers(session, rows, buttons, admin, out subtitle);
                return;
            }

            UserData user = permission.GetUserData(userId);
            HashSet<string> direct = new HashSet<string>(
                user != null && user.Perms != null ? user.Perms : new string[0],
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> effective = EffectiveUserPermissions(userId);
            HashSet<string> viaGroups = new HashSet<string>(effective, StringComparer.OrdinalIgnoreCase);
            viaGroups.ExceptWith(direct);

            List<string> permissions = AllPermissionsForUser(userId)
                .Where(p => string.IsNullOrEmpty(session.Filter) || p.IndexOf(session.Filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ClampPage(session, permissions.Count);
            int pages = Pages(permissions.Count);
            subtitle = "Permissions for " + PlayerName(userId) + " | " + direct.Count + " direct | " + viaGroups.Count + " via groups" +
                       (string.IsNullOrEmpty(session.Filter) ? string.Empty : " | search: " + session.Filter) +
                       " | page " + (session.Page + 1) + "/" + pages;

            int start = session.Page * RowsPerPage;
            for (int i = 0; i < RowsPerPage && start + i < permissions.Count; i++)
            {
                string permissionName = permissions[start + i];
                bool isDirect = direct.Contains(permissionName);
                bool isVia = viaGroups.Contains(permissionName);
                string remaining = isDirect ? TempPermissionRemaining(userId, permissionName) : string.Empty;
                string sub = isDirect
                    ? (string.IsNullOrEmpty(remaining) ? "direct" : "temp " + remaining)
                    : (isVia ? "via group" : "not granted");

                AddRow(rows, "up|toggle|" + userId + "|" + permissionName,
                    (isDirect ? "X " : isVia ? "V " : "- ") + permissionName,
                    sub,
                    admin, false, permissionName);
            }

            if (permissions.Count == 0)
            {
                AddRow(rows, string.Empty, "No permissions match.", string.Empty, false);
            }

            AddPaging(session, pages, buttons);
            if (admin)
            {
                AddButton(buttons, "up|addcustom|" + userId, "Add Custom");
            }
            if (!string.IsNullOrEmpty(session.Filter))
            {
                AddButton(buttons, "clearfilter", "Clear Search");
            }
            AddButton(buttons, "user|select|" + userId, "Back");
        }

        private void RenderUserTracks(Session session, List<RowInfo> rows, List<ButtonInfo> buttons, bool admin, out string subtitle)
        {
            string userId = session.User;
            if (string.IsNullOrEmpty(userId))
            {
                session.Mode = "users";
                RenderUsers(session, rows, buttons, admin, out subtitle);
                return;
            }

            List<string> tracks = config.Tracks.Keys.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
            ClampPage(session, tracks.Count);
            int pages = Pages(tracks.Count);
            subtitle = "Tracks for " + PlayerName(userId) + " | page " + (session.Page + 1) + "/" + pages;

            int start = session.Page * RowsPerPage;
            for (int i = 0; i < RowsPerPage && start + i < tracks.Count; i++)
            {
                string trackName = tracks[start + i];
                string current = UserTrackGroup(trackName, userId);
                AddRow(rows, "ut|select|" + trackName, trackName,
                    current == null ? "not enrolled" : "current: " + current,
                    true,
                    string.Equals(session.Track, trackName, StringComparison.OrdinalIgnoreCase));
            }

            if (tracks.Count == 0)
            {
                AddRow(rows, string.Empty, "No tracks configured.", "Create one from the Tracks page.", false);
            }

            AddPaging(session, pages, buttons);
            if (admin)
            {
                AddButton(buttons, "ut|enroll", "Enroll");
                AddButton(buttons, "ut|promote", "Promote");
                AddButton(buttons, "ut|demote", "Demote");
            }
            AddButton(buttons, "user|select|" + userId, "Back");
        }

        private void RenderUserTemps(Session session, List<RowInfo> rows, List<ButtonInfo> buttons, bool admin, out string subtitle)
        {
            string userId = session.User;
            if (string.IsNullOrEmpty(userId))
            {
                session.Mode = "users";
                RenderUsers(session, rows, buttons, admin, out subtitle);
                return;
            }

            PruneExpiredTempPermissions();
            List<TempPermissionEntry> entries = TempPermissionsForUser(userId);

            ClampPage(session, entries.Count);
            int pages = Pages(entries.Count);
            subtitle = "Temporary permissions for " + PlayerName(userId) +
                       " | " + entries.Count + " active | page " + (session.Page + 1) + "/" + pages;

            int start = session.Page * RowsPerPage;
            for (int i = 0; i < RowsPerPage && start + i < entries.Count; i++)
            {
                TempPermissionEntry entry = entries[start + i];
                DateTime expires = new DateTime(entry.ExpiresUtcTicks, DateTimeKind.Utc);
                AddRow(rows, "utemp|remove|" + userId + "|" + entry.Permission,
                    entry.Permission,
                    "expires in " + FormatDuration(expires - DateTime.UtcNow),
                    admin, false, entry.Permission);
            }

            if (entries.Count == 0)
            {
                AddRow(rows, string.Empty, "No temporary permissions.", "Use Add Temp to grant one.", false);
            }

            AddPaging(session, pages, buttons);
            if (admin)
            {
                AddButton(buttons, "utemp|add|" + userId, "Add Temp");
            }
            AddButton(buttons, "user|select|" + userId, "Back");
        }

        private void RenderTracks(Session session, List<RowInfo> rows, List<ButtonInfo> buttons, bool admin, out string subtitle)
        {
            List<string> tracks = config.Tracks.Keys
                .Where(t => string.IsNullOrEmpty(session.Filter) || t.IndexOf(session.Filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ClampPage(session, tracks.Count);
            int pages = Pages(tracks.Count);
            subtitle = "Tracks | " + tracks.Count + " shown | page " + (session.Page + 1) + "/" + pages;

            int start = session.Page * RowsPerPage;
            for (int i = 0; i < RowsPerPage && start + i < tracks.Count; i++)
            {
                string name = tracks[start + i];
                List<string> ladder = config.Tracks[name];
                AddRow(rows, "track|select|" + name, name, ladder.Count == 0 ? "empty" : string.Join(" -> ", ladder));
            }

            if (tracks.Count == 0)
            {
                AddRow(rows, string.Empty, "No tracks match.", string.Empty, false);
            }

            AddPaging(session, pages, buttons);
            if (admin)
            {
                AddButton(buttons, "track|new", "New Track");
            }
            if (!string.IsNullOrEmpty(session.Filter))
            {
                AddButton(buttons, "clearfilter", "Clear Filter");
            }
            AddButton(buttons, "nav|home", "Home");
        }

        private void RenderTrack(Session session, List<RowInfo> rows, List<ButtonInfo> buttons, bool admin, out string subtitle)
        {
            List<string> track = GetTrack(session.Track);
            if (track == null)
            {
                session.Mode = "tracks";
                RenderTracks(session, rows, buttons, admin, out subtitle);
                return;
            }

            ClampPage(session, track.Count);
            int pages = Pages(track.Count);
            subtitle = "Track: " + session.Track + " | " + track.Count + " group(s) | " +
                       (track.Count == 0 ? "empty" : string.Join(" -> ", track)) +
                       " | page " + (session.Page + 1) + "/" + pages;

            int start = session.Page * RowsPerPage;
            for (int i = 0; i < RowsPerPage && start + i < track.Count; i++)
            {
                string group = track[start + i];
                bool exists = permission.GroupExists(group);
                AddRow(rows, "track|removegroup|" + session.Track + "|" + group,
                    (start + i + 1) + ". " + group,
                    (exists ? "rank " + permission.GetGroupRank(group) : "missing group") + (admin ? " | click to remove" : string.Empty),
                    admin);
            }

            if (track.Count == 0)
            {
                AddRow(rows, string.Empty, "No groups in this track.", "Add a group to start the ladder.", false);
            }

            AddPaging(session, pages, buttons);
            if (admin)
            {
                AddButton(buttons, "track|addgroup|" + session.Track, "Add Group");
                AddButton(buttons, "track|enroll|" + session.Track, "Enroll User");
                AddButton(buttons, "track|promote|" + session.Track, "Promote User");
                AddButton(buttons, "track|demote|" + session.Track, "Demote User");
                AddButton(buttons, "track|confirmdelete|" + session.Track, "Delete Track");
            }
            AddButton(buttons, "nav|tracks", "Back");
        }

        private sealed class PermissionEntry
        {
            public string Kind = "permission";
            public string Label = string.Empty;
            public string Sub = string.Empty;
            public string Permission = string.Empty;
            public string Command = string.Empty;
            public string Source = string.Empty;
            public string Aliases = string.Empty;
            public string Description = string.Empty;
            public int AdminLevel = -1;
            public string SearchText = string.Empty;
            public string Key = string.Empty;
        }

        private List<PermissionEntry> BuildPermissionEntries()
        {
            List<PermissionEntry> entries = new List<PermissionEntry>();
            Dictionary<string, string> owners = GetPermissionOwners();

            foreach (string permissionName in permission.GetPermissions())
            {
                string owner;
                owners.TryGetValue(permissionName, out owner);
                string[] groups = permission.GetPermissionGroups(permissionName);
                string[] users = permission.GetPermissionUsers(permissionName);

                entries.Add(new PermissionEntry
                {
                    Kind = "permission",
                    Label = permissionName,
                    Sub = "NODE: " + permissionName +
                          (string.IsNullOrEmpty(owner) ? string.Empty : " | plugin: " + owner) +
                          " | groups: " + groups.Length + " | users: " + users.Length,
                    Permission = permissionName,
                    Source = string.IsNullOrEmpty(owner) ? "Oxide permission" : "Oxide plugin " + owner,
                    Description = "groups: " + groups.Length + " | users: " + users.Length,
                    SearchText = "oxide permission " + permissionName + " " + (owner ?? string.Empty),
                    Key = "perm|select|" + permissionName
                });
            }

            AddOxideCommandEntries(entries);
            AddConsoleCommandEntries(entries);
            AddConfiguredCommandEntries(entries);
            return entries;
        }

        private void ApplyModIcons(List<RowInfo> rows)
        {
            bool hasPermissions = false;
            for (int i = 0; i < rows.Count; i++)
            {
                if (!string.IsNullOrEmpty(rows[i].Permission))
                {
                    hasPermissions = true;
                    break;
                }
            }

            if (!hasPermissions)
            {
                return;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                RowInfo row = rows[i];
                if (string.IsNullOrEmpty(row.Permission))
                {
                    continue;
                }

                row.Icon = PermissionTypeIcon(row.Permission);
            }
        }

        private static string PermissionTypeIcon(string permissionName)
        {
            if (permissionName.StartsWith("oxide.", StringComparison.OrdinalIgnoreCase))
            {
                return "sprite:sa_nav_server";
            }

            if (permissionName.StartsWith("7daystodie.", StringComparison.OrdinalIgnoreCase) ||
                permissionName.StartsWith("game.", StringComparison.OrdinalIgnoreCase))
            {
                return "sprite:sa_nav_world";
            }

            return "sprite:sa_plugin_hot_reload";
        }

        private Dictionary<string, string> GetPermissionOwners()
        {
            Dictionary<string, string> owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                System.Reflection.FieldInfo field = typeof(Permission).GetField(
                    "registeredPermissions",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                if (field == null)
                {
                    return owners;
                }

                System.Collections.IDictionary dictionary = field.GetValue(permission) as System.Collections.IDictionary;
                if (dictionary == null)
                {
                    return owners;
                }

                foreach (System.Collections.DictionaryEntry entry in dictionary)
                {
                    Plugin plugin = entry.Key as Plugin;
                    if (plugin == null)
                    {
                        continue;
                    }

                    System.Collections.IEnumerable permissions = entry.Value as System.Collections.IEnumerable;
                    if (permissions == null)
                    {
                        continue;
                    }

                    foreach (object value in permissions)
                    {
                        string permissionName = value as string;
                        if (!string.IsNullOrEmpty(permissionName))
                        {
                            owners[permissionName] = plugin.Name;
                        }
                    }
                }
            }
            catch
            {
                // Reflection is best-effort; the permission list still works.
            }

            return owners;
        }

        private void AddOxideCommandEntries(List<PermissionEntry> entries)
        {
            try
            {
                System.Reflection.FieldInfo field = typeof(Plugin).GetField(
                    "commandInfos",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                if (field == null || Interface.Oxide.RootPluginManager == null)
                {
                    return;
                }

                foreach (Plugin plugin in Interface.Oxide.RootPluginManager.GetPlugins())
                {
                    if (plugin == null)
                    {
                        continue;
                    }

                    System.Collections.IDictionary commands = field.GetValue(plugin) as System.Collections.IDictionary;
                    if (commands == null)
                    {
                        continue;
                    }

                    foreach (System.Collections.DictionaryEntry pair in commands)
                    {
                        object commandInfo = pair.Value;
                        if (commandInfo == null)
                        {
                            continue;
                        }

                        System.Reflection.FieldInfo namesField = commandInfo.GetType().GetField(
                            "Names",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        System.Reflection.FieldInfo permsField = commandInfo.GetType().GetField(
                            "PermissionsRequired",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

                        string[] names = namesField != null ? namesField.GetValue(commandInfo) as string[] : null;
                        string[] permissions = permsField != null ? permsField.GetValue(commandInfo) as string[] : null;
                        if (names == null || names.Length == 0)
                        {
                            continue;
                        }

                        string primary = names[0];
                        string permissionName = permissions != null && permissions.Length > 0 ? permissions[0] : string.Empty;
                        bool allRegistered = true;
                        if (permissions != null)
                        {
                            for (int p = 0; p < permissions.Length; p++)
                            {
                                if (!permission.PermissionExists(permissions[p]))
                                {
                                    allRegistered = false;
                                    break;
                                }
                            }
                        }

                        string permissionText = permissions != null && permissions.Length > 0
                            ? string.Join(", ", permissions) + (allRegistered ? string.Empty : " (not all registered)")
                            : "no permission registered";

                        entries.Add(new PermissionEntry
                        {
                            Kind = "oxidecmd",
                            Label = primary,
                            Sub = "NODE: " + (string.IsNullOrEmpty(permissionName) ? "NONE" : permissionName) +
                                  " | Oxide command | plugin: " + plugin.Name +
                                  " | aliases: " + string.Join(", ", names),
                            Permission = permissionName,
                            Command = primary,
                            Source = "Oxide plugin " + plugin.Name,
                            Aliases = string.Join(", ", names),
                            Description = "permission: " + permissionText,
                            SearchText = "oxide command " + string.Join(" ", names) + " " + plugin.Name + " " + (permissions != null ? string.Join(" ", permissions) : string.Empty),
                            Key = "perm|cmd|oxidecmd|" + primary
                        });
                    }
                }
            }
            catch
            {
                // Command reflection is best-effort.
            }
        }

        private void AddConsoleCommandEntries(List<PermissionEntry> entries)
        {
            try
            {
                SdtdConsole console = SingletonMonoBehaviour<SdtdConsole>.Instance;
                if (console == null)
                {
                    return;
                }

                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                IList<IConsoleCommand> commands = console.GetCommands();
                for (int i = 0; i < commands.Count; i++)
                {
                    IConsoleCommand command = commands[i];
                    if (command == null)
                    {
                        continue;
                    }

                    string[] names;
                    string description;
                    try
                    {
                        names = command.GetCommands();
                        description = command.GetDescription();
                    }
                    catch
                    {
                        continue;
                    }

                    if (names == null || names.Length == 0)
                    {
                        continue;
                    }

                    string primary = names[0];
                    if (string.IsNullOrEmpty(primary) || !seen.Add(primary))
                    {
                        continue;
                    }

                    int level = command.DefaultPermissionLevel;
                    try
                    {
                        if (GameManager.Instance != null && GameManager.Instance.adminTools != null)
                        {
                            level = GameManager.Instance.adminTools.Commands.GetCommandPermissionLevel(names);
                            if (level == 0 && command.DefaultPermissionLevel != 0)
                            {
                                level = command.DefaultPermissionLevel;
                            }
                        }
                    }
                    catch
                    {
                        // Keep the default level.
                    }

                    string source = command.GetType().Name + " / " + command.GetType().Assembly.GetName().Name;
                    string shortDescription = string.IsNullOrEmpty(description) ? string.Empty : " | " + description;

                    entries.Add(new PermissionEntry
                    {
                        Kind = "consolecmd",
                        Label = primary,
                        Sub = "NODE: NONE | 7DTD command | admin level " + level +
                              " | aliases: " + string.Join(", ", names) +
                              shortDescription + " | " + source,
                        Command = primary,
                        Source = source,
                        Aliases = string.Join(", ", names),
                        Description = description,
                        AdminLevel = level,
                        SearchText = "7dtd command " + string.Join(" ", names) + " " + description +
                                     " admin level " + level + " " + source,
                        Key = "perm|cmd|consolecmd|" + primary
                    });
                }
            }
            catch
            {
                // Console command lookup is best-effort.
            }
        }

        private void AddConfiguredCommandEntries(List<PermissionEntry> entries)
        {
            try
            {
                if (GameManager.Instance == null || GameManager.Instance.adminTools == null)
                {
                    return;
                }

                HashSet<string> known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < entries.Count; i++)
                {
                    if (!string.IsNullOrEmpty(entries[i].Command))
                    {
                        known.Add(entries[i].Command);
                    }
                }

                Dictionary<string, AdminCommands.CommandPermission> configured =
                    GameManager.Instance.adminTools.Commands.GetCommands();

                foreach (KeyValuePair<string, AdminCommands.CommandPermission> pair in configured)
                {
                    if (string.IsNullOrEmpty(pair.Key) || known.Contains(pair.Key))
                    {
                        continue;
                    }

                    entries.Add(new PermissionEntry
                    {
                        Kind = "configcmd",
                        Label = pair.Key,
                        Sub = "NODE: NONE | serveradmin.xml command permission | admin level " + pair.Value.PermissionLevel,
                        Command = pair.Key,
                        Source = "serveradmin.xml",
                        AdminLevel = pair.Value.PermissionLevel,
                        SearchText = "serveradmin command " + pair.Key + " admin level " + pair.Value.PermissionLevel,
                        Key = "perm|cmd|configcmd|" + pair.Key
                    });
                }
            }
            catch
            {
                // serveradmin.xml lookup is best-effort.
            }
        }

        private static bool MatchesSearch(PermissionEntry entry, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                return true;
            }

            string text = entry.SearchText ?? string.Empty;
            if (text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string[] tokens = filter.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                if (text.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static int EntryScore(PermissionEntry entry, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                return entry.Kind == "permission" ? 0 : 1;
            }

            string label = entry.Label ?? string.Empty;
            if (label.Equals(filter, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (label.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 2;
            }

            return 3;
        }

        private void RenderPermissions(Session session, List<RowInfo> rows, List<ButtonInfo> buttons, bool admin, out string subtitle)
        {
            List<PermissionEntry> entries = BuildPermissionEntries()
                .Where(e => MatchesSearch(e, session.Filter))
                .OrderBy(e => EntryScore(e, session.Filter))
                .ThenBy(e => e.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ClampPage(session, entries.Count);
            int pages = Pages(entries.Count);
            subtitle = "Search permissions/commands | " + entries.Count + " result(s) | selected permission: " +
                       (string.IsNullOrEmpty(session.SelectedPermission) ? "none" : session.SelectedPermission) +
                       " | page " + (session.Page + 1) + "/" + pages;

            int start = session.Page * RowsPerPage;
            for (int i = 0; i < RowsPerPage && start + i < entries.Count; i++)
            {
                PermissionEntry entry = entries[start + i];
                string action = entry.Kind == "permission"
                    ? "perm|select|" + entry.Label
                    : "perm|cmd|" + entry.Kind + "|" + entry.Command;

                bool selected = entry.Kind == "permission"
                    ? string.Equals(session.SelectedPermission, entry.Label, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(session.SelectedEntryKey, entry.Key, StringComparison.OrdinalIgnoreCase);

                AddRow(rows, action, entry.Label, entry.Sub, true, selected, entry.Permission);
            }

            if (entries.Count == 0)
            {
                AddRow(rows, string.Empty, "No permissions or commands match.", string.Empty, false);
            }

            AddPaging(session, pages, buttons);
            if (admin)
            {
                AddButton(buttons, "perm|grantgroup", "Add to Group");
                AddButton(buttons, "perm|grantuser", "Add to User");
                AddButton(buttons, "perm|revokegroup", "Remove from Group");
                AddButton(buttons, "perm|revokeuser", "Remove from User");
            }
            if (!string.IsNullOrEmpty(session.Filter))
            {
                AddButton(buttons, "clearfilter", "Clear Search");
            }
            AddButton(buttons, "nav|home", "Home");
        }

        private void RenderDefaults(Session session, List<RowInfo> rows, List<ButtonInfo> buttons, bool admin, out string subtitle)
        {
            string group = ResolveDefaultGroup();
            List<string> permissions = config.DefaultPermissions
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ClampPage(session, permissions.Count);
            int pages = Pages(permissions.Count);
            subtitle = "Defaults | group: " + (string.IsNullOrEmpty(group) ? "(none)" : group) +
                       " | " + permissions.Count + " permission(s) | load: " + YesNo(config.ApplyDefaultsOnLoad) +
                       " | connect: " + YesNo(config.ApplyDefaultsOnPlayerConnect) +
                       " | once: " + YesNo(config.ApplyDefaultsOncePerPlayer) +
                       " | page " + (session.Page + 1) + "/" + pages;

            int start = session.Page * RowsPerPage;
            for (int i = 0; i < RowsPerPage && start + i < permissions.Count; i++)
            {
                string permissionName = permissions[start + i];
                AddRow(rows, "defaults|remove|" + permissionName, permissionName,
                    admin ? "click to remove" : "default permission", admin, false, permissionName);
            }

            if (permissions.Count == 0)
            {
                AddRow(rows, string.Empty, "No default permissions configured.", string.Empty, false);
            }

            AddPaging(session, pages, buttons);
            if (admin)
            {
                AddButton(buttons, "defaults|add", "Add Permission");
                AddButton(buttons, "defaults|group", "Set Default Group");
                AddButton(buttons, "defaults|apply", "Apply Now");
                AddButton(buttons, "defaults|resetapplied", "Reset Applied");
                AddButton(buttons, "defaults|once", "Toggle Once");
                AddButton(buttons, "defaults|trackgroup", "Toggle Track Group");
                AddButton(buttons, "defaults|reload", "Reload Config");
            }
            AddButton(buttons, "nav|home", "Home");
        }

        #endregion

        #region Actions

        private string HandleAction(string playerId, Session session, string action)
        {
            string[] parts = action.Split('|');
            string command = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
            bool admin = IsAdmin(playerId);
            session.Status = string.Empty;

            switch (command)
            {
                case "open":
                    session = new Session();
                    sessions[playerId] = session;
                    session.Status = "Ready.";
                    break;

                case "nav":
                    Navigate(session, parts.Length > 1 ? parts[1] : "home");
                    break;

                case "refresh":
                    session.Status = "Refreshed.";
                    break;

                case "cancel":
                    session.InputAction = null;
                    session.InputValue = string.Empty;
                    session.Status = "Input cancelled.";
                    break;

                case "clearfilter":
                    session.Filter = string.Empty;
                    session.Page = 0;
                    session.Status = "Filter cleared.";
                    break;

                case "page":
                    if (parts.Length > 1)
                    {
                        session.Page += parts[1].Equals("next", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
                    }
                    break;

                case "group":
                    HandleGroupAction(session, parts, admin);
                    break;

                case "gp":
                    HandleGroupPermissionAction(session, parts, admin);
                    break;

                case "gm":
                    HandleGroupMemberAction(session, parts, admin);
                    break;

                case "user":
                    HandleUserAction(session, parts, admin);
                    break;

                case "up":
                    HandleUserPermissionAction(session, parts, admin);
                    break;

                case "ut":
                    HandleUserTrackAction(session, parts, admin);
                    break;

                case "utemp":
                    HandleUserTempAction(session, parts, admin);
                    break;

                case "track":
                    HandleTrackAction(session, parts, admin);
                    break;

                case "perm":
                    HandlePermissionAction(session, parts, admin);
                    break;

                case "defaults":
                    HandleDefaultsAction(session, parts, admin);
                    break;

                case "back":
                    NavigateBack(session);
                    break;

                default:
                    session.Status = "Unknown action: " + command;
                    break;
            }

            return Render(playerId, session);
        }

        private void Navigate(Session session, string mode)
        {
            session.InputAction = null;
            session.InputValue = string.Empty;
            session.Page = 0;
            session.Filter = string.Empty;
            session.Status = string.Empty;

            switch (mode)
            {
                case "groups":
                    session.Mode = "groups";
                    session.Group = null;
                    break;
                case "users":
                    session.Mode = "users";
                    session.User = null;
                    break;
                case "tracks":
                    session.Mode = "tracks";
                    session.Track = null;
                    break;
                case "permissions":
                    session.Mode = "permissions";
                    session.SelectedPermission = null;
                    break;
                case "defaults":
                    session.Mode = "defaults";
                    break;
                default:
                    session.Mode = "home";
                    break;
            }
        }

        private void NavigateBack(Session session)
        {
            session.InputAction = null;
            session.InputValue = string.Empty;
            session.Filter = string.Empty;
            session.Page = 0;
            session.Status = string.Empty;

            switch (session.Mode)
            {
                case "groupPerms":
                case "groupMembers":
                    session.Mode = "group";
                    break;
                case "group":
                    session.Mode = "groups";
                    session.Group = null;
                    break;
                case "userPerms":
                case "userTracks":
                case "userTemps":
                    session.Mode = "user";
                    break;
                case "user":
                    session.Mode = "users";
                    session.User = null;
                    break;
                case "track":
                    session.Mode = "tracks";
                    session.Track = null;
                    break;
                default:
                    session.Mode = "home";
                    break;
            }
        }

        private void HandleGroupAction(Session session, string[] parts, bool admin)
        {
            string action = parts.Length > 1 ? parts[1].ToLowerInvariant() : string.Empty;

            switch (action)
            {
                case "select":
                    if (parts.Length > 2 && permission.GroupExists(parts[2]))
                    {
                        session.Group = parts[2];
                        session.Mode = "group";
                        session.Page = 0;
                        session.InputAction = null;
                    }
                    else
                    {
                        session.Status = "Group not found.";
                    }
                    break;

                case "perms":
                    if (parts.Length > 2 && permission.GroupExists(parts[2]))
                    {
                        session.Group = parts[2];
                        session.Mode = "groupPerms";
                        session.Page = 0;
                        session.Filter = string.Empty;
                    }
                    break;

                case "members":
                    if (parts.Length > 2 && permission.GroupExists(parts[2]))
                    {
                        session.Group = parts[2];
                        session.Mode = "groupMembers";
                        session.Page = 0;
                    }
                    break;

                case "new":
                    if (RequireAdmin(session, admin))
                    {
                        Prompt(session, "createGroup", "New group name", string.Empty);
                    }
                    break;

                case "prompt":
                    if (parts.Length > 3 && RequireAdmin(session, admin))
                    {
                        PromptGroupField(session, parts[2], parts[3]);
                    }
                    break;

                case "confirmdelete":
                    if (parts.Length > 2 && RequireAdmin(session, admin))
                    {
                        session.Group = parts[2];
                        Prompt(session, "confirmDeleteGroup", "Type the group name to confirm deletion", string.Empty);
                    }
                    break;
            }
        }

        private void PromptGroupField(Session session, string group, string field)
        {
            if (!permission.GroupExists(group))
            {
                session.Status = "Group not found.";
                return;
            }

            session.Group = group;
            field = field.ToLowerInvariant();

            if (field == "title")
            {
                Prompt(session, "groupTitle", "Group title", permission.GetGroupTitle(group));
            }
            else if (field == "rank")
            {
                Prompt(session, "groupRank", "Group rank (higher is stronger)", permission.GetGroupRank(group).ToString());
            }
            else if (field == "parent")
            {
                Prompt(session, "groupParent", "Parent group (empty to clear)", permission.GetGroupParent(group));
            }
            else if (field == "rename")
            {
                Prompt(session, "groupRename", "New group name", group);
            }
        }

        private void HandleGroupPermissionAction(Session session, string[] parts, bool admin)
        {
            string action = parts.Length > 1 ? parts[1].ToLowerInvariant() : string.Empty;
            string group = parts.Length > 2 ? parts[2] : session.Group;
            if (string.IsNullOrEmpty(group) || !permission.GroupExists(group))
            {
                session.Status = "Group not found.";
                return;
            }

            session.Group = group;

            if (action == "toggle")
            {
                if (RequireAdmin(session, admin) && parts.Length > 3)
                {
                    ToggleGroupPermission(group, parts[3]);
                    session.Status = "Toggled '" + parts[3] + "' for group '" + group + "'.";
                }
            }
            else if (action == "addcustom")
            {
                if (RequireAdmin(session, admin))
                {
                    session.Mode = "groupPerms";
                    Prompt(session, "addGroupPermission", "Permission to grant", string.Empty);
                }
            }
        }

        private void HandleGroupMemberAction(Session session, string[] parts, bool admin)
        {
            string action = parts.Length > 1 ? parts[1].ToLowerInvariant() : string.Empty;
            string group = parts.Length > 2 ? parts[2] : session.Group;
            if (string.IsNullOrEmpty(group) || !permission.GroupExists(group))
            {
                session.Status = "Group not found.";
                return;
            }

            session.Group = group;

            if (action == "add" && RequireAdmin(session, admin))
            {
                session.Mode = "groupMembers";
                Prompt(session, "addGroupMember", "Player name or id", string.Empty);
            }
        }

        private void HandleUserAction(Session session, string[] parts, bool admin)
        {
            string action = parts.Length > 1 ? parts[1].ToLowerInvariant() : string.Empty;
            string userId = parts.Length > 2 ? parts[2] : session.User;
            if (string.IsNullOrEmpty(userId))
            {
                session.Status = "No user selected.";
                return;
            }

            session.User = userId;

            if (action == "select")
            {
                session.Mode = "user";
                session.Page = 0;
                session.InputAction = null;
            }
            else if (action == "perms")
            {
                session.Mode = "userPerms";
                session.Page = 0;
                session.Filter = string.Empty;
            }
            else if (action == "tracks")
            {
                session.Mode = "userTracks";
                session.Page = 0;
                session.Track = null;
            }
            else if (action == "groupadd")
            {
                if (RequireAdmin(session, admin))
                {
                    session.Mode = "user";
                    Prompt(session, "addUserGroup", "Group name", string.Empty);
                }
            }
            else if (action == "groupremove" && parts.Length > 3)
            {
                if (RequireAdmin(session, admin))
                {
                    permission.RemoveUserGroup(userId, parts[3]);
                    SavePermissionData();
                    session.Status = "Removed '" + parts[3] + "' from " + PlayerName(userId) + ".";
                }
            }
            else if (action == "permadd")
            {
                if (RequireAdmin(session, admin))
                {
                    session.Mode = "user";
                    Prompt(session, "addUserPermission", "Permission to grant", string.Empty);
                }
            }
            else if (action == "permrevoke" && parts.Length > 3)
            {
                if (RequireAdmin(session, admin))
                {
                    permission.RevokeUserPermission(userId, parts[3]);
                    ForgetTempPermission(userId, parts[3]);
                    SavePermissionData();
                    session.Status = "Revoked '" + parts[3] + "' from " + PlayerName(userId) + ".";
                }
            }
        }

        private void HandleUserPermissionAction(Session session, string[] parts, bool admin)
        {
            string action = parts.Length > 1 ? parts[1].ToLowerInvariant() : string.Empty;
            string userId = parts.Length > 2 ? parts[2] : session.User;
            if (string.IsNullOrEmpty(userId))
            {
                session.Status = "No user selected.";
                return;
            }

            session.User = userId;

            if (action == "toggle")
            {
                if (RequireAdmin(session, admin) && parts.Length > 3)
                {
                    ToggleUserPermission(userId, parts[3]);
                    session.Status = "Toggled '" + parts[3] + "' for " + PlayerName(userId) + ".";
                }
            }
            else if (action == "addcustom")
            {
                if (RequireAdmin(session, admin))
                {
                    session.Mode = "userPerms";
                    Prompt(session, "addUserPermission", "Permission to grant", string.Empty);
                }
            }
        }

        private void HandleUserTempAction(Session session, string[] parts, bool admin)
        {
            string action = parts.Length > 1 ? parts[1].ToLowerInvariant() : string.Empty;
            string userId = parts.Length > 2 ? parts[2] : session.User;
            if (string.IsNullOrEmpty(userId))
            {
                session.Status = "No user selected.";
                return;
            }

            session.User = userId;

            if (action == "list")
            {
                session.Mode = "userTemps";
                session.Page = 0;
                session.InputAction = null;
            }
            else if (action == "add")
            {
                if (RequireAdmin(session, admin))
                {
                    session.Mode = "userTemps";
                    Prompt(session, "tempAddPerm", "Permission node", string.Empty);
                }
            }
            else if (action == "remove" && parts.Length > 3)
            {
                if (RequireAdmin(session, admin))
                {
                    RemoveTempPermission(userId, parts[3], session);
                }
            }
        }

        private void HandleUserTrackAction(Session session, string[] parts, bool admin)
        {
            string action = parts.Length > 1 ? parts[1].ToLowerInvariant() : string.Empty;
            string trackName = parts.Length > 2 ? parts[2] : session.Track;
            string userId = session.User;

            if (action == "select")
            {
                if (!string.IsNullOrEmpty(trackName) && config.Tracks.ContainsKey(trackName))
                {
                    session.Track = trackName;
                    session.Status = "Selected track '" + trackName + "'.";
                }
                else
                {
                    session.Status = "Track not found.";
                }
                return;
            }

            if (!RequireAdmin(session, admin) || string.IsNullOrEmpty(userId))
            {
                return;
            }

            if (string.IsNullOrEmpty(trackName) || !config.Tracks.ContainsKey(trackName))
            {
                session.Status = "Select a track row first.";
                return;
            }

            if (action == "enroll")
            {
                EnrollUserInTrack(trackName, userId, session);
            }
            else if (action == "promote")
            {
                MoveUserInTrack(trackName, userId, true, session);
            }
            else if (action == "demote")
            {
                MoveUserInTrack(trackName, userId, false, session);
            }
        }

        private void HandleTrackAction(Session session, string[] parts, bool admin)
        {
            string action = parts.Length > 1 ? parts[1].ToLowerInvariant() : string.Empty;
            string trackName = parts.Length > 2 ? parts[2] : session.Track;

            if (action == "select")
            {
                if (!string.IsNullOrEmpty(trackName) && config.Tracks.ContainsKey(trackName))
                {
                    session.Track = trackName;
                    session.Mode = "track";
                    session.Page = 0;
                    session.InputAction = null;
                }
                else
                {
                    session.Status = "Track not found.";
                }
                return;
            }

            if (action == "new")
            {
                if (RequireAdmin(session, admin))
                {
                    Prompt(session, "createTrack", "New track name", string.Empty);
                }
                return;
            }

            if (string.IsNullOrEmpty(trackName) || !config.Tracks.ContainsKey(trackName))
            {
                session.Status = "Track not found.";
                return;
            }

            session.Track = trackName;

            if (!RequireAdmin(session, admin))
            {
                return;
            }

            if (action == "addgroup")
            {
                Prompt(session, "addTrackGroup", "Group to append", string.Empty);
            }
            else if (action == "removegroup" && parts.Length > 3)
            {
                RemoveTrackGroup(trackName, parts[3], session);
            }
            else if (action == "enroll")
            {
                Prompt(session, "enrollTrackUser", "Player name or id to enroll", string.Empty);
            }
            else if (action == "promote")
            {
                Prompt(session, "promoteTrackUser", "Player name or id to promote", string.Empty);
            }
            else if (action == "demote")
            {
                Prompt(session, "demoteTrackUser", "Player name or id to demote", string.Empty);
            }
            else if (action == "confirmdelete")
            {
                Prompt(session, "confirmDeleteTrack", "Type the track name to confirm deletion", string.Empty);
            }
        }

        private void HandlePermissionAction(Session session, string[] parts, bool admin)
        {
            string action = parts.Length > 1 ? parts[1].ToLowerInvariant() : string.Empty;

            if (action == "select")
            {
                if (parts.Length > 2)
                {
                    session.SelectedPermission = parts[2];
                    session.SelectedEntryKey = "perm|select|" + parts[2];
                    session.Status = "Selected '" + parts[2] + "'.";
                }
                return;
            }

            if (action == "cmd" && parts.Length > 3)
            {
                string kind = parts[2];
                string commandName = parts[3];
                PermissionEntry entry = BuildPermissionEntries().FirstOrDefault(
                    e => e.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase) &&
                         e.Command.Equals(commandName, StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                {
                    session.Status = "Command not found in the permission index.";
                    return;
                }

                session.SelectedEntryKey = entry.Key;
                if (!string.IsNullOrEmpty(entry.Permission))
                {
                    session.SelectedPermission = entry.Permission;
                }
                else
                {
                    session.SelectedPermission = null;
                }

                string details = entry.Sub;
                if (entry.AdminLevel >= 0)
                {
                    details += " | admin level " + entry.AdminLevel;
                }

                if (string.IsNullOrEmpty(entry.Permission))
                {
                    details += " | This is an admin-level/command entry, not an Oxide permission node.";
                }
                else
                {
                    details += " | Selected permission: " + entry.Permission;
                }

                session.Status = details;
                return;
            }

            if (!RequireAdmin(session, admin))
            {
                return;
            }

            if (string.IsNullOrEmpty(session.SelectedPermission))
            {
                session.Status = "Select a permission row first.";
                return;
            }

            if (action == "grantgroup")
            {
                Prompt(session, "permGrantGroup", "Group to grant '" + session.SelectedPermission + "'", string.Empty);
            }
            else if (action == "grantuser")
            {
                Prompt(session, "permGrantUser", "Player to grant '" + session.SelectedPermission + "'", string.Empty);
            }
            else if (action == "revokegroup")
            {
                Prompt(session, "permRevokeGroup", "Group to revoke '" + session.SelectedPermission + "'", string.Empty);
            }
            else if (action == "revokeuser")
            {
                Prompt(session, "permRevokeUser", "Player to revoke '" + session.SelectedPermission + "'", string.Empty);
            }
        }

        private void HandleDefaultsAction(Session session, string[] parts, bool admin)
        {
            string action = parts.Length > 1 ? parts[1].ToLowerInvariant() : string.Empty;

            if (action == "apply")
            {
                if (RequireAdmin(session, admin))
                {
                    ApplyConfiguredDefaults(true);
                    session.Status = "Defaults applied.";
                }
            }
            else if (action == "reload")
            {
                if (RequireAdmin(session, admin))
                {
                    LoadConfig();
                    ApplyConfiguredDefaults(false);
                    session.Status = configReadFailed
                        ? "Config could not be read; file left untouched. Check the server log."
                        : "Config reloaded and defaults applied.";
                }
            }
            else if (action == "add")
            {
                if (RequireAdmin(session, admin))
                {
                    Prompt(session, "addDefaultPermission", "Permission to add to defaults", string.Empty);
                }
            }
            else if (action == "group")
            {
                if (RequireAdmin(session, admin))
                {
                    Prompt(session, "setDefaultGroup", "Default group name", ResolveDefaultGroup());
                }
            }
            else if (action == "resetapplied")
            {
                if (RequireAdmin(session, admin))
                {
                    int cleared = ClearDefaultsApplied("all");
                    session.Status = "Cleared " + cleared + " applied marker(s). Defaults will apply again on the next connect or Apply Now.";
                }
            }
            else if (action == "once")
            {
                if (RequireAdmin(session, admin))
                {
                    config.ApplyDefaultsOncePerPlayer = !config.ApplyDefaultsOncePerPlayer;
                    SaveConfig();
                    session.Status = "Apply defaults once per player: " + YesNo(config.ApplyDefaultsOncePerPlayer) + ".";
                }
            }
            else if (action == "trackgroup")
            {
                if (RequireAdmin(session, admin))
                {
                    config.AddDefaultGroupForTrackUsers = !config.AddDefaultGroupForTrackUsers;
                    SaveConfig();
                    session.Status = "Track users keep the default group: " + YesNo(config.AddDefaultGroupForTrackUsers) + ".";
                }
            }
            else if (action == "remove" && parts.Length > 2)
            {
                if (RequireAdmin(session, admin))
                {
                    RemoveDefaultPermission(parts[2], session);
                }
            }
        }

        private bool RequireAdmin(Session session, bool admin)
        {
            if (!admin)
            {
                session.Status = "You need the 'permissiongui.admin' permission and the admin wildcard.";
                return false;
            }

            return true;
        }

        private bool CanEditConfig(Session session)
        {
            if (configReadFailed)
            {
                session.Status = "PermissionGui.json could not be read. Fix it and run /perm reload before editing tracks or defaults.";
                return false;
            }

            return true;
        }

        private void Prompt(Session session, string action, string label, string value)
        {
            session.InputAction = action;
            session.InputLabel = label;
            session.InputValue = value ?? string.Empty;
        }

        #endregion

        #region Text/prompt handling

        private string HandleText(string playerId, Session session, string value)
        {
            string action = session.InputAction;
            session.InputAction = null;
            session.InputValue = string.Empty;

            if (string.IsNullOrEmpty(action))
            {
                if (IsSearchable(session.Mode))
                {
                    session.Filter = value.Trim();
                    session.Page = 0;
                    session.Status = string.IsNullOrEmpty(session.Filter) ? "Search cleared." : "Search set.";
                }
                else
                {
                    session.Status = "Nothing is waiting for text input.";
                }

                return Render(playerId, session);
            }

            if (!RequireAdmin(session, IsAdmin(playerId)))
            {
                return Render(playerId, session);
            }

            switch (action)
            {
                case "createGroup":
                    CreateGroup(value.Trim(), session);
                    break;
                case "groupRename":
                    RenameGroup(session.Group, value.Trim(), session);
                    break;
                case "confirmDeleteGroup":
                    if (string.Equals(value.Trim(), session.Group, StringComparison.OrdinalIgnoreCase))
                    {
                        DeleteGroup(session.Group, session);
                    }
                    else
                    {
                        session.Status = "Deletion cancelled.";
                    }
                    break;
                case "groupTitle":
                    if (permission.SetGroupTitle(session.Group, value))
                    {
                        SavePermissionData();
                        session.Status = "Group title updated.";
                    }
                    else
                    {
                        session.Status = "Group not found.";
                    }
                    break;
                case "groupRank":
                    int rank;
                    if (!int.TryParse(value.Trim(), out rank))
                    {
                        session.Status = "Rank must be a whole number.";
                    }
                    else if (permission.SetGroupRank(session.Group, rank))
                    {
                        SavePermissionData();
                        session.Status = "Group rank set to " + rank + ".";
                    }
                    else
                    {
                        session.Status = "Group not found.";
                    }
                    break;
                case "groupParent":
                    SetGroupParent(session, value.Trim());
                    break;
                case "addGroupMember":
                    AddGroupMember(session.Group, value.Trim(), session);
                    break;
                case "addGroupPermission":
                    AddCustomPermissionToGroup(session, value.Trim());
                    break;
                case "addUserGroup":
                    AddUserGroup(session, value.Trim());
                    break;
                case "addUserPermission":
                    AddCustomPermissionToUser(session, value.Trim());
                    break;
                case "tempAddPerm":
                    session.PendingTempPermission = value.Trim();
                    if (string.IsNullOrEmpty(session.PendingTempPermission))
                    {
                        session.Status = "Permission cannot be empty.";
                        break;
                    }

                    if (!permission.PermissionExists(session.PendingTempPermission))
                    {
                        session.Status = "That permission is not registered by any loaded plugin.";
                        session.PendingTempPermission = string.Empty;
                        break;
                    }

                    if (UserHasDirect(session.User, session.PendingTempPermission) &&
                        GetTempPermissionEntry(session.User, session.PendingTempPermission) == null)
                    {
                        session.Status = PlayerName(session.User) + " already has that permission directly.";
                        session.PendingTempPermission = string.Empty;
                        break;
                    }

                    Prompt(session, "tempAddDuration", "Duration (30m, 2h, 7d, 1d12h)", "1h");
                    break;
                case "tempAddDuration":
                    GrantTempPermission(session.User, session.PendingTempPermission, value.Trim(), session);
                    session.PendingTempPermission = string.Empty;
                    session.Mode = "userTemps";
                    session.Page = 0;
                    break;
                case "createTrack":
                    CreateTrack(value.Trim(), session);
                    break;
                case "addTrackGroup":
                    AddTrackGroup(session.Track, value.Trim(), session);
                    break;
                case "confirmDeleteTrack":
                    if (string.Equals(value.Trim(), session.Track, StringComparison.OrdinalIgnoreCase))
                    {
                        DeleteTrack(session.Track, session);
                    }
                    else
                    {
                        session.Status = "Deletion cancelled.";
                    }
                    break;
                case "enrollTrackUser":
                    EnrollTrackUserPrompt(session, value.Trim());
                    break;
                case "promoteTrackUser":
                    MoveTrackUserPrompt(session, value.Trim(), true);
                    break;
                case "demoteTrackUser":
                    MoveTrackUserPrompt(session, value.Trim(), false);
                    break;
                case "permGrantGroup":
                    GrantPermissionToGroup(session, value.Trim());
                    break;
                case "permGrantUser":
                    GrantPermissionToUser(session, value.Trim());
                    break;
                case "permRevokeGroup":
                    RevokePermissionFromGroup(session, value.Trim());
                    break;
                case "permRevokeUser":
                    RevokePermissionFromUser(session, value.Trim());
                    break;
                case "addDefaultPermission":
                    AddDefaultPermission(value.Trim(), session);
                    break;
                case "setDefaultGroup":
                    SetDefaultGroup(value.Trim(), session);
                    break;
                default:
                    session.Status = "Unknown input action.";
                    break;
            }

            return Render(playerId, session);
        }

        private void SetGroupParent(Session session, string parent)
        {
            if (string.IsNullOrEmpty(parent))
            {
                permission.SetGroupParent(session.Group, string.Empty);
                SavePermissionData();
                session.Status = "Parent group cleared.";
                return;
            }

            if (!permission.GroupExists(parent))
            {
                session.Status = "Parent group not found.";
                return;
            }

            if (!permission.SetGroupParent(session.Group, parent))
            {
                session.Status = "Parent was not changed (circular inheritance?).";
                return;
            }

            SavePermissionData();
            session.Status = "Parent group set to '" + parent + "'.";
        }

        private void AddCustomPermissionToGroup(Session session, string permissionName)
        {
            if (string.IsNullOrEmpty(permissionName))
            {
                session.Status = "Permission cannot be empty.";
                return;
            }

            if (!permission.PermissionExists(permissionName))
            {
                session.Status = "That permission is not registered by any loaded plugin.";
                return;
            }

            permission.GrantGroupPermission(session.Group, permissionName, null);
            SavePermissionData();
            session.Status = "Granted '" + permissionName + "' to group '" + session.Group + "'.";
        }

        private void AddCustomPermissionToUser(Session session, string permissionName)
        {
            if (string.IsNullOrEmpty(permissionName))
            {
                session.Status = "Permission cannot be empty.";
                return;
            }

            if (!permission.PermissionExists(permissionName))
            {
                session.Status = "That permission is not registered by any loaded plugin.";
                return;
            }

            permission.GrantUserPermission(session.User, permissionName, null);
            ForgetTempPermission(session.User, permissionName);
            SavePermissionData();
            session.Status = "Granted '" + permissionName + "' to " + PlayerName(session.User) + ".";
        }

        private void AddUserGroup(Session session, string group)
        {
            if (!permission.GroupExists(group))
            {
                session.Status = "Group not found.";
                return;
            }

            permission.AddUserGroup(session.User, group);
            SavePermissionData();
            session.Status = "Added group '" + group + "' to " + PlayerName(session.User) + ".";
        }

        private void EnrollTrackUserPrompt(Session session, string input)
        {
            string userId = ResolveUserId(input);
            if (userId == null)
            {
                session.Status = "Player not found.";
                return;
            }

            EnrollUserInTrack(session.Track, userId, session);
        }

        private void MoveTrackUserPrompt(Session session, string input, bool promote)
        {
            string userId = ResolveUserId(input);
            if (userId == null)
            {
                session.Status = "Player not found.";
                return;
            }

            MoveUserInTrack(session.Track, userId, promote, session);
        }

        private void GrantPermissionToGroup(Session session, string group)
        {
            if (!permission.GroupExists(group))
            {
                session.Status = "Group not found.";
                return;
            }

            permission.GrantGroupPermission(group, session.SelectedPermission, null);
            SavePermissionData();
            session.Status = "Granted '" + session.SelectedPermission + "' to group '" + group + "'.";
        }

        private void GrantPermissionToUser(Session session, string input)
        {
            string userId = ResolveUserId(input);
            if (userId == null)
            {
                session.Status = "Player not found.";
                return;
            }

            permission.GrantUserPermission(userId, session.SelectedPermission, null);
            SavePermissionData();
            session.Status = "Granted '" + session.SelectedPermission + "' to " + PlayerName(userId) + ".";
        }

        private void RevokePermissionFromGroup(Session session, string group)
        {
            if (!permission.GroupExists(group))
            {
                session.Status = "Group not found.";
                return;
            }

            permission.RevokeGroupPermission(group, session.SelectedPermission);
            SavePermissionData();
            session.Status = "Revoked '" + session.SelectedPermission + "' from group '" + group + "'.";
        }

        private void RevokePermissionFromUser(Session session, string input)
        {
            string userId = ResolveUserId(input);
            if (userId == null)
            {
                session.Status = "Player not found.";
                return;
            }

            permission.RevokeUserPermission(userId, session.SelectedPermission);
            SavePermissionData();
            session.Status = "Revoked '" + session.SelectedPermission + "' from " + PlayerName(userId) + ".";
        }

        private void AddDefaultPermission(string permissionName, Session session)
        {
            if (configReadFailed)
            {
                session.Status = "PermissionGui.json could not be read. Fix it and reload before editing defaults.";
                return;
            }

            if (string.IsNullOrEmpty(permissionName))
            {
                session.Status = "Permission cannot be empty.";
                return;
            }

            if (!permission.PermissionExists(permissionName))
            {
                session.Status = "That permission is not registered by any loaded plugin.";
                return;
            }

            if (config.DefaultPermissions.Any(p => p.Equals(permissionName, StringComparison.OrdinalIgnoreCase)))
            {
                session.Status = "That permission is already in the default list.";
                return;
            }

            config.DefaultPermissions.Add(permissionName);
            config.DefaultPermissions = NormalizeList(config.DefaultPermissions);
            SaveConfig();
            ApplyConfiguredDefaults(false);
            session.Status = "Added '" + permissionName + "' to defaults.";

            if (config.RequireWildcardForAdmin && permissionName.Equals(PermAdmin, StringComparison.OrdinalIgnoreCase))
            {
                session.Status += " Note: permissiongui.admin alone does not grant admin access; " + AdminWildcard + " is also required.";
            }
        }

        private void RemoveDefaultPermission(string permissionName, Session session)
        {
            if (configReadFailed)
            {
                session.Status = "PermissionGui.json could not be read. Fix it and reload before editing defaults.";
                return;
            }

            int index = config.DefaultPermissions.FindIndex(p => p.Equals(permissionName, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                session.Status = "That permission is not in the default list.";
                return;
            }

            config.DefaultPermissions.RemoveAt(index);
            SaveConfig();

            string group = EnsureDefaultGroup();
            if (!string.IsNullOrEmpty(group))
            {
                permission.RevokeGroupPermission(group, permissionName);
            }

            if (config.GrantDefaultsDirectlyToPlayer)
            {
                HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (IPlayer player in players.All)
                {
                    if (player != null && !string.IsNullOrEmpty(player.Id))
                    {
                        ids.Add(player.Id);
                    }
                }

                foreach (string entry in permission.GetPermissionUsers(permissionName))
                {
                    string id = ExtractId(entry);
                    if (!string.IsNullOrEmpty(id))
                    {
                        ids.Add(id);
                    }
                }

                foreach (string id in ids)
                {
                    permission.RevokeUserPermission(id, permissionName);
                }
            }

            SavePermissionData();
            session.Status = "Removed '" + permissionName + "' from defaults and revoked it.";
        }

        private void SetDefaultGroup(string group, Session session)
        {
            if (configReadFailed)
            {
                session.Status = "PermissionGui.json could not be read. Fix it and reload before editing defaults.";
                return;
            }

            if (!string.IsNullOrEmpty(group) && !ValidName(group))
            {
                session.Status = "Use only letters, numbers, '_', '-' or '.'.";
                return;
            }

            config.DefaultGroup = group;
            SaveConfig();
            ApplyConfiguredDefaults(true);
            session.Status = string.IsNullOrEmpty(group)
                ? "Default group now follows Oxide's default player group."
                : "Default group set to '" + group + "'.";
        }

        #endregion

        #region Commands

        private void CommandPermGui(IPlayer player, string command, string[] args)
        {
            if (player == null || !CanUse(player.Id))
            {
                Reply(player, "You do not have permission to use the Permission Manager.");
                return;
            }

            if (TryOpenServerAssistHook(player, null))
            {
                return;
            }

            Reply(player, "Install the ServerAssist client mod and Oxide plugin to open the GUI, or use /perm defaults.");
        }

        private void CommandPerm(IPlayer player, string command, string[] args)
        {
            if (args != null && args.Length > 0 && args[0].Equals("whoami", StringComparison.OrdinalIgnoreCase))
            {
                ReplyWhoAmI(player);
                return;
            }

            if (player == null || !CanUse(player.Id))
            {
                Reply(player, "You do not have permission to use the Permission Manager.");
                return;
            }

            if (args == null || args.Length == 0)
            {
                Reply(player, "Usage: /perm defaults [reset <player|all>] | adddefault <perm> | removedefault <perm> | setdefaultgroup <group>");
                Reply(player, "       /perm temp <player> <permission> <duration> | temp remove <player> <permission> | temp list [player]");
                Reply(player, "       /perm search <text> | node <text> | apply | data | save | reload | cleanup | whoami | gui");
                return;
            }

            string sub = args[0].ToLowerInvariant();
            switch (sub)
            {
                case "gui":
                    CommandPermGui(player, command, args);
                    return;
                case "help":
                    CommandPerm(player, command, new string[0]);
                    return;
                case "whoami":
                    ReplyWhoAmI(player);
                    return;
                case "defaults":
                    if (args.Length > 1 && args[1].Equals("reset", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!RequireAdminCommand(player))
                        {
                            return;
                        }

                        string targetArg = args.Length > 2 ? args[2] : "all";
                        string target = targetArg.Equals("all", StringComparison.OrdinalIgnoreCase)
                            ? "all"
                            : ResolveUserId(targetArg);
                        if (string.IsNullOrEmpty(target))
                        {
                            Reply(player, "Player not found: " + targetArg);
                            return;
                        }

                        int cleared = ClearDefaultsApplied(target);
                        Reply(player, "Cleared " + cleared + " applied marker(s). Defaults will apply again on the next connect or /perm apply.");
                        return;
                    }

                    ReplyDefaults(player);
                    return;
                case "temp":
                    if (!RequireAdminCommand(player))
                    {
                        return;
                    }

                    CommandTemp(player, args);
                    return;
                case "search":
                    if (args.Length < 2)
                    {
                        Reply(player, "Usage: /perm search <text>");
                        return;
                    }

                    ReplySearch(player, string.Join(" ", args.Skip(1)));
                    return;
                case "node":
                    if (args.Length < 2)
                    {
                        Reply(player, "Usage: /perm node <text>");
                        return;
                    }

                    ReplyNode(player, string.Join(" ", args.Skip(1)));
                    return;
                case "adddefault":
                    if (!RequireAdminCommand(player))
                    {
                        return;
                    }
                    if (args.Length < 2)
                    {
                        Reply(player, "Usage: /perm adddefault <permission>");
                        return;
                    }
                    AddDefaultPermissionCommand(player, args[1]);
                    return;
                case "removedefault":
                    if (!RequireAdminCommand(player))
                    {
                        return;
                    }
                    if (args.Length < 2)
                    {
                        Reply(player, "Usage: /perm removedefault <permission>");
                        return;
                    }
                    RemoveDefaultPermissionCommand(player, args[1]);
                    return;
                case "setdefaultgroup":
                    if (!RequireAdminCommand(player))
                    {
                        return;
                    }
                    if (args.Length < 2)
                    {
                        Reply(player, "Usage: /perm setdefaultgroup <group>");
                        return;
                    }
                    SetDefaultGroupCommand(player, args[1]);
                    return;
                case "apply":
                    if (!RequireAdminCommand(player))
                    {
                        return;
                    }
                    ApplyConfiguredDefaults(true);
                    Reply(player, "Default permissions applied.");
                    return;
                case "data":
                    if (!RequireAdminCommand(player))
                    {
                        return;
                    }
                    ReplyData(player);
                    return;
                case "save":
                    if (!RequireAdminCommand(player))
                    {
                        return;
                    }
                    Reply(player, SavePermissionData() ? "Oxide permission data saved." : "Save failed: " + lastError);
                    return;
                case "reload":
                    if (!RequireAdminCommand(player))
                    {
                        return;
                    }
                    LoadConfig();
                    ApplyConfiguredDefaults(false);
                    Reply(player, configReadFailed
                        ? "PermissionGui config could not be read; the file was left untouched."
                        : "PermissionGui config reloaded and defaults applied.");
                    return;
                case "cleanup":
                    if (!RequireAdminCommand(player))
                    {
                        return;
                    }
                    CleanupLegacyData(player);
                    return;
                default:
                    Reply(player, "Unknown subcommand. Use /perm help.");
                    return;
            }
        }

        private bool RequireAdminCommand(IPlayer player)
        {
            if (player != null && IsAdmin(player.Id))
            {
                return true;
            }

            Reply(player, "You need permissiongui.admin and " + AdminWildcard + ".");
            return false;
        }

        private void AddDefaultPermissionCommand(IPlayer player, string permissionName)
        {
            Session session = new Session();
            AddDefaultPermission(permissionName.Trim(), session);
            Reply(player, session.Status);
        }

        private void RemoveDefaultPermissionCommand(IPlayer player, string permissionName)
        {
            Session session = new Session();
            RemoveDefaultPermission(permissionName.Trim(), session);
            Reply(player, session.Status);
        }

        private void SetDefaultGroupCommand(IPlayer player, string group)
        {
            Session session = new Session();
            SetDefaultGroup(group.Trim(), session);
            Reply(player, session.Status);
        }

        private void CommandTemp(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /perm temp <player> <permission> <duration>");
                Reply(player, "       /perm temp remove <player> <permission> | temp list [player]");
                return;
            }

            string mode = args[1].ToLowerInvariant();
            if (mode == "list")
            {
                string target = null;
                if (args.Length > 2)
                {
                    target = ResolveUserId(args[2]);
                    if (string.IsNullOrEmpty(target))
                    {
                        Reply(player, "Player not found: " + args[2]);
                        return;
                    }
                }

                ReplyTempPermissions(player, target);
                return;
            }

            if (mode == "remove")
            {
                if (args.Length < 4)
                {
                    Reply(player, "Usage: /perm temp remove <player> <permission>");
                    return;
                }

                string userId = ResolveUserId(args[2]);
                if (string.IsNullOrEmpty(userId))
                {
                    Reply(player, "Player not found: " + args[2]);
                    return;
                }

                Session session = new Session();
                RemoveTempPermission(userId, args[3], session);
                Reply(player, session.Status);
                return;
            }

            if (args.Length < 4)
            {
                Reply(player, "Usage: /perm temp <player> <permission> <duration>");
                return;
            }

            string targetId = ResolveUserId(args[1]);
            if (string.IsNullOrEmpty(targetId))
            {
                Reply(player, "Player not found: " + args[1]);
                return;
            }

            Session grantSession = new Session();
            GrantTempPermission(targetId, args[2], string.Join(" ", args.Skip(3)), grantSession);
            Reply(player, grantSession.Status);
        }

        private void ReplyTempPermissions(IPlayer player, string userId)
        {
            ExpireTempPermissions();

            List<TempPermissionEntry> entries = string.IsNullOrEmpty(userId)
                ? stateData.TempPermissions
                    .Where(entry => entry != null && entry.ExpiresUtcTicks > DateTime.UtcNow.Ticks)
                    .OrderBy(entry => entry.ExpiresUtcTicks)
                    .ToList()
                : TempPermissionsForUser(userId);

            if (entries.Count == 0)
            {
                Reply(player, string.IsNullOrEmpty(userId)
                    ? "No active temporary permissions."
                    : "No active temporary permissions for " + PlayerName(userId) + ".");
                return;
            }

            Reply(player, "Temporary permissions (" + entries.Count + "):");
            for (int i = 0; i < entries.Count; i++)
            {
                TempPermissionEntry entry = entries[i];
                DateTime expires = new DateTime(entry.ExpiresUtcTicks, DateTimeKind.Utc);
                Reply(player, "  " + PlayerName(entry.UserId) + " (" + entry.UserId + ") | " +
                             entry.Permission + " | expires in " + FormatDuration(expires - DateTime.UtcNow));
            }
        }

        private void CleanupLegacyData(IPlayer player)
        {
            if (!Interface.Oxide.DataFileSystem.ExistsDatafile(LegacyDataFileName))
            {
                Reply(player, "Legacy data.json not found. Nothing to clean up.");
                return;
            }

            try
            {
                Interface.Oxide.DataFileSystem.DeleteDataFile(LegacyDataFileName);
                Reply(player, "Deleted oxide/data/PermissionGui/data.json. It is no longer used.");
            }
            catch (Exception exception)
            {
                Reply(player, "Could not delete the legacy data file: " + exception.Message);
            }
        }

        private void ReplyDefaults(IPlayer player)
        {
            string group = ResolveDefaultGroup();
            Reply(player, "Default group: " + (string.IsNullOrEmpty(group) ? "(none)" : group));
            Reply(player, "Default permissions (" + config.DefaultPermissions.Count + "): " +
                         (config.DefaultPermissions.Count == 0 ? "(none)" : string.Join(", ", config.DefaultPermissions)));
            Reply(player, "Apply on load: " + YesNo(config.ApplyDefaultsOnLoad) +
                         " | connect: " + YesNo(config.ApplyDefaultsOnPlayerConnect) +
                         " | once per player: " + YesNo(config.ApplyDefaultsOncePerPlayer) +
                         " | direct mirror: " + YesNo(config.GrantDefaultsDirectlyToPlayer));
            Reply(player, "Track users get default group: " + YesNo(config.AddDefaultGroupForTrackUsers));
        }

        private void ReplySearch(IPlayer player, string filter)
        {
            List<PermissionEntry> matches = BuildPermissionEntries()
                .Where(e => MatchesSearch(e, filter))
                .OrderBy(e => EntryScore(e, filter))
                .ThenBy(e => e.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (matches.Count == 0)
            {
                Reply(player, "No permission or command matches '" + filter + "'.");
                return;
            }

            List<PermissionEntry> nodes = matches.Where(e => e.Kind == "permission").Take(6).ToList();
            List<PermissionEntry> commands = matches.Where(e => e.Kind != "permission").Take(6).ToList();

            Reply(player, "Search '" + filter + "' | " + matches.Count + " match(es)");

            if (nodes.Count > 0)
            {
                Reply(player, "Grantable permission nodes:");
                for (int i = 0; i < nodes.Count; i++)
                {
                    Reply(player, "  NODE: " + nodes[i].Label);
                    Reply(player, "        " + nodes[i].Sub);
                }
            }
            else
            {
                Reply(player, "No grantable Oxide permission node matched.");
            }

            if (commands.Count > 0)
            {
                Reply(player, "Commands:");
                for (int i = 0; i < commands.Count; i++)
                {
                    PermissionEntry entry = commands[i];
                    Reply(player, "  CMD: " + entry.Label + " | NODE: " +
                                 (string.IsNullOrEmpty(entry.Permission) ? "NONE" : entry.Permission));

                    string detail = string.Empty;
                    if (!string.IsNullOrEmpty(entry.Aliases))
                    {
                        detail = "aliases: " + entry.Aliases;
                    }
                    if (!string.IsNullOrEmpty(entry.Description))
                    {
                        detail += (detail.Length > 0 ? " | " : string.Empty) + entry.Description;
                    }
                    if (!string.IsNullOrEmpty(entry.Source))
                    {
                        detail += (detail.Length > 0 ? " | " : string.Empty) + entry.Source;
                    }
                    if (detail.Length > 0)
                    {
                        Reply(player, "       " + detail);
                    }
                }
            }

            int shown = nodes.Count + commands.Count;
            if (matches.Count > shown)
            {
                Reply(player, "  ... " + (matches.Count - shown) + " more. Refine the search or use the GUI.");
            }
        }

        private void ReplyNode(IPlayer player, string query)
        {
            List<PermissionEntry> entries = BuildPermissionEntries();

            PermissionEntry exact = entries.FirstOrDefault(
                e => e.Kind == "permission" && e.Label.Equals(query, StringComparison.OrdinalIgnoreCase));

            if (exact != null)
            {
                Reply(player, "NODE: " + exact.Label);
                Reply(player, "  " + exact.Sub);
                Reply(player, "  Grant with: /perm adddefault " + exact.Label + " or the GUI Add to Group/User.");
                return;
            }

            List<PermissionEntry> matches = entries
                .Where(e => MatchesSearch(e, query))
                .OrderBy(e => EntryScore(e, query))
                .ThenBy(e => e.Label, StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();

            if (matches.Count == 0)
            {
                Reply(player, "No permission node or command matches '" + query + "'.");
                return;
            }

            Reply(player, "No exact permission node match for '" + query + "'. Closest results:");
            for (int i = 0; i < matches.Count; i++)
            {
                PermissionEntry entry = matches[i];
                if (entry.Kind == "permission")
                {
                    Reply(player, "  NODE: " + entry.Label + " | " + entry.Sub);
                }
                else
                {
                    Reply(player, "  CMD: " + entry.Label + " | NODE: " +
                                 (string.IsNullOrEmpty(entry.Permission) ? "NONE" : entry.Permission) +
                                 " | " + entry.Sub);
                }
            }

            if (!matches.Any(e => e.Kind == "permission"))
            {
                Reply(player, "No grantable Oxide permission node matched. These are commands; 7DTD commands use admin levels, not permission nodes.");
            }
        }

        private void ReplyData(IPlayer player)
        {
            string group = ResolveDefaultGroup();
            Reply(player, "Default group: " + (string.IsNullOrEmpty(group) ? "(none)" : group));
            Reply(player, "Default permissions: " + config.DefaultPermissions.Count);
            Reply(player, "Tracks: " + config.Tracks.Count);
            Reply(player, "Admin wildcard: " + AdminWildcard +
                         " | require: " + YesNo(config.RequireWildcardForAdmin) +
                         " | grant all: " + YesNo(config.GrantAllPermissionsToAdmin));
            Reply(player, "Last apply (UTC): " + (lastAppliedUtc == DateTime.MinValue ? "never" : lastAppliedUtc.ToString("u")));
            Reply(player, "Last save (UTC): " + (lastSavedUtc == DateTime.MinValue ? "never" : lastSavedUtc.ToString("u")));

            if (!string.IsNullOrEmpty(lastError))
            {
                Reply(player, "Last error: " + lastError);
            }

            Reply(player, "Apply on load: " + YesNo(config.ApplyDefaultsOnLoad) +
                         " | connect: " + YesNo(config.ApplyDefaultsOnPlayerConnect) +
                         " | once per player: " + YesNo(config.ApplyDefaultsOncePerPlayer));
            Reply(player, "Track users keep default group: " + YesNo(config.AddDefaultGroupForTrackUsers) +
                         " | applied markers: " + stateData.DefaultsApplied.Count +
                         " | temp permissions: " + stateData.TempPermissions.Count);
            Reply(player, "Oxide groups=" + permission.GetGroups().Length + " | known players=" + players.All.Count());
            Reply(player, "Player groups/direct permissions: oxide/data/oxide.users.data");
            Reply(player, "Group permissions/parents: oxide/data/oxide.groups.data");
            Reply(player, "PermissionGui state: oxide/data/PermissionGui/state.json");
            Reply(player, "data.json is not used. /perm cleanup deletes the legacy file.");
        }

        private void ReplyWhoAmI(IPlayer player)
        {
            string id = player != null && !string.IsNullOrEmpty(player.Id) ? player.Id : "server_console";
            Reply(player, "Your id: " + id);

            if (player == null)
            {
                return;
            }

            Reply(player, "Admin: " + IsAdmin(id) + " | Use: " + CanUse(id));
            Reply(player, "Admin wildcard (" + AdminWildcard + "): " + permission.UserHasPermission(id, AdminWildcard));

            UserData userData = permission.GetUserData(id);
            string[] direct = userData != null && userData.Perms != null ? userData.Perms.ToArray() : new string[0];
            Reply(player, "Direct: " + (direct.Length <= 15 ? string.Join(", ", direct) : direct.Length + " permissions (list hidden)"));
            Reply(player, "Groups: " + string.Join(", ", permission.GetUserGroups(id)));
        }

        private void Reply(IPlayer player, string message)
        {
            if (player == null)
            {
                Interface.Oxide.LogInfo("[PermissionGui] " + message);
                return;
            }

            player.Reply(string.IsNullOrEmpty(config.ChatPrefix) ? message : config.ChatPrefix + " " + message);
        }

        #endregion

        #region Config data

        private sealed class StateData
        {
            public bool DefaultsInitialized;
            public List<string> DefaultsApplied = new List<string>();
            public List<TempPermissionEntry> TempPermissions = new List<TempPermissionEntry>();
        }

        private sealed class TempPermissionEntry
        {
            public string UserId = string.Empty;
            public string Permission = string.Empty;
            public long ExpiresUtcTicks;
            public bool WasDirect;
        }

        private sealed class Configuration
        {
            public string ChatPrefix = "Permissions:";
            public string DefaultGroup = "default";
            public List<string> DefaultPermissions = new List<string>();
            public bool ApplyDefaultsOnLoad = true;
            public bool ApplyDefaultsOnPlayerConnect = true;
            public bool ApplyDefaultsOncePerPlayer = true;
            public bool GrantDefaultsDirectlyToPlayer = true;
            public bool CreateDefaultGroup = true;
            public bool AddDefaultGroupForTrackUsers = false;
            public bool GrantAdminPermissionsToAdminGroup = true;
            public string AdminWildcard = "oxide.*";
            public bool RequireWildcardForAdmin = true;
            public bool GrantAllPermissionsToAdmin = true;
            public List<string> AutoGrantUse = new List<string>();
            public List<string> AutoGrantAdmin = new List<string>();
            public Dictionary<string, List<string>> Tracks = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            public int MaxRows = 10;
        }

        #endregion
    }
}

