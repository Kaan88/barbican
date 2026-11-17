using System.Text.RegularExpressions;

namespace Barbican.Automation.Tests;

/// <summary>
/// Static checks of the App's window code (docs/adr/0007 "M0 review": windows appeared on the user's
/// screen three times in M0). The window is shown in exactly one place, a switch over the decided
/// mode; <c>Activate()</c> appears only in the PrivateDisplay and Normal arms; the hidden window never
/// activates, never takes the foreground and never sends input.
/// </summary>
public sealed partial class WindowModeSourceTests
{
    private static string App => Code(TestRepo.PathOf("src/Barbican.App/App.xaml.cs"));

    private static string Hidden => Code(TestRepo.PathOf("src/Barbican.App/Composition/HiddenWindow.cs"));

    [Fact]
    public void ActivateOnlyInTheVisibleArms()
    {
        var app = App;
        Assert.Single(Switch().Matches(app));
        var arms = Arm().Matches(app).ToDictionary(m => m.Groups["mode"].Value, m => m.Groups["body"].Value, StringComparer.Ordinal);
        Assert.True(arms.Keys.Order(StringComparer.Ordinal).SequenceEqual(["Normal", "OffScreen", "PrivateDisplay"], StringComparer.Ordinal), string.Join(",", arms.Keys));
        Assert.DoesNotContain("Activate(", arms["OffScreen"], StringComparison.Ordinal);
        Assert.Contains("HiddenWindow.Show(", arms["OffScreen"], StringComparison.Ordinal);
        Assert.Equal(2, Regex.Count(app, @"\.Activate\s*\(", RegexOptions.None, TimeSpan.FromSeconds(1)));
        Assert.Contains("Activate(", arms["Normal"], StringComparison.Ordinal);
        Assert.Contains("Activate(", arms["PrivateDisplay"], StringComparison.Ordinal);
    }

    [Fact]
    public void TheModeIsDecidedOnceBeforeTheWindow()
    {
        var app = App;
        Assert.Equal(1, Regex.Count(app, @"HiddenWindowOptions\.Decide\(", RegexOptions.None, TimeSpan.FromSeconds(1)));
        Assert.True(app.IndexOf("HiddenWindowOptions.Decide(", StringComparison.Ordinal) < app.IndexOf("new Window", StringComparison.Ordinal));
    }

    // The real-input APIs themselves are refused repo-wide by the rule check of tools/build.ps1.
    [Theory]
    [InlineData("SetForegroundWindow")]
    [InlineData("SendKeys")]
    [InlineData("BringWindowToTop")]
    [InlineData("SwitchDesktop")]
    [InlineData("Activate(")]
    [InlineData("SW_SHOWNORMAL")]
    public void HiddenWindowNeverActivatesOrSendsInput(string api)
    {
        Assert.DoesNotContain(api, Hidden, StringComparison.Ordinal);
        foreach (var f in Directory.EnumerateFiles(TestRepo.PathOf("src/Barbican.Automation"), "*.cs", SearchOption.AllDirectories))
        {
            if (string.Equals(api, "Activate(", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.DoesNotContain(api, Code(f), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void HiddenWindowIsNoActivateToolWindow()
    {
        var h = Hidden;
        Assert.Contains("WsExNoActivate = 0x08000000", h, StringComparison.Ordinal);
        Assert.Contains("WsExToolWindow = 0x00000080", h, StringComparison.Ordinal);
        Assert.Contains("ex | WsExNoActivate | WsExToolWindow", h, StringComparison.Ordinal);
        Assert.Contains("SwShowNoActivate = 4", h, StringComparison.Ordinal);
        Assert.Contains("ShowWindow(hwnd, SwShowNoActivate)", h, StringComparison.Ordinal);
        Assert.Contains("OffScreenX = -12000", h, StringComparison.Ordinal);
    }

    [Fact]
    public void AutomationNeverUsesFlauiInput()
    {
        foreach (var f in Directory.EnumerateFiles(TestRepo.PathOf("src/Barbican.Automation"), "*.cs", SearchOption.AllDirectories))
        {
            var text = Code(f);
            Assert.DoesNotContain("FlaUI.Core.Input", text, StringComparison.Ordinal);
            Assert.DoesNotContain(".SetFocus(", text, StringComparison.Ordinal);
        }
    }

    /// <summary>The file without // and /* */ comments (doc comments name the forbidden APIs).</summary>
    private static string Code(string path) => Comment().Replace(File.ReadAllText(path), string.Empty);

    [GeneratedRegex(@"//[^\n]*|/\*.*?\*/", RegexOptions.Singleline, matchTimeoutMilliseconds: 5000)]
    private static partial Regex Comment();

    [GeneratedRegex(@"switch\s*\(\s*options\.Mode\s*\)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Switch();

    [GeneratedRegex(@"case\s+HiddenWindowMode\.(?<mode>\w+)\s*:(?<body>.*?)break;", RegexOptions.Singleline | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Arm();
}
