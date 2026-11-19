using System.Text.Json;
using Barbican.Core.Plugins.Persistence;
namespace Barbican.Core.Tests.Plugins.Packages;
public sealed class PluginEcmaDateTests
{
    [Fact]
    public void FullRangeMatchesNativeJavaScriptJsonDateVectors()
    {
        var root = FindRoot();
        using var vectors = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "tools", "tests", "plugin-date-golden.json")));
        Assert.Equal(108, vectors.RootElement.GetArrayLength());
        foreach (var vector in vectors.RootElement.EnumerateArray())
            Assert.Equal(vector.GetProperty("expected").GetString(), PluginEcmaDate.Format(vector.GetProperty("milliseconds").GetDouble()), StringComparer.Ordinal);
    }
    [Fact]
    public void InvalidDateBecomesNullAndInvalidInternalTimeValuesAreRejected()
    {
        Assert.Null(PluginEcmaDate.Format(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => PluginEcmaDate.Format(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => PluginEcmaDate.Format(double.NegativeInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => PluginEcmaDate.Format(0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => PluginEcmaDate.Format(8640000000000001d));
    }
    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Barbican.slnx"))) current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
