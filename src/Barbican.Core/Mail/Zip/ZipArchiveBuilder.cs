// Mirrors: Rust zip.rs:14 dos_time, :23 unique_names, :37 build (stored entries, UTF-8 names flag 0x0800, CRC-32, central directory, end record);
// PKZIP APPNOTE 4.3 (4.3.7 local header, 4.3.12 central directory, 4.3.16 end record, 4.4.4 bit 11); Bulwark lib/files-zip.ts (JSZip STORE),
// lib/download-filename.ts createUniqueNamer (Save all as zip). No ZIP64: under 4 GB and 65535 entries. Owner reader.

using System.Buffers.Binary;
using System.Text;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.Mail.Zip;

/// <summary>Builds a plain .zip in memory (no compression, no dependency).</summary>
public static class ZipArchiveBuilder
{
    private static readonly uint[] CrcTable = MakeCrcTable();

    /// <summary>DOS (time, date) words of a moment: 2-second resolution, years 1980 to 2107.</summary>
    public static (ushort Time, ushort Date) DosTime(DateTime when)
    {
        var y = Math.Clamp(when.Year, 1980, 2107);
        var date = (ushort)(((y - 1980) << 9) | (when.Month << 5) | when.Day);
        var time = (ushort)((when.Hour << 11) | (when.Minute << 5) | (when.Second / 2));
        return (time, date);
    }

    /// <summary>Names made unique in order: the second "a.pdf" becomes "a (1).pdf" (case-insensitive), an empty name becomes the fallback.</summary>
    public static IReadOnlyList<string> UniqueNames(IReadOnlyList<string> names, string fallback)
    {
        ArgumentNullException.ThrowIfNull(names);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var output = new List<string>(names.Count);
        foreach (var raw in names)
        {
            var b = raw.Trim().Length == 0 ? fallback : raw;
            var dot = b.LastIndexOf('.');
            var (stem, ext) = dot > 0 ? (b[..dot], b[dot..]) : (b, string.Empty);
            var n = 0;
            var name = b;
            while (used.Contains(name.ToLowerInvariant()))
            {
                n++;
                name = $"{stem} ({n}){ext}";
            }

            used.Add(name.ToLowerInvariant());
            output.Add(name);
        }

        return output;
    }

    /// <summary>The archive's bytes; more than 65535 files, a file over 4 GB or an archive over 4 GB is refused.</summary>
    public static Result<byte[]> Build(IReadOnlyList<ZipEntryData> entries, DateTime when)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count > 0xFFFF)
        {
            return Fail("Too many files for one archive.");
        }

        var (time, date) = DosTime(when);
        using var output = new MemoryStream();
        using var central = new MemoryStream();
        foreach (var e in entries)
        {
            var name = Encoding.UTF8.GetBytes(e.Name.Replace('\\', '/').TrimStart('/'));
            if (name.Length > 0xFFFF)
            {
                return Fail("A file name is too long for the archive.");
            }

            if (output.Length > uint.MaxValue)
            {
                return Fail("The archive would be larger than 4 GB.");
            }

            var offset = (uint)output.Length;
            var size = (uint)e.Data.Length;
            var crc = Crc32(e.Data);

            // local file header
            U32(output, 0x0403_4b50);
            U16(output, 20, 0x0800, 0, time, date);
            U32(output, crc, size, size);
            U16(output, (ushort)name.Length, 0);
            output.Write(name);
            output.Write(e.Data);

            // central directory record
            U32(central, 0x0201_4b50);
            U16(central, 20, 20, 0x0800, 0, time, date);
            U32(central, crc, size, size);
            U16(central, (ushort)name.Length, 0, 0, 0, 0);
            U32(central, 0, offset);
            central.Write(name);
        }

        if (output.Length > uint.MaxValue)
        {
            return Fail("The archive would be larger than 4 GB.");
        }

        var cdOffset = (uint)output.Length;
        var cdSize = (uint)central.Length;
        central.Position = 0;
        central.CopyTo(output);
        U32(output, 0x0605_4b50);
        U16(output, 0, 0, (ushort)entries.Count, (ushort)entries.Count);
        U32(output, cdSize, cdOffset);
        U16(output, 0);
        return Result<byte[]>.Ok(output.ToArray());
    }

    /// <summary>CRC-32 (IEEE 802.3, reflected, as zlib and crc32fast).</summary>
    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        var c = 0xFFFF_FFFFu;
        foreach (var b in data)
        {
            c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        }

        return c ^ 0xFFFF_FFFFu;
    }

    private static Result<byte[]> Fail(string text) => Result<byte[]>.Fail(new ValidationError(text));

    private static void U16(MemoryStream s, params ReadOnlySpan<ushort> values)
    {
        Span<byte> b = stackalloc byte[2];
        foreach (var v in values)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(b, v);
            s.Write(b);
        }
    }

    private static void U32(MemoryStream s, params ReadOnlySpan<uint> values)
    {
        Span<byte> b = stackalloc byte[4];
        foreach (var v in values)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(b, v);
            s.Write(b);
        }
    }

    private static uint[] MakeCrcTable()
    {
        var t = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB8_8320u ^ (c >> 1) : c >> 1;
            }

            t[n] = c;
        }

        return t;
    }
}
