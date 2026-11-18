// Mirrors: Rust jmap.rs identities, settings pages; Bulwark lib/jmap/client.ts:5213-5287 (Identity/set create, update, destroy), stores/identity-store.ts (order in the Inbox metadata `preferredIdentityIds`, G-025), components/settings/identity-settings.tsx, signatures-section.tsx, signature-details-section.tsx, your-name-row.tsx; RFC 8621 section 6; Stalwart crates/jmap/src/identity/set.rs (email must belong to the account; mayDelete). G-023..G-025, I-291, I-401. Contract from the M6 plan section 4.5; owner set-data.

namespace Barbican.Core.Settings.Account;

/// <summary>The fields of an identity a create or an update sends; null leaves a field alone (an empty string clears it).</summary>
public sealed record AccountIdentityEdit(string? Name, string? Email, string? ReplyTo, string? Bcc, string? TextSignature, string? HtmlSignature);
