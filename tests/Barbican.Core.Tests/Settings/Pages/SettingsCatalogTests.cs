// Not Rust rows: B-SET-001 (schema driven pages), G-069 (keyless rows kept), spec/settings.csv (201 settings rows; the schema has 276 rows with its 74 section rows). M6 stream set-data.

using Barbican.Core.Settings.Pages;

namespace Barbican.Core.Tests.Settings.Pages;

public sealed class SettingsCatalogTests
{
    [Fact]
    public void Pages_carry_every_schema_row_with_a_unique_id()
    {
        var pages = SettingsCatalog.Pages();
        Assert.Equal(21, pages.Count);
        var rows = pages.SelectMany(p => p.Rows).ToList();
        Assert.Equal(276, rows.Count);
        Assert.Equal(rows.Count, rows.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(rows, r => Assert.Equal(r.Kind == SettingRowKind.Section, r.Id.StartsWith("S-section-", StringComparison.Ordinal)));
    }

    [Fact]
    public void Keyless_rows_are_kept_with_stable_ids_and_no_key()
    {
        var keyless = SettingsCatalog.Pages().SelectMany(p => p.Rows).Where(r => r.Status == SettingRowStatus.Keyless).ToList();
        Assert.Equal(64, keyless.Count);
        Assert.All(keyless, r => Assert.Null(r.Key));
        Assert.Equal("S-keyless-001", keyless[0].Id);
        Assert.Equal("S-keyless-064", keyless[^1].Id);
        Assert.NotNull(SettingsCatalog.RowOf("S-colorBlindPalette"));
        Assert.Equal("accessibility", SettingsCatalog.RowOf("S-colorBlindPalette")!.Page);
        Assert.Null(SettingsCatalog.RowOf("S-nope"));
    }
    [Theory]
    [InlineData("recipientMentionsEnabled")]
    [InlineData("mentionAddsRecipientTo")]
    [InlineData("filenameCollapseSeparators")]
    [InlineData("filenameLowercase")]
    [InlineData("filenameSpaceReplacement")]
    [InlineData("filenameStripDiacritics")]
    public void Existing_settings_readers_are_not_labelled_inert(string key)
    {
        Assert.Equal(SettingRowStatus.Wired, SettingsCatalog.RowOfKey(key)!.Status);
    }

    [Fact]
    public void Extracted_select_options_are_real_choices_and_numeric_values_stay_numeric()
    {
        var rows = SettingsCatalog.Pages().SelectMany(p => p.Rows).ToList();
        Assert.All(rows.SelectMany(r => r.Options), option =>
        {
            Assert.DoesNotContain("${", option.Value, StringComparison.Ordinal);
            Assert.DoesNotContain("${", option.Label, StringComparison.Ordinal);
            Assert.DoesNotContain("t(`", option.Label, StringComparison.Ordinal);
            Assert.DoesNotContain("�", option.Label, StringComparison.Ordinal);
        });
        var fonts = SettingsCatalog.RowOfKey("fontSize")!;
        Assert.Contains(fonts.Options, option => string.Equals(option.Value, "medium", StringComparison.Ordinal));
        Assert.True(SettingRules.Coerce(fonts, System.Text.Json.Nodes.JsonValue.Create("large")).Ok);
        var marker = SettingsCatalog.RowOfKey("tintListRowsByAccount")!;
        Assert.False(SettingRules.Coerce(marker, System.Text.Json.Nodes.JsonValue.Create("tint")).Ok);
        Assert.True(SettingRules.Coerce(marker, System.Text.Json.Nodes.JsonValue.Create(true)).Ok);
        var scale = SettingsCatalog.RowOfKey("mailTextScale")!;
        var number = SettingRules.Coerce(scale, System.Text.Json.Nodes.JsonValue.Create("1.125"));
        Assert.True(number.Ok);
        Assert.Equal(1.125, (double?)number.Value);
        Assert.False(SettingRules.Coerce(scale, System.Text.Json.Nodes.JsonValue.Create("9")).Ok);
        Assert.True(SettingRules.Coerce(SettingsCatalog.RowOfKey("messageListOrderScope")!, System.Text.Json.Nodes.JsonValue.Create("all")).Ok);
        Assert.False(SettingRules.Coerce(SettingsCatalog.RowOfKey("messageListOrderScope")!, System.Text.Json.Nodes.JsonValue.Create("wrong")).Ok);
        Assert.True(SettingRules.Coerce(SettingsCatalog.RowOfKey("subAddressDelimiter")!, System.Text.Json.Nodes.JsonValue.Create("#")).Ok);
        Assert.False(SettingRules.Coerce(SettingsCatalog.RowOfKey("subAddressDelimiter")!, System.Text.Json.Nodes.JsonValue.Create("ab")).Ok);
        Assert.True(SettingRules.Coerce(SettingsCatalog.RowOfKey("writingLanguage")!, System.Text.Json.Nodes.JsonValue.Create("de-CH")).Ok);
        Assert.False(SettingRules.Coerce(SettingsCatalog.RowOfKey("writingLanguage")!, System.Text.Json.Nodes.JsonValue.Create("<script>")).Ok);
    }

    [Fact]
    public void Every_static_keyed_select_choice_passes_its_value_validation()
    {
        foreach (var row in SettingsCatalog.Pages().SelectMany(page => page.Rows).Where(r => r.Kind == SettingRowKind.Select && r.Key is not null && !string.Equals(r.Key, "tintListRowsByAccount", StringComparison.Ordinal)))
        {
            foreach (var option in row.Options)
            {
                var check = SettingRules.Coerce(row, System.Text.Json.Nodes.JsonValue.Create(option.Value));
                Assert.True(check.Ok, $"{row.Id}: {option.Value}: {check.Error}");
            }
        }
    }

}
