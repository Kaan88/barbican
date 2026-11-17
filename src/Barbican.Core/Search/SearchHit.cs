// Mirrors: Rust gsearch.rs:421 Hit; Bulwark components/command-palette (the global search results). Stub from stage F of M2; owner search.

namespace Barbican.Core.Search;

/// <summary>One global search result.</summary>
/// <param name="Kind">The app the result belongs to.</param>
/// <param name="Id">The object id.</param>
/// <param name="Title">The main line.</param>
/// <param name="Subtitle">The second line.</param>
/// <param name="Date">An ISO date for tie-breaking (newest first).</param>
/// <param name="Place">Where opening it goes (a folder id for mail, a folder or parent id for files).</param>
public sealed record SearchHit(SearchScope Kind, string Id, string Title, string Subtitle, string Date, string Place);
