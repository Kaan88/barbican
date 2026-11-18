// Mirrors: Rust contacts.rs (default book on first contact; commits 4f2d73b, b0dd3bd); Bulwark stores/contacts-store.ts (address books); RFC 9610 AddressBook/get, /set with onSuccessSetIsDefault; Stalwart crates/jmap/src/addressbook/{get,set}.rs. B-CON-003, B-CON-029.
// Stub from the M5 plan section 4.1; owner con-data replaces the instance methods (constructor dependencies: IAccountManager, IContactStore).

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Contacts;

/// <summary>The default <see cref="IAddressBooks"/> and the pure book rules.</summary>
public sealed class AddressBooks : IAddressBooks
{
    /// <summary>The book a new contact goes to: the one with isDefault, else the first named "Contacts" (any case), else the first book; null when there is none and one has to be created (and only then, after asking the server again: B-CON-003).</summary>
    public static JsonNode? DefaultBook(IReadOnlyList<JsonNode> books) => throw new PortPendingException("AddressBooks.DefaultBook");

    public Task<Result<string>> EnsureDefaultAsync(string accountKey, CancellationToken ct) => throw new PortPendingException("AddressBooks.EnsureDefaultAsync");

    public Task<Result<string>> CreateAsync(string accountKey, string name, CancellationToken ct) => throw new PortPendingException("AddressBooks.CreateAsync");

    public Task<Result<Unit>> RenameAsync(string accountKey, string bookId, string name, CancellationToken ct) => throw new PortPendingException("AddressBooks.RenameAsync");

    public Task<Result<Unit>> DeleteAsync(string accountKey, string bookId, bool removeContacts, CancellationToken ct) => throw new PortPendingException("AddressBooks.DeleteAsync");
}
