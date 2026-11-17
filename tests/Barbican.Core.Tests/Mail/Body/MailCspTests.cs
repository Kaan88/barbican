using System.Security.Cryptography;
using System.Text;
using Barbican.Core.Mail.Body;

namespace Barbican.Core.Tests.Mail.Body;

/// <summary>
/// The message-frame CSP (Rust main.rs:89 csp_with). On Linux there is no request filter, so this
/// policy is the only remote-content guard (docs/adr/0005 "M0 review"); the golden string below is
/// asserted on both OSes by the same test run (tools/build.ps1 test and tools/linux.ps1 test).
/// </summary>
public sealed class MailCspTests
{
    [Fact]
    [RustTest("main::blocked_remote_content_includes_stylesheets_and_fonts")]
    public void BlockedRemoteContentIncludesStylesheetsAndFonts()
    {
        var c = MailCsp.MetaTag(false, null);
        Assert.True(c.Contains("style-src 'unsafe-inline';", StringComparison.Ordinal)
            && c.Contains("font-src data:;", StringComparison.Ordinal)
            && c.Contains("img-src data:;", StringComparison.Ordinal), c);
        Assert.Contains("script-src 'none'", c, StringComparison.Ordinal);
        c = MailCsp.MetaTag(true, "x");
        Assert.True(c.Contains("style-src 'unsafe-inline' http: https:;", StringComparison.Ordinal)
            && c.Contains("font-src http: https: data:;", StringComparison.Ordinal)
            && c.Contains("img-src http: https: data:;", StringComparison.Ordinal));
        Assert.Contains("script-src 'sha256-", c, StringComparison.Ordinal);
    }

    [Fact]
    public void BlockedPolicyIsExactlyTheGoldenStringOnEveryOs()
    {
        Assert.Equal(
            "default-src 'none'; style-src 'unsafe-inline'; font-src data:; img-src data:; script-src 'none'; object-src 'none'; frame-src 'none'; form-action 'none'; base-uri 'none'",
            MailCsp.Policy(false, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MediaObjectsFramesAndFormsNeverLoad(bool remote)
    {
        var p = MailCsp.Policy(remote, null);
        Assert.StartsWith("default-src 'none';", p, StringComparison.Ordinal);
        // media-src is not named, so <video>/<audio> fall back to default-src 'none' even with remote content on.
        Assert.DoesNotContain("media-src", p, StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", p, StringComparison.Ordinal);
        Assert.Contains("frame-src 'none'", p, StringComparison.Ordinal);
        Assert.Contains("form-action 'none'", p, StringComparison.Ordinal);
        Assert.Contains("base-uri 'none'", p, StringComparison.Ordinal);
        Assert.DoesNotContain("connect-src", p, StringComparison.Ordinal);
    }

    [Fact]
    public void WithImagesOffNoDirectiveAllowsTheNetwork()
    {
        // A background image (CSS url()), <img>, srcset, poster, a remote stylesheet or @font-face all go
        // through img-src, style-src or font-src: none of them names a network scheme or a wildcard.
        foreach (var directive in MailCsp.Policy(false, "dark()").Split(';', StringSplitOptions.TrimEntries))
        {
            Assert.DoesNotContain("http", directive, StringComparison.Ordinal);
            Assert.DoesNotContain("*", directive, StringComparison.Ordinal);
            Assert.DoesNotContain("blob:", directive, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, "document.documentElement.classList.remove('barbican-dark-pending')")]
    [InlineData(true, "x")]
    public void ScriptsRunOnlyByHashNeverInline(bool remote, string? script)
    {
        var scriptSrc = Directive(MailCsp.Policy(remote, script), "script-src");
        Assert.DoesNotContain("unsafe-inline", scriptSrc, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-eval", scriptSrc, StringComparison.Ordinal);
        Assert.DoesNotContain("http", scriptSrc, StringComparison.Ordinal);
        Assert.Equal(script is null ? "script-src 'none'" : $"script-src {MailCsp.ScriptSource(script)}", scriptSrc);
    }

    [Fact]
    public void ScriptHashIsSha256OfTheUtf8Bytes()
    {
        const string Script = "é ünïcode; dark()";
        var expected = "'sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Script))) + "'";
        Assert.Equal(expected, MailCsp.ScriptSource(Script));
        Assert.False(string.Equals(MailCsp.ScriptSource(Script), MailCsp.ScriptSource(Script + " "), StringComparison.Ordinal));
    }

    [Fact]
    public void ShellPolicyIsTheCeilingAndNeverNavigatesForms()
    {
        Assert.Contains("form-action 'none'", ShellDocument.Policy, StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", ShellDocument.Policy, StringComparison.Ordinal);
        Assert.Contains("base-uri 'none'", ShellDocument.Policy, StringComparison.Ordinal);
        var page = ShellDocument.Build("post('shell')", "#141414");
        Assert.StartsWith("<!doctype html><html style=\"background:#141414\">", page, StringComparison.Ordinal);
        Assert.Contains("<script>post('shell')</script>", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#141414\"><script>alert(1)</script>")]
    [InlineData("")]
    public void ShellBackgroundMustBeAHexColour(string bg) =>
        Assert.Throws<ArgumentException>(() => ShellDocument.Build("x", bg));

    [Fact]
    public void ShellScriptCannotCloseItsOwnTag() =>
        Assert.Throws<ArgumentException>(() => ShellDocument.Build("a</SCRIPT><b>", "#000"));

    private static string Directive(string policy, string name) =>
        policy.Split(';', StringSplitOptions.TrimEntries).Single(d => d.StartsWith(name + " ", StringComparison.Ordinal));
}
