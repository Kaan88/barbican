// Mirrors: Rust src/rte/layout.rs Role (colour roles resolved against the theme so light and dark switch live); design tokens (Tokens.xaml palette brushes). ADR 0006 section 5 (TextContrast 1.6 on the Windows host, 1 on Uno).

using Barbican.App.Theme;
using Barbican.Editor.Layout;
using SkiaSharp;

namespace Barbican.App.Views.Editor;

/// <summary>The layout style of the editor from the live palette: the editor draws with the app's own colours, and a theme change rebuilds it.</summary>
public static class EditorPalette
{
    public static LayoutStyle Current(float baseSize = 15f)
    {
        var style = new LayoutStyle
        {
            BaseSize = baseSize,
            Text = Of("Text", new SKColor(0xFFFAFAFA)),
            Dim = Of("Dim", new SKColor(0xFFA3A3A3)),
            Line = Of("Track", new SKColor(0xFF2E2E2E)),
            CodeBg = Of("Tile2", new SKColor(0xFF1A1A1A)),
            HeaderBg = Of("Tile2", new SKColor(0xFF1A1A1A)),
            Accent = Of("Accent", new SKColor(0xFFF0577A)),
            Link = Of("SkyFg", new SKColor(0xFFA8CBFF)),
            Selection = Of("Accent", new SKColor(0xFFF0577A)).WithAlpha(0x66),
            SpellUnderline = new SKColor(0xFFE5484D),
#if WINDOWS
            TextContrast = 1.6f,
#endif
        };
        return style;
    }

    private static SKColor Of(string key, SKColor fallback)
    {
        try
        {
            var c = AppResources.Brush(key).Color;
            return new SKColor(c.R, c.G, c.B, c.A);
        }
#pragma warning disable CA1031 // a missing palette key (a test host without the theme) falls back to the dark default
        catch (Exception)
#pragma warning restore CA1031
        {
            return fallback;
        }
    }
}
