// Mirrors: Bulwark lib/files-save.ts SaveableBlob.

namespace Barbican.Core.Files;

/// <summary>What filing an attachment needs: its blob, name, media type and size.</summary>
public sealed record FileBlobSource(string BlobId, string Name, string MediaType, long Size);
