// Mirrors: Bulwark lib/plugin-hook-catalog.ts:151 onBeforeBlobUpload ({ file, blobId } | ExternalAttachmentResult): a plugin may offload the file and return the blob id it made. Owner send-pipeline.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>A file about to be uploaded; a transform handler that stored it elsewhere returns it with <see cref="BlobId"/> set and no upload happens.</summary>
public sealed record BlobUploadPayload(string Name, string Type, long Size)
{
    public string? BlobId { get; init; }
}
