// Mirrors: Rust contacts_x.rs:565 shrink_photo (at most 512 px, JPEG, aspect ratio kept); Bulwark lib/contact-media.ts, lib/contact-photo-cache.ts (blob media and data: URIs); Stalwart crates/jmap-proto/src/object/contact.rs (media). B-CON-014, B-CON-015; G-012 (input caps).
// Contract from the M5 plan section 4.1; owner con-data. The image codec is the owner's choice (D-M5-02 default: SkiaSharp, which both heads already carry).

using Barbican.Core.Common;

namespace Barbican.Core.Contacts;

/// <summary>Photos: shrinking a picked image and the caps that keep hostile input small.</summary>
public static class ContactPhoto
{
    /// <summary>The longest side after shrinking.</summary>
    public const int MaxSide = 512;

    /// <summary>The most bytes a picked file or a decoded data: URI may have before it is refused (D-M5-03).</summary>
    public const int MaxInputBytes = 8 * 1024 * 1024;

    /// <summary>Decodes, scales down to <see cref="MaxSide"/> keeping the aspect ratio (a smaller image stays), encodes JPEG; a ValidationError for bytes that are not an image or are over <see cref="MaxInputBytes"/> (900 x 600 becomes 512 x 341).</summary>
    public static Result<ShrunkPhoto> Shrink(byte[] bytes) => throw new PortPendingException("ContactPhoto.Shrink");
}
