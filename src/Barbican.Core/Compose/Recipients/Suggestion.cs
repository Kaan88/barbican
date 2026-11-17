// Mirrors: Rust recipients.rs / chips.rs (tag rust-final); Bulwark lib/email-composer-utils.ts, lib/rfc5322-mailbox.ts, lib/idn.ts, lib/validation.ts, stores/contact-store.ts; RFC 5322 section 3.4 (mailbox, group), RFC 3490/5891 (IDNA). Contract from the M3 plan section 4.5; owner composer-ui.

namespace Barbican.Core.Compose.Recipients;

/// <summary>An autocomplete entry: a person, or a contact group (empty email, <see cref="Group"/> set).</summary>
public sealed record Suggestion(string Name, string Email, RecipientGroup? Group = null);
