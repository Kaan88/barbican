// Mirrors: Rust chips.rs RChip/RSugg; Bulwark email-composer.tsx RecipientInput. Owner composer-ui.

namespace Barbican.Presentation.Compose;

/// <summary>One row of the autocomplete list.</summary>
/// <param name="Name">The name (the address when there is none).</param>
/// <param name="Detail">The address under the name, or "Group · 3 members".</param>
/// <param name="Initials">Avatar letters (empty for a group).</param>
/// <param name="IsGroup">A contact group.</param>
public sealed record SuggestionRow(string Name, string Detail, string Initials, bool IsGroup);
