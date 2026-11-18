// Mirrors: Rust contacts_x.rs:565 shrink_photo (at most 512 px, JPEG, aspect ratio kept); Bulwark lib/contact-media.ts, lib/contact-photo-cache.ts (blob media and data: URIs); Stalwart crates/jmap-proto/src/object/contact.rs (media). B-CON-014, B-CON-015; G-012 (input caps).
// Contract from the M5 plan section 4.1; owner con-data. The image codec is SkiaSharp (D-M5-02).

using SkiaSharp;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.Contacts;

/// <summary>Photos: shrinking a picked image and the caps that keep hostile input small.</summary>
public static class ContactPhoto
{
    /// <summary>The longest side after shrinking.</summary>
    public const int MaxSide = 512;

    /// <summary>The most bytes a picked file or a decoded data: URI may have before it is refused (D-M5-03).</summary>
    public const int MaxInputBytes = 8 * 1024 * 1024;

    /// <summary>The most pixels a picture may have before it is decoded (a small PNG can describe a huge bitmap): 40 million, about 160 MB as RGBA (D-M5-A03).</summary>
    public const long MaxPixels = 40_000_000;

    /// <summary>Decodes, scales down to <see cref="MaxSide"/> keeping the aspect ratio (a smaller image stays), encodes JPEG; a ValidationError for bytes that are not an image or are over <see cref="MaxInputBytes"/> (900 x 600 becomes 512 x 341).</summary>
    public static Result<ShrunkPhoto> Shrink(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length > MaxInputBytes)
        {
            return Result<ShrunkPhoto>.Fail(new ValidationError("This picture is too large (the limit is 8 MB)."));
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var codec = SKCodec.Create(stream);
            if (codec is null)
            {
                return Result<ShrunkPhoto>.Fail(new ValidationError("Not an image."));
            }

            var info = codec.Info;

            // the size is known from the header: refuse before a pixel is allocated
            if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaxPixels)
            {
                return Result<ShrunkPhoto>.Fail(new ValidationError("This picture has too many pixels."));
            }

            using var bitmap = SKBitmap.Decode(codec);
            if (bitmap is null || bitmap.IsNull)
            {
                return Result<ShrunkPhoto>.Fail(new ValidationError("Not an image."));
            }

            var longest = Math.Max(bitmap.Width, bitmap.Height);
            var scale = longest > MaxSide ? (double)MaxSide / longest : 1d;
            var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
            var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
            using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
            if (surface is null)
            {
                return Result<ShrunkPhoto>.Fail(new ValidationError("This picture could not be processed."));
            }

            // a transparent picture becomes white, as a JPEG has no alpha
            surface.Canvas.Clear(SKColors.White);
            using var image = SKImage.FromBitmap(bitmap);
            surface.Canvas.DrawImage(image, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            using var shrunk = surface.Snapshot();
            using var encoded = shrunk.Encode(SKEncodedImageFormat.Jpeg, 85);
            return encoded is null
                ? Result<ShrunkPhoto>.Fail(new ValidationError("This picture could not be saved as JPEG."))
                : Result<ShrunkPhoto>.Ok(new ShrunkPhoto("image/jpeg", encoded.ToArray()));
        }
#pragma warning disable CA1031 // a codec may throw on a malformed file of any kind: the picture is refused, the app goes on (G-012)
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            return Result<ShrunkPhoto>.Fail(new ValidationError("Not an image."));
        }
    }

    /// <summary>The bytes of an embedded base64 data: URI and its media type (empty when the URI has none); a ValidationError for a URI that is not base64 data or whose bytes would pass <see cref="MaxInputBytes"/> (checked on the text, before decoding).</summary>
    public static Result<(string MediaType, byte[] Bytes)> FromDataUri(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (VCardParser.ParseDataUri(uri) is not { } parsed)
        {
            return Result<(string, byte[])>.Fail(new ValidationError("Not an embedded picture."));
        }

        // base64 text is 4 characters for 3 bytes
        if (parsed.Data.Length > (MaxInputBytes / 3 * 4) + 8)
        {
            return Result<(string, byte[])>.Fail(new ValidationError("This picture is too large (the limit is 8 MB)."));
        }

        var text = parsed.Data.Trim();
        var buffer = new byte[(text.Length / 4 * 3) + 3];
        return Convert.TryFromBase64String(text, buffer, out var written)
            ? Result<(string, byte[])>.Ok((parsed.MediaType, buffer[..written]))
            : Result<(string, byte[])>.Fail(new ValidationError("Not an embedded picture."));
    }
}
