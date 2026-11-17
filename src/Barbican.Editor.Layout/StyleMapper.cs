// Mirrors: Rust src/rte/layout.rs (tag rust-final; parley there, Topten.RichTextKit here).
// Spike S3, docs/adr/0006-editor-engine.md. No Bulwark/RFC/Stalwart counterpart (Bulwark uses TipTap in a browser).

using System.Collections.Concurrent;
using System.Globalization;
using Barbican.Core.Editor;
using SkiaSharp;
using Topten.RichTextKit;

namespace Barbican.Editor.Layout;

/// <summary>Turns a block kind plus a run's marks into a RichTextKit style (Rust build_text_layout).</summary>
public static class StyleMapper
{
    private static readonly ConcurrentDictionary<(string Family, int Weight, bool Italic), float> NaturalSpacing = new();

    public static Style ToStyle(BlockKind kind, Marks marks, LayoutStyle style)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(marks);
        ArgumentNullException.ThrowIfNull(style);
        var (size, weight, mono, lineHeight) = kind switch
        {
            BlockKind.Heading h => (style.BaseSize * BlockMetrics.HeadingScale(h.Level), h.Level == 1 ? 700 : 600, false, 1.3f),
            BlockKind.Cell { Header: true } => (style.BaseSize, 700, false, style.LineHeight),
            BlockKind.CodeBlock => (style.BaseSize * 0.93f, 400, true, 1.45f),
            _ => (style.BaseSize, 400, false, style.LineHeight),
        };

        var family = mono ? style.MonoFont : style.BodyFont;
        if (marks.Code && !mono)
        {
            family = style.MonoFont;
            size = (marks.Size ?? size) * 0.92f;
        }
        else if (marks.Size is int px)
        {
            size = kind is BlockKind.Heading hk ? px * BlockMetrics.HeadingScale(hk.Level) : px;
        }

        var rtk = new Style
        {
            FontFamily = family,
            FontSize = size,
            FontWeight = marks.Bold ? 700 : weight,
            FontItalic = marks.Italic,
            Underline = marks.Underline || marks.Link is not null ? UnderlineStyle.Gapped : UnderlineStyle.None,
            StrikeThrough = marks.Strike ? StrikeThroughStyle.Solid : StrikeThroughStyle.None,
            TextColor = marks.Link is not null ? style.Link : ParseRgb(marks.Color) ?? style.Text,
            BackgroundColor = ParseRgb(marks.Highlight) ?? (marks.Code && kind is not BlockKind.CodeBlock ? style.CodeBg : SKColor.Empty),
        };
        rtk.LineHeight = CssLineHeight(rtk, lineHeight);
        return rtk;
    }

    /// <summary>
    /// RichTextKit's <see cref="Style.LineHeight"/> multiplies the font's own line spacing
    /// (ascent + descent + leading), CSS and Rust's parley multiply the font size. Converts a CSS
    /// multiple into RichTextKit's, so a 15 px Segoe UI line is 22.5 px tall as in Bulwark's composer.
    /// </summary>
    public static float CssLineHeight(Style style, float cssMultiple)
    {
        ArgumentNullException.ThrowIfNull(style);
        var key = (style.FontFamily ?? "", style.FontWeight, style.FontItalic);
        var natural = NaturalSpacing.GetOrAdd(key, static (_, s) =>
        {
            var tf = FontMapper.Default.TypefaceFromStyle(s, ignoreFontVariants: false);
            using var font = new SKFont(tf, 100f);
            var m = font.Metrics;
            return (m.Descent - m.Ascent + m.Leading) / 100f;
        }, style);
        return natural > 0f ? cssMultiple / natural : cssMultiple;
    }

    /// <summary>Parses "#rrggbb" (the only form the HTML import keeps); anything else is null.</summary>
    public static SKColor? ParseRgb(string? s)
    {
        if (s is null || s.Length != 7 || s[0] != '#')
        {
            return null;
        }

        return uint.TryParse(s.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            ? new SKColor(0xFF000000 | rgb)
            : null;
    }
}
