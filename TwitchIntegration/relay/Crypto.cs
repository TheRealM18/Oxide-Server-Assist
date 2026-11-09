using System.Security.Cryptography;
using System.Text;

namespace TwitchRelay;

public static class Crypto
{
    public static string NewApiKey()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    public static string Hmac(string secret, string canonical)
    {
        using HMACSHA256 hmac = new(Encoding.UTF8.GetBytes(secret));
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToBase64String(hash);
    }

    public static bool FixedTimeEquals(string left, string right)
    {
        if (left == null || right == null)
        {
            return false;
        }

        byte[] a = Encoding.UTF8.GetBytes(left);
        byte[] b = Encoding.UTF8.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    public static string Canonical(string timestamp, string nonce, string body)
    {
        return timestamp + "\n" + nonce + "\n" + (body ?? string.Empty);
    }
}

/// <summary>
/// Small in-memory replay cache for request nonces.
/// </summary>
public sealed class NonceCache
{
    private readonly Dictionary<string, DateTimeOffset> _seen = new(StringComparer.Ordinal);
    private DateTimeOffset _nextPrune = DateTimeOffset.UtcNow.AddMinutes(1);

    public bool TryUse(string nonce)
    {
        lock (_seen)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (now >= _nextPrune)
            {
                _nextPrune = now.AddMinutes(1);
                foreach (string key in _seen.Where(pair => pair.Value < now).Select(pair => pair.Key).ToList())
                {
                    _seen.Remove(key);
                }
            }

            if (_seen.ContainsKey(nonce))
            {
                return false;
            }

            // Keep nonces longer than the allowed clock skew.
            _seen[nonce] = now.AddMinutes(10);
            return true;
        }
    }
}
