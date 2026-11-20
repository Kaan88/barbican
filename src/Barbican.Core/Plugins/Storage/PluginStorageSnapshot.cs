// Source: Bulwark lib/plugin-sandbox/host-api.ts:407-447 and storage-scope.ts:17-23.
using System.Text;
using System.Text.Json;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Persistence;

internal sealed class PluginStorageSnapshot(long revision, int prefixLength)
{
    internal const int Quota = 1024 * 1024;
    internal const int MaxBytes = 8 * Quota;
    private const int MaxEntries = 4091;
    // Reserve four nodes and one level for the native api-response envelope.
    internal static readonly PluginValueLimits ValueLimits = new(MaxWireBytes: 4 * Quota, MaxNodes: 4092, MaxDepth: 63, MaxStringBytes: 2 * Quota);
    internal long Revision { get; } = revision;
    internal Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

    internal bool TrySet(string key, PluginValue value)
    {
        if (!Values.ContainsKey(key) && Values.Count >= MaxEntries) return false;
        var available = Quota - SizeWithout(key) - prefixLength - key.Length;
        var raw = PluginStorageJson.Stringify(value, available);
        // Typed arrays and expanded graph aliases can become bigger JSON graphs.
        if (raw is null || ParseValue(raw) is null) return false;
        Values[key] = raw;
        return true;
    }

    private int SizeWithout(string key) => Values.Where(pair => !string.Equals(pair.Key, key, StringComparison.Ordinal))
        .Sum(pair => prefixLength + pair.Key.Length + pair.Value.Length);

    internal byte[] Encode()
    {
        var json = new StringBuilder("[");
        var first = true;
        foreach (var (key, raw) in Values)
        {
            if (!first) json.Append(',');
            json.Append('[').Append(PluginStorageJson.Quote(key)).Append(',').Append(raw).Append(']');
            first = false;
        }
        return Encoding.UTF8.GetBytes(json.Append(']').ToString());
    }

    internal static PluginStorageSnapshot? Decode(PluginStoreSnapshot stored, string pluginId, string localLoginKey)
    {
        if (stored.Revision < 0 || stored.Index.IsDefault || stored.Index.Length > MaxBytes) return null;
        var prefix = PrefixLength(pluginId, localLoginKey);
        var result = new PluginStorageSnapshot(stored.Revision, prefix);
        if (stored.Index.IsEmpty) return stored.Revision == 0 ? result : null;
        try
        {
            using var document = JsonDocument.Parse(stored.Index.AsMemory(), new JsonDocumentOptions { MaxDepth = ValueLimits.MaxDepth + 2 });
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() > MaxEntries) return null;
            var total = 0L;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() != 2 || item[0].ValueKind != JsonValueKind.String) return null;
                var key = PluginValue.FromJson(item[0]).StringValue;
                var raw = item[1].GetRawText();
                total += (long)prefix + key.Length + raw.Length;
                if (total > Quota || key.Length is 0 or > 256 || !result.Values.TryAdd(key, raw)) return null;
            }
            return result;
        }
        catch (Exception error) when (error is JsonException or PluginValueFormatException) { return null; }
    }

    internal static PluginValue? ParseValue(string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = ValueLimits.MaxDepth });
            return PluginValue.FromJson(document.RootElement, ValueLimits);
        }
        catch (Exception error) when (error is JsonException or PluginValueFormatException) { return null; }
    }

    private static int PrefixLength(string pluginId, string login)
    {
        // "plugin:<id>:@<encoded account>:". Count encodeURIComponent's UTF-16 output.
        var length = 10L + pluginId.Length;
        foreach (var rune in login.EnumerateRunes())
        {
            var c = rune.Value;
            length += rune.IsAscii && (char.IsAsciiLetterOrDigit((char)c) || "-_.!~*'()".Contains((char)c, StringComparison.Ordinal))
                ? 1 : rune.Utf8SequenceLength * 3;
        }
        return (int)Math.Min(Quota + 1, length);
    }
}
