// Mirrors: Bulwark components/ui/button.tsx (ghost and outline variants), ui/icon-button.tsx, ui/toolbar.tsx:16-29 (ToolbarButton: muted until hovered, tinted while active), ui/empty-state.tsx:25-39 (icon in a 56 px muted circle, 16 px title, 14 px muted line, actions 20 px below).
// The shared Button.* / ToggleButton.* styles do the hover, press and active states (WP-1); this file only builds the controls the three Files views have in common. Shared tokens only, no motion of its own.

using Barbican.App.Theme;
using Barbican.App.Views.Calendar;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Barbican.App.Views.Files;

/// <summary>Controls and brushes the Files views share.</summary>
internal static class FilesVisuals
{
    public static SolidColorBrush Clear { get; } = new(Windows.UI.Color.FromArgb(0, 0, 0, 0));

    public static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    public static Style Sheet(string key) => (Style)Application.Current.Resources[key];

    /// <summary>A 32 px icon button (Bulwark IconButton / ToolbarButton): muted icon, neutral hover.</summary>
    public static Button IconButton(StyleState look, string icon, string id, string name, Action click, bool destructive = false)
    {
        var b = new Button
        {
            Style = Sheet("Button.Icon"),
            Content = CalendarVisuals.Icon(icon, 16),
            Width = 32,
            Height = 32,
            CornerRadius = look.ButtonRadius,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        if (destructive)
        {
            b.Foreground = Res("Destructive");
        }

        Name(b, id, name);
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>A 32 px toggle with the primary-tint active state (Bulwark TOOLBAR_ACTIVE).</summary>
    public static ToggleButton Toggle(StyleState look, string icon, string id, string name, double width = 32)
    {
        var b = new ToggleButton
        {
            Style = Sheet("ToggleButton.Icon"),
            Content = CalendarVisuals.Icon(icon, 16),
            Width = width,
            Height = 32,
            CornerRadius = look.ButtonRadius,
        };
        Name(b, id, name);
        return b;
    }

    /// <summary>A bordered text button with an optional leading icon (Bulwark Button variant outline, size xs: h-8, px-3, text-sm).</summary>
    public static Button Outline(StyleState look, string text, string? icon, string id, Action click, bool destructive = false)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (icon is not null)
        {
            row.Children.Add(CalendarVisuals.Icon(icon, 16));
        }

        row.Children.Add(new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center });
        var b = new Button { Style = Sheet("Button.Action"), Content = row, Height = 32, Padding = new Thickness(12, 0, 12, 0), CornerRadius = look.ButtonRadius, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        if (destructive)
        {
            b.Foreground = Res("Destructive");
        }

        Name(b, id, text);
        b.Click += (_, _) => click();
        return b;
    }

    public static void Name(DependencyObject o, string id, string name)
    {
        AutomationProperties.SetAutomationId(o, id);
        AutomationProperties.SetName(o, name);
        ToolTipService.SetToolTip(o, name);
    }

    /// <summary>The empty state: icon in a muted circle, title, one line, actions (ui/empty-state.tsx).</summary>
    public static FrameworkElement EmptyState(string icon, string title, string? description, params UIElement[] actions)
    {
        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 384, Padding = new Thickness(32) };
        var circle = new Border
        {
            Width = 56,
            Height = 56,
            CornerRadius = new CornerRadius(28),
            Background = Res("FeedbackHover"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16),
        };
        var glyph = CalendarVisuals.Icon(icon, 28, 1.6);
        glyph.Foreground = Res("Dim");
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        circle.Child = glyph;
        column.Children.Add(circle);
        column.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.Medium, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        if (description is not null)
        {
            column.Children.Add(new TextBlock { Text = description, FontSize = 14, Foreground = Res("Dim"), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
        }

        if (actions.Length > 0)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 20, 0, 0) };
            foreach (var a in actions)
            {
                row.Children.Add(a);
            }

            column.Children.Add(row);
        }

        return column;
    }
}
