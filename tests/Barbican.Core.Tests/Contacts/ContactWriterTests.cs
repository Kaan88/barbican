// M5 con-data acceptance tests of the contact writer over FakeJmap: create (default book once), update with pointer patches and the whole keywords map, rollback, delete, move, groups, merge order, import in chunks, and the 11 hooks (M5 plan 6.1 step 2, 2.4; J-071, B-CON-003, B-CON-006, B-CON-016, B-CON-024).

#pragma warning disable MA0006, MA0002 // assertions compare string literals

using System.Text.Json.Nodes;
using Barbican.Core.Contacts;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Contacts;

public sealed class ContactWriterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Setup(ContactRig Rig, string Key, FakeJmapServer Server, FakeContacts Contacts, ContactStore Store, ContactWriter Writer);

    private static async Task<Setup> StartAsync(FakeContacts contacts, bool read = true)
    {
        var rig = ContactRig.Create();
        var (key, server, _) = await rig.AddAsync("user@example.org", contacts);
        var store = new ContactStore(rig, rig.Hooks, rig.Time);
        if (read)
        {
            _ = await store.LoadAsync(key, Ct);
        }

        var writer = new ContactWriter(rig, store, new AddressBooks(rig, store), rig.Hooks);
        return new Setup(rig, key, server, contacts, store, writer);
    }

    [Fact]
    public async Task A_contact_is_created_in_the_default_book_with_a_uid_and_the_hooks_see_it()
    {
        var s = await StartAsync(FakeSeed.ContactsM5(0));
        var r = await s.Writer.CreateAsync(s.Key, null, new JsonObject { ["name"] = new JsonObject { ["full"] = "New One" }, ["emails"] = new JsonObject { ["e0"] = new JsonObject { ["address"] = "new@x.example" } }, ["notes"] = null }, Ct);
        Assert.True(r.IsOk);
        var card = s.Contacts.Card(r.Value!);
        Assert.StartsWith("urn:uuid:", (string?)card["uid"], StringComparison.Ordinal);
        Assert.True((bool?)card["addressBookIds"]!["ab1"]);
        Assert.Null(card["notes"]);
        Assert.Contains(s.Store.Snapshot(s.Key)!.Cards, c => (string?)c["id"] == r.Value);
        var after = (ContactCardPayload)(await s.Rig.Hooks.PayloadAsync("onAfterContactCreate"))!;
        Assert.Equal(r.Value, after.ContactId);
        Assert.Equal(["onBeforeContactCreate", "onAfterContactCreate"], s.Rig.Hooks.Names.Where(n => n.Contains("ContactCreate", StringComparison.Ordinal)).ToArray());
    }

    [Fact]
    public async Task A_plugin_can_stop_a_create_an_update_and_a_delete_before_anything_is_sent()
    {
        var s = await StartAsync(FakeSeed.ContactsM5(0), read: true);
        s.Rig.Hooks.Stop(HookNames.OnBeforeContactCreate);
        s.Rig.Hooks.Stop(HookNames.OnBeforeContactUpdate);
        s.Rig.Hooks.Stop(HookNames.OnBeforeContactDelete);
        Assert.False((await s.Writer.CreateAsync(s.Key, "ab1", new JsonObject { ["name"] = new JsonObject { ["full"] = "X" } }, Ct)).IsOk);
        Assert.False((await s.Writer.UpdateAsync(s.Key, FakeSeed.ContactAdaId, new JsonObject { ["name/full"] = "X" }, Ct)).IsOk);
        Assert.False((await s.Writer.DeleteAsync(s.Key, [FakeSeed.ContactAdaId], Ct)).IsOk);
        Assert.Empty(s.Server.Api.ContactSetRequests);
    }

    [Fact]
    public async Task The_first_contact_of_an_account_without_a_book_makes_one_default_book_and_never_a_second()
    {
        var s = await StartAsync(new FakeContacts(), read: false);
        var first = await s.Writer.CreateAsync(s.Key, null, new JsonObject { ["name"] = new JsonObject { ["full"] = "One" } }, Ct);
        var second = await s.Writer.CreateAsync(s.Key, null, new JsonObject { ["name"] = new JsonObject { ["full"] = "Two" } }, Ct);
        Assert.True(first.IsOk && second.IsOk);
        var book = Assert.Single(s.Contacts.Books());
        Assert.Equal("Contacts", (string?)book["name"]);
        Assert.True((bool?)book["isDefault"]);
        Assert.Equal(1, s.Server.Api.ContactSetRequests.Count(r => r.Type == "AddressBook"));
    }

    [Fact]
    public async Task An_update_sends_pointer_patches_and_the_whole_map_when_the_card_lacks_it()
    {
        var s = await StartAsync(FakeSeed.ContactsM5(0));
        var r = await s.Writer.UpdateAsync(s.Key, FakeSeed.ContactBabbageId, new JsonObject { ["name/full"] = "C. Babbage", ["keywords/Club"] = true }, Ct);
        Assert.True(r.IsOk);
        var sent = s.Server.Api.ContactSetRequests[^1].Args["update"]![FakeSeed.ContactBabbageId]!.AsObject();
        Assert.Equal("C. Babbage", (string?)sent["name/full"]);
        Assert.True((bool?)sent["keywords"]!["Club"]);
        Assert.Null(sent["keywords/Club"]);
        Assert.True((bool?)s.Contacts.Card(FakeSeed.ContactBabbageId)["keywords"]!["Club"]);

        // a card that has keywords takes a pointer
        var again = await s.Writer.UpdateAsync(s.Key, FakeSeed.ContactAdaId, new JsonObject { ["keywords/Friend"] = true }, Ct);
        Assert.True(again.IsOk);
        Assert.True((bool?)s.Server.Api.ContactSetRequests[^1].Args["update"]![FakeSeed.ContactAdaId]!["keywords/Friend"]);
        _ = await s.Rig.Hooks.PayloadAsync("onAfterContactUpdate", 2);
    }

    [Fact]
    public async Task A_refused_update_puts_the_store_back_and_says_why()
    {
        var s = await StartAsync(FakeSeed.ContactsM5(0));
        s.Server.Api.FailNextContactSet("forbidden");
        var r = await s.Writer.UpdateAsync(s.Key, FakeSeed.ContactAdaId, new JsonObject { ["name/full"] = "Nope" }, Ct);
        Assert.False(r.IsOk);
        Assert.Equal("Ada Lovelace", (string?)s.Store.Snapshot(s.Key)!.Cards.First(c => (string?)c["id"] == FakeSeed.ContactAdaId)["name"]!["full"]);
        Assert.DoesNotContain(s.Rig.Hooks.Names, n => n == "onAfterContactUpdate");
    }

    [Fact]
    public async Task Delete_move_and_the_hooks_of_both()
    {
        var s = await StartAsync(FakeSeed.ContactsM5(0));
        var moved = await s.Writer.MoveAsync(s.Key, [FakeSeed.ContactAdaId, FakeSeed.ContactPhotoId], "ab2", Ct);
        Assert.True(moved.IsOk);
        Assert.True((bool?)s.Contacts.Card(FakeSeed.ContactAdaId)["addressBookIds"]!["ab2"]);
        Assert.Null(s.Contacts.Card(FakeSeed.ContactAdaId)["addressBookIds"]!["ab1"]);
        _ = await s.Rig.Hooks.PayloadAsync("onContactMove", 2);

        var gone = await s.Writer.DeleteAsync(s.Key, [FakeSeed.ContactAdaId, FakeSeed.ContactGroupId], Ct);
        Assert.True(gone.IsOk);
        Assert.DoesNotContain(s.Contacts.Cards(), c => (string?)c["id"] == FakeSeed.ContactAdaId);
        Assert.DoesNotContain(s.Store.Snapshot(s.Key)!.Cards, c => (string?)c["id"] == FakeSeed.ContactGroupId);
        var deleted = (ContactDeletePayload)(await s.Rig.Hooks.PayloadAsync("onAfterContactDelete"))!;
        Assert.Equal(2, deleted.ContactIds.Count);
        var group = (ContactGroupChangePayload)(await s.Rig.Hooks.PayloadAsync("onContactGroupChange"))!;
        Assert.Equal(("cc-friends", "deleted"), (group.GroupId, group.Change));
    }

    [Fact]
    public async Task A_group_is_created_and_its_members_added_and_removed_under_the_key_the_group_uses()
    {
        var s = await StartAsync(FakeSeed.ContactsM5(0));
        var created = await s.Writer.CreateGroupAsync(s.Key, null, "Club", [FakeSeed.ContactAdaUid], Ct);
        Assert.True(created.IsOk);
        var card = s.Contacts.Card(created.Value!);
        Assert.Equal("group", (string?)card["kind"]);
        Assert.True((bool?)card["members"]![FakeSeed.ContactAdaUid]);

        // the group lists Babbage as "urn:uuid:2222..." in the seed; removal finds the key by the bare uid
        var bare = FakeSeed.ContactBabbageUid["urn:uuid:".Length..];
        var r = await s.Writer.SetMembersAsync(s.Key, FakeSeed.ContactGroupId, ["new-member"], [bare], Ct);
        Assert.True(r.IsOk);
        var members = s.Contacts.Card(FakeSeed.ContactGroupId)["members"]!.AsObject();
        Assert.True((bool?)members["new-member"]);
        Assert.False(members.ContainsKey(FakeSeed.ContactBabbageUid));
        var change = (ContactGroupMembersPayload)(await s.Rig.Hooks.PayloadAsync("onContactGroupMemberChange"))!;
        Assert.Equal((FakeSeed.ContactGroupId, "new-member", bare), (change.GroupId, Assert.Single(change.Added), Assert.Single(change.Removed)));
        _ = await s.Rig.Hooks.PayloadAsync("onContactGroupChange", 1, p => ((ContactGroupChangePayload)p!).Change == "created");

        // a group without members takes its first one as a whole map
        var empty = await s.Writer.CreateGroupAsync(s.Key, null, "Empty", [], Ct);
        var second = await s.Writer.SetMembersAsync(s.Key, empty.Value!, ["m1"], [], Ct);
        Assert.True(second.IsOk);
        Assert.True((bool?)s.Contacts.Card(empty.Value!)["members"]!["m1"]);
    }

    [Fact]
    public async Task A_merge_patches_the_kept_card_then_moves_the_groups_then_deletes_the_others()
    {
        var s = await StartAsync(FakeSeed.ContactsM5(0));
        var cards = s.Store.Snapshot(s.Key)!.Cards;
        var anna = cards.First(c => (string?)c["id"] == FakeSeed.ContactAnnaId);
        var twin = cards.First(c => (string?)c["id"] == FakeSeed.ContactAnnaTwinId);
        var groupId = s.Contacts.AddCard("ab1", new JsonObject { ["kind"] = "group", ["name"] = new JsonObject { ["full"] = "Annas" }, ["members"] = new JsonObject { [(string)anna["uid"]!] = true } });
        _ = await s.Store.RefreshAsync(s.Key, Ct);
        var plan = ContactMerge.Plan([anna, twin])!;
        Assert.Equal(FakeSeed.ContactAnnaTwinId, (string?)plan.Keep["id"]);
        var groups = s.Store.Snapshot(s.Key)!.Cards.Where(c => (string?)c["kind"] == "group").ToList();

        var done = await s.Writer.MergeAsync(s.Key, plan, groups, Ct);
        Assert.True(done.IsOk);
        var sent = s.Server.Api.ContactSetRequests.Where(r => r.Type == "ContactCard").Select(r => r.Args.ContainsKey("destroy") ? "destroy" : "update:" + string.Join(',', r.Args["update"]!.AsObject().Select(u => u.Key))).ToList();
        Assert.Equal(["update:" + FakeSeed.ContactAnnaTwinId, "update:" + groupId, "destroy"], sent);
        Assert.DoesNotContain(s.Contacts.Cards(), c => (string?)c["id"] == FakeSeed.ContactAnnaId);
        Assert.Contains(s.Contacts.Card(FakeSeed.ContactAnnaTwinId)["emails"]!.AsObject(), e => (string?)e.Value!["address"] == "anna@mueller.example" || (string?)e.Value!["address"] == "ANNA@mueller.example");
        var members = s.Contacts.Card(groupId)["members"]!.AsObject();
        Assert.False(members.ContainsKey((string)anna["uid"]!));
        Assert.True(members.Count == 1);
    }

    [Fact]
    public async Task An_import_goes_in_chunks_of_a_hundred_and_a_refused_chunk_is_counted_not_fatal()
    {
        var s = await StartAsync(FakeSeed.ContactsM5(0));
        IReadOnlyList<JsonNode> Cards(int n) => [.. Enumerable.Range(0, n).Select(i => (JsonNode)new JsonObject { ["name"] = new JsonObject { ["full"] = "Imported " + i } })];
        var ok = await s.Writer.ImportAsync(s.Key, "ab2", Cards(250), Ct);
        Assert.True(ok.IsOk);
        Assert.Equal(250, ok.Value);
        Assert.Equal(3, s.Server.Api.ContactSetRequests.Count(r => r.Type == "ContactCard"));
        Assert.Equal(250, s.Contacts.Cards().Count(c => (c["addressBookIds"] as JsonObject)?.ContainsKey("ab2") == true && ((string?)c["name"]?["full"])?.StartsWith("Imported", StringComparison.Ordinal) == true));
        var payload = (ContactsImportPayload)(await s.Rig.Hooks.PayloadAsync("onContactsImport"))!;
        Assert.Equal(("ab2", 250), (payload.AddressBookId, payload.Imported));

        s.Server.Api.FailNextContactSet("invalidProperties", skip: 1);
        var partial = await s.Writer.ImportAsync(s.Key, "ab2", Cards(250), Ct);
        Assert.False(partial.IsOk);
        Assert.Equal(150, partial.Value);
    }
}
