// Mirrors: Rust palette.rs run_command :485 (what each palette id does), mapped onto the registry's CommandIds (M2 plan 4.1). Owner menus.

using Barbican.Presentation.Calendar.Commands;
using Barbican.Presentation.Commands;

namespace Barbican.Presentation.Palette;

/// <summary>Which registry command a palette row runs, and with what argument.</summary>
public static class PaletteCommandMap
{
    // M5 con-ui: the Contacts rows of the palette run the contacts commands
    private static string? ContactsCommand(string id) => id switch
    {
        "ct:new" => Contacts.ContactsCommandIds.New,
        "ct:group" => Contacts.ContactsCommandIds.NewGroup,
        "ct:import" => Contacts.ContactsCommandIds.Import,
        "ct:export" => Contacts.ContactsCommandIds.Export,
        "ct:merge" => Contacts.ContactsCommandIds.Merge,
        _ => null,
    };

    // a global search hit of a kind the app can open (a plugin's hit has no place to go)
    private static bool IsOpenableHit(string id) =>
        id.StartsWith("search-hit:mail:", StringComparison.Ordinal) || id.StartsWith("search-hit:contact:", StringComparison.Ordinal)
        || id.StartsWith("search-hit:event:", StringComparison.Ordinal) || id.StartsWith("search-hit:file:", StringComparison.Ordinal);

    /// <summary>The command id and argument of a palette row; the id is null for a row nothing handles (calendar, contacts, files rows until their milestones).</summary>
    public static (string? CommandId, string? Argument) Resolve(PaletteCommand command, PaletteContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        var id = command.Id;
        if (id.StartsWith("account:", StringComparison.Ordinal))
        {
            return (CommandIds.SwitchAccount, id["account:".Length..]);
        }

        if (IsOpenableHit(id))
        {
            return (CommandIds.OpenSearchHit, id["search-hit:".Length..]);
        }

        if (id.StartsWith("settings:", StringComparison.Ordinal))
        {
            return (CommandIds.GoSettings, id["settings:".Length..]);
        }

        var mapped = id switch
        {
            "mail:reply" => CommandIds.Reply,
            "mail:reply-all" => CommandIds.ReplyAll,
            "mail:forward" => CommandIds.Forward,
            "mail:archive" => CommandIds.Archive,
            "mail:delete" => CommandIds.Delete,
            "mail:read" => CommandIds.MarkRead,
            "mail:unread" => CommandIds.MarkUnread,
            "mail:star" => context.MailFlagged ? CommandIds.Unstar : CommandIds.Star,
            "mail:spam" => context.InJunk ? CommandIds.NotSpam : CommandIds.ReportSpam,
            "cal:new" => CalendarCommandIds.NewEvent,
            "cal:task" => CalendarCommandIds.NewTask,
            "cal:search" => CalendarCommandIds.Search,
            "cal:today" => CalendarCommandIds.Today,
            "cal:view0" => CalendarCommandIds.ViewDay,
            "cal:view1" => CalendarCommandIds.ViewWeek,
            "cal:view2" => CalendarCommandIds.ViewMonth,
            "cal:view3" => CalendarCommandIds.ViewAgenda,
            "cal:view4" => CalendarCommandIds.ViewTasks,
            "cal:import" => CalendarCommandIds.Import,
            "cal:subscribe" => CalendarCommandIds.Subscribe,
            "cal:print" => CalendarCommandIds.Print,
            "cal:refresh" => CalendarCommandIds.Refresh,
            "go:mail" => CommandIds.GoMail,
            "go:calendar" => CommandIds.GoCalendar,
            "go:contacts" => CommandIds.GoContacts,
            "go:files" => CommandIds.GoFiles,
            "go:settings" => CommandIds.GoSettings,
            "go:admin" => CommandIds.GoAdmin,
            "go:inbox" => CommandIds.GoInbox,
            "compose" => CommandIds.Compose,
            "search" => CommandIds.FocusSearch,
            "search-everything" => CommandIds.GlobalSearch,
            "theme" => CommandIds.ToggleTheme,
            "shortcuts" => CommandIds.ShowShortcuts,
            "a11y" => CommandIds.OpenA11y,
            "sign-out" => CommandIds.SignOut,
            _ => null,
        };
        return (mapped ?? ContactsCommand(id), null);
    }
}
