// Mirrors: Rust oauth.rs:167 (port 8791), :188 (5 minutes). Tests and automation runs use port 0 so they never hold 8791.

namespace Barbican.Core.Auth;

/// <summary>Knobs of the interactive sign-in.</summary>
public sealed record OAuthFlowOptions
{
    /// <summary>The loopback port tried first (0: an ephemeral port only).</summary>
    public int LoopbackPort { get; init; } = LoopbackListener.PreferredPort;

    /// <summary>How long the browser has.</summary>
    public TimeSpan Timeout { get; init; } = LoopbackListener.DefaultTimeout;
}
