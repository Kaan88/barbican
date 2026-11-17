// Mirrors: Rust attachments.rs:53 thumbnail (the image crate's decoders). Owner mailcore.

namespace Barbican.Core.Mail.Attachments;

/// <summary>Decodes an encoded image to RGBA pixels; the platform can add decoders for formats Core does not read itself.</summary>
public interface IImageDecoder
{
    /// <summary>The decoded image, or null when the bytes are not an image this decoder reads.</summary>
    ThumbnailImage? TryDecode(byte[] bytes);

    /// <summary>
    /// The square thumbnail of the image (ThumbnailScaler.Fill), or null. A decoder that can scale while it reads overrides this so a large
    /// picture never sits in memory whole (Fable review 1 F-09); the default decodes and scales.
    /// </summary>
    ThumbnailImage? TryThumbnail(byte[] bytes, int edge) => TryDecode(bytes) is { } image ? ThumbnailScaler.Fill(image, edge) : null;
}
