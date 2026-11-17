// Mirrors: Rust attachments.rs (split_name :24, previewable :32, wanted :39, summary :44, thumbnail :53); Bulwark reader-attachments.tsx
// (attachmentImagePreviewsEnabled). Owner mailcore.

using System.Globalization;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Attachments;

/// <summary>The attachment preview cards of the reader.</summary>
public static class AttachmentCards
{
    /// <summary>Bulwark fetches thumbnails for image attachments up to this size.</summary>
    public const long MaxPreviewBytes = 10_000_000;

    /// <summary>Thumbnail edge in pixels (the tile draws 40 logical px; twice that stays sharp at 200 %).</summary>
    public const int ThumbnailEdge = 80;

    private static readonly HashSet<string> Decodable = new(StringComparer.Ordinal)
    {
        "image/png", "image/jpeg", "image/jpg", "image/pjpeg", "image/gif", "image/webp", "image/bmp",
    };

    /// <summary><c>report.final.pdf</c> gives (<c>report.final</c>, <c>PDF</c>); a leading dot, no dot, a long or spaced extension gives no kind.</summary>
    public static (string Stem, string Kind) SplitName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var dot = name.LastIndexOf('.');
        if (dot > 0 && dot < name.Length - 1)
        {
            var ext = name[(dot + 1)..];
            if (ext.Length <= 8 && !ext.Contains(' ', StringComparison.Ordinal))
            {
                return (name[..dot], ext.ToUpperInvariant());
            }
        }

        return (name, string.Empty);
    }

    /// <summary>An image the app can draw a thumbnail of, within the size cap, with a blob.</summary>
    public static bool Previewable(Attachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        var type = (attachment.Type ?? string.Empty).ToLowerInvariant();
        return Decodable.Contains(type) && attachment.Size <= MaxPreviewBytes && attachment.BlobId is not null;
    }

    /// <summary>Whether the reader shows cards: the setting is on and at least one file is previewable.</summary>
    public static bool Wanted(IReadOnlyList<Attachment> attachments, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        return enabled && attachments.Any(Previewable);
    }

    /// <summary><c>3 attachments · 1.2 MB</c>.</summary>
    public static string Summary(IReadOnlyList<Attachment> attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        var total = attachments.Sum(a => a.Size);
        var plural = attachments.Count == 1 ? string.Empty : "s";
        return string.Create(CultureInfo.InvariantCulture, $"{attachments.Count} attachment{plural} · {ByteSize.Human(total)}");
    }

    /// <summary>Decodes an image and scales it to fill a square thumbnail (cropping the overflow); null when it is not an image.</summary>
    /// <remarks>Core decodes PNG itself; other formats need an <see cref="IImageDecoder"/> from the platform (see <see cref="ThumbnailDecoders"/>).</remarks>
    public static ThumbnailImage? Thumbnail(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        foreach (var decoder in ThumbnailDecoders.All)
        {
            if (decoder.TryDecode(bytes) is { } image)
            {
                return ThumbnailScaler.Fill(image, ThumbnailEdge);
            }
        }

        return null;
    }
}
