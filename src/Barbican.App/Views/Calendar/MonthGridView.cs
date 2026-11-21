// Mirrors: Rust calendar.rs:493-:512 (42 cells, 4 events then "+N more", time only with showTimeInMonthView, month_timed styles), app.slint month grid; Bulwark components/calendar/calendar-month-view.tsx (:638 cell click quick create, :645 right click and wheel,
// :903 event click, :941 "+N more", drag-range create, day number opens Day, roving keys), components/calendar/task-chip.tsx; I-314..I-320, B-CAL-055, B-CAL-059.
// Bulwark (calendar-month-view.tsx:594-700): a header of text-xs medium muted weekday names over a hairline (py-2), a hairline between the cells and under each week, the days of the other months on bg-muted/30,
// the date a 24 px circle in the middle of the cell (today filled primary and bold, the picked day tinted primary/15), and chips and "+N more" at text-xs medium with rounded-xs corners (event-card.tsx:343).
// An add-on's day labels (ui.setDayLabels, plugin-day-label.tsx via calendar-month-view.tsx:696) sit right of the date in the cell head, text only, trimmed with an ellipsis; the hover title is the automation name.
// One pointer handler on the grid does the hit test from the cell geometry, so a click, a double click, a right click, an event drag and a range drag all take the road of the automation commands (CalendarUi, ICalendarGestures).

using Barbican.App.Shell;
using Microsoft.UI.Xaml.Automation;
using System.Globalization;
using Barbican.Core.Calendar;
using Barbican.Presentation.Calendar.Grid;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;

#pragma warning disable MA0051 // a drawing routine builds one frame in one pass (no flicker); splitting it would only pass the same locals around
namespace Barbican.App.Views.Calendar;

/// <summary>The Month grid: weekday heads, six rows of seven cells, items, "+N more".</summary>
public sealed class MonthGridView : UserControl
{
    private static double HeadHeight => CalendarVisuals.Bulwark ? 32 : 28;
    private const double WeekNumberWidth = 30;
    private const double ItemHeight = 20;
    private const double DragThreshold = 4;

    private readonly CalendarViewModel _vm = CalendarUi.Model;
    private readonly Barbican.Core.Plugins.Calendar.PluginDayLabelRegistry _dayLabels = ViewHost.Get<Barbican.Core.Plugins.Calendar.PluginDayLabelRegistry>();
    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)), IsTabStop = true, UseSystemFocusVisuals = false };
    private readonly List<(Rect Rect, string Id)> _items = [];
    private readonly List<(Rect Rect, DateOnly Day)> _more = [];
    private IReadOnlyList<MonthCell> _cells = [];
    private double _width;
    private double _height;
    private DateOnly _focus;
    private DateTime _lastWheel = DateTime.MinValue;
    private Drag? _drag;

    private sealed class Drag
    {
        public required string? Id { get; init; }

        public required DateOnly From { get; init; }

        public bool Moved { get; set; }

        public Point Start { get; init; }

        public DateOnly Over { get; set; }
    }

    public MonthGridView()
    {
        _canvas.PointerPressed += OnPressed;
        _canvas.PointerMoved += OnMoved;
        _canvas.PointerReleased += OnReleased;
        _canvas.PointerCanceled += (_, _) =>
        {
            _drag = null;
            Render();
        };
        _canvas.PointerExited += (_, _) => HoverCleared?.Invoke(this, EventArgs.Empty);
        _canvas.PointerWheelChanged += OnWheel;
        _canvas.DoubleTapped += OnDoubleTapped;
        _canvas.KeyDown += OnKeyDown;
        _canvas.SizeChanged += (_, e) =>
        {
            if (Math.Abs(e.NewSize.Width - _width) > 0.5 || Math.Abs(e.NewSize.Height - _height) > 0.5)
            {
                _width = e.NewSize.Width;
                _height = e.NewSize.Height;
                Render();
            }
        };
        Content = _canvas;
        Loaded += (_, _) => _dayLabels.Changed += OnDayLabelsChanged;
        Unloaded += (_, _) => _dayLabels.Changed -= OnDayLabelsChanged;
        AutomationProperties.SetAutomationId(this, "cal.grid.month");
        AutomationProperties.SetAutomationId(_canvas, "cal.month");
        AutomationProperties.SetName(_canvas, "Month grid");
        _focus = _vm.Anchor;
    }

    private void OnDayLabelsChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(Render);

    public event EventHandler<HoverRequestedEventArgs>? HoverRequested;

    public event EventHandler? HoverCleared;

    // Bulwark plugin-day-label.tsx: the texts of the day's labels joined by " · ", title (or text) and add-on name as the tooltip.
    private void AddDayLabel(List<UIElement> children, DateOnly day, double left, double top, double right)
    {
        var labels = _dayLabels.On(day).Where(l => l.Label.Text is not null).ToList();
        if (labels.Count == 0 || right - left < 12) return;
        var text = CalendarVisuals.Text(string.Join(" · ", labels.Select(l => l.Label.Text)), 10, FontWeights.Normal, CalendarVisuals.Palette("Dim"));
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        text.MaxWidth = right - left;
        text.IsHitTestVisible = false;
        AutomationProperties.SetAutomationId(text, "cal.cell.label." + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AutomationProperties.SetName(text, string.Join("\n", labels.Select(l => (l.Label.Title ?? l.Label.Text) + " (" + l.PackageName + ")")));
        Canvas.SetLeft(text, left);
        Canvas.SetTop(text, top);
        children.Add(text);
    }

    private double Left => _vm.Settings.Core.ShowWeekNumbers ? WeekNumberWidth : 0;

    private double CellW => Math.Max(1, (_width - Left) / 7);

    private double CellH => Math.Max(1, (_height - HeadHeight) / 6);

    /// <summary>Draws the model's current state in one pass.</summary>
    public void Render()
    {
        if (_width < 10 || _height < 10)
        {
            return;
        }

        var settings = _vm.Settings;
        var capacity = Math.Clamp((int)((CellH - 26) / ItemHeight), 1, MonthLayout.MaxItems);
        _cells = MonthLayout.Cells(_vm.Range.From, _vm.Anchor, _vm.TodayDate, _vm.Occurrences, settings, capacity);
        var calendars = _vm.Calendars.ToDictionary(c => c.Id, c => c.Color, StringComparer.Ordinal);
        var children = new List<UIElement>();
        _items.Clear();
        _more.Clear();
        var line = CalendarVisuals.Palette("Line");
        var bulwark = CalendarVisuals.Bulwark;
        var names = _vm.WeekdayNames(initials: false);
        if (bulwark)
        {
            var rule = new Border { Width = _width, Height = 1, Background = line, IsHitTestVisible = false };
            Canvas.SetTop(rule, HeadHeight - 1);
            children.Add(rule);
        }

        for (var i = 0; i < 7; i++)
        {
            var head = CalendarVisuals.Text(names[i], 12, bulwark ? FontWeights.Medium : FontWeights.SemiBold, CalendarVisuals.Palette("Dim"));
            head.HorizontalAlignment = HorizontalAlignment.Center;
            head.Width = CellW;
            head.TextAlignment = TextAlignment.Center;
            Canvas.SetLeft(head, Left + (i * CellW));
            Canvas.SetTop(head, bulwark ? 7 : 6);
            children.Add(head);
        }

        var now = _vm.Now();
        for (var index = 0; index < _cells.Count; index++)
        {
            var cell = _cells[index];
            var col = index % 7;
            var row = index / 7;
            var x = Left + (col * CellW);
            var y = HeadHeight + (row * CellH);
            var inFocus = cell.Day == _focus && _canvas.FocusState != FocusState.Unfocused;
            var box = new Border
            {
                Width = CellW,
                Height = CellH,
                BorderBrush = inFocus ? CalendarVisuals.Palette("Accent") : line,
                BorderThickness = inFocus ? new Thickness(2) : bulwark ? new Thickness(0, 0, 1, 1) : new Thickness(0, 1, 1, 0),
                Background = bulwark ? null : cell.Today ? CalendarVisuals.Palette("Tile2") : null,
                IsHitTestVisible = false,
            };
            if (bulwark && !cell.InMonth)
            {
                // !inMonth && "bg-muted/30" (calendar-month-view.tsx:652)
                var dim = new Border { Width = CellW, Height = CellH, Background = CalendarVisuals.Palette("FeedbackHover"), Opacity = 0.3, IsHitTestVisible = false };
                Canvas.SetLeft(dim, x);
                Canvas.SetTop(dim, y);
                children.Add(dim);
            }

            AutomationProperties.SetAutomationId(box, "cal.cell." + cell.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            AutomationProperties.SetName(box, cell.Day.ToString("dddd d MMMM", CultureInfo.InvariantCulture));
            Canvas.SetLeft(box, x);
            Canvas.SetTop(box, y);
            children.Add(box);

            var picked = bulwark && !cell.Today && cell.Day == _vm.Anchor;
            var number = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = cell.Today ? CalendarVisuals.Palette("Accent") : picked ? CalendarVisuals.Palette("FeedbackActiveHover") : null,
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = cell.Day.Day.ToString(CultureInfo.InvariantCulture),
                    FontSize = bulwark ? 12 : 12.5,
                    FontWeight = bulwark ? (cell.Today || picked ? FontWeights.Bold : cell.InMonth ? FontWeights.Medium : FontWeights.Normal) : (cell.Today ? FontWeights.ExtraBold : FontWeights.SemiBold),
                    Foreground = bulwark
                        ? CalendarVisuals.Palette(cell.Today ? "OnAccent" : picked ? "FeedbackActiveFg" : cell.InMonth ? "Text" : "Dim")
                        : CalendarVisuals.Palette(cell.Today ? "OnAccent" : cell.InMonth ? "Body" : "MutedDay"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            Canvas.SetLeft(number, bulwark ? x + ((CellW - 24) / 2) : x + 4);
            Canvas.SetTop(number, y + 3);
            children.Add(number);
            AddDayLabel(children, cell.Day, bulwark ? x + ((CellW - 24) / 2) + 28 : x + 32, y + 8, x + CellW - 3);
            if (bulwark && cell.Day.Day == 1)
            {
                // the first of a month names it, left of the date (calendar-month-view.tsx:662)
                var month = CalendarVisuals.Text(cell.Day.ToString("MMM", CultureInfo.InvariantCulture), 10, FontWeights.Medium, CalendarVisuals.Palette("Dim"));
                Canvas.SetLeft(month, x + ((CellW - 24) / 2) - 26);
                Canvas.SetTop(month, y + 8);
                children.Add(month);
            }

            var iy = y + 28;
            foreach (var item in cell.Items)
            {
                var o = item.Occurrence;
                var hex = calendars.TryGetValue(o.Calendar, out var c) ? c : "#3b82f6";
                var rect = new Rect(x + 3, iy, CellW - 6, ItemHeight - 2);
                _items.Add((rect, o.Id));
                children.Add(Chip(item, hex, rect, settings, string.Equals(o.Id, _vm.SelectedId, StringComparison.Ordinal), o.End <= now, _drag is { Moved: true, Id: { } dragId } && string.Equals(dragId, o.Id, StringComparison.Ordinal)));
                iy += ItemHeight;
            }

            if (cell.More > 0)
            {
                var more = CalendarVisuals.Text("+" + cell.More.ToString(CultureInfo.InvariantCulture) + " more", bulwark ? 12 : 11, bulwark ? FontWeights.Medium : FontWeights.SemiBold, CalendarVisuals.Palette("Dim"));
                Canvas.SetLeft(more, x + 8);
                Canvas.SetTop(more, iy + 1);
                AutomationProperties.SetAutomationId(more, "cal.more." + cell.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                children.Add(more);
                _more.Add((new Rect(x + 3, iy, CellW - 6, ItemHeight - 2), cell.Day));
            }

            if (col == 0 && settings.Core.ShowWeekNumbers)
            {
                var week = CalendarVisuals.Text(CalendarGridRules.WeekNumber(cell.Day.AddDays(3)).ToString(CultureInfo.InvariantCulture), 10.5, FontWeights.Normal, CalendarVisuals.Palette("Faint"));
                Canvas.SetLeft(week, 6);
                Canvas.SetTop(week, y + 8);
                children.Add(week);
            }
        }

        if (_drag is { Moved: true, Id: null } d)
        {
            var (a, b) = d.From <= d.Over ? (d.From, d.Over) : (d.Over, d.From);
            for (var day = a; day <= b; day = day.AddDays(1))
            {
                var idx = day.DayNumber - _vm.Range.From.DayNumber;
                if (idx is < 0 or >= 42)
                {
                    continue;
                }

                var sel = new Border { Width = CellW - 2, Height = CellH - 2, Background = CalendarVisuals.Palette("RoseBg"), BorderBrush = CalendarVisuals.Palette("Accent"), BorderThickness = new Thickness(1), IsHitTestVisible = false };
                Canvas.SetLeft(sel, Left + ((idx % 7) * CellW) + 1);
                Canvas.SetTop(sel, HeadHeight + ((idx / 7) * CellH) + 1);
                children.Add(sel);
            }
        }
        else if (_drag is { Moved: true, Id: not null } mv)
        {
            var idx = mv.Over.DayNumber - _vm.Range.From.DayNumber;
            if (idx is >= 0 and < 42)
            {
                var mark = new Border { Width = CellW - 2, Height = CellH - 2, BorderBrush = CalendarVisuals.Palette("Accent"), BorderThickness = new Thickness(2), IsHitTestVisible = false };
                AutomationProperties.SetAutomationId(mark, "cal.drop.target");
                Canvas.SetLeft(mark, Left + ((idx % 7) * CellW) + 1);
                Canvas.SetTop(mark, HeadHeight + ((idx / 7) * CellH) + 1);
                children.Add(mark);
            }
        }

        _canvas.Children.Clear();
        foreach (var child in children)
        {
            _canvas.Children.Add(child);
        }
    }

    private static Border Chip(MonthItem item, string hex, Rect rect, CalendarViewSettings settings, bool selected, bool past, bool dragging)
    {
        var o = item.Occurrence;
        var (bg, fg) = CalendarVisuals.Tint(hex, settings.Core.TintedEvents);
        var text = string.IsNullOrEmpty(item.Time) ? o.Title : item.Time + " " + o.Title;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
        var bar = item.Style == MonthItemStyle.Bar;
        if (item.Style == MonthItemStyle.Dot)
        {
            row.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), Background = CalendarVisuals.SolidBrush(hex), VerticalAlignment = VerticalAlignment.Center });
        }

        var bulwark = CalendarVisuals.Bulwark;
        row.Children.Add(CalendarVisuals.Text(text, bulwark ? 12 : 11.5, o.Kind == CalendarOccurrenceKind.Task ? FontWeights.Normal : bulwark ? FontWeights.Medium : FontWeights.SemiBold, bar ? fg : CalendarVisuals.Palette(bulwark ? "Text" : "Body")));
        var chip = new Border
        {
            Width = rect.Width,
            Height = rect.Height,
            CornerRadius = new CornerRadius(bulwark ? 2 : o.Kind == CalendarOccurrenceKind.Task ? 10 : 6),
            Padding = new Thickness(6, 0, 6, 0),
            Background = bar ? bg : null,
            BorderBrush = selected ? (bulwark ? CalendarVisuals.Palette("Accent") : fg) : null,
            BorderThickness = new Thickness(selected ? 2 : 0),
            Opacity = dragging ? 0.35 : past && !bulwark ? 0.55 : 1,
            Child = row,
            IsHitTestVisible = false,
        };
        AutomationProperties.SetAutomationId(chip, "cal.event." + o.Id);
        AutomationProperties.SetName(chip, o.Title);
        Canvas.SetLeft(chip, rect.X);
        Canvas.SetTop(chip, rect.Y);
        return chip;
    }

    // ---- geometry ----

    private DateOnly? DayAt(Point p)
    {
        var x = p.X - Left;
        var y = p.Y - HeadHeight;
        if (x < 0 || y < 0)
        {
            return null;
        }

        var col = Math.Min(6, (int)(x / CellW));
        var row = Math.Min(5, (int)(y / CellH));
        return _vm.Range.From.AddDays((row * 7) + col);
    }

    // ---- pointer ----

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        _ = _canvas.Focus(FocusState.Pointer);
        var point = e.GetCurrentPoint(_canvas);
        var p = point.Position;
        if (DayAt(p) is not { } day)
        {
            return;
        }

        _focus = day;
        var item = _items.FirstOrDefault(i => i.Rect.Contains(p));
        if (point.Properties.IsRightButtonPressed)
        {
            var win = e.GetCurrentPoint(null).Position;
            if (item.Id is not null)
            {
                CalendarUi.Select(item.Id);
                CalendarUi.ShowMenu(CalendarMenuIds.Event, win.X, win.Y, item.Id);
            }
            else
            {
                CalendarUi.ShowMenu(CalendarMenuIds.Slot, win.X, win.Y, slot: day.ToDateTime(TimeOnly.MinValue), allDay: true);
            }

            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var more = _more.FirstOrDefault(m => m.Rect.Contains(p));
        if (more.Day != default)
        {
            _vm.OpenDay(more.Day);
            e.Handled = true;
            return;
        }

        // the day number opens the Day view (I-316)
        var cellX = Left + (((day.DayNumber - _vm.Range.From.DayNumber) % 7) * CellW);
        var cellY = HeadHeight + (((day.DayNumber - _vm.Range.From.DayNumber) / 7) * CellH);
        if (item.Id is null && new Rect(cellX + 4, cellY + 3, 24, 24).Contains(p))
        {
            _vm.OpenDay(day);
            e.Handled = true;
            return;
        }

        if (item.Id is not null)
        {
            CalendarUi.Select(item.Id);
            _drag = CalendarUi.Gestures.CanDrag(item.Id) ? new Drag { Id = item.Id, From = day, Start = p, Over = day } : null;
        }
        else
        {
            _drag = new Drag { Id = null, From = day, Start = p, Over = day };
        }

        if (_drag is not null)
        {
            _ = _canvas.CapturePointer(e.Pointer);
        }

        Render();
        e.Handled = true;
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(_canvas).Position;
        if (_drag is not { } drag)
        {
            var hit = _items.FirstOrDefault(i => i.Rect.Contains(p));
            if (hit.Id is not null && _vm.Occurrences.FirstOrDefault(o => string.Equals(o.Id, hit.Id, StringComparison.Ordinal)) is { } over)
            {
                HoverRequested?.Invoke(this, new HoverRequestedEventArgs(over, _canvas, p));
            }
            else
            {
                HoverCleared?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        if (!drag.Moved && (Math.Abs(p.X - drag.Start.X) > DragThreshold || Math.Abs(p.Y - drag.Start.Y) > DragThreshold))
        {
            drag.Moved = true;
            HoverCleared?.Invoke(this, EventArgs.Empty);
        }

        if (drag.Moved && DayAt(p) is { } day && day != drag.Over)
        {
            drag.Over = day;
            Render();
        }
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        _canvas.ReleasePointerCapture(e.Pointer);
        if (_drag is not { } drag)
        {
            return;
        }

        _drag = null;
        if (!drag.Moved)
        {
            Render();
            if (drag.Id is null)
            {
                // a click on an empty cell: quick create for that day (I-314)
                var day = drag.From.ToDateTime(TimeOnly.MinValue);
                CalendarUi.QuickCreate(day, day.AddDays(1), allDay: true);
            }

            return;
        }

        if (drag.Id is { } id)
        {
            // a month drag moves by whole days and keeps the time of day
            var o = _vm.Occurrences.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));
            if (o is not null)
            {
                var shift = drag.Over.DayNumber - drag.From.DayNumber;
                var newStart = o.Start.AddDays(shift);
                CalendarUi.Run(() => CalendarUi.Gestures.MoveAsync(id, newStart, CancellationToken.None));
            }
        }
        else
        {
            var (a, b) = drag.From <= drag.Over ? (drag.From, drag.Over) : (drag.Over, drag.From);
            CalendarUi.QuickCreate(a.ToDateTime(TimeOnly.MinValue), b.ToDateTime(TimeOnly.MinValue).AddDays(1), allDay: true);
        }

        Render();
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var p = e.GetPosition(_canvas);
        var item = _items.FirstOrDefault(i => i.Rect.Contains(p));
        if (item.Id is not null)
        {
            CalendarUi.Open(item.Id);
        }
        else if (DayAt(p) is { } day)
        {
            var start = day.ToDateTime(TimeOnly.MinValue);
            CalendarUi.NewEvent(start, start.AddDays(1), allDay: true);
        }
    }

    // I-315: the wheel pages months (one step per 250 ms so a free-spinning wheel does not skip a year)
    private void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(_canvas).Properties.MouseWheelDelta;
        if (delta == 0 || (DateTime.UtcNow - _lastWheel).TotalMilliseconds < 250)
        {
            return;
        }

        _lastWheel = DateTime.UtcNow;
        if (delta < 0)
        {
            _vm.GoNext();
        }
        else
        {
            _vm.GoPrevious();
        }

        e.Handled = true;
    }

    // roving keys: arrows move the focused day (a day outside the month pages it), Enter opens that day, Space creates, Home and End go to the week's ends
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var next = e.Key switch
        {
            VirtualKey.Left => _focus.AddDays(-1),
            VirtualKey.Right => _focus.AddDays(1),
            VirtualKey.Up => _focus.AddDays(-7),
            VirtualKey.Down => _focus.AddDays(7),
            VirtualKey.Home => CalendarGridRules.WeekStart(_focus, _vm.Settings.Core.FirstDayOfWeek),
            VirtualKey.End => CalendarGridRules.WeekStart(_focus, _vm.Settings.Core.FirstDayOfWeek).AddDays(6),
            _ => (DateOnly?)null,
        };
        if (next is { } day)
        {
            MoveFocus(day);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Enter)
        {
            _vm.OpenDay(_focus);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Space)
        {
            var start = _focus.ToDateTime(TimeOnly.MinValue);
            CalendarUi.QuickCreate(start, start.AddDays(1), allDay: true);
            e.Handled = true;
        }
    }

    /// <summary>Moves the roving focus to a day (paging the month when it is outside the grid's month).</summary>
    public void MoveFocus(DateOnly day)
    {
        _focus = day;
        if (day < _vm.Range.From || day >= _vm.Range.To || day.Month != _vm.Anchor.Month)
        {
            _vm.ShowDay(new DateOnly(day.Year, day.Month, 1));
        }

        Render();
    }

    /// <summary>The day the roving focus is on.</summary>
    public DateOnly FocusedDay => _focus;
}
