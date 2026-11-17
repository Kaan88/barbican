// Ported from Rust src/dark_mail.rs tests (:113-:147). Red until the body stream implements DarkMail.

using Barbican.Core.Mail.Body;

namespace Barbican.Core.Tests.Mail.Body;

public sealed class DarkMailTests
{
    [Fact]
    [RustTest("dark_mail::native_dark_detection")]
    [Red("body")]
    public void Native_dark_detection()
    {
        Assert.True(DarkMail.HasNativeDark("<style>@media (prefers-color-scheme: dark){body{background:#000}}</style>"));
        Assert.True(DarkMail.HasNativeDark("<STYLE>@media (PREFERS-COLOR-SCHEME:DARK){}</STYLE>"));
        Assert.False(DarkMail.HasNativeDark("<style>@media (prefers-color-scheme: light){}</style>"));
        Assert.False(DarkMail.HasNativeDark("<p>no styles</p>"));
        Assert.Equal("dark", DarkMail.ColorScheme(true, true));
        Assert.Equal("light", DarkMail.ColorScheme(true, false));
        Assert.Equal("light", DarkMail.ColorScheme(false, true));
    }

    [Fact]
    [RustTest("dark_mail::pins_media_queries_to_the_app_theme")]
    [Red("body")]
    public void Pins_media_queries_to_the_app_theme()
    {
        const string h = "<style>@media (prefers-color-scheme: dark){a{}} @media ( prefers-color-scheme : light ){b{}}</style><p>(prefers-color-scheme: dark)</p>";
        var d = DarkMail.ResolveColorSchemeMedia(h, true);
        Assert.Equal("<style>@media (min-width: 0px){a{}} @media (max-width: 0.001px){b{}}</style><p>(prefers-color-scheme: dark)</p>", d);
        var l = DarkMail.ResolveColorSchemeMedia(h, false);
        Assert.StartsWith("<style>@media (max-width: 0.001px){a{}} @media (min-width: 0px){b{}}", l, StringComparison.Ordinal);

        // <style media=…> and several blocks; a <styles> tag is not a style block
        var m = DarkMail.ResolveColorSchemeMedia(
            "<styles>(prefers-color-scheme: dark)</styles><style type=\"text/css\">(prefers-color-scheme:dark)</style><style>(prefers-color-scheme: light)</style>",
            true);
        Assert.Equal(
            "<styles>(prefers-color-scheme: dark)</styles><style type=\"text/css\">(min-width: 0px)</style><style>(max-width: 0.001px)</style>",
            m);
        Assert.Equal("<p>plain</p>", DarkMail.ResolveColorSchemeMedia("<p>plain</p>", true));
    }

    [Fact]
    [RustTest("dark_mail::css_hides_until_the_pass_ran")]
    [Red("body")]
    public void Css_hides_until_the_pass_ran()
    {
        var c = DarkMail.Css();
        Assert.Contains("html.barbican-dark-pending body{visibility:hidden", c, StringComparison.Ordinal);
        Assert.Contains("body{color:#e6e6e6;background:#0a0a0a}", c, StringComparison.Ordinal);

        // the script looks for the same canvas colour and pending class
        Assert.Contains("{ r: 10, g: 10, b: 10, a: 1 }", DarkMail.Script, StringComparison.Ordinal);
        Assert.Contains($"'{DarkMail.PendingClass}'", DarkMail.Script, StringComparison.Ordinal);
    }
}
