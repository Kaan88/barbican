// Mirrors: Rust dnd.rs drop :52 (mail onto a folder: already_in skip, scheduled and unified rows refused; onto a tag: labels the messages; folder onto a folder:
// plan_folder_drop :149), start_drag :16; Bulwark components/layout/sidebar.tsx drop targets, lib/folder-tree-dnd.ts:41-134. Hooks onDragStart, onDragEnd, onEmailDrop,
// onTagDrop. Owner actions.
//
// Real dragging is the view's; everything the drop does goes through here, so the automation `dnd-sim` command runs exactly the code a real drop runs.
// M2 hotfix (HAND TEST 2 "can't drag"): the views had no drag source and no drop target at all. They now use the platform's in-app drag and drop
// (ListView.CanDragItems on the message list and the folder list, AllowDrop on folder and tag rows) and keep what is dragged here: Begin*Drag when
// the drag starts, CanDrop* while it is over a row, DropOn*Async on the drop, CancelDrag when it ends without one. Rust dnd.rs ids_for_mail :47 (the
// dragged row stands for the ticked rows when it is one of them), Bulwark lib/folder-tree-dnd.ts:41 (a folder's top quarter is before, its bottom
// quarter after, the middle inside).

using Barbican.Core.Common;
using Barbican.Core.DragDrop;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Write.Payloads;
using Barbican.Core.Plugins.Hooks;
using Barbican.Presentation.Actions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.DragDrop;

/// <summary>The state and the drop paths of dragging messages, folders and tags.</summary>
public sealed class DropController(MailActionService actions, IHookBus hooks) : ObservableObject
{
    /// <summary>What a mail drag is called in the hook payloads.</summary>
    public const string MailKind = "email";

    private string? _target;
    private bool _active;
    private string _label = string.Empty;
    private IReadOnlyList<MailTarget> _mail = [];
    private string? _folderAccount;
    private string? _folder;

    /// <summary>A drag is in progress (the ghost and the highlight show).</summary>
    public bool IsDragging
    {
        get => _active;
        private set => SetProperty(ref _active, value);
    }

    /// <summary>What the ghost says ("3 messages", the folder's name).</summary>
    public string Label
    {
        get => _label;
        private set => SetProperty(ref _label, value);
    }

    /// <summary>The id of the folder or tag under the pointer, highlighted as a drop target; null when none.</summary>
    public string? Target
    {
        get => _target;
        set => SetProperty(ref _target, value);
    }

    /// <summary>A drag of messages starts: the dragged row stands for the selection when it is in it, else for itself (Rust ids_for_mail :47).</summary>
    public void BeginMailDrag(IReadOnlyList<string> emailIds, string label)
    {
        ArgumentNullException.ThrowIfNull(emailIds);
        Label = label ?? string.Empty;
        IsDragging = true;
        hooks.Fire(HookNames.OnDragStart, () => new DragStartPayload(MailKind, emailIds));
    }

    /// <summary>The messages being dragged (empty when no mail drag is in progress).</summary>
    public IReadOnlyList<MailTarget> DraggedMail => _mail;

    /// <summary>The folder being dragged, or null.</summary>
    public string? DraggedFolder => _folder;

    /// <summary>A real drag of messages starts with these targets (the view resolved the selection rule).</summary>
    public void BeginMailDrag(IReadOnlyList<MailTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        _folder = null;
        _folderAccount = null;
        _mail = [.. targets];
        BeginMailDrag([.. targets.Select(t => t.EmailId)], targets.Count == 1 ? "1 message" : $"{targets.Count} messages");
    }

    /// <summary>A real drag of a folder starts.</summary>
    public void BeginFolderDrag(string accountKey, string folderId, string label)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentNullException.ThrowIfNull(folderId);
        _mail = [];
        _folderAccount = accountKey;
        _folder = folderId;
        Label = label ?? string.Empty;
        IsDragging = true;
        hooks.Fire(HookNames.OnDragStart, () => new DragStartPayload("folder", [folderId]));
    }

    /// <summary>Whether what is dragged may be dropped on folder <paramref name="folderId"/> (messages not all only there already; a folder not onto itself).</summary>
    public bool CanDropOnFolder(string folderId) =>
        _folder is { } dragged
            ? !string.Equals(dragged, folderId, StringComparison.Ordinal)
            : _mail.Count > 0 && _mail.Any(t => !(t.MailboxIds.Count == 1 && t.MailboxIds.ContainsKey(folderId)));

    /// <summary>Whether what is dragged may be dropped on a tag (messages only).</summary>
    public bool CanDropOnTag => _folder is null && _mail.Count > 0;

    /// <summary>Where on a folder row a folder drop lands: the top quarter is before, the bottom quarter after, the middle inside (Bulwark folder-tree-dnd.ts).</summary>
    public static DropZone ZoneAt(double y, double height) =>
        height <= 0 ? DropZone.Inside : y < height * 0.25 ? DropZone.Before : y > height * 0.75 ? DropZone.After : DropZone.Inside;

    /// <summary>The real drop on a folder row: the dragged messages move there, or the dragged folder is reordered or nested by <paramref name="zone"/>.</summary>
    public Task<Result<Unit>> DropOnFolderAsync(string folderId, DropZone zone, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folderId);
        if (_folder is { } dragged)
        {
            var account = _folderAccount ?? string.Empty;
            Clear();
            return DropFolderAsync(account, dragged, folderId, zone, ct);
        }

        var mail = _mail;
        Clear();
        return mail.Count == 0 ? Task.FromResult(Result<Unit>.Ok(default)) : DropMailOnFolderAsync(mail, folderId, ct);
    }

    /// <summary>The real drop on a tag row: the dragged messages get the tag.</summary>
    public Task<Result<Unit>> DropOnTagAsync(string tagId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tagId);
        var mail = _folder is null ? _mail : [];
        Clear();
        return mail.Count == 0 ? Task.FromResult(Result<Unit>.Ok(default)) : DropMailOnTagAsync(mail, tagId, ct);
    }

    /// <summary>The real drag ended; when no drop took it, onDragEnd fires with dropped = false.</summary>
    public void CancelDrag()
    {
        if (!IsDragging)
        {
            return;
        }

        var (kind, ids) = _folder is { } f ? ("folder", (IReadOnlyList<string>)[f]) : (MailKind, [.. _mail.Select(t => t.EmailId)]);
        Clear();
        EndDrag(kind, ids, dropped: false);
    }

    private void Clear()
    {
        _mail = [];
        _folder = null;
        _folderAccount = null;
    }

    /// <summary>The drag ended without a drop, or after one.</summary>
    public void EndDrag(string kind, IReadOnlyList<string> ids, bool dropped)
    {
        ArgumentNullException.ThrowIfNull(ids);
        IsDragging = false;
        Target = null;
        hooks.Fire(HookNames.OnDragEnd, () => new DragEndPayload(kind, ids, dropped));
    }

    /// <summary>Messages dropped on a folder: moved there (those already only there stay), with Undo.</summary>
    public async Task<Result<Unit>> DropMailOnFolderAsync(IReadOnlyList<MailTarget> targets, string folderId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var ids = targets.Select(t => t.EmailId).ToArray();
        var result = await actions.MoveAsync(targets, folderId, ct).ConfigureAwait(true);
        if (result.IsOk && targets.Any(t => !(t.MailboxIds.Count == 1 && t.MailboxIds.ContainsKey(folderId))))
        {
            hooks.Fire(HookNames.OnEmailDrop, () => new EmailDropPayload(ids, folderId));
        }

        EndDrag(MailKind, ids, result.IsOk);
        return result;
    }

    /// <summary>Messages dropped on a tag: the tag is added to all of them (Bulwark: dropping on a tag labels the messages).</summary>
    public async Task<Result<Unit>> DropMailOnTagAsync(IReadOnlyList<MailTarget> targets, string tagId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var ids = targets.Select(t => t.EmailId).ToArray();
        var result = await actions.SetTagAsync(targets, tagId, true, ct).ConfigureAwait(true);
        if (result.IsOk)
        {
            hooks.Fire(HookNames.OnTagDrop, () => new TagDropPayload(ids, MailActionService.TagKeyword(tagId)));
        }

        EndDrag(MailKind, ids, result.IsOk);
        return result;
    }

    /// <summary>A folder dropped on another: reorder or nest (cycles and role folders are refused with a message).</summary>
    public async Task<Result<Unit>> DropFolderAsync(string accountKey, string drag, string target, DropZone zone, CancellationToken ct)
    {
        var result = await actions.MoveFolderAsync(accountKey, drag, target, zone, ct).ConfigureAwait(true);
        EndDrag("folder", [drag], result.IsOk);
        return result;
    }
}
