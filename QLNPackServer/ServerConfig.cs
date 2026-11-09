using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace QLNPackServer
{
    /// <summary>
    /// JSON configuration. Property names intentionally match the JSON keys so
    /// JavaScriptSerializer maps them without attributes.
    /// </summary>
    public sealed class ServerConfig
    {
        public string bindAddress { get; set; } = "0.0.0.0";
        public int port { get; set; } = 8765;
        public string publicBaseUrl { get; set; } = "";
        public string accessToken { get; set; } = "";
        public string modsPath { get; set; } = "Mods";
        public string cacheDir { get; set; } = "cache";
        public List<string> include { get; set; } = new List<string>();
        public List<string> exclude { get; set; } = new List<string>
        {
            "000_QLNPackBootstrap",
            "0_TFP_Harmony",
            "TFP_CommandExtensions",
            "Xample_MarkersMod"
        };
        public string packVersion { get; set; } = "auto";
        public string gameVersion { get; set; } = "3.4";
        public string notes { get; set; } = "";
        public List<string> remove { get; set; } = new List<string>();
        public string signingKeyXml { get; set; } = "";
        public int refreshSeconds { get; set; } = 30;
        public string logFile { get; set; } = "QLNPackServer.log";

        public static ServerConfig Load(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            JavaScriptSerializer serializer = new JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue
            };

            return serializer.Deserialize<ServerConfig>(File.ReadAllText(path));
        }

        public void Normalize(string baseDir)
        {
            if (string.IsNullOrWhiteSpace(bindAddress))
            {
                bindAddress = "0.0.0.0";
            }

            accessToken = (accessToken ?? "").Trim();

            if (port <= 0 || port > 65535)
            {
                port = 8765;
            }

            if (string.IsNullOrWhiteSpace(modsPath))
            {
                modsPath = "Mods";
            }

            if (string.IsNullOrWhiteSpace(cacheDir))
            {
                cacheDir = "cache";
            }

            if (string.IsNullOrWhiteSpace(logFile))
            {
                logFile = "QLNPackServer.log";
            }

            modsPath = Resolve(baseDir, modsPath);
            cacheDir = Resolve(baseDir, cacheDir);
            logFile = Resolve(baseDir, logFile);

            if (!string.IsNullOrWhiteSpace(signingKeyXml))
            {
                signingKeyXml = Resolve(baseDir, signingKeyXml);
            }

            if (publicBaseUrl != null)
            {
                publicBaseUrl = publicBaseUrl.Trim().TrimEnd('/');
            }

            if (include == null)
            {
                include = new List<string>();
            }

            if (exclude == null)
            {
                exclude = new List<string>();
            }

            if (remove == null)
            {
                remove = new List<string>();
            }

            if (refreshSeconds < 0)
            {
                refreshSeconds = 0;
            }

            if (string.IsNullOrWhiteSpace(packVersion))
            {
                packVersion = "auto";
            }

            if (string.IsNullOrWhiteSpace(gameVersion))
            {
                gameVersion = "3.4";
            }

            include = TrimList(include);
            exclude = TrimList(exclude);
            remove = TrimList(remove);
        }

        private static string Resolve(string baseDir, string path)
        {
            return Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(baseDir, path));
        }

        private static List<string> TrimList(List<string> values)
        {
            List<string> result = new List<string>();
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result.Add(value.Trim());
                }
            }

            return result;
        }
    }
}
