// Ported from Rust src/jmap.rs tests parse_changes_reads_response (:1736), object_delta_merges_partial_updates (:1752) and
// counts_only_update_of_unknown_folder_fetches_it_whole (:1900). Red until the jmap stream implements ChangesParser and ObjectDelta.

using System.Text.Json.Nodes;
using Barbican.Core.Jmap.State;
using Barbican.Core.Sync;

namespace Barbican.Core.Tests.Sync;

public sealed class ChangesTests
{
    private static JsonObject Obj(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    [RustTest("jmap::parse_changes_reads_response")]
    [Red("jmap")]
    public void Parse_changes_reads_response()
    {
        var r = JsonNode.Parse(
            """
            ["Mailbox/changes", {"accountId": "a", "oldState": "1", "newState": "2", "hasMoreChanges": false,
              "created": ["x"], "updated": ["y"], "destroyed": ["z"], "updatedProperties": ["totalEmails", "unreadEmails"]}, "c"]
            """)!.AsArray();
        var c = ChangesParser.Parse(r, "1", false)!;
        Assert.Equal(["x"], c.Created);
        Assert.Equal(["y"], c.Updated);
        Assert.Equal(["z"], c.Destroyed);
        Assert.Equal("2", c.NewState);
        Assert.Equal(2, c.UpdatedProperties!.Count);

        // cannotCalculateChanges and friends: no delta
        Assert.Null(ChangesParser.Parse(JsonNode.Parse("""["error", {"type": "cannotCalculateChanges"}, "c"]""")!.AsArray(), "1", false));

        // a Stalwart state that went backwards: no delta
        var back = JsonNode.Parse(
            $$"""["Email/changes", {"oldState": "{{StalwartState.Encode(9)}}", "newState": "{{StalwartState.Encode(3)}}"}, "c"]""")!.AsArray();
        Assert.Null(ChangesParser.Parse(back, StalwartState.Encode(9), true));
        Assert.NotNull(ChangesParser.Parse(back, StalwartState.Encode(9), false));
    }

    [Fact]
    [RustTest("jmap::object_delta_merges_partial_updates")]
    [Red("jmap")]
    public void Object_delta_merges_partial_updates()
    {
        List<JsonObject> have = [Obj("""{"id": "a", "name": "Inbox", "unreadEmails": 1}"""), Obj("""{"id": "b", "name": "Old"}""")];
        var output = ObjectDelta.Apply(have, ["b"], [Obj("""{"id": "a", "unreadEmails": 5}"""), Obj("""{"id": "c", "name": "New"}""")]);
        Assert.Equal(["a", "c"], output.Select(m => m["id"]!.GetValue<string>()), StringComparer.Ordinal);
        Assert.Equal("Inbox", output[0]["name"]!.GetValue<string>());
        Assert.Equal(5, output[0]["unreadEmails"]!.GetValue<int>());
    }

    [Fact]
    [RustTest("jmap::counts_only_update_of_unknown_folder_fetches_it_whole")]
    [Red("jmap")]
    public void Counts_only_update_of_unknown_folder_fetches_it_whole()
    {
        List<JsonObject> have = [Obj("""{"id": "a", "name": "Inbox"}""")];
        var (part, full) = ObjectDelta.SplitKnown(["a", "z"], have, true);
        Assert.Equal(["a"], part);
        Assert.Equal(["z"], full);
        (part, full) = ObjectDelta.SplitKnown(["a", "z"], have, false);
        Assert.Equal(2, part.Count);
        Assert.Empty(full);
    }
}
