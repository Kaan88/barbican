// Not Rust rows: B-SET-001 (schema driven pages), G-069 (keyless rows kept), spec/settings.csv (201 settings rows; the schema has 276 rows with its 74 section rows). M6 stream set-data.

using Barbican.Core.Settings.Pages;

namespace Barbican.Core.Tests.Settings.Pages;

public sealed class SettingsCatalogTests
{
    [Fact]
    [Red("set-data")]
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
    [Red("set-data")]
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
}
