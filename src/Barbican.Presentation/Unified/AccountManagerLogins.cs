// Mirrors: Rust unified.rs:242 users (the remembered logins; the active session's client is reused). Owner unified.

using Barbican.Core.Accounts;
using Barbican.Core.Jmap;

namespace Barbican.Presentation.Unified;

/// <summary>The unified logins of the app: the account manager's accounts and sessions.</summary>
public sealed class AccountManagerLogins(IAccountManager accounts) : IUnifiedLogins
{
    /// <inheritdoc />
    public string? Active => accounts.Active?.Key;

    /// <inheritdoc />
    public IReadOnlyList<(string Login, IJmapClient? Client)> Logins => [.. accounts.Accounts.Select(a => (a.Key, accounts.SessionFor(a.Key)?.Client))];

    /// <inheritdoc />
    public IJmapClient? ClientOf(string login) => accounts.SessionFor(login)?.Client;

    /// <inheritdoc />
    public bool IsTestServer(string login) =>
        accounts.Accounts.FirstOrDefault(a => string.Equals(a.Key, login, StringComparison.Ordinal)) is { } account
        && Uri.TryCreate(account.Server, UriKind.Absolute, out var server) && ServerEndpoint.IsLoopback(server);
}
