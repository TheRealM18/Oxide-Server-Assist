using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TwitchRelay;

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
        builder.Services.AddSingleton<EventSubService>();
        builder.Services.AddSingleton<LinkService>();

        WebApplication app = builder.Build();
        RelayConfig config = app.Services.GetRequiredService<RelayConfig>();
        ILogger log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("TwitchRelay");

        NonceCache nonces = new();

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
            service = "TwitchRelay",
            version = "0.1.1",
            connected = app.Services.GetRequiredService<EventSubService>().Connected,
            subscriptions = app.Services.GetRequiredService<EventSubService>().SubscriptionCount,
            pendingEvents = app.Services.GetRequiredService<EventStore>().TotalCount()
        }));

        app.MapPost("/api/link/start", async (HttpContext context, LinkStartRequest request) =>
        {
            ServerConfig server = GetServer(context);
            LinkService links = context.RequestServices.GetRequiredService<LinkService>();

            if (string.IsNullOrWhiteSpace(config.ClientId))
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

            RewardEvent evt = new()
            {
                Id = "test-" + Guid.NewGuid().ToString("N"),
                Type = string.IsNullOrWhiteSpace(request.Type) ? "bits" : request.Type.Trim().ToLowerInvariant(),
                RawType = "relay.test",
                BroadcasterUserId = "test",
                BroadcasterLogin = "test",
                ViewerUserId = "test-viewer",
                ViewerLogin = "test_viewer",
                ViewerName = "Test Viewer",
                Amount = request.Amount,
                RewardId = request.RewardId,
                RewardTitle = request.RewardTitle,
                SubTier = request.SubTier,
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
            ServerConfig server = GetServer(context);

            return Results.Ok(new
            {
                server = server.Id,
                connected = eventSub.Connected,
                session = eventSub.SessionId,
                subscriptions = eventSub.SubscriptionCount,
                pendingEvents = events.Count(server.Id),
                links = tokens.GetLinks(server.Id).Count
            });
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

        Console.WriteLine("[TwitchRelay] version 0.1.1");
        log.LogInformation("TwitchRelay listening on {Listen}", config.Listen);
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
