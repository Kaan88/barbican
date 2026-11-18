// Mirrors: Rust calendar.rs (ST, range, nav, set_view, toggle_calendar, search, select, summary, detail); Bulwark stores/calendar-store.ts, components/calendar/calendar-app.tsx; spec I-296..I-345.
// Contract from the M4 plan section 4.5; owner cal-views implements, cal-ui (editors, menus, commands, tasks view) consumes it.

using System.ComponentModel;
using Barbican.Core.Calendar;

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>The state of the calendar surface: the view, the anchor date, what is loaded for the visible range, the sidebar calendars, the selection. One per window; <c>PropertyChanged</c> is raised on the UI thread.</summary>
public interface ICalendarViewModel : INotifyPropertyChanged
{
    CalendarViewKind View { get; }

    /// <summary>The day the range is built around (today at first).</summary>
    DateOnly Anchor { get; }

    /// <summary>The visible range, first day to the day after the last: Day 1 day, Week 7 (or the days shown), Month the 6-week grid, Agenda 30 days (<c>CalendarOccurrences.AgendaDays</c>).</summary>
    (DateOnly From, DateOnly To) Range { get; }

    /// <summary>The toolbar title ("November 2026", "16 to 22 Nov 2026"); smaller wording when the grid is narrow is the view's (B-CAL-053).</summary>
    string Title { get; }

    /// <summary>Occurrences of the visible calendars in <see cref="Range"/>, events, tasks (when shown on the calendar) and birthdays, on the user's clock.</summary>
    IReadOnlyList<CalendarOccurrence> Occurrences { get; }

    IReadOnlyList<CalendarListRow> Calendars { get; }

    /// <summary>The selected occurrence id (or null). Selecting is not opening (B-CAL-040); opening is <c>ICalendarEditors</c>.</summary>
    string? SelectedId { get; }

    string SearchText { get; set; }

    bool IsLoading { get; }

    /// <summary>The load error with a Retry (I-301), or null.</summary>
    string? LoadError { get; }

    void SetView(CalendarViewKind view);

    void Today();

    void GoPrevious();

    void GoNext();

    void ShowDay(DateOnly day);

    void SelectOccurrence(string? id);

    void ToggleCalendar(string calendarId);

    /// <summary>Loads the active login's calendars and events (and keeps them live through <c>ICalendarStore.Changed</c>); the Retry button calls it again.</summary>
    Task LoadAsync(CancellationToken ct);
}
