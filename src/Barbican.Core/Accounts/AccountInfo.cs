// Mirrors: Rust oauth.rs remembered sessions (user, server, kind); M1-plan §4.2. Contract stub from F2; owner accounts.

namespace Barbican.Core.Accounts;

/// <summary>A signed-in account.</summary>
/// <param name="Key">Stable key (the Rust <c>state_key</c>): server plus user.</param>
/// <param name="User">The login name.</param>
/// <param name="Server">The server base URL.</param>
public sealed record AccountInfo(string Key, string User, string Server);
