using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Barbican.Platform.Tests;

/// <summary>
/// The platform layer stays below the UI (PORT-PLAN.md §3.1, docs/milestones/M1-plan.md §3.1): the
/// platform assemblies reference neither WinUI nor Uno, and the Linux one nothing Windows-only.
/// </summary>
public sealed class LayeringTests
{
    public static TheoryData<string> Assemblies()
    {
        var data = new TheoryData<string> { "Barbican.Platform.dll", "Barbican.Platform.Linux.dll" };
        if (OperatingSystem.IsWindows())
        {
            data.Add("Barbican.Platform.Windows.dll");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Assemblies))]
    public void NoUiReference(string file)
    {
        var refs = References(file);
        Assert.DoesNotContain(refs, r => r.StartsWith("Microsoft.WinUI", StringComparison.Ordinal) || r.StartsWith("Uno", StringComparison.Ordinal) || r.StartsWith("Microsoft.UI", StringComparison.Ordinal));
    }

    [Fact]
    public void LinuxHasNoWindowsReference()
    {
        var refs = References("Barbican.Platform.Linux.dll");
        Assert.DoesNotContain(refs, r => r.StartsWith("Microsoft.Windows", StringComparison.Ordinal) || r.StartsWith("Barbican.Platform.Windows", StringComparison.Ordinal));
    }

    private static string[] References(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, file);
        Assert.True(File.Exists(path), path + " missing");
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        return md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name)).ToArray();
    }
}
