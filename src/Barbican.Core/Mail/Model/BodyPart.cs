// Mirrors: Bulwark lib/jmap/types.ts, RFC 8621; Rust jmap.rs / mail.rs value shapes (JSON there, typed here). Model stub from F2 (M1-plan §4.1); owner jmap.

namespace Barbican.Core.Mail.Model;

/// <summary>A text or HTML body part (RFC 8621 §4.1.4 EmailBodyPart; BODY_PROPERTIES in Rust jmap.rs:1647).</summary>
public sealed record BodyPart
{
    public string? PartId { get; init; }

    public string? BlobId { get; init; }

    public long Size { get; init; }

    public string? Type { get; init; }

    public string? Charset { get; init; }

    public string? Name { get; init; }

    public string? Cid { get; init; }

    public string? Disposition { get; init; }
}
