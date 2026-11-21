// Mirrors: Rust app.slint:3116-3118 (Today, prev, next), calendar.rs:466-:489 (the title), Bulwark components/calendar/calendar-toolbar.tsx (:410-490 the row, :484 the view menu, :192 More), calendar-search.tsx and components/ui/toolbar.tsx:84-130 (the search field), ui/button.tsx:16,42-55 (xs outline, icon-sm); design BNCalendar (21 px title, a segmented view switch in tile2 with the current view on sel);
// B-CAL-053 (the title gets smaller when the grid is narrow); I-310 (Today, Prev, Next), I-311 (view switch), I-312 (More), I-313 (search).
// Bulwark: [sidebar toggle][Create when the sidebar is hidden][Today][prev][next] title (text-base semibold) ... [search][more (vertical dots)][view dropdown], an h-14 row with px-3 and a border below (calendar-toolbar.tsx:410-490).
// Bento and Nordic keep the toolbar they had (title left, segmented view buttons).

using Microsoft.UI.Xaml.Automation;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Calendar.Grid;
using Microsoft.UI.Text;
using Windows.Foundation;
using Windows.System;

#pragma warning disable MA0051 // a drawing routine builds one frame in one pass (no flicker); splitting it would only pass the same locals around
namespace Barbican.App.Views.Calendar;

/// <summary>The calendar's toolbar: title, paging, view switch, search, More.</summary>
public sealed class CalendarToolbarView : UserControl
{
    private readonly CalendarViewModel _vm = CalendarUi.Model;
    private readonly StyleState _look = ViewHost.Get<StyleState>();
    private readonly TextBlock _title = new() { FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel _views = new() { Orientation = Orientation.Horizontal };
    private readonly Border _viewBox = new();
    private readonly TextBox _search = new() { Style = (Style)Application.Current.Resources["TextBox.Bare"], PlaceholderText = "Search events", Width = 150, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
    private readonly Border _searchBox = new();
    private readonly Button _more;
    private readonly Button _viewButton;
    private readonly TextBlock _viewName = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _sidebarToggle;
    private readonly Button _create;
    private readonly Border _root = new();
    private bool _builtBulwark;
    private bool _updating;
    private double _width;

    public CalendarToolbarView()
    {
        _more = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Width = 34, Height = 34, CornerRadius = new CornerRadius(12), Content = CalendarVisuals.Icon("More", 18, 2), Foreground = CalendarVisuals.Palette("Dim") };
        AutomationProperties.SetAutomationId(_more, "cal.more");
        AutomationProperties.SetName(_more, "More");
        _more.Click += (_, _) => MoreRequested?.Invoke(this, new MoreRequestedEventArgs(_more.TransformToVisual(null).TransformPoint(new Point(_more.ActualWidth, _more.ActualHeight))));

        _sidebarToggle = Icon("cal.sidebar.toggle", "Show or hide the sidebar", CalendarVisuals.MenuIcon(16));
        _sidebarToggle.Click += (_, _) => SidebarToggled?.Invoke(this, EventArgs.Empty);
        _create = Icon("cal.create", "New event", CalendarVisuals.Icon("Plus", 16, 2));
        _create.Click += (_, _) => CalendarUi.NewEventNow();

        _viewButton = new Button { Style = (Style)Application.Current.Resources["Button.Action"], Height = 32, Padding = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        _viewButton.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _viewName, CalendarVisuals.Icon("ChevronDown", 12, 2) } };
        AutomationProperties.SetAutomationId(_viewButton, "cal.view.menu.button");
        _viewButton.Click += (_, _) => ViewMenuRequested?.Invoke(this, new MoreRequestedEventArgs(_viewButton.TransformToVisual(null).TransformPoint(new Point(_viewButton.ActualWidth, _viewButton.ActualHeight))));

        AutomationProperties.SetAutomationId(_search, "cal.search");
        AutomationProperties.SetName(_search, "Search events");
        _search.TextChanged += (_, _) =>
        {
            if (!_updating)
            {
                _vm.SearchText = _search.Text;
            }
        };
        _search.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape)
            {
                _search.Text = string.Empty;
                e.Handled = true;
            }
        };

        SizeChanged += (_, e) =>
        {
            _width = e.NewSize.Width;
            Render();
        };
        Content = _root;
        AutomationProperties.SetAutomationId(this, "cal.toolbar");
        AutomationProperties.SetAutomationId(_title, "cal.title");
        Render();
    }

    /// <summary>The view dropdown (the view menu opens under it).</summary>
    public FrameworkElement ViewMenuAnchor => _viewButton;

    /// <summary>The More button (its menu opens under it).</summary>
    public FrameworkElement MoreAnchor => _more;

    /// <summary>The search field's frame (the results list opens under it).</summary>
    public FrameworkElement SearchBox => _searchBox;

    /// <summary>Whether the screen's sidebar is shown (the Create button takes the sidebar's place while it is hidden, calendar-app.tsx:2404).</summary>
    public bool SidebarOpen
    {
        get => _sidebarOpen;
        set
        {
            if (_sidebarOpen != value)
            {
                _sidebarOpen = value;
                Render();
            }
        }
    }

    private bool _sidebarOpen = true;

    /// <summary>The More button was pressed: the point under it, in window coordinates.</summary>
    public event EventHandler<MoreRequestedEventArgs>? MoreRequested;

    /// <summary>The view dropdown was pressed: the point under it, in window coordinates.</summary>
    public event EventHandler<MoreRequestedEventArgs>? ViewMenuRequested;

    /// <summary>The sidebar toggle was pressed.</summary>
    public event EventHandler? SidebarToggled;

    private static Button Icon(string id, string name, UIElement icon)
    {
        var button = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Width = 32, Height = 32, Content = icon, Foreground = CalendarVisuals.Palette("Dim"), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(button, id);
        AutomationProperties.SetName(button, name);
        return button;
    }

    public void Render()
    {
        var bulwark = _look.IsBulwark;
        if (_root.Child is null || bulwark != _builtBulwark)
        {
            // the controls move to the new row: let go of the old one first
            if (_root.Child is Grid old)
            {
                old.Children.Clear();
            }

            if (_searchBox.Child is Panel inner)
            {
                inner.Children.Clear();
            }

            _searchBox.Child = null;
            _viewBox.Child = null;
            _builtBulwark = bulwark;
            _root.Child = bulwark ? BuildBulwark() : BuildLegacy();
        }

        var narrow = _width > 0 && _width < 760;
        _title.Text = narrow ? _vm.ShortTitle : _vm.Title;
        _title.Foreground = CalendarVisuals.Palette("Text");
        if (bulwark)
        {
            // text-base font-semibold (calendar-toolbar.tsx:453)
            _title.FontSize = 16;
            _title.FontWeight = FontWeights.SemiBold;
            _root.BorderBrush = CalendarVisuals.Palette("Line");
            _root.BorderThickness = new Thickness(0, 0, 0, 1);
            _root.Padding = _look.CalToolbarPadding;
            _root.Height = _look.CalToolbarHeight + 1;
            _viewName.Text = _vm.View.ToString();
            _viewName.FontSize = 14;
            _viewName.FontWeight = FontWeights.Medium;
            _viewName.Foreground = CalendarVisuals.Palette("Text");
            _viewButton.CornerRadius = _look.ButtonRadius;
            AutomationProperties.SetName(_viewButton, _vm.View + " view, choose a view");
            _create.Visibility = _sidebarOpen ? Visibility.Collapsed : Visibility.Visible;
            foreach (var button in new[] { _more, _sidebarToggle, _create })
            {
                button.CornerRadius = _look.ButtonRadius;
                button.Width = 32;
                button.Height = 32;
            }

            // w-40 lg:w-56 xl:w-64 (calendar-app.tsx:2411)
            var wide = _look.WindowWidth >= 1280 ? 256 : _look.WindowWidth >= 1024 ? 224 : 160;
            _searchBox.Width = narrow ? 160 : wide;
            // the Tasks view has no events to search (calendar-app.tsx:2410)
            _searchBox.Visibility = _vm.View == CalendarViewKind.Tasks ? Visibility.Collapsed : Visibility.Visible;
            _searchBox.Height = 32;
            _searchBox.CornerRadius = _look.FieldRadius;
            _searchBox.BorderBrush = CalendarVisuals.Palette("Input");
            _searchBox.Background = CalendarVisuals.Palette("Bg");
            _searchBox.Padding = new Thickness(10, 0, 8, 0);
            _search.Width = double.NaN;
            _search.HorizontalAlignment = HorizontalAlignment.Stretch;
            _search.FontSize = 14;
        }
        else
        {
            _root.BorderThickness = new Thickness(0);
            _root.Padding = new Thickness(0);
            _root.Height = double.NaN;
            _searchBox.Visibility = Visibility.Visible;
            _title.FontSize = narrow ? 17 : 21;
            _title.FontWeight = FontWeights.Bold;
            _viewBox.CornerRadius = new CornerRadius(12);
            _viewBox.Padding = new Thickness(3);
            _viewBox.BorderThickness = new Thickness(1);
            _viewBox.BorderBrush = CalendarVisuals.Palette("Line");
            _viewBox.Background = CalendarVisuals.Palette("Tile2");
            _viewBox.VerticalAlignment = VerticalAlignment.Center;
            _views.Children.Clear();
            foreach (var view in _vm.AvailableViews)
            {
                _views.Children.Add(ViewButton(view));
            }

            _search.Width = narrow ? 100 : 150;
        }

        if (!string.Equals(_search.Text, _vm.SearchText, StringComparison.Ordinal))
        {
            _updating = true;
            _search.Text = _vm.SearchText;
            _updating = false;
        }
    }

    private Grid BuildBulwark()
    {
        // [toggle][create][today][prev][next] title | search more view (gap-1 between the buttons, Today me-1, the title ms-2, the view menu ms-1)
        var grid = new Grid { ColumnSpacing = 4, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < 8; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 4 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        }

        Place(grid, _sidebarToggle, 0);
        Place(grid, _create, 1);
        var today = new Button { Style = (Style)Application.Current.Resources["Button.Action"], Content = "Today", Height = 32, Padding = new Thickness(10, 0, 10, 0), FontSize = 14, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 4, 0), CornerRadius = _look.ButtonRadius, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(today, "cal.today");
        AutomationProperties.SetName(today, "Today");
        today.Click += (_, _) => _vm.Today();
        Place(grid, today, 2);
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        nav.Children.Add(Nav("cal.prev", "Previous", CalendarVisuals.Icon("ChevronLeft", 16, 2), _vm.GoPrevious, _look.ButtonRadius));
        nav.Children.Add(Nav("cal.next", "Next", CalendarVisuals.Icon("ChevronRight", 16, 2), _vm.GoNext, _look.ButtonRadius));
        Place(grid, nav, 3);
        _title.Margin = new Thickness(8, 0, 0, 0);
        Place(grid, _title, 4);

        // the search field: a 16 px icon at 10 px, text from 32 (toolbar.tsx:83-95)
        var searchRow = new Grid { ColumnSpacing = 6, ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) } } };
        var glass = CalendarVisuals.Icon("Search", 16, 1.8);
        glass.Foreground = CalendarVisuals.Palette("Dim");
        searchRow.Children.Add(glass);
        Grid.SetColumn(_search, 1);
        searchRow.Children.Add(_search);
        _searchBox.BorderThickness = new Thickness(1);
        _searchBox.VerticalAlignment = VerticalAlignment.Center;
        _searchBox.Margin = new Thickness(0, 0, 4, 0);
        _searchBox.Child = searchRow;
        Place(grid, _searchBox, 5);
        _more.Content = CalendarVisuals.Icon("MoreVertical", 16, 2);
        Place(grid, _more, 6);
        _viewButton.Margin = new Thickness(4, 0, 0, 0);
        Place(grid, _viewButton, 7);
        return grid;
    }

    private Grid BuildLegacy()
    {
        var grid = new Grid { ColumnSpacing = 8, Height = 44 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 4; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        _title.Margin = new Thickness(0);
        grid.Children.Add(_title);
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        nav.Children.Add(Nav("cal.prev", "Previous", CalendarVisuals.Icon("ChevronLeft", 16, 2), _vm.GoPrevious, new CornerRadius(10)));
        var today = new Button { Style = (Style)Application.Current.Resources["Button.Chip"], Content = "Today", Height = 30 };
        AutomationProperties.SetAutomationId(today, "cal.today");
        AutomationProperties.SetName(today, "Today");
        today.Click += (_, _) => _vm.Today();
        nav.Children.Add(today);
        nav.Children.Add(Nav("cal.next", "Next", CalendarVisuals.Icon("ChevronRight", 16, 2), _vm.GoNext, new CornerRadius(10)));
        Place(grid, nav, 1);
        _viewBox.Child = _views;
        Place(grid, _viewBox, 2);

        _searchBox.CornerRadius = new CornerRadius(12);
        _searchBox.BorderThickness = new Thickness(1);
        _searchBox.BorderBrush = CalendarVisuals.Palette("Line");
        _searchBox.Background = CalendarVisuals.Palette("Tile2");
        _searchBox.Padding = new Thickness(10, 0, 8, 0);
        _searchBox.Height = 34;
        _searchBox.Width = double.NaN;
        _searchBox.Margin = new Thickness(0);
        _searchBox.VerticalAlignment = VerticalAlignment.Center;
        _search.FontSize = 13;
        _search.HorizontalAlignment = HorizontalAlignment.Left;
        _searchBox.Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { CalendarVisuals.Icon("Search", 15, 1.8), _search } };
        Place(grid, _searchBox, 3);
        _more.CornerRadius = new CornerRadius(12);
        _more.Width = 34;
        _more.Height = 34;
        _more.Content = CalendarVisuals.Icon("More", 18, 2);
        Place(grid, _more, 4);
        return grid;
    }

    private static void Place(Grid grid, FrameworkElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private Button ViewButton(CalendarViewKind view)
    {
        var current = _vm.View == view;
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["Button.Icon"],
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10, 5, 10, 5),
            Background = current ? CalendarVisuals.Palette("Sel") : null,
            Content = CalendarVisuals.Text(view.ToString(), 12.5, FontWeights.SemiBold, CalendarVisuals.Palette(current ? "SelText" : "Dim")),
        };
        AutomationProperties.SetAutomationId(button, "cal.view." + CalendarViewModel.ViewName(view));
        AutomationProperties.SetName(button, view + " view");
        button.Click += (_, _) => _vm.SetView(view);
        return button;
    }

    private static Button Nav(string id, string name, UIElement icon, Action act, CornerRadius radius)
    {
        var button = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Width = 30, Height = 30, CornerRadius = radius, Content = icon, Foreground = CalendarVisuals.Palette("Dim") };
        if (radius.TopLeft <= 6)
        {
            button.Width = 32;
            button.Height = 32;
        }

        AutomationProperties.SetAutomationId(button, id);
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => act();
        return button;
    }

    /// <summary>Puts the keyboard in the search box (the "search" command).</summary>
    public void FocusSearch() => _ = _search.Focus(FocusState.Programmatic);
}
