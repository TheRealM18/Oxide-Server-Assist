using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;

namespace QLNPackBootstrap
{
    internal sealed class PackConfig
    {
        public string ManifestUrl = "";
        public string SignatureUrl = "";
        public string PublicKeyXml = "";
        public bool RequireSignature;
        public List<string> AllowedHosts = new List<string>();
        public bool AllowInsecureHttp;
        public bool CheckOnMainMenu = true;
        public bool ShowMainMenuButton = true;
        public bool ShowCheckErrors;
        public int KeepBackups = 3;
        public long MaxModBytes = 2L * 1024 * 1024 * 1024;
        public long MaxTotalBytes = 8L * 1024 * 1024 * 1024;
        public int MaxEntries = 100000;
        public long MaxUncompressedBytes = 8L * 1024 * 1024 * 1024;
        public bool RemoveObsolete;
        public string GameVersion = "";
        public string GameExe = "";
        public bool AutoRelaunch = true;
        public string AccessToken = "";
        public bool AutoRestart = true;
        public string ModsRoot = "";
        public int MaxParallelDownloads = 4;

        public static PackConfig Load(string path)
        {
            PackConfig config = new PackConfig();

            if (!File.Exists(path))
            {
                return config;
            }

            XmlElement root;
            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(path);
                root = document.DocumentElement;
            }
            catch
            {
                return config;
            }

            if (root == null)
            {
                return config;
            }

            config.ManifestUrl = Get(root, "ManifestUrl", "");
            config.SignatureUrl = Get(root, "SignatureUrl", "");
            config.PublicKeyXml = Get(root, "PublicKeyXml", "");
            config.RequireSignature = GetBool(root, "RequireSignature", false);
            config.AllowInsecureHttp = GetBool(root, "AllowInsecureHttp", false);
            config.CheckOnMainMenu = GetBool(root, "CheckOnMainMenu", true);
            config.ShowMainMenuButton = GetBool(root, "ShowMainMenuButton", true);
            config.ShowCheckErrors = GetBool(root, "ShowCheckErrors", false);
            config.KeepBackups = GetInt(root, "KeepBackups", 3);
            config.MaxModBytes = GetLong(root, "MaxModBytes", config.MaxModBytes);
            config.MaxTotalBytes = GetLong(root, "MaxTotalBytes", config.MaxTotalBytes);
            config.MaxEntries = GetInt(root, "MaxEntries", config.MaxEntries);
            config.MaxUncompressedBytes = GetLong(root, "MaxUncompressedBytes", config.MaxUncompressedBytes);
            config.RemoveObsolete = GetBool(root, "RemoveObsolete", false);
            config.GameVersion = Get(root, "GameVersion", "");
            config.GameExe = Get(root, "GameExe", "");
            config.AutoRelaunch = GetBool(root, "AutoRelaunch", true);
            config.AccessToken = Get(root, "AccessToken", "");
            config.AutoRestart = GetBool(root, "AutoRestart", true);
            config.ModsRoot = Get(root, "ModsRoot", "");
            config.MaxParallelDownloads = GetInt(root, "MaxParallelDownloads", 4);

            string hosts = Get(root, "AllowedHosts", "");

            foreach (string host in hosts.Split(','))
            {
                if (!string.IsNullOrWhiteSpace(host))
                {
                    config.AllowedHosts.Add(host.Trim());
                }
            }

            if (string.IsNullOrEmpty(config.SignatureUrl) && !string.IsNullOrEmpty(config.ManifestUrl))
            {
                Uri manifestUri;
                if (Uri.TryCreate(config.ManifestUrl, UriKind.Absolute, out manifestUri) &&
                    manifestUri.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                    manifestUri.Host.IndexOf("drive.google.com", StringComparison.OrdinalIgnoreCase) < 0 &&
                    manifestUri.Host.IndexOf("dropbox.com", StringComparison.OrdinalIgnoreCase) < 0 &&
                    manifestUri.Host.IndexOf("1drv.ms", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    config.SignatureUrl = config.ManifestUrl + ".sig";
                }
            }

            return config;
        }

        private static string Get(XmlElement root, string name, string fallback)
        {
            XmlNode node = root.SelectSingleNode(name);
            if (node == null)
            {
                return fallback;
            }

            string value = node.InnerText;
            return value == null ? fallback : value.Trim();
        }

        private static bool GetBool(XmlElement root, string name, bool fallback)
        {
            bool value;
            return bool.TryParse(Get(root, name, ""), out value) ? value : fallback;
        }

        private static int GetInt(XmlElement root, string name, int fallback)
        {
            int value;
            return int.TryParse(Get(root, name, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value
                : fallback;
        }

        private static long GetLong(XmlElement root, string name, long fallback)
        {
            long value;
            return long.TryParse(Get(root, name, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value
                : fallback;
        }
    }
}
