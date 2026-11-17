// Mirrors: Rust compose.rs upload (:1120 area), upload_inline; Bulwark lib/jmap/client.ts uploadBlob :6662, hooks onBeforeAttachmentUpload / onBeforeBlobUpload / onAfterAttachmentUpload (spec/plugins/hooks.csv, M3); RFC 8620 section 6.1. Stalwart crates/jmap/src/blob/upload.rs. Contract from the M3 plan section 4.3; owner send-pipeline.

using Barbican.Core.Common;

namespace Barbican.Core.Compose;

/// <summary>Uploads attachments after the gates (account upload limit, per-message total, the plugin intercepts).</summary>
public interface IAttachmentUploader
{
    /// <summary>Uploads one file; the result carries the blob id. <paramref name="existing"/> is what the draft already holds (for the total-size gate).</summary>
    Task<Result<DraftAttachment>> UploadAsync(string accountId, AttachmentUpload upload, IReadOnlyList<DraftAttachment> existing, CancellationToken ct);
}
