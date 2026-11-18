// Mirrors: Bulwark filter-rule-modal.tsx (the form follows its field: baseComparatorsFor, REGEX_FIELDS, which value box shows), Rust filters_x.rs rule form. I-434. Contract from the M6 plan section 4.11; owner srv-ui.
// The comparator table is kept here (not read from FilterRules.Comparators) so the form works before and after the srv-data merge; both follow the same Bulwark table.

using Barbican.Core.Filters.Rules;

namespace Barbican.Presentation.Settings.Server;

/// <summary>The pure rules of the rule form.</summary>
public static class SrvUiFilterFormRules
{
    private static readonly string[] Text7 = ["contains", "not_contains", "is", "not_is", "starts_with", "ends_with", "matches"];

    /// <summary>The comparators a field allows (Bulwark baseComparatorsFor plus regex where the editor offers it).</summary>
    public static IReadOnlyList<string> Comparators(string field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field switch
        {
            "from" or "to" or "cc" => [.. Text7, "address_is", "domain_is", "regex"],
            "subject" or "header" => [.. Text7, "regex"],
            "body" => ["contains", "is", "regex"],
            "size" => ["greater_than", "less_than"],
            "attachment" => ["has_any", "has_type"],
            _ => [],
        };
    }

    /// <summary>What a condition becomes when its field changes: the comparator stays when the new field allows it, else the new field's first comparator; the value is cleared when the kind of value changes; the header name is kept only for the field header.</summary>
    public static FilterCond FieldChanged(FilterCond current, string newField)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(newField);
        var allowed = Comparators(newField);
        var comparator = allowed.Contains(current.Comparator, StringComparer.Ordinal) ? current.Comparator : allowed.Count > 0 ? allowed[0] : "any";
        var value = ValueKind(current.Field, current.Comparator) == ValueKind(newField, comparator) ? current.Value : string.Empty;
        var header = string.Equals(newField, "header", StringComparison.Ordinal) ? current.Header : string.Empty;
        return new FilterCond(newField, comparator, value, header);
    }

    /// <summary>The value box of a field and comparator: none for "all" and has_any, a size for size, an attachment type for has_type, else text.</summary>
    public static SrvUiValueKind ValueKind(string field, string comparator)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(comparator);
        return field switch
        {
            "all" => SrvUiValueKind.None,
            "size" => SrvUiValueKind.Size,
            "attachment" => string.Equals(comparator, "has_type", StringComparison.Ordinal) ? SrvUiValueKind.AttachmentType : SrvUiValueKind.None,
            _ => string.Equals(comparator, "any", StringComparison.Ordinal) ? SrvUiValueKind.None : SrvUiValueKind.Text,
        };
    }
}
