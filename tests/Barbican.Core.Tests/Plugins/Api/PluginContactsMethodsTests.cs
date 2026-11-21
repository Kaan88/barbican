// Source: Bulwark lib/plugin-sandbox/host-api.ts:178-186,1019-1090 (contact.get / update / create / search / list / delete and addressbook.list / create, contacts:read and contacts:write), lib/jmap/client.ts:8776-8995 (the calls on the
// server), lib/jmap/contact-wire.ts (contactToWire and contactFromWire), lib/jmap/types.ts:339-387 (ContactCard). The real dispatcher over the real contact store, writer, address-book service and hook bus on the fake JMAP server.
#pragma warning disable MA0006, MA0002 // assertions compare string literals

using System.Text.Json.Nodes;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Tests.Contacts;
using Barbican.FakeJmap;
using Rig = Barbican.Core.Tests.Plugins.Api.PluginContactsRig;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class PluginContactsMethodsTests
{
    private static readonly string[] AdaKeys = ["@type", "addressBookIds", "anniversaries", "emails", "id", "keywords", "name", "organizations", "phones", "uid", "updated", "version"];
    private static readonly string[] BookKeys = ["description", "id", "isDefault", "isSubscribed", "myRights", "name", "sortOrder"];

    private static JsonObject Card(Rig rig, string id) => rig.Contacts.Card(id);

    [Fact]
    public async Task Address_books_list_with_exactly_the_documented_fields_and_no_share_list()
    {
        var rig = await Rig.StartAsync();
        var books = (await rig.CallAsync("addressbook.list", "[]")).Value!;
        Assert.Equal(["ab1", "ab2"], Rig.Ids(books));
        Assert.Equal(BookKeys, Rig.Keys(books.Item(0)), StringComparer.Ordinal);
        Assert.Equal(["mayDelete", "mayRead", "mayShare", "mayWrite"], Rig.Keys(books.Item(0).Property("myRights")), StringComparer.Ordinal);
        Assert.True(books.Item(0).Property("isDefault").BooleanValue);
    }

    [Fact]
    public async Task Cards_list_whole_or_by_book_with_the_card_fields_a_plugin_may_see()
    {
        var rig = await Rig.StartAsync();
        var all = (await rig.CallAsync("contact.list", "[]")).Value!;
        Assert.Equal(["cc-ada", "cc-anna", "cc-anna2", "cc-babbage", "cc-friends", "cc-photo"], Rig.Ids(all));
        Assert.Equal(["cc-anna2", "cc-babbage"], Rig.Ids((await rig.CallAsync("contact.list", """["ab2"]""")).Value!));
        Assert.Equal(6, (await rig.CallAsync("contact.list", "[null]")).Value!.Count);
        Assert.Equal(0, (await rig.CallAsync("contact.list", """["nope"]""")).Value!.Count);
        var ada = Enumerable.Range(0, all.Count).Select(all.Item).First(c => c.Property("id").StringValue == "cc-ada");
        Assert.Equal(AdaKeys, Rig.Keys(ada), StringComparer.Ordinal);
        Assert.Equal("Ada Lovelace", ada.Property("name").Property("full").StringValue, StringComparer.Ordinal);
        Assert.Equal("ada@analytical.example", ada.Property("emails").Property("e0").Property("address").StringValue, StringComparer.Ordinal);
        foreach (var bad in new[] { "[5]", "[{}]", "[\"\"]" }) Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("contact.list", bad)).Error);
    }

    [Fact]
    public async Task A_list_of_a_book_reads_only_the_login_s_own_account_and_never_leaks_bookkeeping_keys()
    {
        var rig = await Rig.StartAsync();
        var all = (await rig.CallAsync("contact.list", "[]")).Value!;
        for (var i = 0; i < all.Count; i++)
        {
            Assert.DoesNotContain(Rig.Keys(all.Item(i)), k => k.StartsWith('_') || k is "accountId" or "accountName" or "originalId" or "isShared");
        }
    }

    [Fact]
    public async Task Get_returns_the_card_or_null_and_keeps_the_photo()
    {
        var rig = await Rig.StartAsync();
        var photo = (await rig.CallAsync("contact.get", """["cc-photo"]""")).Value!;
        Assert.Equal("image/png", photo.Property("media").Property("m0").Property("mediaType").StringValue, StringComparer.Ordinal);
        Assert.Equal(PluginValueKind.Null, (await rig.CallAsync("contact.get", """["nope"]""")).Value!.Kind);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("contact.get", "[]")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("contact.get", "[1]")).Error);
    }

    [Fact]
    public async Task Search_ignores_case_and_accents_and_answers_at_most_fifty_hits_per_account()
    {
        var rig = await Rig.StartAsync();
        Assert.Equal(["cc-anna", "cc-anna2"], Rig.Ids((await rig.CallAsync("contact.search", """["MULLER"]""")).Value!));
        Assert.Equal(["cc-ada", "cc-babbage"], Rig.Ids((await rig.CallAsync("contact.search", """["analytical"]""")).Value!));
        Assert.Equal(0, (await rig.CallAsync("contact.search", """["zzz-nobody"]""")).Value!.Count);
        foreach (var bad in new[] { "[]", "[\"\"]", "[\"   \"]", "[5]", "[\"" + new string('x', 201) + "\"]" }) Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("contact.search", bad)).Error);
        var many = await Rig.StartAsync(contacts: FakeSeed.ContactsM5(80));
        Assert.Equal(50, (await many.CallAsync("contact.search", """["bulk"]""")).Value!.Count);
    }

    [Fact]
    public async Task A_neighbouring_permission_is_denied_before_any_server_call()
    {
        var reader = await Rig.StartAsync(["contacts:read"]);
        foreach (var (method, args) in new[] { ("contact.create", """[{"name":{"full":"X"}}]"""), ("contact.update", """["cc-ada",{"language":"de"}]"""), ("contact.delete", """["cc-ada"]"""), ("addressbook.create", """["Club"]""") })
        {
            Assert.Equal(PluginErrorCode.PermissionDenied, (await reader.CallAsync(method, args)).Error);
        }

        Assert.Equal(0, reader.Writer.Calls);
        Assert.Equal(0, reader.Sets);
        Assert.Empty(reader.Server.ApiCalls);
        var writer = await Rig.StartAsync(["contacts:write"]);
        foreach (var (method, args) in new[] { ("contact.list", "[]"), ("contact.get", """["cc-ada"]"""), ("contact.search", """["ada"]"""), ("addressbook.list", "[]") })
        {
            Assert.Equal(PluginErrorCode.PermissionDenied, (await writer.CallAsync(method, args)).Error);
        }

        Assert.Empty(writer.Server.ApiCalls);
    }

    [Fact]
    public async Task A_created_card_goes_to_the_default_book_runs_the_hooks_and_comes_back_with_its_id()
    {
        var rig = await Rig.StartAsync();
        var created = await rig.CallAsync("contact.create", """
            [{"name":{"full":"Grace Hopper"},"emails":{"e0":{"address":"grace@navy.example"}},"keywords":{"pioneer":true},
              "addresses":{"a0":{"street":"1 Cobol Way","locality":"Arlington","country":"US"}},"calendarUri":"https://cal.example/grace","id":"ignored","created":"2020-01-01T00:00:00Z"}]
            """);
        Assert.True(created.IsSuccess);
        var view = created.Value!;
        var id = view.Property("id").StringValue;
        var stored = Card(rig, id);
        Assert.Equal("Grace Hopper", (string?)stored["name"]!["full"]);
        Assert.True((bool?)stored["addressBookIds"]!["ab1"]);
        Assert.StartsWith("urn:uuid:", (string?)stored["uid"], StringComparison.Ordinal);
        Assert.Equal("https://cal.example/grace", (string?)stored["calendars"]!["cal"]!["uri"]);
        Assert.Equal("name", (string?)stored["addresses"]!["a0"]!["components"]![0]!["kind"]);
        Assert.Equal("https://cal.example/grace", view.Property("calendarUri").StringValue, StringComparer.Ordinal);
        Assert.DoesNotContain("created", Rig.Keys(view));
        var names = rig.Accounts.Hooks.Names;
        Assert.Contains(HookNames.OnBeforeContactCreate, names);
        _ = await rig.Accounts.Hooks.PayloadAsync(HookNames.OnAfterContactCreate);
        var work = await rig.CallAsync("contact.create", """[{"name":{"full":"Work Person"},"addressBookIds":{"ab2":true}}]""");
        Assert.True(work.IsSuccess);
        Assert.Contains("ab2", Card(rig, work.Value!.Property("id").StringValue)["addressBookIds"]!.AsObject().Select(p => p.Key));
    }

    [Fact]
    public async Task Create_refuses_unknown_properties_wrong_shapes_and_several_books_before_anything_is_written()
    {
        var rig = await Rig.StartAsync();
        string[] bad =
        [
            "[]", "[1]", """[{"bogus":1}]""", """[{"_acc":"x"}]""", """[{"name":"Ada"}]""", """[{"emails":{"e":"x"}}]""", """[{"keywords":{"a":"yes"}}]""", """[{"kind":5}]""",
            """[{"addressBookIds":{"ab1":true,"ab2":true}}]""", """[{"name":{"full":"x"},"calendarUri":5}]""", "[{\"notes\":{\"n\":{\"note\":\"" + new string('n', 70_000) + "\"}}}]",
        ];
        foreach (var args in bad) Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("contact.create", args)).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("contact.create", """[{"name":{"full":"x"},"addressBookIds":{"nope":true}}]""")).Error);
        Assert.Equal(0, rig.Sets);
        Assert.Equal(0, rig.Writer.Calls);
    }

    [Fact]
    public async Task Rights_of_a_book_refuse_a_write_before_the_writer_is_asked()
    {
        var contacts = FakeSeed.ContactsM5(0);
        var locked = contacts.AddBook("Locked", extra: new JsonObject { ["myRights"] = new JsonObject { ["mayRead"] = true, ["mayWrite"] = false, ["mayDelete"] = false } });
        var keep = contacts.AddBook("Keep", extra: new JsonObject { ["myRights"] = new JsonObject { ["mayRead"] = true, ["mayWrite"] = true, ["mayDelete"] = false } });
        _ = contacts.AddCard(locked, new JsonObject { ["id"] = "cc-locked", ["name"] = new JsonObject { ["full"] = "Locked" } });
        _ = contacts.AddCard(keep, new JsonObject { ["id"] = "cc-keep", ["name"] = new JsonObject { ["full"] = "Keep" } });
        var rig = await Rig.StartAsync(contacts: contacts);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("contact.create", $$$"""[{"name":{"full":"x"},"addressBookIds":{"{{{locked}}}":true}}]""")).Error);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("contact.update", """["cc-locked",{"language":"de"}]""")).Error);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("contact.delete", """["cc-locked"]""")).Error);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("contact.delete", """["cc-keep"]""")).Error);
        Assert.Equal(0, rig.Writer.Calls);
        Assert.Equal(0, rig.Sets);
        Assert.True((await rig.CallAsync("contact.update", """["cc-keep",{"language":"de"}]""")).IsSuccess);
        Assert.Equal("de", (string?)Card(rig, "cc-keep")["language"]);
    }

    [Fact]
    public async Task Update_replaces_the_given_properties_clears_with_null_and_never_changes_the_uid_or_the_book()
    {
        var rig = await Rig.StartAsync();
        Assert.True((await rig.CallAsync("contact.update", """["cc-ada",{"name":{"full":"Ada King"},"keywords":null,"phones":null,"id":"cc-other","created":"x","accountName":"y"}]""")).IsSuccess);
        var ada = Card(rig, "cc-ada");
        Assert.Equal("Ada King", (string?)ada["name"]!["full"]);
        Assert.Null(ada["keywords"]);
        Assert.Null(ada["phones"]);
        Assert.NotNull(ada["emails"]);
        Assert.Contains(HookNames.OnBeforeContactUpdate, rig.Accounts.Hooks.Names);
        _ = await rig.Accounts.Hooks.PayloadAsync(HookNames.OnAfterContactUpdate);
        Assert.True((await rig.CallAsync("contact.update", $$$"""["cc-ada",{"uid":"{{{FakeSeed.ContactAdaUid}}}","addressBookIds":{"ab1":true}}]""")).IsSuccess);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("contact.update", """["cc-ada",{"uid":"urn:uuid:other"}]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("contact.update", """["cc-ada",{"addressBookIds":{"ab2":true}}]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("contact.update", """["cc-ada",{"bogus":true}]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("contact.update", """["cc-ada"]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("contact.update", """["nope",{"language":"de"}]""")).Error);
        var sets = rig.Sets;
        Assert.True((await rig.CallAsync("contact.update", """["cc-ada",{}]""")).IsSuccess);
        Assert.Equal(sets, rig.Sets);
    }

    [Fact]
    public async Task Another_add_on_stopping_a_write_fails_the_call_and_leaves_the_card()
    {
        var rig = await Rig.StartAsync();
        rig.Accounts.Hooks.Stop(HookNames.OnBeforeContactUpdate);
        rig.Accounts.Hooks.Stop(HookNames.OnBeforeContactDelete);
        rig.Accounts.Hooks.Stop(HookNames.OnBeforeContactCreate);
        Assert.Equal(PluginErrorCode.Failed, (await rig.CallAsync("contact.update", """["cc-ada",{"language":"de"}]""")).Error);
        Assert.Equal(PluginErrorCode.Failed, (await rig.CallAsync("contact.delete", """["cc-ada"]""")).Error);
        Assert.Equal(PluginErrorCode.Failed, (await rig.CallAsync("contact.create", """[{"name":{"full":"x"}}]""")).Error);
        Assert.Equal(0, rig.Sets);
        Assert.Null(Card(rig, "cc-ada")["language"]);
    }

    [Fact]
    public async Task Delete_removes_the_card_through_the_writer_and_an_unknown_id_is_unavailable()
    {
        var rig = await Rig.StartAsync();
        Assert.True((await rig.CallAsync("contact.delete", """["cc-babbage"]""")).IsSuccess);
        Assert.DoesNotContain(rig.Contacts.Cards(), c => (string?)c["id"] == "cc-babbage");
        _ = await rig.Accounts.Hooks.PayloadAsync(HookNames.OnAfterContactDelete);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("contact.delete", """["cc-babbage"]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("contact.delete", "[]")).Error);
        Assert.Equal(["cc-ada", "cc-anna", "cc-anna2", "cc-friends", "cc-photo"], Rig.Ids((await rig.CallAsync("contact.list", "[]")).Value!));
    }

    [Fact]
    public async Task A_created_address_book_comes_back_and_a_blank_name_is_refused()
    {
        var rig = await Rig.StartAsync();
        var made = (await rig.CallAsync("addressbook.create", """[" Club "]""")).Value!;
        Assert.Equal(BookKeys.Where(k => k != "myRights" || made.TryGetProperty("myRights", out _)).Order(StringComparer.Ordinal), Rig.Keys(made), StringComparer.Ordinal);
        Assert.Equal("Club", made.Property("name").StringValue, StringComparer.Ordinal);
        Assert.Contains(rig.Contacts.Books(), b => (string?)b["name"] == "Club");
        foreach (var bad in new[] { "[]", "[\"\"]", "[\"  \"]", "[5]", "[\"" + new string('x', 256) + "\"]" }) Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("addressbook.create", bad)).Error);
        Assert.Equal(3, (await rig.CallAsync("addressbook.list", "[]")).Value!.Count);
    }

    [Fact]
    public async Task A_login_that_went_stale_before_a_write_stops_it_and_one_that_changed_during_it_reports_an_unknown_outcome()
    {
        var before = await Rig.StartAsync();
        before.Store.OnSnapshot = () => before.Sessions.Valid = false;
        Assert.Equal(PluginErrorCode.StaleContext, (await before.CallAsync("contact.delete", """["cc-ada"]""")).Error);
        before.Sessions.Valid = true;
        Assert.Equal(PluginErrorCode.StaleContext, (await before.CallAsync("contact.list", "[]")).Error);
        Assert.Equal(0, before.Writer.Calls);
        Assert.Equal(0, before.Sets);
        Assert.NotNull(Card(before, "cc-ada"));

        var during = await Rig.StartAsync();
        during.Writer.Before = () => during.Sessions.Valid = false;
        var result = await during.CallAsync("contact.create", """[{"name":{"full":"x"}}]""");
        Assert.Equal(PluginErrorCode.UnknownOutcome, result.Error);
        Assert.Null(result.Value);

        var revoked = await Rig.StartAsync();
        revoked.Writer.Before = () => revoked.Authority.Current = Rig.Grant(revoked.Key, "contacts:read");
        Assert.Equal(PluginErrorCode.UnknownOutcome, (await revoked.CallAsync("contact.delete", """["cc-ada"]""")).Error);
    }

    [Fact]
    public async Task A_disabled_add_on_gets_nothing()
    {
        var rig = await Rig.StartAsync();
        rig.Authority.Current = new(rig.Authority.Current.Package, 2, false, ["contacts:read", "contacts:write"], [rig.Key]);
        Assert.False((await rig.CallAsync("contact.list", "[]")).IsSuccess);
        Assert.False((await rig.CallAsync("contact.delete", """["cc-ada"]""")).IsSuccess);
        Assert.Equal(0, rig.Writer.Calls);
        Assert.Empty(rig.Server.ApiCalls);
    }
}
