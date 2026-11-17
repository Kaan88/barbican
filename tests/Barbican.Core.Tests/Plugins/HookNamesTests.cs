// The generated HookNames constants equal spec/plugins/hooks.csv (tools/gen-hooks.py). Not a port of a Rust test: the catalogue is new in C#.

using Barbican.Core.Plugins.Hooks;

namespace Barbican.Core.Tests.Plugins;

public sealed class HookNamesTests
{
    private static string Constant(string hook) => char.ToUpperInvariant(hook[0]) + hook[1..];

    [Fact]
    public void Constants_equal_the_csv()
    {
        var rows = RepoFiles.ReadCsv("spec/plugins/hooks.csv");
        var names = rows.Select(r => r["name"]).ToList();
        Assert.Equal(names, HookNames.All);
        foreach (var name in names)
        {
            var field = typeof(HookNames).GetField(Constant(name));
            Assert.NotNull(field);
            Assert.Equal(name, field.GetRawConstantValue());
        }
    }

    [Fact]
    public void Catalogue_has_the_bulwark_count()
    {
        // lib/plugin-hook-catalog.ts at Bulwark 1.21.2: 218 entries in 27 groups (the plan said 219 and 29: PORT-PLAN.md M6b).
        var rows = RepoFiles.ReadCsv("spec/plugins/hooks.csv");
        Assert.Equal(218, rows.Count);
        Assert.Equal(27, rows.Select(r => r["group"]).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(rows.Count, rows.Select(r => r["name"]).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_m1_hook_has_an_owner_and_every_slot_a_milestone()
    {
        foreach (var row in RepoFiles.ReadCsv("spec/plugins/hooks.csv").Where(r => string.Equals(r["milestone"], "M1", StringComparison.Ordinal)))
        {
            Assert.False(string.IsNullOrEmpty(row["owner"]), $"{row["name"]} is an M1 hook without an owner");
        }

        var slots = RepoFiles.ReadCsv("spec/plugins/slots.csv");
        Assert.Equal(58, slots.Count);
        Assert.All(slots, s => Assert.False(string.IsNullOrEmpty(s["milestone"])));
    }
}
