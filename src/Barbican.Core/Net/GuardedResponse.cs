// Mirrors: M2 plan section 4.5 (IGuardedHttp). Stub from stage F of M2; owner reader.

namespace Barbican.Core.Net;

/// <summary>The answer of a guarded request: the status and the body (capped).</summary>
/// <param name="StatusCode">The HTTP status; a redirect is returned as it is, never followed.</param>
/// <param name="Body">The body bytes, at most the size cap.</param>
/// <param name="ContentType">The Content-Type header, or null.</param>
public sealed record GuardedResponse(int StatusCode, ReadOnlyMemory<byte> Body, string? ContentType);
