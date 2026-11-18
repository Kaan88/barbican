// Mirrors: ICalendarAdmin. Stub from the M4 plan section 4.3; owner cal-edit replaces the class.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Calendar.Editing;

/// <summary>The default <see cref="ICalendarAdmin"/>.</summary>
public sealed class CalendarAdmin : ICalendarAdmin
{
    public Task<Result<string>> CreateCalendarAsync(string accountKey, string name, string color, CancellationToken ct) => throw new PortPendingException("CalendarAdmin.CreateCalendarAsync");

    public Task<Result<Unit>> UpdateCalendarAsync(string accountKey, string calendarId, JsonObject patch, CancellationToken ct) => throw new PortPendingException("CalendarAdmin.UpdateCalendarAsync");

    public Task<Result<Unit>> SetDefaultAsync(string accountKey, string calendarId, CancellationToken ct) => throw new PortPendingException("CalendarAdmin.SetDefaultAsync");

    public Task<Result<Unit>> DeleteCalendarAsync(string accountKey, string calendarId, CancellationToken ct) => throw new PortPendingException("CalendarAdmin.DeleteCalendarAsync");
}
