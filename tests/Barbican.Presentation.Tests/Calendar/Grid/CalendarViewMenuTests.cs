// New for WP-12 (no Rust test): the view dropdown of Bulwark's calendar toolbar (calendar-toolbar.tsx:70-77, :126-129) lists Day, Week, Month, Agenda, Tasks with the keys D, W, M, A, K.

using Barbican.Presentation.Calendar.Grid;

namespace Barbican.Presentation.Tests.Calendar.Grid;

public sealed class CalendarViewMenuTests
{
    [Fact]
    public void Menu_lists_the_views_in_the_order_of_the_dropdown()
    {
        CalendarViewKind[] available = [CalendarViewKind.Month, CalendarViewKind.Week, CalendarViewKind.Day, CalendarViewKind.Agenda, CalendarViewKind.Tasks];
        Assert.Equal([CalendarViewKind.Day, CalendarViewKind.Week, CalendarViewKind.Month, CalendarViewKind.Agenda, CalendarViewKind.Tasks], CalendarViewMenu.Order(available));
    }

    [Fact]
    public void Menu_leaves_out_tasks_where_there_are_none()
    {
        CalendarViewKind[] available = [CalendarViewKind.Month, CalendarViewKind.Week, CalendarViewKind.Day, CalendarViewKind.Agenda];
        Assert.Equal([CalendarViewKind.Day, CalendarViewKind.Week, CalendarViewKind.Month, CalendarViewKind.Agenda], CalendarViewMenu.Order(available));
    }

    [Theory]
    [InlineData(CalendarViewKind.Day, "D")]
    [InlineData(CalendarViewKind.Week, "W")]
    [InlineData(CalendarViewKind.Month, "M")]
    [InlineData(CalendarViewKind.Agenda, "A")]
    [InlineData(CalendarViewKind.Tasks, "K")]
    public void Each_view_has_its_key_cap(CalendarViewKind view, string key) => Assert.Equal(key, CalendarViewMenu.Shortcut(view));
}
