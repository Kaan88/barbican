// Mirrors: Bulwark components/contacts/contacts-sidebar.tsx (books, groups, categories with counts); Rust contacts.rs sidebar. I-345..I-349. Owner con-ui.

namespace Barbican.Presentation.Contacts;

/// <summary>One row of the Contacts sidebar: an address book, a group, a category (or the fixed places).</summary>
public sealed record ContactsSidebarEntry(ContactsPlace Place, string? Id, string Name, int Count, bool IsDefault = false);
