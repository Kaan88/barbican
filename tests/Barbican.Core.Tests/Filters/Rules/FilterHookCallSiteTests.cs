// As HookCallSiteTests for the 15 M6 hooks of srv-data (filters 6, out of office 3, templates 6): each has a call site in src/ and a row in spec/plugins/hooks.csv that names it. Kept in its own file so the shared file is not edited by two streams. M6 stream srv-data.

using Barbican.Core.Tests.Plugins;

namespace Barbican.Core.Tests.Filters.Rules;

public sealed class FilterHookCallSiteTests
{
    private static readonly string[] Hooks =
    [
        "onFiltersLoaded", "onFilterRuleChange", "onBeforeFiltersSave", "onFiltersSave", "onSieveScriptChange", "onSieveScriptGenerate",
        "onVacationLoaded", "onBeforeVacationUpdate", "onVacationUpdate",
        "onTemplateCreate", "onTemplateUpdate", "onTemplateDelete", "onTemplatesImport", "onTemplateApply", "onTemplateRender",
    ];

    [Fact]
    public void Srv_data_m6_hooks_have_call_sites()
    {
        var sources = string.Concat(RepoFiles.SourceFiles().Select(File.ReadAllText));
        var rows = RepoFiles.ReadCsv("spec/plugins/hooks.csv")
            .Where(r => string.Equals(r["milestone"], "M6", StringComparison.Ordinal) && string.Equals(r["owner"], "srv-data", StringComparison.Ordinal))
            .Select(r => r["name"])
            .ToList();
        Assert.Equal(Hooks.Order(StringComparer.Ordinal), rows.Order(StringComparer.Ordinal));
        var missing = rows.Where(name => !sources.Contains($"HookNames.{char.ToUpperInvariant(name[0])}{name[1..]}", StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0, $"M6 hooks of srv-data without a call site: {string.Join(", ", missing)}");
    }
}
