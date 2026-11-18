// Not Rust rows: I-395 (toggles and selects show their stored or default value), I-398 (Show advanced settings), Bulwark settings-section.tsx (an empty section is hidden). M6 stream set-ui.

using System.Text.Json.Nodes;
using Barbican.Core.Settings.Pages;
using Barbican.Presentation.Settings.Ui;

namespace Barbican.Presentation.Tests.Settings.Ui;

public sealed class SetUiRowRulesTests
{
    private static SettingRow R(string id, SettingRowKind kind, bool advanced = false, JsonNode? def = null, params string[] options) =>
        new(id, "p", kind, id, string.Empty, id, [.. options.Select(o => new SettingOption(o, o.ToUpperInvariant()))], def, SettingRowStatus.Wired, advanced, null);

    [Fact]
    [Red("set-ui")]
    public void Advanced_rows_hide_and_a_section_without_visible_rows_hides_with_them()
    {
        var rows = new[]
        {
            R("sec1", SettingRowKind.Section),
            R("a", SettingRowKind.Toggle),
            R("sec2", SettingRowKind.Section),
            R("b", SettingRowKind.Toggle, advanced: true),
            R("sec3", SettingRowKind.Section),
            R("c", SettingRowKind.Toggle),
        };
        Assert.Equal("sec1,a,sec3,c", string.Join(',', SetUiRowRules.Visible(rows, showAdvanced: false).Select(r => r.Id)));
        Assert.Equal(6, SetUiRowRules.Visible(rows, showAdvanced: true).Count);
    }

    [Fact]
    [Red("set-ui")]
    public void A_value_falls_back_to_the_row_default()
    {
        var row = R("a", SettingRowKind.Toggle, def: JsonValue.Create(true));
        Assert.Equal(true, (bool?)SetUiRowRules.EffectiveValue(row, null));
        Assert.Equal(false, (bool?)SetUiRowRules.EffectiveValue(row, JsonValue.Create(false)));
        Assert.Null(SetUiRowRules.EffectiveValue(R("b", SettingRowKind.Text), null));
    }

    [Fact]
    [Red("set-ui")]
    public void A_select_shows_the_label_of_its_value()
    {
        var row = R("s", SettingRowKind.Select, false, null, "x", "y");
        Assert.Equal("Y", SetUiRowRules.SelectText(row, JsonValue.Create("y")));
        Assert.Equal("other", SetUiRowRules.SelectText(row, JsonValue.Create("other")));
        Assert.Equal("X", SetUiRowRules.SelectText(row, null));
    }
}
