// Multi-select (Rust mail.rs toggle_check :829, select_all :837; spec I-120, I-123, I-124, I-126, I-138..I-142): Ctrl-click, Shift ranges across date headers,
// select all and "select all in folder" resolved with Email/query ids, the selection count, the commands acting on it. Owner actions.

#pragma warning disable MA0002 // asserts compare strings ordinally by nature
using Barbican.FakeJmap;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Commands;

namespace Barbican.Presentation.Tests.Actions;

public sealed class SelectionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Ctrl_click_toggles_one_row_and_the_selection_follows()
    {
        using var h = await ActionHarness.StartAsync();
        await h.OpenAsync("inbox");
        var rows = h.List.Messages.ToList();
        var raised = 0;
        h.List.Changed += (_, _) => raised++;

        h.List.ToggleCheck(rows[2]);
        h.List.ToggleCheck(rows[4]);
        Assert.Equal(2, h.List.CheckedCount);
        Assert.True(rows[2].IsChecked && rows[4].IsChecked && !rows[3].IsChecked);
        Assert.Equal([rows[2].Id, rows[4].Id], h.List.Current.Select(t => t.EmailId));
        Assert.True(raised >= 2);

        h.List.ToggleCheck(rows[2]);
        Assert.False(rows[2].IsChecked);
        Assert.Equal(1, h.List.CheckedCount);
    }

    [Fact]
    public async Task A_shift_range_runs_across_a_date_group_header()
    {
        using var h = await ActionHarness.StartAsync();
        var inbox = h.Folder("inbox");
        h.Server.Store.Deliver(new FakeMail { Subject = "today", Text = "x", ReceivedAt = new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero) }, inbox);
        h.Server.Store.Deliver(new FakeMail { Subject = "yesterday", Text = "x", ReceivedAt = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero) }, inbox);
        await h.OpenAsync("inbox");
        var headers = h.List.Rows.OfType<Barbican.Presentation.Mail.GroupHeaderViewModel>().Select(x => x.Title).ToList();
        Assert.Contains("Today", headers);
        Assert.Contains("Yesterday", headers);
        var rows = h.List.Messages.ToList();

        // today, (header Yesterday), yesterday, (header older), the next: a range over two headers takes the three messages and no header
        Assert.Equal("today", rows[0].Subject);
        Assert.Equal("yesterday", rows[1].Subject);
        h.List.ToggleCheck(rows[0]);
        h.List.ExtendTo(rows[2]);
        Assert.Equal(3, h.List.CheckedCount);
        Assert.Equal(rows.Take(3).Select(r => r.Id), h.List.Current.Select(t => t.EmailId));

        // and back up from below: the range is anchored on the first click
        h.List.ExtendTo(rows[1]);
        Assert.Equal(2, h.List.CheckedCount);
    }

    [Fact]
    public async Task Shift_arrow_extends_from_the_anchor_without_opening_anything()
    {
        using var h = await ActionHarness.StartAsync();
        await h.OpenAsync("inbox");
        var rows = h.List.Messages.ToList();
        h.List.ToggleCheck(rows[0]);
        Assert.True(h.List.ExtendBy(1));
        Assert.True(h.List.ExtendBy(1));
        Assert.Equal(3, h.List.CheckedCount);
        Assert.True(h.List.ExtendBy(-1));
        Assert.Equal(2, h.List.CheckedCount);
        Assert.Empty(h.Opener.Opened);
        Assert.Contains("2 selected", h.List.SelectionLabel, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Select_all_toggles_the_tri_state()
    {
        using var h = await ActionHarness.StartAsync();
        await h.OpenAsync("inbox");
        Assert.Equal(0, h.List.SelectAllState);
        h.List.ToggleCheck(h.List.Messages.First());
        Assert.Equal(1, h.List.SelectAllState);
        h.List.ToggleSelectAll();
        Assert.Equal(2, h.List.SelectAllState);
        Assert.Equal(h.List.Messages.Count(), h.List.CheckedCount);
        h.List.ToggleSelectAll();
        Assert.Equal(0, h.List.SelectAllState);
        Assert.False(h.List.HasChecked);
    }

    [Fact]
    public async Task Select_all_in_folder_acts_on_the_query_ids_not_the_loaded_rows()
    {
        using var h = await ActionHarness.StartAsync(prefs: """{"emailsPerPage": 10}""");
        var inbox = h.Folder("inbox");
        for (var i = 0; i < 14; i++)
        {
            h.Server.Store.Deliver(new FakeMail { Subject = "bulk " + i, Text = "x" }, inbox);
        }

        await h.OpenAsync("inbox");
        var loaded = h.List.Messages.Count();
        Assert.True(h.List.CanLoadMore);
        h.List.SelectAllLoaded();
        Assert.True(h.List.CanSelectAllInFolder);
        h.List.SelectAllInFolder();
        Assert.True(h.List.AllInFolder);
        var inFolder = h.Server.Store.Query(new System.Text.Json.Nodes.JsonObject { ["inMailbox"] = inbox }).Count;
        Assert.True(inFolder > loaded);
        Assert.Equal(inFolder, h.List.CheckedCount);

        var resolved = await h.List.ResolveTargetsAsync(Ct);
        Assert.Equal(inFolder, resolved.Count);

        // a command with no explicit targets uses all of them
        var registry = new CommandRegistry(ActionCommandSet.Build(h.Actions, h.List, h.Writers, h.Sidebar, h.Undo));
        Assert.True(registry.TryGet(CommandIds.MarkRead, out var read));
        var ctx = new CommandContext(CommandSurface.SelectionBar, [], null, null, "acct");
        Assert.True(read.CanExecute(ctx));
        Assert.True((await read.ExecuteAsync(ctx, Ct)).IsOk);
        Assert.Empty(h.Server.Store.Query(new System.Text.Json.Nodes.JsonObject { ["inMailbox"] = inbox, ["notKeyword"] = "$seen" }));
    }

    [Fact]
    public async Task Removing_rows_drops_their_ticks_and_the_count()
    {
        using var h = await ActionHarness.StartAsync(prefs: """{"confirmBulkDelete": false}""");
        await h.OpenAsync("inbox");
        var rows = h.List.Messages.ToList();
        h.List.ToggleCheck(rows[0]);
        h.List.ToggleCheck(rows[1]);
        Assert.True((await h.Actions.DeleteAsync(h.List.Current, Ct)).IsOk);
        Assert.False(h.List.HasChecked);
        Assert.Empty(h.List.Current);
    }

    [Fact]
    public async Task Delete_of_the_open_message_goes_back_to_the_list_or_on_to_the_next()
    {
        using var h = await ActionHarness.StartAsync();
        await h.OpenAsync("inbox");
        var rows = h.List.Messages.ToList();
        h.List.Select(rows[2], Barbican.Presentation.Mail.OpenReason.Click, Barbican.Presentation.Mail.ListDirection.None);
        h.Opener.Opened.Clear();
        Assert.True((await h.Actions.DeleteAsync(h.Pick(2), Ct)).IsOk);
        Assert.Null(h.List.Selected);

        h.Prefs.Set("""{"returnToListAfterAction": false}""");
        var again = h.List.Messages.ToList();
        h.List.Select(again[2], Barbican.Presentation.Mail.OpenReason.Click, Barbican.Presentation.Mail.ListDirection.None);
        h.Opener.Opened.Clear();
        Assert.True((await h.Actions.DeleteAsync(h.Pick(2), Ct)).IsOk);
        Assert.Equal(again[3].Id, h.List.Selected?.Id);
        Assert.Equal(again[3].Id, h.Opener.Opened[^1].Id);
    }
}
