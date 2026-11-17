// Mirrors: RFC 8620 §2 (urn:ietf:params:jmap:core limits); Rust jmap.rs:159 the gates, :313 limit, :317-:320 max_get/max_set/
// max_calls/max_upload and their defaults (Stalwart's: 16 calls, 500 objects, 4 concurrent); Bulwark lib/jmap/client.ts:7103
// (maxCallsInRequest); lib/jmap/request-limits.ts. Owner jmap.

namespace Barbican.Core.Jmap;

/// <summary>The server's request limits, with Rust's defaults for a server that omits one.</summary>
public sealed record CoreLimits
{
    public long MaxSizeUpload { get; init; } = 50_000_000;

    public int MaxConcurrentUpload { get; init; } = 4;

    public long MaxSizeRequest { get; init; } = 10_000_000;

    public int MaxConcurrentRequests { get; init; } = 4;

    public int MaxCallsInRequest { get; init; } = 16;

    public int MaxObjectsInGet { get; init; } = 500;

    public int MaxObjectsInSet { get; init; } = 500;
}
