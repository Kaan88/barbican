// Mirrors: Rust app.slint:3036-3065 (mini month: title, prev and next, week numbers, busy dots), calendar.rs:508 MiniDay; Bulwark components/calendar/mini-calendar.tsx (month and year picker, keyboard, plugin marks); design BNCalendar (today in accent, the shown week on tile2). I-302, I-303.
// Plugin marks need the plugin host (M6b) and are not drawn; the picker and the keys are.

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
        var host = new StackPanel { Spacing = 6 };
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
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(6, 3, 6, 3),
            CornerRadius = new CornerRadius(8),
            Content = CalendarVisuals.Text(title, 14, FontWeights.Bold, CalendarVisuals.Palette("Text")),
        };
        AutomationProperties.SetAutomationId(button, "cal.mini.title");
        AutomationProperties.SetName(button, "Choose month and year");
        button.Click += (_, _) =>
        {
            _picking = !_picking;
            _pickYear = _vm.MiniMonth.Year;
            Render();
        };
        grid.Children.Add(button);
        grid.Children.Add(Step("cal.mini.prev", "Previous", "ChevronLeft", 1, () => Move(-1)));
        grid.Children.Add(Step("cal.mini.next", "Next", "ChevronRight", 2, () => Move(1)));
        return grid;
    }

    private static Button Step(string id, string name, string icon, int column, Action act)
    {
        var button = new Button { Style = (Style)Application.Current.Resources["Button.Icon"], Width = 26, Height = 26, CornerRadius = new CornerRadius(8), Content = CalendarVisuals.Icon(icon, 14, 2) };
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
        var grid = new Grid { ColumnSpacing = 2, RowSpacing = 2 };
        var first = weekNumbers ? 1 : 0;
        if (weekNumbers)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        }

        for (var i = 0; i < 7; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var r = 0; r < 7; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(r == 0 ? 20 : 28) });
        }

        var names = _vm.WeekdayNames(initials: true);
        for (var i = 0; i < 7; i++)
        {
            var t = CalendarVisuals.Text(names[i], 11.5, FontWeights.Normal, CalendarVisuals.Palette("Faint"));
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(t, first + i);
            grid.Children.Add(t);
        }

        var weeks = _vm.MiniWeekNumbers;
        var days = _vm.MiniDays;
        for (var w = 0; w < 6 && weekNumbers; w++)
        {
            var t = CalendarVisuals.Text(weeks[w].ToString(CultureInfo.InvariantCulture), 10, FontWeights.Normal, CalendarVisuals.Palette("Faint"));
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(t, w + 1);
            grid.Children.Add(t);
        }

        for (var i = 0; i < days.Count; i++)
        {
            var d = days[i];
            var label = new Grid();
            label.Children.Add(new TextBlock
            {
                Text = d.Day.Day.ToString(CultureInfo.InvariantCulture),
                FontSize = 12,
                FontWeight = d.Today ? FontWeights.ExtraBold : FontWeights.Normal,
                Foreground = CalendarVisuals.Palette(d.Today ? "OnAccent" : d.InMonth ? "Body" : "Faint"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (d.Busy && !d.Today)
            {
                label.Children.Add(new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2), Background = CalendarVisuals.Palette("Accent"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2) });
            }

            var key = d.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var button = new Button
            {
                Style = (Style)Application.Current.Resources["Button.Icon"],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                CornerRadius = new CornerRadius(9),
                Background = d.Today ? CalendarVisuals.Palette("Accent") : d.Selected ? CalendarVisuals.Palette("Tile2") : null,
                Content = label,
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
                CornerRadius = new CornerRadius(10),
                Background = current ? CalendarVisuals.Palette("Sel") : null,
                Content = CalendarVisuals.Text(new DateTime(2000, m, 1).ToString("MMM", CultureInfo.InvariantCulture), 13, FontWeights.SemiBold, CalendarVisuals.Palette(current ? "SelText" : "Body")),
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
