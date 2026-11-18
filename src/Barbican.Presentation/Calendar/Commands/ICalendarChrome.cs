// Mirrors: Bulwark calendar-app.tsx (focus the search box, print, open a meeting link). What the calendar commands need of the calendar screen that is not in ICalendarViewModel; additive contract of cal-ui, cal-views' screen replaces the default.

namespace Barbican.Presentation.Calendar.Commands;

/// <summary>Things only the calendar screen can do.</summary>
public interface ICalendarChrome
{
    /// <summary>Moves the focus to the event search box (the "/" key).</summary>
    void FocusSearch();

    /// <summary>Opens the print view (D-M4-14) for the current range, or for one event.</summary>
    void Print(string? occurrenceId);

    /// <summary>Opens a web address in the browser (a meeting link).</summary>
    void OpenLink(string url);
}
