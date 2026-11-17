using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace Barbican.Build.Tests;

/// <summary>
/// The hidden automation (src/Barbican.Automation with FlaUI and Win2D) exists only in the Automation
/// configuration (docs/adr/0007 "M0 review", docs/milestones/M1-plan.md §5 F1 step 2). Checked three
/// ways: the project files (no other project references it or its packages, and the App's reference is
/// conditioned on Configuration == Automation exactly); the built App outputs (no automation DLL next to
/// a non-Automation Barbican.dll, no AUTOMATION marker in its metadata); and tools/build.ps1 publish
/// (which fails when such a file or marker reaches the publish folder).
/// </summary>
public sealed class ReleaseContentTests
{
    private const string AutomationCondition = "'$(Configuration)' == 'Automation'";

    private const string AutomationWritesCondition = "'$(Configuration)' == 'Automation' and '$(AutomationWrites)' == 'true'";

    private static readonly string[] AutomationOnlyPackages = ["FlaUI.UIA3", "FlaUI.Core", "Microsoft.Graphics.Win2D"];

    private static readonly string[] ForbiddenFiles = ["Barbican.Automation.dll", "FlaUI.Core.dll", "FlaUI.UIA3.dll", "Interop.UIAutomationClient.dll", "Microsoft.Graphics.Canvas.dll", "Microsoft.Graphics.Canvas.Interop.dll", "Barbican.FakeJmap.dll"];

    public static TheoryData<string> Projects()
    {
        var data = new TheoryData<string>();
        foreach (var dir in new[] { "src", "tests", "tools", "plugins" })
        {
            foreach (var f in Directory.EnumerateFiles(TestRepo.PathOf(dir), "*.csproj", SearchOption.AllDirectories))
            {
                data.Add(Path.GetRelativePath(TestRepo.Root, f).Replace('\\', '/'));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void OnlyTheAutomationProjectUsesFlauiOrWin2D(string project)
    {
        if (project is "src/Barbican.Automation/Barbican.Automation.csproj")
        {
            return;
        }

        var refs = XDocument.Load(TestRepo.PathOf(project)).Descendants()
            .Where(e => e.Name.LocalName is "PackageReference")
            .Select(e => (string?)e.Attribute("Include") ?? string.Empty);
        Assert.DoesNotContain(refs, r => AutomationOnlyPackages.Contains(r, StringComparer.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void OnlyTheAppReferencesAutomationAndOnlyInTheAutomationConfiguration(string project)
    {
        var refs = XDocument.Load(TestRepo.PathOf(project)).Descendants()
            .Where(e => e.Name.LocalName is "ProjectReference"
                && ((string?)e.Attribute("Include") ?? string.Empty).Replace('\\', '/').EndsWith("Barbican.Automation/Barbican.Automation.csproj", StringComparison.Ordinal))
            .ToArray();
        if (project is "src/Barbican.App/Barbican.App.csproj")
        {
            var r = Assert.Single(refs);
            Assert.Null(r.Attribute("Condition"));
            Assert.Equal(AutomationCondition, (string?)r.Parent?.Attribute("Condition"));
            Assert.Equal("ItemGroup", r.Parent?.Name.LocalName);
            Assert.Equal("Project", r.Parent?.Parent?.Name.LocalName);
        }
        else if (project is not "tests/Barbican.Automation.Tests/Barbican.Automation.Tests.csproj")
        {
            Assert.Empty(refs);
        }
    }

    /// <summary>The in-process fake server (sample data for hidden screenshots) must never reach a Release build of the App (review of m1-shell-ui).</summary>
    [Fact]
    public void TheAppReferencesFakeJmapOnlyInTheAutomationConfiguration()
    {
        var refs = XDocument.Load(TestRepo.PathOf("src/Barbican.App/Barbican.App.csproj")).Descendants()
            .Where(e => e.Name.LocalName is "ProjectReference" && ((string?)e.Attribute("Include") ?? string.Empty).Replace('\\', '/').EndsWith("Barbican.FakeJmap/Barbican.FakeJmap.csproj", StringComparison.Ordinal))
            .ToArray();
        foreach (var r in refs)
        {
            Assert.Null(r.Attribute("Condition"));
            Assert.Equal(AutomationCondition, (string?)r.Parent?.Attribute("Condition"));
        }

        foreach (var project in Directory.EnumerateFiles(TestRepo.PathOf("src"), "*.csproj", SearchOption.AllDirectories).Where(f => !f.EndsWith("Barbican.App.csproj", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain("Barbican.FakeJmap", File.ReadAllText(project), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheAutomationSymbolIsDefinedOnlyInTheAutomationConfiguration()
    {
        foreach (var file in Directory.EnumerateFiles(TestRepo.Root, "*.props", SearchOption.TopDirectoryOnly)
                     .Concat(Directory.EnumerateFiles(TestRepo.PathOf("src"), "*.csproj", SearchOption.AllDirectories))
                     .Concat(Directory.EnumerateFiles(TestRepo.PathOf("eng"), "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".props", StringComparison.Ordinal) || f.EndsWith(".targets", StringComparison.Ordinal))))
        {
            foreach (var d in XDocument.Load(file).Descendants().Where(e => e.Name.LocalName is "DefineConstants" && e.Value.Contains("AUTOMATION", StringComparison.Ordinal)))
            {
                // AUTOMATION_WRITES (M2): only together with the Automation configuration (the condition below adds the AutomationWrites property)
                var expected = d.Value.Contains("AUTOMATION_WRITES", StringComparison.Ordinal) ? AutomationWritesCondition : AutomationCondition;
                Assert.Equal(expected, (string?)d.Parent?.Attribute("Condition"));
            }
        }
    }

    [Fact]
    public void AutomationWritesIsDefinedOnlyTogetherWithAutomationAndNotByPlainBuilds()
    {
        var props = XDocument.Load(TestRepo.PathOf("Directory.Build.props"));
        var define = Assert.Single(props.Descendants(), e => e.Name.LocalName is "DefineConstants" && e.Value.Contains("AUTOMATION_WRITES", StringComparison.Ordinal));
        Assert.Equal(AutomationWritesCondition, (string?)define.Parent?.Attribute("Condition"));

        // no project, props or targets file defines it anywhere else
        foreach (var file in Directory.EnumerateFiles(TestRepo.Root, "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".csproj", StringComparison.Ordinal) || f.EndsWith(".props", StringComparison.Ordinal) || f.EndsWith(".targets", StringComparison.Ordinal))
                         && !f.Contains("/bin/", StringComparison.Ordinal) && !f.Contains("\\bin\\", StringComparison.Ordinal) && !f.Contains("/obj/", StringComparison.Ordinal) && !f.Contains("\\obj\\", StringComparison.Ordinal)
                         && !f.Contains("node_modules", StringComparison.Ordinal) && !f.Contains("prototype", StringComparison.Ordinal) && !f.Contains("spikes", StringComparison.Ordinal))
                     .Where(f => !string.Equals(Path.GetFullPath(f), Path.GetFullPath(TestRepo.PathOf("Directory.Build.props")), StringComparison.OrdinalIgnoreCase)))
        {
            Assert.DoesNotContain("AUTOMATION_WRITES", File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheBuildScriptOnlyTakesWritesTogetherWithTheAutomationConfiguration()
    {
        var script = File.ReadAllText(TestRepo.PathOf("tools/build.ps1"));
        Assert.Contains("[switch]$Writes", script, StringComparison.Ordinal);
        Assert.Contains("-p:AutomationWrites=true", script, StringComparison.Ordinal);
        Assert.Contains("-Writes needs -Configuration Automation", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMarkerIsOnlyInTheAutomationItemGroup()
    {
        var app = XDocument.Load(TestRepo.PathOf("src/Barbican.App/Barbican.App.csproj"));
        var marker = Assert.Single(app.Descendants(), e => e.Name.LocalName is "AssemblyMetadata" && (string?)e.Attribute("Include") is "BarbicanAutomationBuild");
        Assert.Equal(AutomationCondition, (string?)marker.Parent?.Attribute("Condition"));
        Assert.Single(app.Descendants(), e => e.Name.LocalName is "AssemblyMetadata" && (string?)e.Attribute("Include") is "BarbicanConfiguration" && (string?)e.Attribute("Value") is "$(Configuration)");
    }

    [Fact]
    public void TheSolutionHasTheThreeConfigurations()
    {
        var slnx = XDocument.Load(TestRepo.PathOf("Barbican.slnx"));
        var names = slnx.Descendants().Where(e => e.Name.LocalName is "BuildType").Select(e => (string?)e.Attribute("Name")).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["Automation", "Debug", "Release"], names);
    }

    [Fact]
    public void PublishChecksForAutomationContent()
    {
        var script = File.ReadAllText(TestRepo.PathOf("tools/build.ps1"));
        Assert.Contains("Assert-NoAutomationContent", script, StringComparison.Ordinal);
        foreach (var pattern in new[] { "FlaUI*.dll", "Microsoft.Graphics.Canvas*.dll", "Barbican.Automation.dll", "BarbicanAutomationBuild" })
        {
            Assert.Contains(pattern, script, StringComparison.Ordinal);
        }
    }

    /// <summary>Every App output built in this checkout (bin/Debug, bin/Release, bin/Automation; any TFM).</summary>
    public static TheoryData<string> BuiltApps()
    {
        var data = new TheoryData<string>();
        var bin = TestRepo.PathOf("src/Barbican.App/bin");
        if (Directory.Exists(bin))
        {
            foreach (var f in Directory.EnumerateFiles(bin, "Barbican.dll", SearchOption.AllDirectories))
            {
                data.Add(Path.GetRelativePath(TestRepo.Root, f).Replace('\\', '/'));
            }
        }

        if (data.Count == 0)
        {
            data.Add(string.Empty);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(BuiltApps))]
    public void BuiltAppsCarryAutomationOnlyInTheAutomationConfiguration(string dll)
    {
        if (dll.Length == 0)
        {
            Assert.Skip("the App is not built in this checkout");
        }

        var meta = Metadata(TestRepo.PathOf(dll));
        Assert.True(meta.TryGetValue("BarbicanConfiguration", out var configuration), "BarbicanConfiguration metadata missing");
        var dir = Path.GetDirectoryName(TestRepo.PathOf(dll))!;
        var present = ForbiddenFiles.Where(f => File.Exists(Path.Combine(dir, f))).ToArray();
        if (configuration is "Automation")
        {
            Assert.Equal("true", meta.GetValueOrDefault("BarbicanAutomationBuild"));
            Assert.Contains("Barbican.Automation.dll", present, StringComparer.Ordinal);
        }
        else
        {
            Assert.False(meta.ContainsKey("BarbicanAutomationBuild"), $"{dll}: AUTOMATION marker in a {configuration} build");
            Assert.Empty(present);
        }
    }

    private static Dictionary<string, string?> Metadata(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var h in md.GetAssemblyDefinition().GetCustomAttributes())
        {
            var a = md.GetCustomAttribute(h);
            if (a.Constructor.Kind != HandleKind.MemberReference)
            {
                continue;
            }

            var ctor = md.GetMemberReference((MemberReferenceHandle)a.Constructor);
            if (ctor.Parent.Kind != HandleKind.TypeReference || !string.Equals(md.GetString(md.GetTypeReference((TypeReferenceHandle)ctor.Parent).Name), "AssemblyMetadataAttribute", StringComparison.Ordinal))
            {
                continue;
            }

            // Blob: prolog 0x0001, then two SerString arguments (key, value).
            var blob = md.GetBlobReader(a.Value);
            if (blob.ReadUInt16() != 1)
            {
                continue;
            }

            var key = blob.ReadSerializedString();
            if (key is not null)
            {
                result[key] = blob.ReadSerializedString();
            }
        }

        return result;
    }
}
