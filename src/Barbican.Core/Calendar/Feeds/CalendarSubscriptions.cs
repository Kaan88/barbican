// Mirrors: ICalendarSubscriptions. Stub from the M4 plan section 4.3; owner cal-edit replaces the class (constructor dependencies: ICalendarWriter, ICalendarAdmin, ICalendarStore, an HTTP fetch seam over IHttpClientFactory-style HttpClient, TimeProvider, IProfilePaths).

using Barbican.Core.Common;

namespace Barbican.Core.Calendar.Feeds;

/// <summary>The default <see cref="ICalendarSubscriptions"/>.</summary>
public sealed class CalendarSubscriptions : ICalendarSubscriptions
{
    public Task<IReadOnlyList<CalendarSubscription>> ListAsync(string accountKey, CancellationToken ct) => throw new PortPendingException("CalendarSubscriptions.ListAsync");

    public Task<Result<string?>> SaveAsync(string accountKey, CalendarSubscription subscription, string name, CancellationToken ct) => throw new PortPendingException("CalendarSubscriptions.SaveAsync");

    public Task<Result<int>> RefreshAsync(string accountKey, string calendarId, CancellationToken ct) => throw new PortPendingException("CalendarSubscriptions.RefreshAsync");

    public Task<Result<Unit>> RemoveAsync(string accountKey, string calendarId, bool deleteCalendar, CancellationToken ct) => throw new PortPendingException("CalendarSubscriptions.RemoveAsync");
}
