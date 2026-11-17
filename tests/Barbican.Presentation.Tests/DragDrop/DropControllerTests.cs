// Drag and drop through the same path `dnd-sim` uses (Rust dnd.rs drop :52): mail onto a folder (already_in skipped, Undo recorded), onto a tag (labels), a folder onto a
// folder; hooks onDragStart, onDragEnd, onEmailDrop, onTagDrop. The pane widths (panes.json, onColumnResize) are here too. Owner actions.

#pragma warning disable MA0002 // asserts compare strings ordinally by nature
using Barbican.Core.DragDrop;
using Barbican.Core.Mail.Write.Payloads;
using Barbican.Core.Plugins.Hooks;
using Barbican.Presentation.DragDrop;
using Barbican.Presentation.Shell;
using Barbican.Presentation.Tests.Actions;

namespace Barbican.Presentation.Tests.DragDrop;

public sealed class DropControllerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "barbican-drop-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public async Task A_row_dropped_on_a_folder_moves_with_undo_and_fires_the_hooks()
    {
        using var h = await ActionHarness.StartAsync();
        await h.OpenAsync("inbox");
        var dnd = new DropController(h.Actions, h.Bus);
        var events = new List<string>();
        using var a = h.Bus.Observe<DragStartPayload>(HookNames.OnDragStart, (p, _) => { events.Add("start:" + p.Kind); return ValueTask.CompletedTask; });
        using var b = h.Bus.Observe<EmailDropPayload>(HookNames.OnEmailDrop, (p, _) => { events.Add("drop:" + p.TargetMailboxId); return ValueTask.CompletedTask; });
        using var c = h.Bus.Observe<DragEndPayload>(HookNames.OnDragEnd, (p, _) => { events.Add("end:" + p.Dropped); return ValueTask.CompletedTask; });

        var t = h.Pick(0, 1);
        var archive = h.Folder("archive");
        dnd.BeginMailDrag([t[0].EmailId, t[1].EmailId], "2 messages");
        Assert.True(dnd.IsDragging);
        var r = await dnd.DropMailOnFolderAsync(t, archive, Ct);
        Assert.True(r.IsOk);
        Assert.False(dnd.IsDragging);
        Assert.All(t, x => Assert.Equal(archive, h.Server.Store.Email(x.EmailId)!["mailboxIds"]!.AsObject().Single().Key));
        Assert.Single(h.Undo.History);
        await Task.Delay(150, Ct);
        // observers run off the caller's thread: the order between them is not promised
        Assert.Equal(3, events.Count);
        Assert.Contains("start:email", events);
        Assert.Contains("drop:" + archive, events);
        Assert.Contains("end:True", events);
    }

    [Fact]
    public async Task A_row_dropped_on_the_folder_it_is_in_changes_nothing()
    {
        using var h = await ActionHarness.StartAsync();
        await h.OpenAsync("inbox");
        var dnd = new DropController(h.Actions, h.Bus);
        var requests = h.Requests;
        var r = await dnd.DropMailOnFolderAsync(h.Pick(0), h.Folder("inbox"), Ct);
        Assert.True(r.IsOk);
        Assert.Equal(requests, h.Requests);
        Assert.Empty(h.Undo.History);
    }

    [Fact]
    public async Task A_row_dropped_on_a_tag_gets_the_tag_without_leaving_the_list()
    {
        using var h = await ActionHarness.StartAsync();
        await h.OpenAsync("inbox");
        var dnd = new DropController(h.Actions, h.Bus);
        var rows = h.List.Messages.Count();
        var t = h.Pick(2);
        var tagged = new TaskCompletionSource<TagDropPayload>();
        using var _ = h.Bus.Observe<TagDropPayload>(HookNames.OnTagDrop, (p, _) => { tagged.TrySetResult(p); return ValueTask.CompletedTask; });
        Assert.True((await dnd.DropMailOnTagAsync(t, "work", Ct)).IsOk);
        Assert.Equal(rows, h.List.Messages.Count());
        Assert.NotNull(h.Server.Store.Email(t[0].EmailId)!["keywords"]!["$label:work"]);
        Assert.Equal("$label:work", (await tagged.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct)).Keyword);
    }

    [Fact]
    public async Task A_folder_dropped_into_its_own_child_is_refused_with_a_message()
    {
        using var h = await ActionHarness.StartAsync();
        var parent = h.Server.Store.AddMailbox("Parent", null);
        var child = h.Server.Store.AddMailbox("Child", null, parent);
        h.Sidebar.Update((await h.Active.Mail!.GetMailboxesAsync(Ct)).Value!);
        var dnd = new DropController(h.Actions, h.Bus);
        var r = await dnd.DropFolderAsync("acct", parent, child, DropZone.Inside, Ct);
        Assert.False(r.IsOk);
        Assert.Contains(h.Toasts.Toasts, x => x.IsError);
    }

    [Fact]
    public async Task Panes_load_once_clamp_save_on_commit_and_reset_on_double_click()
    {
        var path = Path.Combine(_dir, "panes.json");
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(path, """{"sidebar": 220, "list": 9000}""", Ct);
        var bus = new HookBus();
        var resized = new List<(string, double)>();
        using var sub = bus.Observe<ColumnResizePayload>(HookNames.OnColumnResize, (p, _) => { resized.Add((p.Column, p.Width)); return ValueTask.CompletedTask; });
        var panes = new PanesViewModel(path, bus);
        Assert.Equal(220f, panes.SidebarWidth);
        Assert.Equal(600f, panes.ListWidth);

        panes.Resize("sidebar", 50f);
        Assert.Equal(180f, panes.SidebarWidth);
        panes.Resize("sidebar", 260f);
        panes.Commit("sidebar");
        Assert.Equal("""{"sidebar":260,"list":600}""", await File.ReadAllTextAsync(path, Ct));

        panes.Reset("list");
        Assert.Equal(0f, panes.ListWidth);
        Assert.Equal("""{"sidebar":260,"list":0}""", await File.ReadAllTextAsync(path, Ct));
        await Task.Delay(150, Ct);
        Assert.Equal([("sidebar", 260d), ("list", 0d)], resized);
    }
}
