// Mirrors: Rust recipients.rs / chips.rs (tag rust-final); Bulwark lib/email-composer-utils.ts, lib/rfc5322-mailbox.ts, lib/idn.ts, lib/validation.ts, stores/contact-store.ts; RFC 5322 section 3.4 (mailbox, group), RFC 3490/5891 (IDNA). Contract from the M3 plan section 4.5; owner composer-ui.

namespace Barbican.Core.Compose.Recipients;

/// <summary>One recipient: an address, or a contact group carrying a snapshot of its members. Value equality includes the members.</summary>
public sealed record Recipient
{
    public string Name { get; init; } = "";

    public string Email { get; init; } = "";

    public IReadOnlyList<Recipient>? Group { get; init; }

    public static Recipient Addr(string name, string email) => new() { Name = name, Email = email };

    public bool Equals(Recipient? other) =>
        other is not null && string.Equals(Name, other.Name, StringComparison.Ordinal) && string.Equals(Email, other.Email, StringComparison.Ordinal) && (Group is null ? other.Group is null : other.Group is not null && Group.SequenceEqual(other.Group));

    public override int GetHashCode() => HashCode.Combine(Name, Email, Group?.Count);
}
