// Mirrors: Rust calendar.rs:1250 detail (the context panel beside the grid: title, when, where, repeats, guests with their answers), Bulwark components/calendar/event-detail-popover.tsx; design BNCalendar "Event details" aside (a tinted title card, facts, a divider, guests with avatars and their answer).
// I-331 (the detail panel beside the grid), B-CAL-054. Selecting shows it; opening (the editor) is cal-ui's: the Edit button calls CalendarViewModel.OpenAsync.

using Microsoft.UI.Xaml.Automation;
using System.Globalization;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Core.Calendar;
using Barbican.Presentation.Calendar.Grid;
using Microsoft.UI.Text;

#pragma warning disable MA0051 // a drawing routine builds one frame in one pass (no flicker); splitting it would only pass the same locals around
namespace Barbican.App.Views.Calendar;

/// <summary>The panel beside the grid with the selected occurrence's details.</summary>
public sealed class EventDetailPanel : UserControl
{
    private readonly CalendarViewModel _vm = CalendarUi.Model;
    private readonly StyleState _look = ViewHost.Get<StyleState>();
    private readonly Border _box = new();
    private readonly StackPanel _stack = new() { Spacing = 14 };

    public EventDetailPanel()
    {
        _box.Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _stack };
        Content = _box;
        AutomationProperties.SetAutomationId(this, "cal.detail");
        AutomationProperties.SetName(this, "Event details");
        Width = 280;
        Render();
    }

    public void Render()
    {
        var bento = _look.IsBento;
        _box.Background = _look.TileBrush;
        _box.BorderBrush = CalendarVisuals.Palette("Line");
        _box.BorderThickness = bento ? new Thickness(1) : new Thickness(1, 0, 0, 0);
        _box.CornerRadius = bento ? _look.PaneRadius : new CornerRadius(0);
        _box.Padding = new Thickness(18, 20, 18, 20);
        var detail = _vm.Detail;
        Visibility = detail is null ? Visibility.Collapsed : Visibility.Visible;
        var children = new List<UIElement>();
        if (detail is not null)
        {
            children.AddRange(Build(detail));
        }

        _stack.Children.Clear();
        foreach (var child in children)
        {
            _stack.Children.Add(child);
        }
    }

    private IEnumerable<UIElement> Build(EventDetail d)
    {
        var (bg, fg) = CalendarVisuals.Tint(d.CalendarColor, _vm.Settings.Core.TintedEvents);
        var head = new StackPanel { Spacing = 3 };
        head.Children.Add(CalendarVisuals.Text(d.CalendarName.Length > 0 ? d.CalendarName : "Event", 11.5, FontWeights.Bold, fg));
        var title = CalendarVisuals.Text(d.Title, 18, FontWeights.ExtraBold, fg, trim: false);
        title.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetAutomationId(title, "cal.detail.title");
        head.Children.Add(title);
        head.Children.Add(CalendarVisuals.Text(d.When, 12.5, FontWeights.Normal, fg, trim: false));
        yield return new Border { Background = bg, CornerRadius = new CornerRadius(18), Padding = new Thickness(14), Child = head };

        if (d.Kind != CalendarOccurrenceKind.Birthday)
        {
            var edit = new Button { Style = (Style)Application.Current.Resources["Button.Primary"], Content = d.Kind == CalendarOccurrenceKind.Task ? "Open task" : "Edit", Height = 36, CornerRadius = new CornerRadius(12), FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetAutomationId(edit, "cal.detail.edit");
            edit.Click += (_, _) => CalendarUi.Open(d.Id);
            yield return edit;
        }

        var facts = new StackPanel { Spacing = 4 };
        if (d.Location.Length > 0)
        {
            facts.Children.Add(CalendarVisuals.Text(d.Location, 13, FontWeights.Normal, CalendarVisuals.Palette("Body"), trim: false));
        }

        facts.Children.Add(CalendarVisuals.Text(d.Repeats ? "Repeats" : "Repeats: never", 13, FontWeights.Normal, CalendarVisuals.Palette("Body"), trim: false));
        if (d.Description.Length > 0)
        {
            var text = CalendarVisuals.Text(d.Description, 12.5, FontWeights.Normal, CalendarVisuals.Palette("Dim"), trim: false);
            text.TextWrapping = TextWrapping.Wrap;
            text.MaxLines = 8;
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            facts.Children.Add(text);
        }

        yield return facts;

        if (d.Guests.Count > 0)
        {
            yield return new Border { Height = 1, Background = CalendarVisuals.Palette("Line") };
            yield return CalendarVisuals.Text("Guests · " + d.Guests.Count.ToString(CultureInfo.InvariantCulture), 11.5, FontWeights.Bold, CalendarVisuals.Palette("Dim"));
            foreach (var g in d.Guests)
            {
                yield return Guest(g);
            }
        }
    }

    private static Grid Guest(EventGuest g)
    {
        var grid = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } } };
        var tint = g.Name.Sum(c => (int)c) % 4;
        string[] bg = ["RoseBg", "SkyBg", "MintBg", "AmberBg"];
        string[] fg = ["RoseFg", "SkyFg", "MintFg", "AmberFg"];
        grid.Children.Add(new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(11),
            Background = CalendarVisuals.Palette(bg[tint]),
            Child = new TextBlock { Text = g.Initials, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = CalendarVisuals.Palette(fg[tint]), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        var name = CalendarVisuals.Text(g.Name, 13, FontWeights.Normal, CalendarVisuals.Palette("Body"));
        name.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        var status = CalendarVisuals.Text(g.Status, 11.5, FontWeights.Normal, CalendarVisuals.Palette(g.Status switch { "Accepted" => "MintFg", "Declined" => "RoseFg", "No reply" => "AmberFg", _ => "Dim" }));
        status.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(status, 2);
        grid.Children.Add(status);
        AutomationProperties.SetName(grid, g.Name + ", " + g.Status);
        return grid;
    }
}
