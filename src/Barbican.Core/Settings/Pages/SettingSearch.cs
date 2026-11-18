// Mirrors: Bulwark components/settings/settings-app.tsx (settings search, I-396: label, description and keywords; every word must match; the label counts most), Rust settings.rs search. Contract from the M6 plan section 4.2; owner set-data.

using Barbican.Core.Common;

namespace Barbican.Core.Settings.Pages;

/// <summary>The settings search.</summary>
public static class SettingSearch
{
    /// <summary>One entry per non-section row of the pages, with the page's label as extra keywords.</summary>
    public static IReadOnlyList<SettingSearchEntry> EntriesOf(IReadOnlyList<SettingPage> pages) => throw new PortPendingException("SettingSearch.EntriesOf");

    /// <summary>Hits for a query: every word (case and diacritics ignored) is in the label, description or keywords; a label match scores above a description match above a keyword match; best first, ties in entry order; an empty query has no hits.</summary>
    public static IReadOnlyList<SettingSearchHit> Search(IReadOnlyList<SettingSearchEntry> entries, string query) => throw new PortPendingException("SettingSearch.Search");
}
