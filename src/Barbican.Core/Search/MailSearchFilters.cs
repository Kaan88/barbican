// Mirrors: Rust gsearch.rs:27 MailFilters; Bulwark lib/search-syntax.ts (the operator filters), lib/jmap/search-utils.ts:1-19 (SearchFilters: body, minSizeKb,
// maxSizeKb). Stub from stage F of M2; owner search.

namespace Barbican.Core.Search;

/// <summary>The mail operators found in a search string.</summary>
public sealed record MailSearchFilters
{
    public string? From { get; init; }

    public string? To { get; init; }

    public string? Subject { get; init; }

    /// <summary>Has the words: the body text (Bulwark filters.body, search-filter-panel.tsx:137).</summary>
    public string? Body { get; init; }

    /// <summary>Message size bounds in KB as typed (Bulwark minSizeKb / maxSizeKb); <see cref="GlobalSearch.SizeBytes"/> validates them.</summary>
    public string? MinSizeKb { get; init; }

    /// <inheritdoc cref="MinSizeKb" />
    public string? MaxSizeKb { get; init; }

    public bool? HasAttachment { get; init; }

    public bool? Unread { get; init; }

    public bool? Starred { get; init; }

    /// <summary>A valid day, yyyy-MM-dd.</summary>
    public string? After { get; init; }

    /// <summary>A valid day, yyyy-MM-dd.</summary>
    public string? Before { get; init; }
}
