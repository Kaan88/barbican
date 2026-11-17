// Mirrors: Rust mail.rs (forced :960, resolve_toggle :965, badge_title :203, archive_path :1064, unread_section_size :591). Stub from F2; owner mailcore.

using Barbican.Core.Common;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Rules;

/// <summary>Small pure rules of the mail actions and the list.</summary>
public static class MailRules
{
    /// <summary><c>read!</c> and <c>unread!</c> name one state (Ctrl+Q, Shift+I, u): the state, else null.</summary>
    public static string? Forced(string action) => throw new PortPendingException("MailRules.Forced: mailcore stream");

    /// <summary>The <c>star</c> and <c>read</c> toggles resolved from the first selected message's state; a forced action never flips.</summary>
    public static string ResolveToggle(IReadOnlyList<string> ids, string action, Func<string, EmailSummary?> lookup) =>
        throw new PortPendingException("MailRules.ResolveToggle: mailcore stream");

    /// <summary>The window title with the unread count when the badge is on.</summary>
    public static string BadgeTitle(bool enabled, long unread) => throw new PortPendingException("MailRules.BadgeTitle: mailcore stream");

    /// <summary>The year/month folders an archived message goes under (local time of the message); empty for another mode or a bad date.</summary>
    public static IReadOnlyList<string> ArchivePath(string receivedAt, string mode) => throw new PortPendingException("MailRules.ArchivePath: mailcore stream");

    /// <summary>The unread section's size: one of 5, 10, 15, 20, 25, 50, else 25.</summary>
    public static int UnreadSectionSize(long value) => throw new PortPendingException("MailRules.UnreadSectionSize: mailcore stream");
}
