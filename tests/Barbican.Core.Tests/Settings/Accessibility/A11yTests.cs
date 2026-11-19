// Ported from Rust src/a11y.rs tests (:265-:314) at tag rust-final. Red until the mailcore stream implements A11y.

using System.Text.Json.Nodes;
using Barbican.Core.Settings.Accessibility;
using Barbican.Core.Storage;

namespace Barbican.Core.Tests.Settings.Accessibility;

public sealed class A11yTests
{
    private static PrefsSnapshot Settings(string json) => PrefsSnapshot.FromJson(JsonNode.Parse(json)!.AsObject());

    [Fact]
    [RustTest("a11y::defaults_follow_bulwark")]
    public void Defaults_follow_bulwark()
    {
        var a = A11y.From(Settings("{}"), default);
        Assert.False(a.MoreContrast);
        Assert.False(a.ReduceMotion);
        Assert.False(a.LargeTargets);
        Assert.False(a.UnderlineLinks);
        Assert.Equal(0, a.FocusRing);
        Assert.True(a.SingleKeys);
        Assert.Equal(5, a.ToastSecs);
        Assert.Equal("count", a.UnreadStyle);
        Assert.Null(a.MailScript());
        Assert.Equal("", a.MailCss(true));

        // "system" follows the OS
        var s = A11y.From(Settings("{}"), new SystemAccessibility(HighContrast: true, ReduceMotion: true, Touch: true));
        Assert.True(s.MoreContrast && s.ReduceMotion && s.LargeTargets);
        var off = A11y.From(
            Settings("""{"contrast": "normal", "reduceMotion": "off", "largeTargets": "off"}"""),
            new SystemAccessibility(HighContrast: true, ReduceMotion: true, Touch: true));
        Assert.False(off.MoreContrast);
        Assert.False(off.ReduceMotion);
        Assert.False(off.LargeTargets);
        Assert.Contains(new KeyValuePair<string, string>("focusRing", "off"), A11y.Defaults);
        Assert.Contains("fontSize", A11y.ResetKeys(), StringComparer.Ordinal);
        Assert.DoesNotContain("doNotDisturb", A11y.ResetKeys(), StringComparer.Ordinal);
    }

    [Fact]
    public void Quick_panel_preferences_map_to_live_accessibility_model()
    {
        var a = A11y.From(Settings("""{"fontSize":"xx-large","appFont":"lexend","focusRing":"thick","largeTargets":"on","showAccessibilityButton":true}"""), default);
        Assert.Equal(1.5, a.FontScaleFactor);
        Assert.Equal("lexend", a.AppFont);
        Assert.Equal(2, a.FocusRing);
        Assert.True(a.LargeTargets);
        Assert.True(a.ShowAccessibilityButton);
    }

    [Fact]
    [RustTest("a11y::mail_css_and_override")]
    public void Mail_css_and_override()
    {
        var a = A11y.From(Settings("""{"underlineLinks": true, "readingWidth": "comfortable", "letterSpacing": "wide"}"""), default);
        var css = a.MailCss(false);
        Assert.Contains("text-decoration-line:underline", css, StringComparison.Ordinal);
        Assert.Contains("max-width:80ch", css, StringComparison.Ordinal);
        Assert.Contains("letter-spacing:0.05em", css, StringComparison.Ordinal);

        var o = A11y.From(Settings("""{"mailOverrideStyles": "high-contrast", "contrast": "more", "mailMinFontSize": 16}"""), default);
        Assert.True(o.OverrideActive());
        css = o.MailCss(true);
        Assert.Contains("color:#ffffff", css, StringComparison.Ordinal);
        Assert.Contains("max(calc(0.875rem * 1), 16px)", css, StringComparison.Ordinal);
        // override sizes come from CSS, so no sizing script
        Assert.Null(o.MailScript());

        var n = A11y.From(
            Settings("""{"mailOverrideStyles": "high-contrast", "contrast": "normal"}"""),
            new SystemAccessibility(HighContrast: true, ReduceMotion: false, Touch: false));
        Assert.False(n.OverrideActive());

        var s = A11y.From(Settings("""{"mailTextScale": 1.25}"""), default);
        Assert.Contains("f=1.25", s.MailScript()!, StringComparison.Ordinal);

        var f = A11y.From(Settings("""{"fontSize": "large"}"""), default);
        Assert.Equal(1.125, f.TextFactor(), 6);
    }

    [Fact]
    [RustTest("a11y::cvd_palette")]
    public void Cvd_palette()
    {
        Assert.Equal(0xD55E00u, A11y.CvdColor(0xef4444)); // red
        Assert.Equal(0xE69F00u, A11y.CvdColor(0xf97316)); // orange
        Assert.Equal(0xF0E442u, A11y.CvdColor(0xeab308)); // yellow
        Assert.Equal(0x009E73u, A11y.CvdColor(0x22c55e)); // green
        Assert.Equal(0x0072B2u, A11y.CvdColor(0x3b82f6)); // blue
        Assert.Equal(0xCC79A7u, A11y.CvdColor(0xa855f7)); // purple
        Assert.Equal(0x999999u, A11y.CvdColor(0x9ca3af)); // grey
    }
}
