using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("BuggeeCore", "BuggeeNet", "0.2.0")]
    [Description("Milestone B: fleet registry, safe handoff and shared player state over Redis. No client mod.")]
    public class BuggeeCore : CovalencePlugin
    {
        #region Constants

        private const string PermUse = "buggeecore.use";
        private const string PermAdmin = "buggeecore.admin";

        private const string EnvelopeMagic = "BGG1";
        private const byte EnvelopeVersion = 1;

        private const string TtpMagic = "ttp\0";

        private const string ServerAssistPluginName = "ServerAssist";
        private const string SaHookId = "buggee";

        private static class SaIcons
        {
            public const string Panel = "sprite:sa_stream_link";
            public const string Home = "sprite:sa_stream_home";
            public const string Info = "sprite:sa_stream_info";
            public const string Warn = "sprite:sa_nav_help";

            public const string TintMuted = "#9AA0A6";
            public const string TintWarn = "#FFD166";
            public const string TintBad = "#FF6B6B";
        }

        #endregion

        #region Configuration

        private Configuration config;

        private class Configuration
        {
            public string ServerId = "home";
            public string DisplayName = "Buggee Home";
            public string Role = "home";
            public string PublicHost = "127.0.0.1";
            public int GamePort = 26900;

            /// <summary>Explicit destination for /hub. Empty = pick the healthiest peer with Role == "home".</summary>
            public string HomeServerId = string.Empty;

            public RedisOptions Redis = new RedisOptions();
            public NetworkOptions Network = new NetworkOptions();
            public TransferOptions Transfer = new TransferOptions();
            public bool Debug = false;

            public void Validate()
            {
                if (string.IsNullOrWhiteSpace(ServerId)) ServerId = "home";
                if (string.IsNullOrWhiteSpace(DisplayName)) DisplayName = ServerId;
                if (string.IsNullOrWhiteSpace(Role)) Role = "home";
                if (string.IsNullOrWhiteSpace(PublicHost)) PublicHost = "127.0.0.1";
                if (GamePort <= 0 || GamePort > 65535) GamePort = 26900;

                if (Redis == null) Redis = new RedisOptions();
                if (Network == null) Network = new NetworkOptions();
                if (Transfer == null) Transfer = new TransferOptions();

                Redis.Validate();
                Network.Validate();
                Transfer.Validate();
            }
        }

        private class RedisOptions
        {
            public bool Enabled = true;
            public string Host = "127.0.0.1";
            public int Port = 6379;
            public string Password = "buggee_dev_only_change_me";
            public int Database = 0;
            public int ConnectTimeoutMs = 1500;
            public int CommandTimeoutMs = 1000;

            public void Validate()
            {
                if (string.IsNullOrWhiteSpace(Host)) Host = "127.0.0.1";
                if (Port <= 0 || Port > 65535) Port = 6379;
                if (Database < 0) Database = 0;
                if (ConnectTimeoutMs < 250) ConnectTimeoutMs = 250;
                if (CommandTimeoutMs < 250) CommandTimeoutMs = 250;
            }
        }

        private class NetworkOptions
        {
            public string KeyPrefix = "bn:";
            public int HeartbeatSeconds = 15;
            public int HeartbeatTtlSeconds = 45;
            public int LeaseSeconds = 300;

            public void Validate()
            {
                if (KeyPrefix == null) KeyPrefix = "bn:";
                if (HeartbeatSeconds < 5) HeartbeatSeconds = 5;
                if (HeartbeatTtlSeconds <= HeartbeatSeconds) HeartbeatTtlSeconds = HeartbeatSeconds * 3;
                if (LeaseSeconds < 60) LeaseSeconds = 60;
            }
        }

        private class TransferOptions
        {
            public bool Enabled = true;

            /// <summary>Kick text shown to the client. Placeholders: {server} {host} {port} {role} {reason}</summary>
            public string KickMessage =
                "Buggee Network\nMoving you to {server}.\n\nReconnect to:\n{host}:{port}";

            /// <summary>Keep = retain source coordinates. Fixed = use SpawnX/Y/Z. WorldSpawn = ask the target world.</summary>
            public string SpawnMode = "Fixed";
            public float SpawnX = 0f;
            public float SpawnY = 0f;
            public float SpawnZ = 0f;

            public bool ClearOwnedEntities = true;
            public bool ClearWaypoints = false;
            public bool RequireSameModSet = true;

            /// <summary>Refuse to overwrite a local .ttp that is newer than the fleet snapshot.</summary>
            public bool LocalFileWinsWhenNewer = true;

            public void Validate()
            {
                if (string.IsNullOrWhiteSpace(KickMessage))
                    KickMessage = "Buggee Network\nMoving you to {server}.\n\nReconnect to:\n{host}:{port}";
                if (string.IsNullOrWhiteSpace(SpawnMode)) SpawnMode = "Fixed";
                SpawnMode = SpawnMode.Trim();
            }
        }

        #endregion

        #region State

        private RedisClient redis;
        private System.Threading.Timer heartbeatTimer;
        private volatile bool redisReady;

        private readonly HashSet<string> onlinePlayerIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly object onlineLock = new object();

        private string gameVersion;
        private string modSetHash;

        // Local counters kept on the main thread; published by the background heartbeat.
        private int onlineCount;

        private bool saRegistered;
        private readonly Dictionary<string, SaSession> saSessions =
            new Dictionary<string, SaSession>(StringComparer.OrdinalIgnoreCase);

        private sealed class SaSession
        {
            public string Screen = "home";
            public string SelectedServer;
            public string Status = string.Empty;
        }

        #endregion

        #region Lifecycle

        private void Init()
        {
            permission.RegisterPermission(PermUse, this);
            permission.RegisterPermission(PermAdmin, this);

            LoadConfig();

            gameVersion = Constants.cVersionMajor + "." + Constants.cVersionMinor + "." + Constants.cVersionBuild;
            modSetHash = ComputeModSetHash();

            AddCovalenceCommand(new[] { "buggee", "net" }, nameof(CommandStatus));
            AddCovalenceCommand(new[] { "hub", "home" }, nameof(CommandHub));
            AddCovalenceCommand(new[] { "horde", "hn" }, nameof(CommandHorde));
            AddCovalenceCommand("goto", nameof(CommandGoto), PermAdmin);
            AddCovalenceCommand("netpeers", nameof(CommandPeers), PermAdmin);
            AddCovalenceCommand("netdiag", nameof(CommandDiag), PermAdmin);
            AddCovalenceCommand("netrelease", nameof(CommandRelease), PermAdmin);

            // ServerAssist may load before or after this plugin; retry a couple of times
            // and also react to OnServerAssistReady.
            timer.Once(2f, RegisterServerAssistPanel);
            timer.Once(5f, RegisterServerAssistPanel);
        }

        private void OnServerInitialized()
        {
            if (!config.Redis.Enabled)
            {
                LogWarning("Redis is disabled in config; BuggeeCore is running in local-only mode.");
                return;
            }

            try
            {
                redis = new RedisClient(config.Redis);
                redis.Connect();
                redisReady = true;
                PublishServerRecord();
                LogInfo("Connected to Redis at " + config.Redis.Host + ":" + config.Redis.Port);
            }
            catch (Exception ex)
            {
                redisReady = false;
                LogError("Redis connection failed: " + ex.Message);
                LogWarning("Fleet features are offline. Players keep using local .ttp files.");
            }

            heartbeatTimer = new System.Threading.Timer(_ => HeartbeatTick(), null,
                config.Network.HeartbeatSeconds * 1000,
                config.Network.HeartbeatSeconds * 1000);
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
                Interface.Oxide.LogWarning("[BuggeeCore] config could not be read: " + exception.Message);
                config = null;
            }

            if (config == null)
                config = new Configuration();

            config.Validate();
            SaveConfig();
        }

        protected override void SaveConfig()
        {
            if (config != null && Config != null)
                Config.WriteObject(config, true);
        }

        private void Unload()
        {
            UnregisterServerAssistPanel();

            if (heartbeatTimer != null)
            {
                try { heartbeatTimer.Dispose(); } catch { }
                heartbeatTimer = null;
            }

            if (redisReady && redis != null)
            {
                try
                {
                    foreach (string pid in SnapshotOnline())
                        ReleaseLease(pid);

                    redis.Del(KeyServer(config.ServerId));
                    redis.SRem(KeyServers(), config.ServerId);
                }
                catch { }
            }

            if (redis != null)
            {
                redis.Dispose();
                redis = null;
            }

            redisReady = false;
        }

        #endregion

        #region Hooks

        /// <summary>
        /// Runs before the game's spawn path (which loads the .ttp from disk), so this is the
        /// only safe place to materialise an incoming fleet snapshot without a Harmony patch.
        /// Returning a string rejects the login with that message.
        /// </summary>
        private object OnUserApprove(ClientInfo clientInfo)
        {
            if (clientInfo == null || !config.Transfer.Enabled || !redisReady)
                return null;

            string playerId = PlayerId(clientInfo);
            if (string.IsNullOrEmpty(playerId))
                return null;

            try
            {
                string holder = redis.GetString(KeyActive(playerId));
                string transferTo = redis.GetString(KeyTransfer(playerId));

                bool hasTicketForMe = string.Equals(transferTo, config.ServerId, StringComparison.OrdinalIgnoreCase);
                bool heldElsewhere = !string.IsNullOrEmpty(holder) &&
                                     !string.Equals(holder, config.ServerId, StringComparison.OrdinalIgnoreCase);

                if (heldElsewhere && !hasTicketForMe)
                {
                    string holderName = PeerName(holder);
                    LogWarning("Rejected " + clientInfo.playerName + " (" + playerId + "): active on " + holder);
                    return "You are already connected to " + holderName + ".\nDisconnect there first.";
                }

                ClaimLease(playerId);
                redis.Del(KeyTransfer(playerId));

                MaterializeSnapshot(playerId, SaveFileName(clientInfo));
                return null;
            }
            catch (Exception ex)
            {
                // Fail open: a Redis outage must never lock players out of their own server.
                LogError("OnUserApprove failed for " + clientInfo.playerName + ": " + ex.Message);
                return null;
            }
        }

        private void OnPlayerConnected(ClientInfo clientInfo)
        {
            if (clientInfo == null) return;

            string playerId = PlayerId(clientInfo);
            if (string.IsNullOrEmpty(playerId)) return;

            lock (onlineLock)
            {
                onlinePlayerIds.Add(playerId);
                onlineCount = onlinePlayerIds.Count;
            }

            if (redisReady)
            {
                try
                {
                    redis.HSet(KeyPresence(playerId), "name", clientInfo.playerName ?? string.Empty);
                    redis.HSet(KeyPresence(playerId), "server", config.ServerId);
                }
                catch (Exception ex)
                {
                    DebugLog("presence write failed: " + ex.Message);
                }
            }
        }

        private void OnPlayerDisconnected(ClientInfo clientInfo)
        {
            if (clientInfo == null) return;

            string playerId = PlayerId(clientInfo);
            if (string.IsNullOrEmpty(playerId)) return;

            if (redisReady)
            {
                try { CaptureSnapshot(clientInfo, playerId, "disconnect"); }
                catch (Exception ex) { LogError("Snapshot capture failed for " + playerId + ": " + ex.Message); }
            }

            lock (onlineLock)
            {
                onlinePlayerIds.Remove(playerId);
                onlineCount = onlinePlayerIds.Count;
            }

            if (redisReady)
            {
                try
                {
                    redis.Del(KeyPresence(playerId));
                    ReleaseLease(playerId);
                }
                catch (Exception ex)
                {
                    DebugLog("disconnect cleanup failed: " + ex.Message);
                }
            }
        }

        private void OnServerSave()
        {
            if (!redisReady) return;

            try
            {
                foreach (string pid in SnapshotOnline())
                {
                    RefreshLease(pid);
                    redis.Expire(KeyPresence(pid), config.Network.HeartbeatTtlSeconds * 4);
                }
            }
            catch (Exception ex)
            {
                DebugLog("OnServerSave refresh failed: " + ex.Message);
            }
        }

        /// <summary>Called by ServerAssist after its initial load and after a reload.</summary>
        private void OnServerAssistReady()
        {
            RegisterServerAssistPanel();
        }

        #endregion

        #region Transfer

        private void TransferTo(IPlayer player, string targetServerId)
        {
            if (player == null || !player.IsConnected)
                return;

            if (!config.Transfer.Enabled)
            {
                player.Message("Transfers are disabled on this server.");
                return;
            }

            if (!redisReady)
            {
                player.Message("The network is offline right now. Try again in a moment.");
                return;
            }

            ServerRecord target;
            if (!TryGetPeer(targetServerId, out target))
            {
                player.Message("Unknown or offline server: " + targetServerId);
                return;
            }

            if (string.Equals(target.Id, config.ServerId, StringComparison.OrdinalIgnoreCase))
            {
                player.Message("You are already on " + target.Name + ".");
                return;
            }

            if (config.Transfer.RequireSameModSet &&
                !string.IsNullOrEmpty(target.Mods) &&
                !string.Equals(target.Mods, modSetHash, StringComparison.OrdinalIgnoreCase))
            {
                player.Message(target.Name + " is running a different mod set. Transfer refused.");
                LogWarning("Mod mismatch: local=" + modSetHash + " remote=" + target.Mods);
                return;
            }

            ClientInfo clientInfo = GetClientInfo(player);
            if (clientInfo == null)
            {
                player.Message("Could not resolve your session.");
                return;
            }

            try
            {
                // 1. Flush current state to the shared store. Never kick if this fails:
                //    the player would arrive with a stale or missing snapshot.
                if (!CaptureSnapshot(clientInfo, PlayerId(clientInfo), "transfer"))
                {
                    player.Message("Could not save your state, so the transfer was aborted.");
                    return;
                }

                // 2. Leave a ticket so the destination accepts us and nobody else does.
                redis.SetString(KeyTransfer(player.Id), target.Id, 180);

                // 3. Kick with reconnect instructions (the only redirect path without a client mod).
                string reason = BuildKickMessage(target);
                LogInfo("Transferring " + player.Name + " (" + player.Id + ") -> " + target.Id);
                player.Kick(reason);
            }
            catch (Exception ex)
            {
                LogError("Transfer failed for " + player.Name + ": " + ex.Message);
                player.Message("Transfer failed: " + ex.Message);
            }
        }

        private string BuildKickMessage(ServerRecord target)
        {
            return config.Transfer.KickMessage
                .Replace("{server}", target.Name)
                .Replace("{host}", target.Host)
                .Replace("{port}", target.Port.ToString(CultureInfo.InvariantCulture))
                .Replace("{role}", target.Role ?? string.Empty)
                .Replace("{reason}", "Transfer");
        }

        private void CommandHub(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(PermUse))
            {
                player.Message("You do not have permission to travel.");
                return;
            }

            string target = config.HomeServerId;
            if (string.IsNullOrWhiteSpace(target))
            {
                ServerRecord home;
                if (!TryPickByRole("home", out home))
                {
                    player.Message("No home server is online.");
                    return;
                }
                target = home.Id;
            }

            TransferTo(player, target);
        }

        private void CommandHorde(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(PermUse))
            {
                player.Message("You do not have permission to travel.");
                return;
            }

            ServerRecord horde;
            if (!TryPickByRole("horde", out horde))
            {
                player.Message("No horde server is online.");
                return;
            }

            TransferTo(player, horde.Id);
        }

        private void CommandGoto(IPlayer player, string command, string[] args)
        {
            if (args.Length < 1)
            {
                player.Message("Usage: /goto <serverId>");
                return;
            }

            if (player.IsConnected)
            {
                TransferTo(player, args[0]);
                return;
            }

            // Console use: transfer someone by name.
            if (args.Length >= 2)
            {
                IPlayer target = players.FindPlayer(args[1]);
                if (target == null)
                {
                    player.Message("Player not found: " + args[1]);
                    return;
                }
                TransferTo(target, args[0]);
                return;
            }

            player.Message("Usage: /goto <serverId> <player> (from console)");
        }

        #endregion

        #region Snapshots

        private bool CaptureSnapshot(ClientInfo clientInfo, string playerId, string reason)
        {
            if (!redisReady || clientInfo == null || string.IsNullOrEmpty(playerId))
                return false;

            EntityPlayer entity = GetEntity(clientInfo);
            if (entity == null)
            {
                DebugLog("capture skipped (" + reason + "): no live entity for " + playerId);
                return false;
            }

            PlayerDataFile pdf = new PlayerDataFile();
            pdf.FromPlayer(entity, StreamModeWrite.Persistency);
            SanitizeForExport(pdf);

            byte[] payload = SerializePlayerData(pdf);
            byte[] envelope = WrapEnvelope(payload, config.ServerId);

            redis.SetBytes(KeySnapshot(playerId), envelope, 0);
            redis.HSet(KeySnapshotMeta(playerId), "savedAt", NowUnix().ToString(CultureInfo.InvariantCulture));
            redis.HSet(KeySnapshotMeta(playerId), "server", config.ServerId);
            redis.HSet(KeySnapshotMeta(playerId), "reason", reason ?? string.Empty);
            redis.HSet(KeySnapshotMeta(playerId), "bytes", payload.Length.ToString(CultureInfo.InvariantCulture));

            DebugLog("captured " + playerId + " (" + payload.Length + " bytes, " + reason + ")");
            return true;
        }

        /// <summary>
        /// Writes the fleet snapshot into this server's player-data directory before the game's
        /// spawn path loads it. The vanilla loader then applies it as if it were a local save.
        /// </summary>
        private void MaterializeSnapshot(string playerId, string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                fileName = playerId;

            byte[] envelope = redis.GetBytes(KeySnapshot(playerId));
            if (envelope == null)
                return;

            long savedAt;
            string sourceServer;
            string sourceVersion;
            string sourceMods;
            byte[] payload;

            if (!TryUnwrapEnvelope(envelope, out savedAt, out sourceServer, out sourceVersion, out sourceMods, out payload))
            {
                LogWarning("Discarding malformed snapshot for " + playerId);
                return;
            }

            if (!string.Equals(sourceVersion, gameVersion, StringComparison.OrdinalIgnoreCase))
            {
                LogWarning("Snapshot version mismatch for " + playerId + ": " + sourceVersion + " -> " + gameVersion + ". Skipping.");
                return;
            }

            if (config.Transfer.RequireSameModSet &&
                !string.IsNullOrEmpty(sourceMods) &&
                !string.Equals(sourceMods, modSetHash, StringComparison.OrdinalIgnoreCase))
            {
                LogWarning("Snapshot mod mismatch for " + playerId + ". Skipping to protect item integrity.");
                return;
            }

            if (config.Transfer.LocalFileWinsWhenNewer)
            {
                string localPath = Path.Combine(GameIO.GetPlayerDataDir(), fileName + ".ttp");
                if (File.Exists(localPath))
                {
                    DateTime localWrite = File.GetLastWriteTimeUtc(localPath);
                    long localUnix = (long)(localWrite - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                    if (localUnix > savedAt)
                    {
                        DebugLog("local .ttp for " + playerId + " is newer than snapshot; keeping local");
                        return;
                    }
                }
            }

            PlayerDataFile pdf = DeserializePlayerData(payload);
            if (pdf == null)
            {
                LogWarning("Snapshot for " + playerId + " could not be deserialized.");
                return;
            }

            SanitizeForImport(pdf);
            pdf.Save(GameIO.GetPlayerDataDir(), fileName);

            DebugLog("materialised " + playerId + " from " + sourceServer + " (savedAt=" + savedAt + ")");
        }

        private void SanitizeForExport(PlayerDataFile pdf)
        {
            if (config.Transfer.ClearOwnedEntities && pdf.ownedEntities != null)
                pdf.ownedEntities.Clear();

            if (config.Transfer.ClearWaypoints && pdf.waypoints != null)
                pdf.waypoints = new WaypointCollection();
        }

        private void SanitizeForImport(PlayerDataFile pdf)
        {
            // Never inherit an entity id from another world; the destination assigns a fresh one.
            pdf.id = -1;
            pdf.bLoaded = true;
            pdf.bDead = false;

            // World-bound ownership must not follow the player across maps.
            if (config.Transfer.ClearOwnedEntities && pdf.ownedEntities != null)
                pdf.ownedEntities.Clear();

            if (config.Transfer.ClearWaypoints && pdf.waypoints != null)
                pdf.waypoints = new WaypointCollection();

            if (pdf.spawnPoints != null)
                pdf.spawnPoints.Clear();
            pdf.selectedSpawnPointKey = 0;

            // Position: the spawn path uses ecd.pos for loaded files, so this must be safe.
            Vector3 spawn;
            string mode = (config.Transfer.SpawnMode ?? "Fixed").Trim();
            if (string.Equals(mode, "Keep", StringComparison.OrdinalIgnoreCase))
            {
                spawn = pdf.ecd.pos;
            }
            else if (string.Equals(mode, "WorldSpawn", StringComparison.OrdinalIgnoreCase))
            {
                spawn = SafeWorldSpawn(pdf.ecd.pos);
            }
            else
            {
                spawn = new Vector3(config.Transfer.SpawnX, config.Transfer.SpawnY, config.Transfer.SpawnZ);
            }

            pdf.ecd.pos = spawn;
            pdf.ecd.rot = Vector3.zero;
            pdf.ecd.onGround = false;
            pdf.lastSpawnPosition = SpawnPosition.Undef;
        }

        private Vector3 SafeWorldSpawn(Vector3 fallback)
        {
            try
            {
                World world = GameManager.Instance != null ? GameManager.Instance.World : null;
                if (world == null) return fallback;

                Vector3[] points = world.GetRandomSpawnPointPositions(1);
                if (points != null && points.Length > 0)
                    return points[0];
            }
            catch (Exception ex)
            {
                DebugLog("SafeWorldSpawn failed: " + ex.Message);
            }

            return new Vector3(config.Transfer.SpawnX, config.Transfer.SpawnY, config.Transfer.SpawnZ);
        }

        #endregion

        #region Binary format

        private static byte[] SerializePlayerData(PlayerDataFile pdf)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                PooledBinaryWriter bw = new PooledBinaryWriter();
                try
                {
                    bw.SetBaseStream(ms);
                    bw.Write('t');
                    bw.Write('t');
                    bw.Write('p');
                    bw.Write((byte)0);
                    bw.Write((byte)PlayerDataFile.cFileVersion);
                    pdf.Write(bw, StreamModeWrite.Persistency);
                    bw.Flush();
                }
                finally
                {
                    bw.SetBaseStream(null);
                }

                return ms.ToArray();
            }
        }

        private static PlayerDataFile DeserializePlayerData(byte[] payload)
        {
            if (payload == null || payload.Length < 5)
                return null;

            using (MemoryStream ms = new MemoryStream(payload, false))
            {
                PooledBinaryReader br = new PooledBinaryReader();
                try
                {
                    br.SetBaseStream(ms);
                    char a = br.ReadChar();
                    char b = br.ReadChar();
                    char c = br.ReadChar();
                    char d = br.ReadChar();
                    if (a != 't' || b != 't' || c != 'p' || d != '\0')
                        return null;

                    uint version = br.ReadByte();
                    PlayerDataFile pdf = new PlayerDataFile();
                    pdf.Read(br, version, StreamModeRead.Persistency);
                    pdf.bLoaded = true;
                    return pdf;
                }
                catch (Exception ex)
                {
                    Interface.Oxide.LogWarning("[BuggeeCore] PlayerDataFile parse failed: " + ex.Message);
                    return null;
                }
                finally
                {
                    br.SetBaseStream(null);
                }
            }
        }

        private byte[] WrapEnvelope(byte[] payload, string sourceServer)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (BinaryWriter bw = new BinaryWriter(ms, Encoding.UTF8, true))
                {
                    bw.Write(Encoding.ASCII.GetBytes(EnvelopeMagic));
                    bw.Write(EnvelopeVersion);
                    bw.Write(NowUnix());
                    bw.Write(sourceServer ?? string.Empty);
                    bw.Write(gameVersion ?? string.Empty);
                    bw.Write(modSetHash ?? string.Empty);
                    bw.Write(payload.Length);
                    bw.Write(payload);
                }
                return ms.ToArray();
            }
        }

        private bool TryUnwrapEnvelope(byte[] envelope, out long savedAt, out string sourceServer,
            out string sourceVersion, out string sourceMods, out byte[] payload)
        {
            savedAt = 0;
            sourceServer = null;
            sourceVersion = null;
            sourceMods = null;
            payload = null;

            try
            {
                using (MemoryStream ms = new MemoryStream(envelope, false))
                using (BinaryReader br = new BinaryReader(ms, Encoding.UTF8))
                {
                    string magic = Encoding.ASCII.GetString(br.ReadBytes(EnvelopeMagic.Length));
                    if (magic != EnvelopeMagic) return false;

                    byte version = br.ReadByte();
                    if (version != EnvelopeVersion) return false;

                    savedAt = br.ReadInt64();
                    sourceServer = br.ReadString();
                    sourceVersion = br.ReadString();
                    sourceMods = br.ReadString();
                    int length = br.ReadInt32();
                    if (length < 0 || length > 64 * 1024 * 1024) return false;

                    payload = br.ReadBytes(length);
                    return payload != null && payload.Length == length;
                }
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Fleet registry

        private void HeartbeatTick()
        {
            if (!redisReady || redis == null)
                return;

            try
            {
                PublishServerRecord();

                foreach (string pid in SnapshotOnline())
                {
                    RefreshLease(pid);
                    redis.Expire(KeyPresence(pid), config.Network.HeartbeatTtlSeconds * 4);
                }
            }
            catch (Exception ex)
            {
                DebugLog("heartbeat failed: " + ex.Message);
                try { redis.Connect(); }
                catch { redisReady = false; }
            }
        }

        private void PublishServerRecord()
        {
            if (!redisReady) return;

            string key = KeyServer(config.ServerId);
            redis.HSet(key, "id", config.ServerId);
            redis.HSet(key, "name", config.DisplayName);
            redis.HSet(key, "role", config.Role);
            redis.HSet(key, "host", config.PublicHost);
            redis.HSet(key, "port", config.GamePort.ToString(CultureInfo.InvariantCulture));
            redis.HSet(key, "players", onlineCount.ToString(CultureInfo.InvariantCulture));
            redis.HSet(key, "version", gameVersion);
            redis.HSet(key, "mods", modSetHash);
            redis.HSet(key, "started", NowUnix().ToString(CultureInfo.InvariantCulture));
            redis.Expire(key, config.Network.HeartbeatTtlSeconds);
            redis.SAdd(KeyServers(), config.ServerId);
        }

        private bool TryGetPeer(string serverId, out ServerRecord record)
        {
            record = null;
            if (string.IsNullOrWhiteSpace(serverId) || !redisReady) return false;

            Dictionary<string, string> hash = redis.HGetAll(KeyServer(serverId));
            if (hash == null || hash.Count == 0) return false;

            record = ServerRecord.FromHash(hash);
            return record != null && !string.IsNullOrEmpty(record.Host);
        }

        private bool TryPickByRole(string role, out ServerRecord record)
        {
            record = null;
            if (!redisReady) return false;

            string[] ids = redis.SMembers(KeyServers());
            if (ids == null) return false;

            List<ServerRecord> candidates = new List<ServerRecord>();
            foreach (string id in ids)
            {
                ServerRecord candidate;
                if (!TryGetPeer(id, out candidate)) continue;
                if (!string.Equals(candidate.Role, role, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(candidate.Id, config.ServerId, StringComparison.OrdinalIgnoreCase)) continue;
                candidates.Add(candidate);
            }

            if (candidates.Count == 0) return false;

            record = candidates
                .OrderBy(c => c.Players)
                .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
                .First();
            return true;
        }

        private string PeerName(string serverId)
        {
            ServerRecord peer;
            if (TryGetPeer(serverId, out peer) && !string.IsNullOrEmpty(peer.Name))
                return peer.Name;
            return serverId;
        }

        private sealed class ServerRecord
        {
            public string Id;
            public string Name;
            public string Role;
            public string Host;
            public int Port;
            public int Players;
            public string Version;
            public string Mods;

            public static ServerRecord FromHash(Dictionary<string, string> hash)
            {
                if (hash == null) return null;

                ServerRecord r = new ServerRecord();
                hash.TryGetValue("id", out r.Id);
                hash.TryGetValue("name", out r.Name);
                hash.TryGetValue("role", out r.Role);
                hash.TryGetValue("host", out r.Host);
                hash.TryGetValue("version", out r.Version);
                hash.TryGetValue("mods", out r.Mods);

                string port;
                if (hash.TryGetValue("port", out port))
                    int.TryParse(port, NumberStyles.Integer, CultureInfo.InvariantCulture, out r.Port);

                string playersValue;
                if (hash.TryGetValue("players", out playersValue))
                    int.TryParse(playersValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out r.Players);

                if (string.IsNullOrEmpty(r.Id) || string.IsNullOrEmpty(r.Name))
                    return null;

                return r;
            }
        }

        #endregion

        #region Leases

        private void ClaimLease(string playerId)
        {
            if (!redisReady || string.IsNullOrEmpty(playerId)) return;
            redis.SetString(KeyActive(playerId), config.ServerId, config.Network.LeaseSeconds);
        }

        /// <summary>
        /// Extends a lease only if we still own it. A stale session on another server must not
        /// be able to steal ownership from the server the player is actually on.
        /// </summary>
        private void RefreshLease(string playerId)
        {
            if (!redisReady || string.IsNullOrEmpty(playerId)) return;

            const string script =
                "if redis.call('GET', KEYS[1]) == ARGV[1] then " +
                "return redis.call('EXPIRE', KEYS[1], ARGV[2]) else return 0 end";

            redis.Eval(script,
                new[] { KeyActive(playerId) },
                new[] { config.ServerId, config.Network.LeaseSeconds.ToString(CultureInfo.InvariantCulture) });
        }

        private void ReleaseLease(string playerId)
        {
            if (!redisReady || string.IsNullOrEmpty(playerId)) return;

            // Compare-and-delete so a slow disconnect cannot clear the new owner's lease.
            const string script =
                "if redis.call('GET', KEYS[1]) == ARGV[1] then " +
                "return redis.call('DEL', KEYS[1]) else return 0 end";

            redis.Eval(script, new[] { KeyActive(playerId) }, new[] { config.ServerId });
        }

        #endregion

        #region Keys

        private string P { get { return config.Network.KeyPrefix; } }

        private string KeyServer(string id) { return P + "srv:" + id; }
        private string KeyServers() { return P + "servers"; }
        private string KeySnapshot(string playerId) { return P + "snap:" + playerId; }
        private string KeySnapshotMeta(string playerId) { return P + "snapmeta:" + playerId; }
        private string KeyActive(string playerId) { return P + "active:" + playerId; }
        private string KeyTransfer(string playerId) { return P + "transfer:" + playerId; }
        private string KeyPresence(string playerId) { return P + "presence:" + playerId; }

        #endregion

        #region Commands

        private void CommandStatus(IPlayer player, string command, string[] args)
        {
            if (player == null) return;

            player.Message("Buggee Network");
            player.Message("Server: " + config.DisplayName + " [" + config.ServerId + "] role=" + config.Role);
            player.Message("Address: " + config.PublicHost + ":" + config.GamePort);
            player.Message("Redis: " + (redisReady ? "connected" : "offline") + "  version=" + gameVersion);
            player.Message("Commands: /hub, /horde" + (player.HasPermission(PermAdmin) ? ", /goto, /netpeers, /netdiag" : string.Empty));
        }

        private void CommandPeers(IPlayer player, string command, string[] args)
        {
            if (!redisReady)
            {
                player.Message("Redis is offline.");
                return;
            }

            string[] ids = redis.SMembers(KeyServers());
            if (ids == null || ids.Length == 0)
            {
                player.Message("No peers registered.");
                return;
            }

            player.Message("Peers (" + ids.Length + "):");
            foreach (string id in ids)
            {
                ServerRecord peer;
                if (!TryGetPeer(id, out peer))
                {
                    player.Message("- " + id + " (stale)");
                    continue;
                }

                player.Message(string.Format(CultureInfo.InvariantCulture,
                    "- {0} [{1}] {2}:{3} role={4} players={5} mods={6}",
                    peer.Name, peer.Id, peer.Host, peer.Port, peer.Role, peer.Players,
                    string.Equals(peer.Mods, modSetHash, StringComparison.OrdinalIgnoreCase) ? "match" : "DIFFERENT"));
            }
        }

        private void CommandRelease(IPlayer player, string command, string[] args)
        {
            if (args.Length < 1)
            {
                player.Message("Usage: /netrelease <playerId>");
                return;
            }

            if (!redisReady)
            {
                player.Message("Redis is offline.");
                return;
            }

            long removed = redis.Del(KeyActive(args[0]));
            redis.Del(KeyTransfer(args[0]));
            player.Message(removed > 0
                ? "Released the session lease for " + args[0] + "."
                : "No lease was held by " + args[0] + ".");
        }

        private void CommandDiag(IPlayer player, string command, string[] args)
        {
            if (!redisReady)
            {
                player.Message("Redis: OFFLINE");
                return;
            }

            try
            {
                player.Message("Redis: " + redis.Ping());
                player.Message("Key prefix: " + config.Network.KeyPrefix);
                player.Message("Mod hash: " + modSetHash);
                player.Message("Local entity: " + gameVersion);

                string[] ids = redis.SMembers(KeyServers());
                player.Message("Registry entries: " + (ids == null ? 0 : ids.Length));
            }
            catch (Exception ex)
            {
                player.Message("Redis error: " + ex.Message);
            }
        }

        #endregion

        #region Helpers

        private static string PlayerId(ClientInfo clientInfo)
        {
            if (clientInfo == null) return null;

            try
            {
                if (clientInfo.PlatformId != null)
                    return clientInfo.PlatformId.ReadablePlatformUserIdentifier;
            }
            catch { }

            try
            {
                if (clientInfo.InternalId != null)
                    return clientInfo.InternalId.CombinedString;
            }
            catch { }

            return null;
        }

        /// <summary>
        /// The game stores player saves as "&lt;InternalId.CombinedString&gt;.ttp" (e.g. "Steam_7656..."),
        /// which is NOT the readable SteamID used by Oxide's IPlayer.Id.
        /// </summary>
        private static string SaveFileName(ClientInfo clientInfo)
        {
            if (clientInfo == null) return null;

            try
            {
                if (clientInfo.InternalId != null)
                    return clientInfo.InternalId.CombinedString;
            }
            catch { }

            try
            {
                if (clientInfo.PlatformId != null)
                    return clientInfo.PlatformId.CombinedString;
            }
            catch { }

            return null;
        }

        private static ClientInfo GetClientInfo(IPlayer player)
        {
            if (player == null) return null;

            try
            {
                object obj = player.Object;
                return obj as ClientInfo;
            }
            catch
            {
                return null;
            }
        }

        private static EntityPlayer GetEntity(ClientInfo clientInfo)
        {
            if (clientInfo == null || clientInfo.entityId == -1) return null;

            try
            {
                World world = GameManager.Instance != null ? GameManager.Instance.World : null;
                if (world == null) return null;
                return world.GetEntity(clientInfo.entityId) as EntityPlayer;
            }
            catch
            {
                return null;
            }
        }

        private string[] SnapshotOnline()
        {
            lock (onlineLock)
            {
                return onlinePlayerIds.ToArray();
            }
        }

        private static long NowUnix()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
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
                        if (mod == null) continue;
                        parts.Add((mod.Name ?? string.Empty) + ":" + (mod.VersionString ?? string.Empty));
                    }
                }

                parts.Sort(StringComparer.OrdinalIgnoreCase);
                string joined = gameVersion + "|" + string.Join("|", parts.ToArray());
                return Fnv1a(joined);
            }
            catch (Exception ex)
            {
                DebugLog("mod hash failed: " + ex.Message);
                return Fnv1a(gameVersion ?? "unknown");
            }
        }

        private static string Fnv1a(string value)
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

                return hash.ToString("x16", CultureInfo.InvariantCulture);
            }
        }

        private void LogInfo(string message)
        {
            Interface.Oxide.LogInfo(message);
        }

        private void DebugLog(string message)
        {
            if (!config.Debug) return;
            Interface.Oxide.LogInfo("[debug] " + message);
        }

        #endregion

        #region Server Assist integration

        /// <summary>
        /// Registers the home-row panel. Safe to call repeatedly: ServerAssist replaces a
        /// panel with the same id, so reloads of either plugin converge.
        /// </summary>
        private void RegisterServerAssistPanel()
        {
            Plugin serverAssist = plugins.Find(ServerAssistPluginName);
            if (serverAssist == null)
                return;

            try
            {
                object result = serverAssist.Call(
                    "RegisterPanel",
                    SaHookId,
                    "Buggee Network",
                    "Fleet status and one-click travel.",
                    PermUse,
                    Name,
                    nameof(HandleGuiRequest),
                    SaIcons.Panel,
                    95);

                bool registered = result is bool && (bool)result;

                // Older ServerAssist builds only expose RegisterGuiHook.
                if (!registered)
                {
                    result = serverAssist.Call(
                        "RegisterGuiHook",
                        SaHookId,
                        "Buggee Network",
                        "Fleet status and one-click travel.",
                        PermUse,
                        Name,
                        nameof(HandleGuiRequest));

                    registered = result is bool && (bool)result;
                    if (registered)
                        serverAssist.Call("SetPanelIcon", SaHookId, SaIcons.Panel);
                }

                if (registered && !saRegistered)
                {
                    saRegistered = true;
                    Interface.Oxide.LogInfo("[BuggeeCore] registered ServerAssist panel '" + SaHookId + "'.");
                }
            }
            catch (Exception ex)
            {
                Interface.Oxide.LogWarning("[BuggeeCore] ServerAssist panel registration failed: " + ex.Message);
            }
        }

        private void UnregisterServerAssistPanel()
        {
            Plugin serverAssist = plugins.Find(ServerAssistPluginName);
            if (serverAssist == null)
            {
                saRegistered = false;
                return;
            }

            try
            {
                serverAssist.Call("UnregisterPanel", SaHookId);
            }
            catch (Exception ex)
            {
                DebugLog("UnregisterPanel failed: " + ex.Message);
            }

            saRegistered = false;
            saSessions.Clear();
        }

        /// <summary>
        /// ServerAssist entry point. Must stay public: it is resolved by name.
        /// Returns the ServerAssist line protocol.
        /// </summary>
        public string HandleGuiRequest(string playerId, string payload)
        {
            try
            {
                if (string.IsNullOrEmpty(playerId))
                    return SaError("Could not determine your player id.");

                if (!permission.UserHasPermission(playerId, PermUse))
                    return SaError("You do not have permission to use the Buggee Network.");

                SaSession session = GetSaSession(playerId);

                if (string.IsNullOrEmpty(payload) || payload == "open")
                    payload = "a|open";

                if (payload.StartsWith("a|", StringComparison.Ordinal))
                    return SaAction(playerId, session, payload.Substring(2));

                if (payload.StartsWith("t|", StringComparison.Ordinal))
                    return SaHome(playerId, session, "This panel has no text input.");

                return SaError("Unknown request.");
            }
            catch (Exception ex)
            {
                Interface.Oxide.LogException("[BuggeeCore] ServerAssist request failed", ex);
                return SaError("Internal error: " + ex.Message);
            }
        }

        private string SaAction(string playerId, SaSession session, string action)
        {
            string[] parts = action.Split('|');
            string command = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
            session.Status = string.Empty;

            switch (command)
            {
                case "open":
                case "home":
                    session.Screen = "home";
                    session.SelectedServer = null;
                    return SaHome(playerId, session, null);

                case "refresh":
                    return SaHome(playerId, session, redisReady ? "Refreshed." : "Redis is offline.");

                case "servers":
                    session.Screen = "servers";
                    return SaServers(playerId, session);

                case "target":
                    if (parts.Length < 2)
                        return SaHome(playerId, session, "No server selected.");
                    session.SelectedServer = parts[1];
                    session.Screen = "confirm";
                    return SaConfirm(playerId, session);

                case "confirm":
                    if (parts.Length < 2)
                        return SaHome(playerId, session, "No server selected.");
                    return SaTravel(playerId, session, parts[1]);

                case "diag":
                    if (!permission.UserHasPermission(playerId, PermAdmin))
                        return SaHome(playerId, session, "Admins only.");
                    session.Screen = "diag";
                    return SaDiag(playerId, session);

                default:
                    return SaHome(playerId, session, "Unknown action: " + command);
            }
        }

        private string SaHome(string playerId, SaSession session, string status)
        {
            List<ServerRecord> peers = GetPeerRecords();

            StringBuilder b = new StringBuilder();
            b.Append("v1\n");
            b.Append("title=").Append(Encode("Buggee Network")).Append('\n');
            b.Append("subtitle=").Append(Encode(config.DisplayName + "  [" + config.ServerId + "]")).Append('\n');
            b.Append("status=").Append(Encode(ComposeStatus(session, status, peers.Count))).Append('\n');
            b.Append("banner=").Append(Encode(SaIcons.Panel)).Append('\n');
            b.Append("background=").Append(Encode(SaIcons.Panel)).Append('\n');
            b.Append("input=||0|\n");

            int row = 0;
            b.Append(Row(row++, string.Empty, config.DisplayName,
                (config.Role ?? "?") + "  ·  " + onlineCount + " online  ·  " + gameVersion,
                false, false, SaIcons.Home, "YOU", SaIcons.TintMuted));

            if (peers.Count == 0)
            {
                b.Append(Row(row++, string.Empty, "No peers online",
                    redisReady ? "Only this server is registered." : "Redis is offline.",
                    false, false, SaIcons.Warn, string.Empty, SaIcons.TintWarn));
            }
            else
            {
                foreach (ServerRecord peer in peers)
                {
                    if (row >= 10) break;

                    bool modsMatch = string.Equals(peer.Mods, modSetHash, StringComparison.OrdinalIgnoreCase);
                    string sub = (peer.Role ?? "?") + "  ·  " + peer.Players + " online  ·  " +
                                 (modsMatch ? "mods ok" : "mod set differs");

                    b.Append(Row(row++, "target|" + peer.Id, peer.Name, sub, true, false,
                        IconForRole(peer.Role), (peer.Role ?? string.Empty).ToUpperInvariant(),
                        modsMatch ? string.Empty : SaIcons.TintWarn));
                }
            }

            int btn = 0;
            b.Append(Btn(btn++, "refresh", "Refresh", true, "Reload fleet status"));
            b.Append(Btn(btn++, "servers", "All Servers", true, "Browse the fleet"));
            if (permission.UserHasPermission(playerId, PermAdmin))
                b.Append(Btn(btn++, "diag", "Diagnostics", true, "Redis and build info"));
            b.Append(Btn(btn++, "hook|back", "Back", true, "Return to Server Assist"));

            return b.ToString();
        }

        private string SaServers(string playerId, SaSession session)
        {
            List<ServerRecord> peers = GetPeerRecords();

            StringBuilder b = new StringBuilder();
            b.Append("v1\n");
            b.Append("title=").Append(Encode("Buggee Network")).Append('\n');
            b.Append("subtitle=").Append(Encode("Fleet")).Append('\n');
            b.Append("status=").Append(Encode(ComposeStatus(session, null, peers.Count))).Append('\n');
            b.Append("banner=").Append(Encode(SaIcons.Panel)).Append('\n');
            b.Append("input=||0|\n");

            int row = 0;
            if (peers.Count == 0)
            {
                b.Append(Row(row++, string.Empty, "No peers online",
                    redisReady ? "Only this server is registered." : "Redis is offline.",
                    false, false, SaIcons.Warn, string.Empty, SaIcons.TintWarn));
            }
            else
            {
                foreach (ServerRecord peer in peers)
                {
                    if (row >= 10) break;

                    bool modsMatch = string.Equals(peer.Mods, modSetHash, StringComparison.OrdinalIgnoreCase);
                    b.Append(Row(row++, "target|" + peer.Id, peer.Name,
                        peer.Host + ":" + peer.Port + "  ·  " + peer.Role + "  ·  " + peer.Players + " online",
                        true, false, IconForRole(peer.Role), modsMatch ? string.Empty : "MODS",
                        modsMatch ? string.Empty : SaIcons.TintWarn));
                }
            }

            int btn = 0;
            b.Append(Btn(btn++, "home", "Overview", true, "Back to the overview"));
            b.Append(Btn(btn++, "refresh", "Refresh", true, "Reload fleet status"));
            b.Append(Btn(btn++, "hook|back", "Back", true, "Return to Server Assist"));

            return b.ToString();
        }

        private string SaConfirm(string playerId, SaSession session)
        {
            ServerRecord target;
            if (!redisReady || !TryGetPeer(session.SelectedServer, out target))
                return SaHome(playerId, session, "That server is no longer online.");

            StringBuilder b = new StringBuilder();
            b.Append("v1\n");
            b.Append("title=").Append(Encode("Buggee Network")).Append('\n');
            b.Append("subtitle=").Append(Encode("Travel to " + target.Name)).Append('\n');
            b.Append("status=").Append(Encode("You will be disconnected. Reconnect to "
                + target.Host + ":" + target.Port + ".")).Append('\n');
            b.Append("banner=").Append(Encode(SaIcons.Panel)).Append('\n');
            b.Append("input=||0|\n");

            bool modsMatch = string.Equals(target.Mods, modSetHash, StringComparison.OrdinalIgnoreCase);

            int row = 0;
            b.Append(Row(row++, string.Empty, target.Name, target.Host + ":" + target.Port,
                false, true, IconForRole(target.Role), (target.Role ?? string.Empty).ToUpperInvariant(),
                string.Empty));
            b.Append(Row(row++, string.Empty, "Role", target.Role ?? "unknown",
                false, false, SaIcons.Info, string.Empty, SaIcons.TintMuted));
            b.Append(Row(row++, string.Empty, "Players",
                target.Players.ToString(CultureInfo.InvariantCulture),
                false, false, SaIcons.Info, string.Empty, SaIcons.TintMuted));
            b.Append(Row(row++, string.Empty, "Mod set",
                modsMatch ? "matches this server" : "DIFFERENT — transfer will be refused",
                false, false, SaIcons.Info, string.Empty,
                modsMatch ? string.Empty : SaIcons.TintBad));

            int btn = 0;
            b.Append(Btn(btn++, "confirm|" + target.Id, "Travel Now", modsMatch,
                modsMatch ? "Save your state and reconnect" : "Mod sets must match"));
            b.Append(Btn(btn++, "home", "Cancel", true, "Stay on this server"));
            b.Append(Btn(btn++, "hook|back", "Back", true, "Return to Server Assist"));

            return b.ToString();
        }

        private string SaDiag(string playerId, SaSession session)
        {
            string ping = "offline";
            int registry = 0;

            if (redisReady)
            {
                try
                {
                    ping = redis.Ping() ?? "no reply";
                    string[] ids = redis.SMembers(KeyServers());
                    registry = ids == null ? 0 : ids.Length;
                }
                catch (Exception ex)
                {
                    ping = "error: " + ex.Message;
                }
            }

            StringBuilder b = new StringBuilder();
            b.Append("v1\n");
            b.Append("title=").Append(Encode("Buggee Network")).Append('\n');
            b.Append("subtitle=").Append(Encode("Diagnostics")).Append('\n');
            b.Append("status=").Append(Encode("Redis: " + ping)).Append('\n');
            b.Append("banner=").Append(Encode(SaIcons.Info)).Append('\n');
            b.Append("input=||0|\n");

            int row = 0;
            b.Append(Row(row++, string.Empty, "Redis", ping, false, false, SaIcons.Info,
                redisReady ? "OK" : "DOWN", redisReady ? string.Empty : SaIcons.TintBad));
            b.Append(Row(row++, string.Empty, "Endpoint",
                config.Redis.Host + ":" + config.Redis.Port + " / db " + config.Redis.Database,
                false, false, SaIcons.Info, string.Empty, SaIcons.TintMuted));
            b.Append(Row(row++, string.Empty, "Key prefix", config.Network.KeyPrefix,
                false, false, SaIcons.Info, string.Empty, SaIcons.TintMuted));
            b.Append(Row(row++, string.Empty, "Registry entries",
                registry.ToString(CultureInfo.InvariantCulture),
                false, false, SaIcons.Info, string.Empty, SaIcons.TintMuted));
            b.Append(Row(row++, string.Empty, "Game version", gameVersion,
                false, false, SaIcons.Info, string.Empty, SaIcons.TintMuted));
            b.Append(Row(row++, string.Empty, "Mod hash", modSetHash,
                false, false, SaIcons.Info, string.Empty, SaIcons.TintMuted));

            int btn = 0;
            b.Append(Btn(btn++, "home", "Overview", true, "Back to the overview"));
            b.Append(Btn(btn++, "diag", "Refresh", true, "Re-run diagnostics"));
            b.Append(Btn(btn++, "hook|back", "Back", true, "Return to Server Assist"));

            return b.ToString();
        }

        private string SaTravel(string playerId, SaSession session, string serverId)
        {
            IPlayer player = players.FindPlayerById(playerId);
            if (player == null || !player.IsConnected)
                return SaError("You are not connected.");

            if (string.Equals(serverId, config.ServerId, StringComparison.OrdinalIgnoreCase))
                return SaHome(playerId, session, "You are already on this server.");

            if (!config.Transfer.Enabled)
                return SaHome(playerId, session, "Transfers are disabled on this server.");

            TransferTo(player, serverId);

            session.Screen = "home";
            session.SelectedServer = null;
            return SaHome(playerId, session, "Transfer requested. Reconnect to the address shown.");
        }

        private string SaError(string message)
        {
            StringBuilder b = new StringBuilder();
            b.Append("v1\n");
            b.Append("title=").Append(Encode("Buggee Network")).Append('\n');
            b.Append("subtitle=").Append(Encode("Unavailable")).Append('\n');
            b.Append("status=").Append(Encode(message)).Append('\n');
            b.Append("banner=").Append(Encode(SaIcons.Warn)).Append('\n');
            b.Append("input=||0|\n");
            b.Append(Row(0, string.Empty, "No data", message, false, false,
                SaIcons.Warn, "ERROR", SaIcons.TintBad));
            b.Append(Btn(0, "hook|back", "Back", true, "Return to Server Assist"));
            return b.ToString();
        }

        private string ComposeStatus(SaSession session, string status, int peerCount)
        {
            string baseStatus;
            if (!config.Redis.Enabled)
                baseStatus = "Redis disabled (local-only mode).";
            else if (!redisReady)
                baseStatus = "Redis offline — fleet features unavailable.";
            else
                baseStatus = "Connected  ·  " + peerCount + " peer" + (peerCount == 1 ? string.Empty : "s") + " online";

            string extra = !string.IsNullOrEmpty(session.Status) ? session.Status : status;
            return string.IsNullOrEmpty(extra) ? baseStatus : extra + "  ·  " + baseStatus;
        }

        private List<ServerRecord> GetPeerRecords()
        {
            List<ServerRecord> result = new List<ServerRecord>();
            if (!redisReady)
                return result;

            try
            {
                string[] ids = redis.SMembers(KeyServers());
                if (ids == null)
                    return result;

                foreach (string id in ids)
                {
                    if (string.Equals(id, config.ServerId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    ServerRecord peer;
                    if (!TryGetPeer(id, out peer))
                        continue;

                    result.Add(peer);
                }

                result.Sort(delegate(ServerRecord a, ServerRecord c)
                {
                    int role = string.Compare(a.Role, c.Role, StringComparison.OrdinalIgnoreCase);
                    return role != 0 ? role : string.Compare(a.Name, c.Name, StringComparison.OrdinalIgnoreCase);
                });
            }
            catch (Exception ex)
            {
                DebugLog("GetPeerRecords failed: " + ex.Message);
            }

            return result;
        }

        private static string IconForRole(string role)
        {
            if (string.Equals(role, "home", StringComparison.OrdinalIgnoreCase))
                return SaIcons.Home;
            if (string.Equals(role, "horde", StringComparison.OrdinalIgnoreCase))
                return SaIcons.Warn;
            return SaIcons.Panel;
        }

        private SaSession GetSaSession(string playerId)
        {
            SaSession session;
            if (!saSessions.TryGetValue(playerId, out session))
            {
                session = new SaSession();
                saSessions[playerId] = session;
            }

            return session;
        }

        private static string Row(int index, string action, string label, string sub,
            bool enabled, bool selected, string icon, string badge, string tint)
        {
            return "row=" + index.ToString(CultureInfo.InvariantCulture) + "|" +
                   Encode(action) + "|" +
                   Encode(label) + "|" +
                   Encode(sub) + "|" +
                   (enabled ? "1" : "0") + "|" +
                   (selected ? "1" : "0") + "|" +
                   Encode(icon) + "|" +
                   Encode(badge) + "|" +
                   Encode(tint) + "\n";
        }

        /// <summary>
        /// The client currently parses exactly five button fields
        /// (index|action|label|enabled|tooltip), so do not append icon/tint here:
        /// they would be folded into the tooltip text.
        /// </summary>
        private static string Btn(int index, string action, string label, bool enabled, string tooltip)
        {
            return "btn=" + index.ToString(CultureInfo.InvariantCulture) + "|" +
                   Encode(action) + "|" +
                   Encode(label) + "|" +
                   (enabled ? "1" : "0") + "|" +
                   Encode(tooltip) + "\n";
        }

        private static string Encode(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

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
                return string.Empty;

            return value.Replace("%0A", "\n").Replace("%7C", "|").Replace("%25", "%");
        }

        #endregion

        #region Redis client

        /// <summary>
        /// Minimal, dependency-free RESP2 client. A single connection guarded by a lock is plenty
        /// for the command volume here (heartbeats + one call per connect/disconnect/transfer).
        /// </summary>
        private sealed class RedisClient : IDisposable
        {
            private readonly object gate = new object();
            private readonly RedisOptions options;

            private TcpClient tcp;
            private NetworkStream stream;

            public RedisClient(RedisOptions options)
            {
                this.options = options;
            }

            public void Connect()
            {
                lock (gate)
                {
                    CloseSocket();
                    OpenSocket();
                }
            }

            public void Dispose()
            {
                lock (gate)
                {
                    CloseSocket();
                }
            }

            private void OpenSocket()
            {
                TcpClient client = new TcpClient();
                client.NoDelay = true;

                System.Threading.Tasks.Task connectTask = client.ConnectAsync(options.Host, options.Port);
                if (!connectTask.Wait(options.ConnectTimeoutMs))
                {
                    try { client.Close(); } catch { }
                    throw new TimeoutException("Redis connect timeout to " + options.Host + ":" + options.Port);
                }

                if (connectTask.IsFaulted)
                {
                    try { client.Close(); } catch { }
                    throw new IOException("Redis connect failed", connectTask.Exception);
                }

                tcp = client;
                stream = client.GetStream();
                stream.ReadTimeout = options.CommandTimeoutMs;
                stream.WriteTimeout = options.CommandTimeoutMs;

                if (!string.IsNullOrEmpty(options.Password))
                    ExpectOk(WriteCommand("AUTH", options.Password));

                ExpectOk(WriteCommand("SELECT", options.Database.ToString(CultureInfo.InvariantCulture)));
            }

            private void CloseSocket()
            {
                try { if (stream != null) stream.Dispose(); } catch { }
                try { if (tcp != null) tcp.Close(); } catch { }
                stream = null;
                tcp = null;
            }

            private void EnsureOpen()
            {
                if (tcp != null && tcp.Connected && stream != null) return;
                CloseSocket();
                OpenSocket();
            }

            private static void ExpectOk(object reply)
            {
                string s = reply as string;
                if (s != "OK")
                    throw new IOException("Redis returned: " + (s ?? "<non-string>"));
            }

            // ---- Commands -------------------------------------------------

            public string Ping()
            {
                lock (gate)
                {
                    return WriteCommand("PING") as string;
                }
            }

            public byte[] GetBytes(string key)
            {
                lock (gate)
                {
                    object reply = WriteCommand("GET", key);
                    return reply as byte[];
                }
            }

            public string GetString(string key)
            {
                byte[] bytes = GetBytes(key);
                if (bytes == null) return null;
                return Encoding.UTF8.GetString(bytes);
            }

            public void SetBytes(string key, byte[] value, int ttlSeconds)
            {
                lock (gate)
                {
                    if (ttlSeconds > 0)
                        WriteCommand("SET", key, value, "EX", ttlSeconds.ToString(CultureInfo.InvariantCulture));
                    else
                        WriteCommand("SET", key, value);
                }
            }

            public void SetString(string key, string value, int ttlSeconds)
            {
                SetBytes(key, Encoding.UTF8.GetBytes(value ?? string.Empty), ttlSeconds);
            }

            public void Expire(string key, int seconds)
            {
                lock (gate)
                {
                    WriteCommand("EXPIRE", key, seconds.ToString(CultureInfo.InvariantCulture));
                }
            }

            public long Del(string key)
            {
                lock (gate)
                {
                    object reply = WriteCommand("DEL", key);
                    return reply is long ? (long)reply : 0L;
                }
            }

            public void HSet(string key, string field, string value)
            {
                lock (gate)
                {
                    WriteCommand("HSET", key, field, value ?? string.Empty);
                }
            }

            public Dictionary<string, string> HGetAll(string key)
            {
                lock (gate)
                {
                    object reply = WriteCommand("HGETALL", key);
                    object[] parts = reply as object[];
                    if (parts == null || parts.Length == 0) return null;

                    Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
                    for (int i = 0; i + 1 < parts.Length; i += 2)
                    {
                        string field = DecodeString(parts[i]);
                        string value = DecodeString(parts[i + 1]);
                        if (field != null) result[field] = value;
                    }

                    return result;
                }
            }

            public void SAdd(string key, string member)
            {
                lock (gate)
                {
                    WriteCommand("SADD", key, member);
                }
            }

            public void SRem(string key, string member)
            {
                lock (gate)
                {
                    WriteCommand("SREM", key, member);
                }
            }

            public string[] SMembers(string key)
            {
                lock (gate)
                {
                    object reply = WriteCommand("SMEMBERS", key);
                    object[] parts = reply as object[];
                    if (parts == null) return new string[0];

                    string[] result = new string[parts.Length];
                    for (int i = 0; i < parts.Length; i++)
                        result[i] = DecodeString(parts[i]);
                    return result;
                }
            }

            public object Eval(string script, string[] keys, string[] args)
            {
                lock (gate)
                {
                    List<object> payload = new List<object>();
                    payload.Add(script);
                    payload.Add((keys == null ? 0 : keys.Length).ToString(CultureInfo.InvariantCulture));
                    if (keys != null) payload.AddRange(keys.Cast<object>());
                    if (args != null) payload.AddRange(args.Cast<object>());

                    return WriteCommand(payload.ToArray());
                }
            }

            private static string DecodeString(object value)
            {
                byte[] bytes = value as byte[];
                if (bytes != null) return Encoding.UTF8.GetString(bytes);
                return value as string;
            }

            // ---- RESP2 ----------------------------------------------------

            private object WriteCommand(params object[] args)
            {
                EnsureOpen();

                try
                {
                    WriteRaw(args);
                    return ReadReply();
                }
                catch (IOException)
                {
                    // One reconnect attempt for transient socket failures.
                    CloseSocket();
                    EnsureOpen();
                    WriteRaw(args);
                    return ReadReply();
                }
                catch (SocketException)
                {
                    CloseSocket();
                    EnsureOpen();
                    WriteRaw(args);
                    return ReadReply();
                }
            }

            private void WriteRaw(object[] args)
            {
                MemoryStream buffer = new MemoryStream(128);
                WriteAscii(buffer, "*" + args.Length + "\r\n");

                for (int i = 0; i < args.Length; i++)
                {
                    byte[] bytes = ToBytes(args[i]);
                    WriteAscii(buffer, "$" + bytes.Length + "\r\n");
                    buffer.Write(bytes, 0, bytes.Length);
                    WriteAscii(buffer, "\r\n");
                }

                byte[] payload = buffer.ToArray();
                stream.Write(payload, 0, payload.Length);
                stream.Flush();
            }

            private static byte[] ToBytes(object value)
            {
                byte[] bytes = value as byte[];
                if (bytes != null) return bytes;

                if (value is int) return Encoding.ASCII.GetBytes(((int)value).ToString(CultureInfo.InvariantCulture));
                if (value is long) return Encoding.ASCII.GetBytes(((long)value).ToString(CultureInfo.InvariantCulture));
                return Encoding.UTF8.GetBytes(value == null ? string.Empty : value.ToString());
            }

            private static void WriteAscii(Stream target, string text)
            {
                byte[] bytes = Encoding.ASCII.GetBytes(text);
                target.Write(bytes, 0, bytes.Length);
            }

            private object ReadReply()
            {
                int prefix = stream.ReadByte();
                if (prefix < 0) throw new IOException("Redis closed the connection");

                switch ((char)prefix)
                {
                    case '+':
                        return ReadLine();

                    case '-':
                        throw new IOException("Redis error: " + ReadLine());

                    case ':':
                        return long.Parse(ReadLine(), CultureInfo.InvariantCulture);

                    case '$':
                    {
                        int length = int.Parse(ReadLine(), CultureInfo.InvariantCulture);
                        if (length < 0) return null;

                        byte[] data = ReadExact(length);
                        ReadExact(2); // trailing CRLF
                        return data;
                    }

                    case '*':
                    {
                        int count = int.Parse(ReadLine(), CultureInfo.InvariantCulture);
                        if (count < 0) return null;

                        object[] items = new object[count];
                        for (int i = 0; i < count; i++)
                            items[i] = ReadReply();
                        return items;
                    }

                    default:
                        throw new IOException("Unexpected RESP prefix: " + (char)prefix);
                }
            }

            private string ReadLine()
            {
                StringBuilder sb = new StringBuilder(32);
                while (true)
                {
                    int b = stream.ReadByte();
                    if (b < 0) throw new IOException("Redis closed the connection");

                    if (b == '\r')
                    {
                        int lf = stream.ReadByte();
                        if (lf != '\n') throw new IOException("Malformed RESP line");
                        return sb.ToString();
                    }

                    sb.Append((char)b);
                }
            }

            private byte[] ReadExact(int length)
            {
                byte[] buffer = new byte[length];
                int offset = 0;

                while (offset < length)
                {
                    int read = stream.Read(buffer, offset, length - offset);
                    if (read <= 0) throw new IOException("Redis connection closed mid-reply");
                    offset += read;
                }

                return buffer;
            }
        }

        #endregion
    }
}
