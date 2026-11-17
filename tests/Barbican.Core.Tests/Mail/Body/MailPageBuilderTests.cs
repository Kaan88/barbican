// Rust main.rs:229 page() (tag rust-final): which page a message gets in which theme, its CSP, script and colour. Owner body.
// Spec: B-READ-040, B-READ-041, B-READ-044 (frame CSP), B-MAIL-025..028 (dark pass), B-SET-019 (message text restyling).

using Barbican.Core.Mail.Body;
using Barbican.Core.Settings.Accessibility;

namespace Barbican.Core.Tests.Mail.Body;

public sealed class MailPageBuilderTests
{
    private static readonly MailPageOptions Dark = new() { Dark = true, ReaderBackground = "#141414" };
    private static readonly MailPageOptions Light = new() { Dark = false, ReaderBackground = "#ffffff" };

    [Fact]
    public void Plain_text_in_the_dark_theme_takes_the_reader_colours_and_runs_nothing()
    {
        var p = MailPageBuilder.Build("<pre>hi</pre>", Dark);
        Assert.Equal("#141414", p.Background);
        Assert.False(p.Dark);
        Assert.Contains("html,body{background:#141414;color:#d4d4d4}", p.Html, StringComparison.Ordinal);
        Assert.Contains("script-src 'none'", p.Html, StringComparison.Ordinal);
        Assert.DoesNotContain(DarkMail.PendingClass, p.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Coloured_html_in_the_dark_theme_gets_the_dark_pass_from_the_shell()
    {
        var body = "<table style=\"background:#fff\"><tr><td>x</td></tr></table><style>@media (prefers-color-scheme: dark){td{color:red}}</style>";
        var p = MailPageBuilder.Build(body, Dark);
        Assert.True(p.Dark);
        Assert.Equal(DarkMail.Background, p.Background);
        Assert.StartsWith($"<!doctype html><html class=\"{DarkMail.PendingClass}\" style=\"color-scheme:", p.Html, StringComparison.Ordinal);
        Assert.Contains("(min-width: 0px)", p.Html, StringComparison.Ordinal);

        // Fable F-03: no script in the frame; the shell runs the pass on the frame document
        Assert.DoesNotContain("<script", p.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("script-src 'none'", p.Html, StringComparison.Ordinal);
        Assert.Contains("x-dns-prefetch-control", p.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_sizing_travels_as_data_not_as_a_script()
    {
        var p = MailPageBuilder.Build("<p>x</p>", Light with { A11y = new A11y { TextScale = 1.25, FitWidth = true } });
        Assert.Equal(new MailSizing(1.25, 0, true), p.Sizing);
        Assert.DoesNotContain("<script", p.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Null(MailPageBuilder.Build("<p>x</p>", Light).Sizing);
    }

    [Fact]
    public void Html_in_the_light_theme_and_always_light_is_on_white()
    {
        var body = "<p style=\"color:#333\">x</p>";
        var light = MailPageBuilder.Build(body, Light);
        var always = MailPageBuilder.Build(body, Dark with { AlwaysLight = true });
        foreach (var p in new[] { light, always })
        {
            Assert.Equal("#ffffff", p.Background);
            Assert.False(p.Dark);
            Assert.Contains("color:#111;background:#fff", p.Html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Remote_content_stays_blocked_until_allowed()
    {
        var body = "<img src=\"https://tracker.example.com/p.gif\">";
        var blocked = MailPageBuilder.Build(body, Light);
        Assert.True(blocked.HasRemoteContent);
        Assert.Contains("img-src data:;", blocked.Html, StringComparison.Ordinal);
        var allowed = MailPageBuilder.Build(body, Light with { AllowRemote = true });
        Assert.Contains("img-src http: https: data:;", allowed.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_meta_refresh_never_reaches_the_frame()
    {
        var p = MailPageBuilder.Build("<META http-equiv='Refresh' content='0;url=https://x.example.com/'><p>x</p>", Light);
        Assert.DoesNotContain("refresh", p.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_accessibility_override_turns_the_dark_pass_off()
    {
        var p = MailPageBuilder.Build("<p style=\"color:red\">x</p>", Dark with { A11y = new A11y { OverrideStyles = "always" } });
        Assert.False(p.Dark);
        Assert.Equal(DarkMail.Background, p.Background);
        Assert.DoesNotContain(DarkMail.PendingClass, p.Html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("auto", "<style>table{width:100%}</style><p>x</p>", "margin:0px")]
    [InlineData("auto", "<p>x</p>", "margin:16px")]
    [InlineData("edge", "<p>x</p>", "margin:0px")]
    [InlineData("always", "<style>table{width:100%}</style>", "margin:16px")]
    public void Message_spacing(string setting, string body, string expected)
    {
        var p = MailPageBuilder.Build(body, Light with { MessageSpacing = setting });
        Assert.Contains("body{" + expected, p.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Mono_plain_text_font()
    {
        var p = MailPageBuilder.Build("<pre>x</pre>", Dark with { PlainTextFont = "mono" });
        Assert.Contains("pre{white-space:pre-wrap;font-family:ui-monospace", p.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_error_page_is_escaped_text_in_the_reader_colours()
    {
        var p = MailPageBuilder.Error("Could not load message: <boom>", Dark);
        Assert.Equal("#141414", p.Background);
        Assert.Contains("&lt;boom&gt;", p.Html, StringComparison.Ordinal);
    }

    // the approved prototype's body typography (prototype/winui/MailHtml.cs Wrap; docs/milestones/M1-look-gap.md)
    [Fact]
    public void Bento_text_is_15_px_with_1_6_line_height_and_no_measure()
    {
        var p = MailPageBuilder.Build("<pre>hi</pre>", Dark);
        Assert.Contains("font-size:15px;line-height:1.6", p.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("max-width:600px", p.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Nordic_prose_is_16_px_with_1_8_line_height_and_a_600_px_measure()
    {
        foreach (var body in new[] { "<pre>hi</pre>", "<p>hello there</p>" })
        {
            var p = MailPageBuilder.Build(body, Dark with { Nordic = true });
            Assert.Contains("font-size:16px;line-height:1.8;max-width:600px", p.Html, StringComparison.Ordinal);
        }

        var light = MailPageBuilder.Build("<p>hello there</p>", Light with { Nordic = true });
        Assert.Contains("font-size:16px;line-height:1.8;max-width:600px", light.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_coloured_layout_keeps_its_own_width_in_Nordic()
    {
        var p = MailPageBuilder.Build("<table style=\"background:#fff\" width=\"100%\"><tr><td>x</td></tr></table>", Light with { Nordic = true });
        Assert.Contains("font-size:16px;line-height:1.8", p.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("max-width:600px", p.Html, StringComparison.Ordinal);
    }
}
