// Mirrors: Bulwark lib/plugin-hook-catalog.ts:153 onAttachmentDownload (AttachmentInfo: name, type, size, blobId, emailId). Owner reader.

namespace Barbican.Core.Mail.Reader.Hooks;

/// <summary>An attachment that was downloaded or saved.</summary>
public sealed record AttachmentInfo(string EmailId, string BlobId, string Name, string? Type, long Size);
