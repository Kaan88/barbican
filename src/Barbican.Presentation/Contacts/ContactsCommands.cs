// Mirrors: M4 CalendarCommands (every command is a registered IAppCommand: menus, palette, keys, buttons and automation run them by id); Rust shortcuts.rs:204 contacts, :796 list_keys; M5 plan section 6.2 step 3.
// B-CON-031 (arrows move, Home and End jump, Enter opens, Delete and Backspace delete, Esc clears, Shift+F10 the menu, "/" searches), I-354, I-357. Owner con-ui.

using Barbican.Core.Common;
using Barbican.Presentation.Commands;

namespace Barbican.Presentation.Contacts;

/// <summary>Creates the commands of the Contacts app.</summary>
public static class ContactsCommands
{
    /// <summary>How many commands <see cref="Create"/> returns (the registration adds one <see cref="IAppCommand"/> per index).</summary>
    public const int Count = 41;

    private static readonly Result<Unit> Done = Result<Unit>.Ok(default);

    public static IReadOnlyList<IAppCommand> Create(ContactsActions a, IContactsViewModel vm, IContactsChrome chrome)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(chrome);
        bool Account(CommandContext _) => vm.AccountKey is not null;
        bool Some(CommandContext c) => vm.AccountKey is not null && a.IdsOf(c.Argument).Count > 0;
        bool WithEmail(CommandContext c) => a.IdsOf(c.Argument).Select(vm.CardOf).Any(card => card is not null && ContactsJson.Emails(card).Any());
        bool HasArg(CommandContext c) => !string.IsNullOrEmpty(c.Argument);
        bool Rows(CommandContext _) => vm.Rows.Count > 0;
        IAppCommand Cmd(string id, Func<CommandContext, bool> can, Func<CommandContext, CancellationToken, Task<Result<Unit>>> run) => new DelegateAppCommand(id, can, run);
        IAppCommand Sync(string id, Action<CommandContext> run, Func<CommandContext, bool>? can = null) => DelegateAppCommand.Sync(id, run, can);
        return
        [
            Cmd(ContactsCommandIds.New, Account, (c, ct) => a.NewContactAsync(c.Argument, ct)),
            Cmd(ContactsCommandIds.Edit, Some, (c, ct) => a.EditAsync(c.Argument, ct)),
            Cmd(ContactsCommandIds.Open, Some, (c, _) => a.OpenAsync(c.Argument)),
            Cmd(ContactsCommandIds.Delete, Some, (c, ct) => a.DeleteAsync(c.Argument, ct)),
            Cmd(ContactsCommandIds.SendEmail, WithEmail, (c, ct) => a.SendEmailAsync(c.Argument, ct)),
            Cmd(ContactsCommandIds.CopyEmail, WithEmail, (c, _) => a.CopyEmailAsync(c.Argument)),
            Cmd(ContactsCommandIds.GroupToggle, HasArg, (c, ct) => a.ToggleGroupAsync(c.Argument, ct)),
            Cmd(ContactsCommandIds.GroupNewWith, Account, (c, ct) => a.NewGroupWithAsync(c.Argument, ct)),
            Cmd(ContactsCommandIds.NewGroup, Account, (_, ct) => a.NewGroupAsync(ct)),
            Cmd(ContactsCommandIds.GroupEdit, HasArg, (c, ct) => a.EditGroupAsync(c.Argument, ct)),
            Cmd(ContactsCommandIds.GroupRename, HasArg, (c, ct) => a.RenameGroupAsync(c.Argument, ct)),
            Cmd(ContactsCommandIds.GroupDelete, HasArg, (c, ct) => a.DeleteGroupAsync(c.Argument, ct)),
            Cmd(ContactsCommandIds.GroupSend, HasArg, (c, ct) => a.SendGroupAsync(c.Argument, ct)),
            Cmd(ContactsCommandIds.CategoryToggle, HasArg, (c, ct) => a.ToggleCategoryAsync(c.Argument, ct)),
            Cmd(ContactsCommandIds.CategoryNew, Account, (c, ct) => a.NewCategoryAsync(c.Argument, ct)),
            Cmd(ContactsCommandIds.CategoryShow, HasArg, (c, _) => a.ShowPlaceAsync(ContactsPlace.Category, c.Argument)),
            Cmd(ContactsCommandIds.CategoryRename, HasArg, (c, ct) => a.RenameCategoryAsync(c.Argument, ct)),
            Cmd(ContactsCommandIds.CategoryDelete, HasArg, (c, ct) => a.DeleteCategoryAsync(c.Argument, ct)),
            Cmd(ContactsCommandIds.BookNew, Account, (_, ct) => a.NewBookAsync(ct)),
            Cmd(ContactsCommandIds.BookNewContact, HasArg, (c, ct) => a.NewContactAsync(c.Argument, ct)),
            Cmd(ContactsCommandIds.BookRename, HasArg, (c, ct) => a.RenameBookAsync(c.Argument, ct)),
            Cmd(ContactsCommandIds.BookDelete, HasArg, (c, ct) => a.DeleteBookAsync(c.Argument, ct)),
            Cmd(ContactsCommandIds.BookShow, HasArg, (c, _) => a.ShowPlaceAsync(ContactsPlace.Book, c.Argument)),
            Cmd(ContactsCommandIds.GroupShow, HasArg, (c, _) => a.ShowPlaceAsync(ContactsPlace.Group, c.Argument)),
            Cmd(ContactsCommandIds.ShowAll, Account, (_, _) => a.ShowPlaceAsync(ContactsPlace.All, null)),
            Cmd(ContactsCommandIds.ShowDuplicates, Account, (_, _) => a.ShowPlaceAsync(ContactsPlace.Duplicates, null)),
            Cmd(ContactsCommandIds.ShowDirectory, c => Account(c) && vm.DirectoryAvailable, (_, _) => a.ShowPlaceAsync(ContactsPlace.Directory, null)),
            Cmd(ContactsCommandIds.Import, Account, (_, ct) => a.ImportAsync(ct)),
            Cmd(ContactsCommandIds.Export, c => Account(c) && (HasArg(c) || Rows(c)), (c, ct) => a.ExportAsync(c.Argument, ct)),
            Cmd(ContactsCommandIds.Merge, c => Account(c) && vm.DuplicateSetCount > 0, (_, ct) => a.MergeAsync(ct)),
            Cmd(ContactsCommandIds.Print, c => Account(c) && (a.IdsOf(c.Argument).Count > 0 || Rows(c)), (c, ct) => a.PrintAsync(c.Argument, ct)),
            Cmd(CommandIds.ViewContact, HasArg, (c, ct) => a.ViewContactAsync(c.Argument, ct)),
            Cmd(CommandIds.AddToContacts, HasArg, (c, ct) => a.AddToContactsAsync(c.Argument, ct)),
            Cmd(ContactsCommandIds.Refresh, Account, async (_, ct) =>
            {
                await vm.RefreshAsync(ct).ConfigureAwait(true);
                return Done;
            }),
            Sync(ContactsCommandIds.KeyUp, _ => vm.Move(-1)),
            Sync(ContactsCommandIds.KeyDown, _ => vm.Move(1)),
            Sync(ContactsCommandIds.KeyFirst, _ => vm.Jump(false)),
            Sync(ContactsCommandIds.KeyLast, _ => vm.Jump(true)),
            Sync(ContactsCommandIds.KeyClear, _ => vm.Clear()),
            Sync(ContactsCommandIds.KeySearch, _ => chrome.FocusSearch()),
            Sync(ContactsCommandIds.KeyMenu, _ => chrome.ShowSelectionMenu(), c => vm.SelectedIds.Count > 0 || vm.OpenId is not null),
        ];
    }
}
