// Mirrors: Bulwark components/calendar/event-modal.tsx field layout (I-332..I-336); ADR 0007 (in-window only). Small builders shared by the calendar cards.
// Bulwark (ui/button.tsx:42-55, ui/input.tsx:11-22, event-modal.tsx): buttons are h-9 with rounded-md corners and text-sm medium, fields h-10, the heading text-lg semibold; the legacy pills stay in Bento and Nordic. WP-12, CA-03.
// Owner cal-ui. Dates and times are typed as text (yyyy-MM-dd, HH:mm) so no picker flyout can open a windowed popup.

#pragma warning disable CA1859 // the builders return UIElement by design

using Barbican.App.Theme;
using Barbican.Presentation.Compose;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Calendar.Editors;

/// <summary>Builders for the fields, combos and buttons of the calendar cards.</summary>
internal static class CalendarCardKit
{
    private static StyleState Look => Barbican.App.Shell.ViewHost.Get<StyleState>();

    private static bool Bulwark => Look.IsBulwark;

    public static TextBlock Heading(string text) => new() { Text = text, FontSize = Bulwark ? 18 : 16, FontWeight = Bulwark ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Bold, Foreground = AppResources.Brush("Text"), TextWrapping = TextWrapping.Wrap };

    public static TextBlock Label(string text) => new() { Text = text, FontSize = 12, Foreground = AppResources.Brush("Dim"), Margin = new Thickness(0, 4, 0, 0) };

    public static TextBlock Body(string text) => new() { Text = text, FontSize = 13, Foreground = AppResources.Brush("Dim"), TextWrapping = TextWrapping.Wrap };

    public static TextBlock ErrorLine(string text)
    {
        var block = new TextBlock { FontSize = 12.5, Foreground = AppResources.Brush("RoseFg"), TextWrapping = TextWrapping.Wrap };
        SetError(block, text);
        return block;
    }

    public static void SetError(TextBlock block, string text)
    {
        block.Text = text;
        block.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    public static TextBox Text(string id, string value, Action<string> set, bool multiline = false, string? placeholder = null)
    {
        var box = new TextBox { Text = value, PlaceholderText = placeholder ?? string.Empty, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 72 : Bulwark ? 40 : 0 };
        box.TextChanged += (_, _) => set(box.Text);
        AutomationProperties.SetAutomationId(box, id);
        return box;
    }

    /// <summary>A one-of-many choice as a row of chips, the chosen one filled: a drop-down opens a popup (no popup may be created in code, ADR 0007) and a RadioButtons drew no selection here. Chips wrap in rows of three.</summary>
    public static StackPanel Combo(string id, IEnumerable<string> items, int selected, Action<int> set)
    {
        var names = items.ToList();
        var chips = new List<Button>();
        var current = selected;
        void Paint()
        {
            for (var i = 0; i < chips.Count; i++)
            {
                chips[i].Style = (Style)Application.Current.Resources[i == current ? "Button.Primary" : "Button.Action"];
            }
        }

        var column = new StackPanel { Spacing = 6 };
        StackPanel? row = null;
        for (var i = 0; i < names.Count; i++)
        {
            if (i % 3 == 0)
            {
                row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                column.Children.Add(row);
            }

            var index = i;
            var chip = new Button { Content = names[i], Height = Bulwark ? 32 : 30, CornerRadius = Bulwark ? Look.ButtonRadius : new CornerRadius(15), FontSize = Bulwark ? 13 : 12.5, Padding = new Thickness(12, 0, 12, 0) };
            AutomationProperties.SetAutomationId(chip, id + "." + i);
            AutomationProperties.SetName(chip, names[i]);
            chip.Click += (_, _) =>
            {
                current = index;
                Paint();
                set(index);
            };
            chips.Add(chip);
            row!.Children.Add(chip);
        }

        Paint();
        AutomationProperties.SetAutomationId(column, id);
        return column;
    }

    public static CheckBox Check(string id, string label, bool value, Action<bool> set)
    {
        var box = new CheckBox { Content = label, IsChecked = value };
        box.Click += (_, _) => set(box.IsChecked == true);
        AutomationProperties.SetAutomationId(box, id);
        return box;
    }

    public static Button Btn(string label, DialogTone tone, string id, Action click, bool enabled = true)
    {
        var style = tone == DialogTone.Primary ? "Button.Primary" : "Button.Action";
        var button = new Button { Style = (Style)Application.Current.Resources[style], Content = label, Height = Bulwark ? 36 : 34, CornerRadius = Bulwark ? Look.ButtonRadius : new CornerRadius(17), FontSize = Bulwark ? 14 : 13, IsEnabled = enabled };
        if (Bulwark)
        {
            button.FontWeight = Microsoft.UI.Text.FontWeights.Medium;
        }

        if (tone == DialogTone.Danger)
        {
            button.Foreground = AppResources.Brush("RoseFg");
        }

        button.Click += (_, _) => click();
        AutomationProperties.SetAutomationId(button, id);
        return button;
    }

    public static StackPanel Buttons(params UIElement[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var b in buttons)
        {
            row.Children.Add(b);
        }

        return row;
    }

    public static StackPanel Card(string id, string heading, double minWidth = 380)
    {
        var panel = new StackPanel { Spacing = 6, MinWidth = minWidth };
        panel.Children.Add(Heading(heading));
        AutomationProperties.SetAutomationId(panel, id);
        AutomationProperties.SetName(panel, heading);
        return panel;
    }

    public static void Run(Task task) => _ = task.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
