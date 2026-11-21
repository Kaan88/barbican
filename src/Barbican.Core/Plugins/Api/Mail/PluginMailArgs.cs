// Mirrors: Bulwark lib/plugin-sandbox/host-api-input.ts:46-110 readObject / readId / readString / readBoolean / readInteger.
// Unknown properties are refused (a misspelt field fails loudly); wrong types are InvalidArguments rather than coerced.
using System.Globalization;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Mail;

internal static class PluginMailArgs
{
    internal const int MaxIdLength = 256;

    /// <summary>The argument at <paramref name="index"/>, or null when missing or undefined (JavaScript's undefined).</summary>
    internal static PluginValue? At(PluginValue args, int index)
    {
        if (args.Kind != PluginValueKind.Array || args.Count <= index) return null;
        var value = args.Item(index);
        return value.Kind == PluginValueKind.Undefined ? null : value;
    }

    internal static bool IsId(PluginValue? value) =>
        value is { Kind: PluginValueKind.String } && value.StringValue.Length is > 0 and <= MaxIdLength;

    /// <summary>A plain object with only the listed keys; absent is fine unless <paramref name="required"/>.</summary>
    internal static bool TryOptions(PluginValue? value, IReadOnlyCollection<string> allowed, bool required, out PluginValue? options)
    {
        options = null;
        if (value is null) return !required;
        if (value.Kind != PluginValueKind.Object) return false;
        foreach (var name in value.PropertyNames)
        {
            if (!allowed.Contains(name, StringComparer.Ordinal)) return false;
        }
        options = value;
        return true;
    }

    /// <summary>The named property, or null when absent or undefined.</summary>
    internal static PluginValue? Prop(PluginValue? options, string name)
    {
        if (options is null || !options.TryGetProperty(name, out var value)) return null;
        return value.Kind == PluginValueKind.Undefined ? null : value;
    }

    /// <summary>A whole number in range; an absent value takes <paramref name="fallback"/>.</summary>
    internal static bool TryInteger(PluginValue? value, int min, int max, int fallback, out int result)
    {
        result = fallback;
        if (value is null) return true;
        if (value.Kind != PluginValueKind.Number
            || !double.TryParse(value.NumberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            || double.IsNaN(number) || double.IsInfinity(number) || number != Math.Floor(number) || number < min || number > max)
        {
            return false;
        }
        result = (int)number;
        return true;
    }

    /// <summary>A string of at most <paramref name="max"/> UTF-16 units.</summary>
    internal static bool TryString(PluginValue? value, int max, out string text)
    {
        text = "";
        if (value is not { Kind: PluginValueKind.String } || value.StringValue.Length > max) return false;
        text = value.StringValue;
        return true;
    }

    /// <summary>The optional <c>accountId</c> of an options object or a positional argument; false when present but not an id.</summary>
    internal static bool TryAccount(PluginValue? value, out string? accountId)
    {
        accountId = null;
        if (value is null) return true;
        if (!IsId(value)) return false;
        accountId = value.StringValue;
        return true;
    }
}
