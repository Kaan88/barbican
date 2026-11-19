// Mirrors: M2 plan 4.4 (IListQueryShaper: the search grammar as the list's filter); Rust gsearch.rs:283 mailbox_search_filter (the typed query inside the open folder, in:trash and
// in:spam search that folder instead), main.rs:1580; Bulwark lib/jmap/search-utils.ts buildJMAPFilter. Chip removal rewrites the text (GlobalSearch.WithoutOperator), so the text is the only state.

using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Mail.Model;
using Barbican.Core.Search;

namespace Barbican.Core.Mail.Lists;

/// <summary>Turns the search box's text into the Email/query filter of the list, and tells the snippet source which words to ask for.</summary>
public sealed class SearchListQueryShaper : IListQueryShaper
{
    private readonly Func<IReadOnlyList<JsonObject>?> _mailboxes;
    private readonly SearchSnippetSource? _snippets;

    /// <param name="mailboxes">The folder objects last read (their id and role give the Trash and Junk ids); null before the first read.</param>
    /// <param name="snippets">The snippet source to tell about the filter in force; null when snippets are not wanted.</param>
    public SearchListQueryShaper(Func<IReadOnlyList<JsonObject>?> mailboxes, SearchSnippetSource? snippets = null)
    {
        ArgumentNullException.ThrowIfNull(mailboxes);
        _mailboxes = mailboxes;
        _snippets = snippets;
    }

    /// <inheritdoc />
    public ListQuery Shape(ListQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.SearchText?.Trim() is not { Length: > 0 } text)
        {
            _snippets?.SetFilter(null);
            return query;
        }

        var filter = query.MailboxId is null
            ? GlobalSearch.GlobalMailFilter(GlobalSearch.Parse(text), RoleId)
            : GlobalSearch.MailboxSearchFilter(text, query.MailboxId, RoleId);
        _snippets?.SetFilter(filter);
        return query with { Filter = filter as JsonObject ?? [] };
    }

    private string? RoleId(string role) =>
        _mailboxes()?.FirstOrDefault(m => string.Equals(m.Str("role"), role, StringComparison.Ordinal))?.Str("id");
}
