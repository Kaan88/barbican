// Mirrors: Bulwark lib/plugin-hook-catalog.ts:150-152 onBeforeAttachmentUpload / onAfterAttachmentUpload (AttachmentInfo of the file being attached). Owner send-pipeline.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>A file attached in the composer: before (intercept) and after (observer, with the blob id) the upload.</summary>
public sealed record AttachmentUploadInfo(string Name, string Type, long Size, string? BlobId = null);
