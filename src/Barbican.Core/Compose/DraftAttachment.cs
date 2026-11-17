// Mirrors: Rust jmap.rs Attachment (:1178 area), compose.rs attachments; Bulwark lib/jmap/client.ts uploadBlob :6662, email-composer.tsx attachments state; RFC 8621 section 4.1.4 (EmailBodyPart), RFC 2392 (cid). Contract from the M3 plan section 4.3; owner send-pipeline.

namespace Barbican.Core.Compose;

/// <summary>An attachment of a draft: uploaded (has a blob id) or still uploading (no blob id).</summary>
public sealed record DraftAttachment
{
    public required string Name { get; init; }

    public string Type { get; init; } = "application/octet-stream";

    public long Size { get; init; }

    public string? BlobId { get; init; }

    /// <summary>The content id without angle brackets when the part is inline (referenced as <c>cid:</c> from the HTML).</summary>
    public string? Cid { get; init; }
}
