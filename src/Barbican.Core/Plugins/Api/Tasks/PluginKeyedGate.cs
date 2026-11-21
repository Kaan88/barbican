// Mirrors: Bulwark lib/plugin-sandbox/host-api-calendar.ts:923-937 oneAtATime: a plugin's writes to one task run one after another (Stalwart refuses a CalendarEvent/set while another is under way on the same
// object), and each is worked out against the task as the one before left it.
namespace Barbican.Core.Plugins.Api.Tasks;

/// <summary>Runs work per key, one at a time, in the order it arrived.</summary>
internal sealed class PluginKeyedGate
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (SemaphoreSlim Lock, int Waiting)> _keys = new(StringComparer.Ordinal);

    internal async Task<T> RunAsync<T>(string key, Func<Task<T>> work, CancellationToken ct)
    {
        SemaphoreSlim slot;
        lock (_gate)
        {
            (SemaphoreSlim Lock, int Waiting) entry = _keys.TryGetValue(key, out var known) ? known : (new SemaphoreSlim(1, 1), 0);
            _keys[key] = (entry.Lock, entry.Waiting + 1);
            slot = entry.Lock;
        }

        try
        {
            await slot.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await work().ConfigureAwait(false);
            }
            finally
            {
                _ = slot.Release();
            }
        }
        finally
        {
            lock (_gate)
            {
                var (held, waiting) = _keys[key];
                if (waiting <= 1)
                {
                    _ = _keys.Remove(key);
                    held.Dispose();
                }
                else
                {
                    _keys[key] = (held, waiting - 1);
                }
            }
        }
    }
}
