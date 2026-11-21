// Mirrors: Bulwark rounded-md fields and menus (components/ui/input.tsx:13, components/ui/context-menu.tsx:667; 6 px) through Themes/Shape.Bulwark.xaml, the legacy
// radii for Bento and Nordic. A CornerRadius cannot bind to a Shape.* x:Double in XAML (StyleState.cs header), so the shape of the re-styled stock controls
// (Styles/FluentOverrides.xaml) is written here as two CornerRadius resources, once per style.

using Barbican.Presentation.Shell;

namespace Barbican.App.Theme;

/// <summary>The corner radii of the stock Fluent controls that Styles/FluentOverrides.xaml restyles, as application resources.</summary>
public static class FluentShape
{
    /// <summary>Key of the text field radius (TextBox, PasswordBox): <see cref="StyleMetrics.FieldRadius"/>.</summary>
    public const string FieldRadiusKey = "Fluent.FieldRadius";

    /// <summary>Key of the menu flyout panel radius: the Bulwark menu radius, the popup radius of the legacy styles.</summary>
    public const string MenuRadiusKey = "Fluent.MenuRadius";

    /// <summary>
    /// Writes the radii of a style. A control reads them when its style is applied, so a field created before a style switch keeps its old radius until it is
    /// rebuilt; a menu flyout is created per opening.
    /// </summary>
    internal static void Apply(ShapeStyle shape)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        var style = shape == ShapeStyle.Bulwark ? ShellStyle.Bulwark : shape == ShapeStyle.Nordic ? ShellStyle.Nordic : ShellStyle.Bento;
        var metrics = StyleMetrics.For(style);
        app.Resources[FieldRadiusKey] = new CornerRadius(metrics.FieldRadius);
        app.Resources[MenuRadiusKey] = new CornerRadius(style == ShellStyle.Bulwark ? metrics.MenuRadius : metrics.PopupRadius);
    }
}
