// Mirrors: Rust compose.rs:48 is_blank, :191 delivery_addresses, :729 find_placeholders (+ is_fill_in :779); Bulwark lib/placeholder-check.ts findUnfilledPlaceholders, lib/email-composer-utils.ts deliveryAddresses; Bulwark components/email/email-composer.tsx.

using System.Text;
using Barbican.Core.Jmap.Submission;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Compose;

/// <summary>The composer's pure rules.</summary>
public static class ComposeRules
{
    private static readonly string[] DeliveryHeaderNames = ["delivered-to", "x-original-to", "x-delivered-to", "x-original-delivered-to"];
    private static readonly string[] Markers = ["TODO", "TBD", "TBA", "TBC", "FIXME", "XXX"];
    private static readonly char[] AddressSeparators = [' ', '\t', '\r', '\n', ',', ';', '"', '\''];
    private static readonly string[] PromptStarts = ["your ", "insert ", "enter ", "add "];

    /// <summary>Nothing worth keeping: no recipients, subject or attachments, and a body that is empty apart from the signature Barbican put there (and dashes).</summary>
    public static bool IsBlank(IReadOnlyList<string> fields, string body, IReadOnlyList<string> signatures, int attachments)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(signatures);
        if (attachments > 0 || fields.Any(f => f.Trim().Length > 0))
        {
            return false;
        }

        var rest = body;
        foreach (var s in signatures.Select(s => s.Trim()).Where(s => s.Length > 0))
        {
            rest = rest.Replace(s, "", StringComparison.Ordinal);
        }

        return rest.All(c => char.IsWhiteSpace(c) || c == '-');
    }

    /// <summary>Addresses in Delivered-To, X-Original-To, X-Delivered-To, X-Original-Delivered-To in that header priority, the address inside <c>&lt;&gt;</c> or the first token with an @, de-duplicated case-insensitively.</summary>
    public static IReadOnlyList<string> DeliveryAddresses(IReadOnlyList<EmailHeader> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        var output = new List<string>();
        foreach (var name in DeliveryHeaderNames)
        {
            foreach (var h in headers.Where(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                var v = h.Value ?? "";
                string? addr = null;
                var lt = v.IndexOf('<', StringComparison.Ordinal);
                if (lt >= 0)
                {
                    var gt = v.IndexOf('>', lt + 1);
                    if (gt >= 0)
                    {
                        addr = v[(lt + 1)..gt];
                    }
                }

                addr ??= v.Split(AddressSeparators).FirstOrDefault(t => t.Contains('@', StringComparison.Ordinal));
                var a = addr?.Trim().TrimEnd('.');
                if (a is { Length: > 0 } && a.Contains('@', StringComparison.Ordinal) && !output.Any(x => string.Equals(x, a, StringComparison.OrdinalIgnoreCase)))
                {
                    output.Add(a);
                }
            }
        }

        return output;
    }

    /// <summary>The entries of a recipient field that are no address (no @ in a bare entry): the safety net before a send, as Rust's invalid address check. Entries are split on commas and semicolons outside quotes and angle brackets.</summary>
    public static IReadOnlyList<string> InvalidAddresses(string field)
    {
        ArgumentNullException.ThrowIfNull(field);
        var bad = new List<string>();
        foreach (var part in SplitEntries(field))
        {
            if (AddressParser.Parse(part).Count == 0)
            {
                bad.Add(part);
            }
        }

        return bad;
    }

    private static List<string> SplitEntries(string field)
    {
        var parts = new List<string>();
        var cur = new StringBuilder();
        var quoted = false;
        var angle = false;
        foreach (var ch in field)
        {
            switch (ch)
            {
                case '"':
                    quoted = !quoted;
                    cur.Append(ch);
                    break;
                case '<' when !quoted:
                    angle = true;
                    cur.Append(ch);
                    break;
                case '>' when !quoted:
                    angle = false;
                    cur.Append(ch);
                    break;
                case ',' or ';' when !quoted && !angle:
                    parts.Add(cur.ToString().Trim());
                    cur.Clear();
                    break;
                default:
                    cur.Append(ch);
                    break;
            }
        }

        parts.Add(cur.ToString().Trim());
        return [.. parts.Where(p => p.Length > 0)];
    }

    /// <summary>Unfilled placeholders: template fields ({{ name }}, also in the subject), bracketed fill-ins ([TIME], [Your Name], [insert date here]), TODO-style markers, circle stand-ins; links and ordinary brackets are text.</summary>
    public static IReadOnlyList<string> FindPlaceholders(string subject, string body)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(body);
        var found = new List<string>();
        void Add(string s)
        {
            if (!found.Contains(s, StringComparer.Ordinal))
            {
                found.Add(s);
            }
        }

        Templates(subject, Add);
        Templates(body, Add);

        Brackets(body, Add);

        foreach (var w in SplitWords(body))
        {
            if (Markers.Contains(w, StringComparer.Ordinal))
            {
                Add(w);
            }
        }

        Circles(body, Add);

        return found;
    }

    private static void Brackets(string body, Action<string> add)
    {
        // brackets: plain, full-width and lenticular (UTF-16 indices; the bracket characters are all BMP)
        var k = 0;
        while (k < body.Length)
        {
            var c = body[k];
            if (c is '[' or '［' or '【')
            {
                var limit = Math.Min(body.Length, k + 45);
                var e = -1;
                for (var i = k + 1; i < limit; i++)
                {
                    if (body[i] is ']' or '］' or '】' or '[' or '［' or '【' or '\n')
                    {
                        e = i;
                        break;
                    }
                }

                if (e >= 0 && body[e] is ']' or '］' or '】')
                {
                    var end = e + 1;
                    var inner = body[(k + 1)..e];
                    var link = end < body.Length && body[end] == '(';
                    if (!link && IsFillIn(inner, c == '['))
                    {
                        add(body[k..end]);
                        k = e + 1;
                        continue;
                    }
                }
            }

            k++;
        }
    }

    private static void Circles(string body, Action<string> add)
    {
        var run = new StringBuilder();
        foreach (var ch in body.Append(' '))
        {
            if (ch is '〇' or '○' or '◯')
            {
                run.Append(ch);
            }
            else
            {
                if (run.Length >= 2)
                {
                    add(run.ToString());
                }

                run.Clear();
            }
        }
    }

    private static IEnumerable<string> SplitWords(string s)
    {
        var start = 0;
        for (var i = 0; i <= s.Length; i++)
        {
            if (i == s.Length || !(char.IsLetterOrDigit(s[i]) || s[i] == '_'))
            {
                if (i > start)
                {
                    yield return s[start..i];
                }

                start = i + 1;
            }
        }
    }

    private static void Templates(string t, Action<string> add)
    {
        var rest = t;
        while (true)
        {
            var i = rest.IndexOf("{{", StringComparison.Ordinal);
            if (i < 0)
            {
                break;
            }

            var after = rest[(i + 2)..];
            var j = after.IndexOf("}}", StringComparison.Ordinal);
            if (j < 0)
            {
                break;
            }

            var inner = after[..j];
            var colon = inner.IndexOf(':', StringComparison.Ordinal);
            var name = (colon >= 0 ? inner[..colon] : inner).Trim();
            if (!inner.Contains('\n', StringComparison.Ordinal) && !inner.Contains('{', StringComparison.Ordinal) && name.Length > 0 && name.All(c => char.IsLetterOrDigit(c) || c is '_' or '.' or '-'))
            {
                add(rest.Substring(i, 2 + j + 2));
            }

            rest = after[(j + 2)..];
        }
    }

    /// <summary>[CAPS], [Title Words] (1-3 capitalised words) or a prompt like [your name] / [insert date].</summary>
    private static bool IsFillIn(string inner, bool asciiBracket)
    {
        var n = inner.EnumerateRunes().Count();
        var caps = asciiBracket && n is >= 2 and <= 30
            && inner.Length > 0 && (char.IsUpper(inner[0]) || char.IsAsciiDigit(inner[0]))
            && (char.IsUpper(inner[^1]) || char.IsAsciiDigit(inner[^1]))
            && inner.All(c => char.IsUpper(c) || char.IsAsciiDigit(c) || c is ' ' or '_' or '.' or '/' or '-')
            && inner.Count(char.IsUpper) >= 2;
        var words = inner.Split(' ');
        var title = asciiBracket && words.Length is >= 1 and <= 3 && words.All(w => w.Length >= 2 && char.IsUpper(w[0]) && w.Skip(1).All(c => char.IsLower(c) || c is '\'' or '’' or '-'));
        var lower = inner.ToLowerInvariant();
        var prompt = n <= 46 && PromptStarts.Any(p => lower.StartsWith(p, StringComparison.Ordinal) && lower.Length > p.Length);
        return caps || title || prompt;
    }
}
