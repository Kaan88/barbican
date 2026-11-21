// Mirrors: Bulwark components/global-search/search-preview.tsx:72-205 (ContactPreview, CalendarPreview, FilePreview: title, where it is, the detail rows, an Open button; the mail view for a message), palette-result-row.tsx:90-121
// (the title with the server's marks, the account and place line, the marked snippet) and hit-subtitle.ts.

using Barbican.Core.Search;
using Barbican.Presentation.Palette;

namespace Barbican.Presentation.Search;

/// <summary>The preview card of a search hit.</summary>
public static class SearchPreviews
{
    /// <summary>The card for a hit: the title, where it is, its detail lines, the marked body snippet (mail) and the Open button's text.</summary>
    public static PalettePreview For(SearchHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        var lines = hit.Details.Select(d => new PalettePreviewLine(d.Icon, d.Text)).ToList();
        // mail: the marked snippet, else the message's own preview text
        IReadOnlyList<TextSegment> snippet = hit.Snippet.Count > 0 ? hit.Snippet : hit.Body.Length > 0 ? [new TextSegment(hit.Body, false)] : [];
        return new PalettePreview(hit.Kind, hit.Title, hit.Subtitle, lines, snippet, hit.Kind == SearchScope.Mail ? "Open message" : "Open")
        {
            Badge = hit.IsRecurring ? "Recurring" : string.Empty,
        };
    }
}
