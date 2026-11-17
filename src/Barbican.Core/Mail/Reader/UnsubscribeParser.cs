// Mirrors: Rust reader.rs:65 valid_email, :74 valid_unsub_url, :83 parse_unsubscribe, :100 pct_decode, :117 unsubscribe_mailto; RFC 2369 section 3.2
// (List-Unsubscribe: angle-bracketed URLs, several allowed), RFC 8058 section 3.1 (List-Unsubscribe-Post: List-Unsubscribe=One-Click), RFC 6068
// (mailto percent-decoding: a "+" stays a plus); Bulwark lib/validation.ts:4 isValidEmail, :76 isValidUnsubscribeUrl, :99 parseUnsubscribeUrls,
// :133 parseMailtoUrl, components/email/unsubscribe-banner.tsx (one recipient, one-line subject).

using System.Text;

namespace Barbican.Core.Mail.Reader;

/// <summary>Parses the List-Unsubscribe and List-Unsubscribe-Post headers.</summary>
public static class UnsubscribeParser
{
    private const string Specials = "<>(),;:\"[]\\";

    /// <summary>
    /// The usable targets of a List-Unsubscribe value, or null when there is none (javascript: links, no angle brackets, several or invalid
    /// mailto recipients are refused). <paramref name="postHeader"/> is the List-Unsubscribe-Post value.
    /// </summary>
    public static UnsubscribeLinks? Parse(string listUnsubscribe, string? postHeader)
    {
        ArgumentNullException.ThrowIfNull(listUnsubscribe);
        var urls = new List<string>();
        var rest = listUnsubscribe.AsSpan();
        while (rest.IndexOf('<') is var a and >= 0)
        {
            var b = rest[a..].IndexOf('>');
            if (b < 0)
            {
                break;
            }

            urls.Add(rest[(a + 1)..(a + b)].Trim().ToString());
            rest = rest[(a + b + 1)..];
        }

        var http = urls.FirstOrDefault(u => (u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) && ValidUrl(u));
        var mailto = urls.FirstOrDefault(u => u.StartsWith("mailto:", StringComparison.Ordinal) && ValidUrl(u) && ParseMailto(u) is not null);
        if (http is null && mailto is null)
        {
            return null;
        }

        var oneClick = http is not null
            && http.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && postHeader is not null
            && postHeader.Split(',').Any(x => string.Equals(x.Trim(), "List-Unsubscribe=One-Click", StringComparison.OrdinalIgnoreCase));
        return new UnsubscribeLinks(http, mailto, oneClick);
    }

    /// <summary>The recipient, subject and body of a mailto: link, or null when the address is not valid.</summary>
    public static UnsubscribeMailto? ParseMailto(string mailto)
    {
        ArgumentNullException.ThrowIfNull(mailto);
        if (!mailto.StartsWith("mailto:", StringComparison.Ordinal))
        {
            return null;
        }

        var rest = mailto["mailto:".Length..];
        var q = rest.IndexOf('?', StringComparison.Ordinal);
        var (addr, query) = q >= 0 ? (rest[..q], rest[(q + 1)..]) : (rest, string.Empty);
        var addrs = addr.Split(',').Where(a => a.Trim().Length > 0).ToList();
        if (addrs.Count != 1)
        {
            return null;
        }

        var to = PercentDecode(addrs[0]).Trim();
        if (!ValidEmail(to))
        {
            return null;
        }

        string? subject = null;
        string? body = null;
        foreach (var pair in query.Split('&'))
        {
            var eq = pair.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0)
            {
                continue;
            }

            switch (pair[..eq].ToLowerInvariant())
            {
                case "subject":
                    subject = PercentDecode(pair[(eq + 1)..]);
                    break;
                case "body":
                    body = PercentDecode(pair[(eq + 1)..]);
                    break;
            }
        }

        // a one-line subject of at most 200 characters and a body of at most 500 (counted in characters, as Rust's chars())
        subject = subject is null ? null : Take(string.Join(' ', subject.Split(['\r', '\n']).Where(x => x.Length > 0)).Trim(), 200);
        body = body is null ? null : Take(body, 500);
        return new UnsubscribeMailto(to, string.IsNullOrEmpty(subject) ? null : subject, string.IsNullOrEmpty(body) ? null : body);
    }

    /// <summary>A plain address check: one @, a dotted domain, no spaces.</summary>
    public static bool ValidEmail(string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var s = address.Trim();
        var at = s.IndexOf('@', StringComparison.Ordinal);
        if (at < 0)
        {
            return false;
        }

        var local = s[..at];
        var domain = s[(at + 1)..];
        return local.Length > 0
            && domain.Length > 0
            && !domain.Contains('@', StringComparison.Ordinal)
            && domain.Contains('.', StringComparison.Ordinal)
            && !domain.StartsWith('.')
            && !domain.EndsWith('.')
            && !domain.Contains("..", StringComparison.Ordinal)
            && !s.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || Specials.Contains(c, StringComparison.Ordinal));
    }

    /// <summary>Only http, https and mailto with a valid address (Bulwark isValidUnsubscribeUrl).</summary>
    public static bool ValidUrl(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var u = url.Trim();
        if (u.StartsWith("mailto:", StringComparison.Ordinal))
        {
            return ValidEmail(PercentDecode(u["mailto:".Length..].Split('?')[0]));
        }

        var lower = u.ToLowerInvariant();
        var rest = lower.StartsWith("https://", StringComparison.Ordinal) ? lower["https://".Length..]
            : lower.StartsWith("http://", StringComparison.Ordinal) ? lower["http://".Length..]
            : null;
        if (rest is null)
        {
            return false;
        }

        var host = rest.Split(['/', '?', '#'])[0];
        return host.Length > 0 && !host.Any(char.IsWhiteSpace);
    }

    /// <summary>RFC 6068 percent-decoding (a "+" stays a plus); invalid UTF-8 is replaced.</summary>
    public static string PercentDecode(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var b = Encoding.UTF8.GetBytes(s);
        var output = new List<byte>(b.Length);
        for (var i = 0; i < b.Length; i++)
        {
            if (b[i] == '%' && i + 2 < b.Length && Hex(b[i + 1]) is { } h && Hex(b[i + 2]) is { } l)
            {
                output.Add((byte)((h * 16) + l));
                i += 2;
                continue;
            }

            output.Add(b[i]);
        }

        return Encoding.UTF8.GetString([.. output]);
    }

    private static int? Hex(byte c) => c switch
    {
        >= (byte)'0' and <= (byte)'9' => c - '0',
        >= (byte)'a' and <= (byte)'f' => c - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => c - 'A' + 10,
        _ => null,
    };

    private static string Take(string s, int max)
    {
        var runes = s.EnumerateRunes().Take(max);
        var sb = new StringBuilder();
        foreach (var r in runes)
        {
            sb.Append(r.ToString());
        }

        return sb.ToString();
    }
}
