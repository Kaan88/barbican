// Mirrors: Rust oauth.rs:262 decide, :273 is_rejection; RFC 6749 §5.2 (invalid_grant); Stalwart rotates refresh tokens. Stub from F2; owner accounts.

using Barbican.Core.Common;

namespace Barbican.Core.Auth;

/// <summary>The pure rules of renewing an OAuth session.</summary>
public static class OAuthRefresh
{
    /// <summary>What to do given the refresh token used, the one stored now and whether the server rejected it.</summary>
    public static RefreshDecision Decide(string used, string? stored, bool rejected) => throw new PortPendingException("OAuthRefresh.Decide: accounts stream");

    /// <summary>True for a 4xx token response naming invalid_grant, invalid_client or unauthorized_client.</summary>
    public static bool IsRejection(string error) => throw new PortPendingException("OAuthRefresh.IsRejection: accounts stream");
}
