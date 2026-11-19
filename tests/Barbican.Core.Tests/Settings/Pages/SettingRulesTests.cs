// Not Rust rows: B-SET-001..005 (typed setters of Bulwark stores/settings-store.ts). M6 stream set-data.

using System.Text.Json.Nodes;
using Barbican.Core.Settings.Pages;

namespace Barbican.Core.Tests.Settings.Pages;

public sealed class SettingRulesTests
{
    private static SettingRow Row(SettingRowKind kind, params string[] options) =>
        new("S-x", "general", kind, "x", string.Empty, "x", [.. options.Select(o => new SettingOption(o, o.ToUpperInvariant()))], null, SettingRowStatus.Wired, false, null);

    [Fact]
    public void Values_are_checked_against_the_kind_of_their_row()
    {
        var toggle = Row(SettingRowKind.Toggle);
        Assert.True(SettingRules.Coerce(toggle, JsonValue.Create(true)).Ok);
        Assert.Equal(true, (bool?)SettingRules.Coerce(toggle, JsonValue.Create("true")).Value);
        Assert.False(SettingRules.Coerce(toggle, JsonValue.Create("maybe")).Ok);
        var select = Row(SettingRowKind.Select, "a", "b");
        Assert.True(SettingRules.Coerce(select, JsonValue.Create("a")).Ok);
        Assert.False(SettingRules.Coerce(select, JsonValue.Create("c")).Ok);
        var text = Row(SettingRowKind.Text);
        Assert.Equal("hi", (string?)SettingRules.Coerce(text, JsonValue.Create("  hi ")).Value);
        Assert.True(SettingRules.Coerce(text, null).Ok);
        Assert.False(SettingRules.Coerce(Row(SettingRowKind.Info), JsonValue.Create("x")).Ok);
        Assert.False(SettingRules.Coerce(Row(SettingRowKind.Section), JsonValue.Create("x")).Ok);
    }
    [Fact]
    public void Time_zone_info_row_allows_setting_and_preserves_unknown_synced_ids()
    {
        var row = SettingsCatalog.RowOfKey("timeZone")!;
        Assert.True(SettingRules.Coerce(row, JsonValue.Create("Europe/Berlin")).Ok);
        Assert.Equal("Future/City", (string?)SettingRules.Coerce(row, JsonValue.Create("Future/City")).Value);
        Assert.True(SettingRules.Coerce(row, null).Ok);
        Assert.False(SettingRules.Coerce(row, JsonValue.Create(true)).Ok);
    }
}
