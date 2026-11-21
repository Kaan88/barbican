// Mirrors: Bulwark lib/plugin-sandbox/host-api-input.ts:183-196 withinRate; bounded key count so a plugin cannot grow host memory.
namespace Barbican.Core.Plugins.Api.Mail;

/// <summary>Sliding one-minute window per (kind, profile, plugin).</summary>
public sealed class PluginMailRateLimiter : IPluginMailRateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Kind, string Profile, string Id), Queue<long>> _entries = [];
    private readonly TimeProvider _time;
    private readonly int _maxKeys;

    public PluginMailRateLimiter(TimeProvider time, int maxKeys = 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxKeys);
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _maxKeys = maxKeys;
    }

    public bool TryAcquire(string kind, string profile, string stablePluginId, int maxPerMinute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(stablePluginId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPerMinute);
        lock (_gate)
        {
            var now = _time.GetTimestamp();
            foreach (var item in _entries.ToArray())
            {
                while (item.Value.TryPeek(out var stamp) && _time.GetElapsedTime(stamp, now) >= Window) item.Value.Dequeue();
                if (item.Value.Count == 0) _entries.Remove(item.Key);
            }
            var key = (kind, profile, stablePluginId);
            if (!_entries.TryGetValue(key, out var queue))
            {
                if (_entries.Count >= _maxKeys) return false;
                queue = new Queue<long>();
                _entries.Add(key, queue);
            }
            if (queue.Count >= maxPerMinute) return false;
            queue.Enqueue(now);
            return true;
        }
    }
}
