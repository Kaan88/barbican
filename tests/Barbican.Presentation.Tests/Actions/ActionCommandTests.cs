// The action commands (M2 plan 4.1): one per id, enabled by what the selection and the account allow, evaluated when asked. Owner actions.

using Barbican.Presentation.Actions;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Tests.Support;

namespace Barbican.Presentation.Tests.Actions;

public sealed class ActionCommandTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CommandRegistry Registry(ActionHarness h) => new(ActionCommandSet.Build(h.Actions, h.List, h.Writers, h.Sidebar, h.Undo));

    [Fact]
    public async Task Ids_list_matches_the_built_commands()
    {
        using var h = await ActionHarness.StartAsync();
        var built = ActionCommandSet.Build(h.Actions, h.List, h.Writers, h.Sidebar, h.Undo).Select(c => c.Id).Order(StringComparer.Ordinal).ToList();
        Assert.True(ActionCommandSet.Ids.Order(StringComparer.Ordinal).SequenceEqual(built, StringComparer.Ordinal));
        Assert.Equal(built.Count, built.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Write_commands_are_disabled_without_targets_and_on_a_read_only_account()
    {
        using var h = await ActionHarness.StartAsync();
        await h.OpenAsync("inbox");
        var registry = Registry(h);
        Assert.True(registry.TryGet(CommandIds.Star, out var star));
        var none = new CommandContext(CommandSurface.Keyboard, [], null, null, "acct");
        Assert.False(star.CanExecute(none));
        Assert.True(star.CanExecute(none with { Targets = h.Pick(0) }));

        using var ro = await ActionHarness.StartAsync(readOnly: true);
        await ro.OpenAsync("inbox");
        Assert.True(Registry(ro).TryGet(CommandIds.Star, out var roStar));
        Assert.False(roStar.CanExecute(none with { Targets = ro.Pick(0) }));
    }

    [Fact]
    public async Task Context_commands_follow_the_open_folder()
    {
        using var h = await ActionHarness.StartAsync();
        var registry = Registry(h);
        await h.OpenAsync("inbox");
        var ctx = new CommandContext(CommandSurface.ContextMenu, h.Pick(0), null, null, "acct");
        Assert.True(registry.TryGet(CommandIds.Archive, out var archive));
        Assert.True(registry.TryGet(CommandIds.MoveToInbox, out var toInbox));
        Assert.True(registry.TryGet(CommandIds.NotSpam, out var notSpam));
        Assert.True(registry.TryGet(CommandIds.Restore, out var restore));
        Assert.True(archive.CanExecute(ctx));
        Assert.False(toInbox.CanExecute(ctx));
        Assert.False(notSpam.CanExecute(ctx));
        Assert.False(restore.CanExecute(ctx));

        h.Server.Store.Deliver(new FakeJmap.FakeMail { Subject = "in archive", Text = "x" }, h.Folder("archive"));
        await h.OpenAsync("archive");
        var inArchive = ctx with { Targets = h.Pick(0) };
        Assert.False(archive.CanExecute(inArchive));
        Assert.True(toInbox.CanExecute(inArchive));
    }

    [Fact]
    public async Task Folder_commands_respect_my_rights()
    {
        using var h = await ActionHarness.StartAsync();
        var inbox = h.Folder("inbox");
        var registry = Registry(h);
        Assert.True(registry.TryGet(CommandIds.RenameFolder, out var rename));
        var ctx = new CommandContext(CommandSurface.ContextMenu, [], new Core.Mail.Model.MailboxRef("acct", string.Empty, inbox, "inbox"), "New", "acct");
        Assert.True(rename.CanExecute(ctx));
        Assert.True(ActionCommandSet.HasRight(h.Sidebar, inbox, "mayRename"));
        Assert.True(ActionCommandSet.HasRight(h.Sidebar, "unknown-folder", "mayRename"));
    }

    [Fact]
    public async Task The_list_menu_commands_write_the_sort_order_and_group_by_date_settings()
    {
        using var h = await ActionHarness.StartAsync();
        await h.OpenAsync("inbox");
        var editor = new RecordingPrefsEditor();
        var registry = new CommandRegistry(ActionCommandSet.Build(h.Actions, h.List, h.Writers, h.Sidebar, h.Undo, null, h.Prefs, editor));
        var ctx = new CommandContext(CommandSurface.ContextMenu, [], null, null, "acct");
        Assert.True(registry.TryGet(CommandIds.SortList, out var sort));
        Assert.False(sort.CanExecute(ctx));
        Assert.False(sort.CanExecute(ctx with { Argument = "nonsense" }));
        Assert.True(sort.CanExecute(ctx with { Argument = "unread" }));
        await sort.ExecuteAsync(ctx with { Argument = "unread" }, Ct);
        Assert.Equal("""[{"criterion":"unread","direction":"desc","tagId":null}]""", editor.Values["messageListOrder"]!.ToJsonString());
        // the open folder is the Inbox, which the order always applies to: the scope stays
        Assert.False(editor.Values.ContainsKey("messageListOrderScope"));
        await sort.ExecuteAsync(ctx with { Argument = "newest" }, Ct);
        Assert.Equal("[]", editor.Values["messageListOrder"]!.ToJsonString());

        Assert.True(registry.TryGet(CommandIds.ToggleGroupByDate, out var group));
        await group.ExecuteAsync(ctx, Ct);
        Assert.True(editor.Values["groupListByDate"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Undo_is_enabled_once_something_can_be_undone_and_runs_the_latest()
    {
        using var h = await ActionHarness.StartAsync();
        await h.OpenAsync("inbox");
        var registry = Registry(h);
        Assert.True(registry.TryGet(CommandIds.Undo, out var undo));
        var ctx = new CommandContext(CommandSurface.Keyboard, [], null, null, "acct");
        Assert.False(undo.CanExecute(ctx));
        var idx = h.List.Messages.ToList().FindIndex(r => !r.Flagged);
        Assert.True((await h.Actions.SetStarAsync(h.Pick(idx), true, Ct)).IsOk);
        Assert.True(undo.CanExecute(ctx));
        Assert.True((await undo.ExecuteAsync(ctx, Ct)).IsOk);
        Assert.False(undo.CanExecute(ctx));
    }
}
