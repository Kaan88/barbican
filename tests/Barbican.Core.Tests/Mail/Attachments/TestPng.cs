using System.Buffers.Binary;
using System.IO.Compression;

namespace Barbican.Core.Tests.Mail.Attachments;

/// <summary>Writes a tiny solid-colour PNG without an imaging library (the Rust test used the image crate).</summary>
internal static class TestPng
{
    public static byte[] Solid(int width, int height, byte r, byte g, byte b, byte a)
    {
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0); // filter: none
            for (var x = 0; x < width; x++)
            {
                raw.WriteByte(r);
                raw.WriteByte(g);
                raw.WriteByte(b);
                raw.WriteByte(a);
            }
        }

        using var idat = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(raw.ToArray());
        }

        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), (uint)height);
        header[8] = 8; // bit depth
        header[9] = 6; // colour type: RGBA

        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", idat.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>A PNG of the given colour type whose scanlines carry <paramref name="filtered"/> as is (filter bytes and all).</summary>
    public static byte[] Raw(int width, int height, byte colorType, byte[] filtered, byte[]? palette = null)
    {
        using var idat = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionLevel.Fastest, leaveOpen: true))
        {
            z.Write(filtered);
        }

        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), (uint)height);
        header[8] = 8;
        header[9] = colorType;

        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Chunk(png, "IHDR", header);
        if (palette is not null)
        {
            Chunk(png, "PLTE", palette);
        }

        Chunk(png, "IDAT", idat.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream to, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        to.Write(length);
        var body = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type, body);
        data.CopyTo(body, 4);
        to.Write(body);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(body));
        to.Write(crc);
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
