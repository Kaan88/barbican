// Mirrors: Rust oauth.rs:253 Next::Retry. Stub from F2; owner accounts.

namespace Barbican.Core.Auth;

/// <summary>Another instance rotated the token: retry once with this one.</summary>
public sealed record RetryWithStored(string RefreshToken) : RefreshDecision;
