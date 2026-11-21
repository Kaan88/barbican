// Mirrors: Bulwark stores/contacts-store.ts (the loaded books and cards the calls read), host-api.ts:1021-1090 (contact.* run on the signed-in client: the user's own account for list, create, update and delete),
// lib/jmap/client.ts:8776-8990 (getContacts, getContact, createContact, updateContact, deleteContact, searchContacts: at most 50 hits per account). RFC 9610 AddressBook myRights (mayRead, mayWrite, mayDelete),
// Stalwart crates/jmap/src/addressbook/get.rs. Reads come from the store the Contacts view shows (loaded first when it never was); writes only into the login's own account, never into a shared one.
using System.Text.Json.Nodes;
using Barbican.Core.Contacts;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Plugins.Api.Contacts;

internal static class PluginContactsData
{
    /// <summary>The most hits one account answers to a search (client.ts:8963 limit: 50).</summary>
    internal const int MaxSearchHitsPerAccount = 50;

    /// <summary>The most cards one contact.list returns; more fail the call instead of being cut (a plugin must not take a cut list for the whole book, host-api.ts:1056-1059).</summary>
    internal const int MaxListCards = 10_000;

    /// <summary>The login's contacts; loaded first when the store never was. Null when the login has none to give (no contacts capability, or the load failed).</summary>
    internal static async Task<ContactSnapshot?> SnapshotAsync(IPluginContactsHost host, string accountKey, CancellationToken ct)
    {
        if (host.Store.Snapshot(accountKey) is { } known) return known;
        var loaded = await host.Store.LoadAsync(accountKey, ct).ConfigureAwait(false);
        return loaded.IsOk ? loaded.Value : null;
    }

    internal static bool IsShared(JsonNode node) => node is JsonObject o && o["_shared"] is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.True;

    internal static JsonNode? CardById(ContactSnapshot snapshot, string id) => snapshot.Cards.FirstOrDefault(c => string.Equals(c.Str("id"), id, StringComparison.Ordinal));

    internal static JsonNode? BookById(ContactSnapshot snapshot, string id) => snapshot.Books.FirstOrDefault(b => string.Equals(b.Str("id"), id, StringComparison.Ordinal));

    internal static IReadOnlyList<string> BookIdsOf(JsonNode card) =>
        card.Obj("addressBookIds") is { } ids ? [.. ids.Where(e => e.Value is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.True).Select(e => e.Key)] : [];

    /// <summary>The books of the login's own account (getAddressBooks asks the primary account only, host-api.ts:1073-1080).</summary>
    internal static IEnumerable<JsonNode> OwnBooks(ContactSnapshot snapshot) => snapshot.Books.Where(b => !IsShared(b));

    /// <summary>The cards of the login's own account, optionally of one book (getContacts, client.ts:8776-8784).</summary>
    internal static IEnumerable<JsonNode> OwnCards(ContactSnapshot snapshot, string? bookId) =>
        snapshot.Cards.Where(c => !IsShared(c) && (bookId is null || BookIdsOf(c).Contains(bookId, StringComparer.Ordinal)));

    /// <summary>A right the book states as false denies; a book that states none (or a right it does not name) allows, as the server decides then.</summary>
    internal static bool Allows(JsonNode? book, string right) =>
        book.Child("myRights") is not JsonObject rights || !rights.TryGetPropertyValue(right, out var value) || (value is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.True);

    /// <summary>Whether the add-on may write into the book: the book is the login's own and its rights allow writing.</summary>
    internal static bool CanWriteBook(JsonNode book) => !IsShared(book) && Allows(book, "mayWrite");

    /// <summary>Whether the card may be changed (or, with <paramref name="delete"/>, deleted): it is the login's own and every book it is in allows writing (and deleting).</summary>
    internal static bool CanChange(ContactSnapshot snapshot, JsonNode card, bool delete)
    {
        if (IsShared(card)) return false;
        var books = BookIdsOf(card).Select(id => BookById(snapshot, id)).OfType<JsonNode>().ToList();
        return books.All(b => !IsShared(b) && Allows(b, "mayWrite") && (!delete || Allows(b, "mayDelete")));
    }

    /// <summary>The book a new card goes to: the one asked for, else the login's default book, else its first (client.ts:8858-8866); null when there is none to pick (the writer creates the default then).</summary>
    internal static JsonNode? PickBook(ContactSnapshot snapshot, string? requested)
    {
        if (requested is not null) return BookById(snapshot, requested);
        var own = OwnBooks(snapshot).ToList();
        return own.FirstOrDefault(b => b.IsTrue("isDefault")) ?? own.FirstOrDefault();
    }

    /// <summary>Whether the folded query is inside any text of the card (the text filter of ContactCard/query, client.ts:8962); photos, keys and ids are not searched.</summary>
    internal static bool Matches(JsonNode card, string foldedQuery)
    {
        if (card is not JsonObject o) return false;
        foreach (var (key, value) in o)
        {
            if (key.StartsWith('_') || key is "media" or "cryptoKeys" or "uid" or "id" or "addressBookIds" or "prodId" or "@type" or "version" or "created" or "updated") continue;
            if (AnyText(value, foldedQuery, 0)) return true;
        }

        return false;
    }

    private static bool AnyText(JsonNode? node, string folded, int depth)
    {
        if (depth > PluginContactProjection.MaxDepth) return false;
        return node switch
        {
            JsonValue v when v.GetValueKind() == System.Text.Json.JsonValueKind.String => ContactText.Fold(v.GetValue<string>()).Contains(folded, StringComparison.Ordinal),
            JsonObject o => o.Any(p => !p.Key.StartsWith('_') && AnyText(p.Value, folded, depth + 1)),
            JsonArray a => a.Any(x => AnyText(x, folded, depth + 1)),
            _ => false,
        };
    }

    internal static bool IsTrue(this JsonNode? node, string key) => node is JsonObject o && o[key] is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.True;

    internal static JsonNode? Child(this JsonNode? node, string key) => node is JsonObject o ? o[key] : null;
}
