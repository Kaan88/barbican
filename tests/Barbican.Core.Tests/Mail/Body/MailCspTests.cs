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
        var c = MailCsp.MetaTag(false);
        Assert.True(c.Contains("style-src 'unsafe-inline';", StringComparison.Ordinal)
            && c.Contains("font-src data:;", StringComparison.Ordinal)
            && c.Contains("img-src data:;", StringComparison.Ordinal), c);
        Assert.Contains("script-src 'none'", c, StringComparison.Ordinal);
        c = MailCsp.MetaTag(true);
        Assert.True(c.Contains("style-src 'unsafe-inline' http: https:;", StringComparison.Ordinal)
            && c.Contains("font-src http: https: data:;", StringComparison.Ordinal)
            && c.Contains("img-src http: https: data:;", StringComparison.Ordinal));
        // Fable F-03: no script runs in a frame, whatever the page (Rust allowed the dark pass by hash)
        Assert.Contains("script-src 'none'", c, StringComparison.Ordinal);
    }

    [Fact]
    public void BlockedPolicyIsExactlyTheGoldenStringOnEveryOs()
    {
        Assert.Equal(
            "default-src 'none'; style-src 'unsafe-inline'; font-src data:; img-src data:; script-src 'none'; object-src 'none'; frame-src 'none'; form-action 'none'; base-uri 'none'",
            MailCsp.Policy(false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MediaObjectsFramesAndFormsNeverLoad(bool remote)
    {
        var p = MailCsp.Policy(remote);
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
        foreach (var directive in MailCsp.Policy(false).Split(';', StringSplitOptions.TrimEntries))
        {
            Assert.DoesNotContain("http", directive, StringComparison.Ordinal);
            Assert.DoesNotContain("*", directive, StringComparison.Ordinal);
            Assert.DoesNotContain("blob:", directive, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void No_script_ever_runs_in_a_frame(bool remote) =>
        Assert.Equal("script-src 'none'", Directive(MailCsp.Policy(remote), "script-src"));

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
        Assert.Contains("form-action 'none'", ShellDocument.BasePolicy, StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", ShellDocument.BasePolicy, StringComparison.Ordinal);
        Assert.Contains("base-uri 'none'", ShellDocument.BasePolicy, StringComparison.Ordinal);
        var page = ShellDocument.Build("post('shell')", "#141414");
        Assert.StartsWith("<!doctype html><html style=\"background:#141414\">", page, StringComparison.Ordinal);
        Assert.Contains("post('shell')</script>", page, StringComparison.Ordinal);
        Assert.Contains("<meta http-equiv=\"x-dns-prefetch-control\" content=\"off\">", page, StringComparison.Ordinal);
    }

    [Fact]
    public void The_shell_runs_only_its_own_script_by_hash()
    {
        // Fable F-03: never 'unsafe-inline'; the one inline script (dark pass + shell.js) is allowed by its SHA-256 alone
        var page = ShellDocument.Build("post('shell');\r\nx()", "#141414");
        var script = ShellDocument.Script("post('shell');\r\nx()");
        Assert.DoesNotContain('\r', script);
        Assert.StartsWith(ShellDocument.DarkPass, script, StringComparison.Ordinal);
        Assert.Contains("<script>" + script + "</script>", page, StringComparison.Ordinal);
        var policy = ShellDocument.Policy(script);
        Assert.Equal($"script-src {MailCsp.ScriptSource(script)}", Directive(policy, "script-src"));
        Assert.DoesNotContain("unsafe-inline'; frame", policy, StringComparison.Ordinal);
        Assert.Contains(policy, page, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(page, "<script", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)));
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
