// Mirrors: Rust oauth.rs (discovery, dynamic registration, loopback); spec I-101. Contract stub from F2; owner accounts.

namespace Barbican.Core.Accounts;

/// <summary>An OAuth sign-in request.</summary>
public sealed record OAuthSignIn(string Server, string? ClientId, bool Remember);
