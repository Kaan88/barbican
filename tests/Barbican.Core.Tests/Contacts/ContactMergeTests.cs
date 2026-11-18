// Ported from Rust src/contact_merge.rs tests :234 finds_groups_by_name_email_phone (T-067) and :250 merge_keeps_newest_and_adds_missing (T-068); M5 stream con-data. AddressBooks.DefaultBook is B-CON-003 (not a Rust test).

using System.Text.Json.Nodes;
using Barbican.Core.Contacts;
using static Barbican.Core.Tests.Contacts.ContactTestData;

namespace Barbican.Core.Tests.Contacts;

public sealed class ContactMergeTests
{
    [Fact]
    [RustTest("contact_merge::finds_groups_by_name_email_phone")]
    public void Finds_groups_by_name_email_phone()
    {
        var cards = new[]
        {
            J("""{"id": "1", "name": {"full": "Anna Müller"}}"""),
            J("""{"id": "2", "name": {"components": [{"kind": "surname", "value": "Muller"}, {"kind": "given", "value": "anna"}]}}"""),
            J("""{"id": "3", "name": {"full": "Bob"}, "emails": {"e": {"address": "bob@x.example"}}}"""),
            J("""{"id": "4", "name": {"full": "Robert"}, "emails": {"e": {"address": "BOB@x.example"}}, "phones": {"p": {"number": "+49 30 5550000"}}}"""),
            J("""{"id": "5", "name": {"full": "R."}, "phones": {"p": {"number": "030 5550000"}}}"""),
            J("""{"id": "6", "name": {"full": "Alone"}}"""),
            J("""{"id": "7", "kind": "group", "name": {"full": "Alone"}}"""),
        };
        Assert.Equal("0,1;2,3,4", Show(ContactMerge.FindGroups(cards)));
        Assert.Equal(string.Empty, ContactMerge.PhoneKey("12"));
    }

    [Fact]
    [RustTest("contact_merge::merge_keeps_newest_and_adds_missing")]
    public void Merge_keeps_newest_and_adds_missing()
    {
        var a = J("""
            {"id": "a", "uid": "ua", "updated": "2024-01-01T00:00:00Z", "name": {"full": "Ann"},
             "emails": {"e1": {"address": "ann@x.example", "pref": 1}}, "keywords": {"VIP": true}}
            """);
        var b = J("""
            {"id": "b", "uid": "urn:uuid:ub", "updated": "2025-01-01T00:00:00Z", "name": {"full": "Ann B"},
             "emails": {"e1": {"address": "ANN@x.example"}}, "phones": {"p1": {"number": "555 1234"}},
             "media": {"m": {"kind": "photo", "uri": "data:image/png;base64,AA"}}}
            """);
        var p = ContactMerge.Plan([a, b])!;
        Assert.Equal("b", (string?)p.Keep["id"]);
        Assert.Equal("a", (string?)p.Others[0]["id"]);
        // email already there (case-insensitive): not added; keywords added
        Assert.False(p.Patch.ContainsKey("emails"));
        Assert.True((bool?)p.Patch["keywords"]!["VIP"]);
        Assert.False(p.Patch.ContainsKey("name"));
        Assert.False(p.Patch.ContainsKey("phones"));
        // the other way round the phone and photo move over, key clash renamed
        var c = J("""{"id": "c", "updated": "2026-01-01T00:00:00Z", "name": {"full": "Ann C"}, "phones": {"p1": {"number": "999 0000"}}}""");
        var p2 = ContactMerge.Plan([b, c])!;
        Assert.Equal("c", (string?)p2.Keep["id"]);
        Assert.Equal("555 1234", (string?)p2.Patch["phones"]!["p1m1"]!["number"]);
        Assert.IsType<JsonObject>(p2.Patch["media"]);
        Assert.Equal("ANN@x.example", (string?)p2.Patch["emails"]!["e1"]!["address"]);

        var groups = new[] { J("""{"id": "g1", "kind": "group", "members": {"ub": true, "other": true}}"""), J("""{"id": "g2", "kind": "group", "members": {"x": true}}""") };
        var moves = ContactMerge.GroupMoves(groups, c, [b]);
        var move = Assert.Single(moves);
        Assert.Equal("g1", move.GroupId);
        Assert.Null(move.Members["ub"]);
        Assert.True((bool?)move.Members["c"]);
        Assert.True((bool?)move.Members["other"]);
    }

    [Fact]
    public void The_default_book_is_the_flagged_one_then_Contacts_then_the_first_and_never_a_duplicate()
    {
        var flagged = J("""{"id": "b2", "name": "Work", "isDefault": true}""");
        var named = J("""{"id": "b1", "name": "contacts"}""");
        var other = J("""{"id": "b3", "name": "Club"}""");
        Assert.Equal("b2", (string?)AddressBooks.DefaultBook([other, named, flagged])!["id"]);
        Assert.Equal("b1", (string?)AddressBooks.DefaultBook([other, named])!["id"]);
        Assert.Equal("b3", (string?)AddressBooks.DefaultBook([other])!["id"]);
        Assert.Null(AddressBooks.DefaultBook([]));
    }
}
