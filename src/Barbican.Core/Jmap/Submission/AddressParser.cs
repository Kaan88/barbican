// Mirrors: Rust jmap.rs:1256 parse_addresses; RFC 5322 §3.4 (address lists, quoted display names and quoted-pairs). Owner jmap.

using System.Text;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Jmap.Submission;

/// <summary>Parses a typed recipient list.</summary>
public static class AddressParser
{
    /// <summary>Splits on commas and semicolons (quotes and angle brackets respected) and reads <c>"Name" &lt;addr&gt;</c> forms; items without an address are dropped.</summary>
    public static IReadOnlyList<EmailAddress> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var output = new List<EmailAddress>();
        foreach (var part in Split(text))
        {
            if (Read(part.Trim()) is { } address)
            {
                output.Add(address);
            }
        }

        return output;
    }

    private static List<string> Split(string text)
    {
        var parts = new List<string>();
        var cur = new StringBuilder();
        var quoted = false;
        var angle = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            switch (ch)
            {
                // a quoted-pair inside a display name ("Doo, \"J\"" <j@d.o>)
                case '\\' when quoted:
                    cur.Append(ch);
                    if (i + 1 < text.Length)
                    {
                        cur.Append(text[++i]);
                    }

                    break;
                case '"':
                    quoted = !quoted;
                    cur.Append(ch);
                    break;
                case '<':
                    angle = true;
                    cur.Append(ch);
                    break;
                case '>':
                    angle = false;
                    cur.Append(ch);
                    break;
                case ',' or ';' when !quoted && !angle:
                    parts.Add(cur.ToString());
                    cur.Clear();
                    break;
                default:
                    cur.Append(ch);
                    break;
            }
        }

        parts.Add(cur.ToString());
        return parts;
    }

    private static EmailAddress? Read(string p)
    {
        if (p.Length == 0)
        {
            return null;
        }

        var a = p.LastIndexOf('<');
        var b = p.LastIndexOf('>');
        if (a >= 0 && b > a)
        {
            var email = p[(a + 1)..b].Trim();
            var raw = p[..a].Trim();
            var name = (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"' ? Unquote(raw[1..^1]) : raw).Trim();
            if (email.Contains('@', StringComparison.Ordinal))
            {
                return new EmailAddress(name.Length == 0 ? null : name, email);
            }
        }

        return p.Contains('@', StringComparison.Ordinal) ? new EmailAddress(null, p.Trim('<', '>')) : null;
    }

    private static string Unquote(string q)
    {
        var sb = new StringBuilder(q.Length);
        for (var i = 0; i < q.Length; i++)
        {
            sb.Append(q[i] == '\\' && i + 1 < q.Length ? q[++i] : q[i]);
        }

        return sb.ToString();
    }
}
