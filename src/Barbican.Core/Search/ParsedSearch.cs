// Mirrors: Rust gsearch.rs:39 Parsed; Bulwark lib/search-syntax.ts. Stub from stage F of M2; owner search.

namespace Barbican.Core.Search;

/// <summary>A search string split into operators and free text.</summary>
public sealed record ParsedSearch
{
    public string Raw { get; init; } = string.Empty;

    /// <summary>What is left after the operators were taken out (unknown keys, impossible dates and phrases stay text).</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>The words of <see cref="Text"/> for matching local data.</summary>
    public IReadOnlyList<string> Terms { get; init; } = [];

    public SearchScope Scope { get; init; } = SearchScope.All;

    public string? Account { get; init; }

    public MailSearchFilters Mail { get; init; } = new();

    /// <summary>"trash" or "junk" for in:trash and in:spam, else null.</summary>
    public string? MailboxRole { get; init; }

    /// <summary>True for is:anything: Trash and Junk are searched too.</summary>
    public bool IncludeTrashAndJunk { get; init; }

    public bool HasOperators { get; init; }
}
