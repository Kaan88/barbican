// Source: Bulwark lib/plugin-sandbox/host-api.ts:427-445 (JSON.stringify storage projection).
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Persistence;

internal static class PluginStorageJson
{
    internal static string? Stringify(PluginValue value, int maxUtf16)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (maxUtf16 < 0 || value.Kind == PluginValueKind.Undefined) return null;
        var writer = new Writer(value.Nodes, maxUtf16);
        return writer.Write(value.Id) ? writer.Result : null;
    }

    internal static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var b = new StringBuilder();
        AppendQuote(b, value);
        return b.ToString();
    }

    private static void AppendQuote(StringBuilder b, string value)
    {
        b.Append('"');
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '"': b.Append("\\\""); break;
                case '\\': b.Append("\\\\"); break;
                case '\b': b.Append("\\b"); break;
                case '\f': b.Append("\\f"); break;
                case '\n': b.Append("\\n"); break;
                case '\r': b.Append("\\r"); break;
                case '\t': b.Append("\\t"); break;
                default:
                    if (c < 32 || (char.IsSurrogate(c) && !(char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))))
                        b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else { b.Append(c); if (char.IsHighSurrogate(c)) b.Append(value[++i]); }
                    break;
            }
        }
        b.Append('"');
    }

    private static string Number(double value)
    {
        if (!double.IsFinite(value)) return "null";
        if (value == 0) return "0";
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        var e = text.IndexOfAny(['E', 'e']);
        if (e < 0) return text;
        var exponent = int.Parse(text.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var mantissa = text[..e];
        var negative = mantissa[0] == '-';
        var digits = mantissa.TrimStart('-').Replace(".", "", StringComparison.Ordinal);
        var decimalPosition = exponent + 1;
        var sign = negative ? "-" : "";
        if (decimalPosition > 0 && decimalPosition <= 21)
            return sign + (decimalPosition >= digits.Length ? digits + new string('0', decimalPosition - digits.Length) : digits.Insert(decimalPosition, "."));
        if (decimalPosition <= 0 && decimalPosition > -6)
            return sign + "0." + new string('0', -decimalPosition) + digits;
        return sign + digits[0] + (digits.Length > 1 ? "." + digits[1..] : "") + "e" + (exponent >= 0 ? "+" : "") + exponent.ToString(CultureInfo.InvariantCulture);
    }

    private sealed class Writer(PluginNode[] nodes, int limit)
    {
        private readonly StringBuilder _output = new();
        private readonly HashSet<int> _active = [];
        private readonly Dictionary<int, int[]> _objectOrder = [];
        private int _steps;
        internal string Result => _output.ToString();
        private bool Add(string s) { if (s.Length > limit - _output.Length) return false; _output.Append(s); return true; }
        private bool Quoted(string s)
        {
            if (!Add("\"")) return false;
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                string part;
                switch (c)
                {
                    case '"': part = "\\\""; break;
                    case '\\': part = "\\\\"; break;
                    case '\b': part = "\\b"; break;
                    case '\f': part = "\\f"; break;
                    case '\n': part = "\\n"; break;
                    case '\r': part = "\\r"; break;
                    case '\t': part = "\\t"; break;
                    default:
                        if (c < 32 || (char.IsSurrogate(c) && !(char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))))
                            part = "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture);
                        else if (char.IsHighSurrogate(c)) part = s.Substring(i++, 2);
                        else part = c.ToString();
                        break;
                }
                if (!Add(part)) return false;
            }
            return Add("\"");
        }
        internal bool Write(int id)
        {
            if (++_steps > limit + 4096L || _active.Count >= 256) return false;
            var n = nodes[id];
            switch (n.Kind)
            {
                case PluginValueKind.Undefined: case PluginValueKind.Null: return Add("null");
                case PluginValueKind.Boolean: return Add(n.Flag ? "true" : "false");
                case PluginValueKind.String: return Quoted(n.Text!);
                case PluginValueKind.Number: return Add(Number(double.Parse(n.Text!, CultureInfo.InvariantCulture)));
                case PluginValueKind.Date:
                    var date = PluginEcmaDate.Format(double.Parse(n.Text!, CultureInfo.InvariantCulture));
                    return date is null ? Add("null") : Quoted(date);
                case PluginValueKind.BigInt: return false;
                case PluginValueKind.Array: return Array(id, n);
                case PluginValueKind.Object: return Object(id, n);
                case PluginValueKind.View: return View(n);
                case PluginValueKind.ArrayBuffer: case PluginValueKind.Blob: case PluginValueKind.Map: case PluginValueKind.Set: case PluginValueKind.Error: return Add("{}");
                default: return false;
            }
        }
        private bool Array(int id, PluginNode n)
        {
            if (!_active.Add(id) || !Add("[")) return false;
            for (var i = 0; i < n.Links!.Length; i++) if ((i > 0 && !Add(",")) || !Write(n.Links[i])) return false;
            _active.Remove(id);
            return Add("]");
        }
        private static uint? Index(string key)
        {
            if (key.Length == 0 || key.Length > 10 || (key.Length > 1 && key[0] == '0')) return null;
            if (!uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index == uint.MaxValue || !string.Equals(key, index.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)) return null;
            return index;
        }
        private bool Object(int id, PluginNode n)
        {
            if (!_active.Add(id) || !Add("{")) return false;
            var keys = n.Keys!;
            var links = n.Links!;
            if (!_objectOrder.TryGetValue(id, out var indices))
            {
                // Charge even omitted properties; cache inspection and sorting for aliases.
                if ((long)_steps + keys.Length > (long)limit + 4096) return false;
                _steps += keys.Length;
                indices = Enumerable.Range(0, keys.Length)
                    .Where(i => nodes[links[i]].Kind != PluginValueKind.Undefined)
                    .OrderBy(i => Index(keys[i]) is null ? 1 : 0)
                    .ThenBy(i => Index(keys[i]) ?? 0).ToArray();
                _objectOrder.Add(id, indices);
            }
            var first = true;
            foreach (var i in indices)
            {
                if ((!first && !Add(",")) || !Quoted(keys[i]) || !Add(":") || !Write(links[i])) return false;
                first = false;
            }
            _active.Remove(id);
            return Add("}");
        }
        private bool View(PluginNode n)
        {
            if (n.View == PluginViewType.DataView) return Add("{}");
            var size = n.View switch { PluginViewType.Int16 or PluginViewType.Uint16 or PluginViewType.Float16 => 2, PluginViewType.Int32 or PluginViewType.Uint32 or PluginViewType.Float32 => 4, PluginViewType.Float64 or PluginViewType.BigInt64 or PluginViewType.BigUint64 => 8, _ => 1 };
            if (n.Length > 0 && n.View is (PluginViewType.BigInt64 or PluginViewType.BigUint64)) return false;
            if (!Add("{")) return false;
            var bytes = nodes[n.Links![0]].Bytes!.AsSpan(n.Offset, n.Length);
            for (var i = 0; i < bytes.Length / size; i++)
            {
                var span = bytes.Slice(i * size, size);
                double x = n.View switch
                {
                    PluginViewType.Int8 => unchecked((sbyte)span[0]), PluginViewType.Uint8 or PluginViewType.Uint8Clamped => span[0],
                    PluginViewType.Int16 => BinaryPrimitives.ReadInt16LittleEndian(span), PluginViewType.Uint16 => BinaryPrimitives.ReadUInt16LittleEndian(span),
                    PluginViewType.Int32 => BinaryPrimitives.ReadInt32LittleEndian(span), PluginViewType.Uint32 => BinaryPrimitives.ReadUInt32LittleEndian(span),
                    PluginViewType.Float16 => (double)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(span)),
                    PluginViewType.Float32 => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(span)),
                    PluginViewType.Float64 => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(span)), _ => 0,
                };
                if ((i > 0 && !Add(",")) || !Quoted(i.ToString(CultureInfo.InvariantCulture)) || !Add(":") || !Add(Number(x))) return false;
            }
            return Add("}");
        }
    }
}
