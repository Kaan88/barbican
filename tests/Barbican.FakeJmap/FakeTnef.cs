// A synthetic TNEF container (a winmail.dat, MS-OXTNEF section 2): the signature, a key, and for each file the attachment attributes attAttachRenddata, attAttachTitle and
// attAttachData, each with its checksum (the 16-bit sum of the data bytes). Built here, never taken from a real message; M2 seed (G-047, stream unified reads it back).

using System.Buffers.Binary;
using System.Text;

namespace Barbican.FakeJmap;

/// <summary>Builds a TNEF file from named byte arrays.</summary>
public static class FakeTnef
{
    private const uint Signature = 0x223E9F78;
    private const uint AttachRendData = 0x00069002;
    private const uint AttachTitle = 0x00018010;
    private const uint AttachData = 0x0006800F;
    private const byte LevelAttachment = 2;

    /// <summary>The bytes of a TNEF stream holding the given files.</summary>
    public static byte[] Build(IReadOnlyList<(string Name, byte[] Data)> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        using var s = new MemoryStream();
        Span<byte> head = stackalloc byte[6];
        BinaryPrimitives.WriteUInt32LittleEndian(head, Signature);
        BinaryPrimitives.WriteUInt16LittleEndian(head[4..], 0x1234);
        s.Write(head);
        foreach (var (name, data) in files)
        {
            // the attachment boundary: a RenddData attribute of 14 zero bytes (attach type, position, size) starts each file
            Attribute(s, AttachRendData, new byte[14]);
            Attribute(s, AttachTitle, [.. Encoding.ASCII.GetBytes(name), 0]);
            Attribute(s, AttachData, data);
        }

        return s.ToArray();
    }

    private static void Attribute(Stream s, uint id, byte[] data)
    {
        Span<byte> h = stackalloc byte[9];
        h[0] = LevelAttachment;
        BinaryPrimitives.WriteUInt32LittleEndian(h[1..], id);
        BinaryPrimitives.WriteUInt32LittleEndian(h[5..], (uint)data.Length);
        s.Write(h);
        s.Write(data);
        ushort sum = 0;
        foreach (var b in data)
        {
            sum = (ushort)(sum + b);
        }

        Span<byte> c = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(c, sum);
        s.Write(c);
    }
}
