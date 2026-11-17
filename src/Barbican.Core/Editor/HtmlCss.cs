// Mirrors: Rust src/rte/html.rs (tag rust-final) style_prop, norm_color, css_size_px, font_size_attr; Bulwark inline styles of the TipTap composer.
// No RFC or Stalwart counterpart. CSS Color 4 notation (rgb(), #rgb) is read leniently; anything else is ignored.

using System.Globalization;

namespace Barbican.Core.Editor;

/// <summary>The few CSS values the importer reads from inline styles and presentational attributes.</summary>
internal static class HtmlCss
{
    private static readonly Dictionary<string, string> Named = new(StringComparer.Ordinal)
    {
        ["red"] = "#ff0000",
        ["green"] = "#008000",
        ["blue"] = "#0000ff",
        ["black"] = "#000000",
        ["white"] = "#ffffff",
        ["yellow"] = "#ffff00",
        ["orange"] = "#ffa500",
        ["purple"] = "#800080",
        ["gray"] = "#808080",
        ["grey"] = "#808080",
        ["navy"] = "#000080",
        ["maroon"] = "#800000",
        ["teal"] = "#008080",
        ["silver"] = "#c0c0c0",
    };

    /// <summary>The value of a property in a <c>style</c> attribute (first match, <c>!important</c> dropped), or null.</summary>
    public static string? StyleProp(string? style, string prop)
    {
        if (style is null)
        {
            return null;
        }

        foreach (var decl in style.Split(';'))
        {
            var c = decl.IndexOf(':', StringComparison.Ordinal);
            if (c < 0 || !decl[..c].Trim().Equals(prop, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var v = decl[(c + 1)..].Trim();
            if (v.EndsWith("!important", StringComparison.OrdinalIgnoreCase))
            {
                v = v[..^"!important".Length];
            }

            return v.Trim();
        }

        return null;
    }

    /// <summary>A CSS colour (<c>rgb()</c>, <c>rgba()</c>, <c>#rgb</c>, <c>#rrggbb</c>, <c>#rrggbbaa</c>, a few names) as <c>#rrggbb</c>; null when unknown or fully transparent.</summary>
    public static string? NormColor(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var v = value.Trim().ToLowerInvariant();
        if (v.StartsWith('#'))
        {
            var h = v[1..];
            if (!h.All(char.IsAsciiHexDigit))
            {
                return null;
            }

            return h.Length switch
            {
                3 => "#" + string.Concat(h.Select(c => new string(c, 2))),
                6 => "#" + h,
                8 => "#" + h[..6],
                _ => null,
            };
        }

        if (v.StartsWith("rgba(", StringComparison.Ordinal) || v.StartsWith("rgb(", StringComparison.Ordinal))
        {
            var inner = v[(v.IndexOf('(', StringComparison.Ordinal) + 1)..].TrimEnd(')');
            var n = inner.Split([',', ' ', '/'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? (float?)f : null)
                .Where(f => f is not null)
                .Select(f => f!.Value)
                .ToList();
            if (n.Count < 3)
            {
                return null;
            }

            if (n.Count == 4 && n[3] == 0f)
            {
                return null;
            }

            return string.Create(CultureInfo.InvariantCulture, $"#{Channel(n[0])}{Channel(n[1])}{Channel(n[2])}");
        }

        return Named.GetValueOrDefault(v);
    }

    /// <summary>A CSS font size as px (6 to 72), or null when not understood. px, pt, em, rem and the size keywords.</summary>
    public static int? SizePx(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var v = value.Trim().ToLowerInvariant();
        string num;
        float mul;
        if (v.EndsWith("px", StringComparison.Ordinal))
        {
            (num, mul) = (v[..^2], 1f);
        }
        else if (v.EndsWith("pt", StringComparison.Ordinal))
        {
            (num, mul) = (v[..^2], 4f / 3f);
        }
        else if (v.EndsWith("rem", StringComparison.Ordinal))
        {
            (num, mul) = (v[..^3], 14f);
        }
        else if (v.EndsWith("em", StringComparison.Ordinal))
        {
            (num, mul) = (v[..^2], 14f);
        }
        else
        {
            return v switch
            {
                "small" or "x-small" => 12,
                "large" => 18,
                "x-large" => 24,
                "xx-large" => 30,
                _ => null,
            };
        }

        if (!float.TryParse(num.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !float.IsFinite(n))
        {
            return null;
        }

        return (int)Math.Clamp(Math.Round(n * mul, MidpointRounding.AwayFromZero), 6, 72);
    }

    /// <summary>The legacy <c>&lt;font size&gt;</c> scale 1 to 7.</summary>
    public static int? FontSizeAttr(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Trim() switch
        {
            "1" => 10,
            "2" => 12,
            "3" => 14,
            "4" => 18,
            "5" => 24,
            "6" => 32,
            "7" => 48,
            _ => null,
        };
    }

    private static string Channel(float f) => ((byte)Math.Clamp(f, 0f, 255f)).ToString("x2", CultureInfo.InvariantCulture);
}
