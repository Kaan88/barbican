// Mirrors: Bulwark components/calendar/calendar-search.tsx (a results list under the search box: title, date, calendar colour; a click jumps to the event); Rust calendar.rs:660 search (the grid also filters to the text); I-313.

using Microsoft.UI.Xaml.Automation;
using System.Globalization;
using Barbican.Presentation.Calendar.Grid;
using Microsoft.UI.Text;

namespace Barbican.App.Views.Calendar;

/// <summary>The search results list under the toolbar's search box.</summary>
public sealed class SearchResultsView : UserControl
{
    private const int Shown = 8;
    private readonly CalendarViewModel _vm = CalendarUi.Model;
    private readonly Border _box = new();

    public SearchResultsView()
    {
        _box.Background = CalendarVisuals.Palette("Popup");
        _box.BorderBrush = CalendarVisuals.Palette("Line");
        _box.BorderThickness = new Thickness(1);
        // Bulwark: the menu panel's corners (context-menu.tsx:667), rows are square bands with the neutral hover
        _box.CornerRadius = CalendarVisuals.Bulwark ? Barbican.App.Shell.ViewHost.Get<Barbican.App.Theme.StyleState>().CalPopupRadius : new CornerRadius(14);
        _box.Padding = new Thickness(6);
        _box.Width = 300;
        Content = _box;
        AutomationProperties.SetAutomationId(this, "cal.search.results");
        AutomationProperties.SetName(this, "Search results");
        Visibility = Visibility.Collapsed;
    }

    /// <summary>Raised when a result was picked (the screen closes the list).</summary>
    public event EventHandler? Picked;

    public void Render()
    {
        if (string.IsNullOrWhiteSpace(_vm.SearchText))
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        var stack = new StackPanel { Spacing = 2 };
        var results = _vm.SearchResults;
        if (results.Count == 0)
        {
            stack.Children.Add(CalendarVisuals.Text("No events found", 12.5, FontWeights.Normal, CalendarVisuals.Palette("Dim")));
        }

        var colors = _vm.Calendars.ToDictionary(c => c.Id, c => c.Color, StringComparer.Ordinal);
        foreach (var o in results.Take(Shown))
        {
            var row = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } } };
            row.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(4), Background = CalendarVisuals.SolidBrush(colors.TryGetValue(o.Calendar, out var c) ? c : "#3b82f6"), VerticalAlignment = VerticalAlignment.Center });
            var title = CalendarVisuals.Text(o.Kind == Barbican.Core.Calendar.CalendarOccurrenceKind.Task ? AgendaRules.TaskTitle(o.Title) : o.Title, 13, FontWeights.SemiBold, CalendarVisuals.Palette("Body"));
            Grid.SetColumn(title, 1);
            row.Children.Add(title);
            var when = CalendarVisuals.Text(o.Start.ToString("d MMM yyyy", CultureInfo.InvariantCulture), 11.5, FontWeights.Normal, CalendarVisuals.Palette("Faint"));
            Grid.SetColumn(when, 2);
            row.Children.Add(when);
            var button = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 6, 8, 6), CornerRadius = new CornerRadius(CalendarVisuals.Bulwark ? 0 : 9), Content = row };
            AutomationProperties.SetAutomationId(button, "cal.search.result." + o.Id);
            AutomationProperties.SetName(button, o.Title);
            var picked = o;
            button.Click += (_, _) =>
            {
                _vm.ShowOccurrence(picked);
                Picked?.Invoke(this, EventArgs.Empty);
            };
            stack.Children.Add(button);
        }

        if (results.Count > Shown)
        {
            stack.Children.Add(CalendarVisuals.Text("+" + (results.Count - Shown).ToString(CultureInfo.InvariantCulture) + " more in the grid", 11.5, FontWeights.Normal, CalendarVisuals.Palette("Faint")));
        }

        _box.Child = stack;
        Visibility = Visibility.Visible;
    }

    public void Dismiss() => Visibility = Visibility.Collapsed;
}
