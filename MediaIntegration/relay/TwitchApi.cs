using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MediaRelay;

public sealed class TwitchApi
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly RelayConfig _config;
    private readonly ILogger<TwitchApi> _log;
    private readonly HttpClient _http;

    public TwitchApi(RelayConfig config, ILogger<TwitchApi> log)
    {
        _config = config;
        _log = log;
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(config.HttpTimeoutSeconds)
        };
    }

    public async Task<DeviceCodeResponse> StartDeviceCodeAsync(CancellationToken ct)
    {
        Dictionary<string, string> form = new()
        {
            ["client_id"] = _config.ClientId,
            ["scopes"] = string.Join(" ", _config.BuildScopes())
        };

        using HttpRequestMessage request = new(HttpMethod.Post, "https://id.twitch.tv/oauth2/device")
        {
            Content = new FormUrlEncodedContent(form)
        };

        using HttpResponseMessage response = await _http.SendAsync(request, ct);
        string body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new TwitchApiException((int)response.StatusCode, body, "Twitch device-code request failed: " + body);
        }

        return JsonSerializer.Deserialize<DeviceCodeResponse>(body, JsonOptions)
               ?? throw new TwitchApiException(500, body, "Twitch returned an empty device-code response.");
    }

    public async Task<TokenResponse> PollDeviceTokenAsync(string deviceCode, CancellationToken ct)
    {
        Dictionary<string, string> form = new()
        {
            ["client_id"] = _config.ClientId,
            ["scopes"] = string.Join(" ", _config.BuildScopes()),
            ["device_code"] = deviceCode,
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
        };

        if (!string.IsNullOrEmpty(_config.ClientSecret))
        {
            form["client_secret"] = _config.ClientSecret;
        }

        using HttpRequestMessage request = new(HttpMethod.Post, "https://id.twitch.tv/oauth2/token")
        {
            Content = new FormUrlEncodedContent(form)
        };

        using HttpResponseMessage response = await _http.SendAsync(request, ct);
        string body = await response.Content.ReadAsStringAsync(ct);

        if (response.IsSuccessStatusCode)
        {
            return JsonSerializer.Deserialize<TokenResponse>(body, JsonOptions)
                   ?? throw new TwitchApiException(500, body, "Twitch returned an empty token response.");
        }

        string error = ReadOAuthError(body);
        throw new TwitchOAuthException(error, "Twitch device authorization: " + error);
    }

    public async Task<TokenResponse> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        Dictionary<string, string> form = new()
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = _config.ClientId
        };

        if (!string.IsNullOrEmpty(_config.ClientSecret))
        {
            form["client_secret"] = _config.ClientSecret;
        }

        using HttpRequestMessage request = new(HttpMethod.Post, "https://id.twitch.tv/oauth2/token")
        {
            Content = new FormUrlEncodedContent(form)
        };

        using HttpResponseMessage response = await _http.SendAsync(request, ct);
        string body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new TwitchApiException((int)response.StatusCode, body, "Twitch token refresh failed: " + body);
        }

        return JsonSerializer.Deserialize<TokenResponse>(body, JsonOptions)
               ?? throw new TwitchApiException(500, body, "Twitch returned an empty refresh response.");
    }

    public async Task<ValidateResponse> ValidateAsync(string accessToken, CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
        request.Headers.TryAddWithoutValidation("Authorization", "OAuth " + accessToken);

        using HttpResponseMessage response = await _http.SendAsync(request, ct);
        string body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new TwitchApiException((int)response.StatusCode, body, "Twitch token validation failed: " + body);
        }

        return JsonSerializer.Deserialize<ValidateResponse>(body, JsonOptions)
               ?? throw new TwitchApiException(500, body, "Twitch returned an empty validation response.");
    }

    public async Task<HelixUser> GetUserAsync(string accessToken, CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "https://api.twitch.tv/helix/users");
        AddHelixHeaders(request, accessToken);

        using HttpResponseMessage response = await _http.SendAsync(request, ct);
        string body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new TwitchApiException((int)response.StatusCode, body, "Twitch user lookup failed: " + body);
        }

        HelixUsersResponse? users = JsonSerializer.Deserialize<HelixUsersResponse>(body, JsonOptions);
        if (users == null || users.Data.Count == 0)
        {
            throw new TwitchApiException(404, body, "Twitch returned no user for this token.");
        }

        return users.Data[0];
    }

    public async Task<EventSubSubscription> CreateEventSubSubscriptionAsync(
        string accessToken,
        string type,
        string version,
        Dictionary<string, string> condition,
        string sessionId,
        CancellationToken ct)
    {
        object payload = new
        {
            type,
            version,
            condition,
            transport = new
            {
                method = "websocket",
                session_id = sessionId
            }
        };

        using HttpRequestMessage request = new(HttpMethod.Post, "https://api.twitch.tv/helix/eventsub/subscriptions")
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        AddHelixHeaders(request, accessToken);

        using HttpResponseMessage response = await _http.SendAsync(request, ct);
        string body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new TwitchApiException((int)response.StatusCode, body, "EventSub subscribe failed: " + body);
        }

        EventSubCreateResponse? created = JsonSerializer.Deserialize<EventSubCreateResponse>(body, JsonOptions);
        if (created == null || created.Data.Count == 0)
        {
            throw new TwitchApiException(500, body, "Twitch returned no EventSub subscription.");
        }

        return created.Data[0];
    }

    public async Task<List<EventSubSubscription>> ListEventSubSubscriptionsAsync(string accessToken, CancellationToken ct)
    {
        List<EventSubSubscription> all = new();
        string? cursor = null;

        for (int page = 0; page < 20; page++)
        {
            string url = "https://api.twitch.tv/helix/eventsub/subscriptions?first=100";
            if (!string.IsNullOrEmpty(cursor))
            {
                url += "&after=" + Uri.EscapeDataString(cursor);
            }

            using HttpRequestMessage request = new(HttpMethod.Get, url);
            AddHelixHeaders(request, accessToken);

            using HttpResponseMessage response = await _http.SendAsync(request, ct);
            string body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new TwitchApiException((int)response.StatusCode, body, "EventSub list failed: " + body);
            }

            EventSubListResponse? result = JsonSerializer.Deserialize<EventSubListResponse>(body, JsonOptions);
            if (result == null)
            {
                break;
            }

            all.AddRange(result.Data);
            cursor = result.Pagination?.Cursor;
            if (string.IsNullOrEmpty(cursor) || result.Data.Count == 0)
            {
                break;
            }
        }

        return all;
    }

    public async Task DeleteEventSubSubscriptionAsync(string accessToken, string subscriptionId, CancellationToken ct)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Delete,
            "https://api.twitch.tv/helix/eventsub/subscriptions?id=" + Uri.EscapeDataString(subscriptionId));
        AddHelixHeaders(request, accessToken);

        using HttpResponseMessage response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            string body = await response.Content.ReadAsStringAsync(ct);
            _log.LogWarning("EventSub delete failed for {Id}: {Status} {Body}", subscriptionId, (int)response.StatusCode, body);
        }
    }

    public async Task RevokeAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        Dictionary<string, string> form = new()
        {
            ["client_id"] = _config.ClientId,
            ["token"] = token
        };

        using HttpRequestMessage request = new(HttpMethod.Post, "https://id.twitch.tv/oauth2/revoke")
        {
            Content = new FormUrlEncodedContent(form)
        };

        using HttpResponseMessage response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync(ct);
            _log.LogWarning("Twitch token revoke failed: {Status} {Body}", (int)response.StatusCode, body);
        }
    }

    private void AddHelixHeaders(HttpRequestMessage request, string accessToken)
    {
        request.Headers.TryAddWithoutValidation("Client-Id", _config.ClientId);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + accessToken);
    }

    private static string ReadOAuthError(string body)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out JsonElement message) &&
                message.ValueKind == JsonValueKind.String)
            {
                return message.GetString() ?? "unknown_error";
            }
        }
        catch
        {
            // Fall through to a trimmed body.
        }

        return string.IsNullOrWhiteSpace(body) ? "unknown_error" : body.Trim();
    }
}
