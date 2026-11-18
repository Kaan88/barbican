// Owner cal-views: the toolbar's More button was pressed.

using Windows.Foundation;

namespace Barbican.App.Views.Calendar;

/// <summary>The point under the More button, in window coordinates.</summary>
public sealed class MoreRequestedEventArgs(Point at) : EventArgs
{
    public Point At { get; } = at;
}
