// Mirrors: Bulwark app/accessibility.css:115-136 (one focus style for every control). The ring colour and its single-colour look come from Styles/Feedback.xaml
// (SystemControlFocusVisualPrimaryBrush is the live palette Ring brush, the secondary brush is transparent); this applies the width.

using Microsoft.UI.Xaml.Input;

namespace Barbican.App.Theme;

/// <summary>
/// Makes the Accessibility focus-ring width apply to every control, not only the three surfaces that bound it (FM-05): when a control gets focus its
/// FocusVisualPrimaryThickness becomes <see cref="StyleState.FocusRingThickness"/>, unless a binding already owns that property.
/// </summary>
public static class FocusRing
{
    private static bool _installed;

    /// <summary>Hooks the focus manager once.</summary>
    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        FocusManager.GotFocus += OnGotFocus;
    }

    private static void OnGotFocus(object? sender, FocusManagerGotFocusEventArgs e)
    {
        if (e.NewFocusedElement is not Control control || StyleState.Current is not { } look)
        {
            return;
        }

        var local = control.ReadLocalValue(Control.FocusVisualPrimaryThicknessProperty);
        if (local is not null && local is not Thickness && !ReferenceEquals(local, DependencyProperty.UnsetValue))
        {
            return;
        }

        var width = look.FocusRingThickness;
        if (!control.FocusVisualPrimaryThickness.Equals(width))
        {
            control.FocusVisualPrimaryThickness = width;
        }
    }
}