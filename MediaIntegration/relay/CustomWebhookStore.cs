using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MediaRelay;

/// <summary>
/// Per-streamer tokens for the generic custom webhook provider. Only the
/// SHA-256 hash of the token is stored; the plaintext token is returned once
/// when the link is created.
/// </summary>
public sealed class CustomWebhookStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _sync = new();
    private readonly string _path;
    private Dictionary<string, CustomWebhookLink> _links = new(StringComparer.Ordinal);

    public CustomWebhookStore(RelayConfig config)
    {
        Directory.CreateDirectory(config.ResolvedDataDir);
        _path = Path.Combine(config.ResolvedDataDir, "customlinks.json");
        Load();
    }

    public CustomWebhookLink Create(string serverId, string steamId, string playerName)
    {
        string token = Crypto.NewApiKey();
        CustomWebhookLink link = new()
        {
            LinkId = Guid.NewGuid().ToString("N"),
            TokenHash = Hash(token),
            ServerId = serverId,
            SteamId = steamId,
            PlayerName = playerName,
            CreatedAt = DateTimeOffset.UtcNow
        };

        lock (_sync)
        {
            _links[link.TokenHash] = link;
            SaveLocked();
        }

        link.Token = token;
        return link;
    }

    public CustomWebhookLink? Find(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        lock (_sync)
        {
            return _links.TryGetValue(Hash(token), out CustomWebhookLink? link) ? link : null;
        }
    }

    public CustomWebhookLink? FindBySteamId(string serverId, string steamId)
    {
        lock (_sync)
        {
            return _links.Values.FirstOrDefault(link =>
                string.Equals(link.ServerId, serverId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(link.SteamId, steamId, StringComparison.Ordinal));
        }
    }

    public bool RemoveByServerSteam(string serverId, string steamId)
    {
        lock (_sync)
        {
            string? key = _links
                .Where(pair =>
                    string.Equals(pair.Value.ServerId, serverId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(pair.Value.SteamId, steamId, StringComparison.Ordinal))
                .Select(pair => pair.Key)
                .FirstOrDefault();

            if (key == null)
            {
                return false;
            }

            _links.Remove(key);
            SaveLocked();
            return true;
        }
    }

    public bool Remove(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        lock (_sync)
        {
            if (!_links.Remove(Hash(token)))
            {
                return false;
            }

            SaveLocked();
            return true;
        }
    }

    public List<LinkInfo> GetLinks(string serverId)
    {
        lock (_sync)
        {
            return _links.Values
                .Where(link => string.Equals(link.ServerId, serverId, StringComparison.OrdinalIgnoreCase))
                .Select(link => new LinkInfo
                {
                    Platform = "custom",
                    SteamId = link.SteamId,
                    UserId = link.LinkId,
                    Login = "custom",
                    DisplayName = "Custom Webhook",
                    LinkedAt = link.CreatedAt
                })
                .OrderBy(link => link.SteamId, StringComparer.Ordinal)
                .ToList();
        }
    }

    public int Count(string serverId)
    {
        lock (_sync)
        {
            return _links.Values.Count(link =>
                string.Equals(link.ServerId, serverId, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string Hash(string token)
    {
        using SHA256 sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }

    private void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            List<CustomWebhookLink>? links =
                JsonSerializer.Deserialize<List<CustomWebhookLink>>(File.ReadAllText(_path), JsonOptions);
            if (links == null)
            {
                return;
            }

            _links = links
                .Where(link => !string.IsNullOrEmpty(link.TokenHash))
                .ToDictionary(link => link.TokenHash, link => link, StringComparer.Ordinal);

            bool backfilled = false;
            foreach (CustomWebhookLink link in _links.Values)
            {
                if (string.IsNullOrEmpty(link.LinkId))
                {
                    // Older records predate the non-secret link id.
                    link.LinkId = link.TokenHash;
                    backfilled = true;
                }
            }

            if (backfilled)
            {
                SaveLocked();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[MediaRelay] could not load custom webhook links: " + ex.Message);
        }
    }

    private void SaveLocked()
    {
        try
        {
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_links.Values.ToList(), JsonOptions));
            UnixPermissions.RestrictFile(tmp);
            File.Move(tmp, _path, true);
            UnixPermissions.RestrictFile(_path);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[MediaRelay] could not save custom webhook links: " + ex.Message);
        }
    }
}

public sealed class CustomWebhookLink
{
    public string LinkId { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public string ServerId { get; set; } = string.Empty;
    public string SteamId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [System.Text.Json.Serialization.JsonIgnore]
    public string Token { get; set; } = string.Empty;
}
