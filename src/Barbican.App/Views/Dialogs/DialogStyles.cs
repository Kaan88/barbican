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

    /// <summary>The gap between footer buttons (Bulwark gap-3 for a dialog, 8 in the older styles).</summary>
    public static double ButtonGap => Bulwark ? 12 : 8;
}
