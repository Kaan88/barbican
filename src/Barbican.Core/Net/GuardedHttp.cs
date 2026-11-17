// Mirrors: M2 plan section 3.1 / 6.3 step 2 and 4.5; Rust reader.rs:169 one_click_post (resolve once, pin, no redirects). Stub from stage F of M2; owner reader.
// The reader stream adds the single local suppression of the banned-symbol rule for `new HttpClient` here, with its reason.

using Barbican.Core.Common;

namespace Barbican.Core.Net;

/// <summary>The only HTTP client besides the JMAP transport.</summary>
public sealed class GuardedHttp : IGuardedHttp
{
    /// <summary>The production client: https only, public addresses only.</summary>
    public GuardedHttp()
    {
    }

    /// <summary>A client that also allows plain http to loopback, for the tests' own listener on port 0 (never a real network).</summary>
    internal static GuardedHttp ForLoopbackTests()
    {
        throw new PortPendingException("GuardedHttp test seam: loopback http after the guard");
    }

    /// <inheritdoc />
    public Task<Result<GuardedResponse>> SendAsync(GuardedRequest request, CancellationToken ct)
    {
        throw new PortPendingException("GuardedHttp.SendAsync");
    }
}
