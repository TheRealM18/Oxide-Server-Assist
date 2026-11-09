using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MediaRelay;

/// <summary>
/// Handles the Twitch device-code flow and binds a Twitch user to a game
/// character. The game server never sees a Twitch password or stream key.
/// </summary>
public sealed class LinkService
{
    private readonly RelayConfig _config;
    private readonly TwitchApi _api;
    private readonly TokenStore _tokens;
    private readonly CustomWebhookStore _custom;
    private readonly EventSubService _eventSub;
    private readonly ILogger<LinkService> _log;
    private readonly ConcurrentDictionary<string, LinkSession> _sessions = new(StringComparer.Ordinal);

    public LinkService(
        RelayConfig config,
        TwitchApi api,
        TokenStore tokens,
        CustomWebhookStore custom,
        EventSubService eventSub,
        ILogger<LinkService> log)
    {
        _config = config;
        _api = api;
        _tokens = tokens;
        _custom = custom;
        _eventSub = eventSub;
        _log = log;
    }

    public async Task<LinkStartResponse> StartAsync(
        ServerConfig server,
        LinkStartRequest request,
        CancellationToken ct)
    {
        string steamId = (request.SteamId ?? string.Empty).Trim();
        string playerName = (request.PlayerName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(steamId))
        {
            throw new ArgumentException("SteamId is required.");
        }

        if (steamId.Length > 64)
        {
            steamId = steamId.Substring(0, 64);
        }

        if (playerName.Length > 64)
        {
            playerName = playerName.Substring(0, 64);
        }

        string platform = string.IsNullOrWhiteSpace(request.Platform)
            ? "twitch"
            : request.Platform.Trim().ToLowerInvariant();

        if (platform == "custom")
        {
            return StartCustomWebhook(server, steamId, playerName);
        }

        if (platform != "twitch")
        {
            throw new ArgumentException("Provider '" + platform + "' is not implemented in this build.");
        }

        DeviceCodeResponse device = await _api.StartDeviceCodeAsync(ct);
        string sessionId = Guid.NewGuid().ToString("N");

        LinkSession session = new()
        {
            SessionId = sessionId,
            Platform = platform,
            ServerId = server.Id,
            SteamId = steamId,
            PlayerName = playerName,
            DeviceCode = device.DeviceCode,
            UserCode = device.UserCode,
            VerificationUri = device.VerificationUri,
            Interval = Math.Max(1, device.Interval),
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, device.ExpiresIn)),
            Status = "pending",
            Cancellation = new CancellationTokenSource()
        };

        _sessions[sessionId] = session;
        _ = Task.Run(() => PollAsync(session));

        return new LinkStartResponse
        {
            Platform = platform,
            SessionId = sessionId,
            UserCode = session.UserCode,
            VerificationUri = session.VerificationUri,
            ExpiresIn = Math.Max(60, device.ExpiresIn),
            Interval = session.Interval,
            Status = "pending"
        };
    }

    private LinkStartResponse StartCustomWebhook(ServerConfig server, string steamId, string playerName)
    {
        CustomWebhookLink link = _custom.Create(server.Id, steamId, playerName);
        string webhook = _config.PublicBaseUrl.TrimEnd('/') + "/webhook/custom";

        return new LinkStartResponse
        {
            Platform = "custom",
            SessionId = Guid.NewGuid().ToString("N"),
            UserCode = link.Token,
            VerificationUri = webhook,
            ExpiresIn = 0,
            Interval = 0,
            Status = "linked",
            UserId = link.LinkId,
            Login = "custom",
            DisplayName = "Custom Webhook"
        };
    }

    public LinkStatusResponse GetStatus(string serverId, string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId) ||
            !_sessions.TryGetValue(sessionId, out LinkSession? session) ||
            !string.Equals(session.ServerId, serverId, StringComparison.OrdinalIgnoreCase))
        {
            return new LinkStatusResponse { Status = "unknown", Error = "Link session was not found or has expired." };
        }

        return new LinkStatusResponse
        {
            Platform = session.Platform,
            Status = session.Status,
            UserId = session.UserId,
            Login = session.Login,
            DisplayName = session.DisplayName,
            Error = session.Error
        };
    }

    public bool Cancel(string serverId, string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out LinkSession? session) ||
            !string.Equals(session.ServerId, serverId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        session.Cancellation.Cancel();
        session.Status = "canceled";
        return true;
    }

    public List<LinkInfo> GetLinks(string serverId)
    {
        List<LinkInfo> links = _tokens.GetLinks(serverId);
        links.AddRange(_custom.GetLinks(serverId));
        return links
            .OrderBy(link => link.Platform, StringComparer.OrdinalIgnoreCase)
            .ThenBy(link => link.Login, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<bool> UnlinkAsync(string serverId, UnlinkRequest request, CancellationToken ct)
    {
        string platform = string.IsNullOrWhiteSpace(request.Platform)
            ? "twitch"
            : request.Platform.Trim().ToLowerInvariant();

        if (platform == "custom")
        {
            if (string.IsNullOrWhiteSpace(request.SteamId))
            {
                return false;
            }

            return _custom.RemoveByServerSteam(serverId, request.SteamId.Trim());
        }

        TokenRecord? record = null;

        if (!string.IsNullOrWhiteSpace(request.UserId))
        {
            record = _tokens.GetByTwitchUserId(request.UserId.Trim());
        }
        else if (!string.IsNullOrWhiteSpace(request.SteamId))
        {
            record = _tokens.FindBySteamId(serverId, request.SteamId.Trim());
        }

        if (record == null)
        {
            return false;
        }

        int removed = record.Servers.RemoveAll(link =>
            string.Equals(link.ServerId, serverId, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(request.SteamId) ||
             string.Equals(link.SteamId, request.SteamId.Trim(), StringComparison.Ordinal)));

        if (removed == 0)
        {
            return false;
        }

        if (record.Servers.Count == 0)
        {
            await _api.RevokeAsync(record.AccessToken, ct);
            _tokens.Remove(record.TwitchUserId);
            await _eventSub.RemoveBroadcasterAsync(record.TwitchUserId);
        }
        else
        {
            _tokens.Save();
        }

        _log.LogInformation("Unlinked Twitch {Login} from server {ServerId}.", record.Login, serverId);
        return true;
    }

    private async Task PollAsync(LinkSession session)
    {
        try
        {
            while (!session.Cancellation.IsCancellationRequested && DateTimeOffset.UtcNow < session.ExpiresAt)
            {
                await Task.Delay(TimeSpan.FromSeconds(session.Interval), session.Cancellation.Token);

                if (session.Cancellation.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    TokenResponse token = await _api.PollDeviceTokenAsync(session.DeviceCode, session.Cancellation.Token);
                    await CompleteLinkAsync(session, token);
                    return;
                }
                catch (TwitchOAuthException ex) when (ex.Error == "authorization_pending")
                {
                    // The streamer has not approved the code yet.
                }
                catch (TwitchOAuthException ex) when (ex.Error == "slow_down")
                {
                    session.Interval += 5;
                }
                catch (TwitchOAuthException ex)
                {
                    session.Status = "failed";
                    session.Error = ex.Error;
                    _log.LogWarning("Twitch link failed for {SteamId}: {Error}", session.SteamId, ex.Error);
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    session.Status = "failed";
                    session.Error = ex.Message;
                    _log.LogWarning(ex, "Twitch link failed for {SteamId}.", session.SteamId);
                    return;
                }
            }

            if (session.Status == "pending")
            {
                session.Status = "expired";
                session.Error = "The Twitch link code expired. Run /twitch link again.";
            }
        }
        finally
        {
            session.Cancellation.Dispose();
        }
    }

    private async Task CompleteLinkAsync(LinkSession session, TokenResponse token)
    {
        ValidateResponse validate = await _api.ValidateAsync(token.AccessToken, session.Cancellation.Token);
        HelixUser user = await _api.GetUserAsync(token.AccessToken, session.Cancellation.Token);

        if (!string.Equals(validate.UserId, user.Id, StringComparison.Ordinal))
        {
            session.Status = "failed";
            session.Error = "Twitch returned inconsistent account data.";
            return;
        }

        TokenRecord record = _tokens.GetOrCreate(user.Id);
        record.Platform = "twitch";
        record.Login = user.Login;
        record.DisplayName = string.IsNullOrEmpty(user.DisplayName) ? user.Login : user.DisplayName;
        record.AccessToken = token.AccessToken;
        if (!string.IsNullOrEmpty(token.RefreshToken))
        {
            record.RefreshToken = token.RefreshToken;
        }

        record.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn));
        record.Scopes = token.Scope.Count > 0 ? token.Scope : validate.Scopes;

        // One Twitch account maps to one character per server.
        record.Servers.RemoveAll(link =>
            string.Equals(link.ServerId, session.ServerId, StringComparison.OrdinalIgnoreCase));

        // One character maps to one Twitch account per server.
        foreach (TokenRecord other in _tokens.All())
        {
            if (other.TwitchUserId == record.TwitchUserId)
            {
                continue;
            }

            int removed = other.Servers.RemoveAll(link =>
                string.Equals(link.ServerId, session.ServerId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(link.SteamId, session.SteamId, StringComparison.Ordinal));

            if (removed > 0 && other.Servers.Count == 0)
            {
                await _api.RevokeAsync(other.AccessToken, session.Cancellation.Token);
                _tokens.Remove(other.TwitchUserId);
            }
        }

        record.Servers.Add(new LinkedServer
        {
            ServerId = session.ServerId,
            SteamId = session.SteamId,
            LinkedAt = DateTimeOffset.UtcNow
        });

        _tokens.Save();
        await _eventSub.EnsureBroadcasterAsync(user.Id);

        session.UserId = user.Id;
        session.Login = user.Login;
        session.DisplayName = record.DisplayName;
        session.Status = "linked";
        session.Error = string.Empty;

        _log.LogInformation(
            "Linked Twitch {Login} to {Player} ({SteamId}) on {ServerId}.",
            user.Login,
            session.PlayerName,
            session.SteamId,
            session.ServerId);
    }

    private sealed class LinkSession
    {
        public string SessionId { get; set; } = string.Empty;
        public string Platform { get; set; } = "twitch";
        public string ServerId { get; set; } = string.Empty;
        public string SteamId { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public string DeviceCode { get; set; } = string.Empty;
        public string UserCode { get; set; } = string.Empty;
        public string VerificationUri { get; set; } = string.Empty;
        public int Interval { get; set; } = 5;
        public DateTimeOffset ExpiresAt { get; set; }
        public string Status { get; set; } = "pending";
        public string Error { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Login { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public CancellationTokenSource Cancellation { get; set; } = new();
    }
}
