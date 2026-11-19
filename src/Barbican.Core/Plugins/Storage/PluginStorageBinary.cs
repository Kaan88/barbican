// Source: Bulwark lib/plugin-sandbox/host-api.ts:407-447; UTF-16 snapshot envelope is host storage only.
using System.Collections.Immutable;

namespace Barbican.Core.Plugins.Persistence;

internal static class PluginStorageBinary
{
    private const int Limit = 4 * 1024 * 1024;
    public static byte[] Encode(Dictionary<string, string> values)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(values.Count);
        foreach (var (key, value) in values)
        {
            Write(writer, key);
            Write(writer, value);
            if (stream.Length > Limit) throw new InvalidDataException();
        }
        return stream.ToArray();
    }
    public static Dictionary<string, string> Decode(ImmutableArray<byte> bytes)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (bytes.IsDefaultOrEmpty) return result;
        if (bytes.Length > Limit) throw new InvalidDataException();
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        using var reader = new BinaryReader(stream);
        var count = reader.ReadInt32();
        if (count is < 0 or > 65536) throw new InvalidDataException();
        for (var i = 0; i < count; i++)
        {
            var key = Read(reader);
            var value = Read(reader);
            if (key.Length is 0 or > 256 || !result.TryAdd(key, value)) throw new InvalidDataException();
        }
        if (stream.Position != stream.Length) throw new InvalidDataException();
        return result;
    }
    private static void Write(BinaryWriter writer, string value)
    {
        writer.Write(value.Length);
        foreach (var c in value) writer.Write((ushort)c);
    }
    private static string Read(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length < 0 || length > Limit / 2 || length * 2L > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException();
        var chars = new char[length];
        for (var i = 0; i < length; i++) chars[i] = (char)reader.ReadUInt16();
        return new string(chars);
    }
}
