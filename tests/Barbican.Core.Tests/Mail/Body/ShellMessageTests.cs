using Barbican.Core.Mail.Body;

namespace Barbican.Core.Tests.Mail.Body;

/// <summary>
/// Host-side validation of page → host messages (docs/adr/0005 "M0 review"): on Linux they come from
/// webkit.messageHandlers.unoWebView in a page that also hosts untrusted mail frames, so everything
/// outside the allow-list, malformed or oversized is dropped and counted.
/// </summary>
public sealed class ShellMessageTests
{
    [Fact]
    public void AcceptsEveryMessageTheShellSends()
    {
        Assert.Equal(new ShellMessage(ShellMessageKind.Shell), Parse("shell"));
        Assert.Equal(new ShellMessage(ShellMessageKind.Dirty), Parse("dirty"));
        Assert.Equal(new ShellMessage(ShellMessageKind.Ready, Sequence: 42), Parse("ready:42"));
        Assert.Equal(new ShellMessage(ShellMessageKind.Key, Key: "cs|m"), Parse("key:cs|m"));
        Assert.Equal(new ShellMessage(ShellMessageKind.Key, Key: "|Escape"), Parse("key:|Escape"));
        Assert.Equal(new ShellMessage(ShellMessageKind.Context, X: 12, Y: -3), Parse("ctx:12,-3"));
        Assert.Equal(new Uri("https://example.org/a?b=c#d"), Parse("link:https://example.org/a?b=c#d")!.Link);
        Assert.Equal("mailto", Parse("link:mailto:someone@example.org?subject=Hi")!.Link!.Scheme);
        Assert.Equal("http", Parse("link:HTTP://example.org/")!.Link!.Scheme);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Shell")]
    [InlineData("shell ")]
    [InlineData(" shell")]
    [InlineData("dim")]
    [InlineData("ready:")]
    [InlineData("ready:0")]
    [InlineData("ready:-1")]
    [InlineData("ready:+1")]
    [InlineData("ready: 1")]
    [InlineData("ready:1x")]
    [InlineData("ready:1234567890123456789")]
    [InlineData("key:")]
    [InlineData("key:|")]
    [InlineData("key:nokey")]
    [InlineData("key:x|j")]
    [InlineData("key:sc|m")]
    [InlineData("key:cc|m")]
    [InlineData("key:|a b")]
    [InlineData("key:|\u0007")]
    [InlineData("ctx:1")]
    [InlineData("ctx:1,2,3")]
    [InlineData("ctx:a,b")]
    [InlineData("ctx:1.5,2")]
    [InlineData("ctx:99999999,1")]
    [InlineData("ctx:,1")]
    [InlineData("link:")]
    [InlineData("link:javascript:alert(1)")]
    [InlineData("link:JavaScript:alert(1)")]
    [InlineData("link:data:text/html,<b>x</b>")]
    [InlineData("link:file:///etc/passwd")]
    [InlineData("link:about:blank")]
    [InlineData("link:blob:https://example.org/uuid")]
    [InlineData("link:ms-settings:privacy")]
    [InlineData("link:/relative/path")]
    [InlineData("link:https://example.org/a b")]
    [InlineData("link:https://example.org/\r\nx")]
    [InlineData("link:https://exa\u0000mple.org/")]
    [InlineData("navigate:https://example.org/")]
    [InlineData("{\"op\":\"shell\"}")]
    public void RejectsAnythingElse(string? raw)
    {
        var gate = new ShellMessageGate();
        Assert.False(gate.TryAccept(raw, out var message));
        Assert.Null(message);
        Assert.Equal(1, gate.Dropped);
    }

    [Fact]
    public void OversizedMessagesAreDroppedBeforeParsing()
    {
        var gate = new ShellMessageGate();
        var longLink = "link:https://example.org/" + new string('a', ShellMessage.MaxLength);
        Assert.False(gate.TryAccept(longLink, out _));
        Assert.False(gate.TryAccept("key:|" + new string('k', ShellMessage.MaxKeyLength), out _));
        Assert.False(gate.TryAccept(new string('x', 1_000_000), out _));
        Assert.Equal(3, gate.Dropped);
        Assert.True(gate.TryAccept("link:https://example.org/" + new string('a', 4000), out var ok));
        Assert.Equal(ShellMessageKind.Link, ok.Kind);
        Assert.Equal(3, gate.Dropped);
    }

    [Fact]
    public void GateCountsOnlyDrops()
    {
        var gate = new ShellMessageGate();
        foreach (var m in new[] { "shell", "ready:1", "bogus", "dirty", "link:ftp://example.org/", "ctx:0,0" })
        {
            _ = gate.TryAccept(m, out _);
        }

        Assert.Equal(2, gate.Dropped);
    }

    private static ShellMessage? Parse(string raw) => ShellMessage.TryParse(raw, out var m) ? m : null;
}
