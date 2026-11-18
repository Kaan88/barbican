// Not Rust rows: B-FILE-012 (delete with an undo toast, cut and paste, move), D-M5-14 (the conflict default), spec/menus.md MENU-FILE / MENU-FILES-BG (item sets). M5 stream files-ui.

#pragma warning disable MA0006, MA0002, CA1861 // assertions compare string literals and literal arrays

using Barbican.Core.Files;
using Barbican.Presentation.Files;
using Barbican.Presentation.Menus;
using Barbican.Presentation.Commands;

namespace Barbican.Presentation.Tests.Files;

public sealed class FilesActionsTests
{
    private static FileEntry E(string key, string? parent, string name, bool dir) => new(key, "a", false, key, parent, name, dir, dir ? null : "b", 1, null, null, null);

    private static (FilesActions A, FilesViewModel Vm, FakeFileWriter W, FakeToasts T) Make()
    {
        var store = new FakeFileStore([E("d1", null, "Docs", true), E("d2", null, "Pics", true), E("f1", null, "a.txt", false), E("f2", "d1", "b.txt", false)]);
        var w = new FakeFileWriter();
        var vm = new FilesViewModel(store, w) { AccountKey = "a" };
        var t = new FakeToasts();
        return (new FilesActions(vm, store, w, toasts: t), vm, w, t);
    }

    [Fact]
    public async Task Delete_moves_to_the_trash_and_the_toast_undoes_it()
    {
        var (a, _, w, t) = Make();
        var r = await a.DeleteAsync(["f1"], CancellationToken.None);
        Assert.True(r.IsOk);
        Assert.Equal(["TrashAsync:f1"], w.Calls);
        var toast = Assert.Single(t.Shown);
        Assert.Equal("Undo", toast.ActionLabel);
        Assert.True(a.CanUndo);
        _ = await a.UndoAsync(CancellationToken.None);
        Assert.Equal("RestoreAsync:f1", w.Calls[1]);
        Assert.False(a.CanUndo);
    }

    [Fact]
    public async Task Cut_then_paste_moves_into_the_open_folder_and_clears_the_clipboard()
    {
        var (a, vm, w, _) = Make();
        a.Cut(["f1"]);
        Assert.True(a.CanPaste);
        vm.Navigate("d2");
        _ = await a.PasteAsync(CancellationToken.None);
        Assert.Equal(["MoveAsync:f1>d2"], w.Calls);
        Assert.False(a.CanPaste);
    }

    [Fact]
    public async Task A_folder_cannot_move_into_itself()
    {
        var (a, _, w, t) = Make();
        _ = await a.MoveAsync(["d1"], "d1", CancellationToken.None);
        Assert.Empty(w.Calls);
        Assert.True(Assert.Single(t.Shown).IsError);
    }

    [Fact]
    public void Move_targets_leave_out_the_moved_folder_and_what_is_inside_it()
    {
        var (a, _, _, _) = Make();
        Assert.Equal(["Pics"], a.MoveTargets(["d1"]).Select(m => m.Folder.Name));
    }

    [Fact]
    public async Task Without_a_dialog_the_conflict_answer_is_Keep_both()
    {
        var (a, _, _, _) = Make();
        Assert.Equal(FileConflictChoice.KeepBoth, await a.ConflictAsker(CancellationToken.None)("a.txt"));
    }

    [Fact]
    public void The_file_menu_has_the_spec_rows_and_the_background_menu_follows_the_place()
    {
        var (a, vm, _, _) = Make();
        var providers = FilesMenus.Create(a).ToDictionary(p => p.MenuId);
        var ctx = new MenuContext(FilesMenuIds.File, [], null, null, null, null, CommandSurface.ContextMenu) { File = new FileMenuTarget("a", ["f1"]) };
        var labels = providers[FilesMenuIds.File].Build(ctx).Select(i => i.Label).ToList();
        Assert.Equal(["a.txt", "Open", "Download", "Rename", "Duplicate", "Move to", "Cut", "Copy", "Delete"], labels.Where(l => l.Length > 0));
        var bg = providers[FilesMenuIds.Background].Build(ctx with { MenuId = FilesMenuIds.Background }).Select(i => i.Label).Where(l => l.Length > 0).ToList();
        Assert.Equal(["New folder", "Upload files", "Upload folder", "Paste", "Refresh"], bg);
        Assert.False(providers[FilesMenuIds.Background].Build(ctx).Single(i => i.Label == "Paste").Enabled);
    }
}
