// Mirrors: Rust palette.rs run_command :485 (go to an app, theme, shortcuts, sign out, switch account) and main.rs go-to-folder keys; Bulwark lib/app-commands.ts. The commands of the
// shell that the menus stream owns (M2 plan 4.1: "shell, palette, shortcuts"); account work is unified's IAccountMaintenance when it is registered. Owner menus.

using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Model;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Palette;
using Barbican.Presentation.Shell;

namespace Barbican.Presentation.Commands;

/// <summary>Creates the shell commands. The palette is taken lazily: it reads the registry that holds these commands.</summary>
public static class ShellCommands
{
    /// <summary>How many commands <see cref="Create"/> returns (the registration adds one <see cref="IAppCommand"/> per index).</summary>
    public const int Count = 18;

    public static IReadOnlyList<IAppCommand> Create(
        ShellViewModel shell,
        SidebarViewModel sidebar,
        MessageListViewModel list,
        Lazy<PaletteController> palette,
        ShortcutSheetController sheet,
        IAccountManager? accounts,
        IAccountMaintenance? maintenance)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(sidebar);
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(sheet);
        var commands = new List<IAppCommand>
        {
            DelegateAppCommand.Sync(CommandIds.OpenPalette, ctx => { _ = palette.Value.OpenAsync(); }),
            DelegateAppCommand.Sync(CommandIds.ShowShortcuts, _ => sheet.Open(TabOf(shell.ActiveApp), shell.Accessibility.SingleKeys)),
            DelegateAppCommand.Sync(CommandIds.ToggleTheme, _ => shell.PreviewLook(shell.Style, !shell.Dark)),
            DelegateAppCommand.Sync(CommandIds.FocusSearch, _ => list.RequestSearchFocus()),
            App(CommandIds.GoMail, AppId.Mail, shell),
            App(CommandIds.GoCalendar, AppId.Calendar, shell),
            App(CommandIds.GoContacts, AppId.Contacts, shell),
            App(CommandIds.GoFiles, AppId.Files, shell),
            App(CommandIds.GoSettings, AppId.Settings, shell),
            Folder(CommandIds.GoInbox, MailboxRole.Inbox, sidebar),
            Folder(CommandIds.GoStarred, MailboxRole.Flagged, sidebar),
            Folder(CommandIds.GoSent, MailboxRole.Sent, sidebar),
            Folder(CommandIds.GoDrafts, MailboxRole.Drafts, sidebar),
            Folder(CommandIds.GoAll, MailboxRole.All, sidebar),
        };
        // account commands always exist; they can run only when the account services are registered (unified's IAccountMaintenance arrives in wave 2)
        commands.Add(new DelegateAppCommand(
            CommandIds.SwitchAccount,
            c => accounts is not null && c.Argument is { Length: > 0 } key && accounts.Accounts.Any(a => string.Equals(a.Key, key, StringComparison.Ordinal)),
            (c, ct) => accounts!.SwitchToAsync(c.Argument!, ct)));
        commands.Add(new DelegateAppCommand(
            CommandIds.SignOut,
            _ => maintenance is not null && accounts is not null,
            (c, ct) => maintenance!.SignOutAsync(c.Argument ?? accounts!.Active?.Key ?? string.Empty, ct)));
        commands.Add(new DelegateAppCommand(CommandIds.SignOutAll, _ => maintenance is not null, (_, ct) => maintenance!.SignOutAllAsync(ct)));
        commands.Add(new DelegateAppCommand(
            CommandIds.SetDefaultAccount,
            c => maintenance is not null && !string.IsNullOrEmpty(c.Argument),
            (c, ct) => maintenance!.SetDefaultAsync(c.Argument!, ct)));

        return commands;
    }

    private static int TabOf(AppId app) => app switch { AppId.Calendar => 1, AppId.Contacts => 2, AppId.Files => 3, _ => 0 };

    private static DelegateAppCommand App(string id, AppId app, ShellViewModel shell) =>
        new(id, _ => shell.AvailableApps.Contains(app), (_, _) => Task.FromResult(shell.TryActivate(app) ? Result<Unit>.Ok(default) : Result<Unit>.Fail(new ValidationError("That app is not available yet."))));

    private static DelegateAppCommand Folder(string id, MailboxRole role, SidebarViewModel sidebar) =>
        DelegateAppCommand.Sync(id, ctx => { _ = sidebar.GoToRole(role); });
}
