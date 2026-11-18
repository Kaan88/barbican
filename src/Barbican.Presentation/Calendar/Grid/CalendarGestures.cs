// Mirrors: ICalendarGestures. Stub from the M4 plan section 4.5; owner cal-views replaces the class (constructor dependencies: ICalendarWriter, ICalendarViewModel, ICalendarEditors, IToastService).

using Barbican.Core.Common;

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>The default <see cref="ICalendarGestures"/>.</summary>
public sealed class CalendarGestures : ICalendarGestures
{
    public DateTime Snap(DateTime time) => throw new PortPendingException("CalendarGestures.Snap");

    public Task<Result<Unit>> MoveAsync(string occurrenceId, DateTime newStart, CancellationToken ct) => throw new PortPendingException("CalendarGestures.MoveAsync");

    public Task<Result<Unit>> ResizeAsync(string occurrenceId, DateTime newEnd, CancellationToken ct) => throw new PortPendingException("CalendarGestures.ResizeAsync");

    public Task CreateAsync(DateTime start, DateTime until, bool allDay, CancellationToken ct) => throw new PortPendingException("CalendarGestures.CreateAsync");
}
