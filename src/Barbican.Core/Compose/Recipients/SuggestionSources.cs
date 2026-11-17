// Mirrors: Rust recipients.rs / chips.rs (tag rust-final); Bulwark lib/email-composer-utils.ts, lib/rfc5322-mailbox.ts, lib/idn.ts, lib/validation.ts, stores/contact-store.ts; RFC 5322 section 3.4 (mailbox, group), RFC 3490/5891 (IDNA). Contract from the M3 plan section 4.5; owner composer-ui.

namespace Barbican.Core.Compose.Recipients;

/// <summary>Where suggestions come from, in precedence order: contacts and groups, then recent recipients.</summary>
public sealed record SuggestionSources
{
    /// <summary>(name, email) per contact address.</summary>
    public IReadOnlyList<(string Name, string Email)> Contacts { get; init; } = [];

    public IReadOnlyList<(string Id, string Name, IReadOnlyList<Recipient> Members)> Groups { get; init; } = [];

    /// <summary>(name, email) people written to, most recent first.</summary>
    public IReadOnlyList<(string Name, string Email)> Recent { get; init; } = [];
}
