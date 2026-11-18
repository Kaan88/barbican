// Mirrors: M6 plan section 3.5 (D-M6-00): an in-memory fake of IAccountQuota that behaves like the real one where the M5 sample stores did not: its calls complete off the caller's thread, and its Changed is raised from a pool thread, so a view built on it meets the same threading the real service has (M5 found two such bugs, the real store loading on the UI thread and raising from the pool).
// Frozen in the M6 plan commit; owner integrator. Used by view-model tests and by sample-data runs (BARBICAN_SAMPLE=1); the data stream's real class never uses it.

using Barbican.Core.Common;
using Barbican.Core.Settings.Account;

namespace Barbican.Presentation.Settings.Sample;

/// <summary>An <see cref="IAccountQuota"/> with 3.5 MB of 1 GB used.</summary>
public sealed class SampleAccountQuota : IAccountQuota
{
    public Task<Result<AccountQuotaUsage>> UsageAsync(string accountKey, CancellationToken ct) =>
        SampleThreading.OffThreadAsync(() => Result<AccountQuotaUsage>.Ok(new AccountQuotaUsage(3_500_000, 1_000_000_000)));
}
