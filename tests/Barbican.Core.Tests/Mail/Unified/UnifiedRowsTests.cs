// Ported from Rust src/unified.rs tests (:504-:601).

using System.Text.Json.Nodes;
using Barbican.Core.Mail.Unified;

namespace Barbican.Core.Tests.Mail.Unified;

public sealed class UnifiedRowsTests
{
    private static JsonNode J(string json) => JsonNode.Parse(json)!;

    [Fact]
    [RustTest("unified::other_logins_badge_sum")]
    public void Other_logins_badge_sum()
    {
        var m = new Dictionary<string, long>(StringComparer.Ordinal) { ["a@x"] = 3, ["b@y"] = 4, ["c@z"] = -1 };
        Assert.Equal(4, UnifiedRows.SumOtherLogins(m, "a@x"));
        Assert.Equal(7, UnifiedRows.SumOtherLogins(m, "nobody"));
        Assert.Equal(0, UnifiedRows.SumOtherLogins(new Dictionary<string, long>(StringComparer.Ordinal), "a@x"));
    }

    [Fact]
    [RustTest("unified::merge_orders_by_parsed_time_and_dedupes")]
    public void Merge_orders_by_parsed_time_and_dedupes()
    {
        List<JsonObject> v =
        [
            (JsonObject)J("""{"id": "a", "__src": "x", "receivedAt": "2024-05-01T10:00:00Z"}"""),

            // 11:30+02:00 is 09:30Z: older than "a" though its string sorts higher
            (JsonObject)J("""{"id": "b", "__src": "y", "receivedAt": "2024-05-01T11:30:00+02:00"}"""),
            (JsonObject)J("""{"id": "c", "__src": "y", "receivedAt": "2024-05-02T00:00:00Z"}"""),
            (JsonObject)J("""{"id": "a", "__src": "x", "receivedAt": "2024-05-01T10:00:00Z"}"""),
            (JsonObject)J("""{"id": "a", "__src": "y", "receivedAt": "2023-01-01T00:00:00Z"}"""),
        ];
        UnifiedRows.MergeSorted(v);
        var ids = v.Select(m => (string)m["__src"]! + (string)m["id"]!).ToList();
        Assert.Equal(["yc", "xa", "yb", "ya"], ids);
    }

    [Fact]
    [RustTest("unified::row_ids_keep_sources_apart")]
    public void Row_ids_keep_sources_apart()
    {
        var own = UnifiedRows.RowId("me@x", null, "b");
        var grp = UnifiedRows.RowId("me@x", "g1", "b");
        Assert.NotEqual(own, grp, StringComparer.Ordinal);
        Assert.Equal(new UnifiedRowId("me@x", null, "b"), UnifiedRows.ParseRow(own));
        Assert.Equal(new UnifiedRowId("me@x", "g1", "b"), UnifiedRows.ParseRow(grp));
        Assert.Equal("b", UnifiedRows.RealId(grp));
        Assert.Equal("plain", UnifiedRows.RealId("plain"));
        Assert.Null(UnifiedRows.ParseRow("plain"));
        Assert.Equal(UnifiedRows.RowId("me@x", "g1", "c"), UnifiedRows.SameSource(grp, "c"));
        Assert.Equal("c", UnifiedRows.SameSource("plain", "c"));

        // the same id from two sources stays two rows
        var a = (JsonObject)J("""{"id": "b", "threadId": "t"}""");
        var g = (JsonObject)J("""{"id": "b", "threadId": "t"}""");
        UnifiedRows.TagRow(a, "me@x", null, "me@x");
        UnifiedRows.TagRow(g, "me@x", "g1", "me@x#g1");
        Assert.NotEqual((string)a["id"]!, (string)g["id"]!, StringComparer.Ordinal);
        Assert.NotEqual((string)a["__tkey"]!, (string)g["__tkey"]!, StringComparer.Ordinal);
        Assert.Equal("b", (string)g["__id"]!);
        Assert.Equal("g1", (string)g["__jmap"]!);
    }

    [Fact]
    [RustTest("unified::dedupe_reads_each_account_once")]
    public void Dedupe_reads_each_account_once()
    {
        // (login, server, account, shared)
        (string Login, string Server, string Account, bool Shared)[] v =
        [
            ("b", "s1", "G", true),   // group via login b
            ("a", "s1", "A", false),  // a's own
            ("b", "s1", "B", false),  // b's own
            ("b", "s1", "A", true),   // a's account shared with b: a's own wins
            ("a", "s1", "G", true),   // the same group via login a: dropped
            ("c", "s2", "G", true),   // same id on another server: kept
        ];
        var output = UnifiedRows.DedupeBy(v, x => x.Shared, x => (x.Server, x.Account));
        Assert.Equal([("b", "G"), ("a", "A"), ("b", "B"), ("c", "G")], output.Select(x => (x.Login, x.Account)));
    }

    [Fact]
    [RustTest("unified::positions_count_per_source")]
    public void Positions_count_per_source()
    {
        List<JsonObject> v = [(JsonObject)J("""{"__src": "a"}"""), (JsonObject)J("""{"__src": "b"}"""), (JsonObject)J("""{"__src": "a"}"""), (JsonObject)J("{}")];
        var p = UnifiedRows.Positions(v);
        Assert.Equal(2, p["a"]);
        Assert.Equal(1, p["b"]);
        Assert.Equal(2, p.Count);
    }

    [Fact]
    [RustTest("unified::shared_accounts_from_session")]
    public void Shared_accounts_from_session()
    {
        var s = J("""
            {"accounts": {
              "own": {"isPersonal": true, "accountCapabilities": {"urn:ietf:params:jmap:mail": {}}},
              "grp": {"isPersonal": false, "accountCapabilities": {"urn:ietf:params:jmap:mail": {}}},
              "cal": {"isPersonal": false, "accountCapabilities": {"urn:ietf:params:jmap:calendars": {}}},
              "other": {"isPersonal": true, "accountCapabilities": {"urn:ietf:params:jmap:mail": {}}}
            }}
            """);
        Assert.Equal(["grp"], UnifiedRows.SharedAccounts(s, "own"));
        Assert.Equal("u#grp", UnifiedRows.SourceKey("u", "grp"));
        Assert.Equal("u", UnifiedRows.SourceKey("u", null));
    }

    [Fact]
    [RustTest("unified::match_by_role_then_name")]
    public void Match_by_role_then_name()
    {
        IReadOnlyList<JsonNode> l =
        [
            J("""{"id": "1", "role": "inbox", "name": "Inbox"}"""),
            J("""{"id": "2", "name": "Receipts"}"""),
            J("""{"id": "3", "role": "archive", "name": "Old"}"""),
        ];
        Assert.Equal("3", UnifiedRows.MatchMailbox(l, "archive", "Archive"));
        Assert.Equal("2", UnifiedRows.MatchMailbox(l, string.Empty, "receipts"));
        Assert.Null(UnifiedRows.MatchMailbox(l, "junk", "Spam"));
        var a = UnifiedRows.MoveToAction(string.Empty, "Receipts");
        Assert.StartsWith("moveto:", a, StringComparison.Ordinal);
        Assert.Equal(["", "Receipts"], a["moveto:".Length..].Split('\u001f'));
    }
}
