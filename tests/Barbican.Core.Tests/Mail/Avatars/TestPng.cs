// Test helper: a solid-colour PNG (8-bit RGBA, no interlace) written without an imaging library, for the avatar raster tests.

using System.Buffers.Binary;
using System.IO.Compression;

namespace Barbican.Core.Tests.Mail.Avatars;

internal static class TestPng
{
    public static byte[] Solid(int width, int height, byte r, byte g, byte b, byte a)
    {
        var raw = new byte[height * (1 + (width * 4))];
        for (var y = 0; y < height; y++)
        {
            var row = y * (1 + (width * 4));
            for (var x = 0; x < width; x++)
            {
                var i = row + 1 + (x * 4);
                raw[i] = r;
                raw[i + 1] = g;
                raw[i + 2] = b;
                raw[i + 3] = a;
            }
        }

        using var z = new MemoryStream();
        using (var zl = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true))
        {
            zl.Write(raw);
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        Chunk(png, "IHDR", ihdr);
        Chunk(png, "IDAT", z.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var typed = new byte[4 + data.Length];
        for (var i = 0; i < 4; i++)
        {
            typed[i] = (byte)type[i];
        }

        data.CopyTo(typed, 4);
        s.Write(typed);
        BinaryPrimitives.WriteUInt32BigEndian(len, TestCrc32.Hash(typed));
        s.Write(len);
    }
}
