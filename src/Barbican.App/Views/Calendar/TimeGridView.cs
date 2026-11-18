// Mirrors: Rust calendar.rs:445-:620 render (day columns, all-day heads, timed events with lanes, 56 px hours, now line, past dimming, tinted cards), app.slint calendar grid; Bulwark components/calendar/calendar-week-view.tsx, calendar-day-view.tsx
// (hour slots, the all-day strip with its three-row cap and show-more toggle, drag move, resize handles and drag-to-create, 15-minute steps), lib/calendar/event-drag-grab.ts; B-CAL-008, 009, 052, 056, 059; G-003; I-318, I-321..I-323.
// One pointer handler on the hour surface does the hit test itself (the cards draw but are not hit-testable), so a click, a double click, a right click, a move, a resize and a create all take the same road as the automation commands (CalendarUi, ICalendarGestures).

using Microsoft.UI.Xaml.Automation;
using System.Globalization;
using Barbican.Presentation.Calendar.Grid;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

#pragma warning disable MA0051 // a drawing routine builds one frame in one pass (no flicker); splitting it would only pass the same locals around
namespace Barbican.App.Views.Calendar;

/// <summary>The Day and Week grids: day heads, the all-day strip, the hour surface.</summary>
public sealed class TimeGridView : UserControl
{
    private const double Gutter = 52;
    private const double ResizeZone = 7;
    private const double DragThreshold = 4;

    private readonly CalendarViewModel _vm = CalendarUi.Model;
    private readonly Grid _head = new() { Height = 44 };
    private readonly Grid _strip = new();
    private readonly Border _stripBox = new();
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Disabled, VerticalScrollMode = ScrollMode.Auto };
    private readonly Canvas _surface = new() { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)) };
    private readonly List<Placed> _placed = [];
    private IReadOnlyList<DateOnly> _columns = [];
    private double _width;
    private bool _scrolled;
    private Drag? _drag;

    private sealed record Placed(Rect Rect, TimedBox Box);

    private sealed class Drag
    {
        public required Mode Kind { get; init; }

        public required Point Start { get; init; }

        public Placed? Hit { get; init; }

        public DateTime Grab { get; init; }

        public bool Moved { get; set; }

        public Point Now { get; set; }

        public Border? Ghost { get; set; }
    }

    private enum Mode
    {
        Move,
        Resize,
        Create,
    }

    public TimeGridView()
    {
        var root = new Grid { RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = new GridLength(1, GridUnitType.Star) } } };
        root.Children.Add(_head);
        _stripBox.Child = _strip;
        Grid.SetRow(_stripBox, 1);
        root.Children.Add(_stripBox);
        _surface.PointerPressed += OnPressed;
        _surface.PointerMoved += OnMoved;
        _surface.PointerReleased += OnReleased;
        _surface.PointerCanceled += OnCanceled;
        _surface.PointerExited += (_, _) => HoverCleared?.Invoke(this, EventArgs.Empty);
        _surface.DoubleTapped += OnDoubleTapped;
        _surface.SizeChanged += (_, e) =>
        {
            if (Math.Abs(e.NewSize.Width - _width) > 0.5)
            {
                _width = e.NewSize.Width;
                RenderSurface();
            }
        };
        _scroll.Content = _surface;
        Grid.SetRow(_scroll, 2);
        root.Children.Add(_scroll);
        Content = root;
        AutomationProperties.SetAutomationId(this, "cal.grid.time");
        AutomationProperties.SetAutomationId(_surface, "cal.surface");
        AutomationProperties.SetAutomationId(_head, "cal.heads");
    }

    /// <summary>The pointer rests on an event card (the hover preview): the occurrence and the point in this view's coordinates.</summary>
    public event EventHandler<HoverRequestedEventArgs>? HoverRequested;

    public event EventHandler? HoverCleared;

    /// <summary>Draws the model's current state; one pass, nothing is cleared before the new children are ready.</summary>
    public void Render()
    {
        _columns = _vm.Columns;
        var settings = _vm.Settings;
        RenderHead();
        RenderStrip(settings);
        _surface.Height = (settings.HourTo - settings.HourFrom) * CalendarGridRules.HourPx;
        RenderSurface();
        if (!_scrolled && _width > 0)
        {
            _scrolled = true;
            var hour = Math.Max(settings.HourFrom, Math.Min(settings.HourTo - 1, 8)) - settings.HourFrom;
            _ = DispatcherQueue.TryEnqueue(() => _scroll.ChangeView(null, hour * CalendarGridRules.HourPx, null, disableAnimation: true));
        }
    }

    private void RenderHead()
    {
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Gutter) });
        var today = _vm.TodayDate;
        var n = _columns.Count;
        for (var i = 0; i < n; i++)
        {
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var day = _columns[i];
            var isToday = day == today;
            var label = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 1, Orientation = Orientation.Horizontal };
            label.Children.Add(CalendarVisuals.Text(day.ToString("ddd", CultureInfo.InvariantCulture), 12, FontWeights.SemiBold, CalendarVisuals.Palette(isToday ? "Accent" : "Dim")));
            var number = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Margin = new Thickness(6, 0, 0, 0),
                Background = isToday ? CalendarVisuals.Palette("Accent") : null,
                Child = new TextBlock
                {
                    Text = day.Day.ToString(CultureInfo.InvariantCulture),
                    FontSize = 13,
                    FontWeight = isToday ? FontWeights.ExtraBold : FontWeights.SemiBold,
                    Foreground = CalendarVisuals.Palette(isToday ? "OnAccent" : "Body"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            label.Children.Add(number);
            var button = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Content = label, CornerRadius = new CornerRadius(10), Tag = day };
            AutomationProperties.SetAutomationId(button, "cal.head." + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            AutomationProperties.SetName(button, day.ToString("dddd d MMMM", CultureInfo.InvariantCulture));
            button.Click += (_, _) => _vm.OpenDay(day);
            Grid.SetColumn(button, i + 1);
            head.Children.Add(button);
        }

        if (_vm.HiddenDayCount > 0)
        {
            var pill = new Border { CornerRadius = new CornerRadius(9), Background = CalendarVisuals.Palette("Chip"), Padding = new Thickness(6, 2, 6, 2), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = CalendarVisuals.Text(_vm.HiddenDayCount.ToString(CultureInfo.InvariantCulture) + " hidden", 10.5, FontWeights.SemiBold, CalendarVisuals.Palette("Dim")) };
            AutomationProperties.SetAutomationId(pill, "cal.hidden-days");
            head.Children.Add(pill);
        }

        head.BorderBrush = CalendarVisuals.Palette("Line");
        head.BorderThickness = new Thickness(0, 0, 0, 1);
        _head.Children.Clear();
        _head.ColumnDefinitions.Clear();
        _head.Children.Add(head);
    }

    private void RenderStrip(CalendarViewSettings settings)
    {
        var (bars, rows) = WeekLayout.AllDay(_columns, _vm.Occurrences);
        var visible = CalendarGridRules.AllDayVisibleRows(rows, _vm.AllDayExpanded);
        var height = CalendarGridRules.AllDayHeight(visible);
        var strip = new Grid { Height = height };
        strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Gutter) });
        for (var i = 0; i < _columns.Count; i++)
        {
            strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var r = 0; r < Math.Max(1, visible); r++)
        {
            strip.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
        }

        var calendars = _vm.Calendars.ToDictionary(c => c.Id, c => c.Color, StringComparer.Ordinal);
        foreach (var bar in bars.Where(b => b.Row < visible))
        {
            var o = bar.Occurrence;
            var hex = calendars.TryGetValue(o.Calendar, out var c) ? c : "#3b82f6";
            var chip = CalendarVisuals.Card(o, hex, settings, string.Equals(o.Id, _vm.SelectedId, StringComparison.Ordinal), o.End <= _vm.Now(), false, 7);
            chip.Padding = new Thickness(7, 1, 7, 1);
            chip.Margin = new Thickness(2, 1, 2, 1);
            chip.IsHitTestVisible = true;
            chip.Tag = o.Id;
            AutomationProperties.SetAutomationId(chip, "cal.allday." + o.Id);
            AutomationProperties.SetName(chip, o.Title);
            chip.Tapped += (_, e) =>
            {
                CalendarUi.Select(o.Id);
                e.Handled = true;
            };
            chip.DoubleTapped += (_, e) =>
            {
                CalendarUi.Open(o.Id);
                e.Handled = true;
            };
            chip.RightTapped += (_, e) =>
            {
                CalendarUi.Select(o.Id);
                var p = e.GetPosition(null);
                CalendarUi.ShowMenu(CalendarMenuIds.Event, p.X, p.Y, o.Id);
                e.Handled = true;
            };
            chip.PointerEntered += (_, e) => HoverRequested?.Invoke(this, new HoverRequestedEventArgs(o, chip, e.GetCurrentPoint(chip).Position));
            chip.PointerExited += (_, _) => HoverCleared?.Invoke(this, EventArgs.Empty);
            Grid.SetColumn(chip, bar.Column + 1);
            Grid.SetColumnSpan(chip, bar.Span);
            Grid.SetRow(chip, bar.Row);
            strip.Children.Add(chip);
        }

        if (CalendarGridRules.AllDayExpandable(rows))
        {
            var hidden = rows - CalendarGridRules.AllDayDefaultMaxRows;
            var toggle = new Button
            {
                Style = (Style)Application.Current.Resources["Button.Icon"],
                Content = CalendarVisuals.Text(_vm.AllDayExpanded ? "Less" : "+" + hidden.ToString(CultureInfo.InvariantCulture) + " more", 11, FontWeights.SemiBold, CalendarVisuals.Palette("Dim")),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
                Height = 24,
                CornerRadius = new CornerRadius(8),
            };
            AutomationProperties.SetAutomationId(toggle, "cal.allday.toggle");
            AutomationProperties.SetName(toggle, _vm.AllDayExpanded ? "Show fewer all-day events" : "Show all all-day events");
            toggle.Click += (_, _) => _vm.ToggleAllDay();
            Grid.SetColumn(toggle, 0);
            Grid.SetRow(toggle, Math.Max(0, visible - 1));
            strip.Children.Add(toggle);
        }

        _stripBox.BorderBrush = CalendarVisuals.Palette("Line");
        _stripBox.BorderThickness = new Thickness(0, 0, 0, 1);
        _strip.Children.Clear();
        _strip.RowDefinitions.Clear();
        _strip.ColumnDefinitions.Clear();
        _stripBox.Child = strip;
    }

    private void RenderSurface()
    {
        var settings = _vm.Settings;
        var width = _width;
        if (width <= Gutter + 1 || _columns.Count == 0)
        {
            return;
        }

        var colW = (width - Gutter) / _columns.Count;
        var hours = settings.HourTo - settings.HourFrom;
        var line = CalendarVisuals.Palette("Line");
        var children = new List<UIElement>();
        for (var h = 0; h <= hours; h++)
        {
            var y = h * CalendarGridRules.HourPx;
            var rule = new Border { Width = width - Gutter, Height = 1, Background = line, IsHitTestVisible = false };
            Canvas.SetLeft(rule, Gutter);
            Canvas.SetTop(rule, y);
            children.Add(rule);
            if (h < hours)
            {
                var label = CalendarVisuals.Text(CalendarFormat.HourLabel(settings.HourFrom + h, settings.Core.TwelveHour), 11, FontWeights.Normal, CalendarVisuals.Palette("Faint"));
                Canvas.SetLeft(label, 4);
                Canvas.SetTop(label, y + (h == 0 ? 2 : -8));
                children.Add(label);
            }
        }

        for (var i = 0; i < _columns.Count; i++)
        {
            var sep = new Border { Width = 1, Height = hours * CalendarGridRules.HourPx, Background = line, IsHitTestVisible = false };
            Canvas.SetLeft(sep, Gutter + (i * colW));
            children.Add(sep);
        }

        _placed.Clear();
        var calendars = _vm.Calendars.ToDictionary(c => c.Id, c => c.Color, StringComparer.Ordinal);
        var now = _vm.Now();
        foreach (var box in WeekLayout.Timed(_columns, _vm.Occurrences, settings.HourFrom, settings.HourTo).OrderBy(b => b.Lane))
        {
            var (laneLeft, laneW) = WeekLayout.Slot(box.Lane, box.Lanes, colW);
            var rect = new Rect(Gutter + (box.Column * colW) + laneLeft + 2, box.Top + 1, Math.Max(8, laneW - 5), Math.Max(10, box.Height - 2));
            _placed.Add(new Placed(rect, box));
            var o = box.Occurrence;
            var hex = calendars.TryGetValue(o.Calendar, out var c) ? c : "#3b82f6";
            var drawing = _drag is { Kind: Mode.Move, Moved: true, Hit: { } hit } && ReferenceEquals(hit.Box.Occurrence, o);
            var card = CalendarVisuals.Card(o, hex, settings, string.Equals(o.Id, _vm.SelectedId, StringComparison.Ordinal), o.End <= now, rect.Height >= 34);
            card.Width = rect.Width;
            card.Height = rect.Height;
            card.Opacity = drawing ? 0.35 : card.Opacity;
            Canvas.SetLeft(card, rect.X);
            Canvas.SetTop(card, rect.Y);
            AutomationProperties.SetAutomationId(card, "cal.event." + o.Id);
            if (laneW > (colW / box.Lanes) + 0.5)
            {
                // a cascaded lane covers part of the one before it: a card of the tinted (translucent) kind gets an opaque ground so the titles do not show through each other (D-M5-I05)
                var ground = new Border { Width = rect.Width, Height = rect.Height, CornerRadius = card.CornerRadius, Background = CalendarVisuals.Palette("Tile"), BorderBrush = CalendarVisuals.Palette("Line"), BorderThickness = new Thickness(1), IsHitTestVisible = false };
                Canvas.SetLeft(ground, rect.X);
                Canvas.SetTop(ground, rect.Y);
                children.Add(ground);
            }

            children.Add(card);
        }

        var todayIndex = _columns.ToList().IndexOf(_vm.TodayDate);
        if (todayIndex >= 0)
        {
            var minutes = (now - _vm.TodayDate.ToDateTime(TimeOnly.MinValue)).TotalMinutes;
            var y = (minutes / 60.0 * CalendarGridRules.HourPx) - (settings.HourFrom * CalendarGridRules.HourPx);
            if (y >= 0 && y <= hours * CalendarGridRules.HourPx)
            {
                var accent = CalendarVisuals.Palette("Accent");
                var nowLine = new Border { Width = colW, Height = 2, Background = accent, IsHitTestVisible = false };
                Canvas.SetLeft(nowLine, Gutter + (todayIndex * colW));
                Canvas.SetTop(nowLine, y - 1);
                var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = accent, IsHitTestVisible = false };
                Canvas.SetLeft(dot, Gutter + (todayIndex * colW) - 5);
                Canvas.SetTop(dot, y - 5);
                AutomationProperties.SetAutomationId(nowLine, "cal.now");
                children.Add(nowLine);
                children.Add(dot);
            }
        }

        if (_drag is { Moved: true } d)
        {
            var ghost = Ghost(d, colW, settings);
            if (ghost is not null)
            {
                children.Add(ghost);
            }
        }

        _surface.Children.Clear();
        foreach (var child in children)
        {
            _surface.Children.Add(child);
        }
    }

    // ---- geometry ----

    private double ColumnWidth => _columns.Count == 0 ? 1 : (_width - Gutter) / _columns.Count;

    private int ColumnAt(double x) => Math.Clamp((int)Math.Floor((x - Gutter) / ColumnWidth), 0, Math.Max(0, _columns.Count - 1));

    /// <summary>The time under a point of the surface on column <paramref name="column"/>.</summary>
    private DateTime TimeAt(double y, int column)
    {
        var hours = (y / CalendarGridRules.HourPx) + _vm.Settings.HourFrom;
        return _columns[column].ToDateTime(TimeOnly.MinValue).AddHours(Math.Clamp(hours, 0, 24));
    }

    private Placed? HitEvent(Point p)
    {
        for (var i = _placed.Count - 1; i >= 0; i--)
        {
            if (_placed[i].Rect.Contains(p))
            {
                return _placed[i];
            }
        }

        return null;
    }

    // ---- pointer ----

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_surface);
        var p = point.Position;
        if (_columns.Count == 0 || p.X < Gutter)
        {
            return;
        }

        var hit = HitEvent(p);
        if (point.Properties.IsRightButtonPressed)
        {
            var win = e.GetCurrentPoint(null).Position;
            if (hit is not null)
            {
                CalendarUi.Select(hit.Box.Occurrence.Id);
                CalendarUi.ShowMenu(CalendarMenuIds.Event, win.X, win.Y, hit.Box.Occurrence.Id);
            }
            else
            {
                var slot = CalendarUi.Gestures.Snap(TimeAt(p.Y, ColumnAt(p.X)));
                CalendarUi.ShowMenu(CalendarMenuIds.Slot, win.X, win.Y, slot: slot);
            }

            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (hit is not null)
        {
            CalendarUi.Select(hit.Box.Occurrence.Id);
            if (!CalendarUi.Gestures.CanDrag(hit.Box.Occurrence.Id))
            {
                return;
            }

            var resizing = hit.Rect.Bottom - p.Y <= ResizeZone;
            var col = ColumnAt(p.X);
            _drag = new Drag { Kind = resizing ? Mode.Resize : Mode.Move, Start = p, Hit = hit, Grab = TimeAt(p.Y, col), Now = p };
        }
        else
        {
            var col = ColumnAt(p.X);
            _drag = new Drag { Kind = Mode.Create, Start = p, Grab = TimeAt(p.Y, col), Now = p };
        }

        _ = _surface.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(_surface).Position;
        if (_drag is not { } drag)
        {
            if (HitEvent(p) is { } over)
            {
                HoverRequested?.Invoke(this, new HoverRequestedEventArgs(over.Box.Occurrence, _surface, p));
            }
            else
            {
                HoverCleared?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        drag.Now = p;
        if (!drag.Moved && (Math.Abs(p.X - drag.Start.X) > DragThreshold || Math.Abs(p.Y - drag.Start.Y) > DragThreshold))
        {
            drag.Moved = true;
            HoverCleared?.Invoke(this, EventArgs.Empty);
        }

        if (drag.Moved)
        {
            RenderSurface();
        }
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        _surface.ReleasePointerCapture(e.Pointer);
        if (_drag is not { } drag)
        {
            return;
        }

        _drag = null;
        var p = e.GetCurrentPoint(_surface).Position;
        if (!drag.Moved)
        {
            RenderSurface();
            if (drag.Kind == Mode.Create)
            {
                var slot = CalendarUi.Gestures.Snap(TimeAt(drag.Start.Y, ColumnAt(drag.Start.X)));
                CalendarUi.QuickCreate(slot, slot, allDay: false);
            }

            return;
        }

        switch (drag.Kind)
        {
            case Mode.Move:
            {
                var o = drag.Hit!.Box.Occurrence;
                var col = ColumnAt(p.X);
                var newStart = TimeAt(p.Y, col) - (drag.Grab - o.Start);
                newStart = new DateTime(_columns[col].Year, _columns[col].Month, _columns[col].Day, 0, 0, 0, DateTimeKind.Unspecified) + (newStart - newStart.Date);
                CalendarUi.Run(() => CalendarUi.Gestures.MoveAsync(o.Id, newStart, CancellationToken.None));
                break;
            }

            case Mode.Resize:
            {
                var o = drag.Hit!.Box.Occurrence;
                var day = _columns[Math.Clamp(drag.Hit.Box.Column, 0, _columns.Count - 1)];
                var end = day.ToDateTime(TimeOnly.MinValue).AddHours(Math.Clamp((p.Y / CalendarGridRules.HourPx) + _vm.Settings.HourFrom, 0, 24));
                CalendarUi.Run(() => CalendarUi.Gestures.ResizeAsync(o.Id, end, CancellationToken.None));
                break;
            }

            default:
            {
                var a = TimeAt(drag.Start.Y, ColumnAt(drag.Start.X));
                var b = TimeAt(p.Y, ColumnAt(p.X));
                CalendarUi.QuickCreate(a <= b ? a : b, a <= b ? b : a, allDay: false);
                break;
            }
        }

        RenderSurface();
    }

    private void OnCanceled(object sender, PointerRoutedEventArgs e)
    {
        _drag = null;
        RenderSurface();
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var p = e.GetPosition(_surface);
        if (p.X < Gutter || _columns.Count == 0)
        {
            return;
        }

        if (HitEvent(p) is { } hit)
        {
            CalendarUi.Open(hit.Box.Occurrence.Id);
            return;
        }

        var slot = CalendarUi.Gestures.Snap(TimeAt(p.Y, ColumnAt(p.X)));
        CalendarUi.NewEvent(slot, slot.AddHours(1), allDay: false);
    }

    private Border? Ghost(Drag drag, double colW, CalendarViewSettings settings)
    {
        var p = drag.Now;
        switch (drag.Kind)
        {
            case Mode.Move:
            {
                var o = drag.Hit!.Box.Occurrence;
                var col = ColumnAt(p.X);
                var start = CalendarUi.Gestures.Snap(TimeAt(p.Y, col) - (drag.Grab - o.Start));
                var y = ((start.TimeOfDay.TotalHours - settings.HourFrom) * CalendarGridRules.HourPx);
                return Outline(Gutter + (col * colW) + 2, y, colW - 5, Math.Max(14, (o.End - o.Start).TotalHours * CalendarGridRules.HourPx), CalendarFormat.Time(start, settings.Core.TwelveHour) + " – " + CalendarFormat.Time(start + (o.End - o.Start), settings.Core.TwelveHour));
            }

            case Mode.Resize:
            {
                var r = drag.Hit!.Rect;
                var end = CalendarUi.Gestures.Snap(TimeAt(p.Y, Math.Clamp(drag.Hit.Box.Column, 0, _columns.Count - 1)));
                return Outline(r.X, r.Y, r.Width, Math.Max(14, p.Y - r.Y), "until " + CalendarFormat.Time(end, settings.Core.TwelveHour));
            }

            default:
            {
                var col = ColumnAt(drag.Start.X);
                var a = Math.Min(drag.Start.Y, p.Y);
                var b = Math.Max(drag.Start.Y, p.Y);
                return Outline(Gutter + (col * colW) + 2, a, colW - 5, Math.Max(14, b - a), CalendarFormat.Time(CalendarUi.Gestures.Snap(TimeAt(a, col)), settings.Core.TwelveHour) + " – " + CalendarFormat.Time(CalendarUi.Gestures.Snap(TimeAt(b, col)), settings.Core.TwelveHour));
            }
        }
    }

    private static Border Outline(double x, double y, double w, double h, string label)
    {
        var accent = CalendarVisuals.Palette("Accent");
        var ghost = new Border
        {
            Width = Math.Max(8, w),
            Height = Math.Max(8, h),
            CornerRadius = new CornerRadius(10),
            BorderBrush = accent,
            BorderThickness = new Thickness(2),
            Background = CalendarVisuals.Palette("RoseBg"),
            IsHitTestVisible = false,
            Padding = new Thickness(6, 3, 6, 3),
            Child = CalendarVisuals.Text(label, 11, FontWeights.SemiBold, CalendarVisuals.Palette("RoseFg")),
        };
        AutomationProperties.SetAutomationId(ghost, "cal.drag.ghost");
        Canvas.SetLeft(ghost, x);
        Canvas.SetTop(ghost, y);
        return ghost;
    }
}
