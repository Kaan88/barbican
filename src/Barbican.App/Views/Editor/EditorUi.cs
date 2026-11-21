// Mirrors: design tokens (Tokens.xaml palette; Controls.xaml Button.Icon, Button.Action, Button.Primary), PORT-PLAN 3.7 (one in-window popup surface, never a windowed popup); Bulwark rich-text-editor.tsx:233-262 (ToolbarButton:
// p-1.5 around a 16 px icon, rounded, hover:bg-accent, active bg-accent text-accent-foreground, transition-colors; Styles/Feedback.xaml Button.Icon carries the hover, pressed and transition), :397-399 (ToolbarSeparator
// w-px h-5 bg-border mx-0.5). Owner editor-view.

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

    /// <summary>A toolbar button with a 16 px line icon (rich-text-editor.tsx:1127 w-4 h-4).</summary>
    public static Button IconButton(string id, string icon, string tip, Action click) =>
        Make(id, new SIcon { Kind = icon, Size = 16 }, tip, click);

    /// <summary>One toolbar button: 28 px square (p-1.5 around a 16 px icon), 4 px radius, the shared icon-button feedback of Styles/Feedback.xaml for hover, pressed and the colour transition.</summary>
    public static Button Make(string id, object content, string tip, Action click)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["Button.Icon"],
            Content = content,
            Padding = new Thickness(6),
            MinWidth = 28,
            Height = 28,
            Foreground = AppResources.Brush("Body"),
            CornerRadius = new CornerRadius(4),
            IsTabStop = false,
        };
        AutomationProperties.SetAutomationId(button, "editor." + id);
        AutomationProperties.SetName(button, tip);
        Barbican.App.Motion.Tip.SetText(button, tip);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>The look of a toolbar toggle: on is bg-accent with text-accent-foreground (the shared hover tint), off is the plain icon button.</summary>
    public static void SetActive(Button button, bool on)
    {
        ArgumentNullException.ThrowIfNull(button);
        if (on)
        {
            button.Background = AppResources.Brush("FeedbackIconHover");
            button.Foreground = AppResources.Brush("FeedbackIconHoverFg");
        }
        else
        {
            button.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            button.Foreground = AppResources.Brush("Body");
        }

        AutomationProperties.SetItemStatus(button, on ? "on" : "off");
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
        Height = 20,
        Margin = new Thickness(2, 0, 2, 0),
        Background = AppResources.Brush("Line"),
        VerticalAlignment = VerticalAlignment.Center,
    };
}
