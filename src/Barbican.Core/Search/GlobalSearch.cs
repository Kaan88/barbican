// Mirrors: Rust gsearch.rs:72 valid_date, :133 parse, :181 with_scope, :190 without_operator, :195 matches_terms, :206 split_search_text, :242 text_conditions,
// :283 mailbox_search_filter, :296 global_mail_filter, :308 has_remote_query, :314 chips, :332 has_snippet_terms, :339 snippet_filter, :365 snippet_segments,
// :387 has_marked, :390 snippet_markdown, :445 score, :460 rank, :473 hit_folder, :572 filter_files; Bulwark lib/search-syntax.ts, lib/jmap/search-utils.ts,
// lib/search-snippet.ts; RFC 8621 section 4.4.1 (Email/query filter: minSize inclusive, maxSize exclusive), RFC 8621 section 5 (SearchSnippet/get);
// Stalwart crates/jmap/src/email/{query,snippet}.rs. body:, larger: and smaller: (KB) carry Bulwark's filter panel fields body / minSizeKb / maxSizeKb
// (lib/jmap/search-utils.ts:98-102 sizeFilterBytes, :200-202 body, :237-245 minSize/maxSize).

#pragma warning disable MA0051 // ported parsers and generators read top to bottom in the order of the Rust source

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Barbican.Core.Search;

/// <summary>The search syntax and the JMAP filters built from it, as pure functions.</summary>
public static class GlobalSearch
{
    /// <summary>The name of the Files trash folder (Rust files::TRASH_NAME).</summary>
    public const string FilesTrashName = ".Trash";

    private static readonly string[] TrashAndJunk = ["trash", "junk"];
    private static readonly string[] SnippetKeys = ["text", "subject", "body"];

    private static readonly string[] Operators = ["in", "account", "from", "to", "subject", "body", "has", "is", "after", "before", "larger", "smaller"];

    private static SearchScope? ScopeAlias(string s) => s switch
    {
        "all" or "everything" => SearchScope.All,
        "mail" or "email" or "emails" or "messages" => SearchScope.Mail,
        "contacts" or "contact" or "people" => SearchScope.Contacts,
        "calendar" or "calendars" or "events" or "event" => SearchScope.Calendar,
        "files" or "file" or "drive" => SearchScope.Files,
        _ => null,
    };

    private static string ScopeName(SearchScope s) => s switch
    {
        SearchScope.Mail => "mail",
        SearchScope.Contacts => "contacts",
        SearchScope.Calendar => "calendar",
        SearchScope.Files => "files",
        _ => "all",
    };

    private static string? Str(JsonNode? n, string key) => n is JsonObject o && o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>A yyyy-MM-dd that names a real day.</summary>
    public static bool ValidDate(string v) =>
        v.Length == 10 && DateTime.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>The KB size field as a positive byte count, or null when unset or invalid (Bulwark sizeFilterBytes, search-utils.ts:98-102).</summary>
    public static long? SizeBytes(string? kb)
    {
        if (string.IsNullOrWhiteSpace(kb) ||
            !double.TryParse(kb.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ||
            !double.IsFinite(v) || v <= 0)
        {
            return null;
        }

        return (long)Math.Round(v * 1024, MidpointRounding.AwayFromZero);
    }

    /// <summary>The preferred folder of a hit: the open folder, else the Inbox, else the smallest id; empty without mailboxIds.</summary>
    public static string HitFolder(JsonNode message, IReadOnlyList<string> prefer)
    {
        if (message["mailboxIds"] is not JsonObject o)
        {
            return string.Empty;
        }

        bool In(string k) => o[k] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
        foreach (var p in prefer)
        {
            if (p.Length > 0 && In(p))
            {
                return p;
            }
        }

        return o.Select(kv => kv.Key).Where(In).OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault() ?? string.Empty;
    }

    /// <summary>The tokens of a search string: key:"quoted", key:value, "phrase" or a bare word; a missing closing quote is fine.</summary>
    public static IReadOnlyList<SearchToken> Tokenize(string raw)
    {
        var res = new List<SearchToken>();
        var ch = raw.ToCharArray();
        var i = 0;
        char? At(int k) => k < ch.Length ? ch[k] : null;
        while (i < ch.Length)
        {
            if (char.IsWhiteSpace(ch[i]))
            {
                i++;
                continue;
            }

            var start = i;
            var j = i;
            while (j < ch.Length && char.IsAsciiLetter(ch[j]))
            {
                j++;
            }

            if (j > i && At(j) == ':')
            {
                var key = new string(ch, i, j - i).ToLowerInvariant();
                var k = j + 1;
                if (At(k) == '"')
                {
                    k++;
                    var vs = k;
                    while (k < ch.Length && ch[k] != '"')
                    {
                        k++;
                    }

                    var value = new string(ch, vs, k - vs);
                    if (k < ch.Length)
                    {
                        k++;
                    }

                    res.Add(new SearchToken(key, value, true, new string(ch, start, k - start)));
                    i = k;
                    continue;
                }

                var vs2 = k;
                while (k < ch.Length && !char.IsWhiteSpace(ch[k]))
                {
                    k++;
                }

                res.Add(new SearchToken(key, new string(ch, vs2, k - vs2), false, new string(ch, start, k - start)));
                i = k;
                continue;
            }

            if (ch[i] == '"')
            {
                var k = i + 1;
                while (k < ch.Length && ch[k] != '"')
                {
                    k++;
                }

                var value = new string(ch, i + 1, k - i - 1);
                if (k < ch.Length)
                {
                    k++;
                }

                res.Add(new SearchToken(null, value, true, new string(ch, start, k - start)));
                i = k;
                continue;
            }

            var e = i;
            while (e < ch.Length && !char.IsWhiteSpace(ch[e]))
            {
                e++;
            }

            var w = new string(ch, i, e - i);
            res.Add(new SearchToken(null, w, false, w));
            i = e;
        }

        return res;
    }

    /// <summary>Splits operators (in:, account:, from:, to:, subject:, has:, is:, after:, before:) from text.</summary>
    public static ParsedSearch Parse(string raw)
    {
        var text = new List<string>();
        var terms = new List<string>();
        var scope = SearchScope.All;
        string? account = null;
        string? role = null;
        var anything = false;
        var hasOps = false;
        string? from = null, to = null, subject = null, body = null, after = null, before = null, minSizeKb = null, maxSizeKb = null;
        bool? hasAtt = null, unread = null, starred = null;

        void Push(string v, bool quoted)
        {
            var t = v.Trim();
            if (t.Length == 0)
            {
                return;
            }

            text.Add(quoted ? $"\"{t}\"" : t);
            terms.Add(t.ToLowerInvariant());
        }

        foreach (var tok in Tokenize(raw))
        {
            if (tok.Key is null || !Operators.Contains(tok.Key))
            {
                if (tok.Key is not null)
                {
                    Push(tok.Original, false);
                }
                else
                {
                    Push(tok.Value, tok.Quoted);
                }

                continue;
            }

            var value = tok.Value.Trim();
            var lower = value.ToLowerInvariant();
            var used = true;
            switch (tok.Key)
            {
                case "in":
                    if (ScopeAlias(lower) is { } sc)
                    {
                        scope = sc;
                    }
                    else if (lower is "trash" or "bin")
                    {
                        scope = SearchScope.Mail;
                        role = "trash";
                    }
                    else if (lower is "junk" or "spam")
                    {
                        scope = SearchScope.Mail;
                        role = "junk";
                    }
                    else
                    {
                        used = false;
                    }

                    break;
                case "account":
                    if (lower.Length == 0) { used = false; } else { account = lower; }
                    break;
                case "from":
                    if (value.Length == 0) { used = false; } else { from = value; }
                    break;
                case "to":
                    if (value.Length == 0) { used = false; } else { to = value; }
                    break;
                case "subject":
                    if (value.Length == 0) { used = false; } else { subject = value; }
                    break;
                case "body":
                    if (value.Length == 0) { used = false; } else { body = value; }
                    break;
                case "larger":
                    if (SizeBytes(value) is null) { used = false; } else { minSizeKb = value; }
                    break;
                case "smaller":
                    if (SizeBytes(value) is null) { used = false; } else { maxSizeKb = value; }
                    break;
                case "has":
                    if (lower is "attachment" or "attachments" or "file") { hasAtt = true; } else { used = false; }
                    break;
                case "is":
                    switch (lower)
                    {
                        case "unread": unread = true; break;
                        case "read": unread = false; break;
                        case "starred" or "flagged": starred = true; break;
                        case "unstarred": starred = false; break;
                        case "anything" or "any": anything = true; break;
                        default: used = false; break;
                    }

                    break;
                case "after":
                    if (ValidDate(value)) { after = value; } else { used = false; }
                    break;
                case "before":
                    if (ValidDate(value)) { before = value; } else { used = false; }
                    break;
                default:
                    used = false;
                    break;
            }

            if (used)
            {
                hasOps = true;
            }
            else
            {
                Push(tok.Original, false);
            }
        }

        return new ParsedSearch
        {
            Raw = raw,
            Text = string.Join(' ', text),
            Terms = terms,
            Scope = scope,
            Account = account,
            Mail = new MailSearchFilters { From = from, To = to, Subject = subject, Body = body, HasAttachment = hasAtt, Unread = unread, Starred = starred, After = after, Before = before, MinSizeKb = minSizeKb, MaxSizeKb = maxSizeKb },
            MailboxRole = role,
            IncludeTrashAndJunk = anything,
            HasOperators = hasOps,
        };
    }

    /// <summary>The string with its in: scope replaced (All removes it; in:trash and in:spam stay).</summary>
    public static string WithScope(string raw, SearchScope scope)
    {
        var kept = Tokenize(raw)
            .Where(t => !(t.Key is "in" && ScopeAlias(t.Value.ToLowerInvariant()) is not null))
            .Select(t => t.Original)
            .ToList();
        if (scope != SearchScope.All)
        {
            kept.Add($"in:{ScopeName(scope)}");
        }

        return string.Join(' ', kept);
    }

    /// <summary>The string without the operators of one key.</summary>
    public static string WithoutOperator(string raw, string key) =>
        string.Join(' ', Tokenize(raw).Where(t => !string.Equals(t.Key, key, StringComparison.Ordinal)).Select(t => t.Original));

    /// <summary>Whether every term is in some field (case-insensitive); no terms never match.</summary>
    public static bool MatchesTerms(IReadOnlyList<string> terms, IReadOnlyList<string> fields)
    {
        if (terms.Count == 0)
        {
            return false;
        }

        var hay = fields.Where(f => f.Length > 0).Select(f => f.ToLowerInvariant()).ToList();
        return hay.Count != 0 && terms.All(t => hay.Any(f => f.Contains(t, StringComparison.Ordinal)));
    }

    /// <summary>Bulwark's split of free text: quoted phrases stay whole, a lone * gives nothing, wildcards stay on their word.</summary>
    public static IReadOnlyList<string> SplitSearchText(string raw)
    {
        var parts = new List<string>();
        var words = new List<string>();
        int? wordsAt = null;
        var ch = raw.ToCharArray();
        var i = 0;
        while (i < ch.Length)
        {
            if (char.IsWhiteSpace(ch[i]))
            {
                i++;
                continue;
            }

            if (ch[i] == '"')
            {
                var k = i + 1;
                while (k < ch.Length && ch[k] != '"')
                {
                    k++;
                }

                var phrase = new string(ch, i + 1, k - i - 1).Trim();
                if (phrase.Length > 0)
                {
                    parts.Add($"\"{phrase}\"");
                }

                i = Math.Min(k + 1, ch.Length);
                continue;
            }

            var e = i;
            while (e < ch.Length && !char.IsWhiteSpace(ch[e]) && ch[e] != '"')
            {
                e++;
            }

            var w = new string(ch, i, e - i);
            if (w.EndsWith('*'))
            {
                if (!w.All(c => c == '*'))
                {
                    parts.Add(w);
                }
            }
            else
            {
                wordsAt ??= parts.Count;
                words.Add(w);
            }

            i = e;
        }

        var bare = string.Join(' ', words);
        while (bare.Length > 1 && bare[0] == '\'' && bare[^1] == '\'')
        {
            bare = bare[1..^1].Trim();
        }

        if (bare.Length > 0)
        {
            parts.Insert(Math.Min(wordsAt ?? parts.Count, parts.Count), bare);
        }

        return parts;
    }

    private static JsonObject Prop(string property, string value) => new JsonObject { [property] = value };

    /// <summary>The JMAP conditions of free text for one property ("text", "subject").</summary>
    public static IReadOnlyList<JsonNode> TextConditions(string raw, string property)
    {
        var parts = SplitSearchText(raw);
        if (parts.Count == 0)
        {
            return [Prop(property, raw.Trim())];
        }

        return parts.Select(p => Prop(property, p)).ToList();
    }

    private static JsonNode AndAll(List<JsonNode> conds) => conds.Count switch
    {
        0 => new JsonObject(),
        1 => conds[0],
        _ => new JsonObject { ["operator"] = "AND", ["conditions"] = new JsonArray(conds.ToArray()) },
    };

    private static string? DayBound(string d, bool end)
    {
        if (!DateTime.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return null;
        }

        var t = end ? day.AddHours(23).AddMinutes(59).AddSeconds(59) : day;
        DateTime utc;
        try
        {
            utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(t, DateTimeKind.Unspecified), TimeZoneInfo.Local);
        }
        catch (ArgumentException)
        {
            utc = DateTime.SpecifyKind(t, DateTimeKind.Utc);
        }

        return utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static List<JsonNode> MailConditions(ParsedSearch p)
    {
        var c = new List<JsonNode>();
        if (p.Text.Trim().Length > 0)
        {
            c.AddRange(TextConditions(p.Text, "text"));
        }

        var m = p.Mail;
        if (m.From is not null) { c.AddRange(TextConditions(m.From, "from")); }
        if (m.To is not null) { c.AddRange(TextConditions(m.To, "to")); }
        if (m.Subject is not null) { c.AddRange(TextConditions(m.Subject, "subject")); }
        if (m.Body is not null) { c.AddRange(TextConditions(m.Body, "body")); }
        if (m.HasAttachment is { } a) { c.Add(new JsonObject { ["hasAttachment"] = a }); }
        if (m.After is not null && DayBound(m.After, false) is { } af) { c.Add(Prop("after", af)); }
        if (m.Before is not null && DayBound(m.Before, true) is { } bf) { c.Add(Prop("before", bf)); }
        if (m.Unread is { } u) { c.Add(Prop(u ? "notKeyword" : "hasKeyword", "$seen")); }
        if (m.Starred is { } s) { c.Add(Prop(s ? "hasKeyword" : "notKeyword", "$flagged")); }

        // minSize is inclusive, maxSize exclusive (RFC 8621 §4.4.1; Bulwark search-utils.ts:237-245).
        if (SizeBytes(m.MinSizeKb) is { } minSize) { c.Add(new JsonObject { ["minSize"] = minSize }); }
        if (SizeBytes(m.MaxSizeKb) is { } maxSize) { c.Add(new JsonObject { ["maxSize"] = maxSize }); }
        return c;
    }

    /// <summary>The Email/query filter for a mail search in one folder; <paramref name="roleId"/> maps "trash" and "junk" to mailbox ids.</summary>
    public static JsonNode MailboxSearchFilter(string text, string? folderId, Func<string, string?> roleId)
    {
        var p = Parse(text);
        var c = MailConditions(p);
        if (c.Count == 0)
        {
            c.Add(Prop("text", text.Trim()));
        }

        var roleFolder = p.MailboxRole is null ? null : roleId(p.MailboxRole);
        if (roleFolder is not null)
        {
            c.Insert(0, Prop("inMailbox", roleFolder));
        }
        else if (!string.IsNullOrEmpty(folderId))
        {
            c.Insert(0, Prop("inMailbox", folderId));
        }

        return AndAll(c);
    }

    /// <summary>The filter for a global mail search: Trash and Junk are left out unless is:anything.</summary>
    public static JsonNode GlobalMailFilter(ParsedSearch parsed, Func<string, string?> roleId)
    {
        var c = MailConditions(parsed);
        if (parsed.MailboxRole is not null)
        {
            if (roleId(parsed.MailboxRole) is { } id)
            {
                c.Add(Prop("inMailbox", id));
            }
        }
        else if (!parsed.IncludeTrashAndJunk)
        {
            var skip = TrashAndJunk.Select(roleId).Where(x => x is not null).Select(x => (JsonNode?)x!).ToArray();
            if (skip.Length > 0)
            {
                c.Add(new JsonObject { ["inMailboxOtherThan"] = new JsonArray(skip) });
            }
        }

        return AndAll(c);
    }

    /// <summary>Whether the search needs a server query (text for non-mail scopes; any filter for mail).</summary>
    public static bool HasRemoteQuery(ParsedSearch parsed, SearchScope scope)
    {
        if (parsed.Text.Trim().Length > 0)
        {
            return true;
        }

        return scope == SearchScope.Mail && (parsed.Mail != new MailSearchFilters() || parsed.MailboxRole is not null);
    }

    /// <summary>The removable chips for the operators in force, in order from, to, subject, body, has, after, before, is, larger, smaller, in.</summary>
    public static IReadOnlyList<SearchChip> Chips(ParsedSearch parsed)
    {
        var m = parsed.Mail;
        var v = new List<SearchChip>();
        if (m.From is not null) { v.Add(new("from", "From", m.From)); }
        if (m.To is not null) { v.Add(new("to", "To", m.To)); }
        if (m.Subject is not null) { v.Add(new("subject", "Subject", m.Subject)); }
        if (m.Body is not null) { v.Add(new("body", "Body", m.Body)); }
        if (m.HasAttachment == true) { v.Add(new("has", "Attachments", "Yes")); }
        if (m.After is not null) { v.Add(new("after", "After", m.After)); }
        if (m.Before is not null) { v.Add(new("before", "Before", m.Before)); }
        if (m.Unread is { } u) { v.Add(new("is", u ? "Unread" : "Read", string.Empty)); }
        if (m.Starred is { } s) { v.Add(new("is", "Starred", s ? "Yes" : "No")); }

        // Bulwark search-chips.tsx:65-70: "Larger than (KB): 100 KB" while the size is a real bound.
        if (SizeBytes(m.MinSizeKb) is not null) { v.Add(new("larger", "Larger than (KB)", $"{m.MinSizeKb} KB")); }
        if (SizeBytes(m.MaxSizeKb) is not null) { v.Add(new("smaller", "Smaller than (KB)", $"{m.MaxSizeKb} KB")); }

        if (parsed.MailboxRole is { } r) { v.Add(new("in", "In", r is "trash" ? "Trash" : "Spam")); }
        return v;
    }

    /// <summary>Whether a filter has a term the server can highlight (text, subject or body).</summary>
    public static bool HasSnippetTerms(JsonNode filter)
    {
        if (filter["conditions"] is JsonArray a)
        {
            return a.Any(x => x is not null && HasSnippetTerms(x));
        }

        return SnippetKeys.Any(k => Str(filter, k) is { } s && s.Trim().Length > 0);
    }

    /// <summary>The filter with quotes and * taken out of the terms, for SearchSnippet/get.</summary>
    public static JsonNode SnippetFilter(JsonNode filter)
    {
        if (filter is not JsonObject o)
        {
            return filter.DeepClone();
        }

        var res = new JsonObject();
        foreach (var (k, v) in o)
        {
            JsonNode? nv;
            if (k is "conditions")
            {
                nv = new JsonArray((v as JsonArray)?.Select(x => x is null ? null : SnippetFilter(x)).ToArray() ?? []);
            }
            else if (k is "text" or "subject" or "body" && v is JsonValue jv && jv.TryGetValue<string>(out var s))
            {
                var t = s.Trim();
                if (t.Length >= 2 && ((t[0] == '"' && t[^1] == '"') || (t[0] == '\'' && t[^1] == '\'')))
                {
                    nv = t[1..^1];
                }
                else
                {
                    nv = string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(w => w.Length > 1 && w.EndsWith('*') ? w[..^1] : w));
                }
            }
            else
            {
                nv = v?.DeepClone();
            }

            res[k] = nv;
        }

        return res;
    }

    private static string Decode(string s) =>
        s.Replace("&lt;", "<", StringComparison.Ordinal).Replace("&gt;", ">", StringComparison.Ordinal).Replace("&quot;", "\"", StringComparison.Ordinal)
            .Replace("&apos;", "'", StringComparison.Ordinal).Replace("&#39;", "'", StringComparison.Ordinal).Replace("&#x27;", "'", StringComparison.Ordinal)
            .Replace("&amp;", "&", StringComparison.Ordinal);

    /// <summary>Splits a server snippet (only mark tags and the HTML entities of text) into segments; any other tag is text.</summary>
    public static IReadOnlyList<TextSegment> SnippetSegments(string snippet)
    {
        var res = new List<TextSegment>();
        var marked = false;
        var rest = snippet;
        while (true)
        {
            var open = rest.IndexOf("<mark>", StringComparison.Ordinal);
            var close = rest.IndexOf("</mark>", StringComparison.Ordinal);
            int at;
            if (open >= 0 && close >= 0) { at = Math.Min(open, close); }
            else if (open >= 0) { at = open; }
            else if (close >= 0) { at = close; }
            else { break; }

            if (at > 0)
            {
                res.Add(new TextSegment(Decode(rest[..at]), marked));
            }

            var isOpen = rest.AsSpan(at).StartsWith("<mark>", StringComparison.Ordinal);
            marked = isOpen;
            rest = rest[(at + (isOpen ? 6 : 7))..];
        }

        if (rest.Length > 0)
        {
            res.Add(new TextSegment(Decode(rest), marked));
        }

        return res;
    }

    /// <summary>Whether any non-empty segment is marked.</summary>
    public static bool HasMarked(IReadOnlyList<TextSegment> segments) => segments.Any(s => s.Marked && s.Text.Length > 0);

    private static string MdEscape(string s)
    {
        var o = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if ("\\`*_{}[]()#+-.!<>|~&".Contains(c, StringComparison.Ordinal))
            {
                o.Append('\\');
            }

            o.Append(c);
        }

        return o.ToString();
    }

    /// <summary>The segments as markdown with the marked runs bold in a colour (Slint StyledText in Rust; the C# UI renders runs itself).</summary>
    public static string SnippetMarkdown(IReadOnlyList<TextSegment> segments, string color)
    {
        var o = new StringBuilder();
        foreach (var seg in segments)
        {
            var t = seg.Text.Replace('\n', ' ').Replace('\r', ' ');
            if (seg.Marked && t.Trim().Length > 0)
            {
                var lead = t[..(t.Length - t.TrimStart().Length)];
                var trail = t[t.TrimEnd().Length..];
                o.Append(lead).Append("<font color=\"").Append(color).Append("\">**").Append(MdEscape(t.Trim())).Append("**</font>").Append(trail);
            }
            else
            {
                o.Append(MdEscape(t));
            }
        }

        return o.ToString();
    }

    private static bool AtWordBoundary(string field, string term)
    {
        var from = 0;
        while (from <= field.Length)
        {
            var i = field.IndexOf(term, from, StringComparison.Ordinal);
            if (i < 0)
            {
                return false;
            }

            if (i == 0 || !char.IsLetterOrDigit(field[i - 1]))
            {
                return true;
            }

            from = i + Math.Max(term.Length, 1);
            if (from >= field.Length)
            {
                break;
            }
        }

        return false;
    }

    private static int Score(SearchHit h, ParsedSearch p)
    {
        var q = string.Join(' ', p.Terms);
        if (q.Length == 0) { return 10; }
        var title = h.Title.ToLowerInvariant();
        if (string.Equals(title, q, StringComparison.Ordinal)) { return 100; }
        if (title.StartsWith(q, StringComparison.Ordinal)) { return 80; }
        if (p.Terms.All(t => AtWordBoundary(title, t))) { return 60; }
        if (p.Terms.All(t => title.Contains(t, StringComparison.Ordinal))) { return 50; }
        if (p.Terms.Any(t => title.Contains(t, StringComparison.Ordinal))) { return 30; }
        var sub = h.Subtitle.ToLowerInvariant();
        return p.Terms.Any(t => sub.Contains(t, StringComparison.Ordinal)) ? 20 : 10;
    }

    /// <summary>Orders hits: whole-title match, then word-start, then contains, then the rest; ties newest first.</summary>
    public static IReadOnlyList<SearchHit> Rank(IReadOnlyList<SearchHit> hits, ParsedSearch parsed) =>
        hits.OrderByDescending(h => Score(h, parsed)).ThenByDescending(h => h.Date, StringComparer.Ordinal).ThenBy(h => h.Title, StringComparer.Ordinal).ToList();

    /// <summary>File and folder hits for the terms; anything under a dot folder (.Trash) is skipped; the word folder matches folders by kind.</summary>
    public static IReadOnlyList<SearchHit> FilterFiles(IReadOnlyList<JsonNode> nodes, ParsedSearch parsed)
    {
        var byId = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        foreach (var n in nodes)
        {
            if (Str(n, "id") is { } id)
            {
                byId[id] = n;
            }
        }

        var trash = nodes.FirstOrDefault(n => string.Equals(Str(n, "name"), FilesTrashName, StringComparison.Ordinal) && string.IsNullOrEmpty(Str(n, "parentId")));
        var trashId = trash is null ? null : Str(trash, "id");

        bool InTrash(JsonNode n)
        {
            var cur = Str(n, "parentId");
            for (var guard = 0; !string.IsNullOrEmpty(cur) && guard <= 64; guard++)
            {
                if (string.Equals(cur, trashId, StringComparison.Ordinal)) { return true; }
                cur = byId.TryGetValue(cur, out var parent) ? Str(parent, "parentId") : null;
            }

            return false;
        }

        string PathOf(JsonNode n)
        {
            var segs = new List<string>();
            var cur = Str(n, "parentId");
            for (var guard = 0; !string.IsNullOrEmpty(cur) && guard <= 64; guard++)
            {
                if (!byId.TryGetValue(cur, out var parent)) { break; }
                segs.Insert(0, Str(parent, "name") ?? string.Empty);
                cur = Str(parent, "parentId");
            }

            return "/" + string.Join('/', segs);
        }

        var hits = new List<SearchHit>();
        foreach (var n in nodes)
        {
            var id = Str(n, "id") ?? string.Empty;
            if (string.Equals(id, trashId, StringComparison.Ordinal) || InTrash(n))
            {
                continue;
            }

            var dir = n["blobId"] is null || Str(n, "nodeType") is "directory" || Str(n, "@type") is "Directory";
            var kind = dir ? "folder" : Str(n, "type") ?? Str(n, "mediaType") ?? string.Empty;
            var name = Str(n, "name") ?? string.Empty;
            if (!MatchesTerms(parsed.Terms, [name, kind]))
            {
                continue;
            }

            hits.Add(new SearchHit(SearchScope.Files, id, name, PathOf(n), Str(n, "modified") ?? Str(n, "created") ?? string.Empty, dir ? id : Str(n, "parentId") ?? string.Empty));
        }

        return hits;
    }
}
