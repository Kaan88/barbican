// Mirrors: lib/plugin-sandbox/host-api-composer.ts:36-42 and host-api-input.ts:183-196.
namespace Barbican.Core.Plugins.Api.Composer;
/// <summary>Creates one bounded, host-shared composer edit rate limiter.</summary>
public static class PluginComposerEditRateLimiter
{
    public static IPluginComposerEditRateLimiter Create(TimeProvider time, int maxKeys)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxKeys);
        return new WindowLimiter(time, maxKeys);
    }
    private sealed class WindowLimiter(TimeProvider time, int maxKeys) : IPluginComposerEditRateLimiter
    {
        private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
        private readonly Lock _gate = new();
        private readonly Dictionary<(string Profile, string Id), Queue<long>> _entries = [];
        public bool TryAcquire(string profile, string stablePluginId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(profile);
            ArgumentException.ThrowIfNullOrWhiteSpace(stablePluginId);
            lock (_gate)
            {
                var now = time.GetTimestamp();
                foreach (var item in _entries.ToArray())
                {
                    while (item.Value.TryPeek(out var stamp) && time.GetElapsedTime(stamp, now) >= Window) item.Value.Dequeue();
                    if (item.Value.Count == 0) _entries.Remove(item.Key);
                }
                var key = (profile, stablePluginId);
                if (!_entries.TryGetValue(key, out var queue))
                {
                    if (_entries.Count >= maxKeys) return false;
                    queue = new Queue<long>(); _entries.Add(key, queue);
                }
                if (queue.Count >= 60) return false;
                queue.Enqueue(now); return true;
            }
        }
    }
}
