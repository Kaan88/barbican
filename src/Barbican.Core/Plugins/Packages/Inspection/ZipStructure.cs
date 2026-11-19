// Source: Bulwark lib/extension-package.ts:54-96; local/central consistency, overlap, ZIP64 and multi-disk checks are host-added restrictions.
using System.Buffers.Binary;

namespace Barbican.Core.Plugins.Packages.Inspection;

internal static class ZipStructure
{
    public static void Validate(ReadOnlySpan<byte> data)
    {
        var end = FindEnd(data);
        if (U16(data, end + 4) != 0 || U16(data, end + 6) != 0) throw new InvalidDataException();
        var count = U16(data, end + 10);
        if (count != U16(data, end + 8) || count > 200) throw new InvalidDataException();
        var centralLength = U32(data, end + 12);
        var centralOffset = U32(data, end + 16);
        if (centralLength == uint.MaxValue || centralOffset == uint.MaxValue || centralOffset + (long)centralLength != end) throw new InvalidDataException();
        var pos = checked((int)centralOffset);
        var ranges = new List<(int Start, long End)>();
        for (var i = 0; i < count; i++) ranges.Add(ReadEntry(data, ref pos, centralOffset));
        if (pos != end) throw new InvalidDataException();
        ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
        for (var i = 1; i < ranges.Count; i++) if (ranges[i].Start < ranges[i - 1].End) throw new InvalidDataException();
    }

    private static (int Start, long End) ReadEntry(ReadOnlySpan<byte> data, ref int pos, uint centralOffset)
    {
        Require(data, pos, 46);
        if (U32(data, pos) != 0x02014b50) throw new InvalidDataException();
        var flags = U16(data, pos + 8);
        var method = U16(data, pos + 10);
        var crc = U32(data, pos + 16);
        var compressed = U32(data, pos + 20);
        var unpacked = U32(data, pos + 24);
        var nameLength = U16(data, pos + 28);
        var extraLength = U16(data, pos + 30);
        var commentLength = U16(data, pos + 32);
        var disk = U16(data, pos + 34);
        var local = U32(data, pos + 42);
        if ((flags & 1) != 0 || disk != 0 || local == uint.MaxValue || compressed == uint.MaxValue || unpacked == uint.MaxValue) throw new InvalidDataException();
        Require(data, pos + 46, nameLength + extraLength + commentLength);
        var localPos = checked((int)local);
        Require(data, localPos, 30);
        if (U32(data, localPos) != 0x04034b50 || U16(data, localPos + 6) != flags || U16(data, localPos + 8) != method) throw new InvalidDataException();
        var localName = U16(data, localPos + 26);
        var localExtra = U16(data, localPos + 28);
        if (nameLength != localName) throw new InvalidDataException();
        Require(data, localPos + 30, localName + localExtra);
        if (!data.Slice(pos + 46, nameLength).SequenceEqual(data.Slice(localPos + 30, localName))) throw new InvalidDataException();
        if ((flags & 8) == 0 && (U32(data, localPos + 14) != crc || U32(data, localPos + 18) != compressed || U32(data, localPos + 22) != unpacked)) throw new InvalidDataException();
        var dataEnd = (long)localPos + 30 + localName + localExtra + compressed;
        if (dataEnd > centralOffset) throw new InvalidDataException();
        if ((flags & 8) != 0) dataEnd = DescriptorEnd(data, checked((int)dataEnd), crc, compressed, unpacked, centralOffset);
        pos = checked(pos + 46 + nameLength + extraLength + commentLength);
        return (localPos, dataEnd);
    }

    private static long DescriptorEnd(ReadOnlySpan<byte> data, int pos, uint crc, uint compressed, uint unpacked, uint centralOffset)
    {
        Require(data, pos, 12);
        if (U32(data, pos) == 0x08074b50) { pos = checked(pos + 4); Require(data, pos, 12); }
        if (U32(data, pos) != crc || U32(data, pos + 4) != compressed || U32(data, pos + 8) != unpacked || (long)pos + 12 > centralOffset)
            throw new InvalidDataException();
        return (long)pos + 12;
    }

    private static int FindEnd(ReadOnlySpan<byte> data)
    {
        for (var i = data.Length - 22; i >= Math.Max(0, data.Length - 65557); i--)
            if (U32(data, i) == 0x06054b50 && i + 22 + U16(data, i + 20) == data.Length) return i;
        throw new InvalidDataException();
    }

    private static ushort U16(ReadOnlySpan<byte> data, int offset) { Require(data, offset, 2); return BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]); }
    private static uint U32(ReadOnlySpan<byte> data, int offset) { Require(data, offset, 4); return BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]); }
    private static void Require(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset < 0 || length < 0 || (long)offset + length > data.Length) throw new InvalidDataException();
    }
}
