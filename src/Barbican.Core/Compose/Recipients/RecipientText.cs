// Mirrors: Rust recipients.rs / chips.rs (tag rust-final); Bulwark lib/email-composer-utils.ts, lib/rfc5322-mailbox.ts, lib/idn.ts, lib/validation.ts, stores/contact-store.ts; RFC 5322 section 3.4 (mailbox, group), RFC 3490/5891 (IDNA). Port verified against recipients.rs (M3 composer-ui).

using System.Globalization;
using System.Text;

namespace Barbican.Core.Compose.Recipients;

/// <summary>The recipient fields' pure text rules (port of Rust <c>recipients.rs</c>). Strings are UTF-16; the Rust byte indexes become char indexes, which is equivalent for every rule here because only ASCII delimiters are searched.</summary>
public static class RecipientText
{
    private const int MaxSuggestions = 10;

    private static readonly IdnMapping Idn = new() { UseStd3AsciiRules = false, AllowUnassigned = false };

    private static readonly char[] TokenSeparators = [';', ' ', '\t', '\r', '\n'];

    /// <summary>A domain in punycode (bücher.example becomes xn--bcher-kva.example); null when it is not a valid domain (a space, an empty label).</summary>
    public static string? ToAsciiDomain(string domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var d = domain.Trim();
        if (d.Length == 0 || d.Any(c => char.IsWhiteSpace(c) || "/\\?#@:%[]".Contains(c, StringComparison.Ordinal)))
        {
            return null;
        }

        try
        {
            var a = Idn.GetAscii(d).ToLowerInvariant();
            return a.Length == 0 ? null : a;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The address with its domain in ASCII; the local part keeps its case, the domain is left as typed when it is already ASCII.</summary>
    public static string ToAsciiEmail(string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var at = address.LastIndexOf('@');
        if (at <= 0)
        {
            return address;
        }

        var domain = address[(at + 1)..];
        if (domain.All(char.IsAscii))
        {
            return address;
        }

        return ToAsciiDomain(domain) is { } a ? address[..at] + "@" + a : address;
    }

    /// <summary>A punycode domain shown in Unicode only when it decodes back to the same ASCII form (xn--zz-zz.de stays).</summary>
    public static string ToUnicodeDomain(string domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var lower = domain.ToLowerInvariant();
        if (!lower.Split('.').Any(l => l.StartsWith("xn--", StringComparison.Ordinal)))
        {
            return domain;
        }

        try
        {
            var uni = Idn.GetUnicode(lower);
            return string.Equals(ToAsciiDomain(uni), lower, StringComparison.Ordinal) ? uni : domain;
        }
        catch (ArgumentException)
        {
            return domain;
        }
    }

    /// <summary>The address with its domain in Unicode (case-insensitive xn-- prefix).</summary>
    public static string ToUnicodeEmail(string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var at = address.LastIndexOf('@');
        if (at <= 0)
        {
            return address;
        }

        var domain = address[(at + 1)..];
        var uni = ToUnicodeDomain(domain);
        return string.Equals(uni, domain, StringComparison.Ordinal) ? address : address[..at] + "@" + uni;
    }

    /// <summary>Plus-tags and internationalised domains are valid; empty, no @, empty or dashed labels, a trailing dot, whitespace, control characters, a second @ or &lt; and a local part over 64 characters are not.</summary>
    public static bool IsValidEmail(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        var e = ToAsciiEmail(email);
        if (e.Length is 0 or > 254 || e.Any(c => c is '\r' or '\n' or '\0' or '<' or '>'))
        {
            return false;
        }

        var at = e.IndexOf('@', StringComparison.Ordinal);
        if (at < 0)
        {
            return false;
        }

        var local = e[..at];
        var domain = e[(at + 1)..];
        if (domain.Contains('@', StringComparison.Ordinal))
        {
            return false;
        }

        const string Atext = ".!#$%&'*+/=?^_`{|}~-";
        if (local.Length is 0 or > 64 || !local.All(c => char.IsAsciiLetterOrDigit(c) || Atext.Contains(c, StringComparison.Ordinal)))
        {
            return false;
        }

        return domain.Length <= 255 && !domain.StartsWith('.') && !domain.EndsWith('.') && !domain.Contains("..", StringComparison.Ordinal) && domain.Split('.').All(LabelOk);
    }

    /// <summary>A display name without its surrounding quotes and quoted-pairs.</summary>
    public static string UnquoteName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var t = name.Trim();
        if (t.Length >= 2 && t[0] == '"' && t[^1] == '"')
        {
            var inner = t[1..^1];
            var sb = new StringBuilder();
            for (var i = 0; i < inner.Length; i++)
            {
                if (inner[i] == '\\' && i + 1 < inner.Length && inner[i + 1] is '"' or '\\')
                {
                    i++;
                }

                sb.Append(inner[i]);
            }

            return sb.ToString().Trim();
        }

        return t;
    }

    /// <summary>A display name cleaned for the chip (quotes removed, an address it carries cut off, control characters dropped).</summary>
    public static string SanitizeDisplayName(string name)
    {
        var n = UnquoteName(name);
        var i = n.LastIndexOf('<');
        var cut = n;
        if (i >= 0 && !n[(i + 1)..].TrimEnd().TrimEnd('>').Contains('>', StringComparison.Ordinal))
        {
            cut = n[..i].TrimEnd();
        }

        var r = UnquoteName(cut);
        return r.Any(char.IsControl) ? new string([.. r.Where(c => !char.IsControl(c))]) : r;
    }

    /// <summary>Name and address of a mailbox string, with or without a space before the angle bracket, an unclosed bracket tolerated, a quoted name that contains an address reduced to the name.</summary>
    public static (string Name, string Email) SplitMailbox(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var t = value.Trim();
        var email = ExtractAddrSpec(t);
        if (email.Length == 0)
        {
            return ("", t);
        }

        var at = Math.Max(0, t.LastIndexOf(email, StringComparison.Ordinal));
        var src = t[..at].TrimEnd();
        if (src.EndsWith('<'))
        {
            src = src[..^1];
        }

        var name = SanitizeDisplayName(src);
        return name.Length > 0 && !string.Equals(name, email, StringComparison.Ordinal) ? (name, email) : ("", email);
    }

    /// <summary>Splits on the separators honouring quoted names, angle brackets and group syntax (Team: a@x.example, b@y.example;); a colon no semicolon closes is not a group.</summary>
    public static IReadOnlyList<string> SplitRecipients(string value, string separators)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(separators);
        var (list, open) = SplitPass(value, separators, int.MaxValue);
        return open is null ? list : SplitPass(value, separators, open.Value).List;
    }

    /// <summary>One entry as a Recipient: a mailbox, or a group with its members (a quoted group name may contain commas).</summary>
    public static Recipient ParseRecipient(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var t = text.Trim();
        if (t.EndsWith(';'))
        {
            var colon = TopLevelColon(t);
            if (colon >= 0)
            {
                var members = SplitRecipients(t[(colon + 1)..^1], ",").Select(ParseRecipient).Where(m => m.Email.Length > 0 && m.Group is null).ToList();
                if (members.Any(m => IsValidEmail(m.Email)))
                {
                    return new Recipient { Name = UnquoteName(t[..colon]), Email = "", Group = members };
                }
            }
        }

        if (t.EndsWith('>'))
        {
            var open = t.IndexOf('<', StringComparison.Ordinal);
            if (open >= 0)
            {
                var inner = t[(open + 1)..^1];
                var name = t[..open].TrimEnd();
                if (name.Length > 0 && inner.Length > 0 && !inner.Contains('>', StringComparison.Ordinal))
                {
                    return Recipient.Addr(UnquoteName(name), inner.Trim());
                }
            }
        }

        return Recipient.Addr("", t);
    }

    /// <summary>A field text as recipients (split, then parse).</summary>
    public static IReadOnlyList<Recipient> ParseList(string value) => [.. SplitRecipients(value, ",").Select(ParseRecipient)];

    /// <summary>Bulwark formatRecipient: the bare address when there is no distinct name, else Name &lt;email&gt;, quoting (and escaping) the name only when it holds one of , &lt; &gt; " @ ; :.</summary>
    public static string FormatRecipient(string name, string email)
    {
        ArgumentNullException.ThrowIfNull(name);
        var n = name.Trim();
        return n.Length == 0 || string.Equals(n, email, StringComparison.Ordinal) ? email : $"{QuoteName(n)} <{email}>";
    }

    /// <summary>One entry as field text, groups as RFC 5322 group syntax.</summary>
    public static string FormatEntry(Recipient recipient)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        if (recipient.Group is { } m)
        {
            var name = recipient.Name.Trim().Length == 0 ? "Group" : recipient.Name.Trim();
            return $"{QuoteName(name)}: {string.Join(", ", m.Select(x => FormatRecipient(x.Name, x.Email)))};";
        }

        return FormatRecipient(recipient.Name, recipient.Email);
    }

    /// <summary>The field text of a list; parsing it again gives the same list.</summary>
    public static string FormatList(IReadOnlyList<Recipient> list)
    {
        ArgumentNullException.ThrowIfNull(list);
        return string.Join(", ", list.Select(FormatEntry));
    }

    /// <summary>Groups replaced by their members, de-duplicated by address (case-insensitive).</summary>
    public static IReadOnlyList<Recipient> Expand(IReadOnlyList<Recipient> list)
    {
        ArgumentNullException.ThrowIfNull(list);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var output = new List<Recipient>();
        foreach (var r in list)
        {
            foreach (var e in r.Group ?? [r])
            {
                var k = e.Email.Trim().ToLowerInvariant();
                if (k.Length > 0 && seen.Add(k))
                {
                    output.Add(Recipient.Addr(e.Name, e.Email.Trim()));
                }
            }
        }

        return output;
    }

    /// <summary>The field text as it goes into the message: groups expanded, domains in ASCII.</summary>
    public static string ExpandForSend(string value) =>
        string.Join(", ", Expand(ParseList(value)).Select(r => FormatRecipient(r.Name, ToAsciiEmail(r.Email))));

    /// <summary>The addresses that do not validate (reported, never dropped).</summary>
    public static IReadOnlyList<string> InvalidAddresses(IReadOnlyList<Recipient> list)
    {
        ArgumentNullException.ThrowIfNull(list);
        return [.. list.SelectMany(r => r.Group ?? [r]).Where(r => !IsValidEmail(r.Email)).Select(r => r.Email)];
    }

    /// <summary>A pasted list split on whitespace, commas, semicolons and newlines into valid new recipients (not in <c>existing</c>, de-duplicated case-insensitively) and the invalid leftovers; a quoted name with a comma stays one name; an unclosed angle bracket is repaired.</summary>
    public static (IReadOnlyList<Recipient> Recipients, IReadOnlyList<string> Invalid) SplitPasted(string text, IReadOnlyList<string> existing)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(existing);
        var seen = new HashSet<string>(existing.Select(e => e.ToLowerInvariant()), StringComparer.Ordinal);
        var valid = new List<Recipient>();
        var invalid = new List<string>();

        bool TryAdd(Recipient r)
        {
            if (!IsValidEmail(r.Email))
            {
                return false;
            }

            if (seen.Add(r.Email.ToLowerInvariant()))
            {
                valid.Add(Recipient.Addr(r.Name, r.Email));
            }

            return true;
        }

        foreach (var entry in SplitRecipients(text, ",;\n\r"))
        {
            PasteEntry(entry, TryAdd, invalid);
        }

        return (valid, invalid);
    }

    private static bool CarriesAddress(string v) =>
        v.Split(TokenSeparators).Any(t => IsValidEmail(t.Trim().TrimStart('<').TrimEnd('>')));

    private static void PasteEntry(string entry, Func<Recipient, bool> tryAdd, List<string> invalid)
    {
        var p = ParseRecipient(entry);
        if (p.Group is null && tryAdd(p))
        {
            return;
        }

        var un = UnquoteName(entry);
        if (!string.Equals(un, entry, StringComparison.Ordinal) && tryAdd(ParseRecipient(un)))
        {
            return;
        }

        var lt = entry.LastIndexOf('<');
        if (lt >= 0 && !CarriesAddress(entry[..lt]))
        {
            var (n, e) = SplitMailbox(entry);
            if (tryAdd(Recipient.Addr(n, e)))
            {
                return;
            }
        }

        foreach (var tok in entry.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Where(t => t.Length > 0))
        {
            if (!tryAdd(Recipient.Addr("", tok.TrimStart('<').TrimEnd('>'))))
            {
                invalid.Add(tok);
            }
        }
    }

    /// <summary>The suggestion Enter or Tab takes before the arrows moved: -1 for blank input or no suggestions; for a complete valid typed address its own suggestion (case-insensitive) or -1 (add as typed); for anything else the top one (0).</summary>
    public static int DefaultSuggestionIndex(string input, IReadOnlyList<Suggestion> suggestions)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(suggestions);
        var typed = input.Trim();
        if (typed.Length == 0 || suggestions.Count == 0)
        {
            return -1;
        }

        var email = ParseRecipient(typed).Email;
        if (IsValidEmail(email))
        {
            for (var i = 0; i < suggestions.Count; i++)
            {
                if (string.Equals(suggestions[i].Email, email, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        return 0;
    }

    /// <summary>0 when a word of any text starts with the query (case-insensitive, accents kept), 1 when a text only contains it, else -1.</summary>
    public static int MatchRank(string query, IReadOnlyList<string> texts)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(texts);
        var q = query.ToLowerInvariant();
        var rank = -1;
        foreach (var t in texts)
        {
            var lower = t.ToLowerInvariant();
            var from = 0;
            while (from <= lower.Length)
            {
                var at = lower.IndexOf(q, from, StringComparison.Ordinal);
                if (at < 0)
                {
                    break;
                }

                if (at == 0 || !char.IsLetterOrDigit(lower[at - 1]))
                {
                    return 0;
                }

                rank = 1;
                from = at + 1;
            }
        }

        return rank;
    }

    /// <summary>Bulwark getAutocomplete: groups and contacts, then recent recipients not already suggested; stable-sorted by rank; cleaned names; 10 at most; a blank query gives none.</summary>
    public static IReadOnlyList<Suggestion> Suggest(string query, SuggestionSources sources)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(sources);
        var q = query.Trim().ToLowerInvariant();
        if (q.Length == 0)
        {
            return [];
        }

        var hits = new List<(int Rank, Suggestion S)>();
        foreach (var (id, name, members) in sources.Groups)
        {
            var r = MatchRank(q, [name]);
            if (r >= 0 && members.Count > 0)
            {
                hits.Add((r, new Suggestion(name, "", new RecipientGroup(id, members))));
            }
        }

        foreach (var (name, email) in sources.Contacts)
        {
            var r = MatchRank(q, [name, email]);
            if (r >= 0)
            {
                hits.Add((r, new Suggestion(name, email)));
            }
        }

        var seen = new HashSet<string>(hits.Select(h => h.S.Email.ToLowerInvariant()), StringComparer.Ordinal);
        foreach (var (name, email) in sources.Recent)
        {
            var k = email.ToLowerInvariant();
            if (seen.Contains(k))
            {
                continue;
            }

            var r = MatchRank(q, [k, name]);
            if (r >= 0)
            {
                seen.Add(k);
                hits.Add((r, new Suggestion(name, email)));
            }
        }

        return [.. Normalize(hits.OrderBy(h => h.Rank).Select(h => h.S)).Take(MaxSuggestions)];
    }

    /// <summary>Two letters for the avatar: first letters of the first two name words, else of the first two parts of the address's local part.</summary>
    public static string Initials(string name, string email)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(email);
        var src = name.Trim().Length == 0 ? email.Split('@')[0] : name;
        var parts = src.Split([' ', '\t', '\r', '\n', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
        var s = parts.Length switch
        {
            0 => "?",
            1 => parts[0].Length > 2 ? parts[0][..2] : parts[0],
            _ => string.Concat(parts.Take(2).Select(p => p[0])),
        };
        return s.ToUpperInvariant();
    }

    private static bool LabelOk(string l) =>
        l.Length is > 0 and <= 63 && char.IsAsciiLetterOrDigit(l[0]) && char.IsAsciiLetterOrDigit(l[^1]) && l.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    private static string ExtractAddrSpec(string v)
    {
        var t = v.TrimEnd();
        var i = t.LastIndexOf('<');
        if (i >= 0)
        {
            var rest = t[(i + 1)..];
            var inner = rest.EndsWith('>') ? rest[..^1] : rest;
            if (inner.IndexOfAny(['<', '>']) < 0)
            {
                return inner.Trim();
            }
        }

        return v.Contains('@', StringComparison.Ordinal) ? v : "";
    }

    private static bool AngleRunCloses(string v, int from)
    {
        var quoted = false;
        for (var i = from + 1; i < v.Length; i++)
        {
            var c = v[i];
            if (quoted && c == '\\')
            {
                i++;
            }
            else if (c == '"')
            {
                quoted = !quoted;
            }
            else if (quoted)
            {
                continue;
            }
            else if (c == '>')
            {
                return true;
            }
            else if (c == '<')
            {
                return false;
            }
        }

        return false;
    }

#pragma warning disable MA0051 // one state machine, ported as it is in recipients.rs split_pass
    private static (List<string> List, int? OpenGroup) SplitPass(string v, string seps, int noGroupFrom)
    {
        var output = new List<string>();
        var groupAt = 0;
        var cur = new StringBuilder();
        bool quoted = false, angle = false, group = false;

        void Push()
        {
            var t = cur.ToString().Trim();
            if (t.Length > 0)
            {
                output.Add(t);
            }

            cur.Clear();
        }

        for (var i = 0; i < v.Length; i++)
        {
            var ch = v[i];
            if (quoted && ch == '\\' && i + 1 < v.Length)
            {
                cur.Append(ch).Append(v[i + 1]);
                i++;
                continue;
            }

            if (ch == '"')
            {
                quoted = !quoted;
                cur.Append(ch);
            }
            else if (ch == '<' && !quoted)
            {
                angle = true;
                cur.Append(ch);
            }
            else if (ch == '>' && !quoted)
            {
                angle = false;
                cur.Append(ch);
            }
            else if (seps.Contains(ch, StringComparison.Ordinal) && angle && !quoted && !group && !AngleRunCloses(v, i))
            {
                angle = false;
                Push();
            }
            else if (ch == ':' && !quoted && !angle && !group && i < noGroupFrom)
            {
                group = true;
                groupAt = i;
                cur.Append(ch);
            }
            else if (ch == ';' && group && !quoted && !angle)
            {
                group = false;
                cur.Append(ch);
            }
            else if (seps.Contains(ch, StringComparison.Ordinal) && !quoted && !angle && !group)
            {
                Push();
            }
            else
            {
                cur.Append(ch);
            }
        }

        if (group)
        {
            return (output, groupAt);
        }

        Push();
        return (output, null);
    }

#pragma warning restore MA0051

    private static int TopLevelColon(string v)
    {
        bool quoted = false, angle = false;
        for (var i = 0; i < v.Length; i++)
        {
            var c = v[i];
            if (quoted && c == '\\')
            {
                i++;
            }
            else if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == '<' && !quoted)
            {
                angle = true;
            }
            else if (c == '>' && !quoted)
            {
                angle = false;
            }
            else if (c == ':' && !quoted && !angle)
            {
                return i;
            }
        }

        return -1;
    }

    private static string QuoteName(string n) =>
        n.IndexOfAny([',', '<', '>', '"', '@', ';', ':']) >= 0 ? "\"" + n.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : n;

    private static List<Suggestion> Normalize(IEnumerable<Suggestion> list)
    {
        var output = new List<Suggestion>();
        foreach (var s in list)
        {
            if (s.Group is not null)
            {
                output.Add(s);
                continue;
            }

            var (mname, email) = SplitMailbox(s.Email);
            if (email.Length == 0)
            {
                continue;
            }

            var name = SanitizeDisplayName(s.Name);
            if (name.Length == 0)
            {
                name = mname;
            }

            if (string.Equals(name, email, StringComparison.Ordinal))
            {
                name = "";
            }

            var idx = output.FindIndex(o => o.Group is null && string.Equals(o.Email, email, StringComparison.OrdinalIgnoreCase));
            if (idx < 0)
            {
                output.Add(new Suggestion(name, email));
            }
            else if (output[idx].Name.Length == 0 && name.Length > 0)
            {
                output[idx] = output[idx] with { Name = name };
            }
        }

        return output;
    }
}
