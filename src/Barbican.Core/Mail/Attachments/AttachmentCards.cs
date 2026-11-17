// Mirrors: Rust attachments.rs (split_name :24, previewable :32, wanted :39, summary :44, thumbnail :53); Bulwark reader-attachments.tsx
// (attachmentImagePreviewsEnabled). Stub from F2; owner mailcore.

using Barbican.Core.Common;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Attachments;

/// <summary>The attachment preview cards of the reader.</summary>
public static class AttachmentCards
{
    /// <summary>Bulwark fetches thumbnails for image attachments up to this size.</summary>
    public const long MaxPreviewBytes = 10_000_000;

    /// <summary>Thumbnail edge in pixels (the tile draws 40 logical px; twice that stays sharp at 200 %).</summary>
    public const int ThumbnailEdge = 80;

    /// <summary><c>report.final.pdf</c> gives (<c>report.final</c>, <c>PDF</c>); a leading dot, no dot, a long or spaced extension gives no kind.</summary>
    public static (string Stem, string Kind) SplitName(string name) => throw new PortPendingException("AttachmentCards.SplitName: mailcore stream");

    /// <summary>An image the app can draw a thumbnail of, within the size cap, with a blob.</summary>
    public static bool Previewable(Attachment attachment) => throw new PortPendingException("AttachmentCards.Previewable: mailcore stream");

    /// <summary>Whether the reader shows cards: the setting is on and at least one file is previewable.</summary>
    public static bool Wanted(IReadOnlyList<Attachment> attachments, bool enabled) => throw new PortPendingException("AttachmentCards.Wanted: mailcore stream");

    /// <summary><c>3 attachments · 1.2 MB</c>.</summary>
    public static string Summary(IReadOnlyList<Attachment> attachments) => throw new PortPendingException("AttachmentCards.Summary: mailcore stream");

    /// <summary>Decodes an image and scales it to fill a square thumbnail (cropping the overflow); null when it is not an image.</summary>
    public static ThumbnailImage? Thumbnail(byte[] bytes) => throw new PortPendingException("AttachmentCards.Thumbnail: mailcore stream");
}
