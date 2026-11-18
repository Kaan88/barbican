// Mirrors: Rust app.slint:3116-3118 (Today, prev, next), calendar.rs:466-:489 (the title), Bulwark components/calendar/calendar-toolbar.tsx (:484 view switch, search, More menu), calendar-search.tsx; design BNCalendar (21 px title, a segmented view switch in tile2 with the current view on sel);
// B-CAL-053 (the title gets smaller when the grid is narrow); I-310 (Today, Prev, Next), I-311 (view switch), I-312 (More), I-313 (search).

using Microsoft.UI.Xaml.Automation;
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
    private readonly TextBlock _title = new() { FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel _views = new() { Orientation = Orientation.Horizontal };
    private readonly Border _viewBox = new();
    private readonly TextBox _search = new() { Style = (Style)Application.Current.Resources["TextBox.Bare"], PlaceholderText = "Search events", Width = 150, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
    private readonly Button _more;
    private bool _updating;
    private double _width;

    public CalendarToolbarView()
    {
        var grid = new Grid { ColumnSpacing = 8, Height = 44 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 4; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        grid.Children.Add(_title);
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        nav.Children.Add(Nav("cal.prev", "Previous", CalendarVisuals.Icon("ChevronLeft", 16, 2), _vm.GoPrevious));
        var today = new Button { Style = (Style)Application.Current.Resources["Button.Chip"], Content = "Today", Height = 30 };
        AutomationProperties.SetAutomationId(today, "cal.today");
        AutomationProperties.SetName(today, "Today");
        today.Click += (_, _) => _vm.Today();
        nav.Children.Add(today);
        nav.Children.Add(Nav("cal.next", "Next", CalendarVisuals.Icon("ChevronRight", 16, 2), _vm.GoNext));
        Grid.SetColumn(nav, 1);
        grid.Children.Add(nav);

        Grid.SetColumn(_viewBox, 2);
        _viewBox.Child = _views;
        grid.Children.Add(_viewBox);

        var searchBox = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = CalendarVisuals.Palette("Line"),
            Background = CalendarVisuals.Palette("Tile2"),
            Padding = new Thickness(10, 0, 8, 0),
            Height = 34,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { CalendarVisuals.Icon("Search", 15, 1.8), _search } },
        };
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
        Grid.SetColumn(searchBox, 3);
        grid.Children.Add(searchBox);
        SearchBox = searchBox;

        _more = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Width = 34, Height = 34, CornerRadius = new CornerRadius(12), Content = CalendarVisuals.Icon("More", 18, 2), Foreground = CalendarVisuals.Palette("Dim") };
        AutomationProperties.SetAutomationId(_more, "cal.more");
        AutomationProperties.SetName(_more, "More");
        _more.Click += (_, _) => MoreRequested?.Invoke(this, new MoreRequestedEventArgs(_more.TransformToVisual(null).TransformPoint(new Point(_more.ActualWidth, _more.ActualHeight))));
        Grid.SetColumn(_more, 4);
        grid.Children.Add(_more);

        SizeChanged += (_, e) =>
        {
            _width = e.NewSize.Width;
            Render();
        };
        Content = grid;
        AutomationProperties.SetAutomationId(this, "cal.toolbar");
        AutomationProperties.SetAutomationId(_title, "cal.title");
        Render();
    }

    /// <summary>The search field's frame (the results list opens under it).</summary>
    public FrameworkElement SearchBox { get; }

    /// <summary>The More button was pressed: the point under it, in window coordinates.</summary>
    public event EventHandler<MoreRequestedEventArgs>? MoreRequested;

    public void Render()
    {
        var narrow = _width > 0 && _width < 760;
        _title.Text = narrow ? _vm.ShortTitle : _vm.Title;
        _title.FontSize = narrow ? 17 : 21;
        _title.Foreground = CalendarVisuals.Palette("Text");
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

        if (!string.Equals(_search.Text, _vm.SearchText, StringComparison.Ordinal))
        {
            _updating = true;
            _search.Text = _vm.SearchText;
            _updating = false;
        }

        _search.Width = narrow ? 100 : 150;
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

    private static Button Nav(string id, string name, UIElement icon, Action act)
    {
        var button = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Width = 30, Height = 30, CornerRadius = new CornerRadius(10), Content = icon, Foreground = CalendarVisuals.Palette("Dim") };
        AutomationProperties.SetAutomationId(button, id);
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => act();
        return button;
    }

    /// <summary>Puts the keyboard in the search box (the "search" command).</summary>
    public void FocusSearch() => _ = _search.Focus(FocusState.Programmatic);
}
