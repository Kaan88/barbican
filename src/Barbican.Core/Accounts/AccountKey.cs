// Mirrors: Rust jmap.rs:332 state_key (server and user; a state never crosses accounts), oauth.rs:378 (one remembered entry per user and
// server). The JMAP account id is left out so the key is known before the account connects (switch list, unread watch).

namespace Barbican.Core.Accounts;

/// <summary>The stable key of an account.</summary>
public static class AccountKey
{
    /// <summary>"<c>server</c>\n<c>user</c>", with the server as normalised by ServerEndpoint (no trailing slash) and the user trimmed.</summary>
    public static string Of(string server, string user)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(user);
        return server.Trim().TrimEnd('/') + "\n" + user.Trim();
    }
}
