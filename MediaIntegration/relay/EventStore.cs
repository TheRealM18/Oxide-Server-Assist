using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace MediaRelay;

/// <summary>
/// Durable per-server event queue. Events are kept until the game plugin
/// acknowledges them, so a game-server restart does not lose a redemption.
/// </summary>
public sealed class EventStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly RelayConfig _config;
    private readonly ILogger<EventStore> _log;
    private readonly string _dir;
    private readonly object _sync = new();
    private readonly Dictionary<string, ServerQueue> _queues = new(StringComparer.OrdinalIgnoreCase);

    public EventStore(RelayConfig config, ILogger<EventStore> log)
    {
        _config = config;
        _log = log;
        _dir = Path.Combine(config.ResolvedDataDir, "events");
        Directory.CreateDirectory(_dir);
        UnixPermissions.RestrictDirectory(_dir);
        LoadAll();
    }

    public bool Enqueue(string serverId, MediaEvent mediaEvent)
    {
        if (string.IsNullOrWhiteSpace(serverId) || mediaEvent == null || string.IsNullOrWhiteSpace(mediaEvent.Id))
        {
            return false;
        }

        lock (_sync)
        {
            ServerQueue queue = GetOrCreateQueue(serverId);
            string key = mediaEvent.Platform + ":" + mediaEvent.Id;
            if (queue.Seen.Contains(key))
            {
                return false;
            }

            queue.Seen.Add(key);
            queue.SeenOrder.Enqueue(key);

            while (queue.SeenOrder.Count > 2000)
            {
                string oldest = queue.SeenOrder.Dequeue();
                queue.Seen.Remove(oldest);
            }

            queue.Events.Add(mediaEvent);
            PruneQueue(queue);
            SaveLocked(serverId, queue);
            _log.LogInformation("Queued {Type} event {Id} for server {ServerId}.", mediaEvent.Type, mediaEvent.Id, serverId);
            return true;
        }
    }

    public List<MediaEvent> GetPending(string serverId, int limit)
    {
        if (string.IsNullOrWhiteSpace(serverId))
        {
            return new List<MediaEvent>();
        }

        lock (_sync)
        {
            ServerQueue queue = GetOrCreateQueue(serverId);
            PruneQueue(queue);
            return queue.Events.Take(Math.Clamp(limit, 1, 500)).ToList();
        }
    }

    public int Ack(string serverId, IEnumerable<string> ids)
    {
        if (string.IsNullOrWhiteSpace(serverId) || ids == null)
        {
            return 0;
        }

        HashSet<string> set = new(ids.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.Ordinal);
        if (set.Count == 0)
        {
            return 0;
        }

        lock (_sync)
        {
            ServerQueue queue = GetOrCreateQueue(serverId);
            int removed = queue.Events.RemoveAll(evt =>
            {
                string platform = string.IsNullOrEmpty(evt.Platform) ? "twitch" : evt.Platform;
                return set.Contains(platform + ":" + evt.Id) || set.Contains(evt.Id);
            });
            if (removed > 0)
            {
                SaveLocked(serverId, queue);
            }

            return removed;
        }
    }

    public int Count(string serverId)
    {
        lock (_sync)
        {
            return GetOrCreateQueue(serverId).Events.Count;
        }
    }

    public int TotalCount()
    {
        lock (_sync)
        {
            return _queues.Values.Sum(queue => queue.Events.Count);
        }
    }

    public void SaveAll()
    {
        lock (_sync)
        {
            foreach (KeyValuePair<string, ServerQueue> pair in _queues)
            {
                SaveLocked(pair.Key, pair.Value);
            }
        }
    }

    private ServerQueue GetOrCreateQueue(string serverId)
    {
        if (!_queues.TryGetValue(serverId, out ServerQueue? queue))
        {
            queue = new ServerQueue();
            _queues[serverId] = queue;
            LoadQueue(serverId, queue);
        }

        return queue;
    }

    private void PruneQueue(ServerQueue queue)
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddHours(-_config.EventRetentionHours);
        queue.Events.RemoveAll(evt => evt.ReceivedAt < cutoff);

        if (queue.Events.Count > _config.MaxEventsPerServer)
        {
            int remove = queue.Events.Count - _config.MaxEventsPerServer;
            queue.Events.RemoveRange(0, remove);
        }
    }

    private void LoadAll()
    {
        foreach (string path in Directory.EnumerateFiles(_dir, "*.json"))
        {
            string serverId = Path.GetFileNameWithoutExtension(path);
            GetOrCreateQueue(serverId);
        }
    }

    private void LoadQueue(string serverId, ServerQueue queue)
    {
        string path = QueuePath(serverId);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            StoredQueue? stored = JsonSerializer.Deserialize<StoredQueue>(File.ReadAllText(path), JsonOptions);
            if (stored == null)
            {
                return;
            }

            queue.Events = stored.Events ?? new List<MediaEvent>();
            queue.Seen = new HashSet<string>(stored.Seen ?? new List<string>(), StringComparer.Ordinal);
            queue.SeenOrder = new Queue<string>(queue.Seen);

            foreach (MediaEvent evt in queue.Events)
            {
                if (string.IsNullOrEmpty(evt.Platform))
                {
                    // Queues written before the rename had no platform field.
                    evt.Platform = "twitch";
                }

                queue.Seen.Add(evt.Platform + ":" + evt.Id);
            }

            PruneQueue(queue);
            _log.LogInformation("Loaded {Count} pending event(s) for server {ServerId}.", queue.Events.Count, serverId);
        }
        catch (Exception ex)
        {
            _log.LogWarning("Could not load event queue for {ServerId}: {Message}", serverId, ex.Message);
        }
    }

    private void SaveLocked(string serverId, ServerQueue queue)
    {
        try
        {
            StoredQueue stored = new()
            {
                Events = queue.Events,
                Seen = queue.SeenOrder.ToList()
            };

            string path = QueuePath(serverId);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(stored, JsonOptions));
            UnixPermissions.RestrictFile(tmp);
            File.Move(tmp, path, true);
            UnixPermissions.RestrictFile(path);
        }
        catch (Exception ex)
        {
            _log.LogWarning("Could not save event queue for {ServerId}: {Message}", serverId, ex.Message);
        }
    }

    private string QueuePath(string serverId)
    {
        string safe = Regex.Replace(serverId, "[^A-Za-z0-9_.-]", "_");
        if (string.IsNullOrWhiteSpace(safe))
        {
            safe = "server";
        }

        return Path.Combine(_dir, safe + ".json");
    }

    private sealed class ServerQueue
    {
        public List<MediaEvent> Events { get; set; } = new();
        public HashSet<string> Seen { get; set; } = new(StringComparer.Ordinal);
        public Queue<string> SeenOrder { get; set; } = new();
    }

    private sealed class StoredQueue
    {
        public List<MediaEvent>? Events { get; set; }
        public List<string>? Seen { get; set; }
    }
}
