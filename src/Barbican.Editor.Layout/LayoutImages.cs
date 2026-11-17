// Mirrors: Rust src/rte/layout.rs ImgDims :57 (the natural size of an image comes from the UI layer's image cache). No Bulwark/RFC/Stalwart counterpart.

using System.Runtime.CompilerServices;
using SkiaSharp;

namespace Barbican.Editor.Layout;

/// <summary>Decodes the encoded bytes of an <see cref="Barbican.Core.Editor.ImageData"/> once per byte array (the model compares them by reference).</summary>
public static class LayoutImages
{
    private static readonly ConditionalWeakTable<byte[], Decoded> Cache = new();

    /// <summary>The decoded image, or null when the bytes are missing or not an image.</summary>
    public static SKImage? Decode(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        return Cache.GetValue(bytes, static b => new Decoded(SKImage.FromEncodedData(b))).Image;
    }

    private sealed class Decoded(SKImage? image)
    {
        public SKImage? Image { get; } = image;
    }
}
