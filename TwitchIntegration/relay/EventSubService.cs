using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TwitchRelay;

/// <summary>
/// Maintains one Twitch EventSub WebSocket connection and fans normalized
/// events out to every game server linked to the broadcaster.
/// </summary>
public sealed class EventSubService
{
    // Twitch retired wss://eventsub.wttv.tv; the current host is configured
    // in RelayConfig.EventSubUrl and can be changed without a new build.

    private readonly RelayConfig _config;
    private readonly TokenStore _tokens;
    private readonly EventStore _events;
    private readonly TwitchApi _api;
    private readonly ILogger<EventSubService> _log;

    private readonly ConcurrentDictionary<string, bool> _linkedBroadcasters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _subscriptions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _scopeDenied = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _reconcileLock = new(1, 1);
    private readonly object _socketLock = new();

    private ClientWebSocket? _socket;
    private string? _sessionId;
    private string? _reconnectUrl;
    private bool _reconnectRequested;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private int _backoffSeconds = 1;

    public EventSubService(
        RelayConfig config,
        TokenStore tokens,
        EventStore events,
        TwitchApi api,
        ILogger<EventSubService> log)
    {
        _config = config;
        _tokens = tokens;
        _events = events;
        _api = api;
        _log = log;
    }

    public bool Connected => _socket?.State == WebSocketState.Open;
    public string? SessionId => _sessionId;
    public int SubscriptionCount => _subscriptions.Count;

    public void Start()
    {
        if (_loopTask != null)
        {
            return;
        }

        foreach (TokenRecord record in _tokens.All())
        {
            if (record.Servers.Count > 0)
            {
                _linkedBroadcasters[record.TwitchUserId] = true;
            }
        }

        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        CloseSocket();

        if (_loopTask != null)
        {
            try
            {
                await _loopTask;
            }
            catch
            {
                // Shutting down.
            }
        }
    }

    public Task EnsureBroadcasterAsync(string twitchUserId)
    {
        if (string.IsNullOrWhiteSpace(twitchUserId))
        {
            return Task.CompletedTask;
        }

        _linkedBroadcasters[twitchUserId] = true;

        if (string.IsNullOrEmpty(_sessionId))
        {
            return Task.CompletedTask;
        }

        return Task.Run(() => ReconcileAsync(CancellationToken.None));
    }

    public async Task RemoveBroadcasterAsync(string twitchUserId)
    {
        _linkedBroadcasters.TryRemove(twitchUserId, out _);

        TokenRecord? token = _tokens.GetByTwitchUserId(twitchUserId);
        if (token == null)
        {
            return;
        }

        await _reconcileLock.WaitAsync();
        try
        {
            string suffix = ":" + twitchUserId;
            foreach (KeyValuePair<string, string> pair in _subscriptions.ToList())
            {
                if (!pair.Key.EndsWith(suffix, StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    await _api.DeleteEventSubSubscriptionAsync(token.AccessToken, pair.Value, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Could not delete EventSub subscription {Id}.", pair.Value);
                }

                _subscriptions.TryRemove(pair.Key, out _);
            }

            foreach (string key in _scopeDenied.Keys.Where(key => key.EndsWith(suffix, StringComparison.Ordinal)).ToList())
            {
                _scopeDenied.TryRemove(key, out _);
            }
        }
        finally
        {
            _reconcileLock.Release();
        }
    }

    public async Task ReconcileAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_sessionId))
        {
            return;
        }

        await _reconcileLock.WaitAsync(ct);
        try
        {
            List<TokenRecord> tokens = _tokens.All().Where(token => token.Servers.Count > 0).ToList();
            if (tokens.Count == 0)
            {
                return;
            }

            await EnsureFreshTokenAsync(tokens[0], ct);
            string listToken = tokens[0].AccessToken;
            if (!string.IsNullOrEmpty(listToken))
            {
                try
                {
                    List<EventSubSubscription> existing =
                        await _api.ListEventSubSubscriptionsAsync(listToken, ct);

                    foreach (EventSubSubscription sub in existing)
                    {
                        if (sub.Transport == null || !string.Equals(sub.Transport.SessionId, _sessionId, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        string broadcaster = GetConditionBroadcaster(sub.Condition);
                        if (!string.IsNullOrEmpty(broadcaster))
                        {
                            _subscriptions[Key(sub.Type, broadcaster)] = sub.Id;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Could not list existing EventSub subscriptions.");
                }
            }

            foreach (TokenRecord token in tokens)
            {
                await EnsureFreshTokenAsync(token, ct);

                foreach (DesiredSubscription desired in BuildDesiredSubscriptions(token))
                {
                    string key = Key(desired.Type, token.TwitchUserId);
                    if (_subscriptions.ContainsKey(key) || _scopeDenied.ContainsKey(key))
                    {
                        continue;
                    }

                    if (!HasScope(token, desired.Scope))
                    {
                        _scopeDenied[key] = true;
                        _log.LogWarning(
                            "Skipping {Type} for {Login}: token is missing scope {Scope}. Re-link the account to grant it.",
                            desired.Type, token.Login, desired.Scope);
                        continue;
                    }

                    try
                    {
                        EventSubSubscription created = await _api.CreateEventSubSubscriptionAsync(
                            token.AccessToken,
                            desired.Type,
                            desired.Version,
                            desired.Condition,
                            _sessionId,
                            ct);

                        _subscriptions[key] = created.Id;
                        _log.LogInformation("Subscribed {Type} for {Login} ({Id}).", desired.Type, token.Login, created.Id);
                    }
                    catch (TwitchApiException ex) when (ex.StatusCode == 409)
                    {
                        _log.LogDebug("{Type} already exists for {Login}.", desired.Type, token.Login);
                    }
                    catch (TwitchApiException ex) when (ex.StatusCode >= 400 && ex.StatusCode < 500)
                    {
                        _scopeDenied[key] = true;
                        _log.LogWarning(
                            "Twitch refused {Type} for {Login} ({Status}). The token may need to be re-linked or the event may not be available for this channel. {Body}",
                            desired.Type, token.Login, ex.StatusCode, ex.ResponseBody);
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Could not subscribe {Type} for {Login}.", desired.Type, token.Login);
                    }
                }
            }
        }
        finally
        {
            _reconcileLock.Release();
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAndReceiveAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "EventSub connection dropped.");
            }

            if (ct.IsCancellationRequested)
            {
                break;
            }

            if (!string.IsNullOrEmpty(_reconnectUrl))
            {
                _backoffSeconds = 1;
                continue;
            }

            int delay = _backoffSeconds;
            _backoffSeconds = Math.Min(_backoffSeconds * 2, 60);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delay), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ConnectAndReceiveAsync(CancellationToken ct)
    {
        string url = string.IsNullOrEmpty(_reconnectUrl) ? _config.EventSubUrl : _reconnectUrl;
        _reconnectUrl = null;
        _sessionId = null;
        _reconnectRequested = false;
        _subscriptions.Clear();

        using ClientWebSocket socket = new();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

        lock (_socketLock)
        {
            _socket = socket;
        }

        await socket.ConnectAsync(new Uri(url), ct);
        _log.LogInformation("EventSub WebSocket connected to {Url}.", url);

        byte[] buffer = new byte[128 * 1024];
        using MemoryStream message = new();

        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (WebSocketException ex)
            {
                _log.LogWarning("EventSub receive failed: {Message}", ex.Message);
                break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
            {
                continue;
            }

            string json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            message.SetLength(0);

            await HandleMessageAsync(json, ct);

            if (_reconnectRequested)
            {
                _reconnectRequested = false;
                break;
            }
        }

        lock (_socketLock)
        {
            if (ReferenceEquals(_socket, socket))
            {
                _socket = null;
            }
        }

        if (socket.State == WebSocketState.Open)
        {
            try
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
            }
            catch
            {
                // Best effort.
            }
        }
    }

    private async Task HandleMessageAsync(string json, CancellationToken ct)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        if (!root.TryGetProperty("metadata", out JsonElement metadata))
        {
            return;
        }

        string messageType = Str(metadata, "message_type") ?? string.Empty;
        switch (messageType)
        {
            case "session_welcome":
                if (root.TryGetProperty("payload", out JsonElement welcomePayload) &&
                    welcomePayload.TryGetProperty("session", out JsonElement session))
                {
                    _sessionId = Str(session, "id");
                    _backoffSeconds = 1;
                    _log.LogInformation("EventSub session ready: {SessionId}", _sessionId);
                    await ReconcileAsync(ct);
                }
                break;

            case "session_keepalive":
                break;

            case "session_reconnect":
                if (root.TryGetProperty("payload", out JsonElement reconnectPayload) &&
                    reconnectPayload.TryGetProperty("session", out JsonElement reconnectSession))
                {
                    _reconnectUrl = Str(reconnectSession, "reconnect_url");
                    _reconnectRequested = true;
                    _log.LogInformation("EventSub requested a reconnect.");
                }
                break;

            case "notification":
                await HandleNotificationAsync(metadata, root, ct);
                break;

            case "revocation":
                HandleRevocation(root);
                break;
        }
    }

    private async Task HandleNotificationAsync(JsonElement metadata, JsonElement root, CancellationToken ct)
    {
        if (!root.TryGetProperty("payload", out JsonElement payload) ||
            !payload.TryGetProperty("event", out JsonElement eventElement))
        {
            return;
        }

        RewardEvent? rewardEvent = NormalizeEvent(metadata, eventElement);
        if (rewardEvent == null)
        {
            return;
        }

        List<string> servers = _tokens.GetServersForBroadcaster(rewardEvent.BroadcasterUserId);
        if (servers.Count == 0)
        {
            _log.LogDebug("No linked server for broadcaster {Broadcaster}.", rewardEvent.BroadcasterUserId);
            return;
        }

        foreach (string serverId in servers)
        {
            rewardEvent.ReceivedAt = DateTimeOffset.UtcNow;
            _events.Enqueue(serverId, Clone(rewardEvent));
        }

        _log.LogInformation(
            "Event {Type} from {Viewer} for broadcaster {Broadcaster} -> {Servers}.",
            rewardEvent.Type,
            rewardEvent.ViewerLogin,
            rewardEvent.BroadcasterLogin,
            string.Join(",", servers));
    }

    private void HandleRevocation(JsonElement root)
    {
        if (!root.TryGetProperty("payload", out JsonElement payload) ||
            !payload.TryGetProperty("subscription", out JsonElement subscription))
        {
            return;
        }

        string? id = Str(subscription, "id");
        string? type = Str(subscription, "type");

        foreach (KeyValuePair<string, string> pair in _subscriptions.ToList())
        {
            if (string.Equals(pair.Value, id, StringComparison.Ordinal))
            {
                _subscriptions.TryRemove(pair.Key, out _);
                _log.LogWarning("EventSub revoked {Type} ({Id}). It will be recreated on the next reconcile.", type, id);
                break;
            }
        }
    }

    private RewardEvent? NormalizeEvent(JsonElement metadata, JsonElement e)
    {
        string rawType = Str(metadata, "subscription_type") ?? string.Empty;
        string id = Str(metadata, "message_id") ?? Guid.NewGuid().ToString("N");

        RewardEvent evt = new()
        {
            Id = id,
            RawType = rawType,
            OccurredAt = DateTimeOffset.UtcNow,
            ReceivedAt = DateTimeOffset.UtcNow
        };

        switch (rawType)
        {
            case "channel.channel_points_custom_reward_redemption.add":
            case "channel.channel_points_custom_reward_redemption.update":
                evt.Type = rawType.EndsWith(".update", StringComparison.Ordinal)
                    ? "channel_points_update"
                    : "channel_points";
                evt.BroadcasterUserId = Str(e, "broadcaster_user_id") ?? string.Empty;
                evt.BroadcasterLogin = Str(e, "broadcaster_user_login") ?? string.Empty;
                evt.ViewerUserId = Str(e, "user_id") ?? string.Empty;
                evt.ViewerLogin = Str(e, "user_login") ?? string.Empty;
                evt.ViewerName = Str(e, "user_name") ?? string.Empty;
                evt.RedemptionId = Str(e, "id") ?? string.Empty;
                evt.Status = Str(e, "status") ?? string.Empty;
                evt.RewardId = NestedStr(e, "reward", "id") ?? string.Empty;
                evt.RewardTitle = NestedStr(e, "reward", "title") ?? string.Empty;
                evt.Amount = NestedNum(e, "reward", "cost");
                break;

            case "channel.channel_points_automatic_reward_redemption.add":
                evt.Type = "automatic_reward";
                evt.BroadcasterUserId = Str(e, "broadcaster_user_id") ?? string.Empty;
                evt.BroadcasterLogin = Str(e, "broadcaster_user_login") ?? string.Empty;
                evt.ViewerUserId = Str(e, "user_id") ?? string.Empty;
                evt.ViewerLogin = Str(e, "user_login") ?? string.Empty;
                evt.ViewerName = Str(e, "user_name") ?? string.Empty;
                evt.RedemptionId = Str(e, "id") ?? string.Empty;
                evt.Status = Str(e, "status") ?? string.Empty;
                evt.RewardId = NestedStr(e, "reward", "id") ?? string.Empty;
                evt.RewardTitle = NestedStr(e, "reward", "title") ?? string.Empty;
                evt.Amount = NestedNum(e, "reward", "cost");
                break;

            case "channel.cheer":
                evt.Type = "bits";
                evt.BroadcasterUserId = Str(e, "broadcaster_user_id") ?? string.Empty;
                evt.BroadcasterLogin = Str(e, "broadcaster_user_login") ?? string.Empty;
                evt.ViewerUserId = Str(e, "user_id") ?? string.Empty;
                evt.ViewerLogin = Str(e, "user_login") ?? string.Empty;
                evt.ViewerName = Str(e, "user_name") ?? string.Empty;
                evt.Amount = Num(e, "bits");
                evt.IsAnonymous = Bool(e, "is_anonymous");
                evt.Extra["message"] = Str(e, "message") ?? string.Empty;
                break;

            case "channel.subscribe":
                evt.Type = "sub";
                evt.BroadcasterUserId = Str(e, "broadcaster_user_id") ?? string.Empty;
                evt.BroadcasterLogin = Str(e, "broadcaster_user_login") ?? string.Empty;
                evt.ViewerUserId = Str(e, "user_id") ?? string.Empty;
                evt.ViewerLogin = Str(e, "user_login") ?? string.Empty;
                evt.ViewerName = Str(e, "user_name") ?? string.Empty;
                evt.SubTier = Str(e, "tier") ?? "1000";
                evt.IsGift = Bool(e, "is_gift");
                evt.Amount = 1;
                break;

            case "channel.subscription.gift":
                evt.Type = "gift_sub";
                evt.BroadcasterUserId = Str(e, "broadcaster_user_id") ?? string.Empty;
                evt.BroadcasterLogin = Str(e, "broadcaster_user_login") ?? string.Empty;
                evt.ViewerUserId = Str(e, "user_id") ?? string.Empty;
                evt.ViewerLogin = Str(e, "user_login") ?? string.Empty;
                evt.ViewerName = Str(e, "user_name") ?? string.Empty;
                evt.SubTier = Str(e, "tier") ?? "1000";
                evt.Amount = Num(e, "total");
                evt.IsGift = true;
                evt.IsAnonymous = Bool(e, "is_anonymous");
                evt.Extra["cumulative_total"] = Num(e, "cumulative_total").ToString();
                break;

            case "channel.subscription.message":
                evt.Type = "resub";
                evt.BroadcasterUserId = Str(e, "broadcaster_user_id") ?? string.Empty;
                evt.BroadcasterLogin = Str(e, "broadcaster_user_login") ?? string.Empty;
                evt.ViewerUserId = Str(e, "user_id") ?? string.Empty;
                evt.ViewerLogin = Str(e, "user_login") ?? string.Empty;
                evt.ViewerName = Str(e, "user_name") ?? string.Empty;
                evt.SubTier = Str(e, "tier") ?? "1000";
                evt.Amount = Num(e, "cumulative_months");
                evt.Extra["streak_months"] = Num(e, "streak_months").ToString();
                evt.Extra["message"] = NestedStr(e, "message", "text") ?? string.Empty;
                break;

            case "channel.raid":
                evt.Type = "raid";
                evt.BroadcasterUserId = Str(e, "to_broadcaster_user_id") ?? string.Empty;
                evt.BroadcasterLogin = Str(e, "to_broadcaster_user_login") ?? string.Empty;
                evt.ViewerUserId = Str(e, "from_broadcaster_user_id") ?? string.Empty;
                evt.ViewerLogin = Str(e, "from_broadcaster_user_login") ?? string.Empty;
                evt.ViewerName = Str(e, "from_broadcaster_user_name") ?? string.Empty;
                evt.Amount = Num(e, "viewers");
                break;

            case "channel.follow":
                evt.Type = "follow";
                evt.BroadcasterUserId = Str(e, "broadcaster_user_id") ?? string.Empty;
                evt.BroadcasterLogin = Str(e, "broadcaster_user_login") ?? string.Empty;
                evt.ViewerUserId = Str(e, "user_id") ?? string.Empty;
                evt.ViewerLogin = Str(e, "user_login") ?? string.Empty;
                evt.ViewerName = Str(e, "user_name") ?? string.Empty;
                break;

            case "channel.chat.message":
                evt.Type = "chat";
                evt.BroadcasterUserId = Str(e, "broadcaster_user_id") ?? string.Empty;
                evt.BroadcasterLogin = Str(e, "broadcaster_user_login") ?? string.Empty;
                evt.ViewerUserId = Str(e, "chatter_user_id") ?? string.Empty;
                evt.ViewerLogin = Str(e, "chatter_user_login") ?? string.Empty;
                evt.ViewerName = Str(e, "chatter_user_name") ?? string.Empty;
                evt.Extra["message"] = NestedStr(e, "message", "text") ?? string.Empty;
                break;

            case "channel.hype_train.begin":
            case "channel.hype_train.progress":
            case "channel.hype_train.end":
                evt.Type = "hype_train";
                evt.BroadcasterUserId = Str(e, "broadcaster_user_id") ?? string.Empty;
                evt.BroadcasterLogin = Str(e, "broadcaster_user_login") ?? string.Empty;
                evt.Amount = Num(e, "level");
                evt.Extra["phase"] = rawType.Substring(rawType.LastIndexOf('.') + 1);
                evt.Extra["total"] = Num(e, "total").ToString();
                evt.Extra["goal"] = Num(e, "goal").ToString();
                evt.Extra["is_golden_kappa_train"] = Bool(e, "is_golden_kappa_train").ToString();
                break;

            default:
                evt.Type = rawType;
                evt.BroadcasterUserId = Str(e, "broadcaster_user_id") ?? string.Empty;
                evt.BroadcasterLogin = Str(e, "broadcaster_user_login") ?? string.Empty;
                break;
        }

        if (string.IsNullOrEmpty(evt.BroadcasterUserId))
        {
            _log.LogWarning("Event {Type} had no broadcaster id and was ignored.", rawType);
            return null;
        }

        return evt;
    }

    private static RewardEvent Clone(RewardEvent source)
    {
        return new RewardEvent
        {
            Id = source.Id,
            Type = source.Type,
            RawType = source.RawType,
            BroadcasterUserId = source.BroadcasterUserId,
            BroadcasterLogin = source.BroadcasterLogin,
            ViewerUserId = source.ViewerUserId,
            ViewerLogin = source.ViewerLogin,
            ViewerName = source.ViewerName,
            Amount = source.Amount,
            RewardId = source.RewardId,
            RewardTitle = source.RewardTitle,
            RedemptionId = source.RedemptionId,
            Status = source.Status,
            SubTier = source.SubTier,
            IsGift = source.IsGift,
            IsAnonymous = source.IsAnonymous,
            OccurredAt = source.OccurredAt,
            ReceivedAt = source.ReceivedAt,
            Extra = new Dictionary<string, string>(source.Extra)
        };
    }

    private async Task EnsureFreshTokenAsync(TokenRecord token, CancellationToken ct)
    {
        if (token.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2) || string.IsNullOrEmpty(token.RefreshToken))
        {
            return;
        }

        try
        {
            TokenResponse refreshed = await _api.RefreshAsync(token.RefreshToken, ct);
            token.AccessToken = refreshed.AccessToken;
            if (!string.IsNullOrEmpty(refreshed.RefreshToken))
            {
                token.RefreshToken = refreshed.RefreshToken;
            }

            token.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, refreshed.ExpiresIn));
            if (refreshed.Scope.Count > 0)
            {
                token.Scopes = refreshed.Scope;
            }

            _tokens.Save();
            _log.LogInformation("Refreshed Twitch token for {Login}.", token.Login);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not refresh Twitch token for {Login}; re-link may be required.", token.Login);
        }
    }

    private bool HasScope(TokenRecord token, string scope)
    {
        if (string.IsNullOrEmpty(scope) || token.Scopes == null || token.Scopes.Count == 0)
        {
            return true;
        }

        return token.Scopes.Contains(scope, StringComparer.Ordinal);
    }

    private List<DesiredSubscription> BuildDesiredSubscriptions(TokenRecord token)
    {
        List<DesiredSubscription> list = new()
        {
            Make("channel.channel_points_custom_reward_redemption.add", "1", token, "channel:read:redemptions"),
            Make("channel.channel_points_custom_reward_redemption.update", "1", token, "channel:read:redemptions"),
            Make("channel.channel_points_automatic_reward_redemption.add", "1", token, "channel:read:redemptions"),
            Make("channel.cheer", "1", token, "bits:read"),
            Make("channel.subscribe", "1", token, "channel:read:subscriptions"),
            Make("channel.subscription.gift", "1", token, "channel:read:subscriptions"),
            Make("channel.subscription.message", "1", token, "channel:read:subscriptions"),
            MakeRaid(token)
        };

        if (_config.EnableFollows)
        {
            list.Add(new DesiredSubscription
            {
                Type = "channel.follow",
                Version = "2",
                Scope = "moderator:read:followers",
                Condition = new Dictionary<string, string>
                {
                    ["broadcaster_user_id"] = token.TwitchUserId,
                    ["moderator_user_id"] = token.TwitchUserId
                }
            });
        }

        if (_config.EnableHypeTrain)
        {
            list.Add(Make("channel.hype_train.begin", "1", token, "channel:read:hype_train"));
            list.Add(Make("channel.hype_train.progress", "1", token, "channel:read:hype_train"));
            list.Add(Make("channel.hype_train.end", "1", token, "channel:read:hype_train"));
        }

        if (_config.EnableChat)
        {
            list.Add(new DesiredSubscription
            {
                Type = "channel.chat.message",
                Version = "1",
                Scope = "user:read:chat",
                Condition = new Dictionary<string, string>
                {
                    ["broadcaster_user_id"] = token.TwitchUserId,
                    ["user_id"] = token.TwitchUserId
                }
            });
        }

        return list;
    }

    private static DesiredSubscription Make(string type, string version, TokenRecord token, string scope)
    {
        return new DesiredSubscription
        {
            Type = type,
            Version = version,
            Scope = scope,
            Condition = new Dictionary<string, string>
            {
                ["broadcaster_user_id"] = token.TwitchUserId
            }
        };
    }

    private static DesiredSubscription MakeRaid(TokenRecord token)
    {
        return new DesiredSubscription
        {
            Type = "channel.raid",
            Version = "1",
            Scope = string.Empty,
            Condition = new Dictionary<string, string>
            {
                ["to_broadcaster_user_id"] = token.TwitchUserId
            }
        };
    }

    private static string Key(string type, string broadcaster)
    {
        return type + ":" + broadcaster;
    }

    private static string GetConditionBroadcaster(Dictionary<string, string> condition)
    {
        if (condition == null)
        {
            return string.Empty;
        }

        foreach (string name in new[] { "broadcaster_user_id", "to_broadcaster_user_id", "from_broadcaster_user_id" })
        {
            if (condition.TryGetValue(name, out string? value) && !string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string? Str(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static long Num(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.TryGetInt64(out long number)
            ? number
            : 0;
    }

    private static bool Bool(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
    }

    private static string? NestedStr(JsonElement element, string parent, string child)
    {
        return element.TryGetProperty(parent, out JsonElement nested) ? Str(nested, child) : null;
    }

    private static long NestedNum(JsonElement element, string parent, string child)
    {
        return element.TryGetProperty(parent, out JsonElement nested) ? Num(nested, child) : 0;
    }

    private void CloseSocket()
    {
        lock (_socketLock)
        {
            try
            {
                _socket?.Abort();
            }
            catch
            {
                // Best effort.
            }
        }
    }

    private sealed class DesiredSubscription
    {
        public string Type { get; set; } = string.Empty;
        public string Version { get; set; } = "1";
        public string Scope { get; set; } = string.Empty;
        public Dictionary<string, string> Condition { get; set; } = new();
    }
}
