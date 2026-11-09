using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;

namespace QLNPackUpdater
{
    internal static class Program
    {
        private static readonly Regex SafeFolderRegex = new Regex("^[0-9A-Za-z_\\-]+$", RegexOptions.Compiled);
        private static readonly object LogSync = new object();
        private static string _logPath;
        private static string _failedPath;

        private static int Main(string[] args)
        {
            Console.Title = "QLN Mod Pack Updater";
            Console.WriteLine("QLN Mod Pack Updater");
            Console.WriteLine();

            Dictionary<string, string> options = ParseArgs(args);
            string pendingPath = Get(options, "pending");
            string modsRoot = Get(options, "mods");
            string dataDir = Get(options, "data");
            string gameExe = Get(options, "game");
            int keep = ParseInt(Get(options, "keep"), 3);
            bool relaunch = options.ContainsKey("relaunch");
            bool quiet = options.ContainsKey("quiet");
            int pid = ParseInt(Get(options, "pid"), 0);

            if (string.IsNullOrEmpty(pendingPath) || string.IsNullOrEmpty(modsRoot) || string.IsNullOrEmpty(dataDir))
            {
                return Fail("Missing required arguments. The bootstrap mod must launch this updater.", null, false);
            }

            _logPath = Path.Combine(dataDir, "logs", "updater.log");
            _failedPath = Path.Combine(dataDir, "updater-failed.txt");

            try
            {
                if (File.Exists(_failedPath))
                {
                    File.Delete(_failedPath);
                }
            }
            catch
            {
                // The update can still proceed.
            }

            using (Mutex mutex = new Mutex(true, "QLNPackUpdater.SingleInstance", out bool createdNew))
            {
                if (!createdNew)
                {
                    Log("Another updater instance is already running.");
                    return 0;
                }

                try
                {
                    WaitForGame(pid);

                    if (!File.Exists(pendingPath))
                    {
                        Log("No pending update file found: " + pendingPath);
                        return 0;
                    }

                    Log("Applying pending update.");
                    Apply(pendingPath, modsRoot, dataDir, keep);

                    if (relaunch && !string.IsNullOrEmpty(gameExe) && File.Exists(gameExe))
                    {
                        Log("Relaunching: " + gameExe);
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = gameExe,
                            WorkingDirectory = Path.GetDirectoryName(gameExe),
                            UseShellExecute = true
                        });
                    }
                    else if (!quiet)
                    {
                        Log("Update applied. Relaunch the game manually.");
                        ShowInfo("The mod pack was updated. Start 7 Days to Die again.");
                    }

                    Log("Done.");
                    return 0;
                }
                catch (Exception exception)
                {
                    Log("ERROR: " + exception);
                    return Fail("The mod pack update failed.\n\n" + exception.Message +
                                "\n\nSee " + _logPath, exception, quiet);
                }
            }
        }

        private static void WaitForGame(int pid)
        {
            if (pid <= 0)
            {
                return;
            }

            try
            {
                Process process = Process.GetProcessById(pid);

                string processName = "";
                try
                {
                    processName = process.ProcessName;
                }
                catch
                {
                    // Continue; access to the name is not essential.
                }

                if (!string.IsNullOrEmpty(processName) &&
                    processName.IndexOf("7DaysToDie", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    Log("PID " + pid + " is no longer the game (" + processName + "); continuing.");
                    return;
                }

                Log("Waiting for process " + pid + " to exit.");
                Console.WriteLine("Waiting for 7 Days to Die to close...");
                process.WaitForExit();
                Log("Game process exited.");
            }
            catch (ArgumentException)
            {
                Log("Game process already exited.");
            }
            catch (InvalidOperationException)
            {
                Log("Game process already exited.");
            }
            catch (Exception exception)
            {
                Log("Could not wait for the game process: " + exception.Message);
                Thread.Sleep(2000);
            }
        }

        private static void Apply(string pendingPath, string modsRoot, string dataDir, int keep)
        {
            XmlDocument document = new XmlDocument();
            document.Load(pendingPath);

            XmlElement root = document.DocumentElement;
            if (root == null)
            {
                throw new InvalidDataException("The pending update file has no root element.");
            }

            string stageRoot = ReadElement(root, "StageRoot");
            if (string.IsNullOrEmpty(stageRoot) || !Directory.Exists(stageRoot))
            {
                throw new InvalidDataException("The staged update folder is missing: " + stageRoot);
            }

            string stagingRoot = Path.GetFullPath(Path.Combine(dataDir, "staging"));
            string stageFull = Path.GetFullPath(stageRoot);
            if (!IsUnder(stageFull, stagingRoot))
            {
                throw new InvalidDataException("The pending update points outside the staging folder.");
            }

            string backupRoot = CreateBackupRoot(dataDir);
            Log("Backup folder: " + backupRoot);

            XmlNode applyNode = root.SelectSingleNode("Apply");
            if (applyNode != null)
            {
                foreach (XmlNode node in applyNode.SelectNodes("Mod"))
                {
                    XmlElement element = node as XmlElement;
                    if (element == null)
                    {
                        continue;
                    }

                    string folder = ReadElement(element, "Folder");
                    if (!IsSafeFolder(folder) || IsReservedFolder(folder))
                    {
                        throw new InvalidDataException("Unsafe folder name in pending update: " + folder);
                    }

                    string source = Path.Combine(stageRoot, folder);
                    string target = Path.Combine(modsRoot, folder);

                    if (!Directory.Exists(source))
                    {
                        throw new InvalidDataException("Staged folder is missing: " + source);
                    }

                    Log("Replacing mod folder: " + folder);

                    string backup = null;
                    if (Directory.Exists(target))
                    {
                        backup = UniquePath(Path.Combine(backupRoot, folder));
                        MoveDirectoryWithRetry(target, backup);
                        Log("  backed up old folder to " + backup);
                    }

                    try
                    {
                        MoveDirectoryWithRetry(source, target);
                    }
                    catch
                    {
                        // Try to put the old version back.
                        if (!Directory.Exists(target) && backup != null && Directory.Exists(backup))
                        {
                            try
                            {
                                MoveDirectoryWithRetry(backup, target);
                            }
                            catch (Exception restoreException)
                            {
                                Log("  could not restore backup: " + restoreException.Message);
                            }
                        }

                        throw;
                    }
                }
            }

            XmlNode removeNode = root.SelectSingleNode("Remove");
            if (removeNode != null)
            {
                foreach (XmlNode node in removeNode.SelectNodes("Folder"))
                {
                    string folder = (node.InnerText ?? "").Trim();
                    if (folder.Length == 0)
                    {
                        continue;
                    }

                    if (!IsSafeFolder(folder) || IsReservedFolder(folder))
                    {
                        throw new InvalidDataException("Unsafe remove folder in pending update: " + folder);
                    }

                    string target = Path.Combine(modsRoot, folder);
                    if (!Directory.Exists(target))
                    {
                        continue;
                    }

                    string backup = UniquePath(Path.Combine(backupRoot, folder));
                    MoveDirectoryWithRetry(target, backup);
                    Log("Removed mod folder (backed up): " + folder);
                }
            }

            WriteState(root, dataDir);

            try
            {
                File.Delete(pendingPath);
            }
            catch (Exception exception)
            {
                Log("Could not delete pending update file: " + exception.Message);
            }

            try
            {
                if (Directory.Exists(stageRoot))
                {
                    Directory.Delete(stageRoot, true);
                }
            }
            catch (Exception exception)
            {
                Log("Could not delete staging folder: " + exception.Message);
            }

            PruneBackups(Path.Combine(dataDir, "backup"), keep);
        }

        private static void WriteState(XmlElement pendingRoot, string dataDir)
        {
            string packVersion = ReadElement(pendingRoot, "PackVersion");
            XmlNode stateNode = pendingRoot.SelectSingleNode("State");

            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                Encoding = new UTF8Encoding(false)
            };

            string statePath = Path.Combine(dataDir, "state.xml");
            Directory.CreateDirectory(dataDir);

            using (XmlWriter writer = XmlWriter.Create(statePath, settings))
            {
                writer.WriteStartElement("PackState");
                writer.WriteElementString("PackVersion", packVersion);
                writer.WriteElementString("InstalledAt", DateTime.UtcNow.ToString("o"));
                writer.WriteStartElement("Mods");

                if (stateNode != null)
                {
                    foreach (XmlNode node in stateNode.SelectNodes("Mod"))
                    {
                        XmlElement element = node as XmlElement;
                        if (element == null)
                        {
                            continue;
                        }

                        writer.WriteStartElement("Mod");
                        writer.WriteElementString("Folder", ReadElement(element, "Folder"));
                        writer.WriteElementString("Version", ReadElement(element, "Version"));
                        writer.WriteElementString("Sha256", ReadElement(element, "Sha256"));
                        writer.WriteEndElement();
                    }
                }

                writer.WriteEndElement();
                writer.WriteEndElement();
            }

            Log("Wrote state: " + statePath);
        }

        private static void PruneBackups(string backupRoot, int keep)
        {
            if (!Directory.Exists(backupRoot))
            {
                return;
            }

            try
            {
                List<string> dirs = Directory.GetDirectories(backupRoot)
                    .OrderByDescending(dir => Path.GetFileName(dir), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                for (int index = Math.Max(0, keep); index < dirs.Count; index++)
                {
                    Directory.Delete(dirs[index], true);
                    Log("Pruned old backup: " + dirs[index]);
                }
            }
            catch (Exception exception)
            {
                Log("Could not prune backups: " + exception.Message);
            }
        }

        private static string CreateBackupRoot(string dataDir)
        {
            string backupRoot = Path.Combine(dataDir, "backup");
            Directory.CreateDirectory(backupRoot);

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string root = Path.Combine(backupRoot, stamp);
            int suffix = 1;

            while (Directory.Exists(root))
            {
                root = Path.Combine(backupRoot, stamp + "-" + suffix);
                suffix++;
            }

            Directory.CreateDirectory(root);
            return root;
        }

        private static string UniquePath(string path)
        {
            if (!Directory.Exists(path) && !File.Exists(path))
            {
                return path;
            }

            int suffix = 1;
            string candidate = path + "-" + suffix;
            while (Directory.Exists(candidate) || File.Exists(candidate))
            {
                suffix++;
                candidate = path + "-" + suffix;
            }

            return candidate;
        }

        private static void MoveDirectoryWithRetry(string source, string destination)
        {
            Exception last = null;

            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    Directory.Move(source, destination);
                    return;
                }
                catch (Exception exception)
                {
                    last = exception;
                    Thread.Sleep(500);
                }
            }

            // Same-volume moves are the normal case. Fall back to copy+delete for
            // unusual setups (for example, a Mods folder on another volume).
            try
            {
                CopyDirectory(source, destination);
                Directory.Delete(source, true);
                return;
            }
            catch
            {
                throw new IOException(
                    "Could not move '" + source + "' to '" + destination + "'.",
                    last);
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);

            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
            }
        }

        private static bool IsUnder(string path, string root)
        {
            string fullPath = Path.GetFullPath(path);
            string fullRoot = Path.GetFullPath(root);

            if (!fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                fullRoot += Path.DirectorySeparatorChar;
            }

            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSafeFolder(string folder)
        {
            return !string.IsNullOrEmpty(folder) &&
                   SafeFolderRegex.IsMatch(folder) &&
                   folder != "." &&
                   folder != "..";
        }

        private static bool IsReservedFolder(string folder)
        {
            return string.Equals(folder, "000_QLNPackBootstrap", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(folder, "Data", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadElement(XmlElement parent, string name)
        {
            XmlNode node = parent.SelectSingleNode(name);
            return node == null ? "" : (node.InnerText ?? "").Trim();
        }

        private static Dictionary<string, string> ParseArgs(string[] args)
        {
            Dictionary<string, string> options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < args.Length; index++)
            {
                string arg = args[index];
                if (!arg.StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                string name = arg.Substring(2);
                string value = "";

                if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = args[index + 1];
                    index++;
                }

                options[name] = value;
            }

            return options;
        }

        private static string Get(Dictionary<string, string> options, string name)
        {
            string value;
            return options.TryGetValue(name, out value) ? value : "";
        }

        private static int ParseInt(string value, int fallback)
        {
            int result;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
                ? result
                : fallback;
        }

        private static void Log(string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message;
            Console.WriteLine(message);

            try
            {
                lock (LogSync)
                {
                    if (!string.IsNullOrEmpty(_logPath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(_logPath));
                        File.AppendAllText(_logPath, line + Environment.NewLine);
                    }
                }
            }
            catch
            {
                // Logging must not break the update.
            }
        }

        private static int Fail(string message, Exception exception, bool quiet)
        {
            if (exception != null)
            {
                Log("ERROR: " + exception);
            }
            else
            {
                Log("ERROR: " + message);
            }

            try
            {
                if (!string.IsNullOrEmpty(_failedPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_failedPath));
                    File.WriteAllText(
                        _failedPath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + message + Environment.NewLine);
                }
            }
            catch
            {
                // Best effort.
            }

            if (!quiet)
            {
                ShowError(message);
            }

            return 1;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        private static void ShowInfo(string message)
        {
            try
            {
                MessageBoxW(IntPtr.Zero, message, "QLN Mod Pack Updater", 0x40);
            }
            catch
            {
                // No UI available.
            }
        }

        private static void ShowError(string message)
        {
            try
            {
                MessageBoxW(IntPtr.Zero, message, "QLN Mod Pack Updater", 0x10);
            }
            catch
            {
                // No UI available.
            }
        }
    }
}
