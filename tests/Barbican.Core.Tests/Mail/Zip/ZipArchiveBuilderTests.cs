// Ported from Rust src/zip.rs tests (:98-:146).
// The CRC-32 reference is the independent TestCrc32 (the Rust test used the crc32fast crate).

using System.Buffers.Binary;
using System.Text;
using Barbican.Core.Mail.Zip;
using Barbican.Core.Tests.Mail.Avatars;

namespace Barbican.Core.Tests.Mail.Zip;

public sealed class ZipArchiveBuilderTests
{
    private static int Rd16(byte[] b, int i) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(i));

    private static uint Rd32(byte[] b, int i) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(i));

    [Fact]
    [RustTest("zip::names_are_unique")]
    public void Names_are_unique()
    {
        var n = ZipArchiveBuilder.UniqueNames(["a.pdf", "A.pdf", "a.pdf", string.Empty, "notes", "notes"], "attachment");
        Assert.Equal(["a.pdf", "A (1).pdf", "a (2).pdf", "attachment", "notes", "notes (1)"], n);
    }

    [Fact]
    [RustTest("zip::archive_layout_reads_back")]
    public void Archive_layout_reads_back()
    {
        var when = new DateTime(2026, 11, 16, 13, 45, 31, DateTimeKind.Unspecified);
        var built = ZipArchiveBuilder.Build(
            [new ZipEntryData("hello.txt", Encoding.UTF8.GetBytes("hello world")), new ZipEntryData("ünï.bin", [0, 1, 2])],
            when);
        Assert.True(built.IsOk);
        var z = built.Value!;

        // end of central directory
        var eocd = z.Length - 22;
        Assert.Equal(0x0605_4b50u, Rd32(z, eocd));
        Assert.Equal(2, Rd16(z, eocd + 10));
        var cdSize = (int)Rd32(z, eocd + 12);
        var cdOff = (int)Rd32(z, eocd + 16);
        Assert.Equal(eocd, cdOff + cdSize);

        // walk the central directory and check each local entry against it
        var p = cdOff;
        var names = new List<(string Name, byte[] Data)>();
        while (p < eocd)
        {
            Assert.Equal(0x0201_4b50u, Rd32(z, p));
            Assert.Equal(0x0800, Rd16(z, p + 8) & 0x0800);
            var crc = Rd32(z, p + 16);
            var size = (int)Rd32(z, p + 24);
            var nlen = Rd16(z, p + 28);
            var local = (int)Rd32(z, p + 42);
            var name = Encoding.UTF8.GetString(z, p + 46, nlen);
            Assert.Equal(0x0403_4b50u, Rd32(z, local));
            var data = z.AsSpan(local + 30 + nlen, size).ToArray();
            Assert.Equal(crc, TestCrc32.Hash(data));
            names.Add((name, data));
            p += 46 + nlen;
        }

        Assert.Equal("hello.txt", names[0].Name);
        Assert.Equal(Encoding.UTF8.GetBytes("hello world"), names[0].Data);
        Assert.Equal("ünï.bin", names[1].Name);

        // CRC-32 of "hello world"
        Assert.Equal(0x0d4a_1185u, TestCrc32.Hash(Encoding.ASCII.GetBytes("hello world")));
        var (t, d) = ZipArchiveBuilder.DosTime(when);
        Assert.Equal((46 << 9) | (11 << 5) | 16, d);
        Assert.Equal((13 << 11) | (45 << 5) | 15, t);
    }
}
