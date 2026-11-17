// R3-03: a bulk write split into chunks (the server's maxObjectsInSet) whose later chunk fails. The chunks before it are applied on the server, so the
// rows they took stay gone with an Undo, only the rows that were not applied come back, and the toast says "N of M". FakeJmap fails the second request.

#pragma warning disable MA0002, VSTHRD003 // asserts compare strings ordinally by nature
using System.Text.Json.Nodes;
using Barbican.FakeJmap;

namespace Barbican.Presentation.Tests.Actions;

public sealed class PartialChunkTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string[] Boxes(ActionHarness h, string id) => [.. ((JsonObject)h.Server.Store.Email(id)!["mailboxIds"]!).Select(kv => kv.Key)];

    [Fact]
    public async Task A_delete_whose_second_chunk_fails_keeps_the_first_chunk_gone_with_an_undo_and_restores_the_rest()
    {
        using var h = await ActionHarness.StartAsync(new FakeJmapOptions { MaxObjectsInSet = 2 }, prefs: """{"confirmBulkDelete": false}""");
        await h.OpenAsync("inbox");
        var targets = h.Pick(0, 1, 2, 3, 4);
        var before = h.List.Messages.ToList().Count;
        h.Server.FailApiRequest(2);

        var r = await h.Actions.DeleteAsync(targets, Ct);

        Assert.False(r.IsOk);
        var moved = targets.Take(2).Select(t => t.EmailId).ToList();
        var stayed = targets.Skip(2).Select(t => t.EmailId).ToList();
        Assert.All(moved, id => Assert.Equal(h.Folder("trash"), Assert.Single(Boxes(h, id))));
        Assert.All(stayed, id => Assert.Equal(h.Folder("inbox"), Assert.Single(Boxes(h, id))));
        // the list: the two applied rows are gone, the three that were not applied are back
        Assert.Equal(before - 2, h.List.Messages.ToList().Count);
        Assert.All(moved, id => Assert.DoesNotContain(h.List.Messages, m => string.Equals(m.Id, id, StringComparison.Ordinal)));
        Assert.All(stayed, id => Assert.Contains(h.List.Messages, m => string.Equals(m.Id, id, StringComparison.Ordinal)));
        Assert.Contains(h.Toasts.Toasts, t => t.Message.Contains("2 of 5 messages deleted", StringComparison.Ordinal) && string.Equals(t.ActionLabel, "Undo", StringComparison.Ordinal));
        Assert.Contains(h.Toasts.Toasts, t => t.IsError);

        // Undo brings the two back to the Inbox and never sends an empty folder set
        Assert.Single(h.Undo.History);
        Assert.True(await h.Undo.UndoLatestAsync(Ct));
        Assert.All(moved, id => Assert.Equal(h.Folder("inbox"), Assert.Single(Boxes(h, id))));
    }

    [Fact]
    public async Task A_permanent_delete_whose_second_chunk_fails_drops_only_the_destroyed_rows()
    {
        using var h = await ActionHarness.StartAsync(new FakeJmapOptions { MaxObjectsInSet = 2 }, prefs: """{"confirmBulkDelete": false}""");
        await h.OpenAsync("inbox");
        var targets = h.Pick(0, 1, 2);
        h.Server.FailApiRequest(2);

        var r = await h.Actions.DeleteForeverAsync(targets, Ct);

        Assert.False(r.IsOk);
        Assert.Null(h.Server.Store.Email(targets[0].EmailId));
        Assert.Null(h.Server.Store.Email(targets[1].EmailId));
        Assert.NotNull(h.Server.Store.Email(targets[2].EmailId));
        Assert.DoesNotContain(h.List.Messages, m => string.Equals(m.Id, targets[0].EmailId, StringComparison.Ordinal));
        Assert.Contains(h.List.Messages, m => string.Equals(m.Id, targets[2].EmailId, StringComparison.Ordinal));
        Assert.Contains(h.Toasts.Toasts, t => t.Message.Contains("2 of 3 messages deleted permanently", StringComparison.Ordinal));
        Assert.Empty(h.Undo.History);
    }

    [Fact]
    public async Task A_move_that_fails_on_its_first_chunk_restores_everything_and_records_no_undo()
    {
        using var h = await ActionHarness.StartAsync(new FakeJmapOptions { MaxObjectsInSet = 2 }, prefs: """{"confirmBulkDelete": false}""");
        await h.OpenAsync("inbox");
        var targets = h.Pick(0, 1, 2);
        var before = h.List.Messages.ToList().Count;
        h.Server.FailApiRequest(1);

        var r = await h.Actions.MoveAsync(targets, h.Folder("archive"), Ct);

        Assert.False(r.IsOk);
        Assert.Equal(before, h.List.Messages.ToList().Count);
        Assert.Empty(h.Undo.History);
        Assert.All(targets, t => Assert.Equal(h.Folder("inbox"), Assert.Single(Boxes(h, t.EmailId))));
    }
}
