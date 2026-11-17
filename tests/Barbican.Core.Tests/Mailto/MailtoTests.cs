#pragma warning disable MA0002, MA0006 // asserts compare strings ordinally by nature (the ported Rust asserts)
using Barbican.Core.Mailto;

namespace Barbican.Core.Tests.Mailto;

/// <summary>The 5 tests of Rust src/mailto.rs, ported (M3 stream composer-ui). Addresses are fictional.</summary>
public sealed class MailtoTests
{
    [Fact]
    [Red("composer-ui")]
    [RustTest("mailto::parse_rfc6068")]
    public void Parse_rfc6068()
    {
        var m = MailtoLink.Parse("mailto:a@x.example,%22Doe,%20J%22%20%3Cj@d.o%3E?cc=c@x.example&CC=d@x.example&bcc=e@x.example&subject=Hi%20there+you&body=Line%201%0D%0ALine%202%07")!;
        Assert.Equal(["a@x.example", "\"Doe, J\" <j@d.o>"], m.To);
        Assert.Equal(["c@x.example", "d@x.example"], m.Cc);
        Assert.Equal(["e@x.example"], m.Bcc);
        Assert.Equal("Hi there+you", m.Subject);
        Assert.Equal("Line 1\nLine 2", m.Body);
        Assert.Equal(["z@x.example"], MailtoLink.Parse("MAILTO:?to=z@x.example")!.To);
        Assert.Null(MailtoLink.Parse("http://x"));

        // a stray % and broken UTF-8 do not lose the link
        Assert.Equal("50% off%2", MailtoLink.Parse("mailto:a@x.example?subject=50%25%20off%2")!.Subject);
        Assert.Equal("\uFFFD", MailtoLink.Parse("mailto:a@x.example?subject=%FF")!.Subject);

        // control characters in the subject are dropped
        Assert.Equal("ab", MailtoLink.Parse("mailto:a@x.example?subject=a%0Ab")!.Subject);
    }

    [Fact]
    [Red("composer-ui")]
    [RustTest("mailto::limits")]
    public void Limits()
    {
        var many = string.Join(',', Enumerable.Range(0, 250).Select(i => $"u{i}@x.example"));
        var m = MailtoLink.Parse($"mailto:{many}?cc=c@x.example")!;
        Assert.Equal(200, m.To.Count);
        Assert.Empty(m.Cc);
    }

    [Fact]
    [Red("composer-ui")]
    [RustTest("mailto::registration_plans")]
    public void Registration_plans()
    {
        var plan = DefaultMailApp.WindowsPlan(@"C:\Apps\Barbican\barbican.exe");
        string? Get(string k, string n) => plan.FirstOrDefault(x => x.Key == k && x.Name == n)?.Value;
        Assert.Equal("\"C:\\Apps\\Barbican\\barbican.exe\" \"%1\"", Get(@"HKCU\Software\Classes\Barbican.Url.mailto\shell\open\command", ""));
        Assert.Equal("", Get(@"HKCU\Software\Classes\Barbican.Url.mailto", "URL Protocol"));
        Assert.Equal(DefaultMailApp.ProgId, Get(@"HKCU\Software\Clients\Mail\Barbican\Capabilities\URLAssociations", "mailto"));
        Assert.Equal(@"Software\Clients\Mail\Barbican\Capabilities", Get(@"HKCU\Software\RegisteredApplications", "Barbican"));

        // everything stays in the user's own hive
        Assert.All(plan, x => Assert.StartsWith(@"HKCU\Software\", x.Key, StringComparison.Ordinal));
        var d = DefaultMailApp.DesktopEntry("/opt/bar bican/$bin");
        Assert.Contains("MimeType=x-scheme-handler/mailto;\n", d, StringComparison.Ordinal);
        Assert.Contains("Exec=\"/opt/bar bican/\\$bin\" %u\n", d, StringComparison.Ordinal);
        var exec = d.Split('\n').Select(l => l.StartsWith("Exec=", StringComparison.Ordinal) ? l["Exec=".Length..] : null).First(l => l is not null)!;
        Assert.True(DefaultMailApp.CommandMatches(DefaultMailApp.DesktopUnquote(exec), "/opt/bar bican/$bin"));

        // removal touches only what registration wrote
        var un = DefaultMailApp.WindowsUnplan();
        Assert.All(un, x => Assert.StartsWith(@"HKCU\Software\", x.Key, StringComparison.Ordinal));
        Assert.All(un, x => Assert.Contains(plan, p => p.Key.StartsWith(x.Key, StringComparison.Ordinal) && (x.Name.Length == 0 || p.Name == x.Name)));
    }

    [Fact]
    [Red("composer-ui")]
    [RustTest("mailto::moved_exe_is_detected")]
    public void Moved_exe_is_detected()
    {
        const string cmd = "\"C:\\Old\\barbican.exe\" \"%1\"";
        Assert.True(DefaultMailApp.CommandMatches(cmd, @"C:\Old\barbican.exe"));
        Assert.False(DefaultMailApp.CommandMatches(cmd, @"C:\New\barbican.exe"));
        Assert.True(DefaultMailApp.CommandMatches("/usr/bin/barbican %u", "/usr/bin/barbican"));
    }

    [Fact]
    [Red("composer-ui")]
    [RustTest("mailto::fragments_and_pending")]
    public void Fragments_and_pending()
    {
        Assert.Equal(["a@x.example"], MailtoLink.Parse("mailto:a@x.example#x")!.To);
        Assert.Equal("Hi", MailtoLink.Parse("mailto:a@x.example?subject=Hi#frag")!.Subject);

        // an encoded # stays
        Assert.Equal("#one", MailtoLink.Parse("mailto:a@x.example?subject=%23one")!.Subject);
        var list = new List<MailtoFields>();
        for (var i = 0; i < 8; i++)
        {
            MailtoLink.KeepPending(list, new MailtoFields { Subject = i.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }

        Assert.Equal(MailtoLink.MaxPending, list.Count);
        Assert.Equal("3", list[0].Subject);
        Assert.Equal("Sign in to write to a@x.example.", MailtoLink.SignInMessage(MailtoLink.Parse("mailto:a@x.example")!));
    }
}
