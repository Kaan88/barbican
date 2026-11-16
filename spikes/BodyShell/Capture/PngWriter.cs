using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Barbican.Spike.BodyShell.Capture;

/// <summary>A minimal PNG encoder (RGBA8, no filtering) so saved frames need no imaging library on either OS.</summary>
internal static class PngWriter
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static void Save(CapturedFrame f, string path)
    {
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[1 + (f.Width * 4)];
            for (var y = 0; y < f.Height; y++)
            {
                row[0] = 0;
                var o = y * f.Stride;
                for (var x = 0; x < f.Width; x++)
                {
                    var s = o + (x * 4);
                    var d = 1 + (x * 4);
                    row[d] = f.Bgra[s + 2];
                    row[d + 1] = f.Bgra[s + 1];
                    row[d + 2] = f.Bgra[s];
                    row[d + 3] = 255;
                }

                z.Write(row);
            }
        }

        using var file = File.Create(path);
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, f.Width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), f.Height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        Chunk(file, "IHDR", ihdr);
        Chunk(file, "IDAT", raw.ToArray());
        Chunk(file, "IEND", []);
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var t = Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        var crc = 0xFFFFFFFFu;
        foreach (var b in t)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        BinaryPrimitives.WriteUInt32BigEndian(len, crc ^ 0xFFFFFFFFu);
        s.Write(len);
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
