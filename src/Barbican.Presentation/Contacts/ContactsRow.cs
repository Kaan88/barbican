// Mirrors: Rust contacts.rs (list rows), Bulwark components/contacts/contact-list-item.tsx; B-CON-004, B-CON-027, B-CON-030. Contract from the M5 plan section 4.5; owner con-ui.

namespace Barbican.Presentation.Contacts;

/// <summary>One row of the contact list.</summary>
public sealed record ContactsRow(string AccountKey, string Id, string DisplayName, string SortKey, string Letter, string Initials, string? Email, string? PhotoUri, bool IsGroup);
