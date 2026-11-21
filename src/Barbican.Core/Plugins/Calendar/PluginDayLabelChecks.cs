// Mirrors: Bulwark lib/plugin-sandbox/day-labels.ts:20-44 checkDayLabels (at most 400 labels; date YYYY-MM-DD; text at most 24 characters and title at most 120, both trimmed and dropped when empty; a label needs text or dot).
// Unknown keys are ignored, as in Bulwark. Where Bulwark throws, the call is InvalidArguments. A date such as 2026-02-31 matches Bulwark's pattern but names no day; Barbican refuses it.
using System.Globalization;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Calendar;

/// <summary>The limits of day labels and the check of the <c>ui.setDayLabels</c> argument.</summary>
public static class PluginDayLabelChecks
{
    public const int MaxLabels = 400;
    public const int MaxTextLength = 24;
    public const int MaxTitleLength = 120;

    /// <summary>Reads the label list; false for anything Bulwark's checkDayLabels throws on.</summary>
    public static bool TryRead(PluginValue? list, out IReadOnlyList<PluginDayLabel> labels)
    {
        labels = [];
        if (list is not { Kind: PluginValueKind.Array } || list.Count > MaxLabels) return false;
        var result = new List<PluginDayLabel>(list.Count);
        for (var i = 0; i < list.Count; i++)
        {
            var raw = list.Item(i);
            if (raw.Kind != PluginValueKind.Object) return false;
            if (!raw.TryGetProperty("date", out var date) || date.Kind != PluginValueKind.String || !TryDate(date.StringValue, out var day)) return false;
            if (!TryText(raw, "text", MaxTextLength, out var text) || !TryText(raw, "title", MaxTitleLength, out var title)) return false;
            var dot = raw.TryGetProperty("dot", out var dotValue) && dotValue is { Kind: PluginValueKind.Boolean, BooleanValue: true };
            if (text is null && !dot) return false;
            result.Add(new PluginDayLabel(day, text, title, dot));
        }

        labels = result;
        return true;
    }

    private static bool TryDate(string text, out DateOnly day)
    {
        day = default;
        if (text.Length != 10 || text[4] != '-' || text[7] != '-') return false;
        for (var i = 0; i < 10; i++)
        {
            if (i is 4 or 7) continue;
            if (text[i] is < '0' or > '9') return false;
        }

        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
    }

    private static bool TryText(PluginValue label, string name, int max, out string? text)
    {
        text = null;
        if (!label.TryGetProperty(name, out var value) || value.Kind == PluginValueKind.Undefined) return true;
        if (value.Kind != PluginValueKind.String || value.StringValue.Length > max) return false;
        var trimmed = value.StringValue.Trim();
        text = trimmed.Length == 0 ? null : trimmed;
        return true;
    }
}
