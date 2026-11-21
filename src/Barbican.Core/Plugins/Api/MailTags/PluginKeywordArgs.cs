// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1565-1650 (assertKeywordFields, assertKeywordDefinition, assertKeywordPatch, assertKeywordDefinitionArray,
// assertKeywordIdArray: at most 500 entries, a label of 1-255 characters after trimming, a colour from the keyword palette, visibility show | hide | unread,
// the id valid as the complete `$label:<id>` keyword) and :1822-1838 assertKeywordIds. Wrong types are InvalidArguments, never coerced.
using Barbican.Core.Mail.Tags;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailTags;

internal static class PluginKeywordArgs
{
    internal static bool IsPaletteKey(string key) =>
        TagRules.Palette.Contains(key, StringComparer.Ordinal)
        || (key.EndsWith("-light", StringComparison.Ordinal) && TagRules.Palette.Contains(key[..^6], StringComparer.Ordinal))
        || (key.EndsWith("-dark", StringComparison.Ordinal) && TagRules.Palette.Contains(key[..^5], StringComparer.Ordinal));

    /// <summary>A definition array for add (label required) or update (changes at least one of label, colour, visibility).</summary>
    internal static bool TryDefinitions(PluginValue? value, bool forAdd, out List<PluginKeywordInput> inputs)
    {
        inputs = [];
        if (value is not { Kind: PluginValueKind.Array } || value.Count > PluginTagDefinitions.MaxDefinitions) return false;
        for (var i = 0; i < value.Count; i++)
        {
            if (!TryDefinition(value.Item(i), forAdd, out var input)) return false;
            inputs.Add(input);
        }

        return true;
    }

    private static bool TryDefinition(PluginValue item, bool forAdd, out PluginKeywordInput input)
    {
        input = null!;
        if (item.Kind != PluginValueKind.Object) return false;
        var id = PluginMailArgs.Prop(item, "id");
        if (id is not { Kind: PluginValueKind.String } || !TagRules.ValidId(id.StringValue)) return false;
        string? label = null;
        var rawLabel = PluginMailArgs.Prop(item, "label");
        if (rawLabel is not null || forAdd)
        {
            if (rawLabel is not { Kind: PluginValueKind.String } || rawLabel.StringValue.Trim().Length == 0 || rawLabel.StringValue.Length > PluginTagDefinitions.MaxLabelLength) return false;
            label = rawLabel.StringValue.Trim();
        }

        string? color = null;
        if (PluginMailArgs.Prop(item, "color") is { } rawColor)
        {
            if (rawColor.Kind != PluginValueKind.String || !IsPaletteKey(rawColor.StringValue)) return false;
            color = rawColor.StringValue;
        }

        string? visibility = null;
        if (PluginMailArgs.Prop(item, "visibility") is { } rawVisibility)
        {
            if (rawVisibility.Kind != PluginValueKind.String || rawVisibility.StringValue is not ("show" or "hide" or "unread")) return false;
            visibility = rawVisibility.StringValue;
        }

        if (!forAdd && label is null && color is null && visibility is null) return false;
        input = new PluginKeywordInput(id.StringValue, label, color, visibility);
        return true;
    }

    /// <summary>An array of at most 500 strings; <paramref name="strict"/> also requires each to be a valid tag id (keywords.getCounts).</summary>
    internal static bool TryIds(PluginValue? value, bool strict, out List<string> ids)
    {
        ids = [];
        if (value is not { Kind: PluginValueKind.Array } || value.Count > PluginTagDefinitions.MaxDefinitions) return false;
        for (var i = 0; i < value.Count; i++)
        {
            var item = value.Item(i);
            if (item.Kind != PluginValueKind.String || (strict && !TagRules.ValidId(item.StringValue))) return false;
            ids.Add(item.StringValue);
        }

        return true;
    }

    internal static int Project(PluginValueBuilder b, PluginKeywordView view)
    {
        var fields = new List<(string, int)> { ("id", b.String(view.Def.Id)), ("label", b.String(view.Def.Label)), ("color", b.String(view.Def.Color)) };
        if (view.Visibility is not null) fields.Add(("visibility", b.String(view.Visibility)));
        if (view.AddedBy is not null) fields.Add(("addedBy", b.String(view.AddedBy)));
        return b.Object([.. fields]);
    }

    internal static int Strings(PluginValueBuilder b, IEnumerable<string> items) => b.Array([.. items.Select(b.String)]);
}
