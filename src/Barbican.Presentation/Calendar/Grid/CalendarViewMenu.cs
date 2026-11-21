// Mirrors: Bulwark components/calendar/calendar-toolbar.tsx:70-77 (VIEW_SHORTCUTS: D, W, M, A, K) and :126-129 (the view menu lists Day, Week, Month, Agenda, Tasks, "in the order Google Calendar lists them"; the Tasks entry only where tasks exist). WP-12, CA-01.

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>The order and the key caps of the toolbar's view menu.</summary>
public static class CalendarViewMenu
{
    private static readonly CalendarViewKind[] MenuOrder = [CalendarViewKind.Day, CalendarViewKind.Week, CalendarViewKind.Month, CalendarViewKind.Agenda, CalendarViewKind.Tasks];

    /// <summary>The available views in the menu's order.</summary>
    public static IReadOnlyList<CalendarViewKind> Order(IEnumerable<CalendarViewKind> available)
    {
        ArgumentNullException.ThrowIfNull(available);
        var set = available.ToHashSet();
        return [.. MenuOrder.Where(set.Contains)];
    }

    /// <summary>The key that switches to <paramref name="view"/>, shown as a key cap.</summary>
    public static string Shortcut(CalendarViewKind view) => view switch
    {
        CalendarViewKind.Day => "D",
        CalendarViewKind.Week => "W",
        CalendarViewKind.Month => "M",
        CalendarViewKind.Agenda => "A",
        _ => "K",
    };
}
