// Mirrors: Rust oauth.rs remembered sessions (user, server, kind); M1-plan §4.2. Contract stub from F2; owner accounts. Accounts added
// Kind and IsReadOnly as init-only members (add-only contract change, FABLE-REVIEW "M1 stream: accounts").

namespace Barbican.Core.Accounts;

/// <summary>A signed-in account.</summary>
/// <param name="Key">Stable key (the Rust <c>state_key</c>): server plus user.</param>
/// <param name="User">The login name.</param>
/// <param name="Server">The server base URL.</param>
public sealed record AccountInfo(string Key, string User, string Server)
{
    /// <summary>How the account signs in: <c>password</c> or <c>oauth</c> (<see cref="StoredAccount"/>).</summary>
    public string Kind { get; init; } = StoredAccount.PasswordKind;

    /// <summary>True when the client refuses every write for this account (PORT-PLAN.md §4.6; every account in M1).</summary>
    public bool IsReadOnly { get; init; }
}
