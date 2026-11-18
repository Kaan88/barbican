// Mirrors: Rust contacts.rs (default book on first contact; commits 4f2d73b, b0dd3bd; :242 AddressBook/set create with onSuccessSetIsDefault), contacts.rs:default_book_for (the server asked again before a book is created); Bulwark stores/contacts-store.ts (address books); RFC 9610 AddressBook/get, /set with onSuccessSetIsDefault and onDestroyRemoveContents;
// Stalwart crates/jmap/src/addressbook/{get,set}.rs. B-CON-003, B-CON-029. Owner con-data.

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Requests;

namespace Barbican.Core.Contacts;

/// <summary>The default <see cref="IAddressBooks"/> and the pure book rules.</summary>
public sealed class AddressBooks(IAccountManager accounts, IContactStore store) : IAddressBooks
{
    /// <summary>The book a new contact goes to: the one with isDefault, else the first named "Contacts" (any case), else the first book; null when there is none and one has to be created (and only then, after asking the server again: B-CON-003).</summary>
    public static JsonNode? DefaultBook(IReadOnlyList<JsonNode> books)
    {
        ArgumentNullException.ThrowIfNull(books);
        return books.FirstOrDefault(b => ContactJson.IsTrue(ContactJson.Get(b, "isDefault")))
            ?? books.FirstOrDefault(b => string.Equals(b.Str("name"), "Contacts", StringComparison.OrdinalIgnoreCase))
            ?? (books.Count > 0 ? books[0] : null);
    }

    public async Task<Result<string>> EnsureDefaultAsync(string accountKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        var client = ContactJmap.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<string>.Fail(client.Error!);
        }

        var account = client.Value!.Session.AccountFor(ContactOptions.Capability);

        // the loaded list first (the primary account's own books: the others are not ours to write to)
        var known = store.Snapshot(accountKey)?.Books.Where(b => !ContactJson.IsTrue(ContactJson.Get(b, "_shared"))).ToList() ?? [];
        if (DefaultBook(known) is { } held && held.Str("id") is { } heldId)
        {
            return Result<string>.Ok(heldId);
        }

        // the local list may not be loaded yet: ask the server before a book is made, so a second "Contacts" never appears
        var asked = await client.Value.CallAsync(JmapRequest.Of(JmapRequest.Call("AddressBook/get", new JsonObject { ["accountId"] = account, ["ids"] = null, ["properties"] = new JsonArray("id", "name", "isDefault") }, "g")), ct).ConfigureAwait(false);
        if (!asked.IsOk)
        {
            return Result<string>.Fail(asked.Error!);
        }

        var books = (asked.Value!.MethodResponses[0][1].Arr("list") ?? []).OfType<JsonObject>().Cast<JsonNode>().ToList();
        if (DefaultBook(books)?.Str("id") is { } serverId)
        {
            return Result<string>.Ok(serverId);
        }

        var made = await ContactJmap.CreateAsync(client.Value, "AddressBook", account, new JsonObject { ["name"] = "Contacts" }, ct, "#c").ConfigureAwait(false);
        if (made.IsOk)
        {
            _ = await store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
        }

        return made;
    }

    public async Task<Result<string>> CreateAsync(string accountKey, string name, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentNullException.ThrowIfNull(name);
        if (name.Trim().Length == 0)
        {
            return Result<string>.Fail(new ValidationError("Give the address book a name."));
        }

        var client = ContactJmap.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<string>.Fail(client.Error!);
        }

        var made = await ContactJmap.CreateAsync(client.Value!, "AddressBook", client.Value!.Session.AccountFor(ContactOptions.Capability), new JsonObject { ["name"] = name.Trim() }, ct).ConfigureAwait(false);
        if (made.IsOk)
        {
            _ = await store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
        }

        return made;
    }

    public async Task<Result<Unit>> RenameAsync(string accountKey, string bookId, string name, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentNullException.ThrowIfNull(bookId);
        ArgumentNullException.ThrowIfNull(name);
        if (name.Trim().Length == 0)
        {
            return Result<Unit>.Fail(new ValidationError("Give the address book a name."));
        }

        var client = ContactJmap.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<Unit>.Fail(client.Error!);
        }

        var (account, id) = Ref(client.Value!, bookId);
        var done = await ContactJmap.UpdateAsync(client.Value!, "AddressBook", account, id, new JsonObject { ["name"] = name.Trim() }, ct).ConfigureAwait(false);
        if (done.IsOk)
        {
            _ = await store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
        }

        return done;
    }

    public async Task<Result<Unit>> DeleteAsync(string accountKey, string bookId, bool removeContacts, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentNullException.ThrowIfNull(bookId);
        var client = ContactJmap.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<Unit>.Fail(client.Error!);
        }

        var (account, id) = Ref(client.Value!, bookId);
        var extra = removeContacts ? new JsonObject { ["onDestroyRemoveContents"] = true } : null;
        var done = await ContactJmap.DestroyManyAsync(client.Value!, "AddressBook", account, [id], extra, ct).ConfigureAwait(false);
        if (done.IsOk)
        {
            _ = await store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
        }

        return done;
    }

    private static (string Account, string Id) Ref(IJmapClient client, string bookId)
    {
        var (account, id) = ContactIds.SplitNs(bookId);
        return (account ?? client.Session.AccountFor(ContactOptions.Capability), id);
    }
}
