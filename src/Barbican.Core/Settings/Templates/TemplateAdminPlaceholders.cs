// Mirrors: Bulwark lib/template-utils.ts (extractPlaceholders, substitutePlaceholders: {{name}} and {{name:default}}), the fill dialog on insert (G-034); the names the composer fills itself are in Core/Compose/TemplateText.BuiltIn. Contract from the M6 plan section 4.8; owner srv-data.

using System.Text.RegularExpressions;

namespace Barbican.Core.Settings.Templates;

/// <summary>Placeholders of a template.</summary>
public static partial class TemplateAdminPlaceholders
{
    /// <summary>The placeholder names in a text, each once, in order of first use ("Hi {{name}}, {{name}} / {{topic}}" gives name, topic); a name is letters, digits, "_" and "-", spaces inside the braces ignored.</summary>
    public static IReadOnlyList<string> Find(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return [.. Placeholder().Matches(text).Select(m => m.Groups["name"].Value).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>The text with each placeholder replaced by its value; a placeholder without a value stays as written.</summary>
    public static string Fill(string text, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(values);
        return Placeholder().Replace(text, m => values.TryGetValue(m.Groups["name"].Value, out var v) ? v : m.Value);
    }

    [GeneratedRegex(@"\{\{[ \t]*(?<name>[A-Za-z0-9_-]+)[ \t]*(?::[^{}\n]*)?\}\}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Placeholder();
}
