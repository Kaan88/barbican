// Mirrors: Bulwark components/calendar/event-modal.tsx field layout (I-332..I-336); ADR 0007 (in-window only). Small builders shared by the calendar cards.
// Owner cal-ui. Dates and times are typed as text (yyyy-MM-dd, HH:mm) so no picker flyout can open a windowed popup.

#pragma warning disable CA1859 // the builders return UIElement by design

using Barbican.App.Theme;
using Barbican.Presentation.Compose;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Calendar.Editors;

/// <summary>Builders for the fields, combos and buttons of the calendar cards.</summary>
internal static class CalendarCardKit
{
    public static TextBlock Heading(string text) => new() { Text = text, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = AppResources.Brush("Text"), TextWrapping = TextWrapping.Wrap };

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
        var box = new TextBox { Text = value, PlaceholderText = placeholder ?? string.Empty, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 72 : 0 };
        box.TextChanged += (_, _) => set(box.Text);
        AutomationProperties.SetAutomationId(box, id);
        return box;
    }

    /// <summary>A one-of-many choice as radio buttons: a drop-down opens a popup, and no popup may be created in code (ADR 0007).</summary>
    public static RadioButtons Combo(string id, IEnumerable<string> items, int selected, Action<int> set)
    {
        var radios = new RadioButtons { MaxColumns = 3 };
        foreach (var item in items)
        {
            radios.Items.Add(item);
        }

        radios.SelectedIndex = selected;
        radios.SelectionChanged += (_, _) =>
        {
            if (radios.SelectedIndex >= 0)
            {
                set(radios.SelectedIndex);
            }
        };
        AutomationProperties.SetAutomationId(radios, id);
        return radios;
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
        var button = new Button { Style = (Style)Application.Current.Resources[style], Content = label, Height = 34, CornerRadius = new CornerRadius(17), FontSize = 13, IsEnabled = enabled };
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
