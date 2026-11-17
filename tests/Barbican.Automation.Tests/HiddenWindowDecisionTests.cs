using Barbican.App.Composition;

namespace Barbican.Automation.Tests;

/// <summary>
/// The window mode is decided once, before any window exists (docs/adr/0007 decision 1, "M0 review"):
/// an AUTOMATION build and a <c>--smoke</c> run never take the normal (visible, activated) path, on
/// any OS, with any environment. Where hiding is impossible the decision carries an error and the
/// App exits instead of showing a window.
/// </summary>
public sealed class HiddenWindowDecisionTests
{
    private static readonly string[] NoArgs = [];

    public static TheoryData<bool, bool, bool, bool, bool> HiddenRuns()
    {
        // automationBuild, smoke, isWindows, canHide, xvfb
        var data = new TheoryData<bool, bool, bool, bool, bool>();
        foreach (var automation in new[] { true, false })
        {
            foreach (var smoke in new[] { true, false })
            {
                if (!automation && !smoke)
                {
                    continue;
                }

                foreach (var windows in new[] { true, false })
                {
                    foreach (var canHide in new[] { true, false })
                    {
                        foreach (var xvfb in new[] { true, false })
                        {
                            data.Add(automation, smoke, windows, canHide, xvfb);
                        }
                    }
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(HiddenRuns))]
    public void AutomationAndSmokeNeverTakeTheNormalPath(bool automation, bool smoke, bool windows, bool canHide, bool xvfb)
    {
        var args = smoke ? new[] { "--smoke", "report.json" } : NoArgs;
        var env = xvfb ? Env(("BARBICAN_XVFB", "1"), ("DISPLAY", ":97")) : Env();
        var o = HiddenWindowOptions.Decide(args, env, automation, windows, canHide);
        Assert.NotEqual(HiddenWindowMode.Normal, o.Mode);
        Assert.True(o.IsHidden);
        var hidable = (windows && canHide) || (!windows && xvfb);
        if (hidable)
        {
            Assert.Null(o.Error);
        }
        else
        {
            Assert.NotNull(o.Error);
        }
    }

    [Fact]
    public void WindowsHidesOffScreen()
    {
        var o = HiddenWindowOptions.Decide(NoArgs, Env(), automationBuild: true, isWindows: true, canHideWindow: true);
        Assert.Equal(HiddenWindowMode.OffScreen, o.Mode);
        Assert.Null(o.Error);
    }

    [Fact]
    public void WindowsIgnoresXvfbVariables()
    {
        // On Windows the off-screen window is the only hidden mode, even with the Linux variables set.
        var o = HiddenWindowOptions.Decide(NoArgs, Env(("BARBICAN_XVFB", "1"), ("DISPLAY", ":97")), automationBuild: true, isWindows: true, canHideWindow: false);
        Assert.Equal(HiddenWindowMode.OffScreen, o.Mode);
        Assert.NotNull(o.Error);
    }

    [Theory]
    [InlineData("1", ":97", true)]
    [InlineData("1", "", false)]
    [InlineData("1", null, false)]
    [InlineData("0", ":97", false)]
    [InlineData("true", ":97", false)]
    [InlineData(null, ":0", false)]
    public void LinuxNeedsTheAnnouncedPrivateXvfb(string? xvfb, string? display, bool ok)
    {
        var o = HiddenWindowOptions.Decide(NoArgs, Env(("BARBICAN_XVFB", xvfb), ("DISPLAY", display)), automationBuild: true, isWindows: false, canHideWindow: false);
        Assert.NotEqual(HiddenWindowMode.Normal, o.Mode);
        Assert.Equal(ok ? HiddenWindowMode.PrivateDisplay : HiddenWindowMode.OffScreen, o.Mode);
        Assert.Equal(ok, o.Error is null);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NormalRunIsNormal(bool windows)
    {
        var o = HiddenWindowOptions.Decide(NoArgs, Env(("BARBICAN_XVFB", "1"), ("DISPLAY", ":97")), automationBuild: false, isWindows: windows, canHideWindow: true);
        Assert.Equal(HiddenWindowMode.Normal, o.Mode);
        Assert.False(o.IsSmoke);
        Assert.Null(o.Error);
        Assert.Equal("default", o.Profile);
    }

    [Theory]
    [InlineData]
    [InlineData("--other")]
    [InlineData("")]
    public void SmokeWithoutAPathIsAnErrorButStillHidden(params string[] after)
    {
        string[] args = ["--smoke", .. after];
        var o = HiddenWindowOptions.Decide(args, Env(), automationBuild: false, isWindows: true, canHideWindow: true);
        Assert.NotNull(o.Error);
        Assert.NotEqual(HiddenWindowMode.Normal, o.Mode);
        Assert.Null(o.SmokeReportPath);
    }

    [Fact]
    public void SmokeTakesThePathAndItsOwnProfile()
    {
        var o = HiddenWindowOptions.Decide(["--smoke", "out/smoke.json"], Env(), automationBuild: false, isWindows: true, canHideWindow: true);
        Assert.True(o.IsSmoke);
        Assert.Equal("out/smoke.json", o.SmokeReportPath);
        Assert.Equal("smoke", o.Profile);
        Assert.Null(o.Error);
    }

    [Theory]
    [InlineData(null, 8792)]
    [InlineData("8801", 8801)]
    [InlineData("80", 8792)]
    [InlineData("70000", 8792)]
    [InlineData("-1", 8792)]
    [InlineData("88x", 8792)]
    public void PortComesFromTheEnvironmentWithinRange(string? value, int port)
    {
        var o = HiddenWindowOptions.Decide(NoArgs, Env(("BARBICAN_AUTOMATION_PORT", value)), automationBuild: true, isWindows: true, canHideWindow: true);
        Assert.Equal(port, o.AutomationPort);
        Assert.Equal($"auto-{port}", o.Profile);
    }

    [Fact]
    public void NamedProfileWins()
    {
        var o = HiddenWindowOptions.Decide(NoArgs, Env(("BARBICAN_PROFILE", "dev-a")), automationBuild: true, isWindows: true, canHideWindow: true);
        Assert.Equal("dev-a", o.Profile);
    }

    private static Func<string, string?> Env(params (string Name, string? Value)[] values) =>
        name => values.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.Ordinal)).Value;
}
