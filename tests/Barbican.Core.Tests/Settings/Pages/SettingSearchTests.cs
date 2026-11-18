// Not Rust rows: I-396 (settings search: every word must match; the label counts most), Bulwark settings-app.tsx. M6 stream set-data.

using Barbican.Core.Settings.Pages;

namespace Barbican.Core.Tests.Settings.Pages;

public sealed class SettingSearchTests
{
    private static readonly SettingSearchEntry[] Entries =
    [
        new("appearance", "S-theme", "Theme", "Light or dark colors", "colour mode"),
        new("accessibility", "S-contrast", "More contrast", "Stronger borders and text", "theme"),
        new("notifications", "S-sound", "Notification sound", "Play a sound for new mail", string.Empty),
    ];

    [Fact]
    public void A_label_match_beats_a_description_match_beats_a_keyword_match()
    {
        var hits = SettingSearch.Search(Entries, "theme");
        Assert.Equal("S-theme,S-contrast", string.Join(',', hits.Select(h => h.RowId)));
        Assert.True(hits[0].Score > hits[1].Score);
        Assert.Equal("S-sound", string.Join(',', SettingSearch.Search(Entries, "SOUND new").Select(h => h.RowId)));
        Assert.Equal("S-theme", string.Join(',', SettingSearch.Search(Entries, "dárk").Select(h => h.RowId)));
        Assert.Empty(SettingSearch.Search(Entries, "theme zebra"));
        Assert.Empty(SettingSearch.Search(Entries, "  "));
    }

    [Fact]
    public void Entries_come_from_rows_but_not_from_section_rows()
    {
        var page = new SettingPage("appearance", "Appearance", "top",
        [
            new SettingRow("S-section-appearance-1", "appearance", SettingRowKind.Section, "Colors", string.Empty, null, [], null, SettingRowStatus.Keyless, false, null),
            new SettingRow("S-theme", "appearance", SettingRowKind.Select, "Theme", "Light or dark", "theme", [], null, SettingRowStatus.Wired, false, "Colors"),
        ]);
        var entries = SettingSearch.EntriesOf([page]);
        var only = Assert.Single(entries);
        Assert.Equal("S-theme", only.RowId);
        Assert.Contains("Appearance", only.Keywords, StringComparison.Ordinal);
    }
}
