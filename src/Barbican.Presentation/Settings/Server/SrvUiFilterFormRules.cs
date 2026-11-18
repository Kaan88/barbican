// Mirrors: Bulwark filter-rule-modal.tsx (the form follows its field: COMPARATORS_BY_FIELD, which value box shows), Rust filters_x.rs rule form. I-434. Contract from the M6 plan section 4.11; owner srv-ui.

using Barbican.Core.Common;
using Barbican.Core.Filters.Rules;

namespace Barbican.Presentation.Settings.Server;

/// <summary>The pure rules of the rule form.</summary>
public static class SrvUiFilterFormRules
{
    /// <summary>What a condition becomes when its field changes: the comparator stays when the new field allows it, else the new field's first comparator; the value is cleared when the kind of value changes; the header name is kept only for the field header.</summary>
    public static FilterCond FieldChanged(FilterCond current, string newField) => throw new PortPendingException("SrvUiFilterFormRules.FieldChanged");

    /// <summary>The value box of a field and comparator: none for "all" and has_any, a size for size, an attachment type for has_type, else text.</summary>
    public static SrvUiValueKind ValueKind(string field, string comparator) => throw new PortPendingException("SrvUiFilterFormRules.ValueKind");
}
