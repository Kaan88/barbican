// Ported from Rust src/dnd.rs tests (:294, :322, :346, :362, :373). Red until stream actions implements FolderDrop (M2 plan 6.1).

using System.Text.Json.Nodes;
using Barbican.Core.DragDrop;

namespace Barbican.Core.Tests.DragDrop;

public sealed class FolderDropTests
{
    private static JsonNode J(string json) => JsonNode.Parse(json)!;

    private static List<JsonObject> Tree() =>
    [
        (JsonObject)J("""{"id": "in", "name": "Inbox", "role": "inbox", "parentId": null, "sortOrder": 0}"""),
        (JsonObject)J("""{"id": "a", "name": "Alpha", "parentId": null, "sortOrder": 1}"""),
        (JsonObject)J("""{"id": "b", "name": "Beta", "parentId": null, "sortOrder": 2}"""),
        (JsonObject)J("""{"id": "c", "name": "Gamma", "parentId": null, "sortOrder": 3}"""),
        (JsonObject)J("""{"id": "a1", "name": "Child", "parentId": "a", "sortOrder": 1}"""),
        (JsonObject)J("""{"id": "a11", "name": "Grandchild", "parentId": "a1", "sortOrder": 1}"""),
    ];

    private static void AssertPatch(string id, string expectedJson, FolderPatch actual)
    {
        Assert.Equal(id, actual.Id);
        Assert.True(JsonNode.DeepEquals(J(expectedJson), actual.Patch), $"{id}: {actual.Patch.ToJsonString()}");
    }

    [Fact]
    [Red("actions")]
    [RustTest("dnd::mail_already_in_target_is_skipped")]
    public void Mail_already_in_target_is_skipped()
    {
        Assert.True(FolderDrop.AlreadyIn(J("""{"mailboxIds": {"in": true}}"""), "in"));
        Assert.False(FolderDrop.AlreadyIn(J("""{"mailboxIds": {"in": true}}"""), "a"));

        // in two folders: the move still consolidates it
        Assert.False(FolderDrop.AlreadyIn(J("""{"mailboxIds": {"in": true, "a": true}}"""), "in"));
        Assert.False(FolderDrop.AlreadyIn(J("{}"), "in"));
    }

    [Fact]
    [Red("actions")]
    [RustTest("dnd::reorder_among_siblings")]
    public void Reorder_among_siblings()
    {
        // Gamma before Alpha: Inbox stays first (role folders lead) and its sortOrder is left alone
        var p = FolderDrop.Plan(Tree(), "c", "a", DropZone.Before);
        Assert.True(p.IsOk);
        var patches = p.Value!;
        Assert.Equal(3, patches.Count);
        AssertPatch("c", """{"sortOrder": 2}""", patches[0]);
        AssertPatch("a", """{"sortOrder": 3}""", patches[1]);
        AssertPatch("b", """{"sortOrder": 4}""", patches[2]);

        // Alpha after Beta
        var q = FolderDrop.Plan(Tree(), "a", "b", DropZone.After).Value!;
        Assert.Contains(q, x => string.Equals(x.Id, "a", StringComparison.Ordinal) && JsonNode.DeepEquals(J("""{"sortOrder": 3}"""), x.Patch));
        Assert.Contains(q, x => string.Equals(x.Id, "c", StringComparison.Ordinal) && JsonNode.DeepEquals(J("""{"sortOrder": 4}"""), x.Patch));

        // Beta keeps its place: nothing written for it
        Assert.DoesNotContain(q, x => string.Equals(x.Id, "b", StringComparison.Ordinal));

        // no-op drop
        Assert.Empty(FolderDrop.Plan(Tree(), "a", "a", DropZone.Inside).Value!);
    }

    [Fact]
    [Red("actions")]
    [RustTest("dnd::reparent")]
    public void Reparent()
    {
        // Beta into Gamma: becomes its only child
        var p = FolderDrop.Plan(Tree(), "b", "c", DropZone.Inside).Value!;
        Assert.Single(p);
        AssertPatch("b", """{"sortOrder": 1, "parentId": "c"}""", p[0]);

        // the grandchild out to the top level, after Beta
        var q = FolderDrop.Plan(Tree(), "a11", "b", DropZone.After).Value!;
        var mine = q.First(x => string.Equals(x.Id, "a11", StringComparison.Ordinal));
        Assert.Null(mine.Patch["parentId"]);
        Assert.Equal(4, (int)mine.Patch["sortOrder"]!);
    }

    [Fact]
    [Red("actions")]
    [RustTest("dnd::refuses_cycles_and_moving_special_folders")]
    public void Refuses_cycles_and_moving_special_folders()
    {
        Assert.False(FolderDrop.Plan(Tree(), "a", "a11", DropZone.Inside).IsOk);
        Assert.False(FolderDrop.Plan(Tree(), "a", "a1", DropZone.After).IsOk);
        Assert.False(FolderDrop.Plan(Tree(), "in", "a", DropZone.Inside).IsOk);

        // reordering Inbox among the top level is fine
        Assert.True(FolderDrop.Plan(Tree(), "in", "b", DropZone.After).IsOk);
        Assert.False(FolderDrop.Plan(Tree(), "zz", "a", DropZone.Inside).IsOk);
    }
}
