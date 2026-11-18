// Mirrors: M6 plan section 3.5 (D-M6-00): an in-memory fake of ISettingValues that behaves like the real one where the M5 sample stores did not: its calls complete off the caller's thread, and its Changed is raised from a pool thread, so a view built on it meets the same threading the real service has (M5 found two such bugs, the real store loading on the UI thread and raising from the pool).
// Frozen in the M6 plan commit; owner integrator. Used by view-model tests and by sample-data runs (BARBICAN_SAMPLE=1); the data stream's real class never uses it.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Settings.Pages;

namespace Barbican.Presentation.Settings.Sample;

/// <summary>An in-memory <see cref="ISettingValues"/>.</summary>
public sealed class SampleSettingValues : ISettingValues
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, JsonNode> _values = new(StringComparer.Ordinal);

    public event EventHandler<SettingChangedEventArgs>? Changed;

    public JsonNode? Stored(string key)
    {
        lock (_lock)
        {
            return _values.TryGetValue(key, out var v) ? v.DeepClone() : null;
        }
    }

    public async Task<Result<Unit>> SetAsync(string key, JsonNode? value, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        await SampleThreading.OffThreadAsync(() =>
        {
            lock (_lock)
            {
                if (value is null)
                {
                    _ = _values.Remove(key);
                }
                else
                {
                    _values[key] = value.DeepClone();
                }
            }
        }).ConfigureAwait(false);
        SampleThreading.RaiseOnPool(() => Changed?.Invoke(this, new SettingChangedEventArgs(key, value?.DeepClone())));
        return Result<Unit>.Ok(default);
    }
}
