// Source: Bulwark lib/extension-package.ts:517-536 (canonical JSON and approval hash); UTF-16-preserving decoding and canonicalization are host implementations.
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Barbican.Core.Plugins.Packages.Inspection;

internal static class JsJson
{
    public static object? Parse(ReadOnlySpan<byte> bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 64 });
        if (!reader.Read()) throw new JsonException("Empty JSON.");
        var value = Read(ref reader);
        if (reader.Read()) throw new JsonException("Trailing JSON.");
        return value;
    }

    private static object? Read(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.StartObject => Object(ref reader),
        JsonTokenType.StartArray => Array(ref reader),
        JsonTokenType.String => String(reader.ValueSpan),
        JsonTokenType.Number => double.Parse(Encoding.UTF8.GetString(reader.ValueSpan), NumberStyles.Float, CultureInfo.InvariantCulture),
        JsonTokenType.True => true,
        JsonTokenType.False => false,
        JsonTokenType.Null => null,
        _ => throw new JsonException("Invalid JSON value."),
    };

    private static Dictionary<string, object?> Object(ref Utf8JsonReader reader)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Invalid property.");
            var key = String(reader.ValueSpan);
            if (!reader.Read()) throw new JsonException("Missing value.");
            result[key] = Read(ref reader);
        }
        if (reader.TokenType != JsonTokenType.EndObject) throw new JsonException("Unclosed object.");
        return result;
    }

    private static List<object?> Array(ref Utf8JsonReader reader)
    {
        var result = new List<object?>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) result.Add(Read(ref reader));
        if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException("Unclosed array.");
        return result;
    }

    private static string String(ReadOnlySpan<byte> raw)
    {
        var source = Encoding.UTF8.GetString(raw);
        if (!source.Contains('\\', StringComparison.Ordinal)) return source;
        var result = new StringBuilder(source.Length);
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] != '\\') { result.Append(source[i]); continue; }
            var escaped = source[++i];
            if (escaped == 'u')
            {
                result.Append((char)int.Parse(source.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                i += 4;
            }
            else result.Append(escaped switch { '"' => '"', '\\' => '\\', '/' => '/', 'b' => '\b', 'f' => '\f', 'n' => '\n', 'r' => '\r', 't' => '\t', _ => throw new JsonException("Invalid escape.") });
        }
        return result.ToString();
    }

    public static string Canonical(object? value)
    {
        var result = new StringBuilder();
        Append(result, value);
        return result.ToString();
    }

    private static void Append(StringBuilder result, object? value)
    {
        switch (value)
        {
            case null: result.Append("null"); break;
            case bool b: result.Append(b ? "true" : "false"); break;
            case double n: result.Append(Number(n)); break;
            case string s: Quote(result, s); break;
            case List<object?> items:
                result.Append('[');
                for (var i = 0; i < items.Count; i++) { if (i > 0) result.Append(','); Append(result, items[i]); }
                result.Append(']');
                break;
            case Dictionary<string, object?> entries:
                result.Append('{');
                var first = true;
                foreach (var (key, item) in entries.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    if (!first) result.Append(',');
                    first = false;
                    Quote(result, key); result.Append(':'); Append(result, item);
                }
                result.Append('}');
                break;
            default: throw new InvalidOperationException("Unsupported JSON value.");
        }
    }

    private static void Quote(StringBuilder result, string value)
    {
        result.Append('"');
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '"': result.Append("\\\""); break;
                case '\\': result.Append("\\\\"); break;
                case '\b': result.Append("\\b"); break;
                case '\f': result.Append("\\f"); break;
                case '\n': result.Append("\\n"); break;
                case '\r': result.Append("\\r"); break;
                case '\t': result.Append("\\t"); break;
                default:
                    if (c < 0x20 || (char.IsSurrogate(c) && !(char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])) && !(char.IsLowSurrogate(c) && i > 0 && char.IsHighSurrogate(value[i - 1]))))
                        result.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else result.Append(c);
                    break;
            }
        }
        result.Append('"');
    }

    internal static string Number(double value)
    {
        if (!double.IsFinite(value)) return "null";
        if (value == 0) return "0";
        var negative = value < 0;
        var raw = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture).ToLowerInvariant();
        var parts = raw.Split('e');
        var exp = parts.Length == 2 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
        var dot = parts[0].IndexOf('.', StringComparison.Ordinal);
        var before = dot < 0 ? parts[0].Length : dot;
        var digits = parts[0].Replace(".", "", StringComparison.Ordinal);
        var decimalPos = before + exp;
        var sign = negative ? "-" : "";
        if (decimalPos > 0 && decimalPos <= 21)
            return sign + (decimalPos >= digits.Length ? digits.PadRight(decimalPos, '0') : digits.Insert(decimalPos, "."));
        if (decimalPos <= 0 && decimalPos > -6) return sign + "0." + new string('0', -decimalPos) + digits;
        var e = decimalPos - 1;
        return sign + digits[0] + (digits.Length > 1 ? "." + digits[1..] : "") + "e" + (e >= 0 ? "+" : "") + e.ToString(CultureInfo.InvariantCulture);
    }
}
