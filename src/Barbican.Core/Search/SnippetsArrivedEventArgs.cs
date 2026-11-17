// Mirrors: M2 plan section 4.4 (ISnippetSource.Arrived); Bulwark lib/search-snippet.ts. Contract from stage F of M2; owner search (wave 2).

namespace Barbican.Core.Search;

/// <summary>Snippets for some messages arrived from SearchSnippet/get.</summary>
public sealed class SnippetsArrivedEventArgs(IReadOnlyList<string> emailIds) : EventArgs
{
    /// <summary>The ids whose snippets are now available, or whose marks are stale (the search changed). Empty: the search changed and every row asks again.</summary>
    public IReadOnlyList<string> EmailIds { get; } = emailIds;
}
