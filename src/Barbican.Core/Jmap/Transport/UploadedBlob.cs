// Mirrors: RFC 8620 §6.1 (the upload response: accountId, blobId, type, size); Rust jmap.rs:797 upload_to. Owner jmap.

namespace Barbican.Core.Jmap.Transport;

/// <summary>A blob the server stored.</summary>
public sealed record UploadedBlob(string BlobId, long Size, string Type);
