// Mirrors: M2 plan section 4.5 (the only outbound HTTP outside the JMAP transport); Rust reader.rs:169 one_click_post, avatars.rs:231 get_capped.
// Stub contract from stage F of M2; owner reader.

using Barbican.Core.Common;

namespace Barbican.Core.Net;

/// <summary>
/// Outbound HTTP for everything that is not the JMAP server: https only (loopback in tests only), every resolved address public, the
/// connection pinned to the checked address, no redirects, no cookies, no credentials, no proxy, size and time caps.
/// </summary>
public interface IGuardedHttp
{
    /// <summary>Sends one request; a refusal (private address, not https, too large, too slow) is a failure, a redirect status is a response.</summary>
    Task<Result<GuardedResponse>> SendAsync(GuardedRequest request, CancellationToken ct);
}
