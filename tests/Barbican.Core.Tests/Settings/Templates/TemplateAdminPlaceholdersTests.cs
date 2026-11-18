// Not Rust rows: G-034 (placeholder fill on insert; Bulwark lib/template-placeholders.ts). M6 stream srv-data.

using Barbican.Core.Settings.Templates;

namespace Barbican.Core.Tests.Settings.Templates;

public sealed class TemplateAdminPlaceholdersTests
{
    [Fact]
    [Red("srv-data")]
    public void Placeholders_are_found_once_in_order_and_filled()
    {
        Assert.Equal(["name", "topic"], TemplateAdminPlaceholders.Find("Hi {{name}}, {{ name }} / {{topic}} {{not valid}} {single}"));
        Assert.Empty(TemplateAdminPlaceholders.Find("plain"));
        var filled = TemplateAdminPlaceholders.Fill("Hi {{name}}, about {{topic}} {{left}}", new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = "Ann", ["topic"] = "tea" });
        Assert.Equal("Hi Ann, about tea {{left}}", filled);
    }
}
