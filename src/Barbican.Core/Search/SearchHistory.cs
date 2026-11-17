// Mirrors: Rust search_history.rs:10-13 (limits), :30 with_added, :58 build, :78 people_matching, :85 person_query; Bulwark lib/search-suggestions.ts,
// stores/search-history-store.ts (ten raw queries, newest first, no duplicates).

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
        var t = query.Trim();
        if (t.Length == 0)
        {
            return list;
        }

        var v = new List<string> { t };
        v.AddRange(list.Where(x => !string.Equals(x, t, StringComparison.OrdinalIgnoreCase)));
        return v.Take(MaxRecent).ToList();
    }

    /// <summary>The suggestions: the newest recent searches for an empty box, else matching recent searches (not the exact one) then the given people (the caller already matched them with PeopleMatching; deduplicated by address, entries without an address dropped, capped at MaxContacts).</summary>
    public static IReadOnlyList<SearchSuggestion> Build(string query, IReadOnlyList<string> recent, IReadOnlyList<(string Name, string Email)> people)
    {
        var q = query.Trim().ToLowerInvariant();
        if (q.Length == 0)
        {
            return recent.Take(MaxRecentIdle).Select(r => (SearchSuggestion)new SearchSuggestion.Recent(r)).ToList();
        }

        var res = recent
            .Where(r =>
            {
                var l = r.ToLowerInvariant();
                return !string.Equals(l, q, StringComparison.Ordinal) && l.Contains(q, StringComparison.Ordinal);
            })
            .Take(MaxRecentTyping)
            .Select(r => (SearchSuggestion)new SearchSuggestion.Recent(r))
            .ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, email) in people)
        {
            if (seen.Count >= MaxContacts)
            {
                break;
            }

            var e = email.Trim();
            if (e.Length == 0 || !seen.Add(e.ToLowerInvariant()))
            {
                continue;
            }

            res.Add(new SearchSuggestion.Person(name.Trim(), e));
        }

        return res;
    }

    /// <summary>The contacts whose name or address contains the query; contacts without an address never match.</summary>
    public static IReadOnlyList<(string Name, string Email)> PeopleMatching(string query, IReadOnlyList<(string Name, string Email)> candidates)
    {
        var q = query.Trim().ToLowerInvariant();
        if (q.Length == 0)
        {
            return [];
        }

        return candidates
            .Where(c => c.Email.Trim().Length > 0 && (c.Name.ToLowerInvariant().Contains(q, StringComparison.Ordinal) || c.Email.ToLowerInvariant().Contains(q, StringComparison.Ordinal)))
            .ToList();
    }

    /// <summary>"from:address" (quoted when the address has whitespace).</summary>
    public static string PersonQuery(string email) => email.Any(char.IsWhiteSpace) ? $"from:\"{email}\"" : $"from:{email}";
}
