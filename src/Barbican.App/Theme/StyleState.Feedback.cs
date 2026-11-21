#pragma warning disable CA1822 // x:Bind binds instance members
// Mirrors: Bulwark app/accessibility.css:115-136 (one 2 px focus outline in --focus-ring-color; the Accessibility "thick" setting makes it 3 px, "extra" 5 px).

using Barbican.Presentation.Theme;

namespace Barbican.App.Theme;

public sealed partial class StyleState
{
    /// <summary>The width of the one focus ring every control draws: Bulwark's 2 px, widened by Settings > Accessibility > Focus ring (thick 3, extra 5).</summary>
    public Thickness FocusRingThickness => new(_accessibility.FocusRing switch
    {
        >= 3 => 5,
        2 => 3,
        _ => Tokens.Motion.FocusWidthPx,
    });

    /// <summary>A dialog card (Bulwark rounded-lg, 8; dialog.tsx:181).</summary>
    public CornerRadius DialogRadius => new(_m.DialogRadius);

    /// <summary>A context-menu panel (Bulwark rounded-md, 6; context-menu.tsx:667).</summary>
    public CornerRadius MenuRadius => new(_m.MenuRadius);

    /// <summary>A context-menu row (square in Bulwark, so the hover fills the panel's width).</summary>
    public CornerRadius MenuItemRadius => new(_m.MenuItemRadius);

    /// <summary>A toast (Bulwark rounded-md, 6; toast.tsx:92).</summary>
    public CornerRadius ToastRadius => new(_m.ToastRadius);

    /// <summary>The style of a menu row: the shared Button.MenuItem feedback (Styles/Feedback.xaml).</summary>
    public Style MenuItemStyle => (Style)Application.Current.Resources["Button.MenuItem"];

    /// <summary>The scrim behind a modal card: Bulwark's black/50 (dialog.tsx:231), Bento and Nordic 40 % black. A new brush each call (it is set on the element that shows it).</summary>
    public Brush ScrimBrush => new SolidColorBrush(global::Windows.UI.Color.FromArgb(IsBulwark ? (byte)0x80 : (byte)0x66, 0, 0, 0));

    /// <summary>The style of a destructive menu row.</summary>
    public Style DestructiveMenuItemStyle => (Style)Application.Current.Resources["Button.MenuItem.Destructive"];
}