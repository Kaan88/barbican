// Mirrors: Rust shortcuts.rs files keys, Bulwark components/files/files-app.tsx (the key handler), B-FILE-012; the commands behind spec/menus.md MENU-FILE and MENU-FILES-BG. The argument is the keys (one per line, a tab and a destination for a move);
// an empty argument means the current selection. Owner files-ui.

using Barbican.Core.Common;
using Barbican.Presentation.Commands;

namespace Barbican.Presentation.Files;

/// <summary>Creates the Files commands.</summary>
public static class FilesCommands
{
    public const int Count = 26;

    public static IReadOnlyList<IAppCommand> Create(FilesActions a)
    {
        ArgumentNullException.ThrowIfNull(a);
        IReadOnlyList<string> Keys(CommandContext c) => string.IsNullOrEmpty(c.Argument) ? [.. a.View.SelectedKeys] : FilesMenus.KeysOf(c.Argument);
        IAppCommand Sync(string id, Action<CommandContext> run, Func<CommandContext, bool>? can = null) => DelegateAppCommand.Sync(id, run, can);
        IAppCommand Run(string id, Func<CommandContext, CancellationToken, Task<Result<Unit>>> run, Func<CommandContext, bool>? can = null) => new DelegateAppCommand(id, can ?? (_ => true), run);
        bool Any(CommandContext c) => Keys(c).Count > 0;
        bool One(CommandContext c) => Keys(c).Count == 1;

        return
        [
            Sync(FilesCommandIds.Open, c => a.Open(Keys(c)[0]), One),
            Run(FilesCommandIds.Download, (c, ct) => a.DownloadAsync(Keys(c), ct), Any),
            Sync(FilesCommandIds.Star, c => a.ToggleStar(Keys(c)[0]), One),
            Sync(FilesCommandIds.Rename, c => a.Rename(Keys(c)[0]), One),
            Run(FilesCommandIds.Duplicate, (c, ct) => a.DuplicateAsync(Keys(c)[0], ct), One),
            Run(FilesCommandIds.MoveTo, (c, ct) => a.MoveAsync(FilesMenus.KeysOf(c.Argument), c.Argument is { } s && s.Contains('\t', StringComparison.Ordinal) && s.Split('\t')[1] is { Length: > 0 } d ? d : null, ct), Any),
            Run(FilesCommandIds.MoveDialog, (c, ct) => a.MoveWithDialogAsync(Keys(c), ct), c => Any(c) && !a.View.InTrash),
            Sync(FilesCommandIds.Cut, c => a.Cut(Keys(c)), Any),
            Sync(FilesCommandIds.Copy, c => a.Copy(Keys(c)), Any),
            Run(FilesCommandIds.Paste, (_, ct) => a.PasteAsync(ct), _ => a.CanPaste && !a.View.InTrash),
            Run(FilesCommandIds.Delete, (c, ct) => a.DeleteAsync(Keys(c), ct), Any),
            Run(FilesCommandIds.Restore, (c, ct) => a.RestoreAsync(Keys(c), ct), c => Any(c) && a.View.InTrash),
            Run(FilesCommandIds.Purge, (c, ct) => a.PurgeAsync(Keys(c), ct), Any),
            Run(FilesCommandIds.EmptyTrash, (_, ct) => a.EmptyTrashAsync(ct), _ => a.View.InTrash),
            Run(FilesCommandIds.NewFolder, (_, ct) => a.NewFolderAsync(ct), _ => !a.View.InTrash),
            Run(FilesCommandIds.NewFile, (_, ct) => a.NewFileAsync(ct), _ => !a.View.InTrash),
            Run(FilesCommandIds.Upload, (_, ct) => a.UploadAsync(false, ct), _ => !a.View.InTrash),
            Run(FilesCommandIds.UploadFolder, (_, ct) => a.UploadAsync(true, ct), _ => !a.View.InTrash),
            Run(FilesCommandIds.Refresh, async (_, ct) =>
            {
                await a.RefreshAsync(ct).ConfigureAwait(true);
                return Result<Unit>.Ok(default);
            }),
            Run(FilesCommandIds.Undo, (_, ct) => a.UndoAsync(ct), _ => a.CanUndo),
            Sync(FilesCommandIds.ShowInFolder, c => a.ShowInFolder(Keys(c)[0]), One),
            Sync(FilesCommandIds.RemoveRecent, c => a.RemoveRecent(Keys(c)[0]), One),
            Sync(FilesCommandIds.ToggleView, _ => a.View.ViewMode = a.View.ViewMode == FilesViewMode.List ? FilesViewMode.Grid : FilesViewMode.List),
            Sync(FilesCommandIds.SelectAll, _ => a.View.SelectAll()),
            Sync(FilesCommandIds.Details, _ => a.View.ShowDetails = !a.View.ShowDetails),
            Sync(FilesCommandIds.Sort, c => FilesSortRules.Apply(a.View, c.Argument)),
        ];
    }
}
