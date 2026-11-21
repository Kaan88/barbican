// Mirrors: PORT-PLAN.md §3.7 (tokens loaded once; shape per style; palette brushes mutated in place). App.xaml (F1) loads Tokens.xaml and Shape.Bento.xaml; this adds
// the control styles once (App.xaml is not owned by shell-ui) and swaps the shape dictionary once per style change.

using Barbican.App.Motion;
using Barbican.Presentation.Theme;
using Windows.UI;

namespace Barbican.App.Theme;

/// <summary>The application's resource dictionaries: the palette brushes (read and written in place) and the style dictionaries.</summary>
public static class AppResources
{
    private static bool _stylesLoaded;
    private static ShapeStyle _shapeStyle = ShapeStyle.Bento;

    /// <summary>Merges the control styles into the application resources, once. Call before any view's InitializeComponent.</summary>
    public static void EnsureStyles()
    {
        if (_stylesLoaded)
        {
            return;
        }

        _stylesLoaded = true;
        var merged = Application.Current.Resources.MergedDictionaries;
        merged.Add(new ResourceDictionary { Source = new Uri("ms-appx:///Styles/Controls.xaml") });
        // the stock Fluent controls dressed as Bulwark ones (after Controls.xaml: it reads the Feedback* brushes); the two radii it uses come from FluentShape
        FluentShape.Apply(_shapeStyle);
        merged.Add(new ResourceDictionary { Source = new Uri("ms-appx:///Styles/FluentOverrides.xaml") });
        FocusRing.Install();
        FluentMotion.Apply(MotionRuntime.Scale);
    }

    /// <summary>The live brush of a palette key (always the same instance).</summary>
    public static SolidColorBrush Brush(string key) => (SolidColorBrush)Application.Current.Resources[key];

    /// <summary>The shape dictionary follows the style (Shape.Bento.xaml, Shape.Nordic.xaml or Shape.Bulwark.xaml), swapped once per style change.</summary>
    public static void UseShape(ThemeVariant variant)
    {
        MotionRuntime.Bulwark = variant.Bulwark;
        var style = variant.Bulwark ? ShapeStyle.Bulwark : variant.Nordic ? ShapeStyle.Nordic : ShapeStyle.Bento;
        if (style == _shapeStyle)
        {
            return;
        }

        _shapeStyle = style;
        FluentShape.Apply(style);
        var merged = Application.Current.Resources.MergedDictionaries;
        for (var i = 0; i < merged.Count; i++)
        {
            var source = merged[i].Source?.ToString();
            if (source is not null && source.Contains("/Shape.", StringComparison.Ordinal))
            {
                merged[i] = new ResourceDictionary { Source = new Uri($"ms-appx:///Themes/Shape.{style}.xaml") };
                return;
            }
        }
    }

    internal static uint ToArgb(Color c) => ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

    internal static Color FromArgb(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
}
