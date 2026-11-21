// Mirrors: Rust gsearch.rs:421 Hit; Bulwark lib/global-search/types.ts:19-73 (HitBase and the four kinds: a hit carries what its preview shows). Stub from stage F of M2; owner search.

namespace Barbican.Core.Search;

/// <summary>One global search result.</summary>
/// <param name="Kind">The app the result belongs to.</param>
/// <param name="Id">The object id.</param>
/// <param name="Title">The main line.</param>
/// <param name="Subtitle">The second line.</param>
/// <param name="Date">An ISO date for tie-breaking (newest first).</param>
/// <param name="Place">Where opening it goes (a folder id for mail, a folder or parent id for files, a day for an event).</param>
public sealed record SearchHit(SearchScope Kind, string Id, string Title, string Subtitle, string Date, string Place)
{
    /// <summary>The lines the preview shows under the title (mail: from, folder, date; contact: emails, phones, organization; event: time, places, guests; file: size, date, folder).</summary>
    public IReadOnlyList<SearchDetail> Details { get; init; } = [];

    /// <summary>Mail only: the server's snippet of the body with the matched words marked; empty when the search had no text term or the server marked nothing.</summary>
    public IReadOnlyList<TextSegment> Snippet { get; init; } = [];

    /// <summary>Mail only: the subject with the matched words marked; empty when none was marked.</summary>
    public IReadOnlyList<TextSegment> SubjectSnippet { get; init; } = [];

    /// <summary>The body text of the preview when it has no marks (mail: the message's preview); empty when there is none.</summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>An event hit is the master of a recurring series.</summary>
    public bool IsRecurring { get; init; }

    /// <summary>A file hit is a folder.</summary>
    public bool IsFolder { get; init; }
}
