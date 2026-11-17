using System.Xml.Linq;

namespace Barbican.Presentation.Tests.ShellUi;

/// <summary>
/// The XAML rules of the shell views (M1-plan §6.4, ADR 0007): every interactive control has an AutomationId and a name, every list has its item transitions cleared,
/// and nothing navigates with a Frame transition. The popup rules are in tests/Barbican.Automation.Tests (PopupLintTests).
/// </summary>
public sealed class ViewXamlLintTests
{
    private static readonly XNamespace Ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string[] Interactive =
        ["Button", "ToggleButton", "RepeatButton", "HyperlinkButton", "CheckBox", "RadioButton", "TextBox", "PasswordBox", "ListView", "GridView", "ToggleSwitch", "Slider"];

    private static readonly string[] Folders = ["Views", "Shell"];

    private static IEnumerable<(string File, XDocument Doc)> ViewFiles() =>
        Folders
            .SelectMany(d => Directory.EnumerateFiles(TestRepo.PathOf("src/Barbican.App/" + d), "*.xaml", SearchOption.AllDirectories))
            .Select(f => (Path.GetRelativePath(TestRepo.Root, f).Replace('\\', '/'), XDocument.Load(f)));

    private static bool HasAttr(XElement e, string name) => e.Attributes().Any(a => a.Name.LocalName.EndsWith(name, StringComparison.Ordinal) && a.Name.LocalName.Contains("AutomationProperties", StringComparison.Ordinal));

    private static IEnumerable<string> MissingIds(string file, XDocument doc) =>
        doc.Descendants()
            .Where(e => e.Name.Namespace == Ui && Interactive.Contains(e.Name.LocalName, StringComparer.Ordinal) && !e.Name.LocalName.Contains('.', StringComparison.Ordinal))
            .Where(e => !HasAttr(e, "AutomationId") || !HasAttr(e, "Name"))
            .Select(e => $"{file}: <{e.Name.LocalName}> {(string?)e.Attribute(X + "Name") ?? (string?)e.Attribute("Content") ?? string.Empty}".TrimEnd());

    [Fact]
    public void TheViewsExist() => Assert.True(ViewFiles().Count() >= 8);

    [Fact]
    public void EveryInteractiveControlHasAnAutomationIdAndAName()
    {
        var bad = ViewFiles().SelectMany(f => MissingIds(f.File, f.Doc)).ToArray();
        Assert.True(bad.Length == 0, "Interactive controls without AutomationId and Name:\n" + string.Join('\n', bad));
    }

    [Fact]
    public void EveryListClearsItsItemTransitions()
    {
        var bad = new List<string>();
        foreach (var (file, doc) in ViewFiles())
        {
            foreach (var list in doc.Descendants().Where(e => e.Name.Namespace == Ui && e.Name.LocalName is "ListView" or "GridView"))
            {
                var cleared = list.Elements().Any(c => c.Name.LocalName.EndsWith(".ItemContainerTransitions", StringComparison.Ordinal) && !c.Descendants().Any(t => !string.Equals(t.Name.LocalName, "TransitionCollection", StringComparison.Ordinal)));
                if (!cleared)
                {
                    bad.Add($"{file}: <{list.Name.LocalName} {(string?)list.Attribute(X + "Name")}>");
                }
            }
        }

        Assert.True(bad.Count == 0, "Lists that do not clear ItemContainerTransitions:\n" + string.Join('\n', bad));
    }

    [Fact]
    public void NoFrameTransitionsAndNoNavigation()
    {
        var bad = ViewFiles().Where(f => f.Doc.Descendants().Any(e => e.Name.LocalName is "NavigationThemeTransition" or "EntranceThemeTransition" or "ContentThemeTransition" or "Frame")).Select(f => f.File).ToArray();
        Assert.True(bad.Length == 0, "Frame navigation or entrance transitions (design/MOTION.md):\n" + string.Join('\n', bad));
    }

    [Theory]
    [InlineData("<Button/>", 1)]
    [InlineData("<Button AutomationProperties.AutomationId=\"a\"/>", 1)]
    [InlineData("<Button AutomationProperties.AutomationId=\"a\" AutomationProperties.Name=\"n\"/>", 0)]
    [InlineData("<Border/>", 0)]
    public void TheLintFindsWhatItMust(string element, int count)
    {
        var doc = XDocument.Parse($"<Grid xmlns=\"{Ui}\" xmlns:x=\"{X}\">{element}</Grid>");
        Assert.Equal(count, MissingIds("t.xaml", doc).Count());
    }
}
