// Mirrors: Rust search_history.rs:52 Suggestion (Recent, Person); Bulwark lib/search-suggestions.ts buildSearchSuggestions. Stub from stage F of M2; owner search.

namespace Barbican.Core.Search;

/// <summary>One suggestion under the search box.</summary>
public abstract record SearchSuggestion
{
    /// <summary>A recent search.</summary>
    public sealed record Recent(string Query) : SearchSuggestion;

    /// <summary>A person from the contacts: searching for them means from:address.</summary>
    public sealed record Person(string Name, string Email) : SearchSuggestion;
}
