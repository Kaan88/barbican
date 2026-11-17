// Mirrors: Rust gsearch.rs:365 snippet_segments ((text, marked)); M2 plan section 4.4 (ISnippetSource); Bulwark lib/search-snippet.ts. Stub from stage F of M2; owner search.

namespace Barbican.Core.Search;

/// <summary>A run of snippet text, marked when the server highlighted it (a hit).</summary>
public sealed record TextSegment(string Text, bool Marked);
