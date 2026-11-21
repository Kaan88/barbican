using Barbican.Presentation.Theme;

namespace Barbican.Presentation.Tests.Theme;

/// <summary>
/// The Bulwark "more contrast" variants carry Bulwark's own pairs (app/accessibility.css:235-335): the semantic colours that read as text
/// (primary, unread, destructive) clear the contrast ratio the setting promises, in dark and light (ledger TH-08).
/// </summary>
public sealed class BulwarkContrastTests
{
    private static double Channel(uint v)
    {
        var c = v / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static double Luminance(uint argb) => (0.2126 * Channel((argb >> 16) & 0xFF)) + (0.7152 * Channel((argb >> 8) & 0xFF)) + (0.0722 * Channel(argb & 0xFF));

    private static double Ratio(uint a, uint b)
    {
        var (hi, lo) = (Math.Max(Luminance(a), Luminance(b)), Math.Min(Luminance(a), Luminance(b)));
        return (hi + 0.05) / (lo + 0.05);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Text_colours_of_the_more_contrast_variant_reach_the_enhanced_ratio(bool dark)
    {
        var p = Tokens.Palette(new ThemeVariant(dark, true, false, true));
        foreach (var key in new[] { "Text", "Body", "Dim", "Accent", "Unread", "Destructive", "SelText" })
        {
            Assert.True(Ratio(p[key], p["Bg"]) >= 6, $"{(dark ? "dark" : "light")} more contrast: {key} on Bg is {Ratio(p[key], p["Bg"]):F1}:1");
        }

        Assert.True(Ratio(p["Line"], p["Bg"]) >= 7, "the border is a strong line");
        Assert.True(Ratio(p["Ring"], p["Bg"]) >= 7, "the focus ring is black or white");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void More_contrast_is_never_weaker_than_the_normal_variant(bool dark)
    {
        var normal = Tokens.Palette(new ThemeVariant(dark, false, false, true));
        var more = Tokens.Palette(new ThemeVariant(dark, true, false, true));
        foreach (var key in new[] { "Text", "Dim", "Unread", "Destructive", "Line", "Ring" })
        {
            Assert.True(Ratio(more[key], more["Bg"]) >= Ratio(normal[key], normal["Bg"]), $"{(dark ? "dark" : "light")}: {key} lost contrast in more contrast");
        }
    }
}
