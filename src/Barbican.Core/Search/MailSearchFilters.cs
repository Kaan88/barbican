// Mirrors: Rust gsearch.rs:27 MailFilters; Bulwark lib/search-syntax.ts (the operator filters). Stub from stage F of M2; owner search.

namespace Barbican.Core.Search;

/// <summary>The mail operators found in a search string.</summary>
public sealed record MailSearchFilters
{
    public string? From { get; init; }

    public string? To { get; init; }

    public string? Subject { get; init; }

    public bool? HasAttachment { get; init; }

    public bool? Unread { get; init; }

    public bool? Starred { get; init; }

    /// <summary>A valid day, yyyy-MM-dd.</summary>
    public string? After { get; init; }

    /// <summary>A valid day, yyyy-MM-dd.</summary>
    public string? Before { get; init; }
}
