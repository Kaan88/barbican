// Mirrors: Bulwark stores/theme-store.ts:578-640 (the CSS of a theme goes to the onThemeBeforeApply transform; the original is applied first,
// so nothing waits for plugins) and :703-710 (onThemeChange { theme, resolved }). Here the "CSS" is the palette as custom properties, which
// the mail body shell can use too.

using System.Globalization;
using System.Text;

namespace Barbican.App.Theme;

/// <summary>The palette of a variant as a CSS custom-property block.</summary>
public static class ThemeCss
{
    /// <summary>":root{--ground:#0a0a0a;...}" in key order (kebab-case names, #rrggbb or rgba()).</summary>
    public static string For(IReadOnlyDictionary<string, uint> palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        var sb = new StringBuilder(":root{");
        foreach (var (key, argb) in palette.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var a = (byte)(argb >> 24);
            var rgb = argb & 0xFFFFFF;
            sb.Append("--").Append(Kebab(key)).Append(':');
            sb.Append(a == 0xFF
                ? string.Create(CultureInfo.InvariantCulture, $"#{rgb:x6}")
                : string.Create(CultureInfo.InvariantCulture, $"rgba({(rgb >> 16) & 0xFF},{(rgb >> 8) & 0xFF},{rgb & 0xFF},{a / 255.0:0.###})"));
            sb.Append(';');
        }

        return sb.Append('}').ToString();
    }

    /// <summary>"Tile2" to "tile2", "RoseBg" to "rose-bg".</summary>
    public static string Kebab(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var sb = new StringBuilder();
        for (var i = 0; i < key.Length; i++)
        {
            if (i > 0 && char.IsUpper(key[i]))
            {
                sb.Append('-');
            }

            sb.Append(char.ToLowerInvariant(key[i]));
        }

        return sb.ToString();
    }
}
