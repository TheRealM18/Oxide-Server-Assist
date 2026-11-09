using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace ServerAssistMod
{
    /// <summary>
    /// Client half of the optional PlayerProfiles protocol.
    ///
    /// This is deliberately carried over the existing Server Assist
    /// request/response packages. Adding new NetPackage types would change the
    /// server package-id table, and every client without the matching type
    /// would be disconnected with "Unknown package type".
    ///
    /// Protocol (all payloads are strings):
    ///
    ///   Server -> client:
    ///     @pp|prepare|token|fromName|toName
    ///     @pp|begin|token|chunks|sha256|bytes|chunkBytes
    ///     @pp|chunk|token|index|base64
    ///     @pp|done|token
    ///     @pp|abort|token|reason
    ///     @pp|ok
    ///
    ///   Client -> server (the panel id lets Server Assist route the payload
    ///   without an active window session; the plugin receives it without the
    ///   panel id, as "@pp|<body>"):
    ///     @pp|playerprofiles|hello|1
    ///     @pp|playerprofiles|ready|token
    ///     @pp|playerprofiles|chunk|token|index
    ///     @pp|playerprofiles|applied|token|sha256
    ///     @pp|playerprofiles|failed|token|reason
    ///     @pp|playerprofiles|recover|token|reason
    ///     @pp|playerprofiles|abort|token|reason
    ///
    /// The server captures the old profile after "ready", then streams the new
    /// .ttp payload in small chunks. The client applies the payload locally,
    /// then sends "applied" so the server applies the same payload to its
    /// entity and commits the profile state.
    /// </summary>
    public static class PlayerProfilesClient
    {
        public const string ProtocolPrefix = "@pp|";
        public const string PanelId = "playerprofiles";
        public const int ProtocolVersion = 1;

        private const float SwitchTimeoutSeconds = 20f;
        private const float HelloIntervalSeconds = 60f;
        private const float PlayerDataTimeoutSeconds = 30f;
        private const float InventoryTimeoutSeconds = 0.1f;

        private static bool locked;
        private static string token;
        private static float lockDeadline;
        private static float recoveryUnlockAt;
        private static bool awaitingDone;
        private static string doneToken;
        private static string doneMessage;
        private static float doneDeadline;
        private static float lastHello = -9999f;

        private static int totalChunks;
        private static int nextChunk;
        private static int chunkBytes;
        private static int expectedBytes;
        private static string expectedHash;
        private static byte[] buffer;
        private static string fromName;
        private static string toName;

        public static void Tick()
        {
            if (GameManager.IsDedicatedServer)
            {
                return;
            }

            if (Time.realtimeSinceStartup - lastHello >= HelloIntervalSeconds)
            {
                SendHello();
            }

            if (awaitingDone && Time.realtimeSinceStartup > doneDeadline)
            {
                CompleteSuccess(string.IsNullOrEmpty(doneMessage)
                    ? "Profile loaded."
                    : doneMessage);
            }

            if (recoveryUnlockAt > 0f && Time.realtimeSinceStartup > recoveryUnlockAt)
            {
                // The server normally kicks after a partially applied switch.
                // If that never happens, do not strand the player forever.
                recoveryUnlockAt = 0f;
                RestoreSendTimers();
                EntityPlayerLocal recoveryPlayer = GetLocalPlayer();
                if (recoveryPlayer != null)
                {
                    try
                    {
                        recoveryPlayer.SetControllable(true);
                    }
                    catch
                    {
                    }
                }
            }

            if (!locked)
            {
                return;
            }

            LockSendTimers();

            if (Time.realtimeSinceStartup > lockDeadline)
            {
                Finish(false, "The profile switch timed out.", true);
            }
        }

        public static void OnLocalPlayerSpawned()
        {
            lastHello = -9999f;
            SendHello();
        }

        public static void Reset()
        {
            locked = false;
            token = null;
            buffer = null;
            expectedHash = null;
            expectedBytes = 0;
            totalChunks = 0;
            nextChunk = 0;
            chunkBytes = 0;
            fromName = null;
            toName = null;
            recoveryUnlockAt = 0f;
            awaitingDone = false;
            doneToken = null;
            doneMessage = null;
            doneDeadline = 0f;
        }

        public static void SendHello()
        {
            lastHello = Time.realtimeSinceStartup;
            SendProtocol("hello|" + ProtocolVersion);
        }

        /// <summary>
        /// Client-to-server requests carry the panel id so Server Assist can
        /// route them to PlayerProfiles even when no window/hook session is
        /// active. The server plugin receives "@pp|&lt;body&gt;".
        /// </summary>
        private static void SendProtocol(string body)
        {
            ServerAssistClient.SendRequest(ProtocolPrefix + PanelId + "|" + body);
        }

        /// <summary>
        /// Called before the normal Server Assist view parser. Returns true for
        /// every @pp payload, including unknown ones, so protocol traffic never
        /// renders as a broken window.
        /// </summary>
        public static bool TryHandle(string payload)
        {
            if (string.IsNullOrEmpty(payload) ||
                !payload.StartsWith(ProtocolPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            string body = payload.Substring(ProtocolPrefix.Length);
            string[] parts = body.Split('|');
            if (parts.Length == 0)
            {
                return true;
            }

            string verb = (parts[0] ?? string.Empty).Trim().ToLowerInvariant();
            switch (verb)
            {
                case "prepare":
                    HandlePrepare(parts);
                    break;
                case "begin":
                    HandleBegin(parts);
                    break;
                case "chunk":
                    HandleChunk(parts);
                    break;
                case "done":
                    HandleDone(parts);
                    break;
                case "abort":
                    HandleAbort(parts);
                    break;
                case "ok":
                default:
                    break;
            }

            return true;
        }

        private static void HandlePrepare(string[] parts)
        {
            string incomingToken = parts.Length > 1 ? parts[1] : string.Empty;
            if (string.IsNullOrEmpty(incomingToken))
            {
                return;
            }

            if (locked)
            {
                SendProtocol("abort|" + incomingToken + "|busy");
                return;
            }

            EntityPlayerLocal player = GetLocalPlayer();
            if (player == null)
            {
                SendProtocol("failed|" + incomingToken + "|No local player.");
                return;
            }

            if (player.IsDead())
            {
                SendProtocol("failed|" + incomingToken + "|Respawn before switching profiles.");
                return;
            }

            if (player.AttachedToEntity != null)
            {
                SendProtocol("failed|" + incomingToken + "|Exit your vehicle or drone before switching profiles.");
                return;
            }

            locked = true;
            token = incomingToken;
            fromName = parts.Length > 2 ? parts[2] : string.Empty;
            toName = parts.Length > 3 ? parts[3] : string.Empty;
            lockDeadline = Time.realtimeSinceStartup + SwitchTimeoutSeconds;

            buffer = null;
            expectedHash = null;
            expectedBytes = 0;
            totalChunks = 0;
            nextChunk = 0;

            player.SetControllable(false);
            LockSendTimers();

            // Send the freshest complete character state before the server
            // captures the profile we are leaving.
            if (!SendFullPlayerData(player))
            {
                Finish(false, "Your current character state could not be sent.", true);
                return;
            }

            SendProtocol("ready|" + token);
        }

        private static void HandleBegin(string[] parts)
        {
            if (!IsToken(parts, 1))
            {
                return;
            }

            int chunks;
            int bytes;
            int chunkSize;
            if (!int.TryParse(parts[2], out chunks) ||
                !int.TryParse(parts[4], out bytes) ||
                !int.TryParse(parts[5], out chunkSize) ||
                chunks <= 0 || bytes <= 0 || chunkSize <= 0)
            {
                Finish(false, "The server sent an invalid profile payload.", true);
                return;
            }

            if (parts.Length > 7)
            {
                int expectedItems;
                if (int.TryParse(parts[6], out expectedItems) &&
                    expectedItems > 0 &&
                    !string.IsNullOrEmpty(parts[7]) &&
                    !ItemMapPrefixMatches(parts[7], expectedItems))
                {
                    Finish(false,
                        "Your client's item layout does not match this profile. Install the same mods as the server.",
                        true);
                    return;
                }
            }

            totalChunks = chunks;
            expectedBytes = bytes;
            chunkBytes = chunkSize;
            expectedHash = parts[3] ?? string.Empty;
            nextChunk = 0;
            buffer = new byte[bytes];

            RequestChunk(0);
        }

        private static void HandleChunk(string[] parts)
        {
            if (!IsToken(parts, 1))
            {
                return;
            }

            int index;
            if (!int.TryParse(parts[2], out index) || buffer == null || index < 0 || index != nextChunk)
            {
                Finish(false, "The profile payload arrived out of order.", true);
                return;
            }

            byte[] chunk;
            try
            {
                chunk = Convert.FromBase64String(parts.Length > 3 ? parts[3] : string.Empty);
            }
            catch
            {
                Finish(false, "The profile payload could not be decoded.", true);
                return;
            }

            int offset = index * chunkBytes;
            if (offset < 0 || offset + chunk.Length > buffer.Length)
            {
                Finish(false, "The profile payload was larger than expected.", true);
                return;
            }

            Buffer.BlockCopy(chunk, 0, buffer, offset, chunk.Length);
            nextChunk++;

            if (nextChunk < totalChunks)
            {
                RequestChunk(nextChunk);
                return;
            }

            if (buffer.Length != expectedBytes ||
                !string.Equals(Sha256Hex(buffer), expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                Finish(false, "The profile payload failed its integrity check.", true);
                return;
            }

            ApplyPayload(buffer);
        }

        private static void HandleDone(string[] parts)
        {
            string incomingToken = parts.Length > 1 ? parts[1] : string.Empty;
            if (!awaitingDone ||
                string.IsNullOrEmpty(doneToken) ||
                !string.Equals(incomingToken, doneToken, StringComparison.Ordinal))
            {
                return;
            }

            // The server committed the switch. Release the client freeze now,
            // not before, so the old server state and the new client state
            // cannot be used at the same time.
            CompleteSuccess(string.IsNullOrEmpty(doneMessage)
                ? "Profile loaded."
                : doneMessage);
        }

        private static void HandleAbort(string[] parts)
        {
            if (!IsToken(parts, 1))
            {
                return;
            }

            string reason = parts.Length > 2 ? parts[2] : "The profile switch was cancelled.";
            // IsToken only succeeds while we are still locked. If the client
            // already applied and finished, this branch is not reached, so it
            // is safe to tell the server to clear its pending session.
            Finish(false, reason, true);
        }

        private static void ApplyPayload(byte[] payload)
        {
            EntityPlayerLocal player = GetLocalPlayer();
            if (player == null)
            {
                Finish(false, "No local player.", true);
                return;
            }

            if (player.IsDead())
            {
                Finish(false, "You died before the profile could be applied.", true);
                return;
            }

            if (player.AttachedToEntity != null)
            {
                Finish(false, "Exit your vehicle or drone before switching profiles.", true);
                return;
            }

            PlayerDataFile pdf = DeserializePlayerData(payload);
            if (pdf == null)
            {
                Finish(false, "The profile payload could not be read.", true);
                return;
            }

            if (pdf.bDead)
            {
                Finish(false, "That profile is dead. Reconnect to respawn it.", true);
                return;
            }

            XUi xui = GetXui(player);
            if (xui == null)
            {
                Finish(false, "The game UI is not ready yet.", true);
                return;
            }

            bool mutated = false;
            try
            {
                ClosePlayerWindows(xui, player);
                mutated = true;
                ClearOldPlayerState(player);
                ResetProgressionToMin(player);

                pdf.id = player.entityId;
                pdf.bLoaded = true;
                pdf.ToPlayer(player, StreamModeRead.Persistency);

                player.SetPosition(pdf.ecd.pos, true);
                player.SetRotation(pdf.ecd.rot);

                if (player.QuestJournal != null)
                {
                    player.QuestJournal.OwnerPlayer = player;
                    player.QuestJournal.StartQuests();
                    player.QuestJournal.RefreshTracked();
                }

                if (player.challengeJournal != null)
                {
                    player.challengeJournal.Player = player;
                }

                if (xui.QuestTracker != null)
                {
                    xui.QuestTracker.TrackedChallenge = null;
                    xui.QuestTracker.TrackedQuest = player.QuestJournal != null
                        ? player.QuestJournal.TrackedQuest
                        : null;
                }

                try
                {
                    xui.SetCraftingData(pdf.craftingData);
                }
                catch (Exception exception)
                {
                    Log.Warning("[PlayerProfiles] crafting data could not be applied: " + exception.Message);
                }

                if (xui.Recipes != null)
                {
                    xui.Recipes.TrackedRecipe = null;
                    xui.Recipes.RefreshTrackedRecipe();
                }

                if (xui.SelectedSkill != null)
                {
                    xui.SelectedSkill = null;
                }

                if (player.inventory != null)
                {
                    player.inventory.CallOnToolbeltChangedInternal();
                }

                if (player.bag != null)
                {
                    player.bag.onBackpackChanged();
                }

                if (player.equipment != null)
                {
                    player.equipment.OnEquipmentChanged();
                    // Equipment is deserialised before progression/buffs in
                    // PlayerDataFile.ToPlayer. Re-fire the set events now that
                    // the new perks and buffs are in place.
                    player.equipment.FireEventsForSetSlots();
                }

                player.bPlayerStatsChanged = true;
                player.bPlayerEquipmentChanged = true;

                string message = string.IsNullOrEmpty(toName)
                    ? "Profile loaded."
                    : "Profile loaded: " + toName;
                Finish(true, message, true);
            }
            catch (Exception exception)
            {
                Log.Error("[PlayerProfiles] apply failed: " + exception);
                Finish(false, "The profile could not be applied: " + exception.Message, true, mutated);
            }
        }

        private static void ClosePlayerWindows(XUi xui, EntityPlayerLocal player)
        {
            try
            {
                if (xui.DragAndDropWindow != null)
                {
                    xui.DragAndDropWindow.SetCurrentStack(ItemStack.Empty);
                }
                else
                {
                    player.DragAndDropItem = ItemStack.Empty;
                }
            }
            catch
            {
                // The drag window is optional UI state.
            }

            try
            {
                if (xui.playerUI != null && xui.playerUI.windowManager != null)
                {
                    GUIWindowManager manager = xui.playerUI.windowManager;
                    for (int i = manager.openWindows.Count - 1; i >= 0; i--)
                    {
                        try
                        {
                            manager.Close(manager.openWindows[i]);
                        }
                        catch
                        {
                            // Keep closing the rest.
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Log.Warning("[PlayerProfiles] could not close open windows: " + exception.Message);
            }
        }

        private static void ResetProgressionToMin(EntityPlayerLocal player)
        {
            try
            {
                Progression progression = player != null ? player.Progression : null;
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
                        // The profile's own values overwrite these in
                        // Progression.Read. New values added by a new mod stay
                        // at minimum instead of leaking from the old profile.
                        value.Level = value.ProgressionClass.MinLevel;
                    }
                }
            }
            catch (Exception exception)
            {
                Log.Warning("[PlayerProfiles] progression reset failed: " + exception.Message);
            }
        }

        private static void ClearOldPlayerState(EntityPlayerLocal player)
        {
            // Waypoint nav objects survive a journal/collection replacement.
            try
            {
                if (player.Waypoints != null &&
                    player.Waypoints.Collection != null &&
                    NavObjectManager.HasInstance)
                {
                    for (int i = 0; i < player.Waypoints.Collection.list.Count; i++)
                    {
                        Waypoint waypoint = player.Waypoints.Collection.list[i];
                        if (waypoint != null && waypoint.navObject != null)
                        {
                            NavObjectManager.Instance.UnRegisterNavObject(waypoint.navObject);
                            waypoint.navObject = null;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Log.Warning("[PlayerProfiles] waypoint cleanup failed: " + exception.Message);
            }

            try
            {
                if (player.ownedEntities != null)
                {
                    player.ownedEntities.Clear();
                }
            }
            catch (Exception exception)
            {
                Log.Warning("[PlayerProfiles] owned entity cleanup failed: " + exception.Message);
            }

            try
            {
                // The bedroll position lives in PersistentPlayerData, not in
                // the profile blob. Clear it before ToPlayer applies the target
                // profile's spawn point, otherwise the previous profile's
                // bedroll leaks into the new one.
                if (player.SpawnPoints != null)
                {
                    player.SpawnPoints.Clear();
                }
            }
            catch (Exception exception)
            {
                Log.Warning("[PlayerProfiles] spawn point cleanup failed: " + exception.Message);
            }

            try
            {
                if (player.Buffs != null)
                {
                    if (player.Buffs.ActiveBuffs != null)
                    {
                        for (int i = player.Buffs.ActiveBuffs.Count - 1; i >= 0; i--)
                        {
                            BuffValue buff = player.Buffs.ActiveBuffs[i];
                            player.Buffs.ActiveBuffs.RemoveAt(i);
                            if (buff != null)
                            {
                                player.Stats.EntityBuffRemoved(buff);
                            }
                        }
                    }

                    if (player.Buffs.CVars != null)
                    {
                        player.Buffs.CVars.Clear();
                    }

                    if (player.Buffs.CVarsLastNetSync != null)
                    {
                        player.Buffs.CVarsLastNetSync.Clear();
                    }
                }
            }
            catch (Exception exception)
            {
                Log.Warning("[PlayerProfiles] buff cleanup failed: " + exception.Message);
            }

            try
            {
                if (player.QuestJournal != null)
                {
                    // Leave party/shared quests before replacing the journal.
                    try
                    {
                        player.QuestJournal.RemoveAllSharedQuests();
                    }
                    catch
                    {
                    }

                    player.QuestJournal.UnHookQuests();
                    player.QuestJournal.TrackedQuest = null;
                    player.QuestJournal.ActiveQuest = null;
                }
            }
            catch (Exception exception)
            {
                Log.Warning("[PlayerProfiles] quest cleanup failed: " + exception.Message);
            }

            try
            {
                CraftingManager.AlreadyCraftedList = new HashSet<string>();
                CraftingManager.UnlockedRecipeList.Clear();
                CraftingManager.FavoriteRecipeList.Clear();
            }
            catch (Exception exception)
            {
                Log.Warning("[PlayerProfiles] recipe cleanup failed: " + exception.Message);
            }
        }

        private static bool SendFullPlayerData(EntityPlayerLocal player)
        {
            try
            {
                ConnectionManager connection = SingletonMonoBehaviour<ConnectionManager>.Instance;
                if (connection == null)
                {
                    return false;
                }

                connection.SendToServer(
                    NetPackageManager.GetPackage<NetPackagePlayerData>().Setup(player));
                return true;
            }
            catch (Exception exception)
            {
                Log.Warning("[PlayerProfiles] could not send full player data: " + exception.Message);
                return false;
            }
        }

        private static void RequestChunk(int index)
        {
            SendProtocol("chunk|" + token + "|" + index);
        }

        private static void Finish(bool success, string message, bool notifyServer, bool requestReconnect = false)
        {
            bool wasLocked = locked;
            string finishedToken = token;
            string finishedHash = expectedHash;

            locked = false;
            token = null;
            buffer = null;
            expectedHash = null;
            expectedBytes = 0;
            totalChunks = 0;
            nextChunk = 0;
            chunkBytes = 0;
            fromName = null;
            toName = null;

            if (success)
            {
                if (notifyServer && wasLocked && !string.IsNullOrEmpty(finishedToken))
                {
                    // Keep the client frozen until the server confirms the
                    // commit. This closes the window where the client has the
                    // new inventory while the server still has the old one.
                    SendProtocol("applied|" + finishedToken + "|" + (finishedHash ?? string.Empty));
                    awaitingDone = true;
                    doneToken = finishedToken;
                    doneMessage = message;
                    doneDeadline = Time.realtimeSinceStartup + 10f;
                    return;
                }

                CompleteSuccess(message);
                return;
            }

            bool holdForReconnect = requestReconnect;
            if (holdForReconnect)
            {
                // A partially applied client must not sync or act before the
                // server kicks it back to the previous profile.
                recoveryUnlockAt = Time.realtimeSinceStartup + 10f;
            }
            else
            {
                recoveryUnlockAt = 0f;
                RestoreSendTimers();
            }

            EntityPlayerLocal player = GetLocalPlayer();
            if (player != null && !holdForReconnect)
            {
                try
                {
                    player.SetControllable(true);
                }
                catch
                {
                    // The player may be leaving the world.
                }
            }

            if (notifyServer && wasLocked && !string.IsNullOrEmpty(finishedToken))
            {
                if (requestReconnect)
                {
                    SendProtocol("recover|" + finishedToken + "|" + SafeReason(message));
                }
                else
                {
                    SendProtocol("failed|" + finishedToken + "|" + SafeReason(message));
                }
            }

            if (player != null && !string.IsNullOrEmpty(message))
            {
                try
                {
                    GameManager.ShowTooltip(player, message);
                }
                catch
                {
                    // Tooltips are cosmetic.
                }
            }

            if (wasLocked && !holdForReconnect)
            {
                // Let the player retry after a failed/aborted switch. A
                // partial apply stays frozen until the server kicks it.
                ServerAssistClient.RequestOpen("a|hook|open|" + PanelId);
            }
        }

        private static void CompleteSuccess(string message)
        {
            awaitingDone = false;
            doneToken = null;
            doneMessage = null;
            doneDeadline = 0f;
            recoveryUnlockAt = 0f;

            RestoreSendTimers();

            EntityPlayerLocal player = GetLocalPlayer();
            if (player != null)
            {
                try
                {
                    player.SetControllable(true);
                }
                catch
                {
                }

                if (!string.IsNullOrEmpty(message))
                {
                    try
                    {
                        GameManager.ShowTooltip(player, message);
                    }
                    catch
                    {
                    }
                }
            }

            ServerAssistClient.RequestOpen("a|hook|open|" + PanelId);
        }

        private static void LockSendTimers()
        {
            try
            {
                GameManager manager = GameManager.Instance;
                if (manager == null)
                {
                    return;
                }

                manager.countdownSendPlayerDataFileToServer.SetTimeout(600f);
                manager.countdownSendPlayerDataFileToServer.SetPassedIn(600f);
                manager.countdownSendPlayerInventoryToServer.SetTimeout(600f);
                manager.countdownSendPlayerInventoryToServer.SetPassedIn(600f);
            }
            catch
            {
                // Never let the lock break the switch.
            }
        }

        private static void RestoreSendTimers()
        {
            try
            {
                GameManager manager = GameManager.Instance;
                if (manager == null)
                {
                    return;
                }

                manager.countdownSendPlayerDataFileToServer.SetTimeout(PlayerDataTimeoutSeconds);
                manager.countdownSendPlayerDataFileToServer.SetPassedIn(0f);
                manager.countdownSendPlayerInventoryToServer.SetTimeout(InventoryTimeoutSeconds);
                manager.countdownSendPlayerInventoryToServer.SetPassedIn(InventoryTimeoutSeconds);
            }
            catch
            {
                // Ignore.
            }
        }

        private static bool IsToken(string[] parts, int index)
        {
            return locked &&
                   parts != null &&
                   parts.Length > index &&
                   !string.IsNullOrEmpty(token) &&
                   string.Equals(parts[index], token, StringComparison.Ordinal);
        }

        private static EntityPlayerLocal GetLocalPlayer()
        {
            try
            {
                GameManager manager = GameManager.Instance;
                if (manager != null && manager.World != null)
                {
                    EntityPlayerLocal player = manager.World.GetPrimaryPlayer();
                    if (player != null)
                    {
                        return player;
                    }
                }

                LocalPlayerUI ui = LocalPlayerUI.primaryUI;
                return ui != null ? ui.entityPlayer : null;
            }
            catch
            {
                return null;
            }
        }

        private static XUi GetXui(EntityPlayerLocal player)
        {
            try
            {
                if (player != null && player.PlayerUI != null)
                {
                    return player.PlayerUI.xui;
                }

                LocalPlayerUI ui = LocalPlayerUI.primaryUI;
                return ui != null ? ui.xui : null;
            }
            catch
            {
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
                using (System.IO.MemoryStream stream = new System.IO.MemoryStream(payload, false))
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
                Log.Error("[PlayerProfiles] payload parse failed: " + exception);
                return null;
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

        private static string HashItemPrefix(int count)
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
                return "switch failed";
            }

            string safe = reason.Replace("|", " ").Replace("\r", " ").Replace("\n", " ");
            return safe.Length > 160 ? safe.Substring(0, 160) : safe;
        }
    }
}


