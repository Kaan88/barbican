// Mirrors: Bulwark lib/template-utils.ts (placeholder regexes {{name}} / {{name:default}}, extractPlaceholders, substitutePlaceholders, getPlaceholderDefaults, getAutoFilledPlaceholders, filterTemplates, validateTemplateName), lib/template-types.ts BUILT_IN_PLACEHOLDERS (:19).

using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Barbican.Core.Compose;

/// <summary>The template placeholders (<c>{{name}}</c>, <c>{{name:default}}</c>), their filling and the picker's search.</summary>
public static partial class TemplateText
{
    /// <summary>The names the composer fills on its own or asks the sender for first (Bulwark BUILT_IN_PLACEHOLDERS).</summary>
    public static IReadOnlyList<string> BuiltIn { get; } = ["recipient_name", "company", "date", "short_date", "day", "month", "year", "day_of_week", "sender_name"];

    /// <summary>The longest template name (Bulwark MAX_TEMPLATE_NAME_LENGTH).</summary>
    public const int MaxNameLength = 200;

    /// <summary>Distinct placeholder names in <paramref name="text"/>, in order; an HTML body stops a default at a tag.</summary>
    public static IReadOnlyList<string> Placeholders(string text, bool html = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        return [.. (html ? HtmlPlaceholder() : TextPlaceholder()).Matches(text).Select(m => m.Groups["name"].Value).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>The names in a template's subject and body.</summary>
    public static IReadOnlyList<string> Placeholders(MailTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return [.. Placeholders(template.Subject).Concat(Placeholders(template.Body, template.IsHtml)).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>The default each name carries (<c>{{name:default}}</c>); the first wins. An HTML default is read as text.</summary>
    public static IReadOnlyDictionary<string, string> Defaults(MailTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        void Collect(string text, bool html)
        {
            foreach (Match m in (html ? HtmlPlaceholder() : TextPlaceholder()).Matches(text))
            {
                if (m.Groups["default"].Success && !map.ContainsKey(m.Groups["name"].Value))
                {
                    var value = m.Groups["default"].Value;
                    map[m.Groups["name"].Value] = html ? WebUtility.HtmlDecode(value) : value;
                }
            }
        }

        Collect(template.Subject, false);
        Collect(template.Body, template.IsHtml);
        return map;
    }

    /// <summary>Fills what <paramref name="values"/> has a value for (escaped for an HTML template, as typed for a plain one); the rest stays as it is.</summary>
    public static string Substitute(string text, IReadOnlyDictionary<string, string> values, bool html = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(values);
        return (html ? HtmlPlaceholder() : TextPlaceholder()).Replace(text, m =>
            values.TryGetValue(m.Groups["name"].Value, out var v) ? (html ? WebUtility.HtmlEncode(v).Replace("'", "&#39;", StringComparison.Ordinal) : v) : m.Value);
    }

    /// <summary>The values the composer fills on its own: the date in its forms and the sender's name.</summary>
    public static IReadOnlyDictionary<string, string> AutoFilled(DateTimeOffset now, string? senderName, CultureInfo? culture = null)
    {
        var c = culture ?? CultureInfo.InvariantCulture;
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["date"] = now.ToString("D", c),
            ["short_date"] = now.ToString("d", c),
            ["day"] = now.Day.ToString("00", CultureInfo.InvariantCulture),
            ["month"] = now.Month.ToString("00", CultureInfo.InvariantCulture),
            ["year"] = now.Year.ToString(CultureInfo.InvariantCulture),
            ["day_of_week"] = now.ToString("dddd", c),
        };
        if (!string.IsNullOrWhiteSpace(senderName))
        {
            map["sender_name"] = senderName;
        }

        return map;
    }

    /// <summary>The picker's search: a template matches when the text is in its name, subject or category (case-insensitive).</summary>
    public static IReadOnlyList<MailTemplate> Filter(IReadOnlyList<MailTemplate> templates, string query)
    {
        ArgumentNullException.ThrowIfNull(templates);
        var q = (query ?? "").Trim();
        return q.Length == 0
            ? templates
            : [.. templates.Where(t => t.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || t.Subject.Contains(q, StringComparison.OrdinalIgnoreCase) || t.Category.Contains(q, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>Null when the name is fine, else "empty" or "too_long" (Bulwark validateTemplateName).</summary>
    public static string? ValidateName(string name)
    {
        var t = (name ?? "").Trim();
        return t.Length == 0 ? "empty" : t.Length > MaxNameLength ? "too_long" : null;
    }

    [GeneratedRegex(@"\{\{(?<name>\w+)(?::(?<default>[^{}\n]*))?\}\}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TextPlaceholder();

    [GeneratedRegex(@"\{\{(?<name>\w+)(?::(?<default>[^{}<>\n]*))?\}\}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HtmlPlaceholder();
}
