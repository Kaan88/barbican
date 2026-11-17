// Mirrors: Bulwark lib/plugin-search-results.ts (ExternalSearchResult: key, title, snippet, score, email). Owner search.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>One entry a plugin adds to a search.</summary>
/// <param name="Key">The entry's key (comes back with onAction for a click that opens no message).</param>
/// <param name="Title">The main line.</param>
/// <param name="Snippet">The second line (may be empty).</param>
/// <param name="EmailId">The message the entry opens, or null.</param>
public sealed record ExternalSearchResult(string Key, string Title, string Snippet, string? EmailId);
