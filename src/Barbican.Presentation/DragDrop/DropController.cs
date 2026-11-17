// Mirrors: Rust dnd.rs drop :52 (mail onto a folder: already_in skip, scheduled and unified rows refused; onto a tag: labels the messages; folder onto a folder:
// plan_folder_drop :149), start_drag :16; Bulwark components/layout/sidebar.tsx drop targets, lib/folder-tree-dnd.ts:41-134. Hooks onDragStart, onDragEnd, onEmailDrop,
// onTagDrop. Owner actions.
//
// Real dragging is the view's (an in-window ghost, hit testing); everything the drop does goes through here, so the automation `dnd-sim` command runs exactly the
// code a real drop runs.

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
        var result = await actions.MoveAsync(targets, folderId, ct).ConfigureAwait(false);
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
        var result = await actions.SetTagAsync(targets, tagId, true, ct).ConfigureAwait(false);
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
        var result = await actions.MoveFolderAsync(accountKey, drag, target, zone, ct).ConfigureAwait(false);
        EndDrag("folder", [drag], result.IsOk);
        return result;
    }
}
