using System.Text.Json.Serialization;

namespace MediaRelay;

#region Twitch OAuth / Helix

public sealed class DeviceCodeResponse
{
    [JsonPropertyName("device_code")]
    public string DeviceCode { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; } = 5;

    [JsonPropertyName("user_code")]
    public string UserCode { get; set; } = string.Empty;

    [JsonPropertyName("verification_uri")]
    public string VerificationUri { get; set; } = string.Empty;
}

public sealed class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("scope")]
    public List<string> Scope { get; set; } = new();

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;
}

public sealed class ValidateResponse
{
    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("login")]
    public string Login { get; set; } = string.Empty;

    [JsonPropertyName("scopes")]
    public List<string> Scopes { get; set; } = new();

    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

public sealed class HelixUser
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("login")]
    public string Login { get; set; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class HelixUsersResponse
{
    [JsonPropertyName("data")]
    public List<HelixUser> Data { get; set; } = new();
}

public sealed class EventSubSubscription
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("condition")]
    public Dictionary<string, string> Condition { get; set; } = new();

    [JsonPropertyName("transport")]
    public EventSubTransport Transport { get; set; } = new();
}

public sealed class EventSubTransport
{
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;
}

public sealed class EventSubCreateResponse
{
    [JsonPropertyName("data")]
    public List<EventSubSubscription> Data { get; set; } = new();
}

public sealed class EventSubListResponse
{
    [JsonPropertyName("data")]
    public List<EventSubSubscription> Data { get; set; } = new();

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("pagination")]
    public EventSubPagination? Pagination { get; set; }
}

public sealed class EventSubPagination
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; set; }
}

#endregion

#region Relay API

public sealed class LinkStartRequest
{
    public string Platform { get; set; } = "twitch";
    public string SteamId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
}

public sealed class LinkStartResponse
{
    public string Platform { get; set; } = "twitch";
    public string SessionId { get; set; } = string.Empty;
    public string UserCode { get; set; } = string.Empty;
    public string VerificationUri { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public int Interval { get; set; }
    public string Status { get; set; } = "pending";
    public string UserId { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class LinkStatusResponse
{
    public string Platform { get; set; } = "twitch";
    public string Status { get; set; } = "pending";
    public string UserId { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}

public sealed class UnlinkRequest
{
    public string Platform { get; set; } = "twitch";
    public string SteamId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
}

public sealed class AckRequest
{
    public List<string> Ids { get; set; } = new();
}

public sealed class LinkInfo
{
    public string Platform { get; set; } = "twitch";
    public string SteamId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTimeOffset LinkedAt { get; set; }
}

public sealed class CustomWebhookRequest
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string ViewerId { get; set; } = string.Empty;
    public string ViewerUserId { get; set; } = string.Empty;
    public string ViewerLogin { get; set; } = string.Empty;
    public string ViewerName { get; set; } = string.Empty;
    public long Amount { get; set; }
    public long AmountMinor { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public double NormalizedUsd { get; set; }
    public string Tier { get; set; } = string.Empty;
    public int Months { get; set; }
    public int GiftCount { get; set; }
    public string RewardId { get; set; } = string.Empty;
    public string RewardTitle { get; set; } = string.Empty;
    public string RedemptionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int ViewerCount { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool IsGift { get; set; }
    public bool IsAnonymous { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string RawType { get; set; } = "custom.webhook";
    public Dictionary<string, string> Extra { get; set; } = new();
}

public sealed class TestEventRequest
{
    public string Platform { get; set; } = "twitch";
    public string Type { get; set; } = "bits";
    public string SteamId { get; set; } = string.Empty;
    public long Amount { get; set; } = 100;
    public string RewardTitle { get; set; } = "Test Reward";
    public string RewardId { get; set; } = "test-reward";
    public string SubTier { get; set; } = "1000";
}

#endregion

#region Normalized reward event

public sealed class MediaEvent
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string RawType { get; set; } = string.Empty;
    public string BroadcasterId { get; set; } = string.Empty;
    public string BroadcasterLogin { get; set; } = string.Empty;
    public string ViewerId { get; set; } = string.Empty;
    public string ViewerLogin { get; set; } = string.Empty;
    public string ViewerName { get; set; } = string.Empty;
    public long Amount { get; set; }
    public long AmountMinor { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public double NormalizedUsd { get; set; }
    public string RewardId { get; set; } = string.Empty;
    public string RewardTitle { get; set; } = string.Empty;
    public string RedemptionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string SubTier { get; set; } = string.Empty;
    public string Tier { get; set; } = string.Empty;
    public int Months { get; set; }
    public int GiftCount { get; set; }
    public int ViewerCount { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool IsGift { get; set; }
    public bool IsAnonymous { get; set; }
    public bool IsTest { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public Dictionary<string, string> Extra { get; set; } = new();
}

#endregion

#region Token store

public sealed class LinkedServer
{
    public string ServerId { get; set; } = string.Empty;
    public string SteamId { get; set; } = string.Empty;
    public DateTimeOffset LinkedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TokenRecord
{
    public string Platform { get; set; } = "twitch";
    public string TwitchUserId { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public List<string> Scopes { get; set; } = new();
    public List<LinkedServer> Servers { get; set; } = new();
}

#endregion

#region Exceptions

public sealed class TwitchApiException : Exception
{
    public int StatusCode { get; }
    public string ResponseBody { get; }

    public TwitchApiException(int statusCode, string responseBody, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}

public sealed class TwitchOAuthException : Exception
{
    public string Error { get; }

    public TwitchOAuthException(string error, string message)
        : base(message)
    {
        Error = error;
    }
}

#endregion
