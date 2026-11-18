// Not Rust rows: B-FILE-002 (a folder on a draft-18 server), B-FILE-006 (move, copy across accounts, duplicate, share), B-FILE-007, B-FILE-009, B-FILE-011 (trash, restore, purge, empty trash; a delete is a move and never a destroy), J-036 (Blob/copy across accounts),
// the hook rows of the files writer (onDirectoryCreate, onBefore/AfterFileDelete, onBefore/FileRename, onBeforeFileMove, onFileMove, onFileCopy, onFileDuplicate) against FakeJmap. M5 stream files-data.

#pragma warning disable MA0006, MA0002, VSTHRD103 // test assertions compare ordinal string literals and read small scratch files synchronously

using Barbican.Core.Files;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Payloads;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Files;

public sealed class FileWriterTests
{
    private static CancellationToken Ct => FileRig.Ct;

    private static async Task<FileRig> LoadedAsync(FakeFiles? files = null)
    {
        var rig = await FileRig.CreateAsync(files);
        _ = await rig.LoadAsync();
        return rig;
    }

    [Fact]
    public async Task A_folder_is_created_with_a_unique_name_and_tells_the_plugins()
    {
        using var rig = await LoadedAsync();
        var first = await rig.Writer.CreateFolderAsync(rig.Key, FakeSeed.FilesDocumentsId, "Reports", Ct);
        var second = await rig.Writer.CreateFolderAsync(rig.Key, FakeSeed.FilesDocumentsId, "Reports", Ct);

        Assert.True(first.IsOk && second.IsOk);
        Assert.Equal("Reports", rig.Store.Snapshot(rig.Key)!.Entries.Single(e => e.Key == first.Value).Name);
        Assert.Equal("Reports (2)", rig.Store.Snapshot(rig.Key)!.Entries.Single(e => e.Key == second.Value).Name);
        var view = Assert.IsType<FileResourceView>(await rig.HookAsync("onDirectoryCreate"));
        Assert.Equal("directory", view.Type);
        Assert.Equal("/Documents", view.Path);
        Assert.False((await rig.Writer.CreateFolderAsync(rig.Key, FileTrash.TrashLocation, "x", Ct)).IsOk);
        Assert.False((await rig.Writer.CreateFolderAsync(rig.Key, null, "  ", Ct)).IsOk);
    }

    [Fact]
    public async Task A_draft_18_server_gets_the_node_type_on_create()
    {
        using var rig = await LoadedAsync(new FakeFiles { SeedM5 = true, Draft18 = true });

        // the fake refuses a create without @type, so a created folder shows the type was sent (B-FILE-002)
        var made = await rig.Writer.CreateFolderAsync(rig.Key, null, "Drafted", Ct);
        Assert.True(made.IsOk, made.Error?.Message);
        Assert.True(rig.Store.Snapshot(rig.Key)!.Entries.Single(e => e.Key == made.Value).IsDirectory);
    }

    [Fact]
    public async Task A_name_the_server_forbids_is_refused_before_any_request()
    {
        using var rig = await LoadedAsync(new FakeFiles { SeedM5 = true, NameRules = true });
        var before = rig.Store.Snapshot(rig.Key)!.Entries.Count;

        var folder = await rig.Writer.CreateFolderAsync(rig.Key, null, "a:b", Ct);
        var rename = await rig.Writer.RenameAsync(rig.Key, "N1", "con.txt", Ct);

        Assert.IsType<ValidationError>(folder.Error);
        Assert.Contains("forbidden", folder.Error!.Message, StringComparison.Ordinal);
        Assert.Contains("reserved", rename.Error!.Message, StringComparison.Ordinal);
        Assert.Equal(before, rig.Store.Snapshot(rig.Key)!.Entries.Count);
    }

    [Fact]
    public async Task Rename_changes_the_name_and_a_plugin_can_stop_it()
    {
        using var rig = await LoadedAsync();
        Assert.True((await rig.Writer.RenameAsync(rig.Key, "N1", "minutes.txt", Ct)).IsOk);
        Assert.Equal("minutes.txt", rig.Entry("minutes.txt").Name);
        var after = Assert.IsType<FileRenamePayload>(await rig.HookAsync("onFileRename"));
        Assert.Equal("notes.txt", after.PreviousName);
        Assert.Equal("minutes.txt", after.File.Name);

        // a sibling's name is taken; and a plugin may say no
        Assert.False((await rig.Writer.RenameAsync(rig.Key, "N1", "Invoices", Ct)).IsOk);
        rig.Hooks.Deny("onBeforeFileRename");
        Assert.False((await rig.Writer.RenameAsync(rig.Key, "N1", "other.txt", Ct)).IsOk);
        Assert.Equal("minutes.txt", rig.Entry("minutes.txt").Name);
    }

    [Fact]
    public async Task Move_puts_items_into_another_folder_and_refuses_what_cannot_work()
    {
        using var rig = await LoadedAsync();
        Assert.True((await rig.Writer.MoveAsync(rig.Key, ["N1"], "D3", Ct)).IsOk);
        Assert.Equal("D3", rig.Entry("notes.txt").ParentKey);
        var moved = Assert.IsType<FileMovePayload>(await rig.HookAsync("onFileMove"));
        Assert.Equal("/Photos", moved.ToPath);

        Assert.False((await rig.Writer.MoveAsync(rig.Key, [FakeSeed.FilesDocumentsId], FakeSeed.FilesInvoicesId, Ct)).IsOk);
        Assert.False((await rig.Writer.MoveAsync(rig.Key, ["N1"], "s1|S1", Ct)).IsOk);
        Assert.False((await rig.Writer.MoveAsync(rig.Key, ["T2"], null, Ct)).IsOk);
        Assert.False((await rig.Writer.MoveAsync(rig.Key, ["N1"], FileTrash.TrashLocation, Ct)).IsOk);
        rig.Hooks.Deny("onBeforeFileMove");
        Assert.False((await rig.Writer.MoveAsync(rig.Key, ["R1"], "D3", Ct)).IsOk);
        Assert.Null(rig.Entry("readme.md").ParentKey);
    }

    [Fact]
    public async Task Move_makes_a_taken_name_unique_on_the_server()
    {
        using var rig = await LoadedAsync();
        _ = await rig.Writer.CreateFolderAsync(rig.Key, "D3", "notes.txt", Ct);

        Assert.True((await rig.Writer.MoveAsync(rig.Key, ["N1"], "D3", Ct)).IsOk);
        Assert.Contains(rig.Children("D3"), e => e.Name == "notes (1).txt");
    }

    [Fact]
    public async Task Copy_duplicates_a_folder_with_everything_below_it()
    {
        using var rig = await LoadedAsync();
        Assert.True((await rig.Writer.CopyAsync(rig.Key, [FakeSeed.FilesInvoicesId, "N1"], "D3", Ct)).IsOk);

        var invoices = rig.Children("D3").Single(e => e.Name == "Invoices");
        var march = rig.Children(invoices.Key).Single();
        Assert.Equal("march.txt", march.Name);
        Assert.Equal("Invoice March\n", System.Text.Encoding.UTF8.GetString((await rig.Transfer.ReadAsync(rig.Key, march.Key, 1000, Ct)).Value!));
        Assert.Contains(rig.Children("D3"), e => e.Name == "notes.txt");
        Assert.Single(rig.Children(FakeSeed.FilesDocumentsId), e => e.Name == "Invoices");
        Assert.NotNull(await rig.HookAsync("onFileCopy"));

        // copying into the same folder gives "name (2).ext"
        Assert.True((await rig.Writer.CopyAsync(rig.Key, ["N1"], FakeSeed.FilesDocumentsId, Ct)).IsOk);
        Assert.Contains(rig.Children(FakeSeed.FilesDocumentsId), e => e.Name == "notes (2).txt");
        Assert.False((await rig.Writer.CopyAsync(rig.Key, [FakeSeed.FilesDocumentsId], FakeSeed.FilesInvoicesId, Ct)).IsOk);
    }

    [Fact]
    public async Task Copy_goes_across_accounts_with_Blob_copy_in_both_directions()
    {
        using var rig = await LoadedAsync();
        Assert.True((await rig.Writer.CopyAsync(rig.Key, ["N1"], "s1|S1", Ct)).IsOk);
        var there = rig.Store.Snapshot(rig.Key)!.Entries.Single(e => e.Name == "notes.txt" && e.Shared);
        Assert.Equal("s1|S1", there.ParentKey);
        Assert.Equal(FakeSeed.FilesNotesText, System.Text.Encoding.UTF8.GetString((await rig.Transfer.ReadAsync(rig.Key, there.Key, 1000, Ct)).Value!));

        // and back into the user's own account, a folder with its file
        Assert.True((await rig.Writer.CopyAsync(rig.Key, ["s1|S1"], FakeSeed.FilesDocumentsId, Ct)).IsOk);
        var team = rig.Children(FakeSeed.FilesDocumentsId).Single(e => e.Name == "Team");
        Assert.Equal(["notes.txt", "plan.md"], rig.Children(team.Key).Select(e => e.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Duplicate_makes_a_copy_next_to_the_original()
    {
        using var rig = await LoadedAsync();
        Assert.True((await rig.Writer.DuplicateAsync(rig.Key, "N1", Ct)).IsOk);
        Assert.True((await rig.Writer.DuplicateAsync(rig.Key, "N1", Ct)).IsOk);
        Assert.True((await rig.Writer.DuplicateAsync(rig.Key, FakeSeed.FilesDocumentsId, Ct)).IsOk);

        Assert.Equal(["Invoices", "notes (copy) (2).txt", "notes (copy).txt", "notes.txt"], rig.Children(FakeSeed.FilesDocumentsId).Select(e => e.Name).Order(StringComparer.Ordinal));
        var copy = rig.Children(null).Single(e => e.Name == "Documents (copy)");
        Assert.Equal(4, rig.Store.Snapshot(rig.Key)!.Entries.Count(e => e.ParentKey == copy.Key));
        Assert.NotNull(await rig.HookAsync("onFileDuplicate"));
    }

    [Fact]
    public async Task A_delete_is_a_move_into_a_dated_batch_and_never_a_destroy()
    {
        using var rig = await LoadedAsync();
        rig.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero));
        Assert.True((await rig.Writer.TrashAsync(rig.Key, ["N1", FakeSeed.FilesInvoicesId, "R1"], Ct)).IsOk);

        var item = FileTrash.Items(rig.Store.Snapshot(rig.Key)!.Entries).Single(t => t.Entry.Key == "N1");
        Assert.Equal(rig.Clock.GetUtcNow().ToUnixTimeMilliseconds(), item.DeletedMs);
        Assert.Equal(FakeSeed.FilesDocumentsId, item.FromParent);
        Assert.Contains(FileTrash.Items(rig.Store.Snapshot(rig.Key)!.Entries), t => t.Entry.Key == "R1" && t.FromParent is null);
        Assert.DoesNotContain(rig.Children(FakeSeed.FilesDocumentsId), e => e.Name == "notes.txt");

        // nothing was destroyed: the nodes still exist on the server under the trash
        var march = rig.Store.Snapshot(rig.Key)!.Entries.Single(e => e.Name == "march.txt");
        Assert.Contains(march.ParentKey!, FileTrash.Subtree(rig.Store.Snapshot(rig.Key)!.Entries));
        var payload = Assert.IsType<FileListPayload>(await rig.HookAsync("onAfterFileDelete"));
        Assert.Equal(3, payload.Files.Count);
    }

    [Fact]
    public async Task A_delete_creates_the_trash_when_it_is_missing()
    {
        using var rig = await FileRig.CreateAsync();
        _ = await rig.CallAsync("FileNode/set", new System.Text.Json.Nodes.JsonObject { ["accountId"] = "b", ["destroy"] = new System.Text.Json.Nodes.JsonArray(FakeSeed.FilesTrashId), ["onDestroyRemoveChildren"] = true });
        _ = await rig.LoadAsync();
        Assert.Null(FileTrash.Folder(rig.Store.Snapshot(rig.Key)!.Entries));

        Assert.True((await rig.Writer.TrashAsync(rig.Key, ["R1"], Ct)).IsOk);
        Assert.NotNull(FileTrash.Folder(rig.Store.Snapshot(rig.Key)!.Entries));
        Assert.Single(FileTrash.Items(rig.Store.Snapshot(rig.Key)!.Entries));
    }

    [Fact]
    public async Task What_is_deleted_for_good_is_not_a_trash_move()
    {
        using var rig = await LoadedAsync();
        var before = rig.Store.Snapshot(rig.Key)!.Entries.Count;

        // something in the trash, something in a shared account, the trash itself: TrashAsync refuses and destroys nothing (B-FILE-011)
        Assert.False((await rig.Writer.TrashAsync(rig.Key, ["T2"], Ct)).IsOk);
        Assert.False((await rig.Writer.TrashAsync(rig.Key, ["s1|S3"], Ct)).IsOk);
        Assert.False((await rig.Writer.TrashAsync(rig.Key, [FakeSeed.FilesTrashId], Ct)).IsOk);
        rig.Hooks.Deny("onBeforeFileDelete");
        Assert.False((await rig.Writer.TrashAsync(rig.Key, ["R1"], Ct)).IsOk);

        Assert.Equal(before, rig.Store.Snapshot(rig.Key)!.Entries.Count);
        Assert.Contains(rig.Store.Snapshot(rig.Key)!.Entries, e => e.Key == "R1" && e.ParentKey is null);
    }

    [Fact]
    public async Task Restore_puts_an_item_back_where_it_came_from_under_a_free_name()
    {
        using var rig = await LoadedAsync();
        Assert.True((await rig.Writer.RestoreAsync(rig.Key, ["T2", "T3"], Ct)).IsOk);

        Assert.Contains(rig.Children(FakeSeed.FilesDocumentsId), e => e.Name == "old.txt");
        Assert.Contains(rig.Children(null), e => e.Name == "stray.txt");
        Assert.Empty(FileTrash.Items(rig.Store.Snapshot(rig.Key)!.Entries));
        Assert.DoesNotContain(rig.Store.Snapshot(rig.Key)!.Entries, e => e.Key == FakeSeed.FilesBatchId);
        Assert.False((await rig.Writer.RestoreAsync(rig.Key, ["N1"], Ct)).IsOk);
    }

    [Fact]
    public async Task Restore_uses_the_root_when_the_folder_is_gone_and_the_name_is_taken()
    {
        using var rig = await LoadedAsync();
        _ = await rig.Writer.CreateFolderAsync(rig.Key, null, "old.txt", Ct);
        _ = await rig.Writer.PurgeAsync(rig.Key, [FakeSeed.FilesDocumentsId], Ct);

        Assert.True((await rig.Writer.RestoreAsync(rig.Key, ["T2"], Ct)).IsOk);
        Assert.Contains(rig.Children(null), e => e.Name == "old (2).txt");
    }

    [Fact]
    public async Task Purge_destroys_for_good_with_the_subtree_and_the_emptied_batch()
    {
        using var rig = await LoadedAsync();
        Assert.True((await rig.Writer.PurgeAsync(rig.Key, ["T2"], Ct)).IsOk);
        Assert.DoesNotContain(rig.Store.Snapshot(rig.Key)!.Entries, e => e.Key is "T2" or "T1");

        Assert.True((await rig.Writer.PurgeAsync(rig.Key, [FakeSeed.FilesDocumentsId], Ct)).IsOk);
        Assert.DoesNotContain(rig.Store.Snapshot(rig.Key)!.Entries, e => e.Name is "Documents" or "notes.txt" or "march.txt");
        Assert.False((await rig.Writer.PurgeAsync(rig.Key, [FakeSeed.FilesTrashId], Ct)).IsOk);
        Assert.NotNull(await rig.HookAsync("onAfterFileDelete"));
    }

    [Fact]
    public async Task Empty_trash_removes_the_folder_with_all_of_it_and_the_next_delete_makes_a_new_one()
    {
        using var rig = await LoadedAsync();
        Assert.True((await rig.Writer.EmptyTrashAsync(rig.Key, Ct)).IsOk);
        Assert.Null(FileTrash.Folder(rig.Store.Snapshot(rig.Key)!.Entries));
        Assert.DoesNotContain(rig.Store.Snapshot(rig.Key)!.Entries, e => e.Name is "old.txt" or "stray.txt");
        Assert.True((await rig.Writer.EmptyTrashAsync(rig.Key, Ct)).IsOk);

        Assert.True((await rig.Writer.TrashAsync(rig.Key, ["R1"], Ct)).IsOk);
        Assert.Single(FileTrash.Items(rig.Store.Snapshot(rig.Key)!.Entries));
    }

    [Fact]
    public async Task Share_sets_and_removes_the_rights_of_one_principal()
    {
        using var rig = await LoadedAsync();
        Assert.True((await rig.Writer.ShareAsync(rig.Key, "N1", "u1", true, Ct)).IsOk);
        var share = rig.Entry("notes.txt").ShareWith!;

        // a server without name rules in its capability is "before 0.16.6": mayWrite (Rust files.rs:726)
        Assert.True((bool)share["u1"]!["mayWrite"]!);
        Assert.True((await rig.Writer.ShareAsync(rig.Key, "N1", "a/b~c", false, Ct)).IsOk);
        Assert.False((bool)rig.Entry("notes.txt").ShareWith!["a/b~c"]!["mayWrite"]!);
        Assert.True((await rig.Writer.ShareAsync(rig.Key, "N1", "u1", null, Ct)).IsOk);
        Assert.Null(rig.Entry("notes.txt").ShareWith!["u1"]);
        Assert.False((await rig.Writer.ShareAsync(rig.Key, "N1", string.Empty, true, Ct)).IsOk);
        Assert.False((await rig.Writer.ShareAsync(rig.Key, "missing", "u1", true, Ct)).IsOk);
    }

    [Fact]
    public async Task A_current_server_gets_the_five_rights()
    {
        using var rig = await LoadedAsync(new FakeFiles { SeedM5 = true, NameRules = true });
        Assert.True((await rig.Writer.ShareAsync(rig.Key, "N1", "u1", true, Ct)).IsOk);

        var rights = rig.Entry("notes.txt").ShareWith!["u1"]!;
        Assert.True((bool)rights["mayModifyContent"]!);
        Assert.True((bool)rights["mayAddChildren"]!);
        Assert.False((bool)rights["mayShare"]!);
    }
}
