// Mirrors: Bulwark components/calendar/calendar-print.tsx + lib/calendar-print.ts; G-008, I-312, I-341; D-M4-14, D-M4-C09. The in-window preview of CalendarPrintModel (pages drawn on white paper in black ink, whatever the theme),
// and the Print button. The OS print path (Windows: the print dialog over these same pages) is built but never run by an agent or a test; on the Linux head the button is greyed (there is no print dialog behind it yet).

using Microsoft.UI.Xaml.Automation;
using System.Globalization;
using Barbican.Presentation.Calendar.Grid;
using Microsoft.UI.Text;
using Windows.UI;

#pragma warning disable MA0051 // a drawing routine builds one frame in one pass (no flicker); splitting it would only pass the same locals around
namespace Barbican.App.Views.Calendar;

/// <summary>The print preview overlay.</summary>
public sealed class CalendarPrintView : UserControl
{
    private const double PageWidth = 1000;
    private const double PageHeight = 700;
    private static readonly SolidColorBrush Paper = new(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush Ink = new(Color.FromArgb(0xFF, 0x11, 0x11, 0x11));
    private static readonly SolidColorBrush Faint = new(Color.FromArgb(0xFF, 0x77, 0x77, 0x77));
    private static readonly SolidColorBrush Rule = new(Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC));

    private readonly CalendarViewModel _vm = CalendarUi.Model;
    private readonly StackPanel _pages = new() { Spacing = 18, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _title = new() { FontSize = 16, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _print;
    private PrintDocument? _document;

    public CalendarPrintView()
    {
        var close = new Button { Style = (Style)Application.Current.Resources["Button.Action"], Content = "Close", Height = 34, CornerRadius = new CornerRadius(12) };
        AutomationProperties.SetAutomationId(close, "cal.print.close");
        close.Click += (_, _) => Hide();
        _print = new Button { Style = (Style)Application.Current.Resources["Button.Primary"], Content = "Print", Height = 34, CornerRadius = new CornerRadius(12), IsEnabled = CalendarPrinter.Available };
        AutomationProperties.SetAutomationId(_print, "cal.print.go");
        _print.Click += (_, _) => CalendarUi.Run(PrintAsync);
        var bar = new Grid { Margin = new Thickness(0, 0, 0, 12), ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = GridLength.Auto } } };
        _title.Foreground = CalendarVisuals.Palette("Text");
        bar.Children.Add(_title);
        Grid.SetColumn(_print, 1);
        bar.Children.Add(_print);
        Grid.SetColumn(close, 2);
        bar.Children.Add(close);
        var body = new Grid { RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = new GridLength(1, GridUnitType.Star) } } };
        body.Children.Add(bar);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = _pages } };
        Grid.SetRow(scroll, 1);
        body.Children.Add(scroll);
        var panel = new Border
        {
            Background = CalendarVisuals.Palette("Popup"),
            BorderBrush = CalendarVisuals.Palette("Line"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(20),
            Margin = new Thickness(40),
            Child = body,
        };
        Content = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), Children = { panel } };
        AutomationProperties.SetAutomationId(this, "cal.print");
        AutomationProperties.SetName(this, "Print preview");
        Visibility = Visibility.Collapsed;
    }

    /// <summary>The document on show, or null.</summary>
    public PrintDocument? Document => _document;

    public void Show()
    {
        _document = CalendarPrintModel.Build(_vm.View, _vm.Anchor, _vm.Range, _vm.Occurrences, _vm.Calendars, _vm.Settings, _vm.TodayDate);
        _title.Text = "Print · " + _document.Title;
        _pages.Children.Clear();
        foreach (var page in _document.Pages)
        {
            _pages.Children.Add(Page(page));
        }

        Visibility = Visibility.Visible;
    }

    public void Hide()
    {
        Visibility = Visibility.Collapsed;
        _pages.Children.Clear();
        _document = null;
    }

    private async Task PrintAsync()
    {
        if (_document is null)
        {
            return;
        }

        var pages = _document.Pages.Select(Page).ToList();
        await CalendarPrinter.PrintAsync(_document.Title, pages).ConfigureAwait(true);
    }

    /// <summary>One page of the document as a UIElement of the paper's size (the preview and the OS path draw the same).</summary>
    public static FrameworkElement Page(PrintPage page)
    {
        var root = new Grid { Width = PageWidth, Height = PageHeight, Background = Paper, Padding = new Thickness(28), RowSpacing = 12 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(new TextBlock { Text = page.Title, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Ink });
        FrameworkElement body = page.Columns == 1 ? List(page) : Table(page);
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        AutomationProperties.SetAutomationId(root, "cal.print.page");
        return root;
    }

    private static StackPanel List(PrintPage page)
    {
        var stack = new StackPanel { Spacing = 10 };
        foreach (var cell in page.Cells)
        {
            stack.Children.Add(new TextBlock { Text = cell.Heading, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Ink, Margin = new Thickness(0, 4, 0, 0) });
            stack.Children.Add(new Border { Height = 1, Background = Rule });
            foreach (var line in cell.Lines)
            {
                stack.Children.Add(Line(line, 13));
            }

            if (cell.Lines.Count == 0)
            {
                stack.Children.Add(new TextBlock { Text = "Nothing scheduled", FontSize = 12.5, Foreground = Faint });
            }
        }

        return stack;
    }

    private static Grid Table(PrintPage page)
    {
        var grid = new Grid();
        for (var c = 0; c < page.Columns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var rows = (page.Cells.Count + page.Columns - 1) / page.Columns;
        for (var r = 0; r < rows; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        for (var i = 0; i < page.Cells.Count; i++)
        {
            var cell = page.Cells[i];
            var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(new TextBlock { Text = cell.Heading, FontSize = 12, FontWeight = cell.Today ? FontWeights.ExtraBold : FontWeights.Bold, Foreground = cell.Muted ? Faint : Ink });
            foreach (var line in cell.Lines.Take(page.Cells.Count > 14 ? 5 : 40))
            {
                stack.Children.Add(Line(line, 10.5));
            }

            if (page.Cells.Count > 14 && cell.Lines.Count > 5)
            {
                stack.Children.Add(new TextBlock { Text = "+" + (cell.Lines.Count - 5).ToString(CultureInfo.InvariantCulture) + " more", FontSize = 10, Foreground = Faint });
            }

            var box = new Border { BorderBrush = Rule, BorderThickness = new Thickness(0.8), Padding = new Thickness(5), Child = stack, Background = cell.Today ? new SolidColorBrush(Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3)) : null };
            Grid.SetColumn(box, i % page.Columns);
            Grid.SetRow(box, i / page.Columns);
            grid.Children.Add(box);
        }

        return grid;
    }

    private static StackPanel Line(PrintLine line, double size)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = CalendarVisuals.SolidBrush(line.Color), VerticalAlignment = VerticalAlignment.Center });
        if (line.Time.Length > 0)
        {
            row.Children.Add(new TextBlock { Text = line.Time, FontSize = size, Foreground = Faint });
        }

        row.Children.Add(new TextBlock { Text = line.Text, FontSize = size, Foreground = Ink, TextTrimming = TextTrimming.CharacterEllipsis });
        return row;
    }
}
