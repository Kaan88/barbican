// Mirrors: Rust contacts.rs (create, save, delete, move, groups: commits e75e107, 4d24260), contacts_x.rs (import, merge apply); Bulwark stores/contacts-store.ts (createContact, updateContact, deleteContact, moveContact, group members), lib/contacts/merge.ts;
// RFC 9610 ContactCard/set; Stalwart crates/jmap/src/contact/set.rs (a keywords/x pointer patch on a card without keywords is refused: categories send the whole keywords map, B-CON-006). B-CON-002..006, B-CON-016, B-CON-024.
// Hooks (11, M5 plan section 2.4): onBefore/AfterContactCreate, Update, Delete, onContactsImport, onContactMove, onContactGroupChange, onContactGroupMemberChange. Contract from the M5 plan section 4.3; owner con-data.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Contacts;

/// <summary>Every ContactCard write. Each applies its hooks, writes through JMAP, and leaves <see cref="IContactStore"/> consistent (optimistic patch, rolled back on failure, confirmed by the push delta).</summary>
public interface IContactWriter
{
    /// <summary>Creates a card in <paramref name="bookId"/> (null: the default book, created once if none exists); a uid is added; returns the new id.</summary>
    Task<Result<string>> CreateAsync(string accountKey, string? bookId, JsonObject card, CancellationToken ct);

    /// <summary>Updates a card with JSON-pointer patches of the changed properties; keywords (categories) and other maps the server's card may lack are sent whole.</summary>
    Task<Result<Unit>> UpdateAsync(string accountKey, string cardId, JsonObject patch, CancellationToken ct);

    Task<Result<Unit>> DeleteAsync(string accountKey, IReadOnlyList<string> cardIds, CancellationToken ct);

    /// <summary>Moves cards between books (addressBookIds patch).</summary>
    Task<Result<Unit>> MoveAsync(string accountKey, IReadOnlyList<string> cardIds, string bookId, CancellationToken ct);

    /// <summary>Creates a group card (kind group) with members keyed by uid.</summary>
    Task<Result<string>> CreateGroupAsync(string accountKey, string? bookId, string name, IReadOnlyList<string> memberUids, CancellationToken ct);

    /// <summary>Adds or removes members of a group (members/uid patches).</summary>
    Task<Result<Unit>> SetMembersAsync(string accountKey, string groupId, IReadOnlyList<string> add, IReadOnlyList<string> remove, CancellationToken ct);

    /// <summary>Applies a merge: the patch on the kept card, the group moves, then the other cards deleted (in that order, so a failure loses nothing).</summary>
    Task<Result<Unit>> MergeAsync(string accountKey, ContactMergePlan plan, IReadOnlyList<JsonNode> groups, CancellationToken ct);

    /// <summary>Creates the batch in chunks (one ContactCard/set per chunk); onContactsImport; returns how many were created. A chunk that fails stops the import and the count so far is in the failed result.</summary>
    Task<Result<int>> ImportAsync(string accountKey, string bookId, IReadOnlyList<JsonNode> cards, CancellationToken ct);
}
