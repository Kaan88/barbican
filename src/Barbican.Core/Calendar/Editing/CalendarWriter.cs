// Mirrors: ICalendarWriter. Stub from the M4 plan section 4.3; owner cal-edit replaces the class (constructor dependencies: IAccountManager, ICalendarStore, IHookBus, TimeProvider).

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Calendar.Editing;

/// <summary>The default <see cref="ICalendarWriter"/>.</summary>
public sealed class CalendarWriter : ICalendarWriter
{
    public Task<Result<string>> SaveEventAsync(EventWrite write, CancellationToken ct) => throw new PortPendingException("CalendarWriter.SaveEventAsync");

    public Task<Result<Unit>> DeleteEventAsync(string accountKey, string eventId, EditScope scope, string? recurrenceId, bool notify, CancellationToken ct) => throw new PortPendingException("CalendarWriter.DeleteEventAsync");

    public Task<Result<Unit>> MoveAsync(string accountKey, string occurrenceId, DateTime newStart, TimeSpan duration, CancellationToken ct) => throw new PortPendingException("CalendarWriter.MoveAsync");

    public Task<Result<string>> CreateAsync(string accountKey, string calendarId, JsonObject data, CancellationToken ct) => throw new PortPendingException("CalendarWriter.CreateAsync");

    public Task<Result<Unit>> UpdateAsync(string accountKey, string id, JsonObject patch, CancellationToken ct) => throw new PortPendingException("CalendarWriter.UpdateAsync");

    public Task<Result<Unit>> DestroyAsync(string accountKey, string id, CancellationToken ct) => throw new PortPendingException("CalendarWriter.DestroyAsync");
}
