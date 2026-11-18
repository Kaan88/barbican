// Mirrors: M4 plan section 6.3 step 1 (CalendarScreen beside MailScreen in ShellPage: rail | calendar sidebar | surface | detail panel; the Mail screen keeps its state while hidden); design BNCalendar / NordCalendar (a tile per region in Bento, one rounded surface in Nordic);
// Bulwark components/calendar/calendar-app.tsx (swipe and horizontal wheel paging I-298, the load error notice I-301, the hover popover I-299); Rust app.slint calendar surface; docs/adr/0007 (every overlay is in-window); design/MOTION.md (the new view is on screen on the first frame, no blank frames).
// The four grids are layered in one host and switched by visibility, so a view switch never rebuilds the others and never shows an empty frame; each redraws from the model in one pass.

using Microsoft.UI.Xaml.Automation;
using System.ComponentModel;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.App.Views.Actions;
using Barbican.App.Views.Rail;
using Barbican.Presentation.Calendar.Grid;
using Barbican.Presentation.Shell;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

#pragma warning disable MA0051 // a drawing routine builds one frame in one pass (no flicker); splitting it would only pass the same locals around
namespace Barbican.App.Views.Calendar;

/// <summary>The Calendar app's screen.</summary>
public sealed class CalendarScreen : UserControl
{
    private readonly CalendarViewModel _vm = CalendarUi.Model;
    private readonly StyleState _look = ViewHost.Get<StyleState>();
    private readonly PanesViewModel _panes = ViewHost.Get<PanesViewModel>();
    private readonly Grid _root = new();
    private readonly RailView _rail = new();
    private readonly CalendarSidebarView _sidebar = new();
    private readonly PaneHandle _handle = new() { Column = "sidebar" };
    private readonly Border _surface = new();
    private readonly CalendarToolbarView _toolbar = new();
    private readonly TimeGridView _timeGrid = new();
    private readonly MonthGridView _month = new();
    private readonly AgendaView _agenda = new();
    private readonly ContentPresenter _tasks = new();
    private readonly TextBlock _tasksFallback = new() { Text = "The task list opens here.", FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _notice = new();
    private readonly EventDetailPanel _detail = new();
    private readonly Canvas _overlay = new();
    private readonly HoverCardView _hover = new();
    private readonly SearchResultsView _results = new();
    private readonly MoreMenuView _more = new();
    private readonly CalendarPrintView _print = new();
    private readonly Grid _content = new();
    private DispatcherTimerHandle? _hoverTimer;
    private (string Id, UIElement Source, Point At)? _pending;
    private DateTime _lastSwipe = DateTime.MinValue;
    private bool _loadedOnce;

    private static CalendarScreen? _current;

    private sealed class DispatcherTimerHandle(Microsoft.UI.Dispatching.DispatcherQueueTimer timer)
    {
        public Microsoft.UI.Dispatching.DispatcherQueueTimer Timer { get; } = timer;
    }

    public CalendarScreen()
    {
        _current = this;
        Build();
        _vm.PropertyChanged += OnModelChanged;
        _look.PropertyChanged += (_, _) =>
        {
            ApplyLook();
            Render();
        };
        _panes.PropertyChanged += (_, _) => ApplyLook();
        ApplyLook();
        Render();
    }

    /// <summary>The screen on show (automation reads it), or null before the first visit.</summary>
    public static CalendarScreen? Current => _current;

    /// <summary>The model the screen draws.</summary>
    public CalendarViewModel Model => _vm;

    /// <summary>The Day and Week grid (automation reads its state).</summary>
    public TimeGridView TimeGrid => _timeGrid;

    /// <summary>The Month grid.</summary>
    public MonthGridView MonthGrid => _month;

    /// <summary>The Agenda list.</summary>
    public AgendaView AgendaList => _agenda;

    /// <summary>The print preview.</summary>
    public CalendarPrintView PrintPreview => _print;

    /// <summary>The hover card.</summary>
    public HoverCardView HoverCard => _hover;

    /// <summary>The screen is shown (the rail opened Calendar): the first time loads the login's calendars.</summary>
    public void OnShown()
    {
        if (_vm.AccountKey is { } key && (!_loadedOnce || !_vm.IsLoadedFor(key)) && !_vm.IsLoading)
        {
            _loadedOnce = true;
            CalendarUi.Run(() => _vm.LoadAsync(CancellationToken.None));
        }

        Render();
    }

    private void Build()
    {
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _root.Children.Add(_rail);
        Grid.SetColumn(_sidebar, 1);
        _root.Children.Add(_sidebar);
        _handle.Target = _sidebar;
        Grid.SetColumn(_handle, 1);
        AutomationProperties.SetAutomationId(_handle, "pane.calendar-sidebar");
        _root.Children.Add(_handle);

        var column = new Grid { RowSpacing = 8 };
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        column.Children.Add(_toolbar);
        _notice.Visibility = Visibility.Collapsed;
        Grid.SetRow(_notice, 1);
        column.Children.Add(_notice);
        _content.Children.Add(_timeGrid);
        _content.Children.Add(_month);
        _content.Children.Add(_agenda);
        _tasks.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _tasks.VerticalContentAlignment = VerticalAlignment.Stretch;
        SlotHost.Fill(_tasks, SlotNames.CalendarTasks);
        if (_tasks.Visibility == Visibility.Collapsed)
        {
            // nobody fills the slot yet: the Tasks view shows a quiet line instead of an empty frame
            _tasks.Visibility = Visibility.Visible;
            _tasks.Content = _tasksFallback;
        }

        _content.Children.Add(_tasks);
        Grid.SetRow(_content, 2);
        column.Children.Add(_content);
        _content.SizeChanged += (_, e) => _vm.SetNarrow(e.NewSize.Width < 1000);
        _content.PointerWheelChanged += OnHorizontalWheel;
        _content.ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.System;
        _content.ManipulationCompleted += OnSwiped;
        _surface.Child = column;
        Grid.SetColumn(_surface, 2);
        _root.Children.Add(_surface);

        Grid.SetColumn(_detail, 3);
        _root.Children.Add(_detail);

        // the in-window layer above everything: hover card, search results, More menu, print preview
        _overlay.IsHitTestVisible = true;
        _overlay.Background = null;
        _hover.Entered += (_, _) => _hoverTimer?.Timer.Stop();
        _hover.Left += (_, _) => HideHover();
        _results.Picked += (_, _) => _results.Dismiss();
        _more.Picked += (_, _) => _more.Hide();
        _more.RefreshRequested += (_, _) => CalendarUi.Run(() => _vm.LoadAsync(CancellationToken.None));
        _more.PrintRequested += (_, _) => _print.Show();
        _toolbar.MoreRequested += (_, e) => ShowMore(e.At);
        _timeGrid.HoverRequested += (_, e) => RequestHover(e.Occurrence, e.Source, e.At);
        _timeGrid.HoverCleared += (_, _) => ClearHover();
        _month.HoverRequested += (_, e) => RequestHover(e.Occurrence, e.Source, e.At);
        _month.HoverCleared += (_, _) => ClearHover();
        PointerPressed += OnAnyPressed;
        var layers = new Grid();
        layers.Children.Add(_root);
        layers.Children.Add(_overlay);
        layers.Children.Add(_print);
        _overlay.Children.Add(_hover);
        _overlay.Children.Add(_results);
        _overlay.Children.Add(_more);
        Content = layers;
        AutomationProperties.SetAutomationId(this, "calendar");
        AutomationProperties.SetName(this, "Calendar");
    }

    // ---- look ----

    private void ApplyLook()
    {
        var layout = _look.Layout;
        var band = false;
#if WINDOWS
        band = true;
#endif
        var pad = _look.WindowPadding;
        Padding = band ? pad : new Thickness(pad.Left, pad.Bottom, pad.Right, pad.Bottom);
        _root.ColumnSpacing = _look.Gap;
        _sidebar.Visibility = layout.SidebarShown ? Visibility.Visible : Visibility.Collapsed;
        _handle.Visibility = _sidebar.Visibility;
        var width = _panes.SidebarWidth > 0 ? _panes.SidebarWidth : _look.SidebarWidth;
        _sidebar.Width = width;
        _surface.Background = _look.TileBrush;
        _surface.BorderBrush = CalendarVisuals.Palette("Line");
        _surface.BorderThickness = new Thickness(1);
        _surface.CornerRadius = _look.IsBento ? new CornerRadius(26) : _look.PaneRadius;
        _surface.Padding = new Thickness(20, 16, 20, 12);
        _detail.Visibility = _look.WindowWidth >= 1100 && _vm.Detail is not null ? Visibility.Visible : Visibility.Collapsed;
        _tasksFallback.Foreground = CalendarVisuals.Palette("Faint");
    }

    // ---- drawing ----

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Render();

    /// <summary>One pass over the whole screen from the model: toolbar, sidebar, the active grid, the notice, the detail panel, the results list.</summary>
    public void Render()
    {
        _toolbar.Render();
        _sidebar.Render();
        var view = _vm.View;
        var timed = view is CalendarViewKind.Day or CalendarViewKind.Week;
        _timeGrid.Visibility = timed ? Visibility.Visible : Visibility.Collapsed;
        _month.Visibility = view == CalendarViewKind.Month ? Visibility.Visible : Visibility.Collapsed;
        _agenda.Visibility = view == CalendarViewKind.Agenda ? Visibility.Visible : Visibility.Collapsed;
        _tasks.Visibility = view == CalendarViewKind.Tasks ? Visibility.Visible : Visibility.Collapsed;
        switch (view)
        {
            case CalendarViewKind.Day:
            case CalendarViewKind.Week:
                _timeGrid.Render();
                break;
            case CalendarViewKind.Month:
                _month.Render();
                break;
            case CalendarViewKind.Agenda:
                _agenda.Render();
                break;
            default:
                break;
        }

        RenderNotice();
        _detail.Render();
        _detail.Visibility = _look.WindowWidth >= 1100 && _vm.Detail is not null ? Visibility.Visible : Visibility.Collapsed;
        _results.Render();
        PositionResults();
        if (_print.Visibility == Visibility.Visible && _print.Document is not null)
        {
            _print.Show();
        }
    }

    // I-301: a load error says so and offers Retry (the grid keeps whatever was loaded)
    private void RenderNotice()
    {
        if (_vm.LoadError is not { } error)
        {
            _notice.Visibility = Visibility.Collapsed;
            return;
        }

        var row = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } } };
        var text = CalendarVisuals.Text("Couldn't load the calendar: " + error, 12.5, FontWeights.SemiBold, CalendarVisuals.Palette("RoseFg"));
        text.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAutomationId(text, "cal.error.text");
        row.Children.Add(text);
        var retry = new Button { Style = (Style)Application.Current.Resources["Button.Action"], Content = "Retry", Height = 30, CornerRadius = new CornerRadius(10), FontSize = 12.5 };
        AutomationProperties.SetAutomationId(retry, "cal.error.retry");
        retry.Click += (_, _) => CalendarUi.Run(() => _vm.LoadAsync(CancellationToken.None));
        Grid.SetColumn(retry, 1);
        row.Children.Add(retry);
        _notice.Child = row;
        _notice.Background = CalendarVisuals.Palette("RoseBg");
        _notice.CornerRadius = new CornerRadius(12);
        _notice.Padding = new Thickness(12, 6, 8, 6);
        _notice.Visibility = Visibility.Visible;
        AutomationProperties.SetAutomationId(_notice, "cal.error");
    }

    private void PositionResults()
    {
        if (_results.Visibility != Visibility.Visible)
        {
            return;
        }

        var at = _toolbar.SearchBox.TransformToVisual(_overlay).TransformPoint(new Point(0, _toolbar.SearchBox.ActualHeight + 6));
        Canvas.SetLeft(_results, Math.Max(8, at.X + _toolbar.SearchBox.ActualWidth - 300));
        Canvas.SetTop(_results, at.Y);
    }

    private void ShowMore(Point windowPoint)
    {
        var at = _overlay.TransformToVisual(null).TransformPoint(new Point(0, 0));
        _more.Show();
        _more.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_more, Math.Max(8, windowPoint.X - at.X - _more.DesiredSize.Width));
        Canvas.SetTop(_more, windowPoint.Y - at.Y + 6);
    }

    // a click anywhere else closes the More menu and the results list
    private void OnAnyPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_more.Visibility == Visibility.Visible && !IsOver(e.OriginalSource, _more))
        {
            _more.Hide();
        }

        if (_results.Visibility == Visibility.Visible && !IsOver(e.OriginalSource, _results) && !IsOver(e.OriginalSource, _toolbar.SearchBox))
        {
            _results.Dismiss();
        }
    }

    private static bool IsOver(object? source, DependencyObject target)
    {
        for (var node = source as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, target))
            {
                return true;
            }
        }

        return false;
    }

    // ---- hover preview (I-299) ----

    private void RequestHover(Barbican.Core.Calendar.CalendarOccurrence occurrence, UIElement source, Point at)
    {
        var delay = _vm.Settings.HoverDelayMs;
        if (delay < 0 || _more.Visibility == Visibility.Visible)
        {
            return;
        }

        if (string.Equals(_hover.ShownId, occurrence.Id, StringComparison.Ordinal))
        {
            _hoverTimer?.Timer.Stop();
            return;
        }

        if (_pending is { } p && string.Equals(p.Id, occurrence.Id, StringComparison.Ordinal))
        {
            return;
        }

        _pending = (occurrence.Id, source, at);
        if (_hoverTimer is null)
        {
            var timer = DispatcherQueue.CreateTimer();
            timer.IsRepeating = false;
            timer.Tick += (_, _) => OnHoverTick();
            _hoverTimer = new DispatcherTimerHandle(timer);
        }

        _hoverTimer.Timer.Stop();
        _hoverTimer.Timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, delay));
        _hoverTimer.Timer.Start();
    }

    private void ClearHover()
    {
        _pending = null;
        if (_hover.Visibility != Visibility.Visible)
        {
            _hoverTimer?.Timer.Stop();
            return;
        }

        // the pointer may be on its way into the card: give it a moment
        if (_hoverTimer is not null)
        {
            _hoverTimer.Timer.Stop();
            _hoverTimer.Timer.Interval = TimeSpan.FromMilliseconds(220);
            _hoverTimer.Timer.Start();
        }
    }

    private void OnHoverTick()
    {
        _hoverTimer?.Timer.Stop();
        if (_pending is not { } p)
        {
            HideHover();
            return;
        }

        _pending = null;
        var o = _vm.Occurrences.FirstOrDefault(x => string.Equals(x.Id, p.Id, StringComparison.Ordinal));
        if (o is null)
        {
            return;
        }

        _hover.Show(_vm.DetailOf(o));
        _hover.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var point = p.Source.TransformToVisual(_overlay).TransformPoint(p.At);
        var x = Math.Clamp(point.X + 14, 8, Math.Max(8, _overlay.ActualWidth - _hover.DesiredSize.Width - 8));
        var y = Math.Clamp(point.Y + 14, 8, Math.Max(8, _overlay.ActualHeight - _hover.DesiredSize.Height - 8));
        Canvas.SetLeft(_hover, x);
        Canvas.SetTop(_hover, y);
    }

    private void HideHover()
    {
        _hoverTimer?.Timer.Stop();
        _pending = null;
        _hover.Hide();
    }

    // ---- paging by wheel and swipe (I-298) ----

    private void OnHorizontalWheel(object sender, PointerRoutedEventArgs e)
    {
        var props = e.GetCurrentPoint(_content).Properties;
        if (!props.IsHorizontalMouseWheel || props.MouseWheelDelta == 0 || (DateTime.UtcNow - _lastSwipe).TotalMilliseconds < 300)
        {
            return;
        }

        _lastSwipe = DateTime.UtcNow;
        if (props.MouseWheelDelta > 0)
        {
            _vm.GoNext();
        }
        else
        {
            _vm.GoPrevious();
        }

        e.Handled = true;
    }

    private void OnSwiped(object sender, ManipulationCompletedRoutedEventArgs e)
    {
        var dx = e.Cumulative.Translation.X;
        if (Math.Abs(dx) < 90 || Math.Abs(dx) < Math.Abs(e.Cumulative.Translation.Y) * 1.5)
        {
            return;
        }

        if (dx < 0)
        {
            _vm.GoNext();
        }
        else
        {
            _vm.GoPrevious();
        }
    }

    /// <summary>Puts the keyboard in the search box.</summary>
    public void FocusSearch() => _toolbar.FocusSearch();
}
