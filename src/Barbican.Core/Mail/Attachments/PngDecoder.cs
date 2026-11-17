// Mirrors: PNG specification (W3C Recommendation, third edition) chapters 5, 7, 9; stands in for the Rust image crate (attachments.rs:53). Owner mailcore.
// Reads non-interlaced 8-bit grayscale, RGB, palette, grayscale+alpha and RGBA. Anything else returns null (the card then shows its kind).
// Fable review 1 F-09: a few kilobytes of PNG can claim tens of megapixels, and the whole-picture decode held raw scanlines plus RGBA
// (about 360 MB at the old 40 MP cap). The thumbnail path (TryThumbnail) now inflates one scanline at a time, keeps only the previous
// row for the filters, and adds each pixel straight into the area-average cells of the square thumbnail (the same cells and rounding as
// ThumbnailScaler.Fill), so its memory is a few rows whatever the picture says. The whole-picture TryDecode is capped at 16 MP. Both
// return null on OutOfMemoryException rather than ending the process; AttachmentCards runs one decode at a time.
// Fable review 2 R2-08 (M2, owner reader): the pixel caps let a 100,000,000 x 1 picture through, and the row buffers alone (three of
// width x channels) asked for about 1.2 GB. Header now also refuses a width over MaxWidth (32,768) and a row over MaxRowBytes (16 MB).

using System.Buffers.Binary;
using System.IO.Compression;

namespace Barbican.Core.Mail.Attachments;

/// <summary>A small PNG reader so Core can make thumbnails without an imaging library.</summary>
public sealed class PngDecoder : IImageDecoder
{
    /// <summary>The whole-picture decode refuses more pixels than this (one row buffer plus RGBA: about 64 MB at the cap).</summary>
    public const long MaxDecodePixels = 16_000_000;

    /// <summary>The streaming thumbnail refuses more pixels than this (memory is a few rows; this bounds the time).</summary>
    public const long MaxThumbnailPixels = 100_000_000;

    /// <summary>R2-08: a wider picture is refused before any row buffer is allocated.</summary>
    public const int MaxWidth = 32_768;

    /// <summary>R2-08: a decoded row (width x channels, 8 bits each) larger than this is refused.</summary>
    public const long MaxRowBytes = 16L * 1024 * 1024;

    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    /// <inheritdoc/>
    public ThumbnailImage? TryDecode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return Guard(() => Decode(bytes));
    }

    /// <inheritdoc/>
    public ThumbnailImage? TryThumbnail(byte[] bytes, int edge)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(edge, 1);
        return Guard(() => Thumbnail(bytes, edge));
    }

    private static ThumbnailImage? Guard(Func<ThumbnailImage?> decode)
    {
        try
        {
            return decode();
        }
        catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException or ArgumentException or OverflowException or EndOfStreamException
            or OutOfMemoryException)
        {
            return null;
        }
    }

    private static Chunks? Header(byte[] bytes, long maxPixels)
    {
        if (bytes.Length < 33 || !bytes.AsSpan(0, 8).SequenceEqual(Signature))
        {
            return null;
        }

        var p = ReadChunks(bytes);
        if (p is null || p.Width <= 0 || p.Height <= 0 || (long)p.Width * p.Height > maxPixels || p.Channels == 0 || (p.ColorType == 3 && p.Palette is null)
            || p.Width > MaxWidth || (long)p.Width * p.Channels > MaxRowBytes)
        {
            return null;
        }

        return p;
    }

    private static ThumbnailImage? Decode(byte[] bytes)
    {
        if (Header(bytes, MaxDecodePixels) is not { } p)
        {
            return null;
        }

        var rgba = new byte[checked(p.Width * p.Height * 4)];
        var ok = ForEachRow(p, (y, row) =>
        {
            for (var x = 0; x < p.Width; x++)
            {
                if (!WritePixel(rgba.AsSpan(((y * p.Width) + x) * 4, 4), row, x * p.Channels, p))
                {
                    return false;
                }
            }

            return true;
        });
        return ok ? new ThumbnailImage(p.Width, p.Height, rgba) : null;
    }

    // ThumbnailScaler.Fill without the picture: the same centre crop, the same cells [x0, x1) x [y0, y1), the same integer means
    private static ThumbnailImage? Thumbnail(byte[] bytes, int edge)
    {
        if (Header(bytes, MaxThumbnailPixels) is not { } p)
        {
            return null;
        }

        var cells = new ThumbnailCells(p.Width, p.Height, edge);
        var pixel = new byte[4];
        var ok = ForEachRow(p, (y, row) =>
        {
            for (var dy = 0; dy < edge; dy++)
            {
                if (!cells.RowIn(dy, y))
                {
                    continue;
                }

                for (var dx = 0; dx < edge; dx++)
                {
                    for (var x = cells.X0(dx); x < cells.X1(dx); x++)
                    {
                        if (!WritePixel(pixel, row, x * p.Channels, p))
                        {
                            return false;
                        }

                        cells.Add((dy * edge) + dx, pixel);
                    }
                }
            }

            return true;
        });
        return ok ? cells.Image() : null;
    }

    // inflates and unfilters one scanline at a time (the filters need only the row above)
    private static bool ForEachRow(Chunks p, Func<int, byte[], bool> row)
    {
        var stride = checked(p.Width * p.Channels);
        var current = new byte[stride + 1];
        var previous = new byte[stride + 1];
        var pixels = new byte[stride];
        p.Idat.Position = 0;
        using var z = new ZLibStream(p.Idat, CompressionMode.Decompress);
        for (var y = 0; y < p.Height; y++)
        {
            z.ReadExactly(current);
            UnfilterRow(current, previous, y > 0, p.Channels);
            current.AsSpan(1).CopyTo(pixels);
            if (!row(y, pixels))
            {
                return false;
            }

            (current, previous) = (previous, current);
        }

        return true;
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

    private static bool WritePixel(Span<byte> d, byte[] row, int s, Chunks p)
    {
        switch (p.ColorType)
        {
            case 0:
                d[0] = d[1] = d[2] = row[s];
                d[3] = 255;
                return true;
            case 2:
                d[0] = row[s];
                d[1] = row[s + 1];
                d[2] = row[s + 2];
                d[3] = 255;
                return true;
            case 3:
                var i = row[s] * 3;
                if (p.Palette is null || i + 2 >= p.Palette.Length)
                {
                    return false;
                }

                d[0] = p.Palette[i];
                d[1] = p.Palette[i + 1];
                d[2] = p.Palette[i + 2];
                d[3] = 255;
                return true;
            case 4:
                d[0] = d[1] = d[2] = row[s];
                d[3] = row[s + 1];
                return true;
            default:
                d[0] = row[s];
                d[1] = row[s + 1];
                d[2] = row[s + 2];
                d[3] = row[s + 3];
                return true;
        }
    }

    // row[0] is the filter type; above is the previous unfiltered row (all zero for the first)
    private static void UnfilterRow(byte[] row, byte[] above, bool hasAbove, int bpp)
    {
        var filter = row[0];
        for (var i = 1; i < row.Length; i++)
        {
            int a = i > bpp ? row[i - bpp] : 0;
            int b = hasAbove ? above[i] : 0;
            int c = hasAbove && i > bpp ? above[i - bpp] : 0;
            var add = filter switch
            {
                0 => 0,
                1 => a,
                2 => b,
                3 => (a + b) / 2,
                4 => Paeth(a, b, c),
                _ => throw new InvalidDataException("bad PNG filter"),
            };
            row[i] = unchecked((byte)(row[i] + add));
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

    private sealed record Chunks(int Width, int Height, int ColorType, byte[]? Palette, MemoryStream Idat)
    {
        public int Channels => ColorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
    }
}
