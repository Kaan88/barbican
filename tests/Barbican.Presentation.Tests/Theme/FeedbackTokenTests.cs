using System.Text.RegularExpressions;
using System.Xml.Linq;
using Barbican.GenTokens;
using Barbican.Presentation.Theme;

namespace Barbican.Presentation.Tests.Theme;

/// <summary>
/// The feedback system (Styles/Feedback.xaml): every state colour is a generated token, in every variant, and the states are ordered
/// rest, hover, pressed the same way in dark and light (design/MOTION.md "Feedback"; ledger FM-01, FM-06, FM-07, TH-01..03).
/// </summary>
public sealed partial class FeedbackTokenTests
{
    private static readonly string Root = RepoRoot.Find(AppContext.BaseDirectory);

    private static readonly string[] FeedbackKeys =
    [
        "FeedbackHover", "FeedbackPressed", "FeedbackIconHover", "FeedbackIconPressed", "FeedbackIconHoverFg", "FeedbackActive", "FeedbackActiveHover",
        "FeedbackActivePressed", "FeedbackActiveFg", "FeedbackSelected", "FeedbackSelectedFg", "FeedbackPrimaryHover", "FeedbackPrimaryPressed",
        "FeedbackDestructiveHover", "FeedbackFocus",
    ];

    private static readonly string[] StyleFiles = ["src/Barbican.App/Styles/Feedback.xaml", "src/Barbican.App/Styles/Controls.xaml"];

    private static readonly string[] SemanticKeys =
    [
        "Destructive", "DestructiveFg", "Success", "SuccessFg", "Warning", "WarningFg", "Info", "InfoFg", "Secondary", "SecondaryFg", "Input", "PopoverFg",
        "AccentFill", "AccentFillFg", "Unread", "Chart1", "Chart2", "Chart3", "Chart4", "Chart5",
    ];

    private static string ReadRepoFile(string relative) => File.ReadAllText(Path.Combine(Root, relative)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static double Luma(uint argb) => (((argb >> 16) & 0xFF) * 0.2126) + (((argb >> 8) & 0xFF) * 0.7152) + ((argb & 0xFF) * 0.0722);

    [Fact]
    public void Every_variant_defines_every_feedback_and_semantic_token()
    {
        foreach (var variant in ThemeVariant.All)
        {
            var palette = Tokens.Palette(variant);
            foreach (var key in FeedbackKeys.Concat(SemanticKeys))
            {
                Assert.True(palette.ContainsKey(key), $"{variant} has no {key}");
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Pressed_is_stronger_than_hover_which_is_stronger_than_rest(bool dark)
    {
        foreach (var bulwark in new[] { true, false })
        {
            var variant = bulwark ? new ThemeVariant(dark, false, false, true) : new ThemeVariant(dark, false, false);
            var p = Tokens.Palette(variant);
            var rest = Luma(p["Ground"]);
            Assert.True(Math.Abs(Luma(p["FeedbackHover"]) - rest) > 0, $"{variant}: hover equals rest");
            Assert.True(Math.Abs(Luma(p["FeedbackPressed"]) - rest) > Math.Abs(Luma(p["FeedbackHover"]) - rest), $"{variant}: neutral pressed is not stronger than hover");
            Assert.True(Math.Abs(Luma(p["FeedbackIconPressed"]) - rest) > Math.Abs(Luma(p["FeedbackIconHover"]) - rest), $"{variant}: icon pressed is not stronger than hover");
        }
    }

    [Fact]
    public void Bulwark_dark_hover_is_muted_and_light_pressed_is_darker_than_hover()
    {
        // TH-02: hover:bg-muted is #262626 in dark (275 uses), not the sidebar-accent #1A1A1A. TH-03: pressed in light is below hover.
        Assert.Equal(0xFF262626u, Tokens.Palette(ThemeVariant.Default)["Hover"]);
        Assert.Equal(0xFF262626u, Tokens.Palette(ThemeVariant.Default)["FeedbackHover"]);
        var light = Tokens.Palette(ThemeVariant.Default with { Dark = false });
        Assert.Equal(0xFFF1F5F9u, light["FeedbackHover"]);
        Assert.True(Luma(light["Pressed"]) < Luma(light["Hover"]));
    }

    [Fact]
    public void Bulwark_icon_buttons_hover_with_the_accent_tint_and_toggles_with_the_primary_tint()
    {
        var dark = Tokens.Palette(ThemeVariant.Default);
        var light = Tokens.Palette(ThemeVariant.Default with { Dark = false });
        Assert.Equal(0xFF1E3A8Au, dark["FeedbackIconHover"]);
        Assert.Equal(0xFFDBEAFEu, light["FeedbackIconHover"]);
        // toolbar.tsx: bg-primary/10, hover primary/15 (alpha 0x1A and 0x26)
        Assert.Equal(0x1A185AEAu, light["FeedbackActive"]);
        Assert.Equal(0x26185AEAu, light["FeedbackActiveHover"]);
        Assert.Equal(0x1AFAFAFAu, dark["FeedbackActive"]);
        Assert.Equal(light["Accent"], light["FeedbackActiveFg"]);
        // button.tsx:48 hover:bg-primary/90
        Assert.Equal(0xE6185AEAu, light["FeedbackPrimaryHover"]);
        Assert.Equal(dark["Ring"], dark["FeedbackFocus"]);
    }

    [Fact]
    public void Legacy_styles_keep_the_grey_hover_and_their_pressed_fill()
    {
        foreach (var nordic in new[] { false, true })
        {
            var p = Tokens.Palette(new ThemeVariant(true, false, nordic));
            Assert.Equal(p["Hover"], p["FeedbackHover"]);
            Assert.Equal(p["Hover"], p["FeedbackIconHover"]);
            Assert.Equal(p["Pressed"], p["FeedbackPressed"]);
            Assert.Equal(p["Tile2"], p["FeedbackSelected"]);
            Assert.Equal(p["Accent"], p["FeedbackPrimaryHover"]);
        }
    }

    [Fact]
    public void Feedback_dictionary_names_no_colour_and_no_duration_of_its_own()
    {
        var text = ReadRepoFile("src/Barbican.App/Styles/Feedback.xaml");
        var uncommented = Regex.Replace(text, "<!--.*?-->", string.Empty, RegexOptions.Singleline, TimeSpan.FromSeconds(1));
        Assert.DoesNotMatch(new Regex("#[0-9A-Fa-f]{6,8}\\b", RegexOptions.None, TimeSpan.FromSeconds(1)), uncommented);
        Assert.DoesNotContain("Duration=", uncommented, StringComparison.Ordinal);
        Assert.DoesNotContain("Storyboard", uncommented, StringComparison.Ordinal);
    }

    [Fact]
    public void Feedback_and_controls_only_reference_existing_resources()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in Tokens.ColorKeys)
        {
            known.Add(key);
        }

        foreach (var motion in XDocument.Parse(ReadRepoFile(Emitters.TokensXamlPath)).Root!.Elements().Select(e => (string?)e.Attribute(x + "Key")))
        {
            known.Add(motion!);
        }

        var texts = StyleFiles.Select(ReadRepoFile).ToList();
        foreach (var text in texts)
        {
            foreach (Match m in DefinedKey().Matches(text))
            {
                known.Add(m.Groups["key"].Value);
            }
        }

        foreach (var text in texts)
        {
            foreach (Match m in StaticReference().Matches(text))
            {
                var key = m.Groups["key"].Value;
                Assert.True(known.Contains(key), $"StaticResource {key} is not defined in Tokens.xaml, Feedback.xaml or Controls.xaml");
            }
        }
    }

    [GeneratedRegex("x:Key=\"(?<key>[^\"]+)\"", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DefinedKey();

    [GeneratedRegex("\\{StaticResource (?<key>[^}\\s]+)\\}|ResourceKey=\"(?<key>[^\"]+)\"", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex StaticReference();
}