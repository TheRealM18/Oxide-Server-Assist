using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediaRelay;

public sealed class RelayConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string EventSubUrl { get; set; } = "wss://eventsub.wss.twitch.tv/ws";
    public string Listen { get; set; } = "http://127.0.0.1:8090";
    public string PublicBaseUrl { get; set; } = "http://127.0.0.1:8090";
    public string DataDir { get; set; } = "data";
    public int LinkSessionMinutes { get; set; } = 10;
    public int EventRetentionHours { get; set; } = 72;
    public int MaxEventsPerServer { get; set; } = 2000;
    public int HttpTimeoutSeconds { get; set; } = 20;
    public int ReconcileMinutes { get; set; } = 5;
    public bool EnableChat { get; set; } = false;
    public bool EnableFollows { get; set; } = true;
    public bool EnableHypeTrain { get; set; } = true;
    public bool RequestManageRedemptions { get; set; } = false;
    public List<ServerConfig> Servers { get; set; } = new();

    [JsonIgnore]
    public string ConfigPath { get; private set; } = string.Empty;

    [JsonIgnore]
    public string ResolvedDataDir =>
        Path.IsPathRooted(DataDir)
            ? DataDir
            : Path.GetFullPath(Path.Combine(
                string.IsNullOrEmpty(ConfigPath) ? AppContext.BaseDirectory : Path.GetDirectoryName(ConfigPath)!,
                DataDir));

    public static RelayConfig Load(string[] args)
    {
        string path = ResolveConfigPath(args);
        RelayConfig config;
        bool created = false;

        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            config = JsonSerializer.Deserialize<RelayConfig>(json, JsonOptions) ?? new RelayConfig();
        }
        else
        {
            config = new RelayConfig();
            created = true;
        }

        config.ConfigPath = path;
        config.Normalize();
        bool dirty = created || !File.Exists(path);
        dirty |= config.EnsureServerKeys();

        if (dirty)
        {
            try
            {
                File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
                UnixPermissions.RestrictFile(path);
                Console.WriteLine("[MediaRelay] wrote a starter config to " + path);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[MediaRelay] could not write config " + path + ": " + ex.Message);
            }
        }

        if (string.IsNullOrWhiteSpace(config.ClientId))
        {
            Console.WriteLine("[MediaRelay] WARNING: ClientId is empty. Create a Twitch application and set it in " + path + ".");
        }

        return config;
    }

    private static string ResolveConfigPath(string[] args)
    {
        string? fromEnv = Environment.GetEnvironmentVariable("MEDIA_RELAY_CONFIG");
        if (string.IsNullOrWhiteSpace(fromEnv))
        {
            fromEnv = Environment.GetEnvironmentVariable("TWITCH_RELAY_CONFIG");
        }

        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return Path.GetFullPath(fromEnv);
        }

        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) && !args[0].StartsWith("-", StringComparison.Ordinal))
        {
            return Path.GetFullPath(args[0]);
        }

        string local = Path.Combine(AppContext.BaseDirectory, "relay.json");
        if (File.Exists(local))
        {
            return local;
        }

        string appSettings = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(appSettings))
        {
            return appSettings;
        }

        return local;
    }

    private void Normalize()
    {
        ClientId = (ClientId ?? string.Empty).Trim();
        ClientSecret = (ClientSecret ?? string.Empty).Trim();
        EventSubUrl = string.IsNullOrWhiteSpace(EventSubUrl)
            ? "wss://eventsub.wss.twitch.tv/ws"
            : EventSubUrl.Trim();
        Listen = string.IsNullOrWhiteSpace(Listen) ? "http://127.0.0.1:8090" : Listen.Trim();
        PublicBaseUrl = string.IsNullOrWhiteSpace(PublicBaseUrl)
            ? Listen.TrimEnd('/')
            : PublicBaseUrl.Trim().TrimEnd('/');
        DataDir = string.IsNullOrWhiteSpace(DataDir) ? "data" : DataDir.Trim();
        LinkSessionMinutes = Math.Clamp(LinkSessionMinutes, 1, 60);
        EventRetentionHours = Math.Clamp(EventRetentionHours, 1, 24 * 30);
        MaxEventsPerServer = Math.Clamp(MaxEventsPerServer, 50, 20000);
        HttpTimeoutSeconds = Math.Clamp(HttpTimeoutSeconds, 5, 120);
        ReconcileMinutes = Math.Clamp(ReconcileMinutes, 1, 60);

        Servers ??= new List<ServerConfig>();
        foreach (ServerConfig server in Servers)
        {
            server.Id = (server.Id ?? string.Empty).Trim().ToLowerInvariant();
            server.Name = (server.Name ?? string.Empty).Trim();
            server.ApiKey = (server.ApiKey ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(server.Name))
            {
                server.Name = server.Id;
            }
        }

        Servers.RemoveAll(s => string.IsNullOrEmpty(s.Id));
        foreach (IGrouping<string, ServerConfig> duplicate in Servers.GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            Console.WriteLine("[MediaRelay] WARNING: duplicate server id '" + duplicate.Key + "' in config; only the first one is used.");
        }

        Servers = Servers
            .GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private bool EnsureServerKeys()
    {
        bool changed = false;

        if (Servers.Count == 0)
        {
            Servers.Add(new ServerConfig
            {
                Id = "main",
                Name = "Main Server",
                ApiKey = Crypto.NewApiKey()
            });
            return true;
        }

        foreach (ServerConfig server in Servers)
        {
            if (string.IsNullOrEmpty(server.ApiKey))
            {
                server.ApiKey = Crypto.NewApiKey();
                changed = true;
            }
        }

        return changed;
    }

    public List<string> BuildScopes()
    {
        List<string> scopes = new()
        {
            "channel:read:redemptions",
            "bits:read",
            "channel:read:subscriptions"
        };

        if (EnableHypeTrain)
        {
            scopes.Add("channel:read:hype_train");
        }

        if (EnableFollows)
        {
            scopes.Add("moderator:read:followers");
        }

        if (EnableChat)
        {
            scopes.Add("user:read:chat");
        }

        if (RequestManageRedemptions)
        {
            scopes.Add("channel:manage:redemptions");
        }

        return scopes.Distinct(StringComparer.Ordinal).ToList();
    }

    public ServerConfig? FindServer(string id)
    {
        return Servers.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class ServerConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}
