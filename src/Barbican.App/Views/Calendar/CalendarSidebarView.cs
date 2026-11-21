// Mirrors: Rust app.slint:3036-3065 + calendar_ext.slint CalSidebarExtra (New event, mini month, calendar list with colours and checkboxes, Tasks entry, capped tasks list); Bulwark components/calendar/calendar-sidebar-panel.tsx (section headers, "+", account groups, row menu, "..." button, colour);
// design BNCalendar / NordCalendar (the sidebar: "+ New event" over a mini-month tile, a Calendars tile, a Tasks tile). I-298 (resize is the screen's handle), I-302, I-304, I-305, I-306, I-308.
// The calendar row's menu (rename, set default, delete, the colour submenu) is cal-ui's provider of MENU-CAL; the row only opens it. The capped task list is cal-ui's control in the slot SlotNames.CalendarSidebarTasks.
// Bulwark: calendar-sidebar-header.tsx:23-40 (an h-14 row, px-3, a border below: "Calendar" text-sm semibold and a ghost "+"), calendar-app.tsx:2429-2450 (py-3, the mini calendar in px-3), calendar-sidebar-panel.tsx:488-510 (the Tasks row: min-h-8 px-3 text-sm, a muted
// icon, the pending count and the overdue count at the end), :445-470 (a section header: a chevron, text-xs medium muted, a plus shown on hover), :354-380 (a row: ps-5 pe-3 min-h-8 text-sm, a 14 px box with a 2 px border in the calendar colour, hover:bg-muted, the options button on hover);
// the capped task list is Barbican's own (Bulwark has none) and stays hidden in the Bulwark style.

using Microsoft.UI.Xaml.Automation;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Calendar.Grid;
using Barbican.Presentation.Calendar.Tasks;
using Microsoft.UI.Text;

#pragma warning disable MA0051 // a drawing routine builds one frame in one pass (no flicker); splitting it would only pass the same locals around
namespace Barbican.App.Views.Calendar;

/// <summary>The calendar sidebar.</summary>
public sealed class CalendarSidebarView : UserControl
{
    private static readonly HashSet<string> Collapsed = new(StringComparer.Ordinal);

    private readonly CalendarViewModel _vm = CalendarUi.Model;
    private readonly StyleState _look = ViewHost.Get<StyleState>();
    private readonly MiniMonthView _mini = new();
    private readonly StackPanel _calendars = new() { Spacing = 2 };
    private readonly Border _miniTile = new();
    private readonly Border _calendarTile = new();
    private readonly Border _tasksTile = new();
    private readonly ContentPresenter _tasksSlot = new();
    private readonly StackPanel _stack = new() { Spacing = 12 };
    private readonly Border _root = new();
    private readonly Border _header = new();
    private readonly Grid _frame = new();
    private readonly ScrollViewer _scroll;
    private readonly Button _new;
    private readonly Button _plus;
    private readonly TasksViewModel? _tasks = ViewHost.Find<TasksViewModel>();

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
        _new.Click += (_, _) => CalendarUi.NewEventNow();

        // calendar-sidebar-header.tsx: the app name and a ghost "+"
        _plus = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Width = 32, Height = 32, Content = CalendarVisuals.Icon("Plus", 16, 2), Foreground = CalendarVisuals.Palette("Dim"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(_plus, "cal.sidebar.create");
        AutomationProperties.SetName(_plus, "New event");
        _plus.Click += (_, _) => CalendarUi.NewEventNow();
        var title = new TextBlock { Text = "Calendar", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Foreground = CalendarVisuals.Palette("Text") };
        _header.Child = new Grid { Children = { title, _plus } };

        _miniTile.Child = _mini;
        _tasksTile.Child = _tasksSlot;
        SlotHost.Fill(_tasksSlot, SlotNames.CalendarSidebarTasks);
        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _stack };
        _frame.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _frame.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _frame.Children.Add(_header);
        Grid.SetRow(_scroll, 1);
        _frame.Children.Add(_scroll);
        _root.Child = _frame;
        Content = _root;
        AutomationProperties.SetAutomationId(this, "cal.sidebar");
        AutomationProperties.SetName(this, "Calendars");
        ApplyLook();
        Render();
        _look.PropertyChanged += (_, _) =>
        {
            ApplyLook();
            Render();
        };
        if (_tasks is not null)
        {
            _tasks.PropertyChanged += (_, e) =>
            {
                if (_look.IsBulwark && (string.Equals(e.PropertyName, nameof(TasksViewModel.Counts), StringComparison.Ordinal) || string.IsNullOrEmpty(e.PropertyName)))
                {
                    if (DispatcherQueue.HasThreadAccess)
                    {
                        Render();
                    }
                    else
                    {
                        _ = DispatcherQueue.TryEnqueue(Render);
                    }
                }
            };
        }
    }

    private void ApplyLook()
    {
        var bulwark = _look.IsBulwark;
        var bento = _look.IsBento && !bulwark;
        foreach (var tile in new[] { _miniTile, _calendarTile })
        {
            Tile(tile, bento);
        }

        Tile(_tasksTile, bento);
        _tasksTile.Visibility = bulwark ? Visibility.Collapsed : _tasksSlot.Visibility;
        _new.Height = bento ? 52 : 46;
        _new.CornerRadius = new CornerRadius(bento ? 20 : 16);
        _new.FontSize = 14.5;

        // the Bulwark column: the sidebar ground, a border at its end, the header, then the scrolled body (calendar-app.tsx:2573-2580)
        _root.Background = bulwark ? _look.SidebarBrush : null;
        _root.BorderBrush = CalendarVisuals.Palette("Line");
        _root.BorderThickness = bulwark ? new Thickness(0, 0, 1, 0) : new Thickness(0);
        _header.Visibility = bulwark ? Visibility.Visible : Visibility.Collapsed;
        _header.BorderBrush = CalendarVisuals.Palette("Line");
        _header.BorderThickness = new Thickness(0, 0, 0, 1);
        _header.Padding = new Thickness(12, 0, 8, 0);
        _header.Height = _look.CalToolbarHeight + 1;
        _plus.CornerRadius = _look.ButtonRadius;
        _stack.Spacing = bulwark ? 0 : 12;
        _stack.Padding = bulwark ? new Thickness(0, 12, 0, 12) : new Thickness(0);
        _scroll.Padding = bulwark ? new Thickness(0) : new Thickness(0, 0, 0, 12);
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
        var bulwark = _look.IsBulwark;
        var rows = _vm.Calendars;
        var children = new List<UIElement>();
        if (bulwark)
        {
            if (_vm.AvailableViews.Contains(CalendarViewKind.Tasks))
            {
                children.Add(TasksRow());
            }

            var mine = rows.Where(r => !r.IsShared).ToList();
            children.Add(Group("__mine__", "My calendars", mine, "cal.sidebar.add", "New calendar", "calendar.new-calendar"));
            var shared = rows.Where(r => r.IsShared).ToList();
            if (shared.Count > 0)
            {
                children.Add(new Border { Margin = new Thickness(0, 8, 0, 0), Child = Group("__others__", "Other calendars", shared, null, null, null) });
            }
        }
        else
        {
            children.Add(Section("Calendars", "cal.sidebar.add", "New calendar", "calendar.new-calendar"));
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
        }

        _calendars.Children.Clear();
        foreach (var child in children)
        {
            _calendars.Children.Add(child);
        }

        _stack.Children.Clear();
        _calendarTile.Child = null;
        if (bulwark)
        {
            _miniTile.Padding = new Thickness(12, 0, 12, 0);
            _calendars.Spacing = 0;
            _calendars.Margin = new Thickness(0, 16, 0, 0);
            _stack.Children.Add(_miniTile);
            _stack.Children.Add(_calendars);
            _tasksTile.Visibility = Visibility.Collapsed;
        }
        else
        {
            _calendars.Spacing = 2;
            _calendars.Margin = new Thickness(0);
            _calendarTile.Child = _calendars;
            _stack.Children.Add(_new);
            _stack.Children.Add(_miniTile);
            _stack.Children.Add(_calendarTile);
            _stack.Children.Add(_tasksTile);
            _tasksTile.Visibility = _tasksSlot.Visibility;
        }
    }

    // ---- Bulwark ----

    private static readonly SolidColorBrush Clear = new(Windows.UI.Color.FromArgb(0, 0, 0, 0));

    private static Brush Hover => CalendarVisuals.Palette("FeedbackHover");

    /// <summary>A header with its calendars under it (renderSection, calendar-sidebar-panel.tsx:446-470); a click on the header folds the group.</summary>
    private StackPanel Group(string key, string label, List<CalendarListRow> rows, string? plusId, string? plusName, string? command)
    {
        var open = !Collapsed.Contains(key);
        var panel = new StackPanel();
        var head = new Grid { Background = Clear, Padding = new Thickness(0, 0, 12, 0), ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } } };
        var toggle = new Button
        {
            Style = (Style)Application.Current.Resources["Button.Icon"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(0),
            Padding = new Thickness(12, 4, 0, 4),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    CalendarVisuals.Icon(open ? "ChevronDown" : "ChevronRight", 12, 2),
                    CalendarVisuals.Text(label, 12, FontWeights.Medium, CalendarVisuals.Palette("Dim")),
                },
            },
        };
        AutomationProperties.SetAutomationId(toggle, "cal.sidebar.group." + key);
        AutomationProperties.SetName(toggle, label);
        toggle.Click += (_, _) =>
        {
            if (!Collapsed.Remove(key))
            {
                Collapsed.Add(key);
            }

            Render();
        };
        head.Children.Add(toggle);
        if (plusId is not null && command is not null)
        {
            var plus = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Width = 32, Height = 32, CornerRadius = _look.ButtonRadius, Content = CalendarVisuals.Icon("Plus", 16, 2), Foreground = CalendarVisuals.Palette("Dim"), IsEnabled = CalendarUi.CanRun(command), Opacity = 0 };
            AutomationProperties.SetAutomationId(plus, plusId);
            AutomationProperties.SetName(plus, plusName ?? label);
            plus.Click += (_, _) => CalendarUi.RunCommand(command);
            Grid.SetColumn(plus, 1);
            head.Children.Add(plus);
            // opacity-0 group-hover:opacity-100 group-focus-within:opacity-100 (calendar-sidebar-panel.tsx:456)
            head.PointerEntered += (_, _) => plus.Opacity = 1;
            head.PointerExited += (_, _) => plus.Opacity = plus.FocusState == FocusState.Unfocused ? 0 : 1;
            plus.GotFocus += (_, _) => plus.Opacity = 1;
            plus.LostFocus += (_, _) => plus.Opacity = 0;
        }

        panel.Children.Add(head);
        if (open)
        {
            foreach (var row in rows)
            {
                panel.Children.Add(Row(row));
            }
        }

        return panel;
    }

    private Grid TasksRow()
    {
        var pending = _tasks is { Counts.Count: > 1 } t ? t.Counts[1] : 0;
        var overdue = _tasks is { Counts.Count: > 2 } o ? o.Counts[2] : 0;
        var current = _vm.View == CalendarViewKind.Tasks;
        var grid = new Grid { Background = Clear, MinHeight = 32, Padding = new Thickness(12, 4, 12, 4), ColumnSpacing = 8, Margin = new Thickness(0, 0, 0, 8), ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = GridLength.Auto } } };
        var icon = CalendarVisuals.Icon("List", 16, 1.8);
        icon.Foreground = CalendarVisuals.Palette("Dim");
        icon.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(icon);
        var label = CalendarVisuals.Text("Tasks", 14, FontWeights.Normal, CalendarVisuals.Palette("Text"));
        label.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        if (pending > 0)
        {
            var count = CalendarVisuals.Text(pending.ToString(System.Globalization.CultureInfo.InvariantCulture), 12, FontWeights.Normal, CalendarVisuals.Palette("Dim"));
            count.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(count, 2);
            grid.Children.Add(count);
        }

        if (overdue > 0)
        {
            var late = CalendarVisuals.Text(overdue.ToString(System.Globalization.CultureInfo.InvariantCulture) + " overdue", 12, FontWeights.Medium, CalendarVisuals.Palette("Destructive"));
            late.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(late, 3);
            grid.Children.Add(late);
        }

        grid.Background = current ? CalendarVisuals.Palette("FeedbackActive") : Clear;
        grid.PointerEntered += (_, _) => grid.Background = current ? CalendarVisuals.Palette("FeedbackActiveHover") : Hover;
        grid.PointerExited += (_, _) => grid.Background = current ? CalendarVisuals.Palette("FeedbackActive") : Clear;
        grid.Tapped += (_, _) => _vm.SetView(CalendarViewKind.Tasks);
        AutomationProperties.SetAutomationId(grid, "cal.sidebar.tasks");
        AutomationProperties.SetName(grid, "Tasks");
        return grid;
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
        var bulwark = _look.IsBulwark;
        var grid = new Grid
        {
            Height = bulwark ? double.NaN : 30,
            MinHeight = bulwark ? 32 : 0,
            Padding = bulwark ? new Thickness(20, 4, 12, 4) : new Thickness(0),
            ColumnSpacing = bulwark ? 8 : 10,
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } },
            Background = Clear,
        };
        var swatch = new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(bulwark ? 4 : 5),
            VerticalAlignment = VerticalAlignment.Center,
            Background = row.Visible ? CalendarVisuals.SolidBrush(row.Color) : null,
            BorderBrush = bulwark && !row.Visible ? CalendarVisuals.Palette("Faint") : CalendarVisuals.SolidBrush(row.Color),
            BorderThickness = new Thickness(bulwark ? 2 : 1.5),
            Child = row.Visible ? new Barbican.App.Controls.SIcon { Kind = "Check", Size = 10, StrokeWidth = bulwark ? 3.5 : 3, Foreground = bulwark ? CalendarVisuals.ReadableOn(row.Color) : CalendarVisuals.Palette("OnAccent"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } : null,
        };
        grid.Children.Add(swatch);
        var name = CalendarVisuals.Text(row.Name, bulwark ? 14 : 13, row.IsDefault && !bulwark ? FontWeights.SemiBold : FontWeights.Normal, CalendarVisuals.Palette(row.Visible ? (bulwark ? "Text" : "Body") : (bulwark ? "Dim" : "Faint")));
        name.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        var more = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Width = bulwark ? 32 : 24, Height = bulwark ? 32 : 24, CornerRadius = new CornerRadius(bulwark ? 6 : 8), Content = CalendarVisuals.Icon(bulwark ? "MoreVertical" : "More", bulwark ? 16 : 14, 2), Opacity = bulwark ? 0 : 0.7, Margin = bulwark ? new Thickness(0, -6, -8, -6) : new Thickness(0), Foreground = CalendarVisuals.Palette("Dim") };
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
        if (bulwark)
        {
            // hover:bg-muted; the options button shows on hover and on keyboard focus (calendar-sidebar-panel.tsx:354-380)
            grid.PointerEntered += (_, _) =>
            {
                grid.Background = Hover;
                more.Opacity = 1;
            };
            grid.PointerExited += (_, _) =>
            {
                grid.Background = Clear;
                more.Opacity = more.FocusState == FocusState.Unfocused ? 0 : 1;
            };
            more.GotFocus += (_, _) => more.Opacity = 1;
            more.LostFocus += (_, _) => more.Opacity = 0;
        }

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
