using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;

namespace QLNPackBootstrap
{
    internal enum ModChangeKind
    {
        New,
        Update,
        UnmanagedExisting,
        Remove
    }

    internal sealed class ModChange
    {
        public PackModEntry Mod;
        public string Folder;
        public ModChangeKind Kind;
        public bool TargetExists;
    }

    internal static class PackInstaller
    {
        private static readonly Regex SafeFolderRegex = new Regex("^[0-9A-Za-z_\\-]+$", RegexOptions.Compiled);
        private static readonly Regex SafePackVersionRegex = new Regex("^[0-9A-Za-z._\\-]+$", RegexOptions.Compiled);
        private static readonly Regex Sha256Regex = new Regex("^[0-9A-Fa-f]{64}$", RegexOptions.Compiled);

        // A lone top-level folder with one of these names is mod content, not a
        // wrapper. This keeps zips like "Config/items.xml" as the mod root.
        private static readonly HashSet<string> KnownContentFolders =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Config",
                "XUi",
                "Resources",
                "Mods",
                "Harmony",
                "Assemblies",
                "Localization",
                "Patches",
                "ItemIcons",
                "UIAtlases",
                "Unity",
                "Data"
            };

        // ------------------------------------------------------------------
        // Manifest validation and change detection
        // ------------------------------------------------------------------

        public static bool ValidateManifest(PackManifest manifest, PackConfig config, out string error)
        {
            error = null;

            if (manifest == null)
            {
                error = "The manifest is empty or is not valid JSON.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(manifest.PackVersion) ||
                !SafePackVersionRegex.IsMatch(manifest.PackVersion) ||
                manifest.PackVersion == "." ||
                manifest.PackVersion == ".." ||
                manifest.PackVersion.IndexOf("..", StringComparison.Ordinal) >= 0)
            {
                error = "The manifest has a missing or unsafe packVersion.";
                return false;
            }

            if (manifest.Mods == null)
            {
                manifest.Mods = new List<PackModEntry>();
            }

            if (manifest.Remove == null)
            {
                manifest.Remove = new List<string>();
            }

            List<PackModEntry> clientMods = manifest.Mods.Where(mod => mod != null && mod.IsClientSide).ToList();
            if (clientMods.Count == 0)
            {
                error = "The manifest does not contain any client-side mods.";
                return false;
            }

            HashSet<string> folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long totalSize = 0;

            foreach (PackModEntry mod in clientMods)
            {
                if (string.IsNullOrWhiteSpace(mod.Name) || !SafeFolderRegex.IsMatch(mod.Name))
                {
                    error = "A manifest mod entry has a missing or invalid name.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(mod.Version))
                {
                    error = "Mod '" + mod.Name + "' is missing its version.";
                    return false;
                }

                string folder = mod.Folder;
                if (!IsValidFolderName(folder))
                {
                    error = "Mod '" + mod.Name + "' has an unsafe install folder '" + folder + "'.";
                    return false;
                }

                if (folder.Equals(PackPaths.BootstrapFolderName, StringComparison.OrdinalIgnoreCase) ||
                    folder.Equals(PackPaths.DataFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    error = "Mod '" + mod.Name + "' uses a reserved folder name.";
                    return false;
                }

                if (!folders.Add(folder))
                {
                    error = "The manifest lists the folder '" + folder + "' more than once.";
                    return false;
                }

                if (!Sha256Regex.IsMatch(mod.Sha256 ?? ""))
                {
                    error = "Mod '" + mod.Name + "' has a missing or invalid sha256.";
                    return false;
                }

                if (mod.Size < 0)
                {
                    error = "Mod '" + mod.Name + "' has an invalid size.";
                    return false;
                }

                if (config.MaxModBytes > 0 && mod.Size > config.MaxModBytes)
                {
                    error = "Mod '" + mod.Name + "' is larger than MaxModBytes.";
                    return false;
                }

                Uri uri;
                if (!Uri.TryCreate(mod.Url, UriKind.Absolute, out uri))
                {
                    error = "Mod '" + mod.Name + "' has an invalid download URL.";
                    return false;
                }

                if (!IsHostAllowed(uri, config))
                {
                    error = "Mod '" + mod.Name + "' points at a host that is not allowed: " + uri.Host;
                    return false;
                }

                totalSize += Math.Max(0, mod.Size);
            }

            if (config.MaxTotalBytes > 0 && totalSize > config.MaxTotalBytes)
            {
                error = "The pack is larger than MaxTotalBytes.";
                return false;
            }

            HashSet<string> removeFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in manifest.Remove)
            {
                if (!IsValidFolderName(folder))
                {
                    error = "The manifest has an unsafe remove folder '" + folder + "'.";
                    return false;
                }

                if (folder.Equals(PackPaths.BootstrapFolderName, StringComparison.OrdinalIgnoreCase) ||
                    folder.Equals(PackPaths.DataFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    error = "The manifest cannot remove a reserved folder.";
                    return false;
                }

                if (!removeFolders.Add(folder))
                {
                    error = "The manifest lists the remove folder '" + folder + "' more than once.";
                    return false;
                }
            }

            return true;
        }

        public static List<ModChange> ComputeChanges(
            PackManifest manifest,
            PackState state,
            PackConfig config,
            out string error)
        {
            error = null;

            if (state == null)
            {
                state = new PackState();
            }

            Dictionary<string, InstalledMod> installed = new Dictionary<string, InstalledMod>(StringComparer.OrdinalIgnoreCase);
            foreach (InstalledMod mod in state.Mods)
            {
                if (mod != null && !string.IsNullOrEmpty(mod.Folder))
                {
                    installed[mod.Folder] = mod;
                }
            }

            List<ModChange> changes = new List<ModChange>();
            HashSet<string> manifestFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (PackModEntry mod in manifest.Mods)
            {
                if (mod == null || !mod.IsClientSide)
                {
                    continue;
                }

                manifestFolders.Add(mod.Folder);

                string target = GetTargetPath(mod.Folder);
                bool targetExists = Directory.Exists(target);

                // ModInfo.xml is optional. Some mods are only Config/XUi/asset
                // folders and still load in game. A non-empty install folder with
                // matching state is valid even when it has no ModInfo.xml.
                bool targetValid = targetExists && IsNonEmptyDirectory(target);

                InstalledMod current;
                installed.TryGetValue(mod.Folder, out current);

                bool sameVersion = current != null &&
                                   string.Equals(current.Version ?? "", mod.Version ?? "", StringComparison.OrdinalIgnoreCase);
                bool sameHash = current != null &&
                                string.Equals(current.Sha256 ?? "", mod.Sha256 ?? "", StringComparison.OrdinalIgnoreCase);

                if (targetValid && sameVersion && sameHash)
                {
                    continue;
                }

                ModChangeKind kind;
                if (!targetExists)
                {
                    kind = ModChangeKind.New;
                }
                else if (!targetValid || current == null)
                {
                    kind = ModChangeKind.UnmanagedExisting;
                }
                else
                {
                    kind = ModChangeKind.Update;
                }

                changes.Add(new ModChange
                {
                    Mod = mod,
                    Folder = mod.Folder,
                    Kind = kind,
                    TargetExists = targetExists
                });
            }

            HashSet<string> removeFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in manifest.Remove)
            {
                if (!string.IsNullOrEmpty(folder))
                {
                    removeFolders.Add(folder);
                }
            }

            if (config.RemoveObsolete)
            {
                foreach (InstalledMod mod in state.Mods)
                {
                    if (mod != null &&
                        !string.IsNullOrEmpty(mod.Folder) &&
                        !manifestFolders.Contains(mod.Folder))
                    {
                        removeFolders.Add(mod.Folder);
                    }
                }
            }

            foreach (string folder in removeFolders)
            {
                if (!IsValidFolderName(folder) ||
                    folder.Equals(PackPaths.BootstrapFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    error = "Unsafe remove folder '" + folder + "'.";
                    return null;
                }

                // Do not remove a folder that the new manifest installs.
                if (manifestFolders.Contains(folder))
                {
                    continue;
                }

                if (Directory.Exists(GetTargetPath(folder)))
                {
                    changes.Add(new ModChange
                    {
                        Folder = folder,
                        Kind = ModChangeKind.Remove,
                        TargetExists = true
                    });
                }
            }

            if (changes.Count == 0)
            {
                error = "No changes are required.";
            }

            return changes;
        }

        // ------------------------------------------------------------------
        // Staging
        // ------------------------------------------------------------------

        public static bool StagePending(
            PackManifest manifest,
            List<ModChange> changes,
            string stageRoot,
            PackConfig config,
            out PackPending pending,
            out string error)
        {
            pending = null;
            error = null;

            if (string.IsNullOrEmpty(stageRoot) || !Directory.Exists(stageRoot))
            {
                error = "The staging folder does not exist.";
                return false;
            }

            PackPending staged = new PackPending
            {
                PackVersion = manifest.PackVersion,
                CreatedAt = DateTime.UtcNow.ToString("o"),
                StageRoot = stageRoot
            };

            foreach (ModChange change in changes)
            {
                if (change.Kind == ModChangeKind.Remove)
                {
                    if (change.TargetExists)
                    {
                        staged.Remove.Add(change.Folder);
                    }

                    continue;
                }

                string source = Path.Combine(stageRoot, change.Folder);
                if (!Directory.Exists(source))
                {
                    error = "Staged folder is missing: " + source;
                    return false;
                }

                staged.Apply.Add(new PendingMod
                {
                    Folder = change.Folder,
                    Version = change.Mod.Version,
                    Sha256 = change.Mod.Sha256,
                    DisplayName = change.Mod.Label
                });
            }

            if (staged.Apply.Count == 0 && staged.Remove.Count == 0)
            {
                error = "There is nothing to install.";
                return false;
            }

            staged.State.AddRange(BuildInstalledList(manifest));

            try
            {
                staged.Save(PackPaths.PendingFile);
            }
            catch (Exception exception)
            {
                error = "Could not write the pending update file: " + exception.Message;
                return false;
            }

            PruneStaging(stageRoot);

            pending = staged;
            PackLogger.Info("Staged pack " + manifest.PackVersion +
                            ": apply=" + staged.Apply.Count + ", remove=" + staged.Remove.Count);
            return true;
        }

        private static void PruneStaging(string keepStageRoot)
        {
            try
            {
                if (!Directory.Exists(PackPaths.StagingDir))
                {
                    return;
                }

                string keep = Path.GetFullPath(keepStageRoot).TrimEnd(Path.DirectorySeparatorChar);
                foreach (string directory in Directory.GetDirectories(PackPaths.StagingDir))
                {
                    string full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
                    if (!string.Equals(full, keep, StringComparison.OrdinalIgnoreCase))
                    {
                        SafeDeleteDirectory(directory);
                    }
                }
            }
            catch (Exception exception)
            {
                PackLogger.Warn("Could not prune old staging folders: " + exception.Message);
            }
        }

        // ------------------------------------------------------------------
        // Zip handling
        // ------------------------------------------------------------------

        public static void ExtractZipSafe(
            string zipPath,
            string destinationDir,
            int maxEntries,
            long maxUncompressedBytes)
        {
            Directory.CreateDirectory(destinationDir);

            string destinationRoot = Path.GetFullPath(destinationDir);
            if (!destinationRoot.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                destinationRoot += Path.DirectorySeparatorChar;
            }

            using (FileStream stream = File.OpenRead(zipPath))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                if (maxEntries > 0 && archive.Entries.Count > maxEntries)
                {
                    throw new InvalidDataException("The archive contains too many entries.");
                }

                long total = 0;

                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    total += entry.Length;
                    if (maxUncompressedBytes > 0 && total > maxUncompressedBytes)
                    {
                        throw new InvalidDataException("The archive expands beyond the configured limit.");
                    }

                    string name = (entry.FullName ?? "").Replace('\\', '/');
                    if (name.Length == 0)
                    {
                        continue;
                    }

                    if (name.StartsWith("/", StringComparison.Ordinal) ||
                        name.IndexOf(':') >= 0)
                    {
                        throw new InvalidDataException("The archive contains an absolute path: " + name);
                    }

                    string target = Path.GetFullPath(Path.Combine(destinationDir, name));
                    if (!target.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("The archive contains a path outside the destination: " + name);
                    }

                    if (name.EndsWith("/", StringComparison.Ordinal))
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }

                    string parent = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }

                    using (Stream input = entry.Open())
                    using (FileStream output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        input.CopyTo(output);
                    }
                }
            }
        }

        public static string FindModRoot(string extractDir)
        {
            if (File.Exists(Path.Combine(extractDir, "ModInfo.xml")))
            {
                return extractDir;
            }

            string[] dirs = Directory.GetDirectories(extractDir);
            List<string> candidates = dirs
                .Where(dir => File.Exists(Path.Combine(dir, "ModInfo.xml")))
                .ToList();

            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            if (candidates.Count == 0)
            {
                // No ModInfo.xml anywhere. Some mods are pure Config/XUi or
                // asset drops and are still loaded by the game. Unwrap a single
                // top-level folder when the archive has no files at its root;
                // otherwise treat the archive root as the mod folder.
                if (dirs.Length == 1 &&
                    Directory.GetFiles(extractDir).Length == 0 &&
                    !KnownContentFolders.Contains(Path.GetFileName(dirs[0])))
                {
                    return dirs[0];
                }

                return extractDir;
            }

            // Multiple top-level mods are ambiguous. Let the caller report it.
            return null;
        }

        private static bool IsNonEmptyDirectory(string path)
        {
            try
            {
                return Directory.Exists(path) &&
                       Directory.EnumerateFileSystemEntries(path).Any();
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------------
        // State and helpers
        // ------------------------------------------------------------------

        public static PackState LoadState()
        {
            return PackState.Load(PackPaths.StateFile);
        }

        public static List<InstalledMod> BuildInstalledList(PackManifest manifest)
        {
            return manifest.Mods
                .Where(mod => mod != null && mod.IsClientSide)
                .Select(mod => new InstalledMod
                {
                    Folder = mod.Folder,
                    Version = mod.Version,
                    Sha256 = mod.Sha256
                })
                .ToList();
        }

        public static string GetTargetPath(string folder)
        {
            return Path.Combine(PackPaths.ModsRoot, folder);
        }

        public static bool IsValidFolderName(string folder)
        {
            return !string.IsNullOrEmpty(folder) &&
                   SafeFolderRegex.IsMatch(folder) &&
                   folder != "." &&
                   folder != "..";
        }

        public static string ComputeSha256(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        public static string SizeText(long bytes)
        {
            if (bytes < 1024)
            {
                return bytes + " B";
            }

            if (bytes < 1024L * 1024)
            {
                return (bytes / 1024.0).ToString("0.0") + " KB";
            }

            if (bytes < 1024L * 1024 * 1024)
            {
                return (bytes / (1024.0 * 1024.0)).ToString("0.0") + " MB";
            }

            return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("0.00") + " GB";
        }

        public static void SafeDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch (Exception exception)
            {
                PackLogger.Warn("Could not delete '" + path + "': " + exception.Message);
            }
        }

        public static void CleanupTemp()
        {
            SafeDeleteDirectory(PackPaths.TempDir);
            SafeDeleteDirectory(PackPaths.ExtractDir);
        }

        private static bool IsHostAllowed(Uri uri, PackConfig config)
        {
            if (uri.Scheme == Uri.UriSchemeHttps)
            {
                // An empty allowlist means any HTTPS host.
            }
            else if (uri.Scheme == Uri.UriSchemeHttp && config.AllowInsecureHttp)
            {
                // Explicitly allowed for local testing.
            }
            else
            {
                return false;
            }

            if (config.AllowedHosts == null || config.AllowedHosts.Count == 0)
            {
                return true;
            }

            string host = uri.Host;
            foreach (string allowed in config.AllowedHosts)
            {
                if (string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

    }
}

