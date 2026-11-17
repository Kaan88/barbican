// Mirrors: Bulwark lib/jmap/client.ts uploadBlob :6662, email-composer.tsx attachment limits (maxSizeUpload, maxSizeAttachmentsPerEmail); RFC 8620 section 6.1. Contract from the M3 plan section 4.3; owner send-pipeline.

namespace Barbican.Core.Compose;

/// <summary>A file to attach (picked, dropped, pasted or an inline image).</summary>
public sealed record AttachmentUpload(string Name, string Type, ReadOnlyMemory<byte> Data)
{
    /// <summary>Set for an inline image: the generated content id the document refers to.</summary>
    public string? Cid { get; init; }
}
