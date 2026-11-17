// Mirrors: see IScheduledSends. Contract stub from the M3 plan section 4.3; owner send-pipeline replaces this class (constructor and body).

using Barbican.Core.Common;

namespace Barbican.Core.Compose;

/// <summary>The default registration of <see cref="IScheduledSends"/>: every call fails with <see cref="PortPendingException"/> until stream send-pipeline lands.</summary>
public sealed class ScheduledSends : IScheduledSends
{
    public Task<Result<IReadOnlyList<ScheduledMessage>>> ListAsync(string accountId, CancellationToken ct) => Task.FromException<Result<IReadOnlyList<ScheduledMessage>>>(new PortPendingException("ScheduledSends.ListAsync"));

    public Task<Result<Unit>> RescheduleAsync(string accountId, string submissionId, DateTimeOffset at, CancellationToken ct) => Task.FromException<Result<Unit>>(new PortPendingException("ScheduledSends.RescheduleAsync"));

    public Task<Result<string>> CancelAsync(string accountId, string submissionId, CancellationToken ct) => Task.FromException<Result<string>>(new PortPendingException("ScheduledSends.CancelAsync"));
}
