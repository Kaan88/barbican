// Mirrors: Bulwark lib/plugin-sandbox/assistant/c.filters.ts:31-45,98-137 (MAX_CONDITIONS 10, MAX_VALUES 20, MAX_VALUE 500; FILTER_FIELD_OPS; readValues, readCondition) over lib/sieve/generator.ts:27-31
// (isValidSieveHeaderName). A condition is checked the way the rule editor checks it; unknown properties, wrong types and ops a field does not take are refused.
using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailRules;

internal static class PluginFilterCondition
{
    internal const int MaxValues = 20;
    internal const int MaxValue = 500;
    private static readonly string[] AddressOps = ["contains", "not_contains", "is", "not_is", "starts_with", "ends_with", "address_is", "domain_is"];
    private static readonly string[] TextOps = ["contains", "not_contains", "is", "not_is", "starts_with", "ends_with"];
    private static readonly string[] Fields = ["field", "op", "value", "headerName"];

    private static string[]? OpsOf(string field) => field switch
    {
        "from" or "to" or "cc" => AddressOps,
        "subject" or "header" => TextOps,
        "body" => ["contains", "not_contains"],
        "size" => ["greater_than", "less_than"],
        "attachment" => ["has_any", "has_type"],
        _ => null,
    };

    /// <summary>The stored form of the condition (field, comparator, value, headerName), or null when it is not acceptable.</summary>
    internal static JsonObject? TryRead(PluginValue? entry)
    {
        if (!PluginMailArgs.TryOptions(entry, Fields, true, out var c) || c is null) return null;
        if (PluginMailArgs.Prop(c, "field") is not { Kind: PluginValueKind.String } fieldValue || OpsOf(fieldValue.StringValue) is not { } ops) return null;
        if (PluginMailArgs.Prop(c, "op") is not { Kind: PluginValueKind.String } opValue || !ops.Contains(opValue.StringValue, StringComparer.Ordinal)) return null;
        var field = fieldValue.StringValue;
        var op = opValue.StringValue;
        var node = new JsonObject { ["field"] = field, ["comparator"] = op };
        var value = PluginMailArgs.Prop(c, "value");
        if (string.Equals(op, "has_any", StringComparison.Ordinal))
        {
            if (value is not null && !(value.Kind == PluginValueKind.String && value.StringValue.Length == 0)) return null;
            node["value"] = "";
        }
        else if (string.Equals(field, "size", StringComparison.Ordinal))
        {
            if (Size(value) is not { } size) return null;
            node["value"] = size;
        }
        else
        {
            if (Values(value) is not { } values) return null;
            node["value"] = values.Count == 1 ? values[0] : new JsonArray([.. values.Select(v => (JsonNode?)v)]);
        }

        var header = PluginMailArgs.Prop(c, "headerName");
        if (string.Equals(field, "header", StringComparison.Ordinal))
        {
            if (header is not { Kind: PluginValueKind.String } || header.StringValue.Trim() is not { Length: > 0 and <= 100 } name || !name.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-')) return null;
            node["headerName"] = name;
        }
        else if (header is not null)
        {
            return null;
        }

        return node;
    }

    // a size in bytes: a whole number or up to ten digits as text (c.filters.ts:114-118)
    private static string? Size(PluginValue? value)
    {
        if (value is { Kind: PluginValueKind.Number })
        {
            return double.TryParse(value.NumberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n >= 0 && n < 1e10 && n == Math.Floor(n)
                ? ((long)n).ToString(CultureInfo.InvariantCulture) : null;
        }

        return value is { Kind: PluginValueKind.String } && value.StringValue.Length is > 0 and <= 10 && value.StringValue.All(char.IsAsciiDigit) ? value.StringValue : null;
    }

    // one value or 1 to 20, each 1-500 characters trimmed, without line breaks or control characters (c.filters.ts:98-107)
    private static List<string>? Values(PluginValue? value)
    {
        if (value is null) return null;
        var items = value.Kind == PluginValueKind.Array ? Enumerable.Range(0, value.Count).Select(value.Item).ToList() : [value];
        if (items.Count is 0 or > MaxValues) return null;
        var list = new List<string>();
        foreach (var item in items)
        {
            if (item.Kind != PluginValueKind.String) return null;
            var text = item.StringValue.Trim();
            if (text.Length is 0 or > MaxValue || text.Any(ch => ch is <= '\u001f' or '\u007f')) return null;
            list.Add(text);
        }

        return list;
    }
}
