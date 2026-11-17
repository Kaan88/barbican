// Mirrors: Rust attachments.rs:53 thumbnail. Owner mailcore.

using System.Collections.Immutable;

namespace Barbican.Core.Mail.Attachments;

/// <summary>The decoders thumbnails try, in order: Core's PNG reader first, then whatever the platform registered (JPEG, GIF, WebP, BMP).</summary>
public static class ThumbnailDecoders
{
    private static ImmutableArray<IImageDecoder> all = [new PngDecoder()];

    public static IReadOnlyList<IImageDecoder> All => all;

    /// <summary>Adds a decoder after the existing ones (the App registers a Skia-based one at start).</summary>
    public static void Register(IImageDecoder decoder)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        ImmutableInterlocked.Update(ref all, a => a.Add(decoder));
    }
}
