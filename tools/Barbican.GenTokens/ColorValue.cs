using System.Globalization;

namespace Barbican.GenTokens;

/// <summary>A colour as 0xAARRGGBB (Slint writes #RRGGBBAA; XAML writes #AARRGGBB).</summary>
public sealed record ColorValue(uint Argb) : SlintValue
{
    /// <summary>Parses a Slint colour literal: #rrggbb or #rrggbbaa.</summary>
    public static ColorValue ParseSlint(string literal)
    {
        var hex = literal.TrimStart('#');
        var rgb = hex.Length switch
        {
            6 or 8 => uint.Parse(hex[..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            _ => throw new FormatException($"Unsupported colour literal '{literal}'"),
        };
        var alpha = hex.Length == 8 ? uint.Parse(hex[6..], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : 0xFFu;
        return new ColorValue((alpha << 24) | rgb);
    }

    /// <summary>XAML colour text: #AARRGGBB.</summary>
    public string ToXaml() => "#" + Argb.ToString("X8", CultureInfo.InvariantCulture);
}
