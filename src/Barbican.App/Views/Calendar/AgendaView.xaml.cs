// Mirrors: Rust cal_extra.rs:149 render_agenda; Bulwark components/calendar/calendar-agenda-view.tsx; I-325, B-CAL-006, 037, 047, 048; L-027 (thousands of tasks open in under a second).

using Barbican.Presentation.Calendar.Grid;
using Microsoft.UI.Xaml.Input;

namespace Barbican.App.Views.Calendar;

/// <summary>The Agenda list over <see cref="AgendaRules.Rows"/>.</summary>
public sealed partial class AgendaView : UserControl
{
    private readonly CalendarViewModel _vm = CalendarUi.Model;

    public AgendaView()
    {
        InitializeComponent();
    }

    /// <summary>The rows built by the last <see cref="Render"/> (the automation dump reads it).</summary>
    public IReadOnlyList<AgendaRow> Rows { get; private set; } = [];

    /// <summary>Builds the rows from the model's immutable occurrence list and swaps the list's source in one assignment.</summary>
    public void Render()
    {
        var rows = AgendaRules.Rows(_vm.Occurrences, _vm.Range.From, _vm.Range.To, _vm.TodayDate, _vm.Settings.Core.TwelveHour);
        var colors = _vm.Calendars.ToDictionary(c => c.Id, c => c.Color, StringComparer.Ordinal);
        var items = new List<AgendaItem>(rows.Count);
        foreach (var row in rows)
        {
            var color = row.CalendarId.Length > 0 && colors.TryGetValue(row.CalendarId, out var c) ? c : "#3b82f6";
            items.Add(new AgendaItem(row, color, row.Id is not null && string.Equals(row.Id, _vm.SelectedId, StringComparison.Ordinal)));
        }

        Rows = rows;
        List.ItemsSource = items;
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AgendaItem { Row.Id: { } id })
        {
            CalendarUi.Select(id);
        }
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (Item(e.OriginalSource) is { Row.Id: { } id })
        {
            CalendarUi.Open(id);
        }
    }

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (Item(e.OriginalSource) is { Row.Id: { } id })
        {
            CalendarUi.Select(id);
            var p = e.GetPosition(null);
            CalendarUi.ShowMenu(CalendarMenuIds.Event, p.X, p.Y, id);
        }
    }

    private static AgendaItem? Item(object source) => (source as FrameworkElement)?.DataContext as AgendaItem;
}
