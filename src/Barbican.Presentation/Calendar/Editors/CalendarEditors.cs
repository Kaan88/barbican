// Mirrors: ICalendarEditors. Stub from the M4 plan section 4.6; owner cal-ui replaces the class (constructor dependencies: ICalendarStore, ICalendarWriter, IAvailabilityService, IHookBus, IDialogService, IToastService, IPrefsStore, TimeProvider, IDispatcher).

using Barbican.Core.Calendar.Editing;
using Barbican.Core.Common;

namespace Barbican.Presentation.Calendar.Editors;

/// <summary>The default <see cref="ICalendarEditors"/>.</summary>
public sealed class CalendarEditors : ICalendarEditors
{
    public Task OpenQuickCreateAsync(NewEventRequest request, CancellationToken ct) => throw new PortPendingException("CalendarEditors.OpenQuickCreateAsync");

    public Task OpenNewEventAsync(NewEventRequest request, CancellationToken ct) => throw new PortPendingException("CalendarEditors.OpenNewEventAsync");

    public Task OpenAsync(string occurrenceId, CancellationToken ct) => throw new PortPendingException("CalendarEditors.OpenAsync");

    public Task OpenNewTaskAsync(string? calendarId, DateOnly? due, CancellationToken ct) => throw new PortPendingException("CalendarEditors.OpenNewTaskAsync");

    public Task<(EditScope Scope, bool Notify)?> AskScopeAsync(string title, bool forDelete, bool hasGuests, CancellationToken ct) => throw new PortPendingException("CalendarEditors.AskScopeAsync");
}
