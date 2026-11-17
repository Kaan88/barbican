// Mirrors: PORT-PLAN.md §3.7 (tokens loaded once; shape per style; palette brushes mutated in place). App.xaml (F1) loads Tokens.xaml and Shape.Bento.xaml; this adds
// the control styles once (App.xaml is not owned by shell-ui) and swaps the shape dictionary once per style change.

using Windows.UI;

namespace Barbican.App.Theme;

/// <summary>The application's resource dictionaries: the palette brushes (read and written in place) and the style dictionaries.</summary>
public static class AppResources
{
    private static bool _stylesLoaded;
    private static bool _nordicShape;

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
    }

    /// <summary>The live brush of a palette key (always the same instance).</summary>
    public static SolidColorBrush Brush(string key) => (SolidColorBrush)Application.Current.Resources[key];

    /// <summary>The shape dictionary follows the style (Shape.Bento.xaml or Shape.Nordic.xaml), swapped once per style change.</summary>
    public static void UseShape(bool nordic)
    {
        if (nordic == _nordicShape)
        {
            return;
        }

        _nordicShape = nordic;
        var merged = Application.Current.Resources.MergedDictionaries;
        for (var i = 0; i < merged.Count; i++)
        {
            var source = merged[i].Source?.ToString();
            if (source is not null && source.Contains("/Shape.", StringComparison.Ordinal))
            {
                merged[i] = new ResourceDictionary { Source = new Uri(nordic ? "ms-appx:///Themes/Shape.Nordic.xaml" : "ms-appx:///Themes/Shape.Bento.xaml") };
                return;
            }
        }
    }

    internal static uint ToArgb(Color c) => ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

    internal static Color FromArgb(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
}
