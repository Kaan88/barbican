// Mirrors: Bulwark lib/jmap/types.ts, RFC 8621; Rust jmap.rs / mail.rs value shapes (JSON there, typed here). Model stub from F2 (M1-plan §4.1); owner jmap.

namespace Barbican.Core.Mail.Model;

/// <summary>How much of a body to fetch (Bulwark getEmail :3517; <c>maxBodyValueBytes</c> truncation, J-053).</summary>
public sealed record BodyFetchOptions
{
    public long? MaxBodyValueBytes { get; init; }

    /// <summary>A prefetch leaves messages with large inline images to the open itself (Rust msgload.rs).</summary>
    public bool Prefetch { get; init; }
}
