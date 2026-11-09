using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaRelay;

public static class Program
{
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        builder.Services.AddSingleton(_ => RelayConfig.Load(args));
        builder.Services.AddSingleton<EventStore>();
        builder.Services.AddSingleton<TwitchApi>();
        builder.Services.AddSingleton<TokenStore>();
        builder.Services.AddSingleton<CustomWebhookStore>();
        builder.Services.AddSingleton<CustomWebhookLimiter>();
        builder.Services.AddSingleton<EventSubService>();
        builder.Services.AddSingleton<LinkService>();

        WebApplication app = builder.Build();
        RelayConfig config = app.Services.GetRequiredService<RelayConfig>();
        ILogger log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MediaRelay");

        NonceCache nonces = new();
        NonceCache webhookNonces = new();

        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api"))
            {
                await next();
                return;
            }

            (ServerConfig? server, string error) = await ServerAuth.AuthenticateAsync(context, config, nonces);
            if (server == null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error });
                return;
            }

            context.Items["Server"] = server;
            await next();
        });

        app.MapGet("/health", () => Results.Ok(new
        {
            ok = true,
            service = "MediaRelay",
            version = "0.3.0",
            connected = app.Services.GetRequiredService<EventSubService>().Connected,
            subscriptions = app.Services.GetRequiredService<EventSubService>().SubscriptionCount,
            pendingEvents = app.Services.GetRequiredService<EventStore>().TotalCount()
        }));

        app.MapPost("/api/link/start", async (HttpContext context, LinkStartRequest request) =>
        {
            ServerConfig server = GetServer(context);
            LinkService links = context.RequestServices.GetRequiredService<LinkService>();

            string requestedPlatform = string.IsNullOrWhiteSpace(request.Platform)
                ? "twitch"
                : request.Platform.Trim().ToLowerInvariant();

            if (requestedPlatform == "twitch" && string.IsNullOrWhiteSpace(config.ClientId))
            {
                return Results.BadRequest(new { error = "Twitch ClientId is not configured on the relay." });
            }

            try
            {
                LinkStartResponse response = await links.StartAsync(server, request, context.RequestAborted);
                return Results.Ok(response);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (TwitchApiException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
        });

        app.MapGet("/api/link/status", (HttpContext context) =>
        {
            ServerConfig server = GetServer(context);
            string sessionId = context.Request.Query["sessionId"].ToString();
            LinkService links = context.RequestServices.GetRequiredService<LinkService>();
            return Results.Ok(links.GetStatus(server.Id, sessionId));
        });

        app.MapPost("/api/link/cancel", (HttpContext context) =>
        {
            ServerConfig server = GetServer(context);
            string sessionId = context.Request.Query["sessionId"].ToString();
            LinkService links = context.RequestServices.GetRequiredService<LinkService>();
            return Results.Ok(new { canceled = links.Cancel(server.Id, sessionId) });
        });

        app.MapGet("/api/links", (HttpContext context) =>
        {
            ServerConfig server = GetServer(context);
            LinkService links = context.RequestServices.GetRequiredService<LinkService>();
            return Results.Ok(links.GetLinks(server.Id));
        });

        app.MapPost("/api/unlink", async (HttpContext context, UnlinkRequest request) =>
        {
            ServerConfig server = GetServer(context);
            LinkService links = context.RequestServices.GetRequiredService<LinkService>();
            bool removed = await links.UnlinkAsync(server.Id, request, context.RequestAborted);
            return Results.Ok(new { removed });
        });

        app.MapGet("/api/events/pending", (HttpContext context) =>
        {
            ServerConfig server = GetServer(context);
            int limit = 100;
            if (int.TryParse(context.Request.Query["limit"].ToString(), out int parsed))
            {
                limit = parsed;
            }

            EventStore events = context.RequestServices.GetRequiredService<EventStore>();
            return Results.Ok(new { events = events.GetPending(server.Id, limit) });
        });

        app.MapPost("/api/events/ack", (HttpContext context, AckRequest request) =>
        {
            ServerConfig server = GetServer(context);
            EventStore events = context.RequestServices.GetRequiredService<EventStore>();
            int removed = events.Ack(server.Id, request.Ids);
            return Results.Ok(new { acknowledged = removed });
        });

        app.MapPost("/api/test-event", (HttpContext context, TestEventRequest request) =>
        {
            ServerConfig server = GetServer(context);
            EventStore events = context.RequestServices.GetRequiredService<EventStore>();

            string testType = string.IsNullOrWhiteSpace(request.Type) ? "bits" : request.Type.Trim().ToLowerInvariant();
            MediaEvent evt = new()
            {
                Id = "test-" + Guid.NewGuid().ToString("N"),
                Platform = string.IsNullOrWhiteSpace(request.Platform) ? "twitch" : request.Platform.Trim().ToLowerInvariant(),
                Type = testType,
                RawType = "relay.test",
                BroadcasterId = "test",
                BroadcasterLogin = "test",
                ViewerId = "test-viewer",
                ViewerLogin = "test_viewer",
                ViewerName = "Test Viewer",
                Amount = request.Amount,
                Unit = testType == "bits" ? "bits" : testType == "sub" || testType == "resub" ? "months" : testType == "raid" ? "viewers" : testType == "channel_points" ? "points" : string.Empty,
                RewardId = request.RewardId,
                RewardTitle = request.RewardTitle,
                SubTier = request.SubTier,
                IsTest = true,
                OccurredAt = DateTimeOffset.UtcNow,
                ReceivedAt = DateTimeOffset.UtcNow
            };
            evt.Extra["steamId"] = request.SteamId ?? string.Empty;

            events.Enqueue(server.Id, evt);
            return Results.Ok(new { queued = true, id = evt.Id });
        });

        app.MapPost("/api/reconcile", async (HttpContext context) =>
        {
            EventSubService eventSub = context.RequestServices.GetRequiredService<EventSubService>();
            await eventSub.ReconcileAsync(context.RequestAborted);
            return Results.Ok(new
            {
                connected = eventSub.Connected,
                subscriptions = eventSub.SubscriptionCount
            });
        });

        app.MapGet("/api/status", (HttpContext context) =>
        {
            EventSubService eventSub = context.RequestServices.GetRequiredService<EventSubService>();
            EventStore events = context.RequestServices.GetRequiredService<EventStore>();
            TokenStore tokens = context.RequestServices.GetRequiredService<TokenStore>();
            CustomWebhookStore custom = context.RequestServices.GetRequiredService<CustomWebhookStore>();
            ServerConfig server = GetServer(context);

            return Results.Ok(new
            {
                server = server.Id,
                connected = eventSub.Connected,
                session = eventSub.SessionId,
                subscriptions = eventSub.SubscriptionCount,
                pendingEvents = events.Count(server.Id),
                links = tokens.GetLinks(server.Id).Count + custom.GetLinks(server.Id).Count
            });
        });

        app.MapGet("/api/providers", (HttpContext context) =>
        {
            ServerConfig server = GetServer(context);
            TokenStore tokens = context.RequestServices.GetRequiredService<TokenStore>();
            CustomWebhookStore custom = context.RequestServices.GetRequiredService<CustomWebhookStore>();

            return Results.Ok(new
            {
                providers = new object[]
                {
                    new
                    {
                        id = "twitch",
                        displayName = "Twitch",
                        linkMode = "OAuthDeviceCode",
                        capabilities = new[] { "chat", "follow", "subscription", "gift_subscription", "donation", "reward", "raid", "momentum", "stream_status", "viewer_count" },
                        links = tokens.GetLinks(server.Id).Count
                    },
                    new
                    {
                        id = "custom",
                        displayName = "Custom Webhook",
                        linkMode = "WebhookToken",
                        capabilities = new[] { "donation", "subscription", "gift_subscription", "resub", "reward", "raid", "follow", "momentum", "chat", "custom" },
                        links = custom.GetLinks(server.Id).Count
                    }
                }
            });
        });

        app.MapPost("/webhook/custom", async (HttpContext context) =>
        {
            CustomWebhookStore custom = context.RequestServices.GetRequiredService<CustomWebhookStore>();
            CustomWebhookLimiter limiter = context.RequestServices.GetRequiredService<CustomWebhookLimiter>();
            EventStore events = context.RequestServices.GetRequiredService<EventStore>();

            string token = context.Request.Headers["X-Media-Token"].ToString();
            CustomWebhookLink? link = custom.Find(token);
            if (link == null)
            {
                return Results.NotFound(new { error = "Unknown custom webhook token." });
            }

            if (!limiter.TryAcquire(link.LinkId))
            {
                return Results.Json(new { error = "Too many requests. Slow down." }, statusCode: StatusCodes.Status429TooManyRequests);
            }

            string timestamp = context.Request.Headers["X-Media-Timestamp"].ToString();
            string nonce = context.Request.Headers["X-Media-Nonce"].ToString();
            string signature = context.Request.Headers["X-Media-Signature"].ToString();

            if (!long.TryParse(timestamp, out long unixSeconds) ||
                Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - unixSeconds) > 300)
            {
                return Results.Json(new { error = "Invalid or stale timestamp." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            if (nonce.Length < 8 || nonce.Length > 128)
            {
                return Results.Json(new { error = "Invalid nonce." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            const int maxBodyChars = 64 * 1024;
            if (context.Request.ContentLength > maxBodyChars)
            {
                return Results.Json(new { error = "Request body is too large." }, statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            context.Request.EnableBuffering();
            string body;
            using (StreamReader reader = new(context.Request.Body, System.Text.Encoding.UTF8, false, 4096, true))
            {
                char[] buffer = new char[maxBodyChars + 1];
                int read = 0;
                int chunk;
                while (read < buffer.Length &&
                       (chunk = await reader.ReadAsync(buffer, read, buffer.Length - read)) > 0)
                {
                    read += chunk;
                }

                if (read > maxBodyChars)
                {
                    return Results.Json(new { error = "Request body is too large." }, statusCode: StatusCodes.Status413PayloadTooLarge);
                }

                body = new string(buffer, 0, read);
            }
            context.Request.Body.Position = 0;

            string expected = Crypto.Hmac(token, timestamp + "\n" + nonce + "\n" + body);
            if (!Crypto.FixedTimeEquals(expected, signature))
            {
                return Results.Json(new { error = "Invalid signature." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            if (!webhookNonces.TryUse(nonce))
            {
                return Results.Json(new { error = "Replayed nonce." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            CustomWebhookRequest? request;
            try
            {
                request = System.Text.Json.JsonSerializer.Deserialize<CustomWebhookRequest>(
                    body,
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = "Invalid JSON: " + ex.Message });
            }

            if (request == null || string.IsNullOrWhiteSpace(request.Type))
            {
                return Results.BadRequest(new { error = "Event type is required." });
            }

            MediaEvent evt = new()
            {
                SchemaVersion = 1,
                Id = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString("N") : request.Id,
                Platform = "custom",
                Type = request.Type.Trim().ToLowerInvariant(),
                RawType = string.IsNullOrWhiteSpace(request.RawType) ? "custom.webhook" : request.RawType,
                BroadcasterId = link.LinkId,
                BroadcasterLogin = link.PlayerName,
                ViewerId = string.IsNullOrWhiteSpace(request.ViewerId) ? request.ViewerId : request.ViewerId,
                ViewerLogin = request.ViewerLogin,
                ViewerName = request.ViewerName,
                Amount = request.Amount,
                AmountMinor = request.AmountMinor,
                Unit = request.Unit,
                Currency = request.Currency,
                NormalizedUsd = request.NormalizedUsd,
                RewardId = request.RewardId,
                RewardTitle = request.RewardTitle,
                RedemptionId = request.RedemptionId,
                Status = request.Status,
                Tier = request.Tier,
                Months = request.Months,
                GiftCount = request.GiftCount,
                ViewerCount = request.ViewerCount,
                Message = request.Message,
                IsGift = request.IsGift,
                IsAnonymous = request.IsAnonymous,
                OccurredAt = request.OccurredAt == default ? DateTimeOffset.UtcNow : request.OccurredAt,
                ReceivedAt = DateTimeOffset.UtcNow,
                Extra = request.Extra ?? new Dictionary<string, string>()
            };

            events.Enqueue(link.ServerId, evt);
            return Results.Ok(new { queued = true, id = evt.Id });
        });

        EventSubService eventSubService = app.Services.GetRequiredService<EventSubService>();
        EventStore eventStore = app.Services.GetRequiredService<EventStore>();

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            eventSubService.Start();
            _ = Task.Run(() => ReconcileLoopAsync(eventSubService, config, app.Lifetime));
        });

        app.Lifetime.ApplicationStopping.Register(() =>
        {
            eventSubService.StopAsync().GetAwaiter().GetResult();
            eventStore.SaveAll();
        });

        Console.WriteLine("[MediaRelay] version 0.3.0");
        log.LogInformation("MediaRelay listening on {Listen}", config.Listen);
        log.LogInformation("EventSub endpoint: {EventSubUrl}", config.EventSubUrl);
        log.LogInformation("Configured server id(s): {Servers}", string.Join(", ", config.Servers.Select(server => server.Id)));

        app.Run(config.Listen);
    }

    private static ServerConfig GetServer(HttpContext context)
    {
        return (ServerConfig)context.Items["Server"]!;
    }

    private static async Task ReconcileLoopAsync(
        EventSubService eventSub,
        RelayConfig config,
        IHostApplicationLifetime lifetime)
    {
        CancellationToken token = lifetime.ApplicationStopping;
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(config.ReconcileMinutes));

        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                try
                {
                    await eventSub.ReconcileAsync(token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Reconcile logs its own errors.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
