using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MediaRelay;

/// <summary>
/// Stores Twitch OAuth tokens encrypted at rest. The encryption key lives in
/// data/secret.key and never leaves the relay machine.
/// </summary>
public sealed class TokenStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _sync = new();
    private readonly string _tokensPath;
    private readonly string _keyPath;
    private readonly byte[] _key;
    private Dictionary<string, TokenRecord> _records = new(StringComparer.Ordinal);

    public TokenStore(RelayConfig config)
    {
        string dir = config.ResolvedDataDir;
        Directory.CreateDirectory(dir);
        UnixPermissions.RestrictDirectory(dir);
        _tokensPath = Path.Combine(dir, "tokens.enc");
        _keyPath = Path.Combine(dir, "secret.key");
        _key = LoadOrCreateKey();
        Load();
    }

    public IReadOnlyList<TokenRecord> All()
    {
        lock (_sync)
        {
            return _records.Values.ToList();
        }
    }

    public TokenRecord? GetByTwitchUserId(string twitchUserId)
    {
        if (string.IsNullOrWhiteSpace(twitchUserId))
        {
            return null;
        }

        lock (_sync)
        {
            return _records.TryGetValue(twitchUserId, out TokenRecord? record) ? record : null;
        }
    }

    public TokenRecord GetOrCreate(string twitchUserId)
    {
        lock (_sync)
        {
            if (!_records.TryGetValue(twitchUserId, out TokenRecord? record))
            {
                record = new TokenRecord { Platform = "twitch", TwitchUserId = twitchUserId };
                _records[twitchUserId] = record;
            }

            return record;
        }
    }

    public TokenRecord? FindBySteamId(string serverId, string steamId)
    {
        lock (_sync)
        {
            return _records.Values.FirstOrDefault(record =>
                record.Servers.Any(link =>
                    string.Equals(link.ServerId, serverId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(link.SteamId, steamId, StringComparison.Ordinal)));
        }
    }

    public List<LinkInfo> GetLinks(string serverId)
    {
        lock (_sync)
        {
            return _records.Values
                .SelectMany(record => record.Servers
                    .Where(link => string.Equals(link.ServerId, serverId, StringComparison.OrdinalIgnoreCase))
                    .Select(link => new LinkInfo
                    {
                        Platform = string.IsNullOrEmpty(record.Platform) ? "twitch" : record.Platform,
                        SteamId = link.SteamId,
                        UserId = record.TwitchUserId,
                        Login = record.Login,
                        DisplayName = record.DisplayName,
                        LinkedAt = link.LinkedAt
                    }))
                .OrderBy(link => link.Login, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public List<string> GetServersForBroadcaster(string twitchUserId)
    {
        lock (_sync)
        {
            return _records.TryGetValue(twitchUserId, out TokenRecord? record)
                ? record.Servers.Select(link => link.ServerId).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string>();
        }
    }

    public void Remove(string twitchUserId)
    {
        lock (_sync)
        {
            _records.Remove(twitchUserId);
            SaveLocked();
        }
    }

    public void Save()
    {
        lock (_sync)
        {
            SaveLocked();
        }
    }

    private void Load()
    {
        if (!File.Exists(_tokensPath))
        {
            return;
        }

        try
        {
            string json = File.ReadAllText(_tokensPath);
            EncryptedBlob? blob = JsonSerializer.Deserialize<EncryptedBlob>(json, JsonOptions);
            if (blob == null)
            {
                return;
            }

            byte[] nonce = Convert.FromBase64String(blob.Nonce);
            byte[] tag = Convert.FromBase64String(blob.Tag);
            byte[] cipher = Convert.FromBase64String(blob.Data);
            byte[] plain = new byte[cipher.Length];

            using AesGcm aes = new(_key, 16);
            aes.Decrypt(nonce, cipher, tag, plain);

            Dictionary<string, TokenRecord>? records =
                JsonSerializer.Deserialize<Dictionary<string, TokenRecord>>(plain, JsonOptions);
            if (records != null)
            {
                _records = new Dictionary<string, TokenRecord>(records, StringComparer.Ordinal);
            }

            Console.WriteLine("[MediaRelay] loaded " + _records.Count + " linked Twitch account(s).");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[MediaRelay] could not decrypt token store: " + ex.Message);
        }
    }

    private void SaveLocked()
    {
        try
        {
            byte[] plain = JsonSerializer.SerializeToUtf8Bytes(_records, JsonOptions);
            byte[] nonce = RandomNumberGenerator.GetBytes(12);
            byte[] cipher = new byte[plain.Length];
            byte[] tag = new byte[16];

            using (AesGcm aes = new(_key, 16))
            {
                aes.Encrypt(nonce, plain, cipher, tag);
            }

            EncryptedBlob blob = new()
            {
                Nonce = Convert.ToBase64String(nonce),
                Tag = Convert.ToBase64String(tag),
                Data = Convert.ToBase64String(cipher)
            };

            string tmp = _tokensPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(blob, JsonOptions));
            UnixPermissions.RestrictFile(tmp);
            File.Move(tmp, _tokensPath, true);
            UnixPermissions.RestrictFile(_tokensPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[MediaRelay] could not save token store: " + ex.Message);
        }
    }

    private byte[] LoadOrCreateKey()
    {
        if (File.Exists(_keyPath))
        {
            byte[] existing = File.ReadAllBytes(_keyPath);
            if (existing.Length == 32)
            {
                return existing;
            }

            Console.WriteLine("[MediaRelay] WARNING: secret.key is not 32 bytes; generating a new key. Existing tokens cannot be read.");
        }

        byte[] key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(_keyPath, key);
        UnixPermissions.RestrictFile(_keyPath);
        return key;
    }

    private sealed class EncryptedBlob
    {
        public string Nonce { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
    }
}
