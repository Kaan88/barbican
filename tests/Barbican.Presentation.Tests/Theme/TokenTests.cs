using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Barbican.GenTokens;
using Barbican.Presentation.Theme;

namespace Barbican.Presentation.Tests.Theme;

/// <summary>
/// The generated design tokens equal the Rust app's ui/theme.slint for every variant
/// (dark/light × normal/more contrast × Bento/Nordic), and the committed generated files are fresh.
/// </summary>
public sealed partial class TokenTests
{
    private static readonly string Root = RepoRoot.Find(AppContext.BaseDirectory);

    private static string ReadRepoFile(string relative) =>
        File.ReadAllText(Path.Combine(Root, relative)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static TokenSet FromSource() => TokenSet.FromSlint(ReadRepoFile(Emitters.SourcePath));

    [Fact]
    public void Committed_generated_files_are_up_to_date()
    {
        foreach (var (relative, expected) in Emitters.All(FromSource()))
        {
            Assert.True(
                string.Equals(expected, ReadRepoFile(relative), StringComparison.Ordinal),
                $"{relative} is stale: run tools/build.ps1 gen-tokens and commit the result.");
        }
    }

    [Fact]
    public void Presentation_palette_equals_theme_slint_for_every_variant()
    {
        var tokens = FromSource();
        Assert.Equal(tokens.ColorKeys, Tokens.ColorKeys);
        foreach (var variant in ThemeVariant.All)
        {
            var expected = tokens.Palettes[variant.Index];
            var actual = Tokens.Palette(variant);
            Assert.Equal(expected.Count, actual.Count);
            foreach (var key in tokens.ColorKeys)
            {
                Assert.True(expected[key] == actual[key], $"{variant} {key}: slint {expected[key]:X8}, generated {actual[key]:X8}");
            }
        }
    }

    [Fact]
    public void Variant_order_matches_the_generator()
    {
        for (var i = 0; i < 8; i++)
        {
            var v = ThemeVariant.All[i];
            var g = Variant.All[i];
            Assert.Equal(i, v.Index);
            Assert.Equal((g.Dark, g.MoreContrast, g.Nordic), (v.Dark, v.MoreContrast, v.Nordic));
        }

        Assert.Equal(0, ThemeVariant.Default.Index);
    }

    /// <summary>Hand-copied from theme.slint (independent of the generator's evaluator).</summary>
    [Theory]
    // key, dark-normal, dark-more, light-normal, light-more (Bento); Nordic is checked separately.
    [InlineData("Ground", 0xFF0A0A0A, 0xFF0A0A0A, 0xFFF4F4F5, 0xFFF4F4F5)]
    [InlineData("Tile", 0xFF141414, 0xFF141414, 0xFFFFFFFF, 0xFFFFFFFF)]
    [InlineData("Line", 0xFF262626, 0xFF9CA3AF, 0xFFE5E5E5, 0xFF4B5563)]
    [InlineData("Text", 0xFFFAFAFA, 0xFFFFFFFF, 0xFF0A0A0A, 0xFF000000)]
    [InlineData("Accent", 0xFFF0577A, 0xFF93C5FD, 0xFFC8234A, 0xFF1D4ED8)]
    [InlineData("OnAccent", 0xFF16070C, 0xFF000000, 0xFFFFFFFF, 0xFFFFFFFF)]
    [InlineData("Sel", 0xFF4A1424, 0xFF1E3A8A, 0xFFFDE4EA, 0xFFDBEAFE)]
    [InlineData("Ring", 0xFFF0577A, 0xFFFFFFFF, 0xFFC8234A, 0xFF000000)]
    [InlineData("RoseBg", 0x24F0577A, 0x24F0577A, 0xFFFDE4EA, 0xFFFDE4EA)]
    [InlineData("MintBg", 0x2134D399, 0x2134D399, 0xFFDCF5E8, 0xFFDCF5E8)]
    [InlineData("AmberFg", 0xFFFCD679, 0xFFFCD679, 0xFF7A4A06, 0xFF7A4A06)]
    [InlineData("Hover", 0xFF1F1F1F, 0xFF1F1F1F, 0xFFF2F2F3, 0xFFF2F2F3)]
    [InlineData("Bg", 0xFF0A0A0A, 0xFF0A0A0A, 0xFFF4F4F5, 0xFFF4F4F5)]
    public void Palette_matches_hand_copied_slint_values(string key, uint darkNormal, uint darkMore, uint lightNormal, uint lightMore)
    {
        foreach (var nordic in new[] { false, true })
        {
            Assert.Equal(darkNormal, Tokens.Palette(new ThemeVariant(true, false, nordic))[key]);
            Assert.Equal(darkMore, Tokens.Palette(new ThemeVariant(true, true, nordic))[key]);
            Assert.Equal(lightNormal, Tokens.Palette(new ThemeVariant(false, false, nordic))[key]);
            Assert.Equal(lightMore, Tokens.Palette(new ThemeVariant(false, true, nordic))[key]);
        }
    }

    [Fact]
    public void Tile2_depends_on_the_style_in_light_mode_only()
    {
        // tile2: dark ? #1a1a1a : (nordic ? #f7f7f8 : #fafafa)
        Assert.Equal(0xFF1A1A1Au, Tokens.Palette(new ThemeVariant(true, false, false))["Tile2"]);
        Assert.Equal(0xFF1A1A1Au, Tokens.Palette(new ThemeVariant(true, false, true))["Tile2"]);
        Assert.Equal(0xFFFAFAFAu, Tokens.Palette(new ThemeVariant(false, false, false))["Tile2"]);
        Assert.Equal(0xFFF7F7F8u, Tokens.Palette(new ThemeVariant(false, false, true))["Tile2"]);
        Assert.Equal(0xFFF7F7F8u, Tokens.Palette(new ThemeVariant(false, false, true))["Bubble"]);
    }

    [Theory]
    [InlineData("Pad", 14, 0)]
    [InlineData("Gap", 12, 0)]
    [InlineData("RailW", 64, 76)]
    [InlineData("ShellX", 90, 76)]
    [InlineData("RTile", 24, 24)]
    [InlineData("RPane", 26, 28)]
    [InlineData("RButton", 12, 999)]
    [InlineData("RRow", 18, 0)]
    [InlineData("RPopup", 14, 16)]
    [InlineData("FsTitle", 28, 28)]
    [InlineData("FsBody", 14.5, 14.5)]
    public void Shape_matches_hand_copied_slint_values(string key, double bento, double nordic)
    {
        Assert.Equal(bento, Tokens.Shape(nordic: false)[key]);
        Assert.Equal(nordic, Tokens.Shape(nordic: true)[key]);
    }

    [Fact]
    public void Motion_and_ease_match_theme_slint()
    {
        Assert.Equal(100, Tokens.Motion.FastMs);
        Assert.Equal(150, Tokens.Motion.EnterMs);
        Assert.Equal(90, Tokens.Motion.ExitMs);
        Assert.Equal(110, Tokens.Motion.ViewMs);
        Assert.Equal(350, Tokens.Motion.TipDelayMs);
        Assert.Equal(0.96, Tokens.Motion.PopScale);
        Assert.Equal(8, Tokens.Motion.RisePx);
        Assert.Equal((0.2, 0d, 0d, 1d), Tokens.Ease.Enter);
        Assert.Equal((0.4, 0d, 1d, 1d), Tokens.Ease.Exit);
        Assert.Equal("M4 4h16v16H4z M22 6l-10 7L2 6", Tokens.Icons["Mail"]);
    }

    [Fact]
    public void Every_colour_property_of_the_theme_global_is_generated()
    {
        // Counted with a plain regex over the Theme block, independent of the generator's parser.
        var source = ReadRepoFile(Emitters.SourcePath);
        var start = source.IndexOf("export global Theme {", StringComparison.Ordinal);
        var end = source.IndexOf("export global Motion {", StringComparison.Ordinal);
        var block = source[start..end];
        var names = ColourProperty().Matches(block).Select(m => TokenSet.Pascal(m.Groups["name"].Value)).ToList();

        Assert.NotEmpty(names);
        Assert.Equal(names, Tokens.ColorKeys);
    }

    [Fact]
    public void Tokens_xaml_brushes_are_the_default_variant()
    {
        var doc = XDocument.Parse(ReadRepoFile(Emitters.TokensXamlPath));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var brushes = doc.Root!.Elements().Where(e => string.Equals(e.Name.LocalName, "SolidColorBrush", StringComparison.Ordinal)).ToList();

        Assert.Equal(Tokens.ColorKeys, brushes.Select(b => (string)b.Attribute(x + "Key")!), StringComparer.Ordinal);
        foreach (var brush in brushes)
        {
            var key = (string)brush.Attribute(x + "Key")!;
            var argb = uint.Parse(((string)brush.Attribute("Color")!).TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            Assert.Equal(Tokens.Palette(ThemeVariant.Default)[key], argb);
        }
    }

    [Fact]
    public void Shape_xaml_matches_the_style_tables()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (var (path, nordic) in new[] { (Emitters.ShapeBentoXamlPath, false), (Emitters.ShapeNordicXamlPath, true) })
        {
            var doc = XDocument.Parse(ReadRepoFile(path));
            var values = doc.Root!.Elements().ToDictionary(
                e => ((string)e.Attribute(x + "Key")!)["Shape.".Length..],
                e => double.Parse(e.Value, CultureInfo.InvariantCulture),
                StringComparer.Ordinal);
            Assert.Equal(Tokens.ShapeKeys.Count, values.Count);
            foreach (var key in Tokens.ShapeKeys)
            {
                Assert.Equal(Tokens.Shape(nordic)[key], values[key]);
            }
        }
    }

    [Theory]
    [InlineData("#0a0a0a", 0xFF0A0A0A)]
    [InlineData("#f0577a24", 0x24F0577A)]
    [InlineData("#00000080", 0x80000000)]
    public void Slint_rrggbbaa_becomes_xaml_aarrggbb(string slint, uint argb)
    {
        Assert.Equal(argb, ColorValue.ParseSlint(slint).Argb);
    }

    [GeneratedRegex(@"^\s*out\s+property\s*<color>\s*(?<name>[\w-]+)\s*:", RegexOptions.Multiline | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ColourProperty();
}
