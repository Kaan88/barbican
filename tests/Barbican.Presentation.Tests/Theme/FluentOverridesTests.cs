using System.Text.RegularExpressions;
using System.Xml.Linq;
using Barbican.GenTokens;
using Barbican.Presentation.Theme;

namespace Barbican.Presentation.Tests.Theme;

/// <summary>
/// The Fluent control overrides (Styles/FluentOverrides.xaml; ledger FM-19, FM-20, FM-21, ST-03, TH-07): they draw from the shared palette and state brushes only, name no
/// colour and no duration of their own, cover every stock control the work package names, and restrain their motion.
/// </summary>
public sealed partial class FluentOverridesTests
{
    private static readonly string Root = RepoRoot.Find(AppContext.BaseDirectory);
    private const string OverridesPath = "src/Barbican.App/Styles/FluentOverrides.xaml";

    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string ReadRepoFile(string relative) => File.ReadAllText(Path.Combine(Root, relative)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Uncommented() => Regex.Replace(ReadRepoFile(OverridesPath), "<!--.*?-->", string.Empty, RegexOptions.Singleline, TimeSpan.FromSeconds(1));

    [Fact]
    public void Overrides_name_no_colour_and_no_literal_duration()
    {
        var text = Uncommented();
        Assert.DoesNotMatch(HexColour(), text);
        // every length is a resource: the Control*AnimationDuration keys follow the motion tokens and reduce motion (Motion/FluentMotion.cs)
        foreach (Match m in KeyTimeValue().Matches(text))
        {
            Assert.True(m.Groups["v"].Value is "0" or "0:0:0" || m.Groups["v"].Value.StartsWith("{StaticResource Control", StringComparison.Ordinal), $"literal KeyTime {m.Groups["v"].Value}");
        }

        foreach (Match m in DurationValue().Matches(text))
        {
            Assert.Equal("0", m.Groups["v"].Value);
        }
    }

    [Fact]
    public void Overrides_only_reference_resources_that_exist()
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in Tokens.ColorKeys)
        {
            known.Add(key);
        }

        foreach (var path in new[] { Emitters.TokensXamlPath, "src/Barbican.App/Styles/Feedback.xaml", "src/Barbican.App/Styles/Controls.xaml", OverridesPath })
        {
            foreach (var element in XDocument.Parse(ReadRepoFile(path)).Descendants())
            {
                if ((string?)element.Attribute(X + "Key") is { } key)
                {
                    known.Add(key);
                }
            }
        }

        // the radii are written by Theme/FluentShape.cs and the Fluent defaults come from the control library (DefaultTextBoxStyle ...): both are allowed by name
        var runtime = new HashSet<string>(StringComparer.Ordinal) { "Fluent.FieldRadius", "Fluent.MenuRadius", "ControlFastAnimationDuration", "ToggleSwitchTopHeaderMargin" };
        foreach (Match m in AnyReference().Matches(Uncommented()))
        {
            var key = m.Groups["key"].Value;
            if (runtime.Contains(key) || key.StartsWith("Default", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.True(known.Contains(key), $"{key} is not defined in Tokens.xaml, Feedback.xaml, Controls.xaml or FluentOverrides.xaml");
        }
    }

    [Fact]
    public void Radii_the_overrides_read_are_written_by_the_shape_class()
    {
        var shape = ReadRepoFile("src/Barbican.App/Theme/FluentShape.cs");
        foreach (Match m in ThemeRadius().Matches(Uncommented()))
        {
            Assert.Contains($"\"{m.Groups["key"].Value}\"", shape, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("TextBox")]
    [InlineData("PasswordBox")]
    [InlineData("ToggleSwitch")]
    [InlineData("MenuFlyoutPresenter")]
    public void Every_restyled_control_has_an_implicit_style_on_the_stock_default(string target)
    {
        Assert.Matches(new Regex($"<Style TargetType=\"{target}\" BasedOn=\"\\{{StaticResource Default{target}Style\\}}\"", RegexOptions.None, TimeSpan.FromSeconds(1)), ReadRepoFile(OverridesPath));
    }

    [Theory]
    [InlineData("CheckBoxCheckBackgroundFillChecked")]
    [InlineData("CheckBoxCheckBackgroundStrokeUnchecked")]
    [InlineData("RadioButtonOuterEllipseCheckedFill")]
    [InlineData("ScrollBarThumbFill")]
    [InlineData("ScrollBarThumbFillPointerOver")]
    [InlineData("ProgressBarForeground")]
    [InlineData("ProgressBarBackground")]
    [InlineData("ExpanderHeaderBackground")]
    [InlineData("ExpanderChevronForeground")]
    [InlineData("MenuFlyoutPresenterBackground")]
    [InlineData("MenuFlyoutItemBackgroundPointerOver")]
    [InlineData("TextControlBorderBrushFocused")]
    public void Named_control_resources_are_overridden(string key)
    {
        Assert.Contains($"x:Key=\"{key}\"", ReadRepoFile(OverridesPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Switch_is_the_bulwark_size_and_its_travel_matches_the_knob_cell()
    {
        // settings-section.tsx:384-410: h-6 w-11 track, h-4 w-4 knob, translate-x-1 off and translate-x-6 on (a 20 px travel)
        var text = Uncommented();
        Assert.Contains("x:Name=\"OffTrack\" Width=\"44\" Height=\"24\" CornerRadius=\"12\"", text, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SwitchKnob\" HorizontalAlignment=\"Left\" Width=\"24\" Height=\"24\"", text, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"KnobOff\" Width=\"16\" Height=\"16\" CornerRadius=\"8\"", text, StringComparison.Ordinal);
        Assert.Equal(20, 44 - 24);
        Assert.Contains("To=\"20\"", text, StringComparison.Ordinal);
        // the control still finds the parts it drives
        foreach (var part in new[] { "SwitchKnob", "SwitchKnobBounds", "KnobTranslateTransform", "SwitchThumb", "OffContentPresenter", "OnContentPresenter", "HeaderContentPresenter" })
        {
            Assert.Contains($"x:Name=\"{part}\"", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Text_box_keeps_the_parts_the_control_drives_and_draws_the_bulwark_field()
    {
        var text = Uncommented();
        foreach (var part in new[] { "ContentElement", "PlaceholderTextContentPresenter", "HeaderContentPresenter", "BorderElement" })
        {
            Assert.Contains($"x:Name=\"{part}\"", text, StringComparison.Ordinal);
        }

        // input.tsx:13-18: border-input at rest, hover:border-muted-foreground, focus-visible:border-ring, no thicker focus line, no clear button
        Assert.Contains("<StaticResource x:Key=\"TextControlBorderBrush\" ResourceKey=\"Input\" />", text, StringComparison.Ordinal);
        Assert.Contains("<StaticResource x:Key=\"TextControlBorderBrushPointerOver\" ResourceKey=\"Dim\" />", text, StringComparison.Ordinal);
        Assert.Contains("<StaticResource x:Key=\"TextControlBorderBrushFocused\" ResourceKey=\"FeedbackFocus\" />", text, StringComparison.Ordinal);
        Assert.Contains("<Thickness x:Key=\"TextControlBorderThemeThicknessFocused\">1</Thickness>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteButton", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Motion_is_restrained_no_scale_no_bounce_no_overshoot()
    {
        var text = Uncommented();
        foreach (var banned in new[] { "ScaleTransform", "CompositeTransform", "ScaleX", "ScaleY", "BounceEase", "ElasticEase", "BackEase", "SpringAnimation", "RepositionThemeAnimation", "PopInThemeAnimation" })
        {
            Assert.DoesNotContain(banned, text, StringComparison.Ordinal);
        }

        // the one curve is Tailwind's default ease (0.4, 0, 0.2, 1): no spline control point leaves the 0..1 box, so nothing overshoots
        foreach (Match m in KeySpline().Matches(text))
        {
            Assert.Equal("0.4,0 0.2,1", m.Groups["s"].Value);
        }
    }

    [Fact]
    public void Merge_line_is_after_the_controls_and_the_shape_follows_the_style()
    {
        var resources = ReadRepoFile("src/Barbican.App/Theme/AppResources.cs");
        var controls = resources.IndexOf("Styles/Controls.xaml", StringComparison.Ordinal);
        var overrides = resources.IndexOf("Styles/FluentOverrides.xaml", StringComparison.Ordinal);
        Assert.True(controls >= 0 && overrides > controls, "FluentOverrides.xaml must merge after Controls.xaml (it reads the Feedback brushes)");
        Assert.Contains("FluentShape.Apply(style)", resources, StringComparison.Ordinal);
    }

    [Fact]
    public void Header_cites_the_bulwark_sources()
    {
        var text = ReadRepoFile(OverridesPath);
        foreach (var source in new[] { "settings-section.tsx:374-410", "input.tsx:13-18", "globals.css:296-330", "account-storage.tsx:67", "context-menu.tsx:663-667" })
        {
            Assert.Contains(source, text, StringComparison.Ordinal);
        }
    }

    [GeneratedRegex("#[0-9A-Fa-f]{6,8}\\b", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HexColour();

    [GeneratedRegex("KeyTime=\"(?<v>[^\"]+)\"", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex KeyTimeValue();

    [GeneratedRegex("Duration=\"(?<v>[^\"]+)\"", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DurationValue();

    [GeneratedRegex("\\{(?:Static|Theme)Resource (?<key>[^}\\s]+)\\}|ResourceKey=\"(?<key>[^\"]+)\"", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AnyReference();

    [GeneratedRegex("\\{ThemeResource (?<key>Fluent\\.[A-Za-z]+)\\}", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ThemeRadius();

    [GeneratedRegex("KeySpline=\"(?<s>[^\"]+)\"", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex KeySpline();
}
