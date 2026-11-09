using System;
using System.IO;
using System.Reflection;

namespace QLNPackBootstrap
{
    /// <summary>
    /// All paths are derived from the bootstrap DLL location, so the mod works
    /// regardless of where the user's Mods folder lives.
    /// </summary>
    internal static class PackPaths
    {
        public const string BootstrapFolderName = "000_QLNPackBootstrap";
        public const string DataFolderName = "Data";

        // Test hook. Null in the game.
        internal static string BootstrapDirOverride = null;

        // Optional override from packbootstrap.xml. Empty means "the folder that
        // contains the bootstrap mod folder", i.e. the active Mods directory.
        internal static string ModsRootOverride = null;

        public static string BootstrapDir
        {
            get
            {
                if (!string.IsNullOrEmpty(BootstrapDirOverride))
                {
                    return BootstrapDirOverride;
                }

                string location = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(location))
                {
                    return Path.GetDirectoryName(location);
                }

                // Should only happen if an unusual loader loaded us from bytes.
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "7DaysToDie",
                    "Mods",
                    BootstrapFolderName);
            }
        }

        public static string ModsRoot
        {
            get
            {
                string overridePath = ModsRootOverride;

                if (!string.IsNullOrWhiteSpace(overridePath))
                {
                    return Path.IsPathRooted(overridePath)
                        ? Path.GetFullPath(overridePath)
                        : Path.GetFullPath(Path.Combine(BootstrapDir, overridePath));
                }

                // The folder that contains 000_QLNPackBootstrap. This is
                // whichever Mods directory the running bootstrap was loaded
                // from, so custom profiles/launchers work automatically.
                return Path.GetDirectoryName(BootstrapDir);
            }
        }

        public static string DataDir
        {
            get { return Path.Combine(BootstrapDir, DataFolderName); }
        }

        public static string ConfigFile
        {
            get { return Path.Combine(BootstrapDir, "packbootstrap.xml"); }
        }

        public static string StateFile
        {
            get { return Path.Combine(DataDir, "state.xml"); }
        }

        public static string PendingFile
        {
            get { return Path.Combine(DataDir, "pending.xml"); }
        }

        public static string UpdaterFailedFile
        {
            get { return Path.Combine(DataDir, "updater-failed.txt"); }
        }

        public static string StagingDir
        {
            get { return Path.Combine(DataDir, "staging"); }
        }

        public static string TempDir
        {
            get { return Path.Combine(DataDir, "temp"); }
        }

        // Short path used while a zip is being extracted. The long staging path
        // plus deep mod paths can exceed the Windows 260-character limit.
        public static string ExtractDir
        {
            get { return Path.Combine(DataDir, "x"); }
        }

        public static string BackupDir
        {
            get { return Path.Combine(DataDir, "backup"); }
        }

        public static string LogsDir
        {
            get { return Path.Combine(DataDir, "logs"); }
        }

        public static string LogFile
        {
            get { return Path.Combine(LogsDir, "bootstrap.log"); }
        }
    }
}
