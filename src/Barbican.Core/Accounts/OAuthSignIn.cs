// Mirrors: Rust oauth.rs (discovery, dynamic registration, loopback); spec I-101. Contract stub from F2; owner accounts.

namespace Barbican.Core.Accounts;

/// <summary>An OAuth sign-in request.</summary>
public sealed record OAuthSignIn(string Server, string? ClientId, bool Remember)
{
    /// <summary>The account name typed (optional): Stalwart's per-account discovery and the <c>login_hint</c> (Rust oauth.rs:183).</summary>
    public string? LoginHint { get; init; }
}
