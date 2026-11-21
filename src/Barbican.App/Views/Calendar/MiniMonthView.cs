// Mirrors: Rust app.slint:3036-3065 (mini month: title, prev and next, week numbers, busy dots), calendar.rs:508 MiniDay; Bulwark components/calendar/mini-calendar.tsx (month and year picker, keyboard, plugin marks); design BNCalendar (today in accent, the shown week on tile2). I-302, I-303.
// Plugin marks need the plugin host (M6b) and are not drawn; the picker and the keys are.
// Bulwark (mini-calendar.tsx:176-300): the header is [prev][month year and a chevron][next] with the title button text-sm medium; weekday initials text-[0.625rem] muted; days are 28 px circles, today filled primary,
// the selected day tinted primary/15 with primary text, other months muted, a hover band on the rest, a 4 px primary dot for a day with events. Bento and Nordic keep the month they had.

using Microsoft.UI.Xaml.Automation;
using System.Globalization;
using Barbican.Presentation.Calendar.Grid;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Input;
using Windows.System;

#pragma warning disable MA0051 // a drawing routine builds one frame in one pass (no flicker); splitting it would only pass the same locals around
namespace Barbican.App.Views.Calendar;

/// <summary>The sidebar's month: a day pick moves the anchor (the view stays); the title opens a month and year picker.</summary>
public sealed class MiniMonthView : UserControl
{
    private readonly CalendarViewModel _vm = CalendarUi.Model;
    private readonly Grid _host = new();
    private readonly bool _bulwark = CalendarVisuals.Bulwark;
    private bool _picking;
    private int _pickYear;

    public MiniMonthView()
    {
        Content = _host;
        IsTabStop = true;
        KeyDown += OnKeyDown;
        AutomationProperties.SetAutomationId(this, "cal.mini");
        AutomationProperties.SetName(this, "Mini calendar");
        Render();
    }

    public void Render()
    {
        var host = new StackPanel { Spacing = _bulwark ? 0 : 6 };
        host.Children.Add(Header());
        host.Children.Add(_picking ? Picker() : Days());
        _host.Children.Clear();
        _host.Children.Add(host);
    }

    private Grid Header()
    {
        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = GridLength.Auto } } };
        var title = _picking ? _pickYear.ToString(CultureInfo.InvariantCulture) : _vm.MiniMonth.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["Button.Icon"],
            HorizontalAlignment = _bulwark ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            Padding = _bulwark ? new Thickness(8, 4, 8, 4) : new Thickness(6, 3, 6, 3),
            CornerRadius = _bulwark ? new CornerRadius(6) : new CornerRadius(8),
            Content = _bulwark
                ? new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { CalendarVisuals.Text(title, 14, FontWeights.Medium, CalendarVisuals.Palette("Text")), CalendarVisuals.Icon("ChevronDown", 14, 2) } }
                : CalendarVisuals.Text(title, 14, FontWeights.Bold, CalendarVisuals.Palette("Text")),
        };
        AutomationProperties.SetAutomationId(button, "cal.mini.title");
        AutomationProperties.SetName(button, "Choose month and year");
        button.Click += (_, _) =>
        {
            _picking = !_picking;
            _pickYear = _vm.MiniMonth.Year;
            Render();
        };
        if (_bulwark)
        {
            // prev | title | next, the title in the middle (justify-between, mini-calendar.tsx:177-210)
            grid.ColumnDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(button, 1);
            grid.Children.Add(button);
            grid.Children.Add(Step("cal.mini.prev", "Previous", "ChevronLeft", 0, () => Move(-1), true));
            grid.Children.Add(Step("cal.mini.next", "Next", "ChevronRight", 2, () => Move(1), true));
            grid.Margin = new Thickness(0, 0, 0, 8);
            return grid;
        }

        grid.Children.Add(button);
        grid.Children.Add(Step("cal.mini.prev", "Previous", "ChevronLeft", 1, () => Move(-1)));
        grid.Children.Add(Step("cal.mini.next", "Next", "ChevronRight", 2, () => Move(1)));
        return grid;
    }

    private static Button Step(string id, string name, string icon, int column, Action act, bool bulwark = false)
    {
        var button = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Width = bulwark ? 32 : 26, Height = bulwark ? 32 : 26, CornerRadius = new CornerRadius(bulwark ? 6 : 8), Content = CalendarVisuals.Icon(icon, bulwark ? 16 : 14, 2), Foreground = CalendarVisuals.Palette("Dim") };
        AutomationProperties.SetAutomationId(button, id);
        AutomationProperties.SetName(button, name);
        Grid.SetColumn(button, column);
        button.Click += (_, _) => act();
        return button;
    }

    private void Move(int direction)
    {
        if (_picking)
        {
            _pickYear += direction;
            Render();
        }
        else
        {
            _vm.MiniStep(direction);
        }
    }

    private Grid Days()
    {
        var weekNumbers = _vm.Settings.Core.ShowWeekNumbers;
        var grid = new Grid { ColumnSpacing = _bulwark ? 0 : 2, RowSpacing = _bulwark ? 0 : 2 };
        var first = weekNumbers ? 1 : 0;
        if (weekNumbers)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_bulwark ? 20 : 22) });
        }

        for (var i = 0; i < 7; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var r = 0; r < 7; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(r == 0 ? (_bulwark ? 24 : 20) : 28) });
        }

        var names = _vm.WeekdayNames(initials: true);
        for (var i = 0; i < 7; i++)
        {
            var t = CalendarVisuals.Text(names[i], _bulwark ? 10 : 11.5, _bulwark ? FontWeights.Medium : FontWeights.Normal, CalendarVisuals.Palette(_bulwark ? "Dim" : "Faint"));
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(t, first + i);
            grid.Children.Add(t);
        }

        var weeks = _vm.MiniWeekNumbers;
        var days = _vm.MiniDays;
        for (var w = 0; w < 6 && weekNumbers; w++)
        {
            var t = CalendarVisuals.Text(weeks[w].ToString(CultureInfo.InvariantCulture), _bulwark ? 9 : 10, _bulwark ? FontWeights.Medium : FontWeights.Normal, CalendarVisuals.Palette(_bulwark ? "Dim" : "Faint"));
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(t, w + 1);
            grid.Children.Add(t);
        }

        for (var i = 0; i < days.Count; i++)
        {
            var d = days[i];
            var label = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            var selectedDay = (_bulwark ? d.Day == _vm.Anchor : d.Selected) && !d.Today;
            label.Children.Add(new TextBlock
            {
                Text = d.Day.Day.ToString(CultureInfo.InvariantCulture),
                FontSize = 12,
                FontWeight = _bulwark ? (d.Today || selectedDay ? FontWeights.SemiBold : FontWeights.Normal) : (d.Today ? FontWeights.ExtraBold : FontWeights.Normal),
                Foreground = _bulwark
                    ? (d.Today ? CalendarVisuals.Palette("OnAccent") : selectedDay ? CalendarVisuals.Palette("FeedbackActiveFg") : CalendarVisuals.Palette(d.InMonth ? "Text" : "Dim"))
                    : CalendarVisuals.Palette(d.Today ? "OnAccent" : d.InMonth ? "Body" : "Faint"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, d.Busy && !d.Today ? 5 : 0),
            });
            if (d.Busy && !d.Today && !(_bulwark && selectedDay))
            {
                label.Children.Add(new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2), Background = CalendarVisuals.Palette("Accent"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 3) });
            }

            var key = d.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var button = new Button
            {
                Style = (Style)Application.Current.Resources["Button.Icon"],
                HorizontalAlignment = _bulwark ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Width = _bulwark ? 28 : double.NaN,
                CornerRadius = _bulwark ? new CornerRadius(14) : new CornerRadius(9),
                Background = d.Today ? CalendarVisuals.Palette("Accent") : selectedDay ? CalendarVisuals.Palette(_bulwark ? "FeedbackActiveHover" : "Tile2") : null,
                Content = label,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                IsTabStop = false,
            };
            AutomationProperties.SetAutomationId(button, "cal.mini.day." + key);
            AutomationProperties.SetName(button, d.Day.ToString("dddd d MMMM", CultureInfo.InvariantCulture));
            button.Click += (_, _) => Pick(d.Day);
            Grid.SetColumn(button, first + (i % 7));
            Grid.SetRow(button, 1 + (i / 7));
            grid.Children.Add(button);
        }

        return grid;
    }

    // a day pick: the Tasks view takes it as its day filter (a second pick lifts it), every other view goes to the day
    private void Pick(DateOnly day)
    {
        if (_vm.View == CalendarViewKind.Tasks)
        {
            _vm.ToggleTaskDay(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
        else
        {
            _vm.ShowDay(day);
        }
    }

    private Grid Picker()
    {
        var grid = new Grid { ColumnSpacing = 4, RowSpacing = 4 };
        for (var c = 0; c < 3; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var r = 0; r < 4; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        }

        for (var m = 1; m <= 12; m++)
        {
            var month = m;
            var current = _vm.MiniMonth.Year == _pickYear && _vm.MiniMonth.Month == m;
            var button = new Button
            {
                Style = (Style)Application.Current.Resources["Button.Icon"],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                CornerRadius = new CornerRadius(_bulwark ? 6 : 10),
                Background = current ? CalendarVisuals.Palette(_bulwark ? "Accent" : "Sel") : null,
                Content = CalendarVisuals.Text(new DateTime(2000, m, 1).ToString("MMM", CultureInfo.InvariantCulture), _bulwark ? 12 : 13, _bulwark ? FontWeights.Normal : FontWeights.SemiBold, CalendarVisuals.Palette(current ? (_bulwark ? "OnAccent" : "SelText") : (_bulwark ? "Text" : "Body"))),
            };
            AutomationProperties.SetAutomationId(button, "cal.mini.month." + m.ToString(CultureInfo.InvariantCulture));
            button.Click += (_, _) =>
            {
                _picking = false;
                _vm.SetMiniMonth(_pickYear, month);
                Render();
            };
            Grid.SetColumn(button, (m - 1) % 3);
            Grid.SetRow(button, (m - 1) / 3);
            grid.Children.Add(button);
        }

        return grid;
    }

    // I-303: arrows move the shown day (a day or a week), Page keys the month, Enter and Escape leave the picker
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_picking)
        {
            if (e.Key is VirtualKey.Escape or VirtualKey.Enter)
            {
                _picking = false;
                Render();
                e.Handled = true;
            }

            return;
        }

        var next = e.Key switch
        {
            VirtualKey.Left => _vm.Anchor.AddDays(-1),
            VirtualKey.Right => _vm.Anchor.AddDays(1),
            VirtualKey.Up => _vm.Anchor.AddDays(-7),
            VirtualKey.Down => _vm.Anchor.AddDays(7),
            VirtualKey.PageUp => _vm.Anchor.AddMonths(-1),
            VirtualKey.PageDown => _vm.Anchor.AddMonths(1),
            _ => (DateOnly?)null,
        };
        if (next is { } day)
        {
            _vm.ShowDay(day);
            e.Handled = true;
        }
    }
}
