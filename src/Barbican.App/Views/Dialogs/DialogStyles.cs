// Mirrors: Bulwark components/ui/button.tsx:42-56 (md size h-10 px-4, text-sm font-medium, radius from the shape, destructive variant bg-destructive) and ui/dialog.tsx:132-134
// (footer gap-3, px-6 pb-6). The solid destructive button lives in Views/Dialogs/DialogStyles.xaml: the shared dictionaries are not edited by the dialog package.

using Barbican.App.Theme;

namespace Barbican.App.Views.Dialogs;

/// <summary>Look shared by the stock dialogs and by the content dialogs that draw their own buttons (the add-on dialog form).</summary>
internal static class DialogStyles
{
    private static ResourceDictionary? _dictionary;

    /// <summary>The solid destructive button style (Bulwark variant destructive).</summary>
    public static Style Destructive => (Style)(_dictionary ??= new ResourceDictionary { Source = new Uri("ms-appx:///Views/Dialogs/DialogStyles.xaml") })["Button.Destructive"];

    /// <summary>True when the Bulwark metrics apply to a dialog (read each time a dialog opens).</summary>
    public static bool Bulwark => StyleState.Current.IsBulwark;

    /// <summary>Dresses a dialog footer button: Bulwark's h-10 button with the shape radius and text-sm, or the older pill in Bento and Nordic.</summary>
    public static void Dress(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);
        button.Padding = new Thickness(16, 0, 16, 0);
        if (Bulwark)
        {
            button.Height = 40;
            button.CornerRadius = new CornerRadius(StyleState.Current.Metrics.ButtonRadius);
            button.FontSize = 14;
            button.FontWeight = Microsoft.UI.Text.FontWeights.Medium;
        }
        else
        {
            button.Height = 32;
            button.CornerRadius = new CornerRadius(14);
            button.ClearValue(Control.FontSizeProperty);
            button.ClearValue(Control.FontWeightProperty);
        }
    }

    /// <summary>A footer button of a content that draws its own buttons: Plain is the outline button, Primary the filled one, Danger the solid destructive one (red text on an outline in the older styles). Dressed like the host's own.</summary>
    public static Button FooterButton(string label, Barbican.Presentation.Compose.DialogTone tone, string id, Action click)
    {
        ArgumentNullException.ThrowIfNull(click);
        var style = tone switch
        {
            Barbican.Presentation.Compose.DialogTone.Primary => (Style)Application.Current.Resources["Button.Primary"],
            Barbican.Presentation.Compose.DialogTone.Danger when Bulwark => Destructive,
            _ => (Style)Application.Current.Resources["Button.Action"],
        };
        var button = new Button { Content = label, Style = style };
        if (tone == Barbican.Presentation.Compose.DialogTone.Danger && !Bulwark)
        {
            button.Foreground = AppResources.Brush("RoseFg");
        }

        Dress(button);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, id);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>The same button with an enabled state (a footer whose action waits on the model).</summary>
    public static Button FooterButton(string label, Barbican.Presentation.Compose.DialogTone tone, string id, Action click, bool enabled)
    {
        var button = FooterButton(label, tone, id, click);
        button.IsEnabled = enabled;
        return button;
    }

    /// <summary>The footer of a content that draws its own buttons (ui/dialog.tsx:132-134 gap-3, right aligned): an optional button at the start (Bulwark's me-auto: Back, Cancel of the upload conflict), then the others in the order given, the action last.</summary>
    public static UIElement Footer(UIElement? start, params UIElement[] end)
    {
        ArgumentNullException.ThrowIfNull(end);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = ButtonGap, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var button in end)
        {
            row.Children.Add(button);
        }

        var footer = new Grid { Margin = new Thickness(0, Bulwark ? 16 : 8, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (start is not null)
        {
            footer.Children.Add(start);
        }

        Grid.SetColumn(row, 1);
        footer.Children.Add(row);
        return footer;
    }

    /// <summary>The footer without a start button.</summary>
    public static UIElement Footer(params UIElement[] buttons) => Footer(null, buttons);

    /// <summary>The text under a title (ui/dialog.tsx:132 text-sm muted, mt-2; 14 in Bulwark, 13 in the older styles). Sits 8 px under the title inside a 12 px column.</summary>
    public static TextBlock Description(string text) => new()
    {
        Text = text,
        FontSize = Bulwark ? 14 : 13,
        Foreground = AppResources.Brush(Bulwark ? "Dim" : "Body"),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, Bulwark ? -4 : 0, 0, 0),
    };

    /// <summary>The 40 px round icon beside a dialog's title column (ui/dialog.tsx:152-160, confirm-dialog.tsx:143-170).</summary>
    public static Border IconBadge(DialogIcon icon)
    {
        ArgumentNullException.ThrowIfNull(icon);
        return new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(20),
            VerticalAlignment = VerticalAlignment.Top,
            Background = AppResources.Brush(icon.BackgroundKey),
            Child = new Barbican.App.Controls.SIcon { Kind = icon.Kind, Size = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = AppResources.Brush(icon.ForegroundKey) },
        };
    }

    /// <summary>The icon of a destructive confirm (a warning triangle on a faint destructive circle).</summary>
    public static DialogIcon DestructiveIcon { get; } = new("Alert", "Destructive", "FeedbackDestructiveHover");

    /// <summary>For a content laid out with <see cref="Footer(UIElement?, UIElement[])"/>: the control kept in the panel's Tag takes the focus when the dialog opens.</summary>
    public static Control? TaggedFocus(UIElement content) => (content as FrameworkElement)?.Tag as Control;

    /// <summary>The gap between footer buttons (Bulwark gap-3 for a dialog, 8 in the older styles).</summary>
    public static double ButtonGap => Bulwark ? 12 : 8;
}
