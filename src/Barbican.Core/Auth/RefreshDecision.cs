// Mirrors: Rust oauth.rs:251 enum Next; spec B-OAUTH-015. Stub from F2; owner accounts.

namespace Barbican.Core.Auth;

/// <summary>What to do after a refresh attempt (closed hierarchy: the sealed records in this folder).</summary>
public abstract record RefreshDecision
{
    private protected RefreshDecision()
    {
    }
}
