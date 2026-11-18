// The default ICalendarChrome: nothing to focus, print or open until the calendar screen provides one. Owner cal-ui.

namespace Barbican.Presentation.Calendar.Commands;

/// <summary>A chrome that does nothing.</summary>
public sealed class NoCalendarChrome : ICalendarChrome
{
    public void FocusSearch()
    {
    }

    public void Print(string? occurrenceId)
    {
    }

    public void OpenLink(string url)
    {
    }
}
