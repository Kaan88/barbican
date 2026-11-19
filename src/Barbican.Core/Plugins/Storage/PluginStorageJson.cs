// Source: Bulwark lib/plugin-sandbox/host-api.ts:427-445; conversion follows JavaScript JSON.stringify.
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Barbican.Core.Plugins.Packages.Inspection;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Persistence;

internal static class PluginStorageJson
{
    private const int MaxUnits = 1024 * 1024;
    public static string Stringify(PluginValue value)
    {
        var text = new StringBuilder();
        var active = new HashSet<int>();
        Write(value, text, active, 0);
        return text.ToString();
    }

    private static void Write(PluginValue value, StringBuilder text, HashSet<int> active, int depth)
    {
        if (depth > 64 || text.Length > MaxUnits) throw new InvalidOperationException("Storage value exceeds its limit.");
        switch (value.Kind)
        {
            case PluginValueKind.Null: text.Append("null"); break;
            case PluginValueKind.Boolean: text.Append(value.BooleanValue ? "true" : "false"); break;
            case PluginValueKind.Number: text.Append(JsJson.Number(double.Parse(value.NumberText, CultureInfo.InvariantCulture))); break;
            case PluginValueKind.String: Quote(text, value.StringValue); break;
            case PluginValueKind.Date: WriteDate(value, text); break;
            case PluginValueKind.Array: WriteArray(value, text, active, depth); break;
            case PluginValueKind.View: WriteView(value, text); break;
            case PluginValueKind.Object: WriteObject(value, text, active, depth); break;
            case PluginValueKind.ArrayBuffer or PluginValueKind.Blob or PluginValueKind.Map or PluginValueKind.Set or PluginValueKind.Error:
                text.Append("{}"); break;
            default: throw new InvalidOperationException("Storage value is not JSON serializable.");
        }
        if (text.Length > MaxUnits) throw new InvalidOperationException("Storage value exceeds its limit.");
    }

    private static void WriteDate(PluginValue value, StringBuilder text)
    {
        var milliseconds = double.Parse(value.NumberText, CultureInfo.InvariantCulture);
        if (!double.IsFinite(milliseconds)) { text.Append("null"); return; }
        try { Quote(text, DateTimeOffset.FromUnixTimeMilliseconds(checked((long)milliseconds)).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)); }
        catch (ArgumentOutOfRangeException ex) { throw new InvalidOperationException("Date is outside the supported storage range.", ex); }
        catch (OverflowException) { text.Append("null"); }
    }

    private static void WriteView(PluginValue value, StringBuilder text)
    {
        if (value.ViewType == PluginViewType.DataView) { text.Append("{}"); return; }
        if (value.ViewType is PluginViewType.BigInt64 or PluginViewType.BigUint64) throw new InvalidOperationException("BigInt storage value.");
        var bytes = value.BinaryBytes();
        var width = value.ViewType switch
        {
            PluginViewType.Int8 or PluginViewType.Uint8 or PluginViewType.Uint8Clamped => 1,
            PluginViewType.Int16 or PluginViewType.Uint16 or PluginViewType.Float16 => 2,
            PluginViewType.Int32 or PluginViewType.Uint32 or PluginViewType.Float32 => 4,
            PluginViewType.Float64 => 8,
            _ => throw new InvalidOperationException("Unsupported typed array."),
        };
        text.Append('{');
        for (var i = 0; i < bytes.Length / width; i++)
        {
            if (i > 0) text.Append(',');
            Quote(text, i.ToString(CultureInfo.InvariantCulture));
            text.Append(':');
            var span = bytes.AsSpan(i * width, width);
            double number = value.ViewType switch
            {
                PluginViewType.Int8 => unchecked((sbyte)span[0]),
                PluginViewType.Uint8 or PluginViewType.Uint8Clamped => span[0],
                PluginViewType.Int16 => BinaryPrimitives.ReadInt16LittleEndian(span),
                PluginViewType.Uint16 => BinaryPrimitives.ReadUInt16LittleEndian(span),
                PluginViewType.Int32 => BinaryPrimitives.ReadInt32LittleEndian(span),
                PluginViewType.Uint32 => BinaryPrimitives.ReadUInt32LittleEndian(span),
                PluginViewType.Float16 => (double)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(span)),
                PluginViewType.Float32 => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(span)),
                PluginViewType.Float64 => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(span)),
                _ => throw new InvalidOperationException("Unsupported typed array."),
            };
            text.Append(JsJson.Number(number));
            if (text.Length > MaxUnits) throw new InvalidOperationException("Storage value exceeds its limit.");
        }
        text.Append('}');
    }

    private static void WriteArray(PluginValue value, StringBuilder text, HashSet<int> active, int depth)
    {
        if (!active.Add(value.Id)) throw new InvalidOperationException("Cyclic storage value.");
        text.Append('[');
        for (var i = 0; i < value.Count; i++)
        {
            if (i != 0) text.Append(',');
            var item = value.Item(i);
            if (item.Kind == PluginValueKind.Undefined) text.Append("null");
            else Write(item, text, active, depth + 1);
        }
        text.Append(']');
        active.Remove(value.Id);
    }

    private static void WriteObject(PluginValue value, StringBuilder text, HashSet<int> active, int depth)
    {
        if (!active.Add(value.Id)) throw new InvalidOperationException("Cyclic storage value.");
        text.Append('{');
        var first = true;
        foreach (var key in value.PropertyNames.OrderBy(IndexOrder).ThenBy(x => IndexOrder(x) == uint.MaxValue ? 0u : IndexOrder(x)))
        {
            var item = value.Property(key);
            if (item.Kind == PluginValueKind.Undefined) continue;
            if (!first) text.Append(',');
            first = false;
            Quote(text, key);
            text.Append(':');
            Write(item, text, active, depth + 1);
        }
        text.Append('}');
        active.Remove(value.Id);
    }

    private static uint IndexOrder(string key) => uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
        && index != uint.MaxValue && string.Equals(index.ToString(CultureInfo.InvariantCulture), key, StringComparison.Ordinal) ? index : uint.MaxValue;

    private static void Quote(StringBuilder text, string value)
    {
        text.Append('"');
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '"': text.Append("\\\""); break;
                case '\\': text.Append("\\\\"); break;
                case '\b': text.Append("\\b"); break;
                case '\f': text.Append("\\f"); break;
                case '\n': text.Append("\\n"); break;
                case '\r': text.Append("\\r"); break;
                case '\t': text.Append("\\t"); break;
                default:
                    if (c < 0x20 || (char.IsSurrogate(c) && !(char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])) && !(char.IsLowSurrogate(c) && i > 0 && char.IsHighSurrogate(value[i - 1]))))
                        text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else text.Append(c);
                    break;
            }
            if (text.Length > MaxUnits) throw new InvalidOperationException("Storage value exceeds its limit.");
        }
        text.Append('"');
    }
}
