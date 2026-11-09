using System.Text;
using Microsoft.AspNetCore.Http;

namespace MediaRelay;

public static class ServerAuth
{
    private const int MaxClockSkewSeconds = 300;

    public static async Task<(ServerConfig? Server, string Error)> AuthenticateAsync(
        HttpContext context,
        RelayConfig config,
        NonceCache nonces)
    {
        string serverId = context.Request.Headers["X-Server-Id"].ToString().Trim();
        string timestamp = context.Request.Headers["X-Timestamp"].ToString().Trim();
        string nonce = context.Request.Headers["X-Nonce"].ToString().Trim();
        string signature = context.Request.Headers["X-Signature"].ToString().Trim();

        if (string.IsNullOrEmpty(serverId) ||
            string.IsNullOrEmpty(timestamp) ||
            string.IsNullOrEmpty(nonce) ||
            string.IsNullOrEmpty(signature))
        {
            return (null, "Missing authentication headers.");
        }

        ServerConfig? server = config.FindServer(serverId);
        if (server == null || string.IsNullOrEmpty(server.ApiKey))
        {
            return (null, "Unknown server id.");
        }

        if (!long.TryParse(timestamp, out long unixSeconds))
        {
            return (null, "Invalid timestamp.");
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - unixSeconds) > MaxClockSkewSeconds)
        {
            return (null, "Request timestamp is outside the allowed window.");
        }

        if (nonce.Length < 8 || nonce.Length > 128)
        {
            return (null, "Invalid nonce.");
        }

        context.Request.EnableBuffering();
        string body;
        using (StreamReader reader = new(
            context.Request.Body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096,
            leaveOpen: true))
        {
            body = await reader.ReadToEndAsync();
        }

        context.Request.Body.Position = 0;

        string expected = Crypto.Hmac(server.ApiKey, Crypto.Canonical(timestamp, nonce, body));
        if (!Crypto.FixedTimeEquals(expected, signature))
        {
            return (null, "Invalid request signature.");
        }

        if (!nonces.TryUse(nonce))
        {
            return (null, "Request nonce was already used.");
        }

        return (server, string.Empty);
    }
}
