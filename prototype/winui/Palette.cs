using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Barbican;

/// Palette from design/mockups (renderVals t). Rewrites the shared brushes in place, so every
/// element that uses them follows live without re-templating.
public static class Palette
{
    public static bool Dark = true;
    public static bool Nordic = false;

    static readonly Dictionary<string, string> D = new()
    {
        ["Ground"] = "#0a0a0a", ["Tile"] = "#141414", ["Tile2"] = "#1a1a1a", ["Line"] = "#262626", ["Track"] = "#2e2e2e",
        ["Text"] = "#fafafa", ["Body"] = "#d4d4d4", ["Dim"] = "#a3a3a3", ["Faint"] = "#737373",
        ["Accent"] = "#f0577a", ["OnAccent"] = "#16070c", ["Sel"] = "#4a1424", ["SelText"] = "#ffd5df",
        ["RoseBg"] = "#24f0577a", ["RoseFg"] = "#ffb3c4", ["MintBg"] = "#2134d399", ["MintFg"] = "#86efc4",
        ["SkyBg"] = "#2460a5fa", ["SkyFg"] = "#a8cbff", ["AmberBg"] = "#21fbbf24", ["AmberFg"] = "#fcd679",
        ["Hover"] = "#1d1d1d", ["Pressed"] = "#282828", ["SelRow"] = "#212121",
    };
    static readonly Dictionary<string, string> L = new()
    {
        ["Ground"] = "#f4f4f5", ["Tile"] = "#ffffff", ["Tile2"] = "#fafafa", ["Line"] = "#e5e5e5", ["Track"] = "#ececec",
        ["Text"] = "#0a0a0a", ["Body"] = "#2b2b2b", ["Dim"] = "#6b6b6b", ["Faint"] = "#8a8a8a",
        ["Accent"] = "#c8234a", ["OnAccent"] = "#ffffff", ["Sel"] = "#fde4ea", ["SelText"] = "#8c1733",
        ["RoseBg"] = "#fde4ea", ["RoseFg"] = "#8c1733", ["MintBg"] = "#dcf5e8", ["MintFg"] = "#17603f",
        ["SkyBg"] = "#dfeafd", ["SkyFg"] = "#1e3f8a", ["AmberBg"] = "#fdf1d3", ["AmberFg"] = "#7a4a06",
        ["Hover"] = "#f3f3f4", ["Pressed"] = "#e9e9eb", ["SelRow"] = "#f1f1f2",
    };

    public static Color C(string key) => Parse((Dark ? D : L)[key]);
    public static SolidColorBrush B(string key) => (SolidColorBrush)Application.Current.Resources[key];

    public static void Apply()
    {
        foreach (var kv in Dark ? D : L) B(kv.Key).Color = Parse(kv.Value);
    }

    public static Color Parse(string hex)
    {
        hex = hex.TrimStart('#');
        byte a = 255; int i = 0;
        if (hex.Length == 8) { a = System.Convert.ToByte(hex[..2], 16); i = 2; }
        return Color.FromArgb(a, System.Convert.ToByte(hex.Substring(i, 2), 16),
            System.Convert.ToByte(hex.Substring(i + 2, 2), 16), System.Convert.ToByte(hex.Substring(i + 4, 2), 16));
    }
    public static string Css(string key) { var c = C(key); return $"rgb({c.R},{c.G},{c.B})"; }
}
