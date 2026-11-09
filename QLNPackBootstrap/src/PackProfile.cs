using Newtonsoft.Json;

namespace QLNPackBootstrap
{
    /// <summary>
    /// One selectable mod pack. A profile can come from packbootstrap.xml or
    /// from a remote pack index.
    /// </summary>
    internal sealed class PackProfile
    {
        [JsonProperty("id")]
        public string Id = "";

        [JsonProperty("name")]
        public string Name = "";

        [JsonProperty("manifestUrl")]
        public string ManifestUrl = "";

        [JsonProperty("signatureUrl")]
        public string SignatureUrl = "";

        [JsonProperty("accessToken")]
        public string AccessToken = "";

        [JsonProperty("gameVersion")]
        public string GameVersion = "";

        [JsonProperty("notes")]
        public string Notes = "";

        [JsonProperty("vanilla")]
        public bool IsVanilla;

        public bool IsRemote;

        public string Label
        {
            get { return string.IsNullOrEmpty(Name) ? Id : Name; }
        }
    }

    internal sealed class PackIndex
    {
        [JsonProperty("packs")]
        public System.Collections.Generic.List<PackProfile> Packs =
            new System.Collections.Generic.List<PackProfile>();
    }
}
