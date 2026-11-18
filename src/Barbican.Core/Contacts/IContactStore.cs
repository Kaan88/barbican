// Mirrors: Rust contacts.rs (load, ST), contacts_x.rs:14 CAP; Bulwark stores/contacts-store.ts; PORT-PLAN section 3.4 (push types for AddressBook and ContactCard through the generic /changes engine); G-044 (contacts live). B-CON-001, B-CON-006.
// Stalwart crates/jmap/src/contact/{get,query}.rs, addressbook/get.rs. Contract from the M5 plan section 4.2; owner con-data.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Contacts;

/// <summary>The in-memory contacts of every signed-in login. The views read it; writes go through <see cref="IContactWriter"/> and arrive back as changes.</summary>
public interface IContactStore
{
    event EventHandler<ContactChangedEventArgs>? Changed;

    /// <summary>Loads (or reloads) address books and every card in chunks of maxObjectsInGet; a failure in a shared account is skipped, in the primary one is the result. Empty without the contacts capability.</summary>
    Task<Result<ContactSnapshot>> LoadAsync(string accountKey, CancellationToken ct);

    ContactSnapshot? Snapshot(string accountKey);

    /// <summary>/changes for AddressBook and ContactCard after a push state change; a full load on cannotCalculateChanges.</summary>
    Task<Result<Unit>> RefreshAsync(string accountKey, CancellationToken ct);

    /// <summary>An optimistic local patch of one card (a category change); returns the previous card for <see cref="Restore"/>.</summary>
    JsonNode? Patch(string accountKey, string cardId, JsonObject patch);

    void Restore(string accountKey, string cardId, JsonNode? previous);
}
