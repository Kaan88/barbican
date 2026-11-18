// Owner cal-views: the pointer rests on an event card (the hover preview).

using Barbican.Core.Calendar;
using Windows.Foundation;

namespace Barbican.App.Views.Calendar;

/// <summary>The occurrence under the pointer, the element the point is in the coordinates of, and the point.</summary>
public sealed class HoverRequestedEventArgs(CalendarOccurrence occurrence, UIElement source, Point at) : EventArgs
{
    public CalendarOccurrence Occurrence { get; } = occurrence;

    public UIElement Source { get; } = source;

    public Point At { get; } = at;
}
