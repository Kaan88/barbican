// Mirrors: Rust shortcuts.rs:384 help_rows (section header row, then one row per entry); Bulwark components/keyboard-shortcuts-modal.tsx. Stub from stage F of M2; owner menus.

namespace Barbican.Presentation.Input;

/// <summary>A row of the ? help sheet: a section header (Section set, Label empty) or an entry (Label and Keys).</summary>
public sealed record HelpRow(string Section, string Label, IReadOnlyList<string> Keys);
