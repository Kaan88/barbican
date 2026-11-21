// Mirrors: Bulwark components/global-search/search-preview.tsx:43-60 (PreviewHeader: visual, title, subtitle, the Open button), :158-205 (SearchPreview: a summary card per kind; mail shows the message).

using Barbican.Core.Search;

namespace Barbican.Presentation.Palette;

/// <summary>What the palette shows for the highlighted row: a card with the hit's title, where it is, its detail lines, and for mail the snippet the server marked.</summary>
/// <param name="Kind">The kind of hit (mail, contacts, calendar, files); the card's icon follows it.</param>
/// <param name="Title">The heading.</param>
/// <param name="Subtitle">Where the hit is (folder, book, calendar, path).</param>
/// <param name="Lines">The detail lines.</param>
/// <param name="Snippet">The body text with the matched words marked (mail); empty when there is none.</param>
/// <param name="OpenLabel">The text of the Open button.</param>
public sealed record PalettePreview(SearchScope Kind, string Title, string Subtitle, IReadOnlyList<PalettePreviewLine> Lines, IReadOnlyList<TextSegment> Snippet, string OpenLabel)
{
    /// <summary>A badge next to the title ("Recurring"), or empty.</summary>
    public string Badge { get; init; } = string.Empty;
}
