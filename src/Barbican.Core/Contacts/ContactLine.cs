// Mirrors: Rust contacts_x.rs:380 detail_lines (CtLine: section heading, label, value, act, tint). Contract from the M5 plan section 4.1; owner con-data. Action and Tint are additions of D-M5-A05 (the Rust CtLine had them).

namespace Barbican.Core.Contacts;

/// <summary>One line of the contact detail pane: section ("EMAIL", "PHONE", "ADDRESS", "RELATED PEOPLE", ...), its label ("Work", "Mobile · Personal") and the value.</summary>
public sealed record ContactLine(string Section, string Label, string Value)
{
    /// <summary>What a click does: "mail", "tel", "url", or empty.</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>"rose" for a birthday or anniversary within 14 days, else empty.</summary>
    public string Tint { get; init; } = string.Empty;
}
