// Mirrors: Rust contacts.rs (the sidebar places); Bulwark components/contacts/contacts-sidebar.tsx, contacts-app.tsx; I-345..I-351. Contract from the M5 plan section 4.5; owner con-ui.

namespace Barbican.Presentation.Contacts;

/// <summary>What the contact list shows.</summary>
public enum ContactsPlace
{
    All,
    Book,
    Group,
    Category,
    Directory,
    Duplicates,
}
