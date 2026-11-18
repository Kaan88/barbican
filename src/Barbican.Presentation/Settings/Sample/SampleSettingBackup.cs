// Mirrors: M6 plan section 3.5 (D-M6-00): an in-memory fake of ISettingBackup that behaves like the real one where the M5 sample stores did not: its calls complete off the caller's thread, and its Changed is raised from a pool thread, so a view built on it meets the same threading the real service has (M5 found two such bugs, the real store loading on the UI thread and raising from the pool).
// Frozen in the M6 plan commit; owner integrator. Used by view-model tests and by sample-data runs (BARBICAN_SAMPLE=1); the data stream's real class never uses it.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Settings.Backup;

namespace Barbican.Presentation.Settings.Sample;

/// <summary>An in-memory <see cref="ISettingBackup"/>: exports a small document, accepts anything that is an object.</summary>
public sealed class SampleSettingBackup : ISettingBackup
{
    public event EventHandler? Changed;

    public Task<Result<JsonObject>> ExportAsync(CancellationToken ct) =>
        SampleThreading.OffThreadAsync(() => Result<JsonObject>.Ok(new JsonObject { ["app"] = "barbican", ["version"] = 1, ["settings"] = new JsonObject(), ["desktop"] = new JsonObject() }));

    public async Task<Result<Unit>> ImportAsync(JsonNode document, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        await SampleThreading.OffThreadAsync(() => { }).ConfigureAwait(false);
        SampleThreading.RaiseOnPool(() => Changed?.Invoke(this, EventArgs.Empty));
        return Result<Unit>.Ok(default);
    }

    public async Task<Result<Unit>> ResetAsync(CancellationToken ct)
    {
        await SampleThreading.OffThreadAsync(() => { }).ConfigureAwait(false);
        SampleThreading.RaiseOnPool(() => Changed?.Invoke(this, EventArgs.Empty));
        return Result<Unit>.Ok(default);
    }
}
