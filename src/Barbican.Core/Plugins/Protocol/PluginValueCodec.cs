#pragma warning disable MA0051 // Codec and validator are deliberately kept together for protocol audit.
using System.Globalization;
using System.Text.Json;

namespace Barbican.Core.Plugins.Protocol;

/// <summary>Versioned tagged graph encoding; it never interprets ordinary object properties as control markers.</summary>
public static class PluginValueCodec
{
    private static readonly string[] Fields = ["id", "kind", "text", "flag", "bytes", "links", "keys", "view", "offset", "length"];

    public static byte[] Encode(PluginValue value, PluginValueLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        var policy = limits ?? new();
        Validate(value.Nodes, value.Id, policy);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject(); writer.WriteNumber("version", 1); writer.WriteNumber("root", value.Id); writer.WriteStartArray("nodes");
            for (var i = 0; i < value.Nodes.Length; i++)
            {
                var n = value.Nodes[i];
                writer.WriteStartObject(); writer.WriteNumber("id", i); writer.WriteString("kind", n.Kind.ToString());
                writer.WritePropertyName("text"); WriteString(writer, n.Text);
                writer.WriteBoolean("flag", n.Flag);
                if (n.Bytes is null) writer.WriteNull("bytes"); else writer.WriteBase64String("bytes", n.Bytes);
                if (n.Links is null) writer.WriteNull("links"); else { writer.WriteStartArray("links"); foreach (var link in n.Links) writer.WriteNumberValue(link); writer.WriteEndArray(); }
                if (n.Keys is null) writer.WriteNull("keys"); else { writer.WriteStartArray("keys"); foreach (var key in n.Keys) WriteString(writer, key); writer.WriteEndArray(); }
                writer.WriteString("view", n.View.ToString()); writer.WriteNumber("offset", n.Offset); writer.WriteNumber("length", n.Length); writer.WriteEndObject();
                writer.Flush();
                if (stream.Length > policy.MaxWireBytes) throw new PluginValueFormatException("Encoded message exceeds limit.");
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        if (stream.Length > policy.MaxWireBytes) throw new PluginValueFormatException("Encoded message exceeds limit.");
        return stream.ToArray();
    }

    public static PluginValue Decode(ReadOnlyMemory<byte> utf8, PluginValueLimits? limits = null)
    {
        var p = limits ?? new();
        CheckLimits(p);
        if (utf8.Length > p.MaxWireBytes) throw new PluginValueFormatException("Encoded message exceeds limit.");
        try
        {
            using var doc = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
            var root = doc.RootElement;
            Exact(root, ["version", "root", "nodes"]);
            if (root.GetProperty("version").GetInt32() != 1) throw new PluginValueFormatException("Unsupported version.");
            var rootId = root.GetProperty("root").GetInt32();
            var list = root.GetProperty("nodes");
            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() > p.MaxNodes) throw new PluginValueFormatException("Node limit exceeded.");
            var nodes = new PluginNode?[list.GetArrayLength()];
            long binaryBytes = 0, stringBytes = 0;
            foreach (var item in list.EnumerateArray())
            {
                Exact(item, Fields);
                var id = item.GetProperty("id").GetInt32();
                if (id < 0 || id >= nodes.Length || nodes[id] is not null) throw new PluginValueFormatException("Duplicate or invalid node id.");
                var kindName = item.GetProperty("kind").GetString();
                if (!Enum.TryParse<PluginValueKind>(kindName, false, out var kind) || !Enum.IsDefined(kind) || !string.Equals(kind.ToString(), kindName, StringComparison.Ordinal)) throw new PluginValueFormatException("Unknown node kind.");
                var viewName = item.GetProperty("view").GetString();
                if (!Enum.TryParse<PluginViewType>(viewName, false, out var view) || !Enum.IsDefined(view) || !string.Equals(view.ToString(), viewName, StringComparison.Ordinal)) throw new PluginValueFormatException("Unknown view kind.");
                var text = ReadString(item.GetProperty("text"), p, ref stringBytes);
                var rawBytes = item.GetProperty("bytes");
                byte[]? bytes = null;
                if (rawBytes.ValueKind != JsonValueKind.Null)
                {
                    var encoded = rawBytes.GetString() ?? throw new PluginValueFormatException("Invalid base64.");
                    if (encoded.Length % 4 != 0 || ((long)encoded.Length / 4 * 3) > (long)p.MaxBinaryBytes + 2) throw new PluginValueFormatException("Binary limit exceeded.");
                    var padding = encoded.EndsWith("==", StringComparison.Ordinal) ? 2 : encoded.EndsWith('=') ? 1 : 0;
                    var decodedLength = ((long)encoded.Length / 4 * 3) - padding;
                    binaryBytes += decodedLength;
                    if (binaryBytes > p.MaxBinaryBytes) throw new PluginValueFormatException("Binary limit exceeded.");
                    bytes = Convert.FromBase64String(encoded);
                    if (bytes.Length != decodedLength || !string.Equals(Convert.ToBase64String(bytes), encoded, StringComparison.Ordinal)) throw new PluginValueFormatException("Invalid base64.");
                }
                var links = Ints(item.GetProperty("links"), p.MaxSequenceLength * 2L);
                var keys = Strings(item.GetProperty("keys"), p, ref stringBytes);
                nodes[id] = new(kind, text, item.GetProperty("flag").GetBoolean(), bytes, links, keys, view, item.GetProperty("offset").GetInt32(), item.GetProperty("length").GetInt32());
            }
            if (nodes.Any(x => x is null)) throw new PluginValueFormatException("Missing node id.");
            var graph = nodes.Select(x => x!).ToArray();
            Validate(graph, rootId, p);
            return new PluginValue(graph, rootId, p);
        }
        catch (PluginValueFormatException) { throw; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or OverflowException or ArgumentException)
        { throw new PluginValueFormatException("Malformed plugin value.", ex); }
    }

    private static void Exact(JsonElement obj, string[] expected)
    {
        if (obj.ValueKind != JsonValueKind.Object) throw new PluginValueFormatException("Object required.");
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in obj.EnumerateObject()) if (!expected.Contains(property.Name, StringComparer.Ordinal) || !found.Add(property.Name)) throw new PluginValueFormatException("Unexpected or duplicate field.");
        if (found.Count != expected.Length) throw new PluginValueFormatException("Missing field.");
    }
    // Well-formed strings use ordinary JSON. Lone UTF-16 code units use a tagged, lossless little-endian payload.
    internal static int JsonStringLength(ReadOnlySpan<char> raw)
    {
        var count = 0;
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '\\') { i++; if (raw[i] == 'u') i += 4; }
            count++;
        }
        return count;
    }
    private static void WriteString(Utf8JsonWriter writer, string? value)
    {
        if (value is null) { writer.WriteNullValue(); return; }
        if (WellFormed(value)) { writer.WriteStringValue(value); return; }
        var bytes = new byte[checked(value.Length * 2)];
        for (var i = 0; i < value.Length; i++) { bytes[i * 2] = (byte)value[i]; bytes[(i * 2) + 1] = (byte)(value[i] >> 8); }
        writer.WriteStartObject(); writer.WriteBase64String("utf16", bytes); writer.WriteEndObject();
    }
    private static string? ReadString(JsonElement value, PluginValueLimits p, ref long total)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.String)
        {
            var raw = value.GetRawText();
            total += (long)JsonStringLength(raw.AsSpan(1, raw.Length - 2)) * 2;
            if (total > p.MaxStringBytes) throw new PluginValueFormatException("String budget exceeded.");
            return value.GetString()!;
        }
        Exact(value, ["utf16"]);
        var encoded = value.GetProperty("utf16").GetString() ?? throw new PluginValueFormatException("Invalid UTF-16 tag.");
        if (encoded.Length % 4 != 0 || ((long)encoded.Length / 4 * 3) > (long)p.MaxStringBytes + 2) throw new PluginValueFormatException("String budget exceeded.");
        var padding = encoded.EndsWith("==", StringComparison.Ordinal) ? 2 : encoded.EndsWith('=') ? 1 : 0;
        var length = ((long)encoded.Length / 4 * 3) - padding;
        total += length;
        if (length % 2 != 0 || total > p.MaxStringBytes) throw new PluginValueFormatException("Invalid UTF-16 byte count.");
        var bytes = Convert.FromBase64String(encoded);
        if (bytes.Length != length || !string.Equals(Convert.ToBase64String(bytes), encoded, StringComparison.Ordinal)) throw new PluginValueFormatException("Invalid UTF-16 encoding.");
        var chars = new char[bytes.Length / 2];
        for (var i = 0; i < chars.Length; i++) chars[i] = (char)(bytes[i * 2] | (bytes[(i * 2) + 1] << 8));
        return new string(chars);
    }
    private static int[]? Ints(JsonElement value, long cap)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > cap) throw new PluginValueFormatException("Sequence limit exceeded.");
        return [.. value.EnumerateArray().Select(x => x.GetInt32())];
    }
    private static string[]? Strings(JsonElement value, PluginValueLimits p, ref long total)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > p.MaxSequenceLength) throw new PluginValueFormatException("Sequence limit exceeded.");
        var result = new string[value.GetArrayLength()];
        var i = 0;
        foreach (var item in value.EnumerateArray()) result[i++] = ReadString(item, p, ref total) ?? throw new PluginValueFormatException("Null key.");
        return result;
    }
    private static void CheckLimits(PluginValueLimits p)
    {
        if (p.MaxWireBytes < 1 || p.MaxBinaryBytes < 0 || p.MaxNodes < 1 || p.MaxDepth < 1 || p.MaxDepth > 256 || p.MaxStringBytes < 0 || p.MaxSequenceLength < 0 || p.MaxBigIntDigits < 1) throw new ArgumentOutOfRangeException(nameof(p));
    }
    internal static void Validate(PluginNode[] nodes, int root, PluginValueLimits p)
    {
        CheckLimits(p);
        if (nodes.Length > p.MaxNodes || root < 0 || root >= nodes.Length) throw new PluginValueFormatException("Invalid graph root or node count.");
        long binary = 0, strings = 0;
        foreach (var n in nodes)
        {
            if (!Enum.IsDefined(n.Kind) || !Enum.IsDefined(n.View)) throw new PluginValueFormatException("Unknown kind.");
            if (n.Bytes is not null) binary += n.Bytes.Length;
            if (n.Text is not null) strings += (long)n.Text.Length * 2;
            if (n.Keys is not null) foreach (var key in n.Keys) strings += key is null ? throw new PluginValueFormatException("Invalid key.") : (long)key.Length * 2;
            if (binary > p.MaxBinaryBytes || strings > p.MaxStringBytes) throw new PluginValueFormatException("Graph byte budget exceeded.");
            var links = n.Links ?? [];
            if (links.LongLength > p.MaxSequenceLength * 2L || links.Any(x => x < 0 || x >= nodes.Length)) throw new PluginValueFormatException("Invalid graph references.");
            var ok = n.Kind switch
            {
                PluginValueKind.Undefined or PluginValueKind.Null => n.Text is null && n.Bytes is null && n.Links is null && n.Keys is null && !n.Flag,
                PluginValueKind.Boolean => n.Text is null && n.Bytes is null && n.Links is null && n.Keys is null,
                PluginValueKind.Number or PluginValueKind.Date => n.Text is not null && double.TryParse(n.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out _) && n.Bytes is null && n.Links is null && n.Keys is null && !n.Flag,
                PluginValueKind.BigInt => n.Text is not null && n.Text.Length <= p.MaxBigIntDigits && System.Numerics.BigInteger.TryParse(n.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _) && n.Bytes is null && n.Links is null && n.Keys is null && !n.Flag,
                PluginValueKind.String => n.Text is not null && n.Bytes is null && n.Links is null && n.Keys is null && !n.Flag,
                PluginValueKind.ArrayBuffer => n.Bytes is not null && n.Text is null && n.Links is null && n.Keys is null && !n.Flag,
                PluginValueKind.Blob => n.Bytes is not null && n.Text is not null && n.Links is null && n.Keys is null && !n.Flag,
                PluginValueKind.View => n.Bytes is null && n.Text is null && n.Keys is null && links.Length == 1 && nodes[links[0]].Kind == PluginValueKind.ArrayBuffer && n.Offset >= 0 && n.Length >= 0 && (long)n.Offset + n.Length <= (nodes[links[0]].Bytes?.Length ?? -1) && Aligned(n.View, n.Offset, n.Length) && !n.Flag,
                PluginValueKind.Object => n.Bytes is null && n.Text is null && n.Keys is not null && n.Links is not null && n.Keys.Length == links.Length && n.Keys.Distinct(StringComparer.Ordinal).Count() == n.Keys.Length && links.Length <= p.MaxSequenceLength && !n.Flag,
                PluginValueKind.Array or PluginValueKind.Set => n.Bytes is null && n.Text is null && n.Keys is null && n.Links is not null && links.Length <= p.MaxSequenceLength && !n.Flag,
                PluginValueKind.Map => n.Bytes is null && n.Text is null && n.Keys is null && n.Links is not null && links.Length % 2 == 0 && links.Length / 2 <= p.MaxSequenceLength && !n.Flag,
                PluginValueKind.Error => n.Bytes is null && n.Text is null && n.Keys is { Length: 3 } && n.Links is not null && links.Length <= 1,
                _ => false,
            };
            if (!ok || (n.Kind != PluginValueKind.View && (n.View != default || n.Offset != 0 || n.Length != 0))) throw new PluginValueFormatException("Invalid node shape.");
        }
        var path = new HashSet<int>();
        var deepest = new int[nodes.Length];
        var visits = 0L;
        void Walk(int id, int depth)
        {
            if (path.Contains(id)) return;
            if (depth > p.MaxDepth || ++visits > (long)p.MaxNodes * p.MaxDepth) throw new PluginValueFormatException("Graph depth or complexity exceeded.");
            if (depth <= deepest[id]) return;
            deepest[id] = depth;
            path.Add(id);
            foreach (var next in nodes[id].Links ?? []) Walk(next, depth + 1);
            path.Remove(id);
        }
        Walk(root, 1);
        foreach (var id in Enumerable.Range(0, nodes.Length)) if (deepest[id] == 0) Walk(id, 1);
    }
    private static bool WellFormed(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]))
            {
                if (++i >= value.Length || !char.IsLowSurrogate(value[i])) return false;
            }
            else if (char.IsLowSurrogate(value[i])) return false;
        }
        return true;
    }
    private static bool Aligned(PluginViewType view, int offset, int length)
    {
        var size = view switch { PluginViewType.Int16 or PluginViewType.Uint16 or PluginViewType.Float16 => 2, PluginViewType.Int32 or PluginViewType.Uint32 or PluginViewType.Float32 => 4, PluginViewType.Float64 or PluginViewType.BigInt64 or PluginViewType.BigUint64 => 8, _ => 1 };
        return offset % size == 0 && length % size == 0;
    }
}
