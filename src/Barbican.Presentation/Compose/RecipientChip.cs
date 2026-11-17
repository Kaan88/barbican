// Mirrors: Rust chips.rs RChip/RSugg; Bulwark email-composer.tsx RecipientInput. Owner composer-ui.

namespace Barbican.Presentation.Compose;

/// <summary>One chip of a field.</summary>
/// <param name="Label">The name, else the address (the Unicode form of an IDN domain).</param>
/// <param name="Full">The text that goes back into the field when the chip is edited (<c>Name &lt;address&gt;</c>).</param>
/// <param name="Initials">Two letters for the avatar.</param>
/// <param name="Kind">The look of the chip.</param>
/// <param name="Armed">Backspace on an empty input armed it: the next Backspace removes it.</param>
/// <param name="Members">The member count of a group, else 0.</param>
public sealed record RecipientChip(string Label, string Full, string Initials, ChipKind Kind, bool Armed, int Members);
