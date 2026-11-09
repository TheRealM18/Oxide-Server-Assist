using System.Collections.Concurrent;

namespace MediaRelay;

/// <summary>
/// Fixed-window rate limit for the public custom webhook. The key is the
/// non-secret link id, so token rotation starts a fresh window.
/// </summary>
public sealed class CustomWebhookLimiter
{
    private readonly int _limit;
    private readonly TimeSpan _window;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private int _calls;

    public CustomWebhookLimiter(int limit = 120, int windowSeconds = 60)
    {
        _limit = Math.Max(1, limit);
        _window = TimeSpan.FromSeconds(Math.Max(1, windowSeconds));
    }

    public bool TryAcquire(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Entry entry = _entries.GetOrAdd(key, _ => new Entry { WindowStart = now });

        lock (entry)
        {
            if (now - entry.WindowStart >= _window)
            {
                entry.WindowStart = now;
                entry.Count = 0;
            }

            entry.Count++;
            bool allowed = entry.Count <= _limit;

            if ((++_calls & 0x3FF) == 0)
            {
                Prune(now);
            }

            return allowed;
        }
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (KeyValuePair<string, Entry> pair in _entries)
        {
            if (now - pair.Value.WindowStart > TimeSpan.FromMinutes(10))
            {
                _entries.TryRemove(pair.Key, out _);
            }
        }
    }

    private sealed class Entry
    {
        public DateTimeOffset WindowStart { get; set; }
        public int Count { get; set; }
    }
}
