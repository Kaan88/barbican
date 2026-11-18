// Mirrors: Rust contacts_x.rs:380 detail_lines (CtLine: section heading, label, value). Contract from the M5 plan section 4.1; owner con-data.

namespace Barbican.Core.Contacts;

/// <summary>One line of the contact detail pane: section ("EMAIL", "PHONE", "ADDRESS", "RELATED PEOPLE", ...), its label ("Work", "Mobile · Personal") and the value.</summary>
public sealed record ContactLine(string Section, string Label, string Value);
