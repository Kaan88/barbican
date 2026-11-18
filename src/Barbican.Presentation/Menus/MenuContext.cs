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
    /// <summary>M4 (additive, cal-views): what a calendar menu (MENU-EVENT, MENU-SLOT, MENU-TASK, MENU-CAL) was opened on.</summary>
    public Barbican.Presentation.Calendar.Grid.CalendarMenuTarget? Calendar { get; init; }
}
