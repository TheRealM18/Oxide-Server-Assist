using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("PlayerProfiles", "TheRealM18", "1.0.0")]
    [Description("Saved character profiles (inventory, quests, level and more) with Server Assist UI and in-place switching.")]
    public class PlayerProfiles : CovalencePlugin
    {
        private const string PanelId = "playerprofiles";
        private const string PanelIcon = "sprite:sa_profile_panel";
        private const string RowIcon = "sprite:sa_profile_identity";
        private const string ActiveRowIcon = "sprite:sa_profile_active";
        private const string NewIcon = "sprite:sa_profile_new";
        private const string RenameIcon = "sprite:sa_profile_rename";
        private const string DeleteIcon = "sprite:sa_profile_delete";
        private const string SwitchIcon = "sprite:sa_profile_switch";
        private const string SaveIcon = "sprite:sa_profile_save";
        private const string HelpIcon = "sprite:sa_nav_help";
        private const string ReloadIcon = "sprite:sa_plugin_hot_reload";
        private const string ProtocolPrefix = "@pp|";
        private const int ProtocolVersion = 1;
        private const int MaxSlotsLimit = 50;
        private const int PageSize = 10;
        private const string DataRoot = "PlayerProfiles";

        private PlayerProfilesConfiguration config;
        private readonly Dictionary<string, PlayerProfilesState> states =
            new Dictionary<string, PlayerProfilesState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, UiSession> uiSessions =
            new Dictionary<string, UiSession>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, LiveSwitch> liveSwitches =
            new Dictionary<string, LiveSwitch>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> seamlessClients =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> lastSwitch =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> lastCapture =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> slotsCache =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, string> itemPrefixCache = new Dictionary<int, string>();
        private static int cachedItemCount;
        private static string cachedItemHash;

        private Timer tickTimer;
        private Plugin serverAssist;
        private bool ready;
        private bool unloading;
        private string gameVersion;
        private string modHash;

        #region Lifecycle

        private void Init()
        {
            LoadConfig();
            RegisterPermissions();

            AddCovalenceCommand(new[] { "profile", "profiles" }, nameof(CommandProfiles));

            gameVersion = Constants.cVersionMajor + "." + Constants.cVersionMinor + "." + Constants.cVersionBuild;
            modHash = ComputeModSetHash();

            tickTimer = timer.Repeat(5f, -1, Tick);
            timer.Once(2f, RegisterServerAssistPanel);
            timer.Once(10f, RegisterServerAssistPanel);
            ready = true;

            LogInfo("loaded. Permissions: " + config.PermissionPrefix +
                    ".slots.1 - " + config.MaxSlots + ". UI is provided by Server Assist.");
        }

        private void OnServerInitialized()
        {
            ClearItemMapCache();
            EnsureDefaultPermissions();
            RegisterServerAssistPanel();
        }

        private static void ClearItemMapCache()
        {
            cachedItemCount = 0;
            cachedItemHash = null;
            itemPrefixCache.Clear();
        }

        private void OnServerAssistReady()
        {
            RegisterServerAssistPanel();
        }

        private void OnServerSave()
        {
            CaptureOnline(false);
        }

        private void OnServerShutdown()
        {
            unloading = true;
            CaptureOnline(true);
        }

        private void Unload()
        {
            unloading = true;
            ready = false;
            CaptureOnline(true);

            if (tickTimer != null)
            {
                tickTimer.Destroy();
                tickTimer = null;
            }

            try
            {
                Plugin assistant = plugins.Find("ServerAssist");
                if (assistant != null)
                {
                    assistant.Call("UnregisterPanel", PanelId);
                }
            }
            catch
            {
                // Server Assist may already be unloading.
            }

            states.Clear();
            uiSessions.Clear();
            liveSwitches.Clear();
            seamlessClients.Clear();
            lastSwitch.Clear();
            lastCapture.Clear();
            slotsCache.Clear();
        }

        private void Tick()
        {
            if (!ready)
            {
                return;
            }

            CheckSwitchTimeouts();
            CaptureOnline(false);
        }

        private void OnUserPermissionGranted(string playerId, string permissionName)
        {
            slotsCache.Remove(playerId);
        }

        private void OnUserPermissionRevoked(string playerId, string permissionName)
        {
            slotsCache.Remove(playerId);
        }

        private void OnGroupPermissionGranted(string groupName, string permissionName)
        {
            slotsCache.Clear();
        }

        private void OnGroupPermissionRevoked(string groupName, string permissionName)
        {
            slotsCache.Clear();
        }

        private void OnUserGroupAdded(string playerId, string groupName)
        {
            slotsCache.Remove(playerId);
        }

        private void OnUserGroupRemoved(string playerId, string groupName)
        {
            slotsCache.Remove(playerId);
        }

        #endregion

        #region Login / logout / save

        /// <summary>
        /// Runs before the vanilla spawn path loads the player's .ttp. This is
        /// the only server-only place to materialise a profile for a reconnect.
        /// </summary>
        private object OnUserApprove(ClientInfo clientInfo)
        {
            if (clientInfo == null)
            {
                return null;
            }

            try
            {
                PlayerProfilesState state = EnsureState(clientInfo);
                if (state == null)
                {
                    return null;
                }

                if (state.PendingSwitch != null)
                {
                    ApplyPendingOnApprove(clientInfo, state);
                    return null;
                }

                ReconcileActiveProfile(clientInfo, state);
            }
            catch (Exception exception)
            {
                // Never lock a player out because profile bookkeeping failed.
                LogError("OnUserApprove failed for " + clientInfo.playerName + ": " + exception);
            }

            return null;
        }

        private void OnPlayerConnected(ClientInfo clientInfo)
        {
            if (clientInfo == null || unloading)
            {
                return;
            }

            try
            {
                EnsureState(clientInfo);
            }
            catch (Exception exception)
            {
                LogWarning("OnPlayerConnected profile state failed: " + exception.Message);
            }
        }

        private void OnPlayerSpawned(ClientInfo clientInfo)
        {
            if (clientInfo == null || unloading)
            {
                return;
            }

            try
            {
                PlayerProfilesState state = EnsureState(clientInfo);
                if (state == null)
                {
                    return;
                }

                state.PlayerName = clientInfo.playerName ?? state.PlayerName;
                state.LastSeenUtc = IsoNow();

                PlayerProfileEntry active = FindProfile(state, state.ActiveProfileId);
                if (active == null)
                {
                    return;
                }

                active.LastUsedUtc = IsoNow();
                UpdateLevel(active, clientInfo);
                if (File.Exists(VanillaPath(clientInfo)))
                {
                    active.HasData = true;
                }

                SaveState(state);

                if (!active.HasData && state.PendingSwitch == null)
                {
                    string playerId = ReadableId(clientInfo);
                    string profileId = active.Id;
                    timer.Once(5f, delegate
                    {
                        try
                        {
                            IPlayer player = players.FindPlayerById(playerId);
                            ClientInfo client = GetClientInfo(playerId);
                            if (player == null || !player.IsConnected || client == null)
                            {
                                return;
                            }

                            PlayerProfilesState current = GetState(playerId, false);
                            if (current == null ||
                                current.PendingSwitch != null ||
                                !string.Equals(current.ActiveProfileId, profileId, StringComparison.OrdinalIgnoreCase))
                            {
                                return;
                            }

                            CaptureLatestToProfile(client, current, profileId, "first-spawn");
                        }
                        catch (Exception exception)
                        {
                            LogWarning("first-spawn capture failed: " + exception.Message);
                        }
                    });
                }
            }
            catch (Exception exception)
            {
                LogError("OnPlayerSpawned failed for " + clientInfo.playerName + ": " + exception);
            }
        }

        private void OnPlayerDisconnected(ClientInfo clientInfo)
        {
            if (clientInfo == null)
            {
                return;
            }

            string playerId = ReadableId(clientInfo);
            if (string.IsNullOrEmpty(playerId))
            {
                return;
            }

            try
            {
                PlayerProfilesState state = GetState(playerId, false);
                if (state != null && !unloading)
                {
                    // A live switch in progress must be reverted on the next
                    // login. A reconnect switch already captured the old profile.
                    if (state.PendingSwitch == null && !string.IsNullOrEmpty(state.ActiveProfileId))
                    {
                        CaptureLatestToProfile(clientInfo, state, state.ActiveProfileId, "disconnect");
                    }

                    state.LastSeenUtc = IsoNow();
                    SaveState(state);
                }
            }
            catch (Exception exception)
            {
                LogError("disconnect capture failed for " + playerId + ": " + exception);
            }

            seamlessClients.Remove(playerId);
            uiSessions.Remove(playerId);
            liveSwitches.Remove(playerId);
            lastSwitch.Remove(playerId);
            lastCapture.Remove(playerId);
        }

        private void OnUserDisconnected(IPlayer player)
        {
            if (player == null)
            {
                return;
            }

            seamlessClients.Remove(player.Id);
            uiSessions.Remove(player.Id);
            liveSwitches.Remove(player.Id);
        }

        private void CaptureOnline(bool force)
        {
            if (!ready && !force)
            {
                return;
            }

            try
            {
                ConnectionManager connection = SingletonMonoBehaviour<ConnectionManager>.Instance;
                if (connection == null || connection.Clients == null)
                {
                    return;
                }

                DateTime now = DateTime.UtcNow;
                for (int i = 0; i < connection.Clients.list.Count; i++)
                {
                    ClientInfo client = connection.Clients.list[i];
                    if (client == null)
                    {
                        continue;
                    }

                    string playerId = ReadableId(client);
                    if (string.IsNullOrEmpty(playerId))
                    {
                        continue;
                    }

                    PlayerProfilesState state = GetState(playerId, false);
                    if (state == null ||
                        state.PendingSwitch != null ||
                        string.IsNullOrEmpty(state.ActiveProfileId))
                    {
                        continue;
                    }

                    DateTime last;
                    if (!force &&
                        lastCapture.TryGetValue(playerId, out last) &&
                        (now - last).TotalSeconds < config.AutosaveSeconds)
                    {
                        continue;
                    }

                    if (CaptureLatestToProfile(client, state, state.ActiveProfileId, "autosave"))
                    {
                        lastCapture[playerId] = now;
                    }
                }
            }
            catch (Exception exception)
            {
                LogWarning("autosave failed: " + exception.Message);
            }
        }

        #endregion

        #region Reconnect materialisation

        private void ApplyPendingOnApprove(ClientInfo clientInfo, PlayerProfilesState state)
        {
            PlayerProfilesPendingSwitch pending = state.PendingSwitch;
            if (pending == null)
            {
                return;
            }

            if (string.Equals(pending.Mode, "live", StringComparison.OrdinalIgnoreCase))
            {
                // The switch never committed. Revert to the profile we left so
                // an interrupted live switch cannot leave a half-applied state.
                LogInfo("Reverting interrupted live switch for " + state.PlayerName + " to " + pending.FromProfileId);
                RevertToProfile(clientInfo, state, pending.FromProfileId, true);
                return;
            }

            PlayerProfileEntry target = FindProfile(state, pending.ToProfileId);
            if (target == null)
            {
                LogWarning("Pending profile " + pending.ToProfileId + " no longer exists for " + state.PlayerName);
                ClearPending(state);
                ReconcileActiveProfile(clientInfo, state);
                return;
            }

            if (pending.TargetFresh || !target.HasData)
            {
                DeleteVanilla(clientInfo);
                state.ActiveProfileId = target.Id;
                target.HasData = false;
                target.Level = 1;
                target.Deaths = 0;
                target.PlaytimeMinutes = 0f;
                target.Bytes = 0;
                target.SavedAtUtc = null;
                target.LastUsedUtc = IsoNow();
                ClearPending(state);
                SaveState(state);
                LogInfo("Created fresh profile '" + target.Name + "' for " + state.PlayerName);
                return;
            }

            byte[] payload = ReadProfileBytes(state.PlayerId, target.Id);
            PlayerDataFile pdf = DeserializePlayerData(payload);
            if (pdf == null)
            {
                LogWarning("Could not read target profile " + target.Id + " for " + state.PlayerName + "; reverting.");
                ClearPending(state);
                RevertToProfile(clientInfo, state, pending.FromProfileId, true);
                return;
            }

            PrepareForReconnectImport(pdf);
            if (!WriteVanilla(clientInfo, pdf))
            {
                LogWarning("Could not materialise target profile " + target.Id + " for " + state.PlayerName);
                ClearPending(state);
                RevertToProfile(clientInfo, state, pending.FromProfileId, true);
                return;
            }

            state.ActiveProfileId = target.Id;
            target.HasData = true;
            target.LastUsedUtc = IsoNow();
            UpdateEntryFromPdf(target, pdf, null, payload != null ? payload.Length : 0);
            ClearPending(state);
            SaveState(state);

            Interface.CallHook("OnPlayerProfileSwitch", players.FindPlayerById(state.PlayerId),
                pending.FromProfileId, target.Id, false);
            LogInfo("Loaded profile '" + target.Name + "' for " + state.PlayerName + " on reconnect");
        }

        private void RevertToProfile(ClientInfo clientInfo, PlayerProfilesState state, string profileId, bool save)
        {
            PlayerProfileEntry entry = FindProfile(state, profileId);
            if (entry == null)
            {
                entry = state.Profiles.FirstOrDefault();
                profileId = entry != null ? entry.Id : null;
            }

            ClearPending(state);
            if (entry == null)
            {
                state.ActiveProfileId = null;
                if (save)
                {
                    SaveState(state);
                }
                return;
            }

            state.ActiveProfileId = entry.Id;
            byte[] payload = ReadProfileBytes(state.PlayerId, entry.Id);
            entry.HasData = payload != null;

            if (entry.HasData)
            {
                PlayerDataFile pdf = DeserializePlayerData(payload);
                if (pdf != null)
                {
                    PrepareForReconnectImport(pdf);
                    WriteVanilla(clientInfo, pdf);
                }
            }
            else
            {
                DeleteVanilla(clientInfo);
            }

            if (save)
            {
                SaveState(state);
            }
        }

        /// <summary>
        /// Normal login without a pending switch:
        ///  - if the vanilla .ttp is newer, adopt it into the active profile
        ///  - otherwise materialise the active profile over the vanilla .ttp
        ///  - if the active profile is new and the vanilla file exists, adopt it
        /// </summary>
        private void ReconcileActiveProfile(ClientInfo clientInfo, PlayerProfilesState state)
        {
            PlayerProfileEntry active = FindProfile(state, state.ActiveProfileId);
            if (active == null)
            {
                active = state.Profiles.FirstOrDefault();
                state.ActiveProfileId = active != null ? active.Id : null;
                if (active == null)
                {
                    SaveState(state);
                    return;
                }
            }

            if (active.ItemCount > 0 &&
                !ItemMapPrefixMatches(active.ItemMapHash, active.ItemCount))
            {
                LogWarning("active profile '" + active.Name + "' was saved with a different item ID layout. " +
                           "The new mods may mis-map items. Restore a profile backup, or reorder the new item mod so it loads last.");
            }
            else if (active.ItemCount <= 0 &&
                     !string.IsNullOrEmpty(active.ModHash) &&
                     !string.Equals(active.ModHash, modHash, StringComparison.OrdinalIgnoreCase))
            {
                LogWarning("active profile '" + active.Name + "' has no item-map metadata and the mod set changed. " +
                           "If the new mod inserted items before existing mods, items may be mis-mapped. " +
                           "Back up oxide/data/PlayerProfiles and check after login.");
            }

            string vanilla = VanillaPath(clientInfo);
            bool vanillaExists = File.Exists(vanilla);
            byte[] profilePayload = ReadProfileBytes(state.PlayerId, active.Id);

            if (profilePayload != null && !active.HasData)
            {
                active.HasData = true;
                SaveState(state);
            }

            if (profilePayload == null)
            {
                // No readable profile (main or backups). If the vanilla save
                // exists it is the only copy left; otherwise this profile is
                // simply brand new.
                if (vanillaExists)
                {
                    AdoptVanillaIntoProfile(clientInfo, state, active, vanilla);
                }
                else if (active.HasData)
                {
                    active.HasData = false;
                    SaveState(state);
                }
                return;
            }

            DateTime profileTime = ProfileLastWrite(state.PlayerId, active.Id);
            if (vanillaExists && File.GetLastWriteTimeUtc(vanilla) > profileTime)
            {
                AdoptVanillaIntoProfile(clientInfo, state, active, vanilla);
                return;
            }

            PlayerDataFile pdf = DeserializePlayerData(profilePayload);
            if (pdf == null)
            {
                LogWarning("Active profile " + active.Id + " is unreadable for " + state.PlayerName +
                           "; leaving the vanilla save alone.");
                return;
            }

            PrepareForReconnectImport(pdf);
            WriteVanilla(clientInfo, pdf);
        }

        private static DateTime ProfileLastWrite(string playerId, string profileId)
        {
            string path = ProfilePath(playerId, profileId);
            DateTime newest = DateTime.MinValue;
            string[] candidates = new[] { path, path + ".bak1", path + ".bak2" };

            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    if (!File.Exists(candidates[i]))
                    {
                        continue;
                    }

                    DateTime write = File.GetLastWriteTimeUtc(candidates[i]);
                    if (write > newest)
                    {
                        newest = write;
                    }
                }
                catch
                {
                }
            }

            return newest;
        }

        private void AdoptVanillaIntoProfile(
            ClientInfo clientInfo, PlayerProfilesState state, PlayerProfileEntry entry, string vanillaPath)
        {
            try
            {
                byte[] raw = File.ReadAllBytes(vanillaPath);
                PlayerDataFile pdf = DeserializePlayerData(raw);
                if (pdf == null)
                {
                    LogWarning("Vanilla .ttp for " + state.PlayerName + " could not be parsed; not adopting it.");
                    return;
                }

                PrepareForStorage(pdf);
                byte[] payload = SerializePlayerData(pdf);
                if (payload == null || !WriteProfileFile(state.PlayerId, entry.Id, payload))
                {
                    return;
                }

                UpdateEntryFromPdf(entry, pdf, GetEntity(clientInfo), payload.Length);
                entry.HasData = true;
                SaveState(state);
                LogDebug("Adopted vanilla save into profile '" + entry.Name + "' for " + state.PlayerName);
            }
            catch (Exception exception)
            {
                LogWarning("Could not adopt vanilla save for " + state.PlayerName + ": " + exception.Message);
            }
        }

        #endregion

        #region Capture / apply

        private bool CaptureLatestToProfile(
            ClientInfo clientInfo, PlayerProfilesState state, string profileId, string reason)
        {
            if (clientInfo == null || state == null || string.IsNullOrEmpty(profileId))
            {
                return false;
            }

            PlayerProfileEntry entry = FindProfile(state, profileId);
            if (entry == null)
            {
                return false;
            }

            try
            {
                PlayerDataFile source = clientInfo.latestPlayerData;
                PlayerDataFile pdf = null;

                if (source != null)
                {
                    pdf = DeserializePlayerData(SerializePlayerData(source));
                    if (pdf != null &&
                        (pdf.inventoryData == null || pdf.inventoryData.Length == 0 ||
                         pdf.bagData == null || pdf.bagData.Length == 0 ||
                         pdf.equipmentData == null || pdf.equipmentData.Length == 0 ||
                         pdf.progressionData == null || pdf.progressionData.Length == 0 ||
                         pdf.buffData == null || pdf.buffData.Length == 0 ||
                         pdf.stealthData == null || pdf.stealthData.Length == 0))
                    {
                        // A client that has not completed its spawn can expose
                        // an empty PlayerDataFile. Never persist that.
                        pdf = null;
                    }
                }

                if (pdf == null)
                {
                    EntityPlayer entity = GetEntity(clientInfo);
                    if (entity != null)
                    {
                        pdf = new PlayerDataFile();
                        pdf.FromPlayer(entity, StreamModeWrite.Persistency);
                        if (pdf.inventoryData == null || pdf.inventoryData.Length == 0 ||
                            pdf.bagData == null || pdf.bagData.Length == 0 ||
                            pdf.equipmentData == null || pdf.equipmentData.Length == 0 ||
                            pdf.progressionData == null || pdf.progressionData.Length == 0 ||
                            pdf.buffData == null || pdf.buffData.Length == 0 ||
                            pdf.stealthData == null || pdf.stealthData.Length == 0)
                        {
                            pdf = null;
                        }
                    }
                }

                if (pdf == null)
                {
                    LogWarning("No player data available to capture for " + state.PlayerName + " (" + reason + ")");
                    return false;
                }

                PrepareForStorage(pdf);
                byte[] payload = SerializePlayerData(pdf);
                if (payload == null || payload.Length < 16)
                {
                    LogWarning("Serialising profile failed for " + state.PlayerName + " (" + reason + ")");
                    return false;
                }

                if (!WriteProfileFile(state.PlayerId, entry.Id, payload))
                {
                    return false;
                }

                UpdateEntryFromPdf(entry, pdf, GetEntity(clientInfo), payload.Length);
                entry.HasData = true;
                entry.LastUsedUtc = IsoNow();
                SaveState(state);

                LogDebug("Captured '" + entry.Name + "' for " + state.PlayerName +
                         " (" + payload.Length + " bytes, " + reason + ")");
                return true;
            }
            catch (Exception exception)
            {
                LogError("capture failed for " + state.PlayerName + " (" + reason + "): " + exception);
                return false;
            }
        }

        private void PrepareForStorage(PlayerDataFile pdf)
        {
            if (pdf == null)
            {
                return;
            }

            pdf.bLoaded = true;
            pdf.bModifiedSinceLastSave = false;
            SanitizeQuestJournal(pdf.questJournal);
        }

        private void PrepareForImport(PlayerDataFile pdf, EntityPlayer entity)
        {
            pdf.id = entity != null ? entity.entityId : -1;
            pdf.bLoaded = true;
            pdf.bModifiedSinceLastSave = true;
            SanitizeQuestJournal(pdf.questJournal);

            if (config.KeepPositionOnSwitch && entity != null)
            {
                pdf.ecd.pos = entity.position;
                pdf.ecd.rot = entity.rotation;
            }
            else if (entity != null && pdf.ecd.pos.sqrMagnitude < 1f)
            {
                pdf.ecd.pos = entity.position;
                pdf.ecd.rot = entity.rotation;
            }
        }

        private void PrepareForReconnectImport(PlayerDataFile pdf)
        {
            if (pdf == null)
            {
                return;
            }

            pdf.bLoaded = true;
            SanitizeQuestJournal(pdf.questJournal);

            if (pdf.ecd.pos.sqrMagnitude < 1f)
            {
                Vector3? spawn = SafeWorldSpawn();
                if (spawn.HasValue)
                {
                    pdf.ecd.pos = spawn.Value;
                    pdf.ecd.rot = Vector3.zero;
                }
            }
        }

        private bool ApplyToServer(ClientInfo clientInfo, EntityPlayer entity, PlayerDataFile pdf)
        {
            if (clientInfo == null || entity == null || pdf == null)
            {
                return false;
            }

            try
            {
                LockManager.Instance.ForceUnlockByPlayer(entity.entityId);
            }
            catch
            {
                // Locks are not fatal.
            }

            try
            {
                ClearServerPlayerState(entity);
                PrepareForImport(pdf, entity);
                ResetProgressionToMin(entity);

                pdf.ToPlayer(entity, StreamModeRead.Persistency);
                entity.SetPosition(pdf.ecd.pos, true);
                entity.SetRotation(pdf.ecd.rot);

                try
                {
                    if (entity.equipment != null)
                    {
                        entity.equipment.FireEventsForSetSlots();
                    }
                }
                catch
                {
                }

                entity.bPlayerStatsChanged = true;
                entity.bPlayerEquipmentChanged = true;

                clientInfo.latestPlayerData = pdf;
                pdf.bModifiedSinceLastSave = true;

                WriteVanilla(clientInfo, pdf);
                BroadcastEquipment(entity);

                return true;
            }
            catch (Exception exception)
            {
                LogError("server apply failed: " + exception);
                return false;
            }
        }

        private void ClearServerPlayerState(EntityPlayer entity)
        {
            if (entity == null)
            {
                return;
            }

            try
            {
                if (entity.Buffs != null)
                {
                    if (entity.Buffs.ActiveBuffs != null)
                    {
                        for (int i = entity.Buffs.ActiveBuffs.Count - 1; i >= 0; i--)
                        {
                            BuffValue buff = entity.Buffs.ActiveBuffs[i];
                            entity.Buffs.ActiveBuffs.RemoveAt(i);
                            if (buff != null)
                            {
                                entity.Stats.EntityBuffRemoved(buff);
                            }
                        }
                    }

                    if (entity.Buffs.CVars != null)
                    {
                        entity.Buffs.CVars.Clear();
                    }

                    if (entity.Buffs.CVarsLastNetSync != null)
                    {
                        entity.Buffs.CVarsLastNetSync.Clear();
                    }
                }
            }
            catch (Exception exception)
            {
                LogWarning("server buff cleanup failed: " + exception.Message);
            }

            try
            {
                if (entity.ownedEntities != null)
                {
                    for (int i = entity.ownedEntities.Count - 1; i >= 0; i--)
                    {
                        entity.RemoveOwnedEntity(entity.ownedEntities[i]);
                    }
                }
            }
            catch (Exception exception)
            {
                LogWarning("server owned entity cleanup failed: " + exception.Message);
            }

            try
            {
                // Bedroll position is PersistentPlayerData, not profile data.
                // Clear it before ToPlayer sets the target profile's spawn.
                if (entity.SpawnPoints != null)
                {
                    entity.SpawnPoints.Clear();
                }
            }
            catch (Exception exception)
            {
                LogWarning("server spawn point cleanup failed: " + exception.Message);
            }

            try
            {
                if (entity.QuestJournal != null)
                {
                    entity.QuestJournal.UnHookQuests();
                    entity.QuestJournal.ActiveQuest = null;
                }
            }
            catch (Exception exception)
            {
                LogWarning("server quest cleanup failed: " + exception.Message);
            }
        }

        private static void ResetProgressionToMin(EntityPlayer entity)
        {
            try
            {
                Progression progression = entity != null ? entity.Progression : null;
                if (progression == null ||
                    progression.ProgressionValues == null ||
                    progression.ProgressionValues.Dict == null)
                {
                    return;
                }

                foreach (ProgressionValue value in progression.ProgressionValues.Dict.Values)
                {
                    if (value != null && value.ProgressionClass != null)
                    {
                        // Values present in the profile are overwritten by
                        // Progression.Read. Values added by a new mod stay at
                        // their minimum instead of leaking from the old profile.
                        value.Level = value.ProgressionClass.MinLevel;
                    }
                }
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[PlayerProfiles] progression reset failed: " + exception.Message);
            }
        }

        private static void BroadcastEquipment(EntityPlayer entity)
        {
            try
            {
                ConnectionManager connection = SingletonMonoBehaviour<ConnectionManager>.Instance;
                if (connection == null || entity == null)
                {
                    return;
                }

                connection.SendPackage(
                    NetPackageManager.GetPackage<NetPackagePlayerEquipment>().Setup(entity),
                    false, -1, entity.entityId);

                connection.SendPackage(
                    NetPackageManager.GetPackage<NetPackagePlayerStats>().Setup(entity),
                    false, -1, entity.entityId);

                connection.SendPackage(
                    NetPackageManager.GetPackage<NetPackageHoldingItem>().Setup(entity),
                    false, -1, entity.entityId);
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[PlayerProfiles] equipment broadcast failed: " + exception.Message);
            }
        }

        private static void SanitizeQuestJournal(QuestJournal journal)
        {
            if (journal == null)
            {
                return;
            }

            try
            {
                if (journal.sharedQuestEntries != null)
                {
                    journal.sharedQuestEntries.Clear();
                }
            }
            catch
            {
            }

            try
            {
                journal.trackedQuest = null;
                journal.ActiveQuest = null;
            }
            catch
            {
            }

            try
            {
                if (journal.quests != null)
                {
                    for (int i = journal.quests.Count - 1; i >= 0; i--)
                    {
                        Quest quest = journal.quests[i];
                        if (quest == null)
                        {
                            journal.quests.RemoveAt(i);
                            continue;
                        }

                        if (quest.SharedOwnerID != -1)
                        {
                            try
                            {
                                quest.Tracked = false;
                            }
                            catch
                            {
                            }
                            journal.quests.RemoveAt(i);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[PlayerProfiles] quest sanitisation failed: " + exception.Message);
            }
        }

        #endregion

        #region Server Assist panel

        private void RegisterServerAssistPanel()
        {
            serverAssist = plugins.Find("ServerAssist");
            if (serverAssist == null)
            {
                return;
            }

            try
            {
                bool registered = false;

                try
                {
                    object result = serverAssist.Call(
                        "RegisterPanel",
                        PanelId,
                        "Profiles",
                        "Save and switch full character profiles.",
                        config.UsePermission,
                        "PlayerProfiles",
                        nameof(HandleGuiRequest),
                        PanelIcon,
                        25);

                    registered = result is bool && (bool)result;
                }
                catch
                {
                    // Older Server Assist builds only expose RegisterGuiHook.
                }

                if (!registered)
                {
                    try
                    {
                        object result = serverAssist.Call(
                            "RegisterGuiHook",
                            PanelId,
                            "Profiles",
                            "Save and switch full character profiles.",
                            config.UsePermission,
                            "PlayerProfiles",
                            nameof(HandleGuiRequest));

                        registered = result is bool && (bool)result;
                    }
                    catch
                    {
                        registered = false;
                    }
                }

                if (registered)
                {
                    try
                    {
                        serverAssist.Call("SetPanelIcon", PanelId, PanelIcon);
                    }
                    catch
                    {
                    }

                    LogInfo("registered the profiles panel with Server Assist (" + PanelIcon + ").");
                }
                else
                {
                    LogWarning("could not register the profiles panel with Server Assist.");
                }
            }
            catch (Exception exception)
            {
                LogWarning("Server Assist panel registration failed: " + exception.Message);
            }
        }

        [HookMethod("GetActiveProfile")]
        public string GetActiveProfile(string playerId)
        {
            PlayerProfilesState state = GetState(playerId, false);
            return state != null ? state.ActiveProfileId : null;
        }

        [HookMethod("GetActiveProfileName")]
        public string GetActiveProfileName(string playerId)
        {
            PlayerProfilesState state = GetState(playerId, false);
            PlayerProfileEntry entry = state != null
                ? FindProfile(state, state.ActiveProfileId)
                : null;
            return entry != null ? entry.Name : null;
        }

        [HookMethod("GetProfileScopedId")]
        public string GetProfileScopedId(string playerId)
        {
            string profileId = GetActiveProfile(playerId);
            return string.IsNullOrEmpty(profileId) ? playerId : playerId + "#" + profileId;
        }

        public string HandleGuiRequest(string playerId, string payload)
        {
            if (!string.IsNullOrEmpty(payload) &&
                payload.StartsWith(ProtocolPrefix, StringComparison.Ordinal))
            {
                return HandleProtocol(playerId, payload);
            }

            IPlayer player = players.FindPlayerById(playerId);
            if (player == null)
            {
                return RenderError("Could not resolve your player.");
            }

            if (!CanUse(player))
            {
                return RenderError("You do not have permission to use profiles.");
            }

            PlayerProfilesState state = GetState(player.Id, true);
            if (state == null)
            {
                return RenderError("Your profile data could not be loaded.");
            }

            EnsureBootstrapProfile(player, state);
            UiSession session = GetUiSession(player.Id);
            string request = payload ?? string.Empty;
            string status = session.Status ?? string.Empty;

            if (request.StartsWith("t|", StringComparison.Ordinal))
            {
                return HandleTextInput(player, state, session, Decode(request.Substring(2)));
            }

            string action = request;
            if (action.StartsWith("a|", StringComparison.OrdinalIgnoreCase))
            {
                action = action.Substring(2);
            }

            string[] parts = action.Split('|');
            string verb = parts.Length > 0 ? (parts[0] ?? string.Empty).Trim().ToLowerInvariant() : string.Empty;

            switch (verb)
            {
                case "":
                case "open":
                    session.Mode = "list";
                    session.Status = string.Empty;
                    return RenderList(player, state, session);

                case "refresh":
                    session.Status = string.Empty;
                    return RenderList(player, state, session);

                case "page":
                    ChangePage(session, parts.Length > 1 ? parts[1] : "next",
                        GetProfileList(state).Count);
                    return RenderList(player, state, session);

                case "select":
                    session.SelectedId = parts.Length > 1 ? parts[1] : null;
                    session.Mode = "detail";
                    session.Status = string.Empty;
                    return RenderDetail(player, state, session);

                case "new":
                    session.Mode = "new";
                    session.Input = string.Empty;
                    session.Status = string.Empty;
                    return RenderNew(player, state, session);

                case "create":
                    return CreateProfile(player, state, session, session.Input);

                case "rename":
                    session.SelectedId = parts.Length > 1 ? parts[1] : session.SelectedId;
                    session.Mode = "rename";
                    PlayerProfileEntry renameEntry = FindProfile(state, session.SelectedId);
                    session.Input = renameEntry != null ? renameEntry.Name : string.Empty;
                    session.Status = string.Empty;
                    return RenderRename(player, state, session);

                case "rename_save":
                    return RenameProfile(player, state, session, session.Input);

                case "delete":
                    session.SelectedId = parts.Length > 1 ? parts[1] : session.SelectedId;
                    session.Mode = "delete";
                    session.Status = string.Empty;
                    return RenderDelete(player, state, session);

                case "delete_confirm":
                    return DeleteProfile(player, state, session);

                case "switch":
                    session.SelectedId = parts.Length > 1 ? parts[1] : session.SelectedId;
                    session.Mode = "switch";
                    session.Status = string.Empty;
                    return RenderSwitch(player, state, session);

                case "switch_confirm":
                    return ConfirmSwitch(player, state, session);

                case "save":
                    if (CaptureActive(player, state))
                    {
                        session.Status = "Your current profile was saved.";
                    }
                    else
                    {
                        session.Status = "Your current profile could not be saved.";
                    }
                    return RenderList(player, state, session);

                case "help":
                    session.Mode = "help";
                    return RenderHelp(player, state, session);

                case "reload":
                    if (player.IsAdmin || permission.UserHasPermission(player.Id, config.AdminPermission))
                    {
                        CaptureOnline(false);
                        LoadConfig();
                        RegisterPermissions();
                        EnsureDefaultPermissions();
                        slotsCache.Clear();
                        states.Clear();
                        session.Status = "Configuration and permissions reloaded.";
                    }
                    else
                    {
                        session.Status = "You do not have permission to reload profiles.";
                    }
                    return RenderList(player, state, session);

                case "back":
                default:
                    session.Mode = "list";
                    session.Status = string.Empty;
                    return RenderList(player, state, session);
            }
        }

        private string HandleTextInput(
            IPlayer player, PlayerProfilesState state, UiSession session, string text)
        {
            session.Input = (text ?? string.Empty).Trim();

            if (string.Equals(session.Mode, "new", StringComparison.OrdinalIgnoreCase))
            {
                return CreateProfile(player, state, session, session.Input);
            }

            if (string.Equals(session.Mode, "rename", StringComparison.OrdinalIgnoreCase))
            {
                return RenameProfile(player, state, session, session.Input);
            }

            session.Status = "That input is not active.";
            return RenderList(player, state, session);
        }

        private string CreateProfile(
            IPlayer player, PlayerProfilesState state, UiSession session, string requestedName)
        {
            string error;
            string name;
            if (!TryNormaliseName(state, requestedName, null, out name, out error))
            {
                session.Status = error;
                return RenderNew(player, state, session);
            }

            int slots = GetSlots(player.Id);
            if (state.Profiles.Count >= slots)
            {
                session.Status = "You have used all " + slots + " profile slot(s). Ask an admin for a higher " +
                                 config.PermissionPrefix + ".slots.N permission.";
                return RenderNew(player, state, session);
            }

            PlayerProfileEntry entry = new PlayerProfileEntry
            {
                Id = NewProfileId(state),
                Name = name,
                CreatedUtc = IsoNow(),
                LastUsedUtc = IsoNow(),
                Order = NextOrder(state),
                HasData = false,
                Level = 1
            };

            state.Profiles.Add(entry);

            if (!config.NewProfileStartsFresh)
            {
                ClientInfo client = GetClientInfo(player.Id);
                if (client == null || !CaptureLatestToProfile(client, state, entry.Id, "created-copy"))
                {
                    state.Profiles.Remove(entry);
                    session.Status = "The new profile could not be copied from your current character.";
                    return RenderNew(player, state, session);
                }
            }

            SaveState(state);
            session.Mode = "detail";
            session.SelectedId = entry.Id;
            session.Status = entry.HasData
                ? "Profile '" + entry.Name + "' created from your current character."
                : "Profile '" + entry.Name + "' created. Switch to it to start it (one reconnect).";

            Interface.CallHook("OnPlayerProfileCreated", player, entry.Id, entry.Name);
            return RenderDetail(player, state, session);
        }

        private string RenameProfile(
            IPlayer player, PlayerProfilesState state, UiSession session, string requestedName)
        {
            PlayerProfileEntry entry = FindProfile(state, session.SelectedId);
            if (entry == null)
            {
                session.Mode = "list";
                session.Status = "That profile no longer exists.";
                return RenderList(player, state, session);
            }

            string error;
            string name;
            if (!TryNormaliseName(state, requestedName, entry.Id, out name, out error))
            {
                session.Status = error;
                return RenderRename(player, state, session);
            }

            string oldName = entry.Name;
            entry.Name = name;
            SaveState(state);
            session.Mode = "detail";
            session.SelectedId = entry.Id;
            session.Status = "Renamed '" + oldName + "' to '" + name + "'.";

            Interface.CallHook("OnPlayerProfileRenamed", player, entry.Id, oldName, name);
            return RenderDetail(player, state, session);
        }

        private string DeleteProfile(IPlayer player, PlayerProfilesState state, UiSession session)
        {
            PlayerProfileEntry entry = FindProfile(state, session.SelectedId);
            if (entry == null)
            {
                session.Mode = "list";
                session.Status = "That profile no longer exists.";
                return RenderList(player, state, session);
            }

            if (string.Equals(entry.Id, state.ActiveProfileId, StringComparison.OrdinalIgnoreCase))
            {
                session.Status = "You cannot delete the active profile.";
                return RenderDelete(player, state, session);
            }

            if (state.Profiles.Count <= 1)
            {
                session.Status = "You cannot delete your only profile.";
                return RenderDelete(player, state, session);
            }

            LiveSwitch activeSwitch;
            if (liveSwitches.TryGetValue(player.Id, out activeSwitch) &&
                string.Equals(activeSwitch.ToProfileId, entry.Id, StringComparison.OrdinalIgnoreCase))
            {
                session.Status = "That profile is being loaded right now.";
                return RenderDelete(player, state, session);
            }

            string name = entry.Name;
            ArchiveProfile(state.PlayerId, entry.Id);
            state.Profiles.Remove(entry);

            if (state.PendingSwitch != null &&
                string.Equals(state.PendingSwitch.ToProfileId, entry.Id, StringComparison.OrdinalIgnoreCase))
            {
                state.PendingSwitch = null;
            }

            SaveState(state);
            session.Mode = "list";
            session.SelectedId = null;
            session.Status = "Deleted profile '" + name + "'.";

            Interface.CallHook("OnPlayerProfileDeleted", player, entry.Id, name);
            return RenderList(player, state, session);
        }

        private string ConfirmSwitch(IPlayer player, PlayerProfilesState state, UiSession session)
        {
            PlayerProfileEntry entry = FindProfile(state, session.SelectedId);
            if (entry == null)
            {
                session.Mode = "list";
                session.Status = "That profile no longer exists.";
                return RenderList(player, state, session);
            }

            string error;
            string payload;
            if (!TrySwitch(player, state, entry, true, out payload, out error))
            {
                session.Mode = "switch";
                session.Status = error;
                return RenderSwitch(player, state, session);
            }

            if (!string.IsNullOrEmpty(payload) && payload.StartsWith(ProtocolPrefix, StringComparison.Ordinal))
            {
                // The client mod takes over the prepare/begin/chunk handshake.
                return payload;
            }

            session.Mode = "list";
            session.Status = string.IsNullOrEmpty(error) ? "Preparing the profile switch..." : error;
            return RenderList(player, state, session);
        }

        private bool CaptureActive(IPlayer player, PlayerProfilesState state)
        {
            ClientInfo client = GetClientInfo(player.Id);
            if (client == null)
            {
                return false;
            }

            return CaptureLatestToProfile(client, state, state.ActiveProfileId, "manual");
        }

        #endregion

        #region Live switch protocol

        private string HandleProtocol(string playerId, string payload)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return ProtocolPrefix + "ok";
            }

            string body = payload.Substring(ProtocolPrefix.Length);
            string[] parts = body.Split('|');
            string verb = parts.Length > 0 ? (parts[0] ?? string.Empty).Trim().ToLowerInvariant() : string.Empty;

            switch (verb)
            {
                case "hello":
                    seamlessClients.Add(playerId);
                    LogDebug("client hello from " + playerId);
                    return ProtocolPrefix + "ok";

                case "ready":
                    return HandleReady(playerId, parts);

                case "chunk":
                    return HandleChunkRequest(playerId, parts);

                case "applied":
                    return HandleApplied(playerId, parts);

                case "failed":
                    return HandleClientAbort(playerId, parts, true);

                case "recover":
                    return HandleClientRecover(playerId, parts);

                case "abort":
                    return HandleClientAbort(playerId, parts, false);

                default:
                    return ProtocolPrefix + "ok";
            }
        }

        private string HandleReady(string playerId, string[] parts)
        {
            string token = parts.Length > 1 ? parts[1] : string.Empty;
            LiveSwitch live;
            if (!liveSwitches.TryGetValue(playerId, out live) ||
                !string.Equals(live.Token, token, StringComparison.Ordinal))
            {
                return ProtocolPrefix + "abort|" + token + "|No profile switch is active.";
            }

            ClientInfo client = GetClientInfo(playerId);
            if (client == null)
            {
                AbortLiveSwitch(playerId, "You are no longer connected.");
                return ProtocolPrefix + "abort|" + token + "|You are no longer connected.";
            }

            PlayerProfilesState state = GetState(playerId, false);
            if (state == null)
            {
                AbortLiveSwitch(playerId, "Profile state is unavailable.");
                return ProtocolPrefix + "abort|" + token + "|Profile state is unavailable.";
            }

            if (!CaptureLatestToProfile(client, state, live.FromProfileId, "switch-old"))
            {
                AbortLiveSwitch(playerId, "Your current profile could not be saved.");
                return ProtocolPrefix + "abort|" + token + "|Your current profile could not be saved.";
            }

            string error;
            if (!PrepareLiveTarget(client, state, live, out error))
            {
                AbortLiveSwitch(playerId, error);
                return ProtocolPrefix + "abort|" + token + "|" + SafeReason(error);
            }

            live.ChunkBytes = Math.Max(1024, config.ChunkBytes);
            live.ChunkCount = (live.TargetPayload.Length + live.ChunkBytes - 1) / live.ChunkBytes;

            int itemCount = live.ItemCount;
            string itemHash = live.ItemMapHash;
            if (string.Equals(config.ModCompatibility, "Ignore", StringComparison.OrdinalIgnoreCase) ||
                itemCount <= 0 ||
                string.IsNullOrEmpty(itemHash))
            {
                // Ignore mode deliberately allows a shifted profile. Legacy
                // profiles have no stored map. In both cases, at least make the
                // client verify that its item table matches this server's.
                TryGetItemMap(out itemCount, out itemHash);
            }

            string itemSuffix = itemCount > 0 && !string.IsNullOrEmpty(itemHash)
                ? "|" + itemCount + "|" + itemHash
                : string.Empty;

            return ProtocolPrefix + "begin|" + token + "|" + live.ChunkCount + "|" +
                   live.TargetHash + "|" + live.TargetPayload.Length + "|" + live.ChunkBytes +
                   itemSuffix;
        }

        private bool PrepareLiveTarget(
            ClientInfo client, PlayerProfilesState state, LiveSwitch live, out string error)
        {
            error = null;

            byte[] stored = ReadProfileBytes(state.PlayerId, live.ToProfileId);
            PlayerDataFile pdf = DeserializePlayerData(stored);
            if (pdf == null)
            {
                error = "That profile could not be read.";
                return false;
            }

            if (pdf.bDead)
            {
                error = "That profile is dead. Reconnect to respawn it.";
                return false;
            }

            SanitizeQuestJournal(pdf.questJournal);
            pdf.id = -1;
            pdf.bLoaded = true;
            pdf.bModifiedSinceLastSave = false;

            EntityPlayer entity = GetEntity(client);
            if (entity != null && (config.KeepPositionOnSwitch || pdf.ecd.pos.sqrMagnitude < 1f))
            {
                pdf.ecd.pos = entity.position;
                pdf.ecd.rot = entity.rotation;
            }

            byte[] payload = SerializePlayerData(pdf);
            if (payload == null || payload.Length < 16)
            {
                error = "That profile could not be prepared.";
                return false;
            }

            live.TargetPayload = payload;
            live.TargetHash = Sha256Hex(payload);
            live.TargetPdf = pdf;
            return true;
        }

        private string HandleChunkRequest(string playerId, string[] parts)
        {
            string token = parts.Length > 1 ? parts[1] : string.Empty;
            LiveSwitch live;
            if (!liveSwitches.TryGetValue(playerId, out live) ||
                !string.Equals(live.Token, token, StringComparison.Ordinal) ||
                live.TargetPayload == null)
            {
                return ProtocolPrefix + "abort|" + token + "|No profile payload is active.";
            }

            int index;
            if (!int.TryParse(parts.Length > 2 ? parts[2] : string.Empty, out index) ||
                index < 0 || index >= live.ChunkCount)
            {
                return ProtocolPrefix + "abort|" + token + "|Invalid profile chunk.";
            }

            int chunkBytes = Math.Max(1024, live.ChunkBytes);
            int offset = index * chunkBytes;
            int length = Math.Min(chunkBytes, live.TargetPayload.Length - offset);
            string chunk = Convert.ToBase64String(live.TargetPayload, offset, length);
            return ProtocolPrefix + "chunk|" + token + "|" + index + "|" + chunk;
        }

        private string HandleApplied(string playerId, string[] parts)
        {
            string token = parts.Length > 1 ? parts[1] : string.Empty;
            string hash = parts.Length > 2 ? parts[2] : string.Empty;

            LiveSwitch live;
            if (!liveSwitches.TryGetValue(playerId, out live) ||
                !string.Equals(live.Token, token, StringComparison.Ordinal))
            {
                return ProtocolPrefix + "ok";
            }

            if (!string.Equals(hash, live.TargetHash, StringComparison.OrdinalIgnoreCase))
            {
                FailAppliedSwitch(playerId, live, "The profile payload changed in transit.");
                return ProtocolPrefix + "abort|" + token + "|The profile payload changed in transit.";
            }

            ClientInfo client = GetClientInfo(playerId);
            PlayerProfilesState state = GetState(playerId, false);
            EntityPlayer entity = GetEntity(client);

            if (client == null || state == null || entity == null)
            {
                FailAppliedSwitch(playerId, live, "The player entity disappeared during the switch.");
                return ProtocolPrefix + "abort|" + token + "|The player entity disappeared during the switch.";
            }

            PlayerProfileEntry target = FindProfile(state, live.ToProfileId);
            if (target == null)
            {
                FailAppliedSwitch(playerId, live, "That profile was deleted during the switch.");
                return ProtocolPrefix + "abort|" + token + "|That profile was deleted during the switch.";
            }

            PlayerDataFile pdf = DeserializePlayerData(live.TargetPayload);
            if (pdf == null || !ApplyToServer(client, entity, pdf))
            {
                LogError("server apply failed during live switch for " + playerId);
                FailAppliedSwitch(playerId, live, "The server could not apply the profile.");
                return ProtocolPrefix + "abort|" + token + "|The server could not apply the profile.";
            }

            string fromId = live.FromProfileId;
            state.ActiveProfileId = live.ToProfileId;
            ClearPending(state);

            UpdateEntryFromPdf(target, pdf, entity, live.TargetPayload.Length);
            target.HasData = true;
            target.LastUsedUtc = IsoNow();

            // Persist the exact payload that both sides applied, with the
            // current entity id so a reconnect can reuse it.
            PlayerDataFile storedPdf = DeserializePlayerData(live.TargetPayload);
            if (storedPdf != null)
            {
                // Keep the player's current entity id in the profile so a
                // reconnect can reuse it. This preserves entity-id based
                // vehicle/drone ownership better than a forced -1.
                storedPdf.id = entity.entityId;
                PrepareForStorage(storedPdf);
                byte[] storedPayload = SerializePlayerData(storedPdf);
                if (storedPayload != null)
                {
                    WriteProfileFile(state.PlayerId, live.ToProfileId, storedPayload);
                }
            }

            SaveState(state);
            liveSwitches.Remove(playerId);
            lastSwitch[playerId] = DateTime.UtcNow;

            Interface.CallHook("OnPlayerProfileSwitch",
                players.FindPlayerById(playerId), fromId, live.ToProfileId, true);

            LogInfo("switched " + state.PlayerName + " from '" + live.FromName + "' to '" + live.ToName + "'");
            return ProtocolPrefix + "done|" + token;
        }

        private string HandleClientAbort(string playerId, string[] parts, bool clientFailed)
        {
            string token = parts.Length > 1 ? parts[1] : string.Empty;
            string reason = parts.Length > 2 ? Decode(parts[2]) : "The profile switch was cancelled.";

            LiveSwitch live;
            if (liveSwitches.TryGetValue(playerId, out live) &&
                string.Equals(live.Token, token, StringComparison.Ordinal))
            {
                AbortLiveSwitch(playerId, reason);
            }

            if (clientFailed)
            {
                LogWarning("client aborted live switch for " + playerId + ": " + reason);
            }

            return ProtocolPrefix + "ok";
        }

        /// <summary>
        /// The client applied part of the payload and then failed. The safest
        /// recovery is to restore the previous profile on both sides and make
        /// the client reconnect.
        /// </summary>
        private string HandleClientRecover(string playerId, string[] parts)
        {
            string token = parts.Length > 1 ? parts[1] : string.Empty;
            string reason = parts.Length > 2 ? Decode(parts[2]) : "The profile apply failed.";

            ClientInfo client = GetClientInfo(playerId);
            PlayerProfilesState state = GetState(playerId, false);
            LiveSwitch live;

            if (client != null && state != null &&
                liveSwitches.TryGetValue(playerId, out live) &&
                string.Equals(live.Token, token, StringComparison.Ordinal))
            {
                LogWarning("client requested recovery during live switch for " + playerId + ": " + reason);
                RecoverFailedCommit(client, state, live);
                return ProtocolPrefix + "ok";
            }

            if (client != null)
            {
                string playerIdCopy = playerId;
                timer.Once(1f, delegate
                {
                    try
                    {
                        IPlayer player = players.FindPlayerById(playerIdCopy);
                        if (player != null && player.IsConnected)
                        {
                            player.Kick("The profile switch failed.\n\nReconnect to resync your character.");
                        }
                    }
                    catch
                    {
                    }
                });
            }

            return ProtocolPrefix + "ok";
        }

        private void AbortLiveSwitch(string playerId, string reason)
        {
            LiveSwitch live;
            if (liveSwitches.TryGetValue(playerId, out live))
            {
                liveSwitches.Remove(playerId);
                ClientInfo client = GetClientInfo(playerId);
                if (client != null && seamlessClients.Contains(playerId))
                {
                    SendRawResponse(client,
                        ProtocolPrefix + "abort|" + live.Token + "|" + SafeReason(reason));
                }
            }

            PlayerProfilesState state = GetState(playerId, false);
            if (state != null && state.PendingSwitch != null &&
                string.Equals(state.PendingSwitch.Mode, "live", StringComparison.OrdinalIgnoreCase))
            {
                ClearPending(state);
                SaveState(state);
            }

            LogDebug("live switch aborted for " + playerId + ": " + reason);
        }

        private void CheckSwitchTimeouts()
        {
            if (liveSwitches.Count == 0)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            List<string> expired = new List<string>();

            foreach (KeyValuePair<string, LiveSwitch> pair in liveSwitches)
            {
                LiveSwitch live = pair.Value;
                double age = (now - live.StartedUtc).TotalSeconds;

                if (age <= config.SwitchTimeoutSeconds)
                {
                    continue;
                }

                if (live.TimedOutUtc == default(DateTime))
                {
                    // Warn the client but keep the session alive briefly. The
                    // "applied" acknowledgement may simply be in flight; a late
                    // commit is much safer than tearing down a successful swap.
                    live.TimedOutUtc = now;
                    ClientInfo client = GetClientInfo(pair.Key);
                    if (client != null && seamlessClients.Contains(pair.Key))
                    {
                        SendRawResponse(client,
                            ProtocolPrefix + "abort|" + live.Token +
                            "|The profile switch timed out. Reconnecting is recommended.");
                    }

                    LogWarning("live switch timed out for " + pair.Key + "; waiting for a late acknowledgement.");
                }
                else if ((now - live.TimedOutUtc).TotalSeconds > 15.0)
                {
                    expired.Add(pair.Key);
                }
            }

            for (int i = 0; i < expired.Count; i++)
            {
                string key = expired[i];
                ClientInfo client = GetClientInfo(key);
                PlayerProfilesState state = GetState(key, false);
                LiveSwitch live;

                if (client == null)
                {
                    // The player is offline. Keep the pending live marker in
                    // state so the next login reverts to the previous profile.
                    liveSwitches.Remove(key);
                    LogWarning("dropped an unacknowledged switch for offline player " + key +
                               "; the next login will revert it.");
                }
                else if (state != null &&
                         liveSwitches.TryGetValue(key, out live))
                {
                    // No acknowledgement arrived. Force a reconnect back to
                    // the profile the player came from so the client and
                    // server cannot remain on different character states.
                    LogWarning("forcing a reconnect after an unacknowledged switch for " + key);
                    RecoverFailedCommit(client, state, live);
                }
                else
                {
                    AbortLiveSwitch(key, "The profile switch timed out.");
                }
            }
        }

        /// <summary>
        /// The client has already applied the payload but the server could not
        /// commit it. If the player is still connected, restore the old profile
        /// and kick them to resync. If they are already gone, leave the pending
        /// live marker in place so the next login reverts to the old profile.
        /// </summary>
        private void FailAppliedSwitch(string playerId, LiveSwitch live, string reason)
        {
            ClientInfo client = GetClientInfo(playerId);
            PlayerProfilesState state = GetState(playerId, false);

            if (client != null && state != null)
            {
                LogWarning("recovering a failed applied switch for " + playerId + ": " + reason);
                RecoverFailedCommit(client, state, live);
                return;
            }

            LogWarning("failed applied switch for offline player " + playerId +
                       "; the next login will revert to the previous profile: " + reason);
        }

        private void RecoverFailedCommit(ClientInfo client, PlayerProfilesState state, LiveSwitch live)
        {
            try
            {
                state.ActiveProfileId = live.FromProfileId;
                ClearPending(state);

                byte[] payload = ReadProfileBytes(state.PlayerId, live.FromProfileId);
                PlayerDataFile pdf = DeserializePlayerData(payload);
                if (pdf != null)
                {
                    PrepareForReconnectImport(pdf);
                    WriteVanilla(client, pdf);
                }

                SaveState(state);
            }
            catch (Exception exception)
            {
                LogError("could not restore previous profile after a failed switch: " + exception);
            }

            liveSwitches.Remove(ReadableId(client));
            lastSwitch[ReadableId(client)] = DateTime.UtcNow;

            string message = "The profile switch failed. Reconnecting to your previous profile.";
            timer.Once(1f, delegate
            {
                try
                {
                    IPlayer player = players.FindPlayerById(ReadableId(client));
                    if (player != null && player.IsConnected)
                    {
                        player.Kick(message + "\n\nReconnect to the server.");
                    }
                }
                catch
                {
                }
            });
        }

        private bool TrySwitch(
            IPlayer player,
            PlayerProfilesState state,
            PlayerProfileEntry entry,
            bool fromPanel,
            out string protocolPayload,
            out string error)
        {
            protocolPayload = null;
            error = null;

            if (player == null || entry == null)
            {
                error = "That profile does not exist.";
                return false;
            }

            if (player.IsServer)
            {
                error = "Profile switching must be started by a connected player.";
                return false;
            }

            ClientInfo client = GetClientInfo(player.Id);
            if (client == null)
            {
                error = "You are not connected.";
                return false;
            }

            if (string.Equals(entry.Id, state.ActiveProfileId, StringComparison.OrdinalIgnoreCase))
            {
                error = "That profile is already active.";
                return false;
            }

            DateTime switchedAt;
            if (lastSwitch.TryGetValue(player.Id, out switchedAt) &&
                (DateTime.UtcNow - switchedAt).TotalSeconds < config.SwitchCooldownSeconds)
            {
                error = "Please wait a moment before switching again.";
                return false;
            }

            EntityPlayer entity = GetEntity(client);
            if (entity != null)
            {
                if (config.DisallowWhileDead && entity.IsDead())
                {
                    error = "Respawn before switching profiles.";
                    return false;
                }

                if (config.DisallowWhileInVehicle && entity.AttachedToEntity != null)
                {
                    error = "Exit your vehicle or drone before switching profiles.";
                    return false;
                }
            }

            if (liveSwitches.ContainsKey(player.Id))
            {
                error = "A profile switch is already in progress.";
                return false;
            }

            if (state.PendingSwitch != null)
            {
                error = "A profile switch is already pending. Reconnect to finish it.";
                return false;
            }

            if (entry.HasData)
            {
                if (!IsProfileCompatible(entry, out error))
                {
                    return false;
                }

                byte[] payload = ReadProfileBytes(state.PlayerId, entry.Id);
                PlayerDataFile target = DeserializePlayerData(payload);
                if (target == null)
                {
                    error = "That profile's saved data is missing or unreadable. Restore a backup or delete it.";
                    return false;
                }

                if (target.bDead)
                {
                    error = "That profile is dead. Reconnect to respawn it.";
                    return false;
                }
            }

            bool canSwitchInPlace =
                config.AllowSeamlessSwitch &&
                entry.HasData &&
                seamlessClients.Contains(player.Id);

            if (canSwitchInPlace)
            {
                LiveSwitch live = new LiveSwitch
                {
                    Token = NewToken(),
                    FromProfileId = state.ActiveProfileId,
                    ToProfileId = entry.Id,
                    FromName = FindProfile(state, state.ActiveProfileId) != null
                        ? FindProfile(state, state.ActiveProfileId).Name
                        : "current",
                    ToName = entry.Name,
                    StartedUtc = DateTime.UtcNow,
                    ItemCount = entry.ItemCount,
                    ItemMapHash = entry.ItemMapHash
                };

                liveSwitches[player.Id] = live;
                state.PendingSwitch = new PlayerProfilesPendingSwitch
                {
                    Mode = "live",
                    FromProfileId = live.FromProfileId,
                    ToProfileId = live.ToProfileId,
                    Token = live.Token,
                    StartedUtc = IsoNow()
                };
                SaveState(state);
                lastSwitch[player.Id] = DateTime.UtcNow;

                protocolPayload = ProtocolPrefix + "prepare|" + live.Token + "|" +
                                  Encode(live.FromName) + "|" + Encode(live.ToName);

                if (!fromPanel)
                {
                    if (!SendRawResponse(client, protocolPayload))
                    {
                        liveSwitches.Remove(player.Id);
                        ClearPending(state);
                        SaveState(state);

                        if (!BeginReconnectSwitch(player, client, state, entry, out error))
                        {
                            return false;
                        }

                        protocolPayload = null;
                        error = "Instant switching is unavailable; reconnecting to load the profile.";
                        return true;
                    }

                    error = "Preparing to switch to '" + entry.Name + "'...";
                    protocolPayload = null;
                }

                return true;
            }

            if (!BeginReconnectSwitch(player, client, state, entry, out error))
            {
                return false;
            }

            lastSwitch[player.Id] = DateTime.UtcNow;
            return true;
        }

        private bool BeginReconnectSwitch(
            IPlayer player,
            ClientInfo client,
            PlayerProfilesState state,
            PlayerProfileEntry entry,
            out string error)
        {
            error = null;

            PlayerProfileEntry fromEntry = FindProfile(state, state.ActiveProfileId);
            string fromId = fromEntry != null ? fromEntry.Id : state.ActiveProfileId;
            bool targetFresh = !entry.HasData || ReadProfileBytes(state.PlayerId, entry.Id) == null;

            if (!CaptureLatestToProfile(client, state, fromId, "switch-reconnect"))
            {
                error = "Your current profile could not be saved, so the switch was cancelled.";
                return false;
            }

            state.PendingSwitch = new PlayerProfilesPendingSwitch
            {
                Mode = "reconnect",
                FromProfileId = fromId,
                ToProfileId = entry.Id,
                Token = NewToken(),
                TargetFresh = targetFresh,
                StartedUtc = IsoNow()
            };
            SaveState(state);

            string kickMessage = config.ReconnectMessage
                .Replace("{profile}", entry.Name ?? string.Empty)
                .Replace("{player}", player.Name ?? string.Empty);

            string playerId = player.Id;
            timer.Once(1f, delegate
            {
                try
                {
                    IPlayer reconnect = players.FindPlayerById(playerId);
                    if (reconnect != null && reconnect.IsConnected)
                    {
                        reconnect.Kick(kickMessage);
                    }
                }
                catch (Exception exception)
                {
                    LogWarning("kick for profile reconnect failed: " + exception.Message);
                }
            });

            error = targetFresh
                ? "Starting profile '" + entry.Name + "' requires one reconnect."
                : "Reconnecting to load profile '" + entry.Name + "'...";
            return true;
        }

        #endregion

        #region Protocol helpers

        private static bool SendRawResponse(ClientInfo client, string payload)
        {
            if (client == null || string.IsNullOrEmpty(payload))
            {
                return false;
            }

            try
            {
                Type responseType = FindRawResponseType();
                if (responseType == null)
                {
                    return false;
                }

                MethodInfo getPackage = typeof(NetPackageManager)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(method =>
                        method.Name == "GetPackage" &&
                        method.IsGenericMethodDefinition &&
                        method.GetParameters().Length == 0);

                if (getPackage == null)
                {
                    return false;
                }

                object package = getPackage.MakeGenericMethod(responseType).Invoke(null, null);
                MethodInfo setup = responseType.GetMethod(
                    "Setup", BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(string) }, null);

                if (setup == null)
                {
                    return false;
                }

                setup.Invoke(package, new object[] { payload });
                client.SendPackage((NetPackage)package);
                return true;
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[PlayerProfiles] raw response failed: " + exception.Message);
                return false;
            }
        }

        private static Type FindRawResponseType()
        {
            try
            {
                Type direct = Type.GetType(
                    "ServerAssistMod.NetPackageServerAssistResponse, ServerAssistMod", false);
                if (direct != null)
                {
                    return direct;
                }

                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    try
                    {
                        Type type = assemblies[i].GetType(
                            "ServerAssistMod.NetPackageServerAssistResponse", false);
                        if (type != null)
                        {
                            return type;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static string Sha256Hex(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(data);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        private static string SafeReason(string reason)
        {
            if (string.IsNullOrEmpty(reason))
            {
                return "switch cancelled";
            }

            string safe = reason.Replace("|", " ").Replace("\r", " ").Replace("\n", " ");
            return safe.Length > 160 ? safe.Substring(0, 160) : safe;
        }

        #endregion

        #region Rendering

        private string RenderList(IPlayer player, PlayerProfilesState state, UiSession session)
        {
            List<PlayerProfileEntry> profiles = GetProfileList(state);
            int slots = GetSlots(player.Id);
            int pages = Math.Max(1, (profiles.Count + PageSize - 1) / PageSize);
            session.Page = Clamp(session.Page, 0, pages - 1);

            string subtitle = profiles.Count + "/" + slots + " profiles";
            if (pages > 1)
            {
                subtitle += "  |  page " + (session.Page + 1) + "/" + pages;
            }

            PlayerProfileEntry active = FindProfile(state, state.ActiveProfileId);
            if (active != null)
            {
                subtitle += "  |  active: " + active.Name;
            }

            StringBuilder builder = BeginView("Profiles", subtitle, session.Status);
            int start = session.Page * PageSize;

            if (profiles.Count == 0)
            {
                AppendRow(builder, 0, string.Empty, "No profiles",
                    "Create a profile to get started.", false, RowIcon, "EMPTY", string.Empty);
            }
            else
            {
                for (int i = 0; i < PageSize && start + i < profiles.Count; i++)
                {
                    PlayerProfileEntry entry = profiles[start + i];
                    bool isActive = string.Equals(entry.Id, state.ActiveProfileId, StringComparison.OrdinalIgnoreCase);
                    AppendRow(builder, i, "select|" + entry.Id, entry.Name,
                        BuildProfileSub(entry, isActive), true, isActive ? ActiveRowIcon : RowIcon,
                        isActive ? "ACTIVE" : (entry.HasData ? string.Empty : "NEW"),
                        isActive ? "#FFCC66" : string.Empty);
                }
            }

            AppendInput(builder, string.Empty, false, string.Empty);

            bool canCreate = profiles.Count < slots;
            AppendButton(builder, 0, "new", "New", canCreate,
                canCreate ? "Create a new profile" : "All profile slots are in use", NewIcon, null);
            AppendButton(builder, 1, "refresh", "Refresh", true, "Reload the profile list");
            AppendButton(builder, 2, "save", "Save Current", true,
                "Save the active profile now", SaveIcon, null);
            AppendButton(builder, 3, "help", "Help", true, "Show profile help", HelpIcon, null);
            AppendButton(builder, 4, "page|first", "First", session.Page > 0, "First profile page");
            AppendButton(builder, 5, "page|prev", "Prev", session.Page > 0, "Previous profile page");
            AppendButton(builder, 6, "page|next", "Next", session.Page + 1 < pages, "Next profile page");
            AppendButton(builder, 7, "page|last", "Last", session.Page + 1 < pages, "Last profile page");
            AppendButton(builder, 8, "reload", "Reload",
                player.IsAdmin || permission.UserHasPermission(player.Id, config.AdminPermission),
                "Reload config and permissions", ReloadIcon, null);
            AppendButton(builder, 11, "hook|back", "Back", true, "Return to Server Assist");

            return builder.ToString();
        }

        private string RenderDetail(IPlayer player, PlayerProfilesState state, UiSession session)
        {
            PlayerProfileEntry entry = FindProfile(state, session.SelectedId);
            if (entry == null)
            {
                session.Mode = "list";
                session.Status = "That profile no longer exists.";
                return RenderList(player, state, session);
            }

            bool isActive = string.Equals(entry.Id, state.ActiveProfileId, StringComparison.OrdinalIgnoreCase);
            StringBuilder builder = BeginView(entry.Name,
                isActive ? "Active profile" : "Saved profile", session.Status);

            AppendRow(builder, 0, string.Empty, entry.Name, BuildProfileSub(entry, isActive),
                true, isActive ? ActiveRowIcon : RowIcon,
                isActive ? "ACTIVE" : (entry.HasData ? string.Empty : "NEW"),
                isActive ? "#FFCC66" : string.Empty);

            AppendInput(builder, string.Empty, false, string.Empty);
            AppendButton(builder, 0, "switch|" + entry.Id, "Switch", !isActive,
                "Load this profile now", SwitchIcon, null);
            AppendButton(builder, 1, "rename|" + entry.Id, "Rename", true,
                "Give this profile a new name", RenameIcon, null);
            AppendButton(builder, 2, "delete|" + entry.Id, "Delete", !isActive && state.Profiles.Count > 1,
                isActive ? "The active profile cannot be deleted" : "Delete this profile",
                DeleteIcon, null);
            AppendButton(builder, 10, "back", "Back", true, "Return to the profile list");
            AppendButton(builder, 11, "hook|back", "Server Assist", true, "Return to Server Assist");

            return builder.ToString();
        }

        private string RenderNew(IPlayer player, PlayerProfilesState state, UiSession session)
        {
            int slots = GetSlots(player.Id);
            StringBuilder builder = BeginView("New Profile",
                state.Profiles.Count + "/" + slots + " profiles used", session.Status);

            AppendRow(builder, 0, string.Empty, "New Profile",
                config.NewProfileStartsFresh
                    ? "Starts as a fresh character. Switching to it needs one reconnect."
                    : "Copies your current character into the new profile.",
                true, NewIcon, "NEW", string.Empty);

            AppendInput(builder, "Profile name (press Submit)", true, session.Input);
            AppendButton(builder, 0, "create", "Create", !string.IsNullOrEmpty(session.Input),
                "Create the profile using the submitted name", NewIcon, null);
            AppendButton(builder, 10, "back", "Cancel", true, "Return to the profile list");
            AppendButton(builder, 11, "hook|back", "Server Assist", true, "Return to Server Assist");

            return builder.ToString();
        }

        private string RenderRename(IPlayer player, PlayerProfilesState state, UiSession session)
        {
            PlayerProfileEntry entry = FindProfile(state, session.SelectedId);
            if (entry == null)
            {
                session.Mode = "list";
                session.Status = "That profile no longer exists.";
                return RenderList(player, state, session);
            }

            StringBuilder builder = BeginView("Rename Profile", entry.Name, session.Status);
            AppendRow(builder, 0, string.Empty, entry.Name, "Enter a new name below.",
                true, RenameIcon, string.Empty, string.Empty);

            AppendInput(builder, "New name (press Submit)", true, session.Input);
            AppendButton(builder, 0, "rename_save", "Save", !string.IsNullOrEmpty(session.Input),
                "Rename the profile using the submitted name", RenameIcon, null);
            AppendButton(builder, 10, "back", "Cancel", true, "Return to the profile list");
            AppendButton(builder, 11, "hook|back", "Server Assist", true, "Return to Server Assist");

            return builder.ToString();
        }

        private string RenderDelete(IPlayer player, PlayerProfilesState state, UiSession session)
        {
            PlayerProfileEntry entry = FindProfile(state, session.SelectedId);
            if (entry == null)
            {
                session.Mode = "list";
                session.Status = "That profile no longer exists.";
                return RenderList(player, state, session);
            }

            StringBuilder builder = BeginView("Delete Profile",
                entry.Name, session.Status);

            AppendRow(builder, 0, string.Empty, entry.Name,
                "This cannot be undone. The saved payload is archived first.",
                true, DeleteIcon, "DELETE", "#FF6666");

            AppendInput(builder, string.Empty, false, string.Empty);
            bool canDelete = state.Profiles.Count > 1 &&
                             !string.Equals(entry.Id, state.ActiveProfileId, StringComparison.OrdinalIgnoreCase);
            AppendButton(builder, 0, "delete_confirm", "Delete", canDelete,
                canDelete ? "Permanently delete this profile" : "The active or only profile cannot be deleted",
                DeleteIcon, null);
            AppendButton(builder, 10, "back", "Cancel", true, "Keep this profile");
            AppendButton(builder, 11, "hook|back", "Server Assist", true, "Return to Server Assist");

            return builder.ToString();
        }

        private string RenderSwitch(IPlayer player, PlayerProfilesState state, UiSession session)
        {
            PlayerProfileEntry entry = FindProfile(state, session.SelectedId);
            if (entry == null)
            {
                session.Mode = "list";
                session.Status = "That profile no longer exists.";
                return RenderList(player, state, session);
            }

            bool capable = config.AllowSeamlessSwitch && seamlessClients.Contains(player.Id);
            string note;
            if (!entry.HasData)
            {
                note = "This profile has not been started. Switching reconnects once to create it.";
            }
            else if (capable)
            {
                note = "Inventory, quests, level and position change in place.";
            }
            else
            {
                note = "The updated Server Assist client mod is not available; this switch reconnects.";
            }

            StringBuilder builder = BeginView("Switch Profile", entry.Name, session.Status);
            AppendRow(builder, 0, string.Empty, entry.Name, note, true, SwitchIcon,
                entry.HasData ? string.Empty : "NEW", "#FFCC66");

            AppendInput(builder, string.Empty, false, string.Empty);
            AppendButton(builder, 0, "switch_confirm|" + entry.Id, "Switch Now", true,
                "Load this profile", SwitchIcon, null);
            AppendButton(builder, 10, "back", "Cancel", true, "Return to the profile list");
            AppendButton(builder, 11, "hook|back", "Server Assist", true, "Return to Server Assist");

            return builder.ToString();
        }

        private string RenderHelp(IPlayer player, PlayerProfilesState state, UiSession session)
        {
            StringBuilder builder = BeginView("Profile Help",
                "World storage, land claims and other plugins are shared.", session.Status);

            AppendRow(builder, 0, string.Empty, "What switches",
                "Inventory, backpack, equipment, level, perks, quests, buffs, waypoints and position.",
                false, HelpIcon, string.Empty, string.Empty);
            AppendRow(builder, 1, string.Empty, "What does not switch",
                "Oxide permissions, groups, land claims, allies, party, and data stored by other plugins.",
                false, HelpIcon, string.Empty, string.Empty);
            AppendRow(builder, 2, string.Empty, "Fresh profiles",
                "A brand-new profile needs one reconnect so the game can run its normal new-character spawn.",
                false, HelpIcon, string.Empty, string.Empty);
            AppendRow(builder, 3, string.Empty, "Slots",
                "Your limit comes from " + config.PermissionPrefix + ".slots.N. Existing profiles remain readable.",
                false, HelpIcon, string.Empty, string.Empty);
            AppendRow(builder, 4, string.Empty, "Commands",
                "/profile use <name>, /profile new <name>, /profile rename <old> <new>, /profile delete <name> confirm",
                false, HelpIcon, string.Empty, string.Empty);

            AppendInput(builder, string.Empty, false, string.Empty);
            AppendButton(builder, 10, "back", "Back", true, "Return to the profile list");
            AppendButton(builder, 11, "hook|back", "Server Assist", true, "Return to Server Assist");
            return builder.ToString();
        }

        private string RenderError(string message)
        {
            StringBuilder builder = BeginView("Profiles", "Profiles unavailable", message);
            AppendRow(builder, 0, string.Empty, "Profiles", message, false, HelpIcon, "ERROR", "#FF6666");
            AppendInput(builder, string.Empty, false, string.Empty);
            AppendButton(builder, 11, "hook|back", "Back", true, "Return to Server Assist");
            return builder.ToString();
        }

        private static StringBuilder BeginView(string title, string subtitle, string status)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("v2\n");
            builder.Append("protocol=2\n");
            builder.Append("title=").Append(Encode(title)).Append('\n');
            builder.Append("subtitle=").Append(Encode(subtitle)).Append('\n');
            builder.Append("status=").Append(Encode(status ?? string.Empty)).Append('\n');
            builder.Append("chrome=normal\n");
            builder.Append("background=").Append(PanelIcon).Append('\n');
            builder.Append("banner=").Append(PanelIcon).Append('\n');
            return builder;
        }

        private static void AppendRow(
            StringBuilder builder, int index, string action, string label, string sub,
            bool enabled, string icon, string badge, string tint)
        {
            builder.Append("row=").Append(index).Append('|')
                .Append(Encode(action)).Append('|')
                .Append(Encode(label)).Append('|')
                .Append(Encode(sub)).Append('|')
                .Append(enabled ? "1" : "0").Append('|')
                .Append("0").Append('|')
                .Append(Encode(icon)).Append('|')
                .Append(Encode(badge)).Append('|')
                .Append(Encode(tint)).Append('\n');
        }

        private static void AppendButton(
            StringBuilder builder, int index, string action, string label, bool enabled, string tooltip)
        {
            AppendButton(builder, index, action, label, enabled, tooltip, null, null);
        }

        private static void AppendButton(
            StringBuilder builder,
            int index,
            string action,
            string label,
            bool enabled,
            string tooltip,
            string icon,
            string tint)
        {
            builder.Append("btn=").Append(index).Append('|')
                .Append(Encode(action)).Append('|')
                .Append(Encode(label)).Append('|')
                .Append(enabled ? "1" : "0").Append('|')
                .Append(Encode(tooltip));

            if (!string.IsNullOrEmpty(icon) || !string.IsNullOrEmpty(tint))
            {
                builder.Append('|').Append(Encode(icon));
                builder.Append('|').Append(Encode(tint));
            }

            builder.Append('\n');
        }

        private static void AppendInput(StringBuilder builder, string label, bool visible, string value)
        {
            if (!visible)
            {
                builder.Append("input=||0|\n");
                return;
            }

            builder.Append("input=").Append(Encode(label)).Append("|1|")
                .Append(Encode(value ?? string.Empty)).Append('\n');
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

        private static void ChangePage(UiSession session, string action, int count)
        {
            int pages = Math.Max(1, (count + PageSize - 1) / PageSize);
            string verb = (action ?? string.Empty).Trim().ToLowerInvariant();

            switch (verb)
            {
                case "first":
                    session.Page = 0;
                    break;
                case "last":
                    session.Page = pages - 1;
                    break;
                case "prev":
                case "-10":
                    session.Page -= verb == "-10" ? 10 : 1;
                    break;
                case "next":
                case "+10":
                    session.Page += verb == "+10" ? 10 : 1;
                    break;
                default:
                    int absolute;
                    if (int.TryParse(verb, out absolute))
                    {
                        session.Page = absolute;
                    }
                    break;
            }

            session.Page = Clamp(session.Page, 0, pages - 1);
        }

        private string BuildProfileSub(PlayerProfileEntry entry, bool isActive)
        {
            if (!entry.HasData)
            {
                return "Not started yet" + (isActive ? "  |  active" : string.Empty);
            }

            List<string> parts = new List<string>();
            parts.Add("Lv " + entry.Level);
            if (entry.PlaytimeMinutes > 0.5f)
            {
                parts.Add(FormatPlaytime(entry.PlaytimeMinutes));
            }
            if (!string.IsNullOrEmpty(entry.LastUsedUtc))
            {
                parts.Add("last used " + RelativeTime(entry.LastUsedUtc));
            }
            if (isActive)
            {
                parts.Add("active");
            }
            return string.Join("  |  ", parts.ToArray());
        }

        #endregion

        #region Commands

        private void CommandProfiles(IPlayer player, string command, string[] args)
        {
            if (player == null)
            {
                return;
            }

            if (!CanUse(player))
            {
                Reply(player, "You do not have permission to use profiles.");
                return;
            }

            if (args == null || args.Length == 0)
            {
                if (TryOpenPanel(player))
                {
                    return;
                }

                CommandList(player);
                return;
            }

            string verb = (args[0] ?? string.Empty).Trim().ToLowerInvariant();
            switch (verb)
            {
                case "list":
                    CommandList(player);
                    return;

                case "use":
                case "switch":
                    CommandSwitch(player, args);
                    return;

                case "new":
                case "create":
                    CommandNew(player, args);
                    return;

                case "rename":
                    CommandRename(player, args);
                    return;

                case "delete":
                case "remove":
                    CommandDelete(player, args);
                    return;

                case "save":
                    CommandSave(player);
                    return;

                case "reload":
                    CommandReload(player);
                    return;

                case "help":
                default:
                    Reply(player, "Commands: /profile list | use <name> | new <name> | rename <old> <new> | delete <name> confirm | save | reload");
                    return;
            }
        }

        private void CommandList(IPlayer player)
        {
            PlayerProfilesState state = GetState(player.Id, true);
            EnsureBootstrapProfile(player, state);
            if (state == null || state.Profiles.Count == 0)
            {
                Reply(player, "You have no profiles yet.");
                return;
            }

            int slots = GetSlots(player.Id);
            Reply(player, "Profiles (" + state.Profiles.Count + "/" + slots + "):");
            for (int i = 0; i < state.Profiles.Count; i++)
            {
                PlayerProfileEntry entry = state.Profiles[i];
                string marker = string.Equals(entry.Id, state.ActiveProfileId, StringComparison.OrdinalIgnoreCase)
                    ? " *"
                    : string.Empty;
                Reply(player, "  " + entry.Name + marker +
                             (entry.HasData ? "  (Lv " + entry.Level + ")" : "  (not started)"));
            }
        }

        private void CommandSwitch(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /profile use <name>");
                return;
            }

            PlayerProfilesState state = GetState(player.Id, true);
            EnsureBootstrapProfile(player, state);
            string error;
            PlayerProfileEntry entry = FindProfileByName(state, JoinArgs(args, 1), out error);
            if (entry == null)
            {
                Reply(player, error);
                return;
            }

            string protocolPayload;
            if (!TrySwitch(player, state, entry, false, out protocolPayload, out error))
            {
                Reply(player, error);
                return;
            }

            Reply(player, error);
        }

        private void CommandNew(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /profile new <name>");
                return;
            }

            PlayerProfilesState state = GetState(player.Id, true);
            EnsureBootstrapProfile(player, state);
            UiSession session = GetUiSession(player.Id);
            CreateProfile(player, state, session, JoinArgs(args, 1));
            Reply(player, session.Status);
        }

        private void CommandRename(IPlayer player, string[] args)
        {
            if (args.Length < 3)
            {
                Reply(player, "Usage: /profile rename <old> <new>");
                return;
            }

            PlayerProfilesState state = GetState(player.Id, true);
            EnsureBootstrapProfile(player, state);

            string error;
            string oldName = args[1];
            PlayerProfileEntry entry = FindProfileByName(state, oldName, out error);
            if (entry == null)
            {
                Reply(player, error);
                return;
            }

            UiSession session = GetUiSession(player.Id);
            session.SelectedId = entry.Id;
            RenameProfile(player, state, session, JoinArgs(args, 2));
            Reply(player, session.Status);
        }

        private void CommandDelete(IPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Reply(player, "Usage: /profile delete <name> confirm");
                return;
            }

            PlayerProfilesState state = GetState(player.Id, true);
            EnsureBootstrapProfile(player, state);

            string error;
            PlayerProfileEntry entry = FindProfileByName(state, args[1], out error);
            if (entry == null)
            {
                Reply(player, error);
                return;
            }

            bool confirmed = args.Length > 2 &&
                             string.Equals(args[2], "confirm", StringComparison.OrdinalIgnoreCase);
            if (!confirmed)
            {
                Reply(player, "This deletes '" + entry.Name + "' permanently. Run /profile delete " +
                              entry.Name + " confirm");
                return;
            }

            UiSession session = GetUiSession(player.Id);
            session.SelectedId = entry.Id;
            DeleteProfile(player, state, session);
            Reply(player, session.Status);
        }

        private void CommandSave(IPlayer player)
        {
            PlayerProfilesState state = GetState(player.Id, true);
            EnsureBootstrapProfile(player, state);
            if (CaptureActive(player, state))
            {
                Reply(player, "Profile saved.");
            }
            else
            {
                Reply(player, "Profile could not be saved.");
            }
        }

        private void CommandReload(IPlayer player)
        {
            if (!player.IsServer &&
                !player.IsAdmin &&
                !permission.UserHasPermission(player.Id, config.AdminPermission))
            {
                Reply(player, "You do not have permission to reload profiles.");
                return;
            }

            CaptureOnline(false);
            LoadConfig();
            RegisterPermissions();
            EnsureDefaultPermissions();
            slotsCache.Clear();
            states.Clear();
            Reply(player, "PlayerProfiles configuration and permissions reloaded.");
        }

        private bool TryOpenPanel(IPlayer player)
        {
            try
            {
                Plugin assistant = plugins.Find("ServerAssist");
                if (assistant == null)
                {
                    return false;
                }

                object result = assistant.Call("OpenPanel", player.Id, PanelId, string.Empty);
                return result is bool && (bool)result;
            }
            catch
            {
                return false;
            }
        }

        private static string JoinArgs(string[] args, int start)
        {
            if (args == null || start >= args.Length)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            for (int i = start; i < args.Length; i++)
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }
                builder.Append(args[i]);
            }
            return builder.ToString();
        }

        #endregion

        #region State / profiles

        private PlayerProfilesState EnsureState(ClientInfo clientInfo)
        {
            string playerId = ReadableId(clientInfo);
            if (string.IsNullOrEmpty(playerId))
            {
                return null;
            }

            PlayerProfilesState state = GetState(playerId, true);
            if (state == null)
            {
                return null;
            }

            state.PlayerId = playerId;
            state.PlayerName = clientInfo.playerName ?? state.PlayerName;

            if (state.Profiles == null)
            {
                state.Profiles = new List<PlayerProfileEntry>();
            }

            if (state.Profiles.Count == 0)
            {
                PlayerProfileEntry entry = new PlayerProfileEntry
                {
                    Id = NewProfileId(state),
                    Name = UniqueName(state, config.BootstrapProfileName, null),
                    CreatedUtc = IsoNow(),
                    LastUsedUtc = IsoNow(),
                    HasData = File.Exists(VanillaPath(clientInfo)),
                    Level = GetLevel(clientInfo),
                    Order = 0
                };
                state.Profiles.Add(entry);
                state.ActiveProfileId = entry.Id;
                SaveState(state);
                LogInfo("created bootstrap profile '" + entry.Name + "' for " + state.PlayerName);
            }

            if (FindProfile(state, state.ActiveProfileId) == null)
            {
                state.ActiveProfileId = state.Profiles[0].Id;
                SaveState(state);
            }

            return state;
        }

        private void EnsureBootstrapProfile(IPlayer player, PlayerProfilesState state)
        {
            if (state == null || player == null)
            {
                return;
            }

            if (state.Profiles == null)
            {
                state.Profiles = new List<PlayerProfileEntry>();
            }

            if (state.Profiles.Count == 0)
            {
                ClientInfo client = GetClientInfo(player.Id);
                PlayerProfileEntry entry = new PlayerProfileEntry
                {
                    Id = NewProfileId(state),
                    Name = UniqueName(state, config.BootstrapProfileName, null),
                    CreatedUtc = IsoNow(),
                    LastUsedUtc = IsoNow(),
                    HasData = client != null && File.Exists(VanillaPath(client)),
                    Level = client != null ? GetLevel(client) : 1,
                    Order = 0
                };
                state.Profiles.Add(entry);
                state.ActiveProfileId = entry.Id;
                SaveState(state);
            }

            if (FindProfile(state, state.ActiveProfileId) == null)
            {
                state.ActiveProfileId = state.Profiles[0].Id;
                SaveState(state);
            }
        }

        private PlayerProfilesState GetState(string playerId, bool create)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return null;
            }

            PlayerProfilesState state;
            if (states.TryGetValue(playerId, out state) && state != null)
            {
                return state;
            }

            try
            {
                state = Interface.Oxide.DataFileSystem.ReadObject<PlayerProfilesState>(DataKey(playerId));
            }
            catch (Exception exception)
            {
                LogWarning("state load failed for " + playerId + ": " + exception.Message);
                state = null;
            }

            if (state == null && create)
            {
                state = new PlayerProfilesState
                {
                    SchemaVersion = 1,
                    PlayerId = playerId,
                    Profiles = new List<PlayerProfileEntry>(),
                    CreatedUtc = IsoNow()
                };
            }

            if (state == null)
            {
                return null;
            }

            state.PlayerId = playerId;
            if (state.Profiles == null)
            {
                state.Profiles = new List<PlayerProfileEntry>();
            }

            state.Profiles.RemoveAll(entry => entry == null || string.IsNullOrEmpty(entry.Id));

            states[playerId] = state;
            return state;
        }

        private void SaveState(PlayerProfilesState state)
        {
            if (state == null || string.IsNullOrEmpty(state.PlayerId))
            {
                return;
            }

            try
            {
                state.LastSeenUtc = IsoNow();
                Interface.Oxide.DataFileSystem.WriteObject(DataKey(state.PlayerId), state, true);
            }
            catch (Exception exception)
            {
                LogError("state save failed for " + state.PlayerId + ": " + exception);
            }
        }

        private static List<PlayerProfileEntry> GetProfileList(PlayerProfilesState state)
        {
            if (state == null || state.Profiles == null)
            {
                return new List<PlayerProfileEntry>();
            }

            return state.Profiles
                .Where(entry => entry != null)
                .OrderBy(entry => entry.Order)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static PlayerProfileEntry FindProfile(PlayerProfilesState state, string profileId)
        {
            if (state == null || state.Profiles == null || string.IsNullOrEmpty(profileId))
            {
                return null;
            }

            return state.Profiles.FirstOrDefault(entry =>
                entry != null &&
                string.Equals(entry.Id, profileId, StringComparison.OrdinalIgnoreCase));
        }

        private PlayerProfileEntry FindProfileByName(
            PlayerProfilesState state, string name, out string error)
        {
            error = null;
            if (state == null || state.Profiles == null || string.IsNullOrEmpty(name))
            {
                error = "Usage: /profile use <name>";
                return null;
            }

            string wanted = name.Trim();
            PlayerProfileEntry exact = state.Profiles.FirstOrDefault(entry =>
                entry != null &&
                string.Equals(entry.Name, wanted, StringComparison.OrdinalIgnoreCase));

            if (exact != null)
            {
                return exact;
            }

            List<PlayerProfileEntry> matches = state.Profiles
                .Where(entry => entry != null &&
                                entry.Name != null &&
                                entry.Name.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            if (matches.Count == 1)
            {
                return matches[0];
            }

            if (matches.Count == 0)
            {
                error = "No profile matches '" + wanted + "'.";
            }
            else
            {
                error = "That name matches more than one profile. Use the exact name.";
            }

            return null;
        }

        private bool TryNormaliseName(
            PlayerProfilesState state, string input, string ignoreProfileId, out string name, out string error)
        {
            name = (input ?? string.Empty).Trim();
            error = null;

            while (name.Contains("  "))
            {
                name = name.Replace("  ", " ");
            }

            if (name.Length == 0)
            {
                error = "Enter a profile name.";
                return false;
            }

            if (name.Length > config.MaxNameLength)
            {
                error = "Profile names may be at most " + config.MaxNameLength + " characters.";
                return false;
            }

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsLetterOrDigit(c) ||
                    c == ' ' || c == '_' || c == '-' || c == '.' || c == '\'')
                {
                    continue;
                }

                error = "Profile names may only contain letters, numbers, spaces, _ - . and '.";
                return false;
            }

            string candidate = name;
            PlayerProfileEntry duplicate = state.Profiles.FirstOrDefault(entry =>
                entry != null &&
                !string.Equals(entry.Id, ignoreProfileId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.Name, candidate, StringComparison.OrdinalIgnoreCase));

            if (duplicate != null)
            {
                error = "A profile named '" + name + "' already exists.";
                return false;
            }

            return true;
        }

        private static string UniqueName(PlayerProfilesState state, string baseName, string ignoreProfileId)
        {
            string name = string.IsNullOrEmpty(baseName) ? "Profile" : baseName.Trim();
            string candidate = name;
            int suffix = 2;

            while (state.Profiles.Any(entry =>
                entry != null &&
                !string.Equals(entry.Id, ignoreProfileId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                candidate = name + " " + suffix;
                suffix++;
            }

            return candidate;
        }

        private static string NewProfileId(PlayerProfilesState state)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                string id = Guid.NewGuid().ToString("N").Substring(0, 8);
                if (state == null || state.Profiles == null ||
                    !state.Profiles.Any(entry => entry != null &&
                        string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase)))
                {
                    return id;
                }
            }

            return Guid.NewGuid().ToString("N");
        }

        private static int NextOrder(PlayerProfilesState state)
        {
            if (state == null || state.Profiles == null || state.Profiles.Count == 0)
            {
                return 0;
            }

            return state.Profiles.Max(entry => entry != null ? entry.Order : 0) + 1;
        }

        private static void ClearPending(PlayerProfilesState state)
        {
            if (state != null)
            {
                state.PendingSwitch = null;
            }
        }

        private void UpdateEntryFromPdf(
            PlayerProfileEntry entry, PlayerDataFile pdf, EntityPlayer entity, int bytes)
        {
            if (entry == null || pdf == null)
            {
                return;
            }

            entry.HasData = true;
            entry.Bytes = bytes;
            entry.Deaths = pdf.deaths;
            entry.PlaytimeMinutes = pdf.totalTimePlayed;
            entry.SavedAtUtc = IsoNow();
            entry.GameVersion = gameVersion;
            entry.ModHash = modHash;
            UpdateItemMapMetadata(entry);

            // The server entity's progression can lag behind the client's
            // latest full sync. The captured progression blob is authoritative.
            int pdfLevel = GetLevelFromPdf(pdf);
            if (pdfLevel > 0)
            {
                entry.Level = pdfLevel;
            }
            else if (entity != null && entity.Progression != null)
            {
                entry.Level = entity.Progression.Level;
            }
            else if (entry.Level <= 0)
            {
                entry.Level = 1;
            }

            if (string.IsNullOrEmpty(entry.CreatedUtc))
            {
                entry.CreatedUtc = IsoNow();
            }
        }

        private static void UpdateItemMapMetadata(PlayerProfileEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            int count;
            string hash;
            if (!TryGetItemMap(out count, out hash))
            {
                return;
            }

            entry.ItemCount = count;
            entry.ItemMapHash = hash;
        }

        private static bool TryGetItemMap(out int count, out string hash)
        {
            count = 0;
            hash = null;

            try
            {
                ItemClass[] items = ItemClass.list;
                if (items == null || items.Length == 0)
                {
                    return false;
                }

                count = items.Length;
                if (cachedItemCount == count && !string.IsNullOrEmpty(cachedItemHash))
                {
                    hash = cachedItemHash;
                    return true;
                }

                if (cachedItemCount != 0 && cachedItemCount != count)
                {
                    itemPrefixCache.Clear();
                }

                hash = HashItemPrefixUncached(count);
                if (string.IsNullOrEmpty(hash))
                {
                    return false;
                }

                cachedItemCount = count;
                cachedItemHash = hash;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string HashItemPrefix(int count)
        {
            try
            {
                ItemClass[] items = ItemClass.list;
                if (items == null || count <= 0)
                {
                    return string.Empty;
                }

                if (cachedItemCount == items.Length &&
                    count == cachedItemCount &&
                    !string.IsNullOrEmpty(cachedItemHash))
                {
                    return cachedItemHash;
                }

                if (cachedItemCount != 0 && cachedItemCount != items.Length)
                {
                    itemPrefixCache.Clear();
                }

                string cached;
                if (itemPrefixCache.TryGetValue(count, out cached))
                {
                    return cached;
                }

                string hash = HashItemPrefixUncached(count);
                if (!string.IsNullOrEmpty(hash))
                {
                    itemPrefixCache[count] = hash;
                }

                return hash;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string HashItemPrefixUncached(int count)
        {
            try
            {
                ItemClass[] items = ItemClass.list;
                if (items == null || count <= 0)
                {
                    return string.Empty;
                }

                unchecked
                {
                    const ulong offset = 14695981039346656037UL;
                    const ulong prime = 1099511628211UL;

                    ulong hash = offset;
                    int limit = Math.Min(count, items.Length);
                    for (int i = 0; i < limit; i++)
                    {
                        ItemClass item = items[i];
                        string name = item != null ? item.GetItemName() : string.Empty;
                        byte[] bytes = Encoding.UTF8.GetBytes(name ?? string.Empty);
                        for (int j = 0; j < bytes.Length; j++)
                        {
                            hash ^= bytes[j];
                            hash *= prime;
                        }

                        // Separator so ["ab","c"] and ["a","bc"] differ.
                        hash ^= 0xFF;
                        hash *= prime;
                    }

                    return hash.ToString("x16");
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool ItemMapPrefixMatches(string expectedHash, int expectedCount)
        {
            if (expectedCount <= 0 || string.IsNullOrEmpty(expectedHash))
            {
                return true;
            }

            ItemClass[] items = ItemClass.list;
            if (items == null || items.Length < expectedCount)
            {
                return false;
            }

            string current = HashItemPrefix(expectedCount);
            return !string.IsNullOrEmpty(current) &&
                   string.Equals(current, expectedHash, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A profile created before item-map metadata existed cannot be checked.
        /// Additive mode allows it once, then the next capture records the map.
        /// Strict mode still compares the old mod hash.
        /// </summary>
        private bool IsProfileCompatible(PlayerProfileEntry entry, out string error)
        {
            error = null;

            if (entry == null)
            {
                error = "That profile does not exist.";
                return false;
            }

            if (config.RequireSameGameVersion &&
                !string.IsNullOrEmpty(entry.GameVersion) &&
                !string.Equals(entry.GameVersion, gameVersion, StringComparison.OrdinalIgnoreCase))
            {
                error = "That profile was saved on game " + entry.GameVersion +
                        " and this server is on " + gameVersion + ".";
                return false;
            }

            string mode = config.ModCompatibility ?? "Additive";
            if (string.Equals(mode, "Ignore", StringComparison.OrdinalIgnoreCase))
            {
                LogDebug("mod compatibility is Ignore for '" + entry.Name + "'");
                return true;
            }

            if (string.Equals(mode, "Strict", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(entry.ModHash) &&
                    !string.Equals(entry.ModHash, modHash, StringComparison.OrdinalIgnoreCase))
                {
                    error = "That profile was saved with a different mod set. Restore the exact mods, or set ModCompatibility to Additive or Ignore.";
                    return false;
                }

                return true;
            }

            // Additive: existing item IDs must still line up. New items may be
            // appended by a new mod without a wipe.
            if (entry.ItemCount <= 0 || string.IsNullOrEmpty(entry.ItemMapHash))
            {
                LogWarning("legacy profile '" + entry.Name + "' has no item-map metadata; allowing it once. " +
                           "The next save will record the current item layout.");
                return true;
            }

            if (!ItemMapPrefixMatches(entry.ItemMapHash, entry.ItemCount))
            {
                error = "The current mods change existing item IDs, not just add new items. " +
                        "Reorder the new item mod so it loads after the existing item mods, " +
                        "or set ModCompatibility to Ignore to force the switch (back up the profiles first).";
                return false;
            }

            LogDebug("profile '" + entry.Name + "' is item-map compatible");
            return true;
        }

        private static int GetLevelFromPdf(PlayerDataFile pdf)
        {
            try
            {
                if (pdf == null || pdf.progressionData == null || pdf.progressionData.Length < 3)
                {
                    return 0;
                }

                byte[] data = pdf.progressionData.ToArray();
                if (data.Length < 3)
                {
                    return 0;
                }

                return data[1] | (data[2] << 8);
            }
            catch
            {
                return 0;
            }
        }

        private void UpdateLevel(PlayerProfileEntry entry, ClientInfo clientInfo)
        {
            if (entry == null)
            {
                return;
            }

            EntityPlayer entity = GetEntity(clientInfo);
            if (entity != null && entity.Progression != null)
            {
                entry.Level = entity.Progression.Level;
            }
        }

        #endregion

        #region Files

        private bool WriteProfileFile(string playerId, string profileId, byte[] payload)
        {
            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(profileId) ||
                payload == null || payload.Length < 16)
            {
                return false;
            }

            string path = ProfilePath(playerId, profileId);
            return WriteAtomic(path, payload, config.BackupCount > 0);
        }

        private byte[] ReadProfileBytes(string playerId, string profileId)
        {
            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(profileId))
            {
                return null;
            }

            string path = ProfilePath(playerId, profileId);
            string[] candidates = new[]
            {
                path,
                path + ".bak1",
                path + ".bak2"
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    if (!File.Exists(candidates[i]))
                    {
                        continue;
                    }

                    byte[] payload = File.ReadAllBytes(candidates[i]);
                    if (payload != null && payload.Length >= 16 &&
                        DeserializePlayerData(payload) != null)
                    {
                        return payload;
                    }
                }
                catch (Exception exception)
                {
                    LogWarning("profile read failed (" + candidates[i] + "): " + exception.Message);
                }
            }

            return null;
        }

        private static bool WriteAtomic(string path, byte[] payload, bool backup)
        {
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (backup && File.Exists(path))
                {
                    string bak1 = path + ".bak1";
                    string bak2 = path + ".bak2";
                    if (File.Exists(bak2))
                    {
                        File.Delete(bak2);
                    }
                    if (File.Exists(bak1))
                    {
                        File.Move(bak1, bak2);
                    }
                    File.Copy(path, bak1, true);
                }

                string temp = path + ".tmp";
                File.WriteAllBytes(temp, payload);
                File.Copy(temp, path, true);
                File.Delete(temp);
                return true;
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogError("[PlayerProfiles] atomic write failed for " + path + ": " + exception);
                return false;
            }
        }

        private bool WriteVanilla(ClientInfo clientInfo, PlayerDataFile pdf)
        {
            if (clientInfo == null || pdf == null)
            {
                return false;
            }

            byte[] payload = SerializePlayerData(pdf);
            if (payload == null)
            {
                return false;
            }

            return WriteAtomic(VanillaPath(clientInfo), payload, false);
        }

        private static void DeleteVanilla(ClientInfo clientInfo)
        {
            string path = VanillaPath(clientInfo);
            string[] files = new[]
            {
                path,
                path + ".tmp",
                path + ".bak",
                path + ".meta"
            };

            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    if (File.Exists(files[i]))
                    {
                        File.Delete(files[i]);
                    }
                }
                catch (Exception exception)
                {
                    Interface.Oxide.LogWarning("[PlayerProfiles] could not delete " + files[i] + ": " + exception.Message);
                }
            }
        }

        private void ArchiveProfile(string playerId, string profileId)
        {
            string source = ProfilePath(playerId, profileId);
            string[] files = new[] { source, source + ".bak1", source + ".bak2" };

            if (config.ArchiveDeletedProfiles)
            {
                try
                {
                    string archiveDir = Path.Combine(PlayerDir(playerId), "deleted");
                    Directory.CreateDirectory(archiveDir);
                    string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

                    for (int i = 0; i < files.Length; i++)
                    {
                        if (!File.Exists(files[i]))
                        {
                            continue;
                        }

                        string suffix = i == 0 ? string.Empty : ".bak" + i;
                        string target = Path.Combine(archiveDir,
                            stamp + "-" + profileId + suffix + ".ttp");
                        File.Copy(files[i], target, true);
                    }
                }
                catch (Exception exception)
                {
                    LogWarning("profile archive failed: " + exception.Message);
                }
            }

            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    if (File.Exists(files[i]))
                    {
                        File.Delete(files[i]);
                    }
                }
                catch (Exception exception)
                {
                    LogWarning("could not delete " + files[i] + ": " + exception.Message);
                }
            }
        }

        private static string DataKey(string playerId)
        {
            return DataRoot + "/" + SafeKey(playerId) + "/state";
        }

        private static string PlayerDir(string playerId)
        {
            return Path.Combine(Interface.Oxide.DataDirectory, DataRoot, SafeKey(playerId));
        }

        private static string ProfilePath(string playerId, string profileId)
        {
            return Path.Combine(PlayerDir(playerId), SafeProfileId(profileId) + ".ttp");
        }

        private static string VanillaPath(ClientInfo clientInfo)
        {
            string directory = GameIO.GetPlayerDataDir();
            string name = null;

            if (clientInfo != null)
            {
                if (clientInfo.InternalId != null &&
                    !string.IsNullOrEmpty(clientInfo.InternalId.CombinedString))
                {
                    name = clientInfo.InternalId.CombinedString;
                }
                else
                {
                    name = ReadableId(clientInfo);
                }
            }

            if (string.IsNullOrEmpty(name))
            {
                name = "unknown";
            }

            return Path.Combine(directory ?? string.Empty, name + ".ttp");
        }

        private static string SafeKey(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return "unknown";
            }

            StringBuilder builder = new StringBuilder(playerId.Length);
            for (int i = 0; i < playerId.Length; i++)
            {
                char c = playerId[i];
                if (char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-')
                {
                    builder.Append(c);
                }
                else
                {
                    builder.Append('_');
                }
            }

            string key = builder.ToString();
            if (key.Length == 0)
            {
                key = "unknown";
            }

            if (!string.Equals(key, playerId, StringComparison.Ordinal))
            {
                key += "-" + Fnv1aHex(playerId);
            }

            return key;
        }

        private static string SafeProfileId(string profileId)
        {
            if (string.IsNullOrEmpty(profileId))
            {
                return "unknown";
            }

            StringBuilder builder = new StringBuilder(profileId.Length);
            for (int i = 0; i < profileId.Length; i++)
            {
                char c = profileId[i];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
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

        #endregion

        #region Binary format

        private static byte[] SerializePlayerData(PlayerDataFile pdf)
        {
            if (pdf == null)
            {
                return null;
            }

            try
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    PooledBinaryWriter writer = new PooledBinaryWriter();
                    try
                    {
                        writer.SetBaseStream(stream);
                        writer.Write('t');
                        writer.Write('t');
                        writer.Write('p');
                        writer.Write((byte)0);
                        writer.Write((byte)PlayerDataFile.cFileVersion);
                        pdf.Write(writer, StreamModeWrite.Persistency);
                        writer.Flush();
                    }
                    finally
                    {
                        writer.SetBaseStream(null);
                    }

                    return stream.ToArray();
                }
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogError("[PlayerProfiles] serialise failed: " + exception);
                return null;
            }
        }

        private static PlayerDataFile DeserializePlayerData(byte[] payload)
        {
            if (payload == null || payload.Length < 5)
            {
                return null;
            }

            try
            {
                using (MemoryStream stream = new MemoryStream(payload, false))
                {
                    PooledBinaryReader reader = new PooledBinaryReader();
                    try
                    {
                        reader.SetBaseStream(stream);
                        char a = reader.ReadChar();
                        char b = reader.ReadChar();
                        char c = reader.ReadChar();
                        char d = reader.ReadChar();
                        if (a != 't' || b != 't' || c != 'p' || d != '\0')
                        {
                            return null;
                        }

                        uint version = reader.ReadByte();
                        if (version < 38)
                        {
                            return null;
                        }

                        PlayerDataFile pdf = new PlayerDataFile();
                        pdf.Read(reader, version, StreamModeRead.Persistency);

                        if (pdf.inventoryData == null || pdf.inventoryData.Length == 0 ||
                            pdf.bagData == null || pdf.bagData.Length == 0 ||
                            pdf.equipmentData == null || pdf.equipmentData.Length == 0 ||
                            pdf.progressionData == null || pdf.progressionData.Length == 0 ||
                            pdf.buffData == null || pdf.buffData.Length == 0 ||
                            pdf.stealthData == null || pdf.stealthData.Length == 0)
                        {
                            return null;
                        }

                        pdf.bLoaded = true;
                        return pdf;
                    }
                    finally
                    {
                        reader.SetBaseStream(null);
                    }
                }
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[PlayerProfiles] parse failed: " + exception.Message);
                return null;
            }
        }

        #endregion

        #region Configuration / permissions

        protected override void LoadDefaultConfig()
        {
            config = new PlayerProfilesConfiguration();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<PlayerProfilesConfiguration>();
            }
            catch (Exception exception)
            {
                LogWarning("config could not be read: " + exception.Message);
                config = null;
            }

            if (config == null)
            {
                config = new PlayerProfilesConfiguration();
            }

            NormaliseConfig();
            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        private void NormaliseConfig()
        {
            if (string.IsNullOrEmpty(config.ChatPrefix))
            {
                config.ChatPrefix = "[Profiles]";
            }

            config.PermissionPrefix = (config.PermissionPrefix ?? "playerprofiles").Trim().TrimEnd('.');
            if (string.IsNullOrEmpty(config.PermissionPrefix))
            {
                config.PermissionPrefix = "playerprofiles";
            }

            if (string.IsNullOrEmpty(config.UsePermission))
            {
                config.UsePermission = config.PermissionPrefix + ".use";
            }

            if (string.IsNullOrEmpty(config.AdminPermission))
            {
                config.AdminPermission = config.PermissionPrefix + ".admin";
            }

            config.MaxSlots = Clamp(config.MaxSlots, 1, MaxSlotsLimit);
            config.DefaultSlots = Clamp(config.DefaultSlots, 0, config.MaxSlots);
            config.MaxNameLength = Clamp(config.MaxNameLength, 1, 48);
            config.SwitchCooldownSeconds = Clamp(config.SwitchCooldownSeconds, 0, 3600);
            config.AutosaveSeconds = Clamp(config.AutosaveSeconds, 10, 3600);
            config.SwitchTimeoutSeconds = Clamp(config.SwitchTimeoutSeconds, 5, 120);
            config.ChunkBytes = Clamp(config.ChunkBytes, 4096, 65536);
            config.BackupCount = Clamp(config.BackupCount, 0, 2);

            config.ModCompatibility = (config.ModCompatibility ?? string.Empty).Trim();
            if (config.ModCompatibility.Equals("strict", StringComparison.OrdinalIgnoreCase))
            {
                config.ModCompatibility = "Strict";
            }
            else if (config.ModCompatibility.Equals("ignore", StringComparison.OrdinalIgnoreCase) ||
                     config.ModCompatibility.Equals("any", StringComparison.OrdinalIgnoreCase) ||
                     config.ModCompatibility.Equals("always", StringComparison.OrdinalIgnoreCase))
            {
                config.ModCompatibility = "Ignore";
            }
            else
            {
                config.ModCompatibility = "Additive";
            }

            if (string.IsNullOrEmpty(config.BootstrapProfileName))
            {
                config.BootstrapProfileName = "Main";
            }

            if (string.IsNullOrEmpty(config.ReconnectMessage))
            {
                config.ReconnectMessage =
                    "Profile '{profile}' is ready.\n\nReconnect to the server to load it.";
            }
        }

        private void RegisterPermissions()
        {
            permission.RegisterPermission(config.UsePermission, this);
            permission.RegisterPermission(config.AdminPermission, this);

            for (int i = 1; i <= config.MaxSlots; i++)
            {
                permission.RegisterPermission(config.PermissionPrefix + ".slots." + i, this);
            }

            permission.RegisterPermission(config.PermissionPrefix + ".slots.*", this);
        }

        private void EnsureDefaultPermissions()
        {
            GrantGroupIfMissing("players", config.UsePermission);
            if (config.GrantDefaultSlotsToPlayers && config.DefaultSlots > 0)
            {
                GrantGroupIfMissing("players", config.PermissionPrefix + ".slots." + config.DefaultSlots);
            }

            GrantGroupIfMissing("administrators", config.UsePermission);
            GrantGroupIfMissing("administrators", config.AdminPermission);
            if (config.GrantAdminToAdministrators)
            {
                GrantGroupIfMissing("administrators", config.PermissionPrefix + ".slots." + config.MaxSlots);
            }
        }

        private void GrantGroupIfMissing(string group, string permissionName)
        {
            if (string.IsNullOrEmpty(group) || string.IsNullOrEmpty(permissionName) ||
                !permission.GroupExists(group))
            {
                return;
            }

            if (!permission.GroupHasPermission(group, permissionName))
            {
                permission.GrantGroupPermission(group, permissionName, this);
            }
        }

        private int GetSlots(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return 0;
            }

            IPlayer player = players.FindPlayerById(playerId);
            if (player != null && player.IsAdmin)
            {
                return config.MaxSlots;
            }

            int cached;
            if (slotsCache.TryGetValue(playerId, out cached))
            {
                return cached;
            }

            int slots = config.DefaultSlots;

            if (permission.UserHasPermission(playerId, config.AdminPermission))
            {
                slots = config.MaxSlots;
            }
            else
            {
                string[] direct = permission.GetUserPermissions(playerId);
                if (direct != null)
                {
                    for (int i = 0; i < direct.Length; i++)
                    {
                        slots = Math.Max(slots, ParseSlotPermission(direct[i]));
                    }
                }

                string[] groups = permission.GetUserGroups(playerId);
                if (groups != null)
                {
                    for (int i = 0; i < groups.Length; i++)
                    {
                        string[] groupPermissions = permission.GetGroupPermissions(groups[i], true);
                        if (groupPermissions == null)
                        {
                            continue;
                        }

                        for (int j = 0; j < groupPermissions.Length; j++)
                        {
                            slots = Math.Max(slots, ParseSlotPermission(groupPermissions[j]));
                        }
                    }
                }
            }

            slots = Clamp(slots, 0, config.MaxSlots);
            slotsCache[playerId] = slots;
            return slots;
        }

        private int ParseSlotPermission(string permissionName)
        {
            if (string.IsNullOrEmpty(permissionName))
            {
                return 0;
            }

            string wildcard = config.PermissionPrefix + ".slots.*";
            if (string.Equals(permissionName, wildcard, StringComparison.OrdinalIgnoreCase))
            {
                return config.MaxSlots;
            }

            string prefix = config.PermissionPrefix + ".slots.";
            if (!permissionName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            int value;
            return int.TryParse(permissionName.Substring(prefix.Length), out value) ? value : 0;
        }

        private bool CanUse(IPlayer player)
        {
            if (player == null)
            {
                return false;
            }

            return player.IsServer ||
                   player.IsAdmin ||
                   permission.UserHasPermission(player.Id, config.UsePermission);
        }

        private void Reply(IPlayer player, string message)
        {
            if (player == null || string.IsNullOrEmpty(message))
            {
                return;
            }

            player.Message(string.IsNullOrEmpty(config.ChatPrefix)
                ? message
                : config.ChatPrefix + " " + message);
        }

        #endregion

        #region Helpers

        private static string ReadableId(ClientInfo clientInfo)
        {
            if (clientInfo == null)
            {
                return null;
            }

            try
            {
                if (clientInfo.PlatformId != null &&
                    !string.IsNullOrEmpty(clientInfo.PlatformId.ReadablePlatformUserIdentifier))
                {
                    return clientInfo.PlatformId.ReadablePlatformUserIdentifier;
                }
            }
            catch
            {
            }

            try
            {
                if (clientInfo.CrossplatformId != null &&
                    !string.IsNullOrEmpty(clientInfo.CrossplatformId.ReadablePlatformUserIdentifier))
                {
                    return clientInfo.CrossplatformId.ReadablePlatformUserIdentifier;
                }
            }
            catch
            {
            }

            try
            {
                if (clientInfo.InternalId != null)
                {
                    return clientInfo.InternalId.CombinedString;
                }
            }
            catch
            {
            }

            return null;
        }

        private static ClientInfo GetClientInfo(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return null;
            }

            try
            {
                ConnectionManager connection = SingletonMonoBehaviour<ConnectionManager>.Instance;
                if (connection == null || connection.Clients == null)
                {
                    return null;
                }

                for (int i = 0; i < connection.Clients.list.Count; i++)
                {
                    ClientInfo client = connection.Clients.list[i];
                    if (client == null)
                    {
                        continue;
                    }

                    if (string.Equals(ReadableId(client), playerId, StringComparison.OrdinalIgnoreCase))
                    {
                        return client;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static EntityPlayer GetEntity(ClientInfo clientInfo)
        {
            if (clientInfo == null)
            {
                return null;
            }

            try
            {
                GameManager manager = GameManager.Instance;
                if (manager == null || manager.World == null || clientInfo.entityId == -1)
                {
                    return null;
                }

                return manager.World.GetEntity(clientInfo.entityId) as EntityPlayer;
            }
            catch
            {
                return null;
            }
        }

        private static int GetLevel(ClientInfo clientInfo)
        {
            EntityPlayer entity = GetEntity(clientInfo);
            return entity != null && entity.Progression != null ? entity.Progression.Level : 1;
        }

        private static Vector3? SafeWorldSpawn()
        {
            try
            {
                GameManager manager = GameManager.Instance;
                if (manager == null || manager.World == null)
                {
                    return null;
                }

                Vector3[] points = manager.World.GetRandomSpawnPointPositions(1);
                if (points != null && points.Length > 0)
                {
                    return points[0];
                }
            }
            catch
            {
            }

            return null;
        }

        private static string NewToken()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 12);
        }

        private static string IsoNow()
        {
            return DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        }

        private static string FormatPlaytime(float minutes)
        {
            int total = Math.Max(0, (int)minutes);
            int days = total / 1440;
            int hours = (total % 1440) / 60;
            int mins = total % 60;

            if (days > 0)
            {
                return days + "d " + hours + "h";
            }
            if (hours > 0)
            {
                return hours + "h " + mins + "m";
            }
            return mins + "m";
        }

        private static string RelativeTime(string iso)
        {
            DateTime value;
            if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out value))
            {
                return "unknown";
            }

            TimeSpan delta = DateTime.UtcNow - value.ToUniversalTime();
            if (delta.TotalSeconds < 60)
            {
                return "just now";
            }
            if (delta.TotalMinutes < 60)
            {
                return (int)delta.TotalMinutes + "m ago";
            }
            if (delta.TotalHours < 24)
            {
                return (int)delta.TotalHours + "h ago";
            }
            return (int)delta.TotalDays + "d ago";
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

        private UiSession GetUiSession(string playerId)
        {
            UiSession session;
            if (!uiSessions.TryGetValue(playerId, out session) || session == null)
            {
                session = new UiSession();
                uiSessions[playerId] = session;
            }
            return session;
        }

        private string ComputeModSetHash()
        {
            try
            {
                List<string> parts = new List<string>();
                List<Mod> mods = ModManager.GetLoadedMods();
                if (mods != null)
                {
                    foreach (Mod mod in mods)
                    {
                        if (mod == null)
                        {
                            continue;
                        }

                        parts.Add((mod.Name ?? string.Empty) + ":" + (mod.VersionString ?? string.Empty));
                    }
                }

                parts.Sort(StringComparer.OrdinalIgnoreCase);
                string joined = gameVersion + "|" + string.Join("|", parts.ToArray());
                return Fnv1aHex(joined);
            }
            catch (Exception exception)
            {
                LogWarning("mod hash failed: " + exception.Message);
                return Fnv1aHex(gameVersion ?? "unknown");
            }
        }

        private static string Fnv1aHex(string value)
        {
            unchecked
            {
                const ulong offset = 14695981039346656037UL;
                const ulong prime = 1099511628211UL;

                ulong hash = offset;
                byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
                for (int i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= prime;
                }

                return hash.ToString("x16");
            }
        }

        private void LogDebug(string message)
        {
            if (config != null && config.Debug)
            {
                Interface.Oxide.LogInfo("[PlayerProfiles] " + message);
            }
        }

        private void LogInfo(string message)
        {
            Interface.Oxide.LogInfo("[PlayerProfiles] " + message);
        }

        private void LogWarning(string message)
        {
            Interface.Oxide.LogWarning("[PlayerProfiles] " + message);
        }

        private void LogError(string message)
        {
            Interface.Oxide.LogError("[PlayerProfiles] " + message);
        }

        private class UiSession
        {
            public string Mode = "list";
            public string SelectedId;
            public int Page;
            public string Input = string.Empty;
            public string Status = string.Empty;
        }

        private class LiveSwitch
        {
            public string Token;
            public string FromProfileId;
            public string ToProfileId;
            public string FromName;
            public string ToName;
            public DateTime StartedUtc;
            public byte[] TargetPayload;
            public string TargetHash;
            public PlayerDataFile TargetPdf;
            public int ChunkCount;
            public int ChunkBytes;
            public DateTime TimedOutUtc;
            public int ItemCount;
            public string ItemMapHash;
        }

        #endregion
    }

    public class PlayerProfilesConfiguration
    {
        public string ChatPrefix = "[Profiles]";
        public string PermissionPrefix = "playerprofiles";
        public string UsePermission = "playerprofiles.use";
        public string AdminPermission = "playerprofiles.admin";
        public int MaxSlots = 10;
        public int DefaultSlots = 2;
        public bool GrantDefaultSlotsToPlayers = true;
        public bool GrantAdminToAdministrators = true;
        public int MaxNameLength = 24;
        public string BootstrapProfileName = "Main";
        public bool NewProfileStartsFresh = true;
        public bool AllowSeamlessSwitch = true;
        public string ReconnectMessage = "Profile '{profile}' is ready.\n\nReconnect to the server to load it.";
        public int SwitchCooldownSeconds = 5;
        public bool DisallowWhileDead = true;
        public bool DisallowWhileInVehicle = true;
        public bool KeepPositionOnSwitch = false;
        public int AutosaveSeconds = 60;
        public int SwitchTimeoutSeconds = 20;
        public int ChunkBytes = 24576;
        public int BackupCount = 2;
        public bool ArchiveDeletedProfiles = true;
        public string ModCompatibility = "Additive";
        public bool RequireSameGameVersion = false;
        public bool RequireSameModSet = false;
        public bool Debug = false;
    }

    public class PlayerProfilesState
    {
        public int SchemaVersion = 1;
        public string PlayerId;
        public string PlayerName;
        public string ActiveProfileId;
        public PlayerProfilesPendingSwitch PendingSwitch;
        public List<PlayerProfileEntry> Profiles = new List<PlayerProfileEntry>();
        public string CreatedUtc;
        public string LastSeenUtc;
    }

    public class PlayerProfileEntry
    {
        public string Id;
        public string Name;
        public int Order;
        public string CreatedUtc;
        public string LastUsedUtc;
        public string SavedAtUtc;
        public bool HasData;
        public int Level = 1;
        public int Deaths;
        public float PlaytimeMinutes;
        public long Bytes;
        public string GameVersion;
        public string ModHash;
        public int ItemCount;
        public string ItemMapHash;
    }

    public class PlayerProfilesPendingSwitch
    {
        public string Mode;
        public string FromProfileId;
        public string ToProfileId;
        public string Token;
        public bool TargetFresh;
        public string StartedUtc;
    }
}
