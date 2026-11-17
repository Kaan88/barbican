// Mirrors: Rust oauth.rs:262 decide, :273 is_rejection (at rust-final); RFC 6749 §5.2 (invalid_grant, invalid_client, unauthorized_client);
// spec B-OAUTH-013, B-OAUTH-014. Stalwart rotates refresh tokens (crates/common/src/auth/oauth/token.rs), so another instance may have
// used ours: the stored token wins over the one in memory, and a rejection is retried once with a newer stored token.

namespace Barbican.Core.Auth;

/// <summary>The pure rules of renewing an OAuth session.</summary>
public static class OAuthRefresh
{
    /// <summary>The prefix of a token endpoint error (Rust oauth.rs:239 <c>parse_tokens</c>).</summary>
    public const string TokenRequestFailed = "Token request failed (";

    /// <summary>What to do given the refresh token used, the one stored now and whether the server rejected it.</summary>
    public static RefreshDecision Decide(string used, string? stored, bool rejected)
    {
        ArgumentNullException.ThrowIfNull(used);
        if (!rejected)
        {
            return new TransientFailure();
        }

        return !string.IsNullOrEmpty(stored) && !string.Equals(stored, used, StringComparison.Ordinal)
            ? new RetryWithStored(stored)
            : new NeedsSignIn();
    }

    /// <summary>True for a 4xx token response naming invalid_grant, invalid_client or unauthorized_client.</summary>
    public static bool IsRejection(string error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.StartsWith(TokenRequestFailed + "4", StringComparison.Ordinal)
            && (error.Contains("invalid_grant", StringComparison.Ordinal)
                || error.Contains("invalid_client", StringComparison.Ordinal)
                || error.Contains("unauthorized_client", StringComparison.Ordinal));
    }
}
