// Mirrors: M6 plan section 3.5 (D-M6-00): an in-memory fake of ISettingInfo that behaves like the real one where the M5 sample stores did not: its calls complete off the caller's thread, and its Changed is raised from a pool thread, so a view built on it meets the same threading the real service has (M5 found two such bugs, the real store loading on the UI thread and raising from the pool).
// Frozen in the M6 plan commit; owner integrator. Used by view-model tests and by sample-data runs (BARBICAN_SAMPLE=1); the data stream's real class never uses it.

using Barbican.Core.Settings.Pages;

namespace Barbican.Presentation.Settings.Sample;

/// <summary>An <see cref="ISettingInfo"/> that answers every keyless row with a sample text.</summary>
public sealed class SampleSettingInfo : ISettingInfo
{
    public bool Provides(string rowId) => rowId.StartsWith("S-keyless-", StringComparison.Ordinal);

    public Task<string> TextAsync(string rowId, CancellationToken ct) => SampleThreading.OffThreadAsync(() => "Sample " + rowId);
}
