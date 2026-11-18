// Mirrors: Rust app.slint:3036-3065 + calendar_ext.slint CalSidebarExtra (New event, mini month, calendar list with colours and checkboxes, Tasks entry, capped tasks list); Bulwark components/calendar/calendar-sidebar-panel.tsx (section headers, "+", account groups, row menu, "..." button, colour);
// design BNCalendar / NordCalendar (the sidebar: "+ New event" over a mini-month tile, a Calendars tile, a Tasks tile). I-298 (resize is the screen's handle), I-302, I-304, I-305, I-306, I-308.
// The calendar row's menu (rename, set default, delete, the colour submenu) is cal-ui's provider of MENU-CAL; the row only opens it. The capped task list is cal-ui's control in the slot SlotNames.CalendarSidebarTasks.

using Microsoft.UI.Xaml.Automation;
using System.Globalization;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Calendar.Grid;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Input;

#pragma warning disable MA0051 // a drawing routine builds one frame in one pass (no flicker); splitting it would only pass the same locals around
namespace Barbican.App.Views.Calendar;

/// <summary>The calendar sidebar.</summary>
public sealed class CalendarSidebarView : UserControl
{
    private readonly CalendarViewModel _vm = CalendarUi.Model;
    private readonly StyleState _look = ViewHost.Get<StyleState>();
    private readonly MiniMonthView _mini = new();
    private readonly StackPanel _calendars = new() { Spacing = 2 };
    private readonly Border _miniTile = new();
    private readonly Border _calendarTile = new();
    private readonly Border _tasksTile = new();
    private readonly ContentPresenter _tasksSlot = new();
    private readonly StackPanel _stack = new() { Spacing = 12 };
    private readonly Button _new;

    public CalendarSidebarView()
    {
        _new = new Button
        {
            Style = (Style)Application.Current.Resources["Button.Primary"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { CalendarVisuals.Icon("Plus", 16, 2.2), new TextBlock { Text = "New event", VerticalAlignment = VerticalAlignment.Center } } },
        };
        AutomationProperties.SetAutomationId(_new, "cal.new-event");
        AutomationProperties.SetName(_new, "New event");
        _new.Click += (_, _) =>
        {
            var now = _vm.Now();
            var start = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Unspecified).AddHours(1);
            var anchored = _vm.Anchor == _vm.TodayDate ? start : _vm.Anchor.ToDateTime(new TimeOnly(9, 0));
            CalendarUi.NewEvent(anchored, anchored.AddHours(1), allDay: false);
        };
        _miniTile.Child = _mini;
        _calendarTile.Child = _calendars;
        _tasksTile.Child = _tasksSlot;
        SlotHost.Fill(_tasksSlot, SlotNames.CalendarSidebarTasks);
        _stack.Children.Add(_new);
        _stack.Children.Add(_miniTile);
        _stack.Children.Add(_calendarTile);
        _stack.Children.Add(_tasksTile);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _stack, Padding = new Thickness(0, 0, 0, 12) };
        AutomationProperties.SetAutomationId(this, "cal.sidebar");
        AutomationProperties.SetName(this, "Calendars");
        ApplyLook();
        Render();
        _look.PropertyChanged += (_, _) =>
        {
            ApplyLook();
            Render();
        };
    }

    private void ApplyLook()
    {
        var bento = _look.IsBento;
        foreach (var tile in new[] { _miniTile, _calendarTile })
        {
            Tile(tile, bento);
        }

        Tile(_tasksTile, bento);
        _tasksTile.Visibility = _tasksSlot.Visibility;
        _new.Height = bento ? 52 : 46;
        _new.CornerRadius = new CornerRadius(bento ? 20 : 16);
        _new.FontSize = 14.5;
    }

    private void Tile(Border tile, bool bento)
    {
        tile.Background = bento ? _look.TileBrush : null;
        tile.BorderBrush = bento ? CalendarVisuals.Palette("Line") : null;
        tile.BorderThickness = new Thickness(bento ? 1 : 0);
        tile.CornerRadius = bento ? _look.PaneRadius : new CornerRadius(0);
        tile.Padding = bento ? new Thickness(14) : new Thickness(4, 0, 4, 0);
    }

    /// <summary>Redraws the mini month and the calendar list from the model.</summary>
    public void Render()
    {
        _mini.Render();
        var rows = _vm.Calendars;
        var children = new List<UIElement> { Section("Calendars", "cal.sidebar.add", "New calendar", "calendar.new-calendar") };
        foreach (var row in rows.Where(r => !r.IsShared))
        {
            children.Add(Row(row));
        }

        var shared = rows.Where(r => r.IsShared).ToList();
        if (shared.Count > 0)
        {
            children.Add(Section("Shared with me", null, null, null));
            children.AddRange(shared.Select(Row));
        }

        if (_vm.AvailableViews.Contains(CalendarViewKind.Tasks))
        {
            children.Add(TasksEntry());
        }

        _calendars.Children.Clear();
        foreach (var child in children)
        {
            _calendars.Children.Add(child);
        }

        _tasksTile.Visibility = _tasksSlot.Visibility;
    }

    private static Grid Section(string title, string? plusId, string? plusName, string? command)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4), ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } } };
        grid.Children.Add(new TextBlock { Text = title, FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = CalendarVisuals.Palette("Dim"), VerticalAlignment = VerticalAlignment.Center });
        if (plusId is not null && command is not null)
        {
            var plus = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Width = 24, Height = 24, CornerRadius = new CornerRadius(8), Content = CalendarVisuals.Icon("Plus", 14, 2), IsEnabled = CalendarUi.CanRun(command) };
            AutomationProperties.SetAutomationId(plus, plusId);
            AutomationProperties.SetName(plus, plusName ?? title);
            plus.Click += (_, _) => CalendarUi.RunCommand(command);
            Grid.SetColumn(plus, 1);
            grid.Children.Add(plus);
        }

        return grid;
    }

    private Grid Row(CalendarListRow row)
    {
        var grid = new Grid { Height = 30, ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } }, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)) };
        var swatch = new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(5),
            VerticalAlignment = VerticalAlignment.Center,
            Background = row.Visible ? CalendarVisuals.SolidBrush(row.Color) : null,
            BorderBrush = CalendarVisuals.SolidBrush(row.Color),
            BorderThickness = new Thickness(1.5),
            Child = row.Visible ? new Barbican.App.Controls.SIcon { Kind = "Check", Size = 10, StrokeWidth = 3, Foreground = CalendarVisuals.Palette("OnAccent"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } : null,
        };
        grid.Children.Add(swatch);
        var name = CalendarVisuals.Text(row.Name, 13, row.IsDefault ? FontWeights.SemiBold : FontWeights.Normal, CalendarVisuals.Palette(row.Visible ? "Body" : "Faint"));
        name.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        var more = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Width = 24, Height = 24, CornerRadius = new CornerRadius(8), Content = CalendarVisuals.Icon("More", 14, 2), Opacity = 0.7 };
        AutomationProperties.SetAutomationId(more, "cal.row.more." + row.Id);
        AutomationProperties.SetName(more, row.Name + " options");
        more.Click += (_, _) =>
        {
            var p = more.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, more.ActualHeight));
            CalendarUi.ShowMenu(CalendarMenuIds.Calendar, p.X, p.Y, calendarId: row.Id);
        };
        Grid.SetColumn(more, 2);
        grid.Children.Add(more);
        AutomationProperties.SetAutomationId(grid, "cal.row." + row.Id);
        AutomationProperties.SetName(grid, row.Name);
        grid.Tapped += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, more) && !IsInside(e.OriginalSource as DependencyObject, more))
            {
                _vm.ToggleCalendar(row.Id);
            }
        };
        grid.RightTapped += (_, e) =>
        {
            var p = e.GetPosition(null);
            CalendarUi.ShowMenu(CalendarMenuIds.Calendar, p.X, p.Y, calendarId: row.Id);
            e.Handled = true;
        };
        return grid;
    }

    private static bool IsInside(DependencyObject? node, DependencyObject ancestor)
    {
        for (; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private Button TasksEntry()
    {
        var current = _vm.View == CalendarViewKind.Tasks;
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["Button.Icon"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Height = 32,
            Margin = new Thickness(0, 6, 0, 0),
            CornerRadius = new CornerRadius(10),
            Background = current ? CalendarVisuals.Palette("Sel") : null,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Padding = new Thickness(2, 0, 0, 0),
                Children = { CalendarVisuals.Icon("Check", 16, 2), new TextBlock { Text = "Tasks", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = CalendarVisuals.Palette(current ? "SelText" : "Body"), VerticalAlignment = VerticalAlignment.Center } },
            },
            Foreground = CalendarVisuals.Palette(current ? "SelText" : "Dim"),
        };
        AutomationProperties.SetAutomationId(button, "cal.sidebar.tasks");
        AutomationProperties.SetName(button, "Tasks");
        button.Click += (_, _) => _vm.SetView(CalendarViewKind.Tasks);
        return button;
    }
}
