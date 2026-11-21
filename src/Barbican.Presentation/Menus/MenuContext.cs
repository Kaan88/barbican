// Mirrors: M2 plan section 4.2 (MenuContext). Contract from stage F of M2; owner menus.

using Barbican.Core.Mail.Model;
using Barbican.Presentation.Commands;

namespace Barbican.Presentation.Menus;

/// <summary>What a menu is opened for.</summary>
public sealed record MenuContext(
    string MenuId,
    IReadOnlyList<MailTarget> Targets,
    MailboxRef? Mailbox,
    string? TagId,
    string? Address,
    int? AttachmentIndex,
    CommandSurface Surface)
{
    public bool IncludeReaderOverflow { get; init; }

    /// <summary>RD-08: the text selected in the message body (MENU-SELECTION); <see langword="null"/> for every other menu.</summary>
    public string? SelectionText { get; init; }

    /// <summary>M4 (additive, cal-views): what a calendar menu (MENU-EVENT, MENU-SLOT, MENU-TASK, MENU-CAL) was opened on.</summary>
    public Barbican.Presentation.Calendar.Grid.CalendarMenuTarget? Calendar { get; init; }

    /// <summary>M5 (additive, plan commit, frozen): what a contacts menu (MENU-CONTACT, MENU-GROUP, MENU-CATEGORY, MENU-BOOK) was opened on. Owner con-ui.</summary>
    public Barbican.Presentation.Contacts.ContactMenuTarget? Contact { get; init; }

    /// <summary>M5 (additive, plan commit, frozen): what a Files menu (MENU-FILE, MENU-FILE-TRASH, MENU-FILE-FAV, MENU-FILE-RECENT, MENU-FILES-BG) was opened on. Owner files-ui.</summary>
    public Barbican.Presentation.Files.FileMenuTarget? File { get; init; }
}
