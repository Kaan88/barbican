// Mirrors: Rust mail.rs (forced :960, resolve_toggle :965, badge_title :203, archive_path :1064, unread_section_size :591);
// Bulwark UNREAD_SECTION_SIZES, archiveEmails. Owner mailcore.

using System.Globalization;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Rules;

/// <summary>Small pure rules of the mail actions and the list.</summary>
public static class MailRules
{
    /// <summary><c>read!</c> and <c>unread!</c> name one state (Ctrl+Q, Shift+I, u): the state, else null.</summary>
    public static string? Forced(string action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return action switch { "read!" => "read", "unread!" => "unread", _ => null };
    }

    /// <summary>The <c>star</c> and <c>read</c> toggles resolved from the first selected message's state; a forced action never flips.</summary>
    public static string ResolveToggle(IReadOnlyList<string> ids, string action, Func<string, EmailSummary?> lookup)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(lookup);
        var first = ids.Count > 0 ? lookup(ids[0]) : null;
        if (string.Equals(action, "star", StringComparison.Ordinal) && first?.Keywords.Flagged == true)
        {
            return "unstar";
        }

        if (string.Equals(action, "read", StringComparison.Ordinal) && first?.Keywords.Seen == true)
        {
            return "unread";
        }

        // a key that names one state (Ctrl+Q / Shift+I mark as read, u marks unread) never flips
        return Forced(action) ?? action;
    }

    /// <summary>The window title with the unread count when the badge is on.</summary>
    public static string BadgeTitle(bool enabled, long unread) =>
        enabled && unread > 0 ? string.Create(CultureInfo.InvariantCulture, $"({unread}) Barbican") : "Barbican";

    /// <summary>The year/month folders an archived message goes under (local time of the message); empty for another mode or a bad date.</summary>
    public static IReadOnlyList<string> ArchivePath(string receivedAt, string mode)
    {
        ArgumentNullException.ThrowIfNull(receivedAt);
        ArgumentNullException.ThrowIfNull(mode);
        if (!DateTimeOffset.TryParse(receivedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
        {
            return [];
        }

        var local = at.ToLocalTime();
        var year = local.Year.ToString(CultureInfo.InvariantCulture);
        return mode switch
        {
            "year" => [year],
            "month" => [year, local.Month.ToString("00", CultureInfo.InvariantCulture)],
            _ => [],
        };
    }

    /// <summary>The unread section's size: one of 5, 10, 15, 20, 25, 50, else 25.</summary>
    public static int UnreadSectionSize(long value) => value is 5 or 10 or 15 or 20 or 25 or 50 ? (int)value : 25;
}
