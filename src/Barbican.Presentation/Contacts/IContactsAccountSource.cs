// Mirrors: Bulwark stores/contacts-store.ts (the contacts of the active account); Rust contacts.rs (one login at a time). Owner con-ui: which login the Contacts app shows; the default follows the active account, a sample run answers its own key.

namespace Barbican.Presentation.Contacts;

/// <summary>The login the Contacts app shows.</summary>
public interface IContactsAccountSource
{
    /// <summary>The account key, or null while nobody is signed in.</summary>
    string? AccountKey { get; }

    /// <summary>Whether the login offers contacts (the capability is advertised); drives the rail entry.</summary>
    bool HasContacts { get; }

    /// <summary>The key or the capability changed.</summary>
    event EventHandler? Changed;
}
