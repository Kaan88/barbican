// Mirrors: lib/plugin-sandbox/host-api-input.ts:183-196; host-api-print.ts:22-27; host-api-file-viewer.ts:22-24.
namespace Barbican.Core.Plugins.Api.Preview;
public static class PluginPresentationRateLimiter
{
    public static IPluginPresentationRateLimiter Create(TimeProvider time, int maxKeys)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxKeys);
        return new Limiter(time, maxKeys);
    }
    private sealed class Limiter(TimeProvider time, int maxKeys) : IPluginPresentationRateLimiter
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<(string Profile, string Plugin, PluginPresentationOperation Operation), Queue<long>> _entries = [];
        public bool TryAcquire(string profile, string stablePluginId, PluginPresentationOperation operation)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(profile);
            ArgumentException.ThrowIfNullOrWhiteSpace(stablePluginId);
            if (!Enum.IsDefined(operation)) return false;
            lock (_gate)
            {
                var now = time.GetTimestamp();
                foreach (var pair in _entries.ToArray())
                {
                    while (pair.Value.TryPeek(out var stamp) && time.GetElapsedTime(stamp, now) >= TimeSpan.FromMinutes(1)) pair.Value.Dequeue();
                    if (pair.Value.Count == 0) _entries.Remove(pair.Key);
                }
                var key = (profile, stablePluginId, operation);
                if (!_entries.TryGetValue(key, out var queue))
                {
                    if (_entries.Count >= maxKeys) return false;
                    queue = new Queue<long>(); _entries.Add(key, queue);
                }
                var limit = operation == PluginPresentationOperation.OpenFile ? 5 : 3;
                if (queue.Count >= limit) return false;
                queue.Enqueue(now); return true;
            }
        }
    }
}
