// Mirrors: Rust contacts.rs (suggestions); Bulwark components/email/recipient-popover.tsx, address-context-menu.tsx (View contact, Add to contacts), email-viewer.tsx contact sidebar, lib/plugin-hook-catalog.ts:202 (onProvideRecipientSuggestions); I-192, I-193, I-204, I-205, I-231, I-247. B-CON-002.
// The seam between contacts and mail: the composer's recipient autocomplete and the reader's contact card read this and never read IContactStore. Contract from the M5 plan section 4.5; owner con-data.

namespace Barbican.Core.Contacts;

/// <summary>Lookups the mail app needs from the contacts of every signed-in login.</summary>
public interface IContactLookup
{
    /// <summary>Cards whose name or email contains <paramref name="text"/> (case-insensitive, diacritics ignored), best first (name starts with, email starts with, contains), at most <paramref name="max"/>, one per address, groups excluded; fires onProvideRecipientSuggestions so plugins may add or change entries.</summary>
    Task<IReadOnlyList<ContactMatch>> SuggestAsync(string text, int max, CancellationToken ct);

    /// <summary>The card of an address in any login (null when none); loads the store on first use.</summary>
    Task<ContactMatch?> FindByEmailAsync(string address, CancellationToken ct);
}
