using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml;

namespace UpdaterHarness
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string updater = args.Length > 0
                ? Path.GetFullPath(args[0])
                : Path.GetFullPath(Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "..", "..", "..", "..", "..", "dist", "000_QLNPackBootstrap", "QLNPackUpdater.exe"));

            if (!File.Exists(updater))
            {
                Console.Error.WriteLine("Updater not found: " + updater);
                return 2;
            }

            string root = Path.Combine(Path.GetTempPath(), "QLNPackUpdaterTest", Guid.NewGuid().ToString("N"));
            string mods = Path.Combine(root, "Mods");
            string data = Path.Combine(mods, "000_QLNPackBootstrap", "Data");
            string stageRoot = Path.Combine(data, "staging", "1.0.0");
            string stagedMod = Path.Combine(stageRoot, "QuantumLogisticsNetwork");
            string target = Path.Combine(mods, "QuantumLogisticsNetwork");

            try
            {
                Directory.CreateDirectory(stagedMod);
                Directory.CreateDirectory(target);

                File.WriteAllText(Path.Combine(stagedMod, "ModInfo.xml"), "<xml><Name value=\"QuantumLogisticsNetwork\" /></xml>");
                File.WriteAllText(Path.Combine(stagedMod, "new-marker.txt"), "new");
                File.WriteAllText(Path.Combine(target, "old-marker.txt"), "old");

                string pending = Path.Combine(data, "pending.xml");
                WritePending(pending, stageRoot);

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = updater,
                    Arguments = "--pid 999999 --pending \"" + pending +
                                "\" --mods \"" + mods +
                                "\" --data \"" + data +
                                "\" --keep 3 --quiet",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(startInfo))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    Console.WriteLine(output);
                    if (!string.IsNullOrEmpty(error))
                    {
                        Console.WriteLine("STDERR: " + error);
                    }

                    if (process.ExitCode != 0)
                    {
                        Console.Error.WriteLine("Updater exited with " + process.ExitCode);
                        return 1;
                    }
                }

                bool newPresent = File.Exists(Path.Combine(target, "new-marker.txt"));
                bool oldGone = !File.Exists(Path.Combine(target, "old-marker.txt"));
                bool pendingGone = !File.Exists(pending);
                bool statePresent = File.Exists(Path.Combine(data, "state.xml"));

                bool backupHasOld = false;
                string backupRoot = Path.Combine(data, "backup");
                if (Directory.Exists(backupRoot))
                {
                    foreach (string backup in Directory.GetDirectories(backupRoot))
                    {
                        if (File.Exists(Path.Combine(backup, "QuantumLogisticsNetwork", "old-marker.txt")))
                        {
                            backupHasOld = true;
                        }
                    }
                }

                Console.WriteLine("new file present: " + newPresent);
                Console.WriteLine("old file removed: " + oldGone);
                Console.WriteLine("pending removed: " + pendingGone);
                Console.WriteLine("state written:   " + statePresent);
                Console.WriteLine("backup found:    " + backupHasOld);

                return newPresent && oldGone && pendingGone && statePresent && backupHasOld ? 0 : 1;
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch
                {
                    Console.WriteLine("Test folder left behind: " + root);
                }
            }
        }

        private static void WritePending(string path, string stageRoot)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                Encoding = new UTF8Encoding(false)
            };

            using (XmlWriter writer = XmlWriter.Create(path, settings))
            {
                writer.WriteStartElement("PendingUpdate");
                writer.WriteElementString("PackVersion", "1.0.0");
                writer.WriteElementString("CreatedAt", DateTime.UtcNow.ToString("o"));
                writer.WriteElementString("StageRoot", stageRoot);

                writer.WriteStartElement("Apply");
                writer.WriteStartElement("Mod");
                writer.WriteElementString("Folder", "QuantumLogisticsNetwork");
                writer.WriteElementString("Version", "2.0.0");
                writer.WriteElementString("Sha256", new string('a', 64));
                writer.WriteElementString("DisplayName", "Quantum Logistics Network");
                writer.WriteEndElement();
                writer.WriteEndElement();

                writer.WriteStartElement("Remove");
                writer.WriteEndElement();

                writer.WriteStartElement("State");
                writer.WriteStartElement("Mod");
                writer.WriteElementString("Folder", "QuantumLogisticsNetwork");
                writer.WriteElementString("Version", "2.0.0");
                writer.WriteElementString("Sha256", new string('a', 64));
                writer.WriteEndElement();
                writer.WriteEndElement();

                writer.WriteEndElement();
            }
        }
    }
}
