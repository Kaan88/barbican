// Mirrors: Rust oauth.rs:258 Next::Transient. Stub from F2; owner accounts.

namespace Barbican.Core.Auth;

/// <summary>Network or server trouble: keep everything, try again later.</summary>
public sealed record TransientFailure : RefreshDecision;
