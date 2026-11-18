// Mirrors: M6 plan section 3.5 (D-M6-00): an in-memory fake of IVacationService that behaves like the real one where the M5 sample stores did not: its calls complete off the caller's thread, and its Changed is raised from a pool thread, so a view built on it meets the same threading the real service has (M5 found two such bugs, the real store loading on the UI thread and raising from the pool).
// Frozen in the M6 plan commit; owner integrator. Used by view-model tests and by sample-data runs (BARBICAN_SAMPLE=1); the data stream's real class never uses it.

using Barbican.Core.Common;
using Barbican.Core.Settings.Vacation;

namespace Barbican.Presentation.Settings.Sample;

/// <summary>An in-memory <see cref="IVacationService"/>.</summary>
public sealed class SampleVacationService : IVacationService
{
    private readonly Lock _lock = new();
    private VacationSettings _current = new(false, "Out of office", string.Empty, null, null, null, null, null, false);

    public event EventHandler<VacationChangedEventArgs>? Changed;

    public Task<Result<VacationSettings>> LoadAsync(string accountKey, CancellationToken ct) =>
        SampleThreading.OffThreadAsync(() =>
        {
            lock (_lock)
            {
                return Result<VacationSettings>.Ok(_current);
            }
        });

    public async Task<Result<VacationSettings>> SaveAsync(string accountKey, VacationSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await SampleThreading.OffThreadAsync(() =>
        {
            lock (_lock)
            {
                _current = settings;
            }
        }).ConfigureAwait(false);
        SampleThreading.RaiseOnPool(() => Changed?.Invoke(this, new VacationChangedEventArgs(accountKey)));
        return Result<VacationSettings>.Ok(settings);
    }
}
