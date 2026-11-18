// Mirrors: IAvailabilityService. Stub from the M4 plan section 4.3; owner cal-edit replaces the class.

using Barbican.Core.Common;

namespace Barbican.Core.Calendar.Editing;

/// <summary>The default <see cref="IAvailabilityService"/>.</summary>
public sealed class AvailabilityService : IAvailabilityService
{
    public bool IsSupported(string accountKey) => throw new PortPendingException("AvailabilityService.IsSupported");

    public Task<Result<IReadOnlyDictionary<string, Result<IReadOnlyList<(long Start, long End, bool Tentative)>>>>> BusyAsync(string accountKey, IReadOnlyList<string> emails, long fromMs, long toMs, CancellationToken ct) =>
        throw new PortPendingException("AvailabilityService.BusyAsync");
}
