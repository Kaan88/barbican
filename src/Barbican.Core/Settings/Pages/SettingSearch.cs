// Mirrors: Bulwark components/settings/settings-app.tsx (settings search, I-396: label, description and keywords; every word must match; the label counts most), Rust settings.rs:442 search. Contract from the M6 plan section 4.2; owner set-data.

using System.Globalization;
using System.Text;

namespace Barbican.Core.Settings.Pages;

/// <summary>The settings search.</summary>
public static class SettingSearch
{
    private const double LabelScore = 3;
    private const double DescriptionScore = 2;
    private const double KeywordScore = 1;

    /// <summary>One entry per non-section row of the pages, with the page's label as extra keywords.</summary>
    public static IReadOnlyList<SettingSearchEntry> EntriesOf(IReadOnlyList<SettingPage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        return
        [
            .. pages.SelectMany(p => p.Rows
                .Where(r => r.Kind != SettingRowKind.Section)
                .Select(r => new SettingSearchEntry(p.Id, r.Id, r.Label, r.Description, r.Section is { Length: > 0 } s ? p.Label + " " + s : p.Label))),
        ];
    }

    /// <summary>Hits for a query: every word (case and diacritics ignored) is in the label, description or keywords; a label match scores above a description match above a keyword match; best first, ties in entry order; an empty query has no hits.</summary>
    public static IReadOnlyList<SettingSearchHit> Search(IReadOnlyList<SettingSearchEntry> entries, string query)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var words = Fold(query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return [];
        }

        var hits = new List<SettingSearchHit>();
        foreach (var e in entries)
        {
            var label = Fold(e.Label);
            var description = Fold(e.Description);
            var keywords = Fold(e.Keywords);
            var score = 0.0;
            var all = true;
            foreach (var w in words)
            {
                if (label.Contains(w, StringComparison.Ordinal))
                {
                    score += LabelScore;
                }
                else if (description.Contains(w, StringComparison.Ordinal))
                {
                    score += DescriptionScore;
                }
                else if (keywords.Contains(w, StringComparison.Ordinal))
                {
                    score += KeywordScore;
                }
                else
                {
                    all = false;
                    break;
                }
            }

            if (all)
            {
                hits.Add(new SettingSearchHit(e.PageId, e.RowId, e.Label, e.Description, score));
            }
        }

        // OrderByDescending is stable: ties stay in entry order
        return [.. hits.OrderByDescending(h => h.Score)];
    }

    private static string Fold(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString();
    }
}
