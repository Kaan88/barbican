// Mirrors: Bulwark onProvideSearchResults (lib/plugin-hook-catalog.ts:147; lib/plugin-search-results.ts): a plugin adds entries to a search; one that names a message opens it. Owner search.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The entries plugins add to a search (transform): each handler gets the list so far and returns it with its own entries.</summary>
/// <param name="Query">The search as typed.</param>
/// <param name="Results">The entries so far.</param>
public sealed record ProvideSearchResultsPayload(string Query, IReadOnlyList<ExternalSearchResult> Results);
