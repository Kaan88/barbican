// Mirrors: Rust files.rs (the actions behind the menus: delete to the trash with undo, restore, rename, move, copy and paste, duplicate, star, upload with the conflict dialog), Bulwark components/files/files-app.tsx, lib/files-clipboard.ts, undo toasts (lib/files-undo.ts);
// spec/menus.md "Confirmation labels"; D-M5-D03 (undo is the trash restore; Delete outside the trash asks nothing, Delete in the trash asks "Delete for good"). Hooks: onFileUndo, onFileFavoriteToggle (files-data fires it from ToggleStar). Owner files-ui.

using Barbican.Core.Common;
using Barbican.Core.Files;
using Barbican.Core.Plugins.Hooks;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Dialogs;

namespace Barbican.Presentation.Files;

/// <summary>What the Files menus, keys, palette and automation do to the selection and the place.</summary>
public sealed class FilesActions
{
    private readonly FilesViewModel _vm;
    private readonly IFileStore _store;
    private readonly IFileWriter _writer;
    private readonly IFileTransfer? _transfer;
    private readonly IDialogService? _dialogs;
    private readonly IToastService? _toasts;
    private readonly IHookBus? _hooks;
    private readonly IFilesPicker? _picker;
    private List<string> _clip = [];
    private bool _clipCut;
    private Func<CancellationToken, Task>? _undo;

    public FilesActions(FilesViewModel vm, IFileStore store, IFileWriter writer, IFileTransfer? transfer = null, IDialogService? dialogs = null, IToastService? toasts = null, IHookBus? hooks = null, IFilesPicker? picker = null)
    {
        ArgumentNullException.ThrowIfNull(vm);
        _vm = vm;
        _store = store;
        _writer = writer;
        _transfer = transfer;
        _dialogs = dialogs;
        _toasts = toasts;
        _hooks = hooks;
        _picker = picker;
    }

#pragma warning disable MA0046 // the events carry one value to the one screen that listens, on the UI thread
    /// <summary>The screen starts an inline rename of this key (F2, Rename).</summary>
    public event Action<string>? RenameRequested;

    /// <summary>A file the app opens in its preview modal (image, text, eml).</summary>
    public event Action<FileEntry>? PreviewRequested;

    /// <summary>A file the system opens (pdf, audio, video; D-M5-15): the screen downloads it to a temp folder and launches it.</summary>
    public event Action<FileEntry>? ExternalRequested;

#pragma warning restore MA0046

    public FilesViewModel View => _vm;

    public bool CanPaste => _clip.Count > 0;

    public bool CanUndo => _undo is not null;

    public FileEntry? Find(string key) => _store.Snapshot(_vm.AccountKey)?.Entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.Ordinal));

    public IReadOnlyList<FileEntry> Selected => [.. _vm.SelectedKeys.Select(Find).OfType<FileEntry>()];

    /// <summary>Folders open, files preview (or open outside) and join Recent.</summary>
    public void Open(string key)
    {
        if (Find(key) is not { } e)
        {
            return;
        }

        if (e.IsDirectory)
        {
            _vm.Navigate(e.Key);
            return;
        }

        _store.AddRecent(e.Key);
        if (FilesPreviewRules.KindOf(e.MediaType, e.Name) is FilesPreviewKind.Image or FilesPreviewKind.Text or FilesPreviewKind.Eml)
        {
            PreviewRequested?.Invoke(e);
        }
        else
        {
            ExternalRequested?.Invoke(e);
        }
    }

    public void ShowInFolder(string key)
    {
        if (Find(key) is { } e)
        {
            _vm.Navigate(e.ParentKey);
            _vm.SelectFile(e.Key, false);
        }
    }

    public void Rename(string key) => RenameRequested?.Invoke(key);

    public Task<Result<Unit>> RenameAsync(string key, string name, CancellationToken ct) =>
        GuardAsync(string.IsNullOrWhiteSpace(name) || name.Contains('/', StringComparison.Ordinal) ? "That name is not allowed" : null, () => _writer.RenameAsync(_vm.AccountKey, key, name.Trim(), ct), "Renamed");

    public void ToggleStar(string key) => _store.ToggleStar(key);

    public IReadOnlyList<string> Favourites() => _store.Favourites;

    public bool IsStarred(string key) => _store.Favourites.Contains(key, StringComparer.Ordinal);

    public async Task<Result<Unit>> NewFolderAsync(CancellationToken ct)
    {
        var name = await AskAsync("New folder", "Folder name", "New folder", "Create", ct).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<Unit>.Ok(default);
        }

        var r = await _writer.CreateFolderAsync(_vm.AccountKey, FolderForNew(), name.Trim(), ct).ConfigureAwait(true);
        return r.IsOk ? Result<Unit>.Ok(default) : Fail(r.Error);
    }

    public async Task<Result<Unit>> NewFileAsync(CancellationToken ct)
    {
        var name = await AskAsync("New text file", "File name", "Untitled.txt", "Create", ct).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name) || _transfer is null)
        {
            return Result<Unit>.Ok(default);
        }

        var r = await _transfer.CreateTextFileAsync(_vm.AccountKey, FolderForNew(), name.Trim(), string.Empty, ct).ConfigureAwait(true);
        return r.IsOk ? Result<Unit>.Ok(default) : Fail(r.Error);
    }

    public Task<Result<Unit>> DuplicateAsync(string key, CancellationToken ct) => GuardAsync(null, () => _writer.DuplicateAsync(_vm.AccountKey, key, ct), "Duplicated");

    /// <summary>Outside the trash: move to the trash and offer Undo. In the trash: ask, then delete for good.</summary>
    public async Task<Result<Unit>> DeleteAsync(IReadOnlyList<string> keys, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
        {
            return Result<Unit>.Ok(default);
        }

        if (_vm.InTrash)
        {
            return await PurgeAsync(keys, ct).ConfigureAwait(true);
        }

        var r = await _writer.TrashAsync(_vm.AccountKey, keys, ct).ConfigureAwait(true);
        if (!r.IsOk)
        {
            return Fail(r.Error);
        }

        var moved = keys.ToList();
        _undo = async c => _ = await _writer.RestoreAsync(_vm.AccountKey, moved, c).ConfigureAwait(true);
        Toast(keys.Count == 1 ? "Moved to trash" : $"Moved {keys.Count} items to trash", "Undo", UndoAsync);
        await _vm.RefreshAsync(ct).ConfigureAwait(true);
        return Result<Unit>.Ok(default);
    }

    public async Task<Result<Unit>> UndoAsync(CancellationToken ct)
    {
        if (_undo is not { } undo)
        {
            return Result<Unit>.Ok(default);
        }

        _undo = null;
        await undo(ct).ConfigureAwait(true);
        if (_hooks is { } h && h.HasHandlers(HookNames.OnFileUndo))
        {
            _ = h.NotifyAsync(HookNames.OnFileUndo, new FileUndoPayload("trash"), CancellationToken.None).AsTask();
        }

        await _vm.RefreshAsync(ct).ConfigureAwait(true);
        return Result<Unit>.Ok(default);
    }

    public Task<Result<Unit>> RestoreAsync(IReadOnlyList<string> keys, CancellationToken ct) => GuardAsync(null, () => _writer.RestoreAsync(_vm.AccountKey, keys, ct), keys.Count == 1 ? "Restored" : "Restored items");

    /// <summary>Delete for good: asks first ("Delete for good").</summary>
    public async Task<Result<Unit>> PurgeAsync(IReadOnlyList<string> keys, CancellationToken ct)
    {
        if (_dialogs is { } d && !await d.ShowAsync(new ConfirmDialogRequest("Delete for good", "This cannot be undone.", "Delete for good", Danger: true), ct).ConfigureAwait(true))
        {
            return Result<Unit>.Ok(default);
        }

        return await GuardAsync(null, () => _writer.PurgeAsync(_vm.AccountKey, keys, ct), "Deleted for good").ConfigureAwait(true);
    }

    public async Task<Result<Unit>> EmptyTrashAsync(CancellationToken ct)
    {
        if (_dialogs is { } d && !await d.ShowAsync(new ConfirmDialogRequest("Empty trash", "Delete everything in the trash.", "Delete everything in the trash", Danger: true), ct).ConfigureAwait(true))
        {
            return Result<Unit>.Ok(default);
        }

        return await GuardAsync(null, () => _writer.EmptyTrashAsync(_vm.AccountKey, ct), "Trash emptied").ConfigureAwait(true);
    }

    public void Cut(IReadOnlyList<string> keys)
    {
        _clip = [.. keys];
        _clipCut = true;
    }

    public void Copy(IReadOnlyList<string> keys)
    {
        _clip = [.. keys];
        _clipCut = false;
    }

    public async Task<Result<Unit>> PasteAsync(CancellationToken ct)
    {
        if (_clip.Count == 0)
        {
            return Result<Unit>.Ok(default);
        }

        var keys = _clip;
        var dest = FolderForNew();
        var r = _clipCut ? await _writer.MoveAsync(_vm.AccountKey, keys, dest, ct).ConfigureAwait(true) : await _writer.CopyAsync(_vm.AccountKey, keys, dest, ct).ConfigureAwait(true);
        if (!r.IsOk)
        {
            return Fail(r.Error);
        }

        if (_clipCut)
        {
            _clip = [];
        }

        await _vm.RefreshAsync(ct).ConfigureAwait(true);
        return r;
    }

    public Task<Result<Unit>> MoveAsync(IReadOnlyList<string> keys, string? destination, CancellationToken ct) =>
        GuardAsync(keys.Contains(destination ?? string.Empty, StringComparer.Ordinal) ? "A folder cannot move into itself" : null, () => _writer.MoveAsync(_vm.AccountKey, keys, destination, ct), "Moved");

    /// <summary>The valid targets of a move: the root and every folder not inside one of the moved items, trash excluded.</summary>
    public IReadOnlyList<FileMoveTarget> MoveTargets(IReadOnlyList<string> keys)
    {
        var all = _store.Snapshot(_vm.AccountKey)?.Entries ?? [];
        var result = new List<FileMoveTarget>();
        void Walk(string? parent, int depth)
        {
            foreach (var f in FilesListRules.Sort(FilesListRules.Visible([.. all.Where(e => e.IsDirectory && string.Equals(e.ParentKey, parent, StringComparison.Ordinal) && !e.Shared)], false), FilesSortKey.Name, false))
            {
                if (keys.Contains(f.Key, StringComparer.Ordinal))
                {
                    continue;
                }

                result.Add(new FileMoveTarget(f, depth));
                Walk(f.Key, depth + 1);
            }
        }

        Walk(null, 0);
        return result;
    }

    public async Task<Result<Unit>> MoveWithDialogAsync(IReadOnlyList<string> keys, CancellationToken ct)
    {
        if (_dialogs is null)
        {
            return Result<Unit>.Ok(default);
        }

        var answer = await _dialogs.ShowAsync(new FileMoveDialogRequest(MoveTargets(keys)), ct).ConfigureAwait(true);
        return answer is null ? Result<Unit>.Ok(default) : await MoveAsync(keys, answer.Key, ct).ConfigureAwait(true);
    }

    public async Task<Result<Unit>> DownloadAsync(IReadOnlyList<string> keys, CancellationToken ct)
    {
        if (_transfer is null || keys.Count == 0)
        {
            return Result<Unit>.Ok(default);
        }

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var single = keys.Count == 1 && Find(keys[0]) is { IsDirectory: false };
        var r = single ? await _transfer.DownloadAsync(_vm.AccountKey, keys[0], dir, ct).ConfigureAwait(true) : await _transfer.DownloadZipAsync(_vm.AccountKey, keys, dir, ct).ConfigureAwait(true);
        if (!r.IsOk)
        {
            return Fail(r.Error);
        }

        Toast("Saved to Downloads");
        return Result<Unit>.Ok(default);
    }

    public async Task<Result<Unit>> UploadAsync(bool folder, CancellationToken ct)
    {
        if (_picker is null || _transfer is null)
        {
            return Result<Unit>.Ok(default);
        }

        if (folder)
        {
            return await UploadFolderAsync(await _picker.PickFolderAsync(ct).ConfigureAwait(true), ct).ConfigureAwait(true);
        }

        return await UploadPathsAsync(await _picker.PickFilesAsync(ct).ConfigureAwait(true) ?? [], ct).ConfigureAwait(true);
    }

    public async Task<Result<Unit>> UploadFolderAsync(string? path, CancellationToken ct)
    {
        if (path is null || _transfer is null)
        {
            return Result<Unit>.Ok(default);
        }

        var ask = ConflictAsker(ct);
        var r = await _transfer.UploadFolderAsync(_vm.AccountKey, FolderForNew(), path, ask, null, ct).ConfigureAwait(true);
        return await AfterUploadAsync(r.IsOk, r.Error, ct).ConfigureAwait(true);
    }

    /// <summary>Uploads local files (the picker, or an OS file drop onto the window).</summary>
    public async Task<Result<Unit>> UploadPathsAsync(IReadOnlyList<string> paths, CancellationToken ct)
    {
        if (paths.Count == 0 || _transfer is null)
        {
            return Result<Unit>.Ok(default);
        }

        var r = await _transfer.UploadFilesAsync(_vm.AccountKey, FolderForNew(), paths, ConflictAsker(ct), null, ct).ConfigureAwait(true);
        return await AfterUploadAsync(r.IsOk, r.Error, ct).ConfigureAwait(true);
    }

    /// <summary>Asks the conflict dialog once, then repeats the answer when "Apply to all" was ticked. The default button is Keep both.</summary>
    public Func<string, Task<FileConflictChoice>> ConflictAsker(CancellationToken ct)
    {
        FileConflictChoice? all = null;
        return async name =>
        {
            if (all is { } remembered)
            {
                return remembered;
            }

            if (_dialogs is null)
            {
                return FileConflictChoice.KeepBoth;
            }

            var answer = await _dialogs.ShowAsync(new FileConflictDialogRequest(name), ct).ConfigureAwait(true) ?? new FileConflictAnswer(FileConflictChoice.Skip, false);
            if (answer.ApplyToAll)
            {
                all = answer.Choice;
            }

            return answer.Choice;
        };
    }

    public Task RefreshAsync(CancellationToken ct) => _vm.RefreshAsync(ct);

    public void RemoveRecent(string key) => _store.RemoveRecent(key);

    /// <summary>The folder a New, Paste or Upload goes into: the open folder, else the root.</summary>
    public string? FolderForNew() => _vm.Location is { } l && !l.StartsWith('*') ? l : null;

    private async Task<Result<Unit>> AfterUploadAsync(bool ok, Barbican.Core.Jmap.Errors.JmapError? error, CancellationToken ct)
    {
        if (!ok)
        {
            return Fail(error);
        }

        Toast("Upload finished");
        await _vm.RefreshAsync(ct).ConfigureAwait(true);
        return Result<Unit>.Ok(default);
    }

    private async Task<Result<Unit>> GuardAsync(string? problem, Func<Task<Result<Unit>>> run, string? success)
    {
        if (problem is not null)
        {
            Toast(problem, error: true);
            return Result<Unit>.Ok(default);
        }

        var r = await run().ConfigureAwait(true);
        if (!r.IsOk)
        {
            return Fail(r.Error);
        }

        if (success is not null)
        {
            Toast(success);
        }

        await _vm.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
        return r;
    }

    private async Task<string?> AskAsync(string title, string label, string initial, string confirm, CancellationToken ct) =>
        _dialogs is null ? null : await _dialogs.ShowAsync(new PromptDialogRequest(title, label, initial, confirm), ct).ConfigureAwait(true);

    private Result<Unit> Fail(Barbican.Core.Jmap.Errors.JmapError? error)
    {
        Toast(error?.ToString() ?? "That did not work", error: true);
        return Result<Unit>.Fail(error!);
    }

    private void Toast(string message, string? action = null, Func<CancellationToken, Task>? run = null, bool error = false) =>
        _toasts?.Show(new ToastRequest(message, error, action, run));
}
