using System.Collections.Generic;
using Newtonsoft.Json;

namespace QLNPackBootstrap
{
    internal sealed class PackManifest
    {
        [JsonProperty("schemaVersion")]
        public int SchemaVersion = 1;

        [JsonProperty("packVersion")]
        public string PackVersion = "";

        [JsonProperty("gameVersion")]
        public string GameVersion = "";

        [JsonProperty("generatedAt")]
        public string GeneratedAt = "";

        [JsonProperty("notes")]
        public string Notes = "";

        [JsonProperty("mods")]
        public List<PackModEntry> Mods = new List<PackModEntry>();

        [JsonProperty("remove")]
        public List<string> Remove = new List<string>();
    }

    internal sealed class PackModEntry
    {
        [JsonProperty("name")]
        public string Name = "";

        [JsonProperty("displayName")]
        public string DisplayName = "";

        [JsonProperty("version")]
        public string Version = "";

        [JsonProperty("url")]
        public string Url = "";

        [JsonProperty("sha256")]
        public string Sha256 = "";

        [JsonProperty("size")]
        public long Size = 0;

        /// <summary>client, server, or both. Defaults to both.</summary>
        [JsonProperty("target")]
        public string Target = "";

        [JsonProperty("installFolder")]
        public string InstallFolder = "";

        [JsonProperty("required")]
        public bool Required = true;

        public string Folder
        {
            get { return string.IsNullOrEmpty(InstallFolder) ? Name : InstallFolder; }
        }

        public string Label
        {
            get { return string.IsNullOrEmpty(DisplayName) ? Name : DisplayName; }
        }

        public bool IsClientSide
        {
            get
            {
                if (string.IsNullOrEmpty(Target))
                {
                    return true;
                }

                return Target != "server";
            }
        }
    }
}
