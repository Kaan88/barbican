// Mirrors: Bulwark onSearchResults (lib/plugin-hook-catalog.ts:146). Owner mailcore.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The ids a search found; a plugin may reorder or drop some (transform).</summary>
public sealed record SearchResultsPayload(string Query, IReadOnlyList<string> EmailIds);
