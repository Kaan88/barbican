// Mirrors: Rust auth.rs (parse :34, topmost :55, domain_of :60, aligned :66, aligned_dkim_pass :72, aligned_pass :79, failure :90);
// RFC 8601; DMARC relaxed alignment (RFC 7489 §3.1); Bulwark lib/email-headers.ts. Owner mailcore.
// Fable review 1 F-07 / D-9: the reader takes the topmost header whose authserv-id the account's server owns (Trusted, AuthServTrust), never
// simply the topmost one; a message with no such header has no chips and no verdict.

using System.Text;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.AuthResults;

/// <summary>Reads the receiving server's Authentication-Results; only a pass that aligns with the From domain says anything.</summary>
public static class AuthenticationResults
{
    /// <summary>Parses one Authentication-Results value (the authserv-id is skipped, comments stripped).</summary>
    public static IReadOnlyList<AuthResult> Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var clean = StripComments(value);
        var output = new List<AuthResult>();
        foreach (var segment in clean.Split(';'))
        {
            var tokens = segment.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                continue;
            }

            var eq = tokens[0].IndexOf('=', StringComparison.Ordinal);
            if (eq < 0)
            {
                continue;
            }

            var method = tokens[0][..eq].ToLowerInvariant();
            var result = tokens[0][(eq + 1)..].ToLowerInvariant();
            var props = new List<KeyValuePair<string, string>>();
            foreach (var token in tokens.Skip(1))
            {
                var i = token.IndexOf('=', StringComparison.Ordinal);
                if (i >= 0)
                {
                    props.Add(new KeyValuePair<string, string>(token[..i].ToLowerInvariant(), token[(i + 1)..].Trim('"')));
                }
            }

            output.Add(new AuthResult(method, result, props));
        }

        return output;
    }

    /// <summary>The authserv-id of one Authentication-Results value (RFC 8601 §2.2: the first token, before any version and the first ';').</summary>
    public static string? AuthServId(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var head = StripComments(Unfold(value)).Split(';')[0].Trim();
        var id = head.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrEmpty(id) || id.Contains('=', StringComparison.Ordinal) ? null : id.Trim('"');
    }

    /// <summary>
    /// The topmost Authentication-Results whose authserv-id <paramref name="trusted"/> accepts (RFC 8601 §5), or nothing: a header from any
    /// other id is the sender's word, not the server's.
    /// </summary>
    public static IReadOnlyList<AuthResult> Trusted(IReadOnlyList<EmailHeader> headers, Func<string, bool> trusted)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(trusted);
        foreach (var h in headers)
        {
            if (string.Equals(h.Name, "Authentication-Results", StringComparison.OrdinalIgnoreCase) && AuthServId(h.Value) is { } id && trusted(id))
            {
                return Parse(Unfold(h.Value));
            }
        }

        return [];
    }

    /// <summary>The topmost Authentication-Results of a header list, whatever its authserv-id (Rust; the reader uses <see cref="Trusted"/>).</summary>
    public static IReadOnlyList<AuthResult> Topmost(IReadOnlyList<EmailHeader> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        var first = headers.FirstOrDefault(h => string.Equals(h.Name, "Authentication-Results", StringComparison.OrdinalIgnoreCase));
        return first is null ? [] : Parse(Unfold(first.Value));
    }

    /// <summary>The domain part of an address, lower-case, without a trailing dot; null if it has no dot.</summary>
    public static string? DomainOf(string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var at = address.LastIndexOf('@');
        var d = (at < 0 ? address : address[(at + 1)..]).Trim().TrimEnd('>').TrimEnd('.').ToLowerInvariant();
        var ok = d.Contains('.', StringComparison.Ordinal)
            && !d.StartsWith('.')
            && !d.Any(char.IsWhiteSpace)
            && d.IndexOfAny(['<', '>', '@']) < 0;
        return ok ? d : null;
    }

    /// <summary>DMARC relaxed alignment: the same domain or a parent/child of it.</summary>
    public static bool Aligned(string fromDomain, string? candidate)
    {
        ArgumentNullException.ThrowIfNull(fromDomain);
        if (candidate is null || DomainOf("@" + candidate) is not { } o)
        {
            return false;
        }

        return string.Equals(o, fromDomain, StringComparison.Ordinal)
            || o.EndsWith("." + fromDomain, StringComparison.Ordinal)
            || fromDomain.EndsWith("." + o, StringComparison.Ordinal);
    }

    /// <summary>A DKIM pass whose signing domain aligns with the From domain.</summary>
    public static bool AlignedDkimPass(IReadOnlyList<AuthResult> results, string fromDomain)
    {
        ArgumentNullException.ThrowIfNull(results);
        return results.Any(r => Is(r, "dkim", "pass") && Aligned(fromDomain, r.Prop("header.d")));
    }

    /// <summary>Whether the checks tie the message to its From address: dmarc=pass, or an aligned SPF (MAIL FROM, never HELO) or DKIM pass.</summary>
    public static bool AlignedPass(IReadOnlyList<AuthResult> results, string from)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(from);
        if (DomainOf(from) is not { } d)
        {
            return false;
        }

        return results.Any(r => Is(r, "dmarc", "pass"))
            || results.Any(r => Is(r, "spf", "pass") && r.Prop("smtp.mailfrom") is { } m && Aligned(d, DomainOf(m) ?? string.Empty))
            || AlignedDkimPass(results, d);
    }

    /// <summary>A failure that leaves the From domain unauthenticated.</summary>
    public static bool Failure(IReadOnlyList<AuthResult> results, bool verified)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (results.Any(r => Is(r, "dmarc", "fail")))
        {
            return true;
        }

        if (verified)
        {
            return false;
        }

        return results.Any(r =>
            (string.Equals(r.Method, "dkim", StringComparison.Ordinal) && r.Result is "fail" or "policy" or "permerror")
            || (string.Equals(r.Method, "spf", StringComparison.Ordinal) && r.Result is "fail" or "softfail" or "permerror"));
    }

    private static bool Is(AuthResult r, string method, string result) =>
        string.Equals(r.Method, method, StringComparison.Ordinal) && string.Equals(r.Result, result, StringComparison.Ordinal);

    private static string StripComments(string s)
    {
        var sb = new StringBuilder();
        var depth = 0;
        foreach (var c in s)
        {
            if (c == '(')
            {
                depth++;
            }
            else if (c == ')' && depth > 0)
            {
                depth--;
            }
            else if (depth == 0)
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static string Unfold(string v)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < v.Length; i++)
        {
            var c = v[i];
            if (c == '\r')
            {
                continue;
            }

            if (c == '\n')
            {
                while (i + 1 < v.Length && v[i + 1] is ' ' or '\t')
                {
                    i++;
                }

                sb.Append(' ');
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
