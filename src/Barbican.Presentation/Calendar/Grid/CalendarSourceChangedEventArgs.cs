// Owner cal-views.

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>The data of a login changed; <c>CalendarsChanged</c> when a calendar (not only an event) did.</summary>
public sealed class CalendarSourceChangedEventArgs(string accountKey, bool calendarsChanged) : EventArgs
{
    public string AccountKey { get; } = accountKey;

    public bool CalendarsChanged { get; } = calendarsChanged;
}
