// Mirrors: RFC 8601 §5 (a consumer ignores results from an authserv-id it does not trust; only the receiving server's own header counts)
// and §7.1 (forged headers); user decision D-9 (ignore Authentication-Results from a foreign authserv-id: no chip, no verdict). Fable
// review 1 F-07. Owner mailcore (fix-security stream).
// Which ids count: the host of the account's JMAP server, or a host in the same parent domain (Stalwart signs as its configured hostname,
// for example mx.<domain>, while the client may reach it as mail.<domain>). A sender's own header names the sender's domain, so it is
// ignored. Rust auth.rs:55 trusted the topmost header whatever its id; this is the deliberate change.

namespace Barbican.Core.Mail.AuthResults;

/// <summary>Decides which authserv-ids belong to the account's own server.</summary>
public static class AuthServTrust
{
    /// <summary>The host of the server in an account key (Accounts.AccountKey: "server\nuser"), lower-case, or null.</summary>
    public static string? ServerHostOf(string? accountKey)
    {
        if (string.IsNullOrEmpty(accountKey))
        {
            return null;
        }

        var server = accountKey.Split('\n')[0];
        return Uri.TryCreate(server, UriKind.Absolute, out var uri) && uri.Host.Length > 0 ? uri.IdnHost.ToLowerInvariant() : null;
    }

    /// <summary>True when <paramref name="authServId"/> was written by the server at <paramref name="serverHost"/>.</summary>
    public static bool Trusts(string? serverHost, string? authServId)
    {
        if (string.IsNullOrEmpty(serverHost) || string.IsNullOrEmpty(authServId))
        {
            return false;
        }

        var id = authServId.Trim().TrimEnd('.').ToLowerInvariant();
        var host = serverHost.Trim().TrimEnd('.').ToLowerInvariant();
        if (string.Equals(id, host, StringComparison.Ordinal))
        {
            return true;
        }

        // the parent domain of the server host, when it still has two labels (mail.example.org -> example.org; never a bare TLD)
        var dot = host.IndexOf('.', StringComparison.Ordinal);
        var parent = dot > 0 ? host[(dot + 1)..] : string.Empty;
        if (!parent.Contains('.', StringComparison.Ordinal))
        {
            return id.EndsWith("." + host, StringComparison.Ordinal);
        }

        return string.Equals(id, parent, StringComparison.Ordinal) || id.EndsWith("." + parent, StringComparison.Ordinal);
    }
}
