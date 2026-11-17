// Mirrors: Fable review 2 R2-07 (M2, owner reader) on top of D-9 / F-07 (AuthServTrust): RFC 8601 §5 (a consumer trusts only results written by
// its own border, and a border must remove forged ones) and §1.6; RFC 5321 §4.4 (each hop prepends a Received trace header);
// Stalwart crates/smtp/src/inbound/data.rs:322 (AuthenticationResults::new(&self.hostname)) and :482-501 (write_received, then
// auth_results.write_header: the server's Received comes first and its Authentication-Results right below it, above the message's own
// headers). Stalwart removes no inbound header that carries its id, so a forged "mx.<domain>; dmarc=pass" inside the message counts unless the
// client checks where the header sits.
// The rule here: an Authentication-Results counts only inside the server's own trace block, which is the part below the topmost Received of
// the message (whose "by" host must be the authserv-id) and above the next Received. The authserv-id is learned the first time such a block
// is found under a host the account trusts (AuthServTrust: the JMAP host or its parent domain) and pinned per account (AuthServPins);
// afterwards only the pinned id counts. A message without a server trace (imported mail, a server with the header turned off) has no
// results. Deviation from the literal R2-07 wording ("the topmost Received whose by host equals the authserv-id"): the block must start at the
// very first Received of the message, so a forged "Received: by <server>" lower down never opens a block.

using System.Text;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.AuthResults;

/// <summary>Finds the Authentication-Results the account's own server wrote (R2-07).</summary>
public static class AuthTrace
{
    /// <summary>
    /// The results of the server's own trace block, or nothing. <paramref name="pinned"/> is the account's pinned authserv-id (null before one
    /// is learned, then <paramref name="trusts"/> decides); <paramref name="learned"/> is the id the block was found under.
    /// </summary>
    public static IReadOnlyList<AuthResult> ServerResults(IReadOnlyList<EmailHeader> headers, string? pinned, Func<string, bool> trusts, out string? learned)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(trusts);
        learned = null;
        var top = -1;
        for (var i = 0; i < headers.Count; i++)
        {
            if (IsReceived(headers[i]))
            {
                top = i;
                break;
            }
        }

        if (top < 0 || ReceivedBy(headers[top].Value) is not { } by)
        {
            return [];
        }

        var accepted = pinned is null ? trusts(by) : Same(by, pinned);
        if (!accepted)
        {
            return [];
        }

        for (var i = top + 1; i < headers.Count && !IsReceived(headers[i]); i++)
        {
            var h = headers[i];
            if (string.Equals(h.Name, "Authentication-Results", StringComparison.OrdinalIgnoreCase)
                && AuthenticationResults.AuthServId(h.Value) is { } id
                && Same(id, by))
            {
                learned = by.Trim().TrimEnd('.').ToLowerInvariant();
                return AuthenticationResults.Parse(Unfold(h.Value));
            }
        }

        return [];
    }

    /// <summary>The host after "by" in a Received header (comments stripped), or null.</summary>
    public static string? ReceivedBy(string received)
    {
        ArgumentNullException.ThrowIfNull(received);
        var clean = StripComments(Unfold(received));
        var semi = clean.IndexOf(';', StringComparison.Ordinal);
        var tokens = (semi >= 0 ? clean[..semi] : clean).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i + 1 < tokens.Length; i++)
        {
            if (string.Equals(tokens[i], "by", StringComparison.OrdinalIgnoreCase))
            {
                return tokens[i + 1].Trim('[', ']');
            }
        }

        return null;
    }

    private static bool IsReceived(EmailHeader h) => string.Equals(h.Name, "Received", StringComparison.OrdinalIgnoreCase);

    private static bool Same(string a, string b) =>
        string.Equals(a.Trim().TrimEnd('.'), b.Trim().TrimEnd('.'), StringComparison.OrdinalIgnoreCase);

    private static string StripComments(string s)
    {
        var sb = new StringBuilder(s.Length);
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

    private static string Unfold(string v) => v.Replace("\r", string.Empty, StringComparison.Ordinal).Replace('\n', ' ').Replace('\t', ' ');
}
