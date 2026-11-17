// Mirrors: RFC 8620 section 6.1 (upload response: blobId, type, size); Bulwark lib/jmap/client.ts uploadBlob :6662. Contract from stage F of M2; owner actions.

namespace Barbican.Core.Mail.Write;

/// <summary>A blob the server stored.</summary>
public sealed record UploadedBlob(string BlobId, string Type, long Size);
