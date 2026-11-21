// Mirrors: Bulwark lib/image-shrink.ts:11-145 shrinkImage (long edge at most maxSide, aspect kept, never larger than the source; a tracker of 2 px or less and an image under 64 px or 128x128 are never decoded; JPEG at quality 85 then 70, PNG
// when transparent, smaller by a quarter per step until it is at most 1.5 MB) and lib/plugin-sandbox/assistant/d.ts:343-378 doFilesGetImage (SVG left out, it can load remote content). The codec is SkiaSharp (D-M5-02), as the contact photos.
// Deviations: the picture is not turned by its EXIF orientation, the header is read by the codec (a format it does not know, HEIC for one, is "unsupported"), and more than 40 million pixels (Bulwark: 50 million) is "too_big".
using SkiaSharp;

namespace Barbican.Core.Plugins.Api.Files;

internal static class PluginFileImage
{
    internal const int DefaultSide = 1568;
    internal const int MinSide = 64;
    internal const int MaxSide = 2576;
    private const int MaxShrunkBytes = (int)(1.5 * 1024 * 1024);
    private const long MaxPixels = 40_000_000;
    private const int TrackerSide = 2;
    private const int TinySide = 64;
    private const int TinyArea = 128 * 128;

    private static readonly (SKEncodedImageFormat Format, int Quality, string Type)[] PngTries = [(SKEncodedImageFormat.Png, 100, "image/png")];
    private static readonly (SKEncodedImageFormat Format, int Quality, string Type)[] JpegTries = [(SKEncodedImageFormat.Jpeg, 85, "image/jpeg"), (SKEncodedImageFormat.Jpeg, 70, "image/jpeg")];

    internal sealed record Shrunk(string Status, string? Type = null, int Width = 0, int Height = 0, byte[]? Bytes = null);

    /// <summary>d.ts:337-341 isRasterImage: a picture the app can draw; SVG is left out.</summary>
    internal static bool IsRaster(string name, string type)
    {
        var mediaType = type.Split(';')[0].Trim().ToLowerInvariant();
        if (string.Equals(mediaType, "image/svg+xml", StringComparison.Ordinal) || name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".svgz", StringComparison.OrdinalIgnoreCase)) return false;
        return mediaType.StartsWith("image/", StringComparison.Ordinal) || Array.IndexOf(["jpg", "jpeg", "png", "gif", "webp", "bmp", "avif", "heic", "heif", "tif", "tiff"], PluginFileKinds.Extension(name)) >= 0;
    }

    internal static Shrunk Shrink(byte[] bytes, int maxSide)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var codec = SKCodec.Create(stream);
            if (codec is null) return new("unsupported");
            var (w, h) = (codec.Info.Width, codec.Info.Height);
            // the size is known from the header: nothing is decoded for a picture that is refused here
            if (w < 1 || h < 1) return new("unsupported");
            if ((long)w * h > MaxPixels) return new("too_big", Width: w, Height: h);
            if (w <= TrackerSide && h <= TrackerSide) return new("tracker", Width: w, Height: h);
            if (Math.Min(w, h) < TinySide || (long)w * h < TinyArea) return new("tiny", Width: w, Height: h);
            using var bitmap = SKBitmap.Decode(codec);
            if (bitmap is null || bitmap.IsNull) return new("unsupported", Width: w, Height: h);
            var side = Math.Max(MinSide, Math.Min(MaxSide, maxSide));
            return Encode(bitmap, codec.Info.AlphaType != SKAlphaType.Opaque, side, w, h);
        }
#pragma warning disable CA1031 // a codec may throw on a malformed file of any kind: the picture is refused, the app goes on
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            return new("failed");
        }
    }

    private static Shrunk Encode(SKBitmap bitmap, bool mayBeTransparent, int side, int w, int h)
    {
        using var image = SKImage.FromBitmap(bitmap);
        var (width, height) = Fit(bitmap.Width, bitmap.Height, side);
        bool? transparent = mayBeTransparent ? null : false;
        for (var step = 0; step < 12; step++)
        {
            using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            if (surface is null) return new("failed");
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.DrawImage(image, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            transparent ??= HasAlpha(surface);
            // JPEG has no transparency: what shows through becomes white, not black
            if (transparent != true) surface.Canvas.DrawColor(SKColors.White, SKBlendMode.DstOver);
            using var snapshot = surface.Snapshot();
            foreach (var (format, quality, type) in transparent == true ? PngTries : JpegTries)
            {
                using var data = snapshot.Encode(format, quality);
                if (data is null) return new("failed");
                if (data.Size <= MaxShrunkBytes) return new("ok", type, width, height, data.ToArray());
            }

            (width, height) = ((int)Math.Floor(width * 0.75), (int)Math.Floor(height * 0.75));
            if (Math.Max(width, height) < MinSide) break;
        }

        return new("too_big", Width: w, Height: h);
    }

    private static bool HasAlpha(SKSurface surface)
    {
        using var pixmap = surface.PeekPixels();
        if (pixmap is null) return false;
        var pixels = pixmap.GetPixelSpan();
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] < 255) return true;
        }

        return false;
    }

    /// <summary>image-shrink.ts:55-59 fitSize: long edge <paramref name="maxSide"/> at most, aspect kept, never larger than the source.</summary>
    private static (int Width, int Height) Fit(int width, int height, int maxSide)
    {
        var scale = Math.Min(1d, (double)maxSide / Math.Max(width, height));
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }
}
