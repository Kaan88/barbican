// Mirrors: M2 plan 6.1 (the commands of actions: read, star, tags, move, archive, junk, delete, restore, pin, empty, mark folder read, folder create/rename/delete,
// list navigation and selection, undo); Rust ctxmenu.rs pick :830, reader.rs pick :1084, shortcuts.rs tables; Bulwark lib/app-commands.ts, components/email/message-actions.ts. Owner actions.

using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Mail.Lists;
using Barbican.Core.Mail.Model;
using Barbican.Core.Storage;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Menus;

namespace Barbican.Presentation.Actions;

/// <summary>Builds the commands of the mail and folder actions over the action service and the message list.</summary>
public static class ActionCommandSet
{
    private static readonly Result<Unit> Done = Result<Unit>.Ok(default);

    /// <summary>The ids <see cref="Build"/> makes a command for (the registration adds one IAppCommand per id; a test keeps this in step with Build).</summary>
    public static IReadOnlyList<string> Ids { get; } =
    [
        CommandIds.MarkRead,
        CommandIds.MarkUnread,
        CommandIds.ToggleRead,
        CommandIds.Star,
        CommandIds.Unstar,
        CommandIds.ToggleStar,
        CommandIds.Pin,
        CommandIds.Unpin,
        CommandIds.Tag,
        CommandIds.Untag,
        CommandIds.MoveTo,
        CommandIds.MoveToInbox,
        CommandIds.CopyToAccount,
        CommandIds.Archive,
        CommandIds.Delete,
        CommandIds.DeleteForever,
        CommandIds.Restore,
        CommandIds.RemoveFromFolder,
        CommandIds.ReportSpam,
        CommandIds.NotSpam,
        CommandIds.MarkFolderRead,
        CommandIds.MarkAllFoldersRead,
        CommandIds.EmptyTrash,
        CommandIds.EmptySpam,
        CommandIds.NewFolder,
        CommandIds.NewSubfolder,
        CommandIds.RenameFolder,
        CommandIds.DeleteFolder,
        CommandIds.RefreshFolder,
        CommandIds.SelectNext,
        CommandIds.SelectPrevious,
        CommandIds.ExtendDown,
        CommandIds.ExtendUp,
        CommandIds.OpenSelected,
        CommandIds.CloseReader,
        CommandIds.SelectAll,
        CommandIds.SelectAllInFolder,
        CommandIds.ClearSelection,
        CommandIds.ToggleSelect,
        CommandIds.RefreshList,
        CommandIds.SortList,
        CommandIds.ToggleGroupByDate,
        CommandIds.Undo,
    ];

    /// <summary>Every command this stream owns.</summary>
    public static IReadOnlyList<IAppCommand> Build(MailActionService actions, MessageListViewModel list, IMailWriterSource writers, IMailboxDirectory folders, IUndoService undo, IDialogService? dialogs = null, IPrefsStore? prefs = null, IPrefsEditor? prefsEditor = null)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(writers);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(undo);
        return [.. MailCommands(actions, list, writers, dialogs), .. FolderCommands(actions, list, writers, folders, dialogs), .. ListCommands(list), .. ListViewCommands(list, prefs, prefsEditor), Undo(undo)];
    }

    /// <summary>The targets a command acts on: the ones the surface names, else the list's selection (all of the folder's when "select all" was taken).</summary>
    private static async Task<IReadOnlyList<MailTarget>> TargetsAsync(CommandContext c, MessageListViewModel list, CancellationToken ct) =>
        c.Targets.Count > 0 ? c.Targets : await list.ResolveTargetsAsync(ct).ConfigureAwait(true);

    private static IReadOnlyList<MailTarget> Known(CommandContext c, MessageListViewModel list) => c.Targets.Count > 0 ? c.Targets : list.Current;

    private static bool Writable(CommandContext c, MessageListViewModel list, IMailWriterSource writers, Func<IReadOnlyList<MailTarget>, bool>? extra = null)
    {
        var t = Known(c, list);
        return t.Count > 0 && writers.CanWrite(t[0].AccountKey) && (extra?.Invoke(t) ?? true);
    }

    private static ActionCommand Mail(string id, MessageListViewModel list, IMailWriterSource writers, Func<IReadOnlyList<MailTarget>, bool>? when, Func<IReadOnlyList<MailTarget>, CommandContext, CancellationToken, Task<Result<Unit>>> run) =>
        new(id, c => Writable(c, list, writers, when), async (c, ct) => await run(await TargetsAsync(c, list, ct).ConfigureAwait(true), c, ct).ConfigureAwait(true));

    private static IEnumerable<IAppCommand> MailCommands(MailActionService a, MessageListViewModel list, IMailWriterSource w, IDialogService? dialogs)
    {
        yield return Mail(CommandIds.MarkRead, list, w, null, (t, _, ct) => a.SetReadAsync(t, true, ct));
        yield return Mail(CommandIds.MarkUnread, list, w, null, (t, _, ct) => a.SetReadAsync(t, false, ct));
        yield return Mail(CommandIds.ToggleRead, list, w, null, (t, _, ct) => a.ToggleReadAsync(t, ct));
        yield return Mail(CommandIds.Star, list, w, null, (t, _, ct) => a.SetStarAsync(t, true, ct));
        yield return Mail(CommandIds.Unstar, list, w, null, (t, _, ct) => a.SetStarAsync(t, false, ct));
        yield return Mail(CommandIds.ToggleStar, list, w, null, (t, _, ct) => a.ToggleStarAsync(t, ct));
        yield return Mail(CommandIds.Pin, list, w, t => t.Any(x => !x.Keywords.IsSet("$pinned")), (t, _, ct) => a.SetPinnedAsync(t, true, ct));
        yield return Mail(CommandIds.Unpin, list, w, t => t.Any(x => x.Keywords.IsSet("$pinned")), (t, _, ct) => a.SetPinnedAsync(t, false, ct));

        // the tag picker: a tag every message has is removed, otherwise it is added to all (tri-state, tag-picker.tsx)
        yield return Mail(CommandIds.Tag, list, w, null, (t, c, ct) => c.Argument is { Length: > 0 } tag ? a.SetTagAsync(t, tag, MailActionService.TagState(t, tag) != true, ct) : Task.FromResult(Done));
        yield return Mail(CommandIds.Untag, list, w, null, (t, c, ct) => c.Argument is { Length: > 0 } tag ? a.SetTagAsync(t, tag, false, ct) : Task.FromResult(Done));
        yield return Mail(CommandIds.MoveTo, list, w, null, (t, c, ct) => c.Argument is { Length: > 0 } to ? a.MoveAsync(t, to, ct) : Task.FromResult(Done));
        yield return Mail(CommandIds.MoveToInbox, list, w, _ => list.ArchiveMovesToInbox, (t, _, ct) => a.MoveToInboxAsync(t, ct));
        // G-048: the other signed-in accounts' folders, picked in a dialog (Bulwark email-context-menu.tsx:288-290, :371-379)
        yield return new ActionCommand(CommandIds.CopyToAccount, c => Known(c, list).Count > 0 || list.HasChecked, async (c, ct) =>
        {
            var targets = await TargetsAsync(c, list, ct).ConfigureAwait(true);
            if (targets.Count == 0 || dialogs is null)
            {
                return Done;
            }

            var offered = await a.CopyTargetsAsync(targets[0].Login, ct).ConfigureAwait(true);
            if (!offered.IsOk || offered.Value!.Count == 0)
            {
                return Done;
            }

            var answer = await dialogs.ShowAsync(new CopyToAccountDialogRequest(offered.Value), ct).ConfigureAwait(true);
            if (answer is null)
            {
                return Done;
            }

            var folder = offered.Value.FirstOrDefault(x => string.Equals(x.AccountKey, answer.AccountKey, StringComparison.Ordinal))?
                .Folders.FirstOrDefault(f => string.Equals(f.MailboxId, answer.MailboxId, StringComparison.Ordinal));
            return await a.CopyToAccountAsync(targets, answer.AccountKey, answer.MailboxId, folder?.Name ?? "folder", ct).ConfigureAwait(true);
        });
        yield return Mail(CommandIds.Archive, list, w, _ => list.Role != MailboxRole.Archive, (t, _, ct) => a.ArchiveAsync(t, ct));
        yield return Mail(CommandIds.Delete, list, w, null, (t, _, ct) => a.DeleteAsync(t, ct));
        yield return Mail(CommandIds.DeleteForever, list, w, null, (t, _, ct) => a.DeleteForeverAsync(t, ct));
        yield return Mail(CommandIds.Restore, list, w, _ => list.Role == MailboxRole.Trash, (t, _, ct) => a.RestoreAsync(t, ct));
        yield return Mail(CommandIds.RemoveFromFolder, list, w, _ => list.MailboxId is not null, (t, _, ct) => list.MailboxId is { } id ? a.RemoveFromFolderAsync(t, id, ct) : Task.FromResult(Done));
        yield return Mail(CommandIds.ReportSpam, list, w, _ => list.Role != MailboxRole.Junk, (t, _, ct) => a.ReportSpamAsync(t, true, ct));
        yield return Mail(CommandIds.NotSpam, list, w, _ => list.Role == MailboxRole.Junk, (t, _, ct) => a.ReportSpamAsync(t, false, ct));
    }

    private static IEnumerable<IAppCommand> FolderCommands(MailActionService a, MessageListViewModel list, IMailWriterSource w, IMailboxDirectory dir, IDialogService? dialogs)
    {
        bool Can(CommandContext c, string right) => c.Mailbox is { } m && w.CanWrite(m.AccountKey) && HasRight(dir, m.MailboxId, right);
        MailboxRef? RoleRef(MailboxRole role, string? account) =>
            dir.Tree.All.FirstOrDefault(m => m.Role == role) is { } f && account is not null ? new MailboxRef(account, string.Empty, f.Id, MailboxRoleName(role)) : null;
        string? Account(CommandContext c) => c.AccountKey ?? c.Mailbox?.AccountKey ?? (list.Current.Count > 0 ? list.Current[0].AccountKey : null);

        yield return new ActionCommand(CommandIds.MarkAllFoldersRead, c => Account(c) is { } k && w.CanWrite(k), (c, ct) => a.MarkAllFoldersReadAsync(Account(c)!, ct));
        yield return new ActionCommand(CommandIds.MarkFolderRead, c => Can(c, "maySetSeen"), (c, ct) => a.MarkFolderReadAsync(c.Mailbox!, string.Equals(c.Argument, "children", StringComparison.Ordinal), ct));
        yield return new ActionCommand(CommandIds.EmptyTrash, c => RoleRef(MailboxRole.Trash, Account(c)) is { } r && w.CanWrite(r.AccountKey), (c, ct) => a.EmptyFolderAsync(RoleRef(MailboxRole.Trash, Account(c))!, ct));
        yield return new ActionCommand(CommandIds.EmptySpam, c => RoleRef(MailboxRole.Junk, Account(c)) is { } r && w.CanWrite(r.AccountKey), (c, ct) => a.EmptyFolderAsync(RoleRef(MailboxRole.Junk, Account(c))!, ct));
        // a name that is not given with the command is asked for in the prompt dialog (Rust ctxmenu.rs PROMPT :32); cancelling does nothing
        async Task<string?> NameAsync(CommandContext c, string title, string initial, string confirm, CancellationToken ct) =>
            !string.IsNullOrEmpty(c.Argument) ? c.Argument
            : dialogs is null ? string.Empty
            : await dialogs.ShowAsync(new PromptDialogRequest(title, "Name", initial, confirm), ct).ConfigureAwait(true);
        string CurrentName(CommandContext c) => c.Mailbox is { } m ? dir.Tree.All.FirstOrDefault(x => string.Equals(x.Id, m.MailboxId, StringComparison.Ordinal))?.Name ?? string.Empty : string.Empty;

        yield return new ActionCommand(CommandIds.NewFolder, c => Account(c) is { } k && w.CanWrite(k), async (c, ct) =>
            await NameAsync(c, "New folder", string.Empty, "Create", ct).ConfigureAwait(true) is { } name
                ? (await a.CreateFolderAsync(Account(c)!, name, null, ct).ConfigureAwait(true)).Map(_ => default(Unit))
                : Done);
        yield return new ActionCommand(CommandIds.NewSubfolder, c => Can(c, "mayCreateChild"), async (c, ct) =>
            await NameAsync(c, "New subfolder", string.Empty, "Create", ct).ConfigureAwait(true) is { } name
                ? (await a.CreateFolderAsync(c.Mailbox!.AccountKey, name, c.Mailbox.MailboxId, ct).ConfigureAwait(true)).Map(_ => default(Unit))
                : Done);
        yield return new ActionCommand(CommandIds.RenameFolder, c => Can(c, "mayRename"), async (c, ct) =>
            await NameAsync(c, "Rename folder", CurrentName(c), "Rename", ct).ConfigureAwait(true) is { } name
                ? await a.RenameFolderAsync(c.Mailbox!.AccountKey, c.Mailbox.MailboxId, name, ct).ConfigureAwait(true)
                : Done);
        yield return new ActionCommand(CommandIds.DeleteFolder, c => Can(c, "mayDelete"), (c, ct) => a.DeleteFolderAsync(c.Mailbox!.AccountKey, c.Mailbox.MailboxId, ct));
        yield return new ActionCommand(CommandIds.RefreshFolder, _ => true, async (_, _) =>
        {
            await list.ReloadAsync().ConfigureAwait(true);
            return Done;
        });
    }

    private static IEnumerable<IAppCommand> ListCommands(MessageListViewModel list)
    {
        static ActionCommand Sync(string id, Func<bool> can, Action run) => new(id, _ => can(), (_, _) =>
        {
            run();
            return Task.FromResult(Done);
        });

        yield return Sync(CommandIds.SelectNext, () => true, () => list.MoveSelection(1));
        yield return Sync(CommandIds.SelectPrevious, () => true, () => list.MoveSelection(-1));
        yield return Sync(CommandIds.ExtendDown, () => true, () => list.ExtendBy(1));
        yield return Sync(CommandIds.ExtendUp, () => true, () => list.ExtendBy(-1));
        yield return Sync(CommandIds.OpenSelected, () => list.Selected is not null, () => list.OpenSelected());
        yield return Sync(CommandIds.CloseReader, () => true, list.CloseReader);
        yield return Sync(CommandIds.SelectAll, () => true, list.SelectAllLoaded);
        yield return Sync(CommandIds.SelectAllInFolder, () => list.CanSelectAllInFolder, list.SelectAllInFolder);
        yield return Sync(CommandIds.ClearSelection, () => list.HasChecked, list.ClearChecked);
        yield return Sync(CommandIds.ToggleSelect, () => list.Selected is not null, () =>
        {
            if (list.Selected is { } row)
            {
                list.ToggleCheck(row);
            }
        });
        yield return new ActionCommand(CommandIds.RefreshList, _ => true, async (_, _) =>
        {
            await list.ReloadAsync().ConfigureAwait(true);
            return Done;
        });
    }

    // the list's folder-actions menu: Sort and Group by date write the settings Settings shows (Bulwark mail-list-menus.tsx:50-60 applySortChoice, :111-126)
    private static IEnumerable<IAppCommand> ListViewCommands(MessageListViewModel list, IPrefsStore? prefs, IPrefsEditor? editor)
    {
        yield return new ActionCommand(CommandIds.SortList, c => editor is not null && ListSortChoices.LevelsOf(c.Argument ?? string.Empty) is not null, (c, _) =>
        {
            var levels = ListSortChoices.LevelsOf(c.Argument ?? string.Empty)!;
            editor!.Put("messageListOrder", MessageListOrder.ToJson(levels));
            // the order applies to the Inbox only unless "apply to all folders" is on: picked in another folder it turns that on, or the pick would do nothing there
            if (levels.Count > 0 && list.Role != MailboxRole.Inbox && string.Equals(prefs?.Current.Str("messageListOrderScope", "inbox"), "inbox", StringComparison.Ordinal))
            {
                editor.Put("messageListOrderScope", JsonValue.Create("all"));
            }

            return Task.FromResult(Done);
        });
        yield return new ActionCommand(CommandIds.ToggleGroupByDate, _ => editor is not null && prefs is not null, (_, _) =>
        {
            editor!.Put("groupListByDate", JsonValue.Create(!prefs!.Current.Bool("groupListByDate", false)));
            return Task.FromResult(Done);
        });
    }

    private static ActionCommand Undo(IUndoService undo) =>
        new(CommandIds.Undo, _ => undo.History.Count > 0, async (_, ct) =>
        {
            await undo.UndoLatestAsync(ct).ConfigureAwait(true);
            return Done;
        });

    /// <summary>A folder right from the server's <c>myRights</c> (Mailbox/get); a folder that names none allows everything (J-048).</summary>
    public static bool HasRight(IMailboxDirectory dir, string mailboxId, string right)
    {
        ArgumentNullException.ThrowIfNull(dir);
        var m = dir.Tree.All.FirstOrDefault(x => string.Equals(x.Id, mailboxId, StringComparison.Ordinal));
        return m?.Extra is not null && m.Extra.TryGetValue("myRights", out var rights) && rights.ValueKind == JsonValueKind.Object
            ? !rights.TryGetProperty(right, out var v) || v.ValueKind != JsonValueKind.False
            : true;
    }

    private static string MailboxRoleName(MailboxRole role) => role.ToString().ToLowerInvariant();
}
