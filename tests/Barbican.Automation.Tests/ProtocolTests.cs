using Barbican.App.Composition;
using Barbican.Automation.Protocol;
using System.Text.Json;

namespace Barbican.Automation.Tests;

/// <summary>The UI-free parts of the automation protocol (spec/automation-protocol.md) and the smoke report.</summary>
public sealed class ProtocolTests
{
    [Theory]
    [InlineData("size", "size", "")]
    [InlineData("click 10 20", "click", "10 20")]
    [InlineData("type hello  world\r\n", "type", "hello  world")]
    [InlineData("wv-eval document.title", "wv-eval", "document.title")]
    public void ParsesNameAndArgument(string line, string name, string argument)
    {
        var c = CommandLine.Parse(line);
        Assert.Equal(name, c.Name);
        Assert.Equal(argument, c.Argument);
    }

    [Fact]
    public void NumbersAndTokensFallBack()
    {
        var c = CommandLine.Parse("burst 1500 x out");
        Assert.Equal(1500, c.Number(0));
        Assert.Equal(16, c.Number(1, 16));
        Assert.Equal("out", c.Token(2));
        Assert.Equal("d", c.Token(9, "d"));
        Assert.Equal(7, c.Number(9, 7));
    }

    [Fact]
    public void RepliesAreOneLine() => Assert.Equal("a b c", CommandLine.OneLine("a\r\nb\nc").Replace("  ", " ", StringComparison.Ordinal));

    [Theory]
    [InlineData("click 100+10i 50", 0, "click 100 50")]
    [InlineData("click 100+10i 50", 3, "click 130 50")]
    [InlineData("scroll 5 5 -20+2.5i", 2, "scroll 5 5 -15")]
    [InlineData("type a+1i", 1, "type a+1i")]
    public void EveryExpandsSteps(string command, int i, string expected) => Assert.Equal(expected, EveryExpansion.Expand(command, i));

    [Fact]
    public void GuardClosesOnlyUnconstrainedPopups()
    {
        var (close, error) = PopupGuardPolicy.Evaluate([new("MenuFlyoutPresenter", true), new("ComboBox", false), new("", false)]);
        Assert.Equal([1, 2], close);
        Assert.Equal("error unconstrained-popup ComboBox", error);
    }

    [Fact]
    public void GuardIsQuietWhenAllPopupsAreConstrained()
    {
        var (close, error) = PopupGuardPolicy.Evaluate([new("FlyoutPresenter", true)]);
        Assert.Empty(close);
        Assert.Null(error);
        Assert.Equal("ok", PopupGuardPolicy.Reply("ok", error));
    }

    [Fact]
    public void GuardErrorReplacesTheReply() =>
        Assert.Equal("error unconstrained-popup Popup", PopupGuardPolicy.Reply("ok", "error unconstrained-popup Popup"));

    [Fact]
    public void DeniedAndConstrainedListsDoNotOverlap() =>
        Assert.Empty(PopupControls.Denied.Intersect(PopupControls.MustConstrain, StringComparer.Ordinal));

    [Fact]
    public void SmokeReportIsVersionedJson()
    {
        var json = new SmokeReport(true, null, "Release", "win", 812.345, 1201.2, null, 0).ToJson();
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        Assert.Equal(SmokeReport.SchemaVersion, r.GetProperty("schema").GetInt32());
        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.Equal(JsonValueKind.Null, r.GetProperty("error").ValueKind);
        Assert.Equal("Release", r.GetProperty("configuration").GetString());
        Assert.Equal("win", r.GetProperty("head").GetString());
        Assert.Equal(812.3, r.GetProperty("processToLaunchedMs").GetDouble());
        Assert.Equal(1201.2, r.GetProperty("processToFirstFrameMs").GetDouble());
        Assert.Equal(JsonValueKind.Null, r.GetProperty("processToShellReadyMs").ValueKind);
        Assert.Equal(0, r.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public void SmokeReportCarriesTheError()
    {
        using var doc = JsonDocument.Parse(new SmokeReport(false, "no \"display\"", "Debug", "desktop", 1, null, null, 2).ToJson());
        Assert.Equal("no \"display\"", doc.RootElement.GetProperty("error").GetString());
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
    }
}
