// Mirrors: Rust reader.rs:143 dkim_pass (the topmost Authentication-Results, an aligned DKIM pass for the From domain), auth.rs:66
// aligned_dkim_pass; RFC 8058 section 4 (a DKIM signature over the list headers before a one-click POST), RFC 8601, RFC 6376.
// The production gate (OneClick) takes the results of the server's own trace block only (AuthTrace, R2-07, D-9); the JSON overload keeps
// the Rust semantics (topmost header) for the ported test.

using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Mail.AuthResults;

/// <summary>The gate that lets one-click unsubscribe run only for a sender whose DKIM signature vouches for the From domain.</summary>
public static class DkimGate
{
    /// <summary>True when the topmost Authentication-Results has dkim=pass with a signing domain aligned to the domain of <paramref name="from"/>.</summary>
    public static bool DkimPass(JsonArray headers, string from)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(from);
        var topmost = headers.FirstOrDefault(h => string.Equals(h.Str("name"), "Authentication-Results", StringComparison.OrdinalIgnoreCase)).Str("value");
        return topmost is not null && ServerDkimPass(AuthenticationResults.Parse(topmost), from);
    }

    /// <summary>True when <paramref name="serverResults"/> (the account server's own, AuthTrace) hold an aligned DKIM pass for the From domain.</summary>
    public static bool ServerDkimPass(IReadOnlyList<AuthResult> serverResults, string from)
    {
        ArgumentNullException.ThrowIfNull(serverResults);
        ArgumentNullException.ThrowIfNull(from);
        return AuthenticationResults.DomainOf(from) is { } domain && AuthenticationResults.AlignedDkimPass(serverResults, domain);
    }
}
