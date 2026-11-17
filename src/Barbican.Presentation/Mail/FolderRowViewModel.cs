// Mirrors: Rust mail.rs rebuild_folders :115 (Folder struct: id, name, icon, depth, unread, total, color, count, chevron, muted); spec I-038..I-046. Owner mailcore.

using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Mail;

/// <summary>One folder row of the sidebar. A record, so ListSync leaves an unchanged row alone and sets a changed one in place.</summary>
/// <param name="Id">The mailbox id.</param>
/// <param name="Name">The role's name (Inbox, Sent, Spam, ...) or the server name of a user folder.</param>
/// <param name="Role">The mailbox role.</param>
/// <param name="Glyph">The Segoe Fluent icon character of the role.</param>
/// <param name="Depth">Nesting level.</param>
/// <param name="Unread">Unread messages in the folder.</param>
/// <param name="Total">Messages in the folder.</param>
/// <param name="ColorRgb">The icon colour 0xRRGGBB (grey unless "colorful icons" is on).</param>
/// <param name="Count">The count text: a number, "unread / total", a dot, or nothing.</param>
/// <param name="Chevron">0 no children, 1 open, 2 closed.</param>
/// <param name="Muted">The folder is not subscribed (shown muted).</param>
/// <param name="ChildUnread">A collapsed folder with unread messages somewhere below it (the sub-unread dot).</param>
public sealed record FolderRowViewModel(
    string Id,
    string Name,
    MailboxRole Role,
    string Glyph,
    int Depth,
    int Unread,
    int Total,
    uint ColorRgb,
    string Count,
    int Chevron,
    bool Muted,
    bool ChildUnread);
