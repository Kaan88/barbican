// Mirrors: Rust oauth.rs:256 Next::NeedsSignIn. Stub from F2; owner accounts.

namespace Barbican.Core.Auth;

/// <summary>The server rejected the token and nothing newer is stored: the user must sign in again (nothing is deleted).</summary>
public sealed record NeedsSignIn : RefreshDecision;
