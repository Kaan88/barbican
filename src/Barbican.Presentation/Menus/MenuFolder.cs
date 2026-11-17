// Mirrors: Rust ctxmenu.rs submenu "move" :575 (every folder except virtual ones and the current one, indented by depth); Bulwark mailbox-context-menu.tsx. Owner menus.

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
    /// <summary>Virtual folders (<c>__scheduled</c>, ...) are not mailboxes: no menu, never a move target.</summary>
    public bool IsVirtual => Id.StartsWith("__", StringComparison.Ordinal);
}
