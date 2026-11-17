using System.Text.RegularExpressions;
using System.Xml.Linq;
using Barbican.Automation.Protocol;

namespace Barbican.Automation.Tests;

/// <summary>
/// No popup of the App may leave its window (docs/adr/0007 decision 4, ADR 0005 "M0 review": in M0 an
/// unconstrained MenuFlyout opened as its own top-level window on the user's screen). Every
/// <see cref="PopupControls.MustConstrain"/> element in src/Barbican.App/**/*.xaml sets
/// <c>ShouldConstrainToRootBounds="True"</c>; no <see cref="PopupControls.Denied"/> element or
/// attribute appears; and App code creates no popup outside <see cref="PopupControls.CodeAllowed"/>.
/// </summary>
public sealed partial class PopupLintTests
{
    private static readonly Lazy<IReadOnlyList<(string File, XDocument Xaml)>> Xaml = new(() =>
        Directory.EnumerateFiles(TestRepo.PathOf("src/Barbican.App"), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => (Path.GetRelativePath(TestRepo.Root, f).Replace('\\', '/'), XDocument.Load(f)))
            .ToArray());

    [Fact]
    public void TheAppHasXaml() => Assert.NotEmpty(Xaml.Value);

    [Fact]
    public void EveryPopupIsConstrainedToTheWindow()
    {
        var bad = Violations(Xaml.Value).ToArray();
        Assert.True(bad.Length == 0, "Popups not constrained to the window:\n" + string.Join('\n', bad));
    }

    [Fact]
    public void NoDeniedControl()
    {
        var bad = Denied(Xaml.Value).ToArray();
        Assert.True(bad.Length == 0, "Denied popup controls (docs/adr/0007 \"M1 F1\"):\n" + string.Join('\n', bad));
    }

    [Fact]
    public void NoPopupCreatedInAppCode()
    {
        var allowed = PopupControls.CodeAllowed.ToHashSet(StringComparer.Ordinal);
        var bad = Directory.EnumerateFiles(TestRepo.PathOf("src/Barbican.App"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => (Path: Path.GetRelativePath(TestRepo.Root, f).Replace('\\', '/'), Text: File.ReadAllText(f)))
            .Where(f => !allowed.Contains(f.Path) && CodePopup().IsMatch(f.Text))
            .Select(f => f.Path)
            .ToArray();
        Assert.True(bad.Length == 0, "Popups created in code outside PopupControls.CodeAllowed:\n" + string.Join('\n', bad));
    }

    // Self-tests: the lint finds what it must find.
    [Theory]
    [InlineData("<MenuFlyout />", 1)]
    [InlineData("<MenuFlyout ShouldConstrainToRootBounds=\"False\" />", 1)]
    [InlineData("<MenuFlyout ShouldConstrainToRootBounds=\"True\" />", 0)]
    [InlineData("<Button><Button.Flyout><Flyout /></Button.Flyout></Button>", 1)]
    [InlineData("<Button><Button.ContextFlyout><MenuFlyout ShouldConstrainToRootBounds=\"True\" /></Button.ContextFlyout></Button>", 0)]
    [InlineData("<SolidColorBrush x:Key=\"Popup\" Color=\"#FF000000\" />", 0)]
    [InlineData("<controls:TeachingTip />", 1)]
    public void LintFindsUnconstrainedPopups(string element, int count) =>
        Assert.Equal(count, Violations([("t.xaml", Doc(element))]).Count());

    [Theory]
    [InlineData("<ComboBox />", 1)]
    [InlineData("<Button ToolTipService.ToolTip=\"hi\" />", 1)]
    [InlineData("<Button><ToolTipService.ToolTip><ToolTip /></ToolTipService.ToolTip></Button>", 2)]
    [InlineData("<AutoSuggestBox />", 1)]
    [InlineData("<CommandBarFlyout ShouldConstrainToRootBounds=\"True\" />", 1)]
    [InlineData("<ContentDialog />", 0)]
    [InlineData("<Button Content=\"ComboBox\" />", 0)]
    public void LintFindsDeniedControls(string element, int count) =>
        Assert.Equal(count, Denied([("t.xaml", Doc(element))]).Count());

    [Theory]
    [InlineData("var m = new MenuFlyout();", true)]
    [InlineData("var p = new Popup { Child = x };", true)]
    [InlineData("var t = new ToolTip();", true)]
    [InlineData("FlyoutBase.ShowAttachedFlyout(x);", true)]
    [InlineData("var n = new PopupGuardPolicy();", false)]
    public void LintFindsCodePopups(string code, bool found) => Assert.Equal(found, CodePopup().IsMatch(code));

    private static XDocument Doc(string element) => XDocument.Parse(
        "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:controls=\"using:Microsoft.UI.Xaml.Controls\">" + element + "</Grid>");

    private static IEnumerable<string> Violations(IEnumerable<(string File, XDocument Xaml)> files)
    {
        foreach (var (file, doc) in files)
        {
            foreach (var e in doc.Descendants().Where(e => PopupControls.MustConstrain.Contains(e.Name.LocalName, StringComparer.Ordinal)))
            {
                var v = e.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "ShouldConstrainToRootBounds", StringComparison.Ordinal))?.Value;   // a win:-prefixed attribute counts: Uno has no such member (Uno0001)
                if (!string.Equals(v, "True", StringComparison.OrdinalIgnoreCase))
                {
                    yield return $"{file}:{((System.Xml.IXmlLineInfo)e).LineNumber} <{e.Name.LocalName}> without ShouldConstrainToRootBounds=\"True\"";
                }
            }
        }
    }

    private static IEnumerable<string> Denied(IEnumerable<(string File, XDocument Xaml)> files)
    {
        foreach (var (file, doc) in files)
        {
            foreach (var e in doc.Descendants())
            {
                if (PopupControls.Denied.Contains(e.Name.LocalName, StringComparer.Ordinal))
                {
                    yield return $"{file}: <{e.Name.LocalName}>";
                }

                foreach (var a in e.Attributes().Where(a => PopupControls.Denied.Contains(a.Name.LocalName, StringComparer.Ordinal)))
                {
                    yield return $"{file}: {a.Name.LocalName}=";
                }
            }
        }
    }

    [GeneratedRegex(@"new\s+(?:MenuFlyout|Flyout|CommandBarFlyout|Popup|TeachingTip|ToolTip|ComboBox|AutoSuggestBox|ContentDialog)\s*[({]|\bShowAttachedFlyout\s*\(|ToolTipService\s*\.\s*SetToolTip", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CodePopup();

    // M2 menus (append only): the menu host, the dialog host, the palette and the shortcuts sheet are each one constrained Popup (ADR 0005, ADR 0007); none uses a MenuFlyout.
    [Theory]
    [InlineData("Views/Menus/MenuHostView.xaml")]
    [InlineData("Views/Dialogs/DialogHostView.xaml")]
    [InlineData("Views/Palette/PaletteView.xaml")]
    [InlineData("Views/Shortcuts/ShortcutSheetView.xaml")]
    public void The_menus_streams_hosts_are_a_single_constrained_popup(string view)
    {
        var (file, doc) = Xaml.Value.Single(f => f.File.EndsWith(view, StringComparison.Ordinal));
        var popup = Assert.Single(doc.Descendants(), e => string.Equals(e.Name.LocalName, "Popup", StringComparison.Ordinal));
        Assert.Equal("True", popup.Attributes().Single(a => string.Equals(a.Name.LocalName, "ShouldConstrainToRootBounds", StringComparison.Ordinal)).Value);
        Assert.DoesNotContain(doc.Descendants(), e => e.Name.LocalName.EndsWith("Flyout", StringComparison.Ordinal));
        Assert.NotNull(file);
    }
}
