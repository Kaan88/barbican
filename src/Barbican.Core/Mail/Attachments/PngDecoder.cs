// Mirrors: PNG specification (W3C Recommendation, third edition) chapters 5, 7, 9; stands in for the Rust image crate (attachments.rs:53). Owner mailcore.
// Reads non-interlaced 8-bit grayscale, RGB, palette, grayscale+alpha and RGBA. Anything else returns null (the card then shows its kind).

using System.Buffers.Binary;
using System.IO.Compression;

namespace Barbican.Core.Mail.Attachments;

/// <summary>A small PNG reader so Core can make thumbnails without an imaging library.</summary>
public sealed class PngDecoder : IImageDecoder
{
    /// <summary>Refuse pictures with more pixels than this (a decompression bomb inside a 10 MB file).</summary>
    private const long MaxPixels = 40_000_000;

    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public ThumbnailImage? TryDecode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        try
        {
            return Decode(bytes);
        }
        catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException or ArgumentException or OverflowException or EndOfStreamException)
        {
            return null;
        }
    }

    private static ThumbnailImage? Decode(byte[] bytes)
    {
        if (bytes.Length < 33 || !bytes.AsSpan(0, 8).SequenceEqual(Signature))
        {
            return null;
        }

        var parts = ReadChunks(bytes);
        if (parts is not { } p || p.Width <= 0 || p.Height <= 0 || (long)p.Width * p.Height > MaxPixels)
        {
            return null;
        }

        var channels = p.ColorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
        if (channels == 0 || (p.ColorType == 3 && p.Palette is null))
        {
            return null;
        }

        var stride = checked(p.Width * channels);
        var raw = new byte[checked((stride + 1) * p.Height)];
        p.Idat.Position = 0;
        using (var z = new ZLibStream(p.Idat, CompressionMode.Decompress))
        {
            z.ReadExactly(raw);
        }

        Unfilter(raw, stride, p.Height, channels);
        var rgba = ToRgba(raw, p, channels);
        return rgba is null ? null : new ThumbnailImage(p.Width, p.Height, rgba);
    }

    private static Chunks? ReadChunks(byte[] bytes)
    {
        int width = 0, height = 0, colorType = -1;
        byte[]? palette = null;
        var idat = new MemoryStream();
        var pos = 8;
        var sawHeader = false;
        while (pos + 12 <= bytes.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(pos, 4));
            var type = System.Text.Encoding.ASCII.GetString(bytes, pos + 4, 4);
            var dataStart = pos + 8;
            if (length > bytes.Length - dataStart - 4)
            {
                return null;
            }

            var data = bytes.AsSpan(dataStart, (int)length);
            if (string.Equals(type, "IHDR", StringComparison.Ordinal))
            {
                if (data.Length != 13 || data[8] != 8 || data[10] != 0 || data[11] != 0 || data[12] != 0)
                {
                    return null;
                }

                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[..4]));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4)));
                colorType = data[9];
                sawHeader = true;
            }
            else if (string.Equals(type, "PLTE", StringComparison.Ordinal))
            {
                palette = data.ToArray();
            }
            else if (string.Equals(type, "IDAT", StringComparison.Ordinal))
            {
                idat.Write(data);
            }
            else if (string.Equals(type, "IEND", StringComparison.Ordinal))
            {
                break;
            }

            pos = dataStart + (int)length + 4;
        }

        return sawHeader ? new Chunks(width, height, colorType, palette, idat) : null;
    }

    private static byte[]? ToRgba(byte[] raw, Chunks p, int channels)
    {
        var stride = p.Width * channels;
        var rgba = new byte[checked(p.Width * p.Height * 4)];
        for (var y = 0; y < p.Height; y++)
        {
            var row = (y * (stride + 1)) + 1;
            for (var x = 0; x < p.Width; x++)
            {
                var s = row + (x * channels);
                var d = ((y * p.Width) + x) * 4;
                if (!WritePixel(rgba, d, raw, s, p))
                {
                    return null;
                }
            }
        }

        return rgba;
    }

    private static bool WritePixel(byte[] rgba, int d, byte[] raw, int s, Chunks p)
    {
        switch (p.ColorType)
        {
            case 0:
                rgba[d] = rgba[d + 1] = rgba[d + 2] = raw[s];
                rgba[d + 3] = 255;
                return true;
            case 2:
                rgba[d] = raw[s];
                rgba[d + 1] = raw[s + 1];
                rgba[d + 2] = raw[s + 2];
                rgba[d + 3] = 255;
                return true;
            case 3:
                var i = raw[s] * 3;
                if (p.Palette is null || i + 2 >= p.Palette.Length)
                {
                    return false;
                }

                rgba[d] = p.Palette[i];
                rgba[d + 1] = p.Palette[i + 1];
                rgba[d + 2] = p.Palette[i + 2];
                rgba[d + 3] = 255;
                return true;
            case 4:
                rgba[d] = rgba[d + 1] = rgba[d + 2] = raw[s];
                rgba[d + 3] = raw[s + 1];
                return true;
            default:
                rgba[d] = raw[s];
                rgba[d + 1] = raw[s + 1];
                rgba[d + 2] = raw[s + 2];
                rgba[d + 3] = raw[s + 3];
                return true;
        }
    }

    private static void Unfilter(byte[] raw, int stride, int height, int bpp)
    {
        for (var y = 0; y < height; y++)
        {
            var row = y * (stride + 1);
            var filter = raw[row];
            for (var i = 0; i < stride; i++)
            {
                var at = row + 1 + i;
                int a = i >= bpp ? raw[at - bpp] : 0;
                int b = y > 0 ? raw[at - (stride + 1)] : 0;
                int c = y > 0 && i >= bpp ? raw[at - (stride + 1) - bpp] : 0;
                var add = filter switch
                {
                    0 => 0,
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => throw new InvalidDataException("bad PNG filter"),
                };
                raw[at] = unchecked((byte)(raw[at] + add));
            }
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private sealed record Chunks(int Width, int Height, int ColorType, byte[]? Palette, MemoryStream Idat);
}
