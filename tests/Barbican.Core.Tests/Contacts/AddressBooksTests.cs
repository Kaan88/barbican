// M5 con-data: the address book writes over FakeJmap (RFC 9610 AddressBook/set; B-CON-003: the server is asked again before a default book is created).

#pragma warning disable MA0006, MA0002 // assertions compare string literals

using System.Text.Json.Nodes;
using Barbican.Core.Contacts;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Contacts;

public sealed class AddressBooksTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_book_the_server_has_is_used_even_when_the_local_list_was_never_loaded()
    {
        var rig = ContactRig.Create();
        var contacts = FakeSeed.ContactsM5(0);
        var (key, server, _) = await rig.AddAsync("user@example.org", contacts);
        var books = new AddressBooks(rig, new ContactStore(rig, rig.Hooks, rig.Time));
        var r = await books.EnsureDefaultAsync(key, Ct);
        Assert.Equal("ab1", r.Value);
        Assert.Empty(server.Api.ContactSetRequests);
    }

    [Fact]
    public async Task Create_rename_and_delete_with_or_without_the_contacts()
    {
        var rig = ContactRig.Create();
        var contacts = FakeSeed.ContactsM5(0);
        var (key, server, _) = await rig.AddAsync("user@example.org", contacts);
        var store = new ContactStore(rig, rig.Hooks, rig.Time);
        _ = await store.LoadAsync(key, Ct);
        var books = new AddressBooks(rig, store);

        var made = await books.CreateAsync(key, " Club ", Ct);
        Assert.True(made.IsOk);
        Assert.Equal("Club", (string?)contacts.Books().First(b => (string?)b["id"] == made.Value)["name"]);
        Assert.Contains(store.Snapshot(key)!.Books, b => (string?)b["id"] == made.Value);

        Assert.True((await books.RenameAsync(key, made.Value!, "Clubs", Ct)).IsOk);
        Assert.Equal("Clubs", (string?)contacts.Books().First(b => (string?)b["id"] == made.Value)["name"]);
        Assert.False((await books.CreateAsync(key, "  ", Ct)).IsOk);
        Assert.False((await books.RenameAsync(key, made.Value!, "", Ct)).IsOk);

        var refused = await books.DeleteAsync(key, "ab2", removeContacts: false, Ct);
        Assert.False(refused.IsOk);
        Assert.Contains(contacts.Books(), b => (string?)b["id"] == "ab2");
        Assert.True((await books.DeleteAsync(key, "ab2", removeContacts: true, Ct)).IsOk);
        Assert.DoesNotContain(contacts.Books(), b => (string?)b["id"] == "ab2");
        Assert.DoesNotContain(contacts.Cards(), c => (c["addressBookIds"] as JsonObject)?.ContainsKey("ab2") == true);
        Assert.Equal(1, server.Api.ContactSetRequests.Count(r => r.Args.ContainsKey("onDestroyRemoveContents")));
    }

    [Fact]
    public async Task A_read_only_login_cannot_write_a_book()
    {
        var rig = ContactRig.Create();
        var (key, server, _) = await rig.AddAsync("user@example.org", FakeSeed.ContactsM5(0), readOnly: true);
        var books = new AddressBooks(rig, new ContactStore(rig, rig.Hooks, rig.Time));
        Assert.False((await books.CreateAsync(key, "Nope", Ct)).IsOk);
        Assert.Empty(server.Api.ContactSetRequests);
    }
}
