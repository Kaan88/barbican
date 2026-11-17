// Mirrors: Rust gsearch.rs:72 valid_date, :133 parse, :181 with_scope, :190 without_operator, :195 matches_terms, :206 split_search_text, :242 text_conditions,
// :283 mailbox_search_filter, :296 global_mail_filter, :308 has_remote_query, :314 chips, :332 has_snippet_terms, :339 snippet_filter, :365 snippet_segments,
// :387 has_marked, :390 snippet_markdown, :445 score, :460 rank, :473 hit_folder, :572 filter_files; Bulwark lib/search-syntax.ts, lib/jmap/search-utils.ts,
// lib/search-snippet.ts; RFC 8621 section 4.4.1 (Email/query filter), RFC 8621 section 5 (SearchSnippet/get); Stalwart crates/jmap/src/email/{query,snippet}.rs.
// Stub from stage F of M2; owner search (wave 2).

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Search;

/// <summary>The search syntax and the JMAP filters built from it, as pure functions.</summary>
public static class GlobalSearch
{
    /// <summary>The preferred folder of a hit: the open folder, else the Inbox, else the smallest id; empty without mailboxIds.</summary>
    public static string HitFolder(JsonNode message, IReadOnlyList<string> prefer)
    {
        throw new PortPendingException("gsearch.rs:473 hit_folder");
    }

    /// <summary>Splits operators (in:, account:, from:, to:, subject:, has:, is:, after:, before:) from text.</summary>
    public static ParsedSearch Parse(string raw)
    {
        throw new PortPendingException("gsearch.rs:133 parse");
    }

    /// <summary>The string with its in: scope replaced (All removes it; in:trash and in:spam stay).</summary>
    public static string WithScope(string raw, SearchScope scope)
    {
        throw new PortPendingException("gsearch.rs:181 with_scope");
    }

    /// <summary>The string without the operators of one key.</summary>
    public static string WithoutOperator(string raw, string key)
    {
        throw new PortPendingException("gsearch.rs:190 without_operator");
    }

    /// <summary>Whether every term is in some field (case-insensitive); no terms never match.</summary>
    public static bool MatchesTerms(IReadOnlyList<string> terms, IReadOnlyList<string> fields)
    {
        throw new PortPendingException("gsearch.rs:195 matches_terms");
    }

    /// <summary>Bulwark's split of free text: quoted phrases stay whole, a lone * gives nothing, wildcards stay on their word.</summary>
    public static IReadOnlyList<string> SplitSearchText(string raw)
    {
        throw new PortPendingException("gsearch.rs:206 split_search_text");
    }

    /// <summary>The JMAP conditions of free text for one property ("text", "subject").</summary>
    public static IReadOnlyList<JsonNode> TextConditions(string raw, string property)
    {
        throw new PortPendingException("gsearch.rs:242 text_conditions");
    }

    /// <summary>The Email/query filter for a mail search in one folder; <paramref name="roleId"/> maps "trash" and "junk" to mailbox ids.</summary>
    public static JsonNode MailboxSearchFilter(string text, string? folderId, Func<string, string?> roleId)
    {
        throw new PortPendingException("gsearch.rs:283 mailbox_search_filter");
    }

    /// <summary>The filter for a global mail search: Trash and Junk are left out unless is:anything.</summary>
    public static JsonNode GlobalMailFilter(ParsedSearch parsed, Func<string, string?> roleId)
    {
        throw new PortPendingException("gsearch.rs:296 global_mail_filter");
    }

    /// <summary>Whether the search needs a server query (text for non-mail scopes; any filter for mail).</summary>
    public static bool HasRemoteQuery(ParsedSearch parsed, SearchScope scope)
    {
        throw new PortPendingException("gsearch.rs:308 has_remote_query");
    }

    /// <summary>The removable chips for the operators in force, in order from, to, subject, has, after, before, is, in.</summary>
    public static IReadOnlyList<SearchChip> Chips(ParsedSearch parsed)
    {
        throw new PortPendingException("gsearch.rs:314 chips");
    }

    /// <summary>Whether a filter has a term the server can highlight (text, subject or body).</summary>
    public static bool HasSnippetTerms(JsonNode filter)
    {
        throw new PortPendingException("gsearch.rs:332 has_snippet_terms");
    }

    /// <summary>The filter with quotes and * taken out of the terms, for SearchSnippet/get.</summary>
    public static JsonNode SnippetFilter(JsonNode filter)
    {
        throw new PortPendingException("gsearch.rs:339 snippet_filter");
    }

    /// <summary>Splits a server snippet (only mark tags and the HTML entities of text) into segments; any other tag is text.</summary>
    public static IReadOnlyList<TextSegment> SnippetSegments(string snippet)
    {
        throw new PortPendingException("gsearch.rs:365 snippet_segments");
    }

    /// <summary>Whether any non-empty segment is marked.</summary>
    public static bool HasMarked(IReadOnlyList<TextSegment> segments)
    {
        throw new PortPendingException("gsearch.rs:387 has_marked");
    }

    /// <summary>The segments as markdown with the marked runs bold in a colour (Slint StyledText in Rust; the C# UI renders runs itself).</summary>
    public static string SnippetMarkdown(IReadOnlyList<TextSegment> segments, string color)
    {
        throw new PortPendingException("gsearch.rs:390 snippet_markdown");
    }

    /// <summary>Orders hits: whole-title match, then word-start, then contains, then the rest; ties newest first.</summary>
    public static IReadOnlyList<SearchHit> Rank(IReadOnlyList<SearchHit> hits, ParsedSearch parsed)
    {
        throw new PortPendingException("gsearch.rs:460 rank");
    }

    /// <summary>File and folder hits for the terms; anything under a dot folder (.Trash) is skipped; the word folder matches folders by kind.</summary>
    public static IReadOnlyList<SearchHit> FilterFiles(IReadOnlyList<JsonNode> nodes, ParsedSearch parsed)
    {
        throw new PortPendingException("gsearch.rs:572 filter_files");
    }
}
