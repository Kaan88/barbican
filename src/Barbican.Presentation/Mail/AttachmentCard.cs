// Mirrors: Rust attachments.rs show :80 (AttCard: index, stem, ext, size, has_thumb, thumb); Bulwark reader-attachments.tsx. Owner mailcore.

using Barbican.Core.Mail.Attachments;

namespace Barbican.Presentation.Mail;

/// <summary>A tile of the reader's attachment strip: name, kind, size and, once fetched, a picture.</summary>
public sealed record AttachmentCard(int Index, string Stem, string Kind, string Size, string? BlobId, bool Previewable, ThumbnailImage? Thumbnail)
{
    public bool HasThumbnail => Thumbnail is not null;
}
