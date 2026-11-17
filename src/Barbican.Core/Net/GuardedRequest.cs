// Mirrors: M2 plan section 4.5 (IGuardedHttp: Method, Uri, form body, Accept). Stub from stage F of M2; owner reader.

namespace Barbican.Core.Net;

/// <summary>One outbound request through the guarded client.</summary>
/// <param name="Method">GET or POST.</param>
/// <param name="Uri">The target; https only (loopback http only through the test seam).</param>
/// <param name="FormBody">An application/x-www-form-urlencoded body for a POST, or null.</param>
/// <param name="Accept">The Accept header, or null.</param>
public sealed record GuardedRequest(HttpMethod Method, Uri Uri, string? FormBody = null, string? Accept = null);
