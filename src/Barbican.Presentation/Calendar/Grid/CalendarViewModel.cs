// Mirrors: ICalendarViewModel. Stub from the M4 plan section 4.5; owner cal-views replaces the class (constructor dependencies: ICalendarStore, IAccountManager, IPrefsStore, IHookBus, TimeProvider, IDispatcher).

using System.ComponentModel;
using Barbican.Core.Calendar;
using Barbican.Core.Common;

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>The default <see cref="ICalendarViewModel"/>.</summary>
public sealed class CalendarViewModel : ICalendarViewModel
{
    public event PropertyChangedEventHandler? PropertyChanged
    {
        add { }
        remove { }
    }

    public CalendarViewKind View => throw new PortPendingException("CalendarViewModel.View");

    public DateOnly Anchor => throw new PortPendingException("CalendarViewModel.Anchor");

    public (DateOnly From, DateOnly To) Range => throw new PortPendingException("CalendarViewModel.Range");

    public string Title => throw new PortPendingException("CalendarViewModel.Title");

    public IReadOnlyList<CalendarOccurrence> Occurrences => throw new PortPendingException("CalendarViewModel.Occurrences");

    public IReadOnlyList<CalendarListRow> Calendars => throw new PortPendingException("CalendarViewModel.Calendars");

    public string? SelectedId => throw new PortPendingException("CalendarViewModel.SelectedId");

    public string SearchText
    {
        get => throw new PortPendingException("CalendarViewModel.SearchText");
        set => throw new PortPendingException("CalendarViewModel.SearchText");
    }

    public bool IsLoading => throw new PortPendingException("CalendarViewModel.IsLoading");

    public string? LoadError => throw new PortPendingException("CalendarViewModel.LoadError");

    public void SetView(CalendarViewKind view) => throw new PortPendingException("CalendarViewModel.SetView");

    public void Today() => throw new PortPendingException("CalendarViewModel.Today");

    public void GoPrevious() => throw new PortPendingException("CalendarViewModel.GoPrevious");

    public void GoNext() => throw new PortPendingException("CalendarViewModel.GoNext");

    public void ShowDay(DateOnly day) => throw new PortPendingException("CalendarViewModel.ShowDay");

    public void SelectOccurrence(string? id) => throw new PortPendingException("CalendarViewModel.SelectOccurrence");

    public void ToggleCalendar(string calendarId) => throw new PortPendingException("CalendarViewModel.ToggleCalendar");

    public Task LoadAsync(CancellationToken ct) => throw new PortPendingException("CalendarViewModel.LoadAsync");
}
