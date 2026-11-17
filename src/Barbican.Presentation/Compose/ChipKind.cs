// Mirrors: Rust chips.rs RChip/RSugg; Bulwark email-composer.tsx RecipientInput. Owner composer-ui.

namespace Barbican.Presentation.Compose;

/// <summary>What a chip is.</summary>
public enum ChipKind
{
    /// <summary>A valid address.</summary>
    Person,

    /// <summary>A contact group (shows its member count; double click opens it into its members).</summary>
    Group,

    /// <summary>Not an address (or a group with a member that is not): drawn marked, reported when sending, never dropped.</summary>
    Invalid,
}
