// Mirrors: Bulwark components/layout/mailbox-context-menu.tsx:309-336 (unreadEmails, totalEmails and the tree decide which rows are disabled); Rust ctxmenu.rs submenu "move" :575 (every folder except virtual ones and the current one, indented by depth); Bulwark mailbox-context-menu.tsx. Owner menus.

using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Menus;

/// <summary>A folder as a menu needs it.</summary>
/// <param name="Id">The mailbox id.</param>
/// <param name="Name">The name shown.</param>
/// <param name="Role">The mailbox role.</param>
/// <param name="Depth">Nesting level.</param>
/// <param name="Rights">What the user may do in it.</param>
public sealed record MenuFolder(string Id, string Name, MailboxRole Role, int Depth, MailboxRights Rights)
{
    /// <summary>Unread messages; -1 when unknown (the item then stays on, as in a menu opened before the counts arrived).</summary>
    public int Unread { get; init; } = -1;

    /// <summary>Messages in the folder; -1 when unknown.</summary>
    public int Total { get; init; } = -1;

    /// <summary>Virtual folders (<c>__scheduled</c>, ...) are not mailboxes: no menu, never a move target.</summary>
    public bool IsVirtual => Id.StartsWith("__", StringComparison.Ordinal);
}
