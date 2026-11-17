// Mirrors: Rust oauth.rs refresh/decide (:251-:300), spec B-OAUTH-015; M1-plan §4.1. Contract stub from F2;
// interface owned by jmap, implemented by accounts.

using System.Net.Http.Headers;
using Barbican.Core.Common;

namespace Barbican.Core.Auth;

/// <summary>Supplies the Authorization header and renews it after a rejection.</summary>
public interface IAccessTokenSource
{
    /// <summary>The current header (Bearer or Basic).</summary>
    ValueTask<Result<AuthenticationHeaderValue>> GetAsync(CancellationToken ct);

    /// <summary>
    /// Called by the transport after a 401 with the header that was rejected (B-OAUTH-015: adopt a token
    /// rotated elsewhere, retry once, never delete the stored one).
    /// </summary>
    ValueTask<Result<AuthenticationHeaderValue>> RenewAfterRejectAsync(AuthenticationHeaderValue rejected, CancellationToken ct);
}
