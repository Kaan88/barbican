// Mirrors: Bulwark lib/plugin-sandbox/protocol.ts:115-334 and runtime.tsx:368-407,646-666; the tagged v1 graph is a native transport adaptation.
using System.Text;
using System.Text.Json;

namespace Barbican.Core.Plugins.Protocol;

/// <summary>An immutable view into a private graph. Byte access returns a copy; no disposal is required.</summary>
public sealed class PluginValue
{
    private readonly PluginNode[] _nodes;
    private readonly int _id;
    private readonly PluginValueLimits _limits;
    internal PluginValue(PluginNode[] nodes, int id, PluginValueLimits limits) { _nodes = nodes; _id = id; _limits = limits; }
    internal PluginNode[] Nodes => _nodes;
    internal int Id => _id;
    private PluginNode Node => _nodes[_id];
    public PluginValueKind Kind => Node.Kind;
    public string StringValue => Kind == PluginValueKind.String ? Node.Text! : throw new InvalidOperationException("Not a string.");
    public bool BooleanValue => Kind == PluginValueKind.Boolean ? Node.Flag : throw new InvalidOperationException("Not a boolean.");
    public string NumberText => Kind is PluginValueKind.Number or PluginValueKind.Date or PluginValueKind.BigInt ? Node.Text! : throw new InvalidOperationException("Not a numeric value.");
    public int Count => Kind is PluginValueKind.Object or PluginValueKind.Array or PluginValueKind.Map or PluginValueKind.Set ? (Node.Links?.Length ?? 0) / (Kind == PluginValueKind.Map ? 2 : 1) : throw new InvalidOperationException("Not a collection.");
    public IReadOnlyList<string> PropertyNames => Kind == PluginValueKind.Object ? Array.AsReadOnly((string[])Node.Keys!.Clone()) : throw new InvalidOperationException("Not an object.");
    public PluginValue Property(string name)
    {
        if (Kind != PluginValueKind.Object) throw new InvalidOperationException("Not an object.");
        var index = Array.FindIndex(Node.Keys!, x => string.Equals(x, name, StringComparison.Ordinal));
        return index < 0 ? throw new KeyNotFoundException(name) : new(_nodes, Node.Links![index], _limits);
    }
    public PluginValue Item(int index) => Kind is PluginValueKind.Array or PluginValueKind.Set ? new(_nodes, Node.Links![index], _limits) : throw new InvalidOperationException("Not an array or set.");
    public (PluginValue Key, PluginValue Value) MapEntry(int index) => Kind == PluginValueKind.Map ? (new(_nodes, Node.Links![index * 2], _limits), new(_nodes, Node.Links![(index * 2) + 1], _limits)) : throw new InvalidOperationException("Not a map.");
    public PluginViewType ViewType => Kind == PluginValueKind.View ? Node.View : throw new InvalidOperationException("Not a view.");
    public int ByteOffset => Kind == PluginValueKind.View ? Node.Offset : throw new InvalidOperationException("Not a view.");
    public int ByteLength => Kind == PluginValueKind.View ? Node.Length : Kind is PluginValueKind.ArrayBuffer or PluginValueKind.Blob ? Node.Bytes!.Length : throw new InvalidOperationException("Not binary.");
    public string BlobType => Kind == PluginValueKind.Blob ? Node.Text! : throw new InvalidOperationException("Not a blob.");
    public PluginValue BackingBuffer => Kind == PluginValueKind.View ? new(_nodes, Node.Links![0], _limits) : throw new InvalidOperationException("Not a view.");
    public (string Name, string Message, string? Stack, PluginValue? Cause) ErrorValue => Kind == PluginValueKind.Error ? (Node.Keys![0], Node.Keys[1], Node.Flag ? Node.Keys[2] : null, Node.Links!.Length == 0 ? null : new(_nodes, Node.Links[0], _limits)) : throw new InvalidOperationException("Not an error.");
    public byte[] BinaryBytes() => Kind switch
    {
        PluginValueKind.ArrayBuffer or PluginValueKind.Blob => Node.Bytes!.ToArray(),
        PluginValueKind.View => _nodes[Node.Links![0]].Bytes!.AsSpan(Node.Offset, Node.Length).ToArray(),
        _ => throw new InvalidOperationException("Not binary."),
    };
    public bool SameNode(PluginValue other) => other is not null && ReferenceEquals(_nodes, other._nodes) && _id == other._id;
    public static PluginValue FromBinary(ReadOnlySpan<byte> bytes, PluginValueLimits? limits = null)
    {
        var policy = limits ?? new();
        if (bytes.Length > policy.MaxBinaryBytes) throw new PluginValueFormatException("Binary limit exceeded.");
        var builder = new PluginValueBuilder();
        return builder.Build(builder.Buffer(bytes), policy);
    }
    public static PluginValue FromJson(JsonElement element, PluginValueLimits? limits = null)
    {
        var policy = limits ?? new();
        var raw = element.GetRawText();
        if (Encoding.UTF8.GetByteCount(raw) > policy.MaxWireBytes) throw new PluginValueFormatException("JSON input exceeds limit.");
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(raw), new JsonReaderOptions { MaxDepth = 256 });
        var builder = new PluginValueBuilder();
        var count = 0;
        long strings = 0;
        if (!reader.Read()) throw new PluginValueFormatException("Empty JSON.");
        var root = ReadJson(ref reader, builder, policy, 1, ref count, ref strings);
        if (reader.Read()) throw new PluginValueFormatException("Trailing JSON.");
        return builder.Build(root, policy);
    }
    private static int ReadJson(ref Utf8JsonReader reader, PluginValueBuilder builder, PluginValueLimits p, int depth, ref int count, ref long strings)
    {
        if (depth > p.MaxDepth || ++count > p.MaxNodes) throw new PluginValueFormatException("JSON graph limit exceeded.");
        switch (reader.TokenType)
        {
            case JsonTokenType.Null: return builder.Null();
            case JsonTokenType.True: return builder.Boolean(true);
            case JsonTokenType.False: return builder.Boolean(false);
            case JsonTokenType.String: return builder.String(ReadJsonString(ref reader, p, ref strings));
            case JsonTokenType.Number: return builder.AddRaw(new(PluginValueKind.Number, Text: Encoding.UTF8.GetString(reader.ValueSpan)));
            case JsonTokenType.StartArray: return ReadJsonArray(ref reader, builder, p, depth, ref count, ref strings);
            case JsonTokenType.StartObject: return ReadJsonObject(ref reader, builder, p, depth, ref count, ref strings);
            default: throw new PluginValueFormatException("Unsupported JSON value.");
        }
    }
    private static int ReadJsonArray(ref Utf8JsonReader reader, PluginValueBuilder builder, PluginValueLimits p, int depth, ref int count, ref long strings)
    {
        var items = new List<int>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (items.Count >= p.MaxSequenceLength) throw new PluginValueFormatException("JSON sequence limit exceeded.");
            items.Add(ReadJson(ref reader, builder, p, depth + 1, ref count, ref strings));
        }
        if (reader.TokenType != JsonTokenType.EndArray) throw new PluginValueFormatException("Truncated array.");
        return builder.Array([.. items]);
    }
    private static int ReadJsonObject(ref Utf8JsonReader reader, PluginValueBuilder builder, PluginValueLimits p, int depth, ref int count, ref long strings)
    {
        var properties = new List<(string Key, int Value)>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName || properties.Count >= p.MaxSequenceLength) throw new PluginValueFormatException("Invalid JSON object.");
            var key = ReadJsonString(ref reader, p, ref strings);
            if (!reader.Read()) throw new PluginValueFormatException("Truncated object.");
            properties.Add((key, ReadJson(ref reader, builder, p, depth + 1, ref count, ref strings)));
        }
        if (reader.TokenType != JsonTokenType.EndObject) throw new PluginValueFormatException("Truncated object.");
        return builder.Object([.. properties]);
    }
    private static string ReadJsonString(ref Utf8JsonReader reader, PluginValueLimits p, ref long total)
    {
        var raw = Encoding.UTF8.GetString(reader.ValueSpan);
        var length = PluginValueCodec.JsonStringLength(raw);
        total += (long)length * 2;
        if (total > p.MaxStringBytes) throw new PluginValueFormatException("String budget exceeded.");
        var result = new StringBuilder(length);
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (c == '\\')
            {
                c = raw[++i];
                if (c == 'u')
                {
                    c = (char)int.Parse(raw.AsSpan(i + 1, 4), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
                    i += 4;
                }
                else c = c switch { '"' => '"', '\\' => '\\', '/' => '/', 'b' => '\b', 'f' => '\f', 'n' => '\n', 'r' => '\r', 't' => '\t', _ => throw new PluginValueFormatException("Invalid JSON escape.") };
            }
            result.Append(c);
        }
        return result.ToString();
    }
    /// <summary>Converts only acyclic JSON-compatible values; aliases expand within the output budget.</summary>
#pragma warning disable MA0051 // JSON emission keeps bounded quoting and cycle detection in one scope.
    public JsonElement ToJson()
    {
        var output = new StringBuilder();
        var bytes = 0;
        void Add(string part)
        {
            bytes = checked(bytes + Encoding.UTF8.GetByteCount(part));
            if (bytes > _limits.MaxWireBytes) throw new InvalidOperationException("JSON expansion exceeds limit.");
            output.Append(part);
        }
        void Quote(string text)
        {
            Add("\"");
            foreach (var c in text)
            {
                if (c == '"') Add("\\\"");
                else if (c == '\\') Add("\\\\");
                else if (c < 0x20 || char.IsSurrogate(c)) Add("\\u" + ((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                else Add(c.ToString());
            }
            Add("\"");
        }
        var active = new HashSet<int>();
        void Write(int id)
        {
            var node = _nodes[id];
            switch (node.Kind)
            {
                case PluginValueKind.Null: Add("null"); return;
                case PluginValueKind.Boolean: Add(node.Flag ? "true" : "false"); return;
                case PluginValueKind.String: Quote(node.Text!); return;
                case PluginValueKind.Number:
                    try { using var number = JsonDocument.Parse(node.Text!); if (number.RootElement.ValueKind != JsonValueKind.Number) throw new InvalidOperationException("Number is not JSON-compatible."); }
                    catch (JsonException ex) { throw new InvalidOperationException("Number is not JSON-compatible.", ex); }
                    Add(node.Text!); return;
                case PluginValueKind.Object:
                case PluginValueKind.Array:
                    if (!active.Add(id)) throw new InvalidOperationException("Cyclic value is not JSON-compatible.");
                    Add(node.Kind == PluginValueKind.Object ? "{" : "[");
                    for (var i = 0; i < node.Links!.Length; i++)
                    {
                        if (i > 0) Add(",");
                        if (node.Kind == PluginValueKind.Object) { Quote(node.Keys![i]); Add(":"); }
                        Write(node.Links[i]);
                    }
                    Add(node.Kind == PluginValueKind.Object ? "}" : "]");
                    active.Remove(id); return;
                default: throw new InvalidOperationException("Value is not JSON-compatible.");
            }
        }
        Write(_id);
        using var doc = JsonDocument.Parse(output.ToString(), new JsonDocumentOptions { MaxDepth = _limits.MaxDepth + 1 });
        return doc.RootElement.Clone();
    }
#pragma warning restore MA0051
}
