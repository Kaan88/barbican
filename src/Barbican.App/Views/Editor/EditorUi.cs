// Mirrors: design tokens (Tokens.xaml palette; Controls.xaml Button.Icon, Button.Action, Button.Primary), PORT-PLAN 3.7 (one in-window popup surface, never a windowed popup). Owner editor-view.

using Barbican.App.Controls;
using Barbican.App.Theme;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Editor;

/// <summary>Small builders for the editor's own chrome (toolbar buttons, popover cards), in code because the control is one view with no XAML.</summary>
public static class EditorUi
{
    public static Border Card(UIElement content, double minWidth = 0) => new()
    {
        Background = AppResources.Brush("Popup"),
        BorderBrush = AppResources.Brush("Line"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(8),
        MinWidth = minWidth,
        Child = content,
    };

    public static Button TextButton(string id, string text, string tip, Action click, bool bold = false, bool italic = false, bool underline = false, bool strike = false)
    {
        var label = new TextBlock { Text = text, FontSize = 13, FontWeight = bold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        if (italic)
        {
            label.FontStyle = Windows.UI.Text.FontStyle.Italic;
        }

        if (underline)
        {
            label.TextDecorations = Windows.UI.Text.TextDecorations.Underline;
        }

        if (strike)
        {
            label.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
        }

        return Make(id, label, tip, click);
    }

    public static Button IconButton(string id, string icon, string tip, Action click) =>
        Make(id, new SIcon { Kind = icon, Size = 16 }, tip, click);

    public static Button Make(string id, object content, string tip, Action click)
    {
        var button = new Button
        {
            Content = content,
            Padding = new Thickness(6, 4, 6, 4),
            MinWidth = 28,
            Height = 30,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Foreground = AppResources.Brush("Body"),
            CornerRadius = new CornerRadius(6),
            IsTabStop = false,
        };
        AutomationProperties.SetAutomationId(button, "editor." + id);
        AutomationProperties.SetName(button, tip);
        ToolTipService.SetToolTip(button, tip);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>A full-width row of a picker list (the size and block pickers).</summary>
    public static Button Row(string id, string text, bool selected, Action click, double fontSize = 13)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = text, FontSize = fontSize, Foreground = AppResources.Brush(selected ? "Accent" : "Body") },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 4, 10, 4),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            IsTabStop = false,
        };
        AutomationProperties.SetAutomationId(button, "editor." + id);
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => click();
        return button;
    }

    public static TextBlock Label(string text, bool dim = false) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = AppResources.Brush(dim ? "Dim" : "Body"),
        TextWrapping = TextWrapping.Wrap,
    };

    public static TextBox Field(string id, string name, string text = "", string placeholder = "")
    {
        var box = new TextBox { Text = text, PlaceholderText = placeholder, FontSize = 13, MinWidth = 260 };
        AutomationProperties.SetAutomationId(box, "editor." + id);
        AutomationProperties.SetName(box, name);
        return box;
    }

    public static Button Action(string id, string text, Action click, bool primary = false)
    {
        var button = new Button { Content = text, MinWidth = 72, Padding = new Thickness(12, 5, 12, 5) };
        if (Application.Current.Resources.TryGetValue(primary ? "Button.Primary" : "Button.Action", out var style) && style is Style s)
        {
            button.Style = s;
        }

        AutomationProperties.SetAutomationId(button, "editor." + id);
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => click();
        return button;
    }

    public static Border Separator() => new()
    {
        Width = 1,
        Height = 18,
        Margin = new Thickness(4, 0, 4, 0),
        Background = AppResources.Brush("Line"),
        VerticalAlignment = VerticalAlignment.Center,
    };
}
