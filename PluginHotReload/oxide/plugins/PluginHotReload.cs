using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Oxide.Core;
using Oxide.Core.Extensions;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("PluginHotReload", "TheRealM18", "1.0.1")]
    [Description("Watches oxide/plugins for .cs changes and hot-reloads only Oxide plugins, with filtering, debouncing, logging and manual reload commands.")]
    public class PluginHotReload : CovalencePlugin
    {
        private const string PermAdmin = "pluginhotreload.admin";
        private const string PermUse = "pluginhotreload.use";
        private const string DefaultSelfFile = "PluginHotReload";
        private const string ServerAssistHookId = "pluginhotreload";
        private const string ServerAssistIcon = "sprite:sa_plugin_hot_reload";
        private const string StatusIcon = "sprite:sa_nav_server";
        private const int GuiRows = 10;
        private const int GuiButtons = 10;

        private static readonly Dictionary<string, string> PluginIcons =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "AdminKits", "sprite:sa_admin_kits" },
                { "AdminShop", "sprite:sa_admin_shop" },
                { "AdminSuite", "sprite:sa_admin_suite" },
                { "AuctionHouse", "sprite:sa_auction_house" },
                { "DailyRewards", "sprite:sa_daily_rewards" },
                { "DynamicBackpack", "sprite:sa_dynamic_backpack" },
                { "PermissionGui", "sprite:sa_permission_gui" },
                { "PluginHotReload", "sprite:sa_plugin_hot_reload" },
                { "VoteSkipNight", "sprite:sa_vote_skip_night" },
                { "QLNPack", "sprite:sa_qln_pack" },
                { "QLNPackServer", "sprite:sa_qln_pack" },
                { "QLNPackBootstrap", "sprite:sa_qln_pack" },
                { "ServerAssist", "sprite:sa_server_assist" }
            };

        [PluginReference] private Plugin ServerAssist = null;

        private Configuration config;
        private FileSystemWatcher watcher;
        private Timer pollTimer;
        private string selfRelative = DefaultSelfFile;
        private bool serverAssistHookRegistered;

        private readonly Dictionary<string, GuiSession> guiSessions =
            new Dictionary<string, GuiSession>(StringComparer.OrdinalIgnoreCase);

        private readonly object pendingLock = new object();
        private readonly Dictionary<string, PendingChange> pending =
            new Dictionary<string, PendingChange>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, string> contentHashes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly object reloadLock = new object();
        private readonly Dictionary<string, DateTime> pendingReloads =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private readonly Queue<DateTime> reloadTimes = new Queue<DateTime>();
        private readonly List<FileSystemWatcher> disabledCoreWatchers = new List<FileSystemWatcher>();

        private bool weDisabledCoreWatcher;
        private bool autoWatchActive;
        private bool coreWatcherActive;
        private bool warnedCoreWatcher;
        private bool loggedDisabled;
        private int reloadCount;
        private string lastReload = "none";
        private string lastResult = "none";

        #region Lifecycle

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            permission.RegisterPermission(PermUse, this);

            LoadConfig();

            string relative = GetRelativePluginName(Filename);
            if (!string.IsNullOrEmpty(relative))
            {
                selfRelative = relative;
            }

            AddCovalenceCommand(new[] { "hotreload", "phr", "plreload" }, nameof(CommandHotReload));

            timer.Once(1f, EnsurePermissions);
            timer.Once(2f, TryStartWatcher);
            timer.Once(3f, TryRegisterServerAssist);
            timer.Once(6f, TryRegisterServerAssist);
        }

        private void OnServerInitialized()
        {
            EnsurePermissions();
            TryStartWatcher();
            TryRegisterServerAssist();
        }

        private void OnServerAssistReady()
        {
            TryRegisterServerAssist();
        }

        private void Unload()
        {
            StopWatcher();
            UnregisterServerAssist();

            bool shuttingDown = false;
            try
            {
                shuttingDown = Interface.Oxide != null && Interface.Oxide.IsShuttingDown;
            }
            catch
            {
                // Ignore and restore the core watcher.
            }

            if (!shuttingDown)
            {
                RestoreCoreWatcher();
            }

            lock (pendingLock)
            {
                pending.Clear();
            }

            lock (reloadLock)
            {
                pendingReloads.Clear();
            }

            contentHashes.Clear();
            guiSessions.Clear();
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
                Interface.Oxide.LogWarning("[PluginHotReload] config could not be read: " + exception.Message);
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

        private static void NormalizeConfig(Configuration value)
        {
            value.DebounceSeconds = Math.Max(0.2f, Math.Min(10f, value.DebounceSeconds));
            value.MaxReloadsPerMinute = Math.Max(1, Math.Min(600, value.MaxReloadsPerMinute));
            value.ReloadTimeoutSeconds = Math.Max(10, Math.Min(600, value.ReloadTimeoutSeconds));
            value.Ignore = CleanList(value.Ignore);
            value.IncludeOnly = CleanList(value.IncludeOnly);
        }

        private void NormalizeConfig()
        {
            NormalizeConfig(config);
        }

        private static List<string> CleanList(List<string> list)
        {
            List<string> result = new List<string>();
            if (list == null)
            {
                return result;
            }

            foreach (string item in list)
            {
                string value = item == null ? null : item.Trim();
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                if (!result.Any(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(value);
                }
            }

            return result;
        }

        private void EnsurePermissions()
        {
            if (config == null || !config.GrantAdminPermission)
            {
                return;
            }

            string group;
            try
            {
                group = Interface.Oxide.Config.Options.DefaultGroups.Administrators;
            }
            catch
            {
                return;
            }

            if (string.IsNullOrEmpty(group) || !permission.GroupExists(group))
            {
                return;
            }

            GrantGroupIfMissing(group, PermAdmin);
            GrantGroupIfMissing(group, PermUse);
        }

        private void GrantGroupIfMissing(string group, string permissionName)
        {
            if (permission.GroupHasPermission(group, permissionName))
            {
                return;
            }

            permission.GrantGroupPermission(group, permissionName, this);
        }

        #endregion

        #region Watcher

        private string PluginDirectory
        {
            get
            {
                try
                {
                    return Interface.Oxide == null ? null : Interface.Oxide.PluginDirectory;
                }
                catch
                {
                    return null;
                }
            }
        }

        private void TryStartWatcher()
        {
            if (config == null)
            {
                return;
            }

            if (!config.Enabled)
            {
                if (!loggedDisabled)
                {
                    loggedDisabled = true;
                    Interface.Oxide.LogInfo("[PluginHotReload] automatic reload is disabled in the config.");
                }

                // If this plugin owns the watcher, keep it off after a reload
                // too. Otherwise Oxide's built-in watcher would come back and
                // silently re-enable automatic reloading.
                if (config.TakeOverCoreWatcher && !weDisabledCoreWatcher && IsCoreWatcherConfigured())
                {
                    if (TryDisableCoreWatcher())
                    {
                        coreWatcherActive = false;
                    }
                }
                else if (!config.TakeOverCoreWatcher)
                {
                    coreWatcherActive = IsCoreWatcherConfigured();
                }

                return;
            }

            if (watcher != null || autoWatchActive)
            {
                return;
            }

            string directory = PluginDirectory;
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                Interface.Oxide.LogWarning("[PluginHotReload] plugin directory was not found.");
                return;
            }

            coreWatcherActive = IsCoreWatcherConfigured();
            if (coreWatcherActive)
            {
                if (config.TakeOverCoreWatcher && TryDisableCoreWatcher())
                {
                    coreWatcherActive = false;
                    Interface.Oxide.LogInfo("[PluginHotReload] took over plugin watching from Oxide's built-in watcher.");
                }
                else
                {
                    if (!warnedCoreWatcher)
                    {
                        warnedCoreWatcher = true;
                        Interface.Oxide.LogWarning(
                            "[PluginHotReload] Oxide's built-in plugin watcher is active, so no second watcher will be started. " +
                            "Set TakeOverCoreWatcher to true (default) or set PluginWatchers to false in oxide.config.json, then reload this plugin.");
                    }

                    return;
                }
            }

            StartWatcher(directory);
        }

        private bool IsCoreWatcherConfigured()
        {
            try
            {
                return Interface.Oxide != null &&
                       Interface.Oxide.Config != null &&
                       Interface.Oxide.Config.Options != null &&
                       Interface.Oxide.Config.Options.PluginWatchers;
            }
            catch
            {
                return false;
            }
        }

        private bool TryDisableCoreWatcher()
        {
            try
            {
                Extension extension = Interface.Oxide.GetExtension("CSharp");
                if (extension == null)
                {
                    return false;
                }

                PropertyInfo property = extension.GetType().GetProperty(
                    "Watcher",
                    BindingFlags.Public | BindingFlags.Instance);

                object fsWatcher = property == null ? null : property.GetValue(extension, null);
                if (fsWatcher == null)
                {
                    return false;
                }

                List<FileSystemWatcher> found = new List<FileSystemWatcher>();
                CollectFileSystemWatchers(fsWatcher, found, new HashSet<object>(), 0);
                if (found.Count == 0)
                {
                    return false;
                }

                foreach (FileSystemWatcher item in found)
                {
                    try
                    {
                        item.EnableRaisingEvents = false;
                        if (!disabledCoreWatchers.Contains(item))
                        {
                            disabledCoreWatchers.Add(item);
                        }
                    }
                    catch
                    {
                        // Ignore a watcher which cannot be disabled.
                    }
                }

                weDisabledCoreWatcher = disabledCoreWatchers.Count > 0;
                return weDisabledCoreWatcher;
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[PluginHotReload] could not take over the built-in watcher: " + exception.Message);
                return false;
            }
        }

        private static void CollectFileSystemWatchers(object source, List<FileSystemWatcher> results, HashSet<object> seen, int depth)
        {
            if (source == null || depth > 3 || !seen.Add(source))
            {
                return;
            }

            Type type = source.GetType();
            while (type != null && type != typeof(object))
            {
                FieldInfo[] fields;
                try
                {
                    fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                }
                catch
                {
                    break;
                }

                foreach (FieldInfo field in fields)
                {
                    object value;
                    try
                    {
                        value = field.GetValue(source);
                    }
                    catch
                    {
                        continue;
                    }

                    if (value == null)
                    {
                        continue;
                    }

                    FileSystemWatcher item = value as FileSystemWatcher;
                    if (item != null)
                    {
                        if (!results.Contains(item))
                        {
                            results.Add(item);
                        }

                        continue;
                    }

                    System.Collections.IDictionary dictionary = value as System.Collections.IDictionary;
                    if (dictionary == null)
                    {
                        continue;
                    }

                    foreach (object entry in dictionary.Values)
                    {
                        FileSystemWatcher entryWatcher = entry as FileSystemWatcher;
                        if (entryWatcher != null)
                        {
                            if (!results.Contains(entryWatcher))
                            {
                                results.Add(entryWatcher);
                            }
                        }
                        else
                        {
                            CollectFileSystemWatchers(entry, results, seen, depth + 1);
                        }
                    }
                }

                type = type.BaseType;
            }
        }

        private void StartWatcher(string directory)
        {
            try
            {
                watcher = new FileSystemWatcher(directory, "*.cs");
                watcher.IncludeSubdirectories = config.WatchSubdirectories;
                watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName |
                                       NotifyFilters.Size | NotifyFilters.CreationTime;
                watcher.Changed += OnFileChanged;
                watcher.Created += OnFileChanged;
                watcher.Deleted += OnFileChanged;
                watcher.Renamed += OnFileRenamed;
                watcher.Error += OnWatcherError;
                RefreshContentHashes();
                watcher.EnableRaisingEvents = true;

                pollTimer = timer.Every(0.5f, ProcessPendingChanges);
                autoWatchActive = true;
                loggedDisabled = false;
                warnedCoreWatcher = false;

                Interface.Oxide.LogInfo(
                    "[PluginHotReload] watching " + directory + " for .cs changes (debounce " +
                    config.DebounceSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s).");
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogError("[PluginHotReload] could not start the file watcher: " + exception);
                StopWatcher();
                RestoreCoreWatcher();
            }
        }

        private void StopWatcher()
        {
            autoWatchActive = false;

            if (pollTimer != null)
            {
                try
                {
                    pollTimer.Destroy();
                }
                catch
                {
                    // Ignore.
                }

                pollTimer = null;
            }

            if (watcher == null)
            {
                return;
            }

            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Changed -= OnFileChanged;
                watcher.Created -= OnFileChanged;
                watcher.Deleted -= OnFileChanged;
                watcher.Renamed -= OnFileRenamed;
                watcher.Error -= OnWatcherError;
                watcher.Dispose();
            }
            catch
            {
                // Ignore.
            }

            watcher = null;
        }

        private void RestoreCoreWatcher()
        {
            if (!weDisabledCoreWatcher)
            {
                return;
            }

            foreach (FileSystemWatcher item in disabledCoreWatchers)
            {
                try
                {
                    item.EnableRaisingEvents = true;
                }
                catch
                {
                    // Ignore.
                }
            }

            disabledCoreWatchers.Clear();
            weDisabledCoreWatcher = false;
            coreWatcherActive = IsCoreWatcherConfigured();
        }

        private void RestartWatcher()
        {
            if (config == null || !config.Enabled)
            {
                return;
            }

            string directory = PluginDirectory;
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return;
            }

            StopWatcher();
            StartWatcher(directory);
        }

        private void OnFileChanged(object sender, FileSystemEventArgs args)
        {
            if (args == null || string.IsNullOrEmpty(args.FullPath))
            {
                return;
            }

            QueueChange(args.FullPath, args.ChangeType);
        }

        private void OnFileRenamed(object sender, RenamedEventArgs args)
        {
            if (args == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(args.OldFullPath))
            {
                QueueChange(args.OldFullPath, WatcherChangeTypes.Deleted);
            }

            if (!string.IsNullOrEmpty(args.FullPath))
            {
                QueueChange(args.FullPath, WatcherChangeTypes.Created);
            }
        }

        private void OnWatcherError(object sender, ErrorEventArgs args)
        {
            Exception exception = args == null ? null : args.GetException();
            Interface.Oxide.LogError("[PluginHotReload] file watcher error: " + (exception == null ? "unknown" : exception.ToString()));

            // The OS buffer can overflow when a lot of files change at once.
            timer.Once(2f, RestartWatcher);
        }

        #endregion

        #region Change queue

        private void QueueChange(string fullPath, WatcherChangeTypes type)
        {
            string relative = GetRelativePluginName(fullPath);
            if (string.IsNullOrEmpty(relative))
            {
                return;
            }

            string key = relative.ToLowerInvariant();

            lock (pendingLock)
            {
                PendingChange change;
                if (!pending.TryGetValue(key, out change))
                {
                    change = new PendingChange();
                    pending[key] = change;
                }

                change.Relative = relative;
                change.FullPath = fullPath;
                change.LastUtc = DateTime.UtcNow;

                switch (type)
                {
                    case WatcherChangeTypes.Created:
                        change.Type = change.Type == WatcherChangeTypes.Deleted
                            ? WatcherChangeTypes.Changed
                            : WatcherChangeTypes.Created;
                        break;

                    case WatcherChangeTypes.Deleted:
                        if (change.Type == WatcherChangeTypes.Created)
                        {
                            pending.Remove(key);
                        }
                        else
                        {
                            change.Type = WatcherChangeTypes.Deleted;
                        }

                        break;

                    default:
                        if (change.Type != WatcherChangeTypes.Created)
                        {
                            change.Type = WatcherChangeTypes.Changed;
                        }

                        break;
                }
            }
        }

        private void ProcessPendingChanges()
        {
            try
            {
                ProcessPendingChangesCore();
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogError("[PluginHotReload] change processing failed: " + exception);
            }
        }

        private void ProcessPendingChangesCore()
        {
            List<PendingChange> ready = null;
            DateTime cutoff = DateTime.UtcNow.AddSeconds(-Math.Max(0.2f, config.DebounceSeconds));

            lock (pendingLock)
            {
                foreach (PendingChange change in pending.Values)
                {
                    if (change.LastUtc > cutoff)
                    {
                        continue;
                    }

                    if (ready == null)
                    {
                        ready = new List<PendingChange>();
                    }

                    ready.Add(change);
                }

                if (ready != null)
                {
                    foreach (PendingChange change in ready)
                    {
                        pending.Remove(change.Relative.ToLowerInvariant());
                    }
                }
            }

            if (ready != null)
            {
                foreach (PendingChange change in ready)
                {
                    ProcessChange(change);
                }
            }

            ExpirePendingReloads();
        }

        private void ProcessChange(PendingChange change)
        {
            if (change == null || string.IsNullOrEmpty(change.Relative) || !ShouldHandle(change.Relative))
            {
                return;
            }

            switch (change.Type)
            {
                case WatcherChangeTypes.Deleted:
                    contentHashes.Remove(change.Relative);
                    if (config.UnloadOnDelete)
                    {
                        RequestUnload(change.Relative, "file deleted");
                    }

                    break;

                case WatcherChangeTypes.Created:
                    if (config.ReloadOnCreate && ContentChanged(change))
                    {
                        RequestReload(change.Relative, "file created");
                    }

                    break;

                default:
                    if (ContentChanged(change))
                    {
                        RequestReload(change.Relative, "file changed");
                    }

                    break;
            }
        }

        private bool ContentChanged(PendingChange change)
        {
            if (change == null || string.IsNullOrEmpty(change.Relative))
            {
                return true;
            }

            string hash = ComputeFileHash(change.FullPath);
            if (string.IsNullOrEmpty(hash))
            {
                // The file may be mid-write or locked. Let Oxide try the reload
                // rather than silently dropping the change.
                return true;
            }

            string previous;
            bool hadPrevious = contentHashes.TryGetValue(change.Relative, out previous);
            contentHashes[change.Relative] = hash;

            // Editors and build tools sometimes touch every file in a folder.
            // Only reload a file when its contents actually changed.
            return !hadPrevious || !string.Equals(previous, hash, StringComparison.Ordinal);
        }

        private void RefreshContentHashes()
        {
            contentHashes.Clear();

            string directory = PluginDirectory;
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            foreach (string relative in GetPluginFiles())
            {
                string fullPath = Path.Combine(
                    directory,
                    relative.Replace('\\', Path.DirectorySeparatorChar) + ".cs");
                string hash = ComputeFileHash(fullPath);
                if (!string.IsNullOrEmpty(hash))
                {
                    contentHashes[relative] = hash;
                }
            }
        }

        private static string ComputeFileHash(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(stream);
                    StringBuilder builder = new StringBuilder(hash.Length * 2);
                    for (int i = 0; i < hash.Length; i++)
                    {
                        builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                    }

                    return builder.ToString();
                }
            }
            catch
            {
                return null;
            }
        }

        private void ExpirePendingReloads()
        {
            if (pendingReloads.Count == 0)
            {
                return;
            }

            List<string> expired = null;
            DateTime cutoff = DateTime.UtcNow.AddSeconds(-config.ReloadTimeoutSeconds);

            lock (reloadLock)
            {
                foreach (KeyValuePair<string, DateTime> pair in pendingReloads)
                {
                    if (pair.Value > cutoff)
                    {
                        continue;
                    }

                    if (expired == null)
                    {
                        expired = new List<string>();
                    }

                    expired.Add(pair.Key);
                }

                if (expired != null)
                {
                    foreach (string key in expired)
                    {
                        pendingReloads.Remove(key);
                    }
                }
            }

            if (expired == null)
            {
                return;
            }

            foreach (string key in expired)
            {
                lastResult = "timeout";
                Interface.Oxide.LogWarning(
                    "[PluginHotReload] reload of " + key + " did not complete within " +
                    config.ReloadTimeoutSeconds + " seconds. Check the server log for compile errors.");
                Interface.Oxide.CallHook("OnPluginHotReloadFailed", key, "timeout");
            }
        }

        #endregion

        #region Reload actions

        private bool RequestReload(string relative, string reason)
        {
            if (string.IsNullOrEmpty(relative) || !ShouldHandle(relative))
            {
                return false;
            }

            // Reloading an include file tells Oxide to reload every plugin that
            // references it. Never do that from a single-plugin button or
            // command unless the server owner explicitly opts in. Automatic
            // include watching still uses the normal dependency reload.
            if (IsIncludeFile(relative) && IsManualReason(reason) &&
                (config == null || !config.AllowManualIncludeReload))
            {
                Interface.Oxide.LogWarning(
                    "[PluginHotReload] refusing manual reload of include file '" + relative +
                    "' because it reloads every plugin that references it. Edit the include file to trigger a dependency reload, or set AllowManualIncludeReload to true.");
                return false;
            }

            if (IsRelativeSelf(relative) && !config.ReloadSelf)
            {
                if (config.LogReloads)
                {
                    Interface.Oxide.LogInfo("[PluginHotReload] skipping self-reload (ReloadSelf is false).");
                }

                return false;
            }

            if (!AllowReload())
            {
                Interface.Oxide.LogWarning("[PluginHotReload] reload rate limit reached; skipping " + relative + ".");
                return false;
            }

            if (config.LogReloads)
            {
                Interface.Oxide.LogInfo("[PluginHotReload] reloading " + relative + " (" + reason + ").");
            }

            lock (reloadLock)
            {
                pendingReloads[relative] = DateTime.UtcNow;
            }

            reloadCount++;
            lastReload = relative + " @ " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            lastResult = "requested";
            Interface.Oxide.CallHook("OnPluginHotReloadRequested", relative, reason);

            try
            {
                if (!Interface.Oxide.ReloadPlugin(relative))
                {
                    lock (reloadLock)
                    {
                        pendingReloads.Remove(relative);
                    }

                    lastResult = "rejected";
                    Interface.Oxide.LogWarning("[PluginHotReload] Oxide did not accept the reload request for " + relative + ".");
                    Interface.Oxide.CallHook("OnPluginHotReloadFailed", relative, "rejected");
                    return false;
                }
            }
            catch (Exception exception)
            {
                lock (reloadLock)
                {
                    pendingReloads.Remove(relative);
                }

                lastResult = "error";
                Interface.Oxide.LogError("[PluginHotReload] reload failed for " + relative + ": " + exception);
                Interface.Oxide.CallHook("OnPluginHotReloadFailed", relative, exception.Message);
                return false;
            }

            return true;
        }

        private bool RequestUnload(string relative, string reason)
        {
            Plugin plugin = FindLoadedPluginByFile(relative);
            if (plugin == null)
            {
                return false;
            }

            if (config.LogReloads)
            {
                Interface.Oxide.LogInfo("[PluginHotReload] unloading " + plugin.Name + " (" + reason + ").");
            }

            lastReload = plugin.Name + " unloaded @ " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            lastResult = "unloaded";
            Interface.Oxide.CallHook("OnPluginHotReloadRequested", relative, reason);

            try
            {
                if (Interface.Oxide.UnloadPlugin(plugin.Name))
                {
                    Interface.Oxide.CallHook("OnPluginHotReloadUnloaded", relative);
                    return true;
                }
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogError("[PluginHotReload] unload failed for " + plugin.Name + ": " + exception);
                Interface.Oxide.CallHook("OnPluginHotReloadFailed", relative, exception.Message);
                return false;
            }

            lastResult = "unload failed";
            Interface.Oxide.CallHook("OnPluginHotReloadFailed", relative, "unload failed");
            return false;
        }

        private bool AllowReload()
        {
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-1);
            while (reloadTimes.Count > 0 && reloadTimes.Peek() < cutoff)
            {
                reloadTimes.Dequeue();
            }

            if (reloadTimes.Count >= config.MaxReloadsPerMinute)
            {
                return false;
            }

            reloadTimes.Enqueue(DateTime.UtcNow);
            return true;
        }

        private int ReloadAllInternal(string reason)
        {
            string[] files = GetPluginFiles();
            string self = null;
            int count = 0;

            foreach (string relative in files)
            {
                if (!ShouldHandle(relative) || IsIncludeFile(relative))
                {
                    continue;
                }

                if (IsRelativeSelf(relative))
                {
                    self = relative;
                    continue;
                }

                // ReloadAll only touches plugins that are currently loaded.
                // Use /hotreload scan to load new files.
                if (FindLoadedPluginByFile(relative) == null)
                {
                    continue;
                }

                if (RequestReload(relative, reason))
                {
                    count++;
                }
            }

            if (!string.IsNullOrEmpty(self) && config.ReloadSelf)
            {
                string selfCopy = self;
                timer.Once(1f, () => RequestReload(selfCopy, reason));
                count++;
            }

            return count;
        }

        private int ScanAndLoad()
        {
            int count = 0;
            foreach (string relative in GetPluginFiles())
            {
                if (!ShouldHandle(relative) || IsRelativeSelf(relative) || IsIncludeFile(relative))
                {
                    continue;
                }

                if (FindLoadedPluginByFile(relative) != null)
                {
                    continue;
                }

                if (RequestReload(relative, "scan"))
                {
                    count++;
                }
            }

            return count;
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin == null)
            {
                return;
            }

            if (string.Equals(plugin.Name, "ServerAssist", StringComparison.OrdinalIgnoreCase))
            {
                TryRegisterServerAssist();
            }

            string relative = GetRelativeFromFilename(plugin);
            if (string.IsNullOrEmpty(relative))
            {
                return;
            }

            bool wasPending = false;
            lock (reloadLock)
            {
                if (pendingReloads.ContainsKey(relative))
                {
                    pendingReloads.Remove(relative);
                    wasPending = true;
                }
            }

            if (!wasPending)
            {
                return;
            }

            lastResult = "success";
            if (config != null && config.LogReloads)
            {
                Interface.Oxide.LogInfo("[PluginHotReload] reloaded " + relative + " successfully.");
            }

            Interface.Oxide.CallHook("OnPluginHotReloaded", relative, true);
        }

        private void OnPluginUnloaded(Plugin plugin)
        {
            if (plugin != null &&
                string.Equals(plugin.Name, "ServerAssist", StringComparison.OrdinalIgnoreCase))
            {
                serverAssistHookRegistered = false;
            }
        }

        #endregion

        #region Commands

        private void CommandHotReload(IPlayer player, string command, string[] args)
        {
            if (!CanUse(player))
            {
                Reply(player, "You need the '" + PermUse + "' or '" + PermAdmin + "' permission.");
                return;
            }

            string sub = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "status";

            switch (sub)
            {
                case "status":
                    Reply(player, "Auto-watch: " + (autoWatchActive ? "on" : "off") +
                                  " | built-in watcher: " + (coreWatcherActive ? "on" : "off"));
                    Reply(player, "Tracked .cs files: " + GetPluginFiles().Length +
                                  " | pending changes: " + PendingCount() +
                                  " | debounce: " + config.DebounceSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s");
                    Reply(player, "Last: " + lastReload + " | " + lastResult + " | reloads this session: " + reloadCount);
                    break;

                case "list":
                case "plugins":
                    ReplyPluginList(player, args != null && args.Length > 1 ? args[1] : null);
                    break;

                case "reload":
                    if (!RequireAdmin(player))
                    {
                        return;
                    }

                    if (args == null || args.Length < 2)
                    {
                        Reply(player, "Usage: /hotreload reload <plugin>");
                        return;
                    }

                    string relative = ResolvePluginName(args[1]);
                    if (string.IsNullOrEmpty(relative))
                    {
                        Reply(player, "No plugin file matched '" + args[1] + "'.");
                        return;
                    }

                    if (IsIncludeFile(relative))
                    {
                        Reply(player, "That is an include file; reloading it would reload every plugin that references it. Edit the include file to trigger the dependency reload, or set AllowManualIncludeReload to true.");
                        return;
                    }

                    if (RequestReload(relative, "command"))
                    {
                        Reply(player, "Reload requested for " + relative + ".");
                    }
                    else
                    {
                        Reply(player, "Reload was not started for " + relative + ". Check the server log or the ignore list.");
                    }

                    break;

                case "reloadall":
                    if (!RequireAdmin(player))
                    {
                        return;
                    }

                    if (args == null || args.Length < 2 ||
                        !string.Equals(args[1], "confirm", StringComparison.OrdinalIgnoreCase))
                    {
                        Reply(player, "This reloads every loaded plugin. Run /hotreload reloadall confirm");
                        return;
                    }

                    int reloaded = ReloadAllInternal("command");
                    Reply(player, "Reload requested for " + reloaded + " plugin(s).");
                    break;

                case "scan":
                    if (!RequireAdmin(player))
                    {
                        return;
                    }

                    int scanned = ScanAndLoad();
                    Reply(player, "Scan complete. Load requested for " + scanned + " new plugin(s).");
                    break;

                case "enable":
                    if (!RequireAdmin(player))
                    {
                        return;
                    }

                    config.Enabled = true;
                    SaveConfig();
                    TryStartWatcher();
                    Reply(player, "Automatic reload enabled. " + (autoWatchActive ? "The watcher is running." : "The watcher could not start; check the server log."));
                    break;

                case "disable":
                    if (!RequireAdmin(player))
                    {
                        return;
                    }

                    config.Enabled = false;
                    SaveConfig();
                    StopWatcher();
                    Reply(player, "Automatic reload disabled. Manual /hotreload commands still work.");
                    break;

                case "ignore":
                    if (!RequireAdmin(player))
                    {
                        return;
                    }

                    HandleIgnore(player, args, true);
                    break;

                case "unignore":
                    if (!RequireAdmin(player))
                    {
                        return;
                    }

                    HandleIgnore(player, args, false);
                    break;

                case "reloadconfig":
                    if (!RequireAdmin(player))
                    {
                        return;
                    }

                    StopWatcher();
                    LoadConfig();
                    TryStartWatcher();
                    Reply(player, "Config reloaded.");
                    break;

                case "help":
                    ReplyHelp(player);
                    break;

                default:
                    Reply(player, "Unknown subcommand. Try /hotreload help.");
                    break;
            }
        }

        private bool CanUse(IPlayer player)
        {
            if (player == null)
            {
                return false;
            }

            if (player.IsServer)
            {
                return true;
            }

            return permission.UserHasPermission(player.Id, PermUse) ||
                   permission.UserHasPermission(player.Id, PermAdmin);
        }

        private bool RequireAdmin(IPlayer player)
        {
            if (player != null && (player.IsServer || permission.UserHasPermission(player.Id, PermAdmin)))
            {
                return true;
            }

            Reply(player, "You need the '" + PermAdmin + "' permission.");
            return false;
        }

        private void HandleIgnore(IPlayer player, string[] args, bool add)
        {
            if (args == null || args.Length < 2)
            {
                Reply(player, "Ignored: " + (config.Ignore.Count == 0
                    ? "(none)"
                    : string.Join(", ", config.Ignore.ToArray())));
                return;
            }

            string value = args[1].Trim();
            if (value.Length == 0)
            {
                return;
            }

            if (add)
            {
                if (!config.Ignore.Any(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)))
                {
                    config.Ignore.Add(value);
                }

                SaveConfig();
                Reply(player, "Ignoring '" + value + "'.");
            }
            else
            {
                int removed = config.Ignore.RemoveAll(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase));
                SaveConfig();
                Reply(player, removed > 0
                    ? "No longer ignoring '" + value + "'."
                    : "'" + value + "' was not in the ignore list.");
            }
        }

        private void ReplyPluginList(IPlayer player, string filter)
        {
            string[] files = GetPluginFiles();
            if (files.Length == 0)
            {
                Reply(player, "No .cs plugin files were found.");
                return;
            }

            int matched = 0;
            int shown = 0;

            foreach (string relative in files)
            {
                if (!string.IsNullOrEmpty(filter) &&
                    relative.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                matched++;
                if (shown >= 20)
                {
                    continue;
                }

                Plugin plugin = FindLoadedPluginByFile(relative);
                string state = IsIncludeFile(relative)
                    ? "include"
                    : (plugin != null ? "loaded" : "not loaded");
                string ignored = IsIgnored(relative) ? " [ignored]" : string.Empty;
                Reply(player, relative + "  (" + state + ")" + ignored);
                shown++;
            }

            if (matched == 0)
            {
                Reply(player, "No plugin files matched '" + filter + "'.");
            }
            else if (matched > shown)
            {
                Reply(player, "Showing " + shown + " of " + matched + " matches. Use a filter to narrow the list.");
            }
        }

        private void ReplyHelp(IPlayer player)
        {
            Reply(player, "Usage: /hotreload status | list [filter] | reload <plugin> | reloadall confirm");
            Reply(player, "       /hotreload scan | enable | disable | ignore [name] | unignore <name> | reloadconfig | help");
        }

        private void Reply(IPlayer player, string message)
        {
            if (player == null)
            {
                Interface.Oxide.LogInfo("[PluginHotReload] " + message);
                return;
            }

            string prefix = config == null || string.IsNullOrEmpty(config.ChatPrefix) ? string.Empty : config.ChatPrefix + " ";
            player.Reply(prefix + message);
        }

        #endregion

        #region Public API

        /// <summary>
        /// Reloads one Oxide plugin by file name (with or without .cs) or by plugin name.
        /// Returns false when no matching plugin file exists.
        /// </summary>
        [HookMethod("ReloadPlugin")]
        public bool ReloadPlugin(string name)
        {
            string relative = ResolvePluginName(name);
            if (string.IsNullOrEmpty(relative))
            {
                return false;
            }

            return RequestReload(relative, "api");
        }

        /// <summary>
        /// Reloads every loaded watched .cs plugin. Returns the number of reloads requested.
        /// </summary>
        [HookMethod("ReloadAll")]
        public int ReloadAll()
        {
            return ReloadAllInternal("api");
        }

        /// <summary>
        /// Returns the relative names of every .cs plugin file in oxide/plugins.
        /// </summary>
        [HookMethod("GetPluginFiles")]
        public string[] GetPluginFiles()
        {
            List<string> files = EnumeratePluginFiles();
            return files.ToArray();
        }

        /// <summary>
        /// Returns a short status string suitable for another plugin or GUI.
        /// </summary>
        [HookMethod("GetStatus")]
        public string GetStatus()
        {
            return "auto=" + (autoWatchActive ? "on" : "off") +
                   ", core=" + (coreWatcherActive ? "on" : "off") +
                   ", pending=" + PendingCount() +
                   ", reloads=" + reloadCount +
                   ", last=" + lastReload +
                   ", result=" + lastResult;
        }

        /// <summary>
        /// Returns true when this plugin is currently watching oxide/plugins.
        /// </summary>
        [HookMethod("IsWatching")]
        public bool IsWatching()
        {
            return autoWatchActive;
        }

        #endregion

        #region ServerAssist GUI

        private void TryRegisterServerAssist()
        {
            if (ServerAssist == null || !ServerAssist.IsLoaded)
            {
                serverAssistHookRegistered = false;
                return;
            }

            try
            {
                object result = ServerAssist.Call(
                    "RegisterPanel",
                    ServerAssistHookId,
                    "Plugin Hot Reload",
                    "Reload plugins, scan for new files and check the watcher.",
                    PermUse,
                    Name,
                    nameof(HandleServerAssistRequest),
                    ServerAssistIcon,
                    80);

                serverAssistHookRegistered = result is bool && (bool)result;

                if (!serverAssistHookRegistered)
                {
                    result = ServerAssist.Call(
                        "RegisterGuiHook",
                        ServerAssistHookId,
                        "Plugin Hot Reload",
                        "Reload plugins, scan for new files and check the watcher.",
                        PermUse,
                        Name,
                        nameof(HandleServerAssistRequest));

                    serverAssistHookRegistered = result is bool && (bool)result;
                    if (serverAssistHookRegistered)
                    {
                        ServerAssist.Call("SetPanelIcon", ServerAssistHookId, ServerAssistIcon);
                    }
                }

                if (serverAssistHookRegistered)
                {
                    Interface.Oxide.LogInfo("[PluginHotReload] registered the ServerAssist panel.");
                }
            }
            catch (Exception exception)
            {
                serverAssistHookRegistered = false;
                Interface.Oxide.LogWarning(
                    "[PluginHotReload] could not register the ServerAssist panel: " +
                    exception.Message);
            }
        }

        private void UnregisterServerAssist()
        {
            if (ServerAssist == null || !ServerAssist.IsLoaded || !serverAssistHookRegistered)
            {
                return;
            }

            try
            {
                ServerAssist.Call("UnregisterPanel", ServerAssistHookId);
            }
            catch
            {
                // ServerAssist may already be unloading.
            }

            serverAssistHookRegistered = false;
        }

        /// <summary>
        /// Called by ServerAssist while the Plugin Hot Reload panel is active.
        /// Returns the ServerAssist line protocol.
        /// </summary>
        public string HandleServerAssistRequest(string playerId, string payload)
        {
            try
            {
                if (string.IsNullOrEmpty(playerId))
                {
                    return ErrorView("Could not determine your player id.");
                }

                if (!CanUseGui(playerId))
                {
                    return ErrorView("You need the '" + PermUse + "' or '" + PermAdmin + "' permission.");
                }

                GuiSession session = GetGuiSession(playerId);
                if (string.IsNullOrEmpty(payload) || string.Equals(payload, "open", StringComparison.OrdinalIgnoreCase))
                {
                    payload = "a|open";
                }

                if (payload.StartsWith("a|", StringComparison.Ordinal))
                {
                    return HandleGuiAction(playerId, session, payload.Substring(2));
                }

                return ErrorView("Unknown Plugin Hot Reload request.");
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogException("[PluginHotReload] GUI request failed", exception);
                return ErrorView("Internal error: " + exception.Message);
            }
        }

        private bool CanUseGui(string playerId)
        {
            return !string.IsNullOrEmpty(playerId) &&
                   (permission.UserHasPermission(playerId, PermUse) ||
                    permission.UserHasPermission(playerId, PermAdmin));
        }

        private bool IsAdminId(string playerId)
        {
            return !string.IsNullOrEmpty(playerId) &&
                   permission.UserHasPermission(playerId, PermAdmin);
        }

        private GuiSession GetGuiSession(string playerId)
        {
            GuiSession session;
            if (!guiSessions.TryGetValue(playerId, out session))
            {
                session = new GuiSession();
                guiSessions[playerId] = session;
            }

            return session;
        }

        private string HandleGuiAction(string playerId, GuiSession session, string action)
        {
            string[] parts = action.Split('|');
            string command = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
            bool admin = IsAdminId(playerId);

            if (!string.Equals(command, "reloadall", StringComparison.OrdinalIgnoreCase))
            {
                session.ConfirmReloadAll = false;
            }

            switch (command)
            {
                case "reload":
                case "reloadone":
                    if (!admin)
                    {
                        session.Status = "You need the '" + PermAdmin + "' permission to reload plugins.";
                        break;
                    }

                    if (parts.Length < 2 || string.IsNullOrEmpty(parts[1]))
                    {
                        session.Status = "No plugin selected.";
                        break;
                    }

                    string relative = ResolvePluginName(parts[1]);
                    if (string.IsNullOrEmpty(relative))
                    {
                        session.Status = "No plugin file matched '" + parts[1] + "'.";
                        break;
                    }

                    if (IsIncludeFile(relative))
                    {
                        session.Status = "That is an include file. Reloading it would reload every plugin that references it; edit the include file instead.";
                        break;
                    }

                    session.Status = RequestReload(relative, "gui")
                        ? "Reload requested for " + relative + "."
                        : "Reload was not started for " + relative + ". Check the log or the ignore list.";
                    break;

                case "reloadall":
                    if (!admin)
                    {
                        session.Status = "You need the '" + PermAdmin + "' permission to reload plugins.";
                        break;
                    }

                    if (!session.ConfirmReloadAll)
                    {
                        session.ConfirmReloadAll = true;
                        session.Status = "Click Reload All again to confirm. This reloads every loaded plugin.";
                        break;
                    }

                    session.ConfirmReloadAll = false;
                    int reloaded = ReloadAllInternal("gui");
                    session.Status = "Reload requested for " + reloaded + " plugin(s).";
                    break;

                case "scan":
                    if (!admin)
                    {
                        session.Status = "You need the '" + PermAdmin + "' permission to scan plugins.";
                        break;
                    }

                    int scanned = ScanAndLoad();
                    session.Status = "Scan complete. Load requested for " + scanned + " new plugin(s).";
                    break;

                case "enable":
                    if (!admin)
                    {
                        session.Status = "You need the '" + PermAdmin + "' permission to change the watcher.";
                        break;
                    }

                    config.Enabled = true;
                    SaveConfig();
                    TryStartWatcher();
                    session.Status = "Automatic reload enabled.";
                    break;

                case "disable":
                    if (!admin)
                    {
                        session.Status = "You need the '" + PermAdmin + "' permission to change the watcher.";
                        break;
                    }

                    config.Enabled = false;
                    SaveConfig();
                    StopWatcher();
                    session.Status = "Automatic reload disabled. Manual commands still work.";
                    break;

                case "page":
                    if (parts.Length > 1 && string.Equals(parts[1], "next", StringComparison.OrdinalIgnoreCase))
                    {
                        session.Page++;
                    }
                    else if (session.Page > 0)
                    {
                        session.Page--;
                    }
                    break;

                case "refresh":
                    session.Status = string.Empty;
                    break;

                default:
                    session.Page = 0;
                    session.Status = string.Empty;
                    break;
            }

            return RenderGui(playerId, session);
        }

        private string RenderGui(string playerId, GuiSession session)
        {
            bool admin = IsAdminId(playerId);
            string[] files = GetPluginFiles()
                .Where(file => !IsIncludeFile(file))
                .ToArray();
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            int pages = Math.Max(1, (files.Length + GuiRows - 1) / GuiRows);
            if (session.Page < 0)
            {
                session.Page = 0;
            }
            else if (session.Page >= pages)
            {
                session.Page = pages - 1;
            }

            int start = session.Page * GuiRows;
            StringBuilder builder = new StringBuilder(4096);
            builder.Append("v1\n");
            builder.Append("title=").Append(Encode("Plugin Hot Reload")).Append('\n');
            builder.Append("subtitle=").Append(Encode(
                "auto-watch " + (autoWatchActive ? "on" : "off") +
                "  |  core " + (coreWatcherActive ? "on" : "off") +
                "  |  " + files.Length + " tracked .cs file" + (files.Length == 1 ? string.Empty : "s") +
                "  |  page " + (session.Page + 1) + "/" + pages)).Append('\n');
            builder.Append("status=").Append(Encode(session.Status)).Append('\n');
            builder.Append("input=||0|\n");

            int rowIndex = 0;
            for (int i = start; i < files.Length && rowIndex < GuiRows; i++, rowIndex++)
            {
                string relative = files[i];
                string label = relative;
                if (label.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    label = label.Substring(0, label.Length - 3);
                }

                builder.Append("row=").Append(rowIndex).Append('|')
                    .Append(Encode(admin ? "reloadone|" + relative : string.Empty)).Append('|')
                    .Append(Encode(label)).Append('|')
                    .Append(Encode(admin ? "Click to reload this plugin." : "Admin permission required to reload.")).Append('|')
                    .Append(admin ? "1" : "0").Append('|')
                    .Append("0|")
                    .Append(Encode(IconForPlugin(relative))).Append('|')
                    .Append(Encode("PLUGIN")).Append('|')
                    .Append('\n');
            }

            if (files.Length == 0)
            {
                builder.Append("row=0||No plugin files found|The oxide/plugins folder is empty or unreadable|0|0|")
                    .Append(Encode(StatusIcon)).Append("|STATUS|\n");
            }

            int buttonIndex = 0;
            AppendGuiButton(builder, ref buttonIndex, "refresh", "Refresh", true);
            AppendGuiButton(builder, ref buttonIndex, "scan", "Scan", admin);
            if (autoWatchActive)
            {
                AppendGuiButton(builder, ref buttonIndex, "disable", "Disable Watch", admin);
            }
            else
            {
                AppendGuiButton(builder, ref buttonIndex, "enable", "Enable Watch", admin);
            }

            if (pages > 1)
            {
                AppendGuiButton(builder, ref buttonIndex, "page|prev", "< Prev", session.Page > 0);
                AppendGuiButton(builder, ref buttonIndex, "page|next", "Next >", session.Page + 1 < pages);
            }

            // Keep the bulk action last so it is not mistaken for a
            // single-plugin reload.
            AppendGuiButton(
                builder,
                ref buttonIndex,
                "reloadall",
                session.ConfirmReloadAll ? "Confirm Reload ALL" : "Reload ALL",
                admin);

            return builder.ToString();
        }

        private static void AppendGuiButton(StringBuilder builder, ref int index, string action, string label, bool enabled)
        {
            builder.Append("btn=").Append(index++).Append('|')
                .Append(Encode(action)).Append('|')
                .Append(Encode(label)).Append('|')
                .Append(enabled ? "1" : "0").Append('|')
                .Append(Encode(string.Empty)).Append('\n');
        }

        private static string ErrorView(string message)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("v1\n");
            builder.Append("title=").Append(Encode("Plugin Hot Reload")).Append('\n');
            builder.Append("subtitle=").Append(Encode("Unavailable")).Append('\n');
            builder.Append("status=").Append(Encode(message)).Append('\n');
            builder.Append("input=||0|\n");
            builder.Append("row=0||No data|").Append(Encode(message)).Append("|0|0|")
                .Append(Encode(ServerAssistIcon)).Append("|PLUGIN|\n");
            builder.Append("btn=0|refresh|").Append(Encode("Retry")).Append("|1|\n");
            return builder.ToString();
        }

        #endregion

        #region Helpers

        private int PendingCount()
        {
            lock (pendingLock)
            {
                return pending.Count;
            }
        }

        private bool IsRelativeSelf(string relative)
        {
            return string.Equals(relative, selfRelative, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsIncludeFile(string relative)
        {
            if (string.IsNullOrEmpty(relative))
            {
                return false;
            }

            return relative.StartsWith("include\\", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(relative, "include", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsManualReason(string reason)
        {
            return string.Equals(reason, "gui", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(reason, "command", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(reason, "api", StringComparison.OrdinalIgnoreCase);
        }

        private static string IconForPlugin(string relative)
        {
            if (string.IsNullOrEmpty(relative))
            {
                return ServerAssistIcon;
            }

            string name = relative;
            int slash = name.LastIndexOf('\\');
            if (slash >= 0)
            {
                name = name.Substring(slash + 1);
            }

            if (name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - 3);
            }

            string icon;
            if (PluginIcons.TryGetValue(name, out icon))
            {
                return icon;
            }

            foreach (KeyValuePair<string, string> pair in PluginIcons)
            {
                if (name.IndexOf(pair.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return pair.Value;
                }
            }

            return ServerAssistIcon;
        }

        private bool ShouldHandle(string relative)
        {
            if (string.IsNullOrEmpty(relative))
            {
                return false;
            }

            if (IsIgnored(relative))
            {
                return false;
            }

            if (config != null && config.IncludeOnly.Count > 0 && !MatchesAny(config.IncludeOnly, relative))
            {
                return false;
            }

            return true;
        }

        private bool IsIgnored(string relative)
        {
            return config != null && config.Ignore.Count > 0 && MatchesAny(config.Ignore, relative);
        }

        private static bool MatchesAny(List<string> patterns, string relative)
        {
            string name = relative.Replace('/', '\\');
            string fileName = name;
            int slash = name.LastIndexOf('\\');
            if (slash >= 0)
            {
                fileName = name.Substring(slash + 1);
            }

            foreach (string pattern in patterns)
            {
                if (string.IsNullOrEmpty(pattern))
                {
                    continue;
                }

                if (WildcardMatch(name, pattern) || WildcardMatch(fileName, pattern))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool WildcardMatch(string input, string pattern)
        {
            string expression = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(input, expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private string ResolvePluginName(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return null;
            }

            string value = input.Trim();
            if (value.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(0, value.Length - 3);
            }

            value = value.Replace('/', '\\').Trim('\\');
            if (value.Length == 0)
            {
                return null;
            }

            string direct = FindPluginFile(value);
            if (!string.IsNullOrEmpty(direct))
            {
                return direct;
            }

            try
            {
                foreach (Plugin plugin in Interface.Oxide.RootPluginManager.GetPlugins())
                {
                    if (plugin == null || plugin.IsCorePlugin)
                    {
                        continue;
                    }

                    if (!string.Equals(plugin.Name, value, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(plugin.Title, value, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string relative = GetRelativeFromFilename(plugin);
                    if (!string.IsNullOrEmpty(relative))
                    {
                        return relative;
                    }
                }
            }
            catch
            {
                // Fall through to the file name search.
            }

            return FindPluginFileByFileName(value);
        }

        private string FindPluginFile(string relative)
        {
            foreach (string file in EnumeratePluginFiles())
            {
                if (string.Equals(file, relative, StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }

            return null;
        }

        private string FindPluginFileByFileName(string input)
        {
            string match = null;
            foreach (string file in EnumeratePluginFiles())
            {
                string fileName = file;
                int slash = fileName.LastIndexOf('\\');
                if (slash >= 0)
                {
                    fileName = fileName.Substring(slash + 1);
                }

                if (!string.Equals(fileName, input, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Prefer a top-level plugin file when both a top-level file
                // and a nested file have the same name.
                if (file.IndexOf('\\') < 0)
                {
                    return file;
                }

                if (match == null)
                {
                    match = file;
                }
            }

            return match;
        }

        private List<string> EnumeratePluginFiles()
        {
            List<string> files = new List<string>();
            string directory = PluginDirectory;
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return files;
            }

            try
            {
                bool recursive = config == null || config.WatchSubdirectories;
                SearchOption option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                foreach (string file in Directory.GetFiles(directory, "*.cs", option))
                {
                    string relative = GetRelativePluginName(file);
                    if (!string.IsNullOrEmpty(relative))
                    {
                        files.Add(relative);
                    }
                }
            }
            catch (Exception exception)
            {
                Interface.Oxide.LogWarning("[PluginHotReload] could not enumerate plugin files: " + exception.Message);
            }

            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }

        private string GetRelativePluginName(string fullPath)
        {
            string directory = PluginDirectory;
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fullPath))
            {
                return null;
            }

            try
            {
                string root = Path.GetFullPath(directory);
                if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                {
                    root += Path.DirectorySeparatorChar;
                }

                string full = Path.GetFullPath(fullPath);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                string relative = full.Substring(root.Length);
                if (!relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                relative = relative.Substring(0, relative.Length - 3);
                relative = relative.Replace('/', '\\').Trim('\\');
                if (relative.Length == 0)
                {
                    return null;
                }

                string[] parts = relative.Split('\\');
                foreach (string part in parts)
                {
                    if (string.IsNullOrEmpty(part))
                    {
                        return null;
                    }

                    if (part.StartsWith(".", StringComparison.Ordinal))
                    {
                        return null;
                    }

                    if (string.Equals(part, "obj", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(part, "bin", StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }
                }

                string fileName = parts[parts.Length - 1];
                if (fileName.EndsWith("~", StringComparison.Ordinal) ||
                    fileName.StartsWith("~", StringComparison.Ordinal))
                {
                    return null;
                }

                return relative;
            }
            catch
            {
                return null;
            }
        }

        private string GetRelativeFromFilename(Plugin plugin)
        {
            if (plugin == null || string.IsNullOrEmpty(plugin.Filename))
            {
                return null;
            }

            string relative = GetRelativePluginName(plugin.Filename);
            if (!string.IsNullOrEmpty(relative))
            {
                return relative;
            }

            string fileName = Path.GetFileNameWithoutExtension(plugin.Filename);
            return string.IsNullOrEmpty(fileName) ? null : fileName;
        }

        private Plugin FindLoadedPluginByFile(string relative)
        {
            if (string.IsNullOrEmpty(relative))
            {
                return null;
            }

            try
            {
                foreach (Plugin plugin in Interface.Oxide.RootPluginManager.GetPlugins())
                {
                    if (plugin == null || plugin.IsCorePlugin)
                    {
                        continue;
                    }

                    if (string.Equals(GetRelativeFromFilename(plugin), relative, StringComparison.OrdinalIgnoreCase))
                    {
                        return plugin;
                    }
                }
            }
            catch
            {
                // Return null below.
            }

            return null;
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

        #endregion

        #region Data classes

        private sealed class PendingChange
        {
            public string Relative;
            public string FullPath;
            public WatcherChangeTypes Type;
            public DateTime LastUtc;
        }

        private sealed class GuiSession
        {
            public int Page;
            public string Status = string.Empty;
            public bool ConfirmReloadAll;
        }

        private sealed class Configuration
        {
            public string ChatPrefix = "[HotReload]";
            public bool Enabled = true;
            public bool TakeOverCoreWatcher = true;
            public bool WatchSubdirectories = true;
            public float DebounceSeconds = 1.5f;
            public bool ReloadOnCreate = true;
            public bool UnloadOnDelete = false;
            public bool ReloadSelf = true;
            public bool AllowManualIncludeReload = false;
            public bool LogReloads = true;
            public bool GrantAdminPermission = true;
            public int MaxReloadsPerMinute = 60;
            public int ReloadTimeoutSeconds = 90;
            public List<string> Ignore = new List<string>();
            public List<string> IncludeOnly = new List<string>();
        }

        #endregion
    }
}
