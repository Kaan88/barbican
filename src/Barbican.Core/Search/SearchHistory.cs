// Mirrors: Rust search_history.rs:10-13 (limits), :30 with_added, :58 build, :78 people_matching, :85 person_query; Bulwark lib/search-suggestions.ts,
// stores/search-store.ts (recent searches). Stub from stage F of M2; owner search.

using Barbican.Core.Common;

namespace Barbican.Core.Search;

/// <summary>Recent searches and the suggestions built from them.</summary>
public static class SearchHistory
{
    public const int MaxRecent = 10;
    public const int MaxRecentIdle = 5;
    public const int MaxRecentTyping = 3;
    public const int MaxContacts = 5;

    /// <summary>The list with the query added at the front (trimmed, case-insensitive duplicates removed, capped at <see cref="MaxRecent"/>); blank adds nothing.</summary>
    public static IReadOnlyList<string> WithAdded(IReadOnlyList<string> list, string query)
    {
        throw new PortPendingException("search_history.rs:30 with_added");
    }

    /// <summary>The suggestions: the newest recent searches for an empty box, else matching recent searches (not the exact one) then the given people (the caller already matched them with PeopleMatching; deduplicated by address, entries without an address dropped, capped at MaxContacts).</summary>
    public static IReadOnlyList<SearchSuggestion> Build(string query, IReadOnlyList<string> recent, IReadOnlyList<(string Name, string Email)> people)
    {
        throw new PortPendingException("search_history.rs:58 build");
    }

    /// <summary>The contacts whose name or address contains the query; contacts without an address never match.</summary>
    public static IReadOnlyList<(string Name, string Email)> PeopleMatching(string query, IReadOnlyList<(string Name, string Email)> candidates)
    {
        throw new PortPendingException("search_history.rs:78 people_matching");
    }

    /// <summary>"from:address".</summary>
    public static string PersonQuery(string email)
    {
        throw new PortPendingException("search_history.rs:85 person_query");
    }
}
