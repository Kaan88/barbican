// Mirrors: RFC 8621 section 5 (a SearchSnippet: emailId, subject and preview with <mark> around the hits); Bulwark lib/search-snippet.ts.

namespace Barbican.Core.Search;

/// <summary>The server's snippet of one message: each part with <c>&lt;mark&gt;</c> tags, or null when the part had no hit.</summary>
public sealed record SnippetEntry(string EmailId, string? Subject, string? Preview);
