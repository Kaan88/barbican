// Mirrors: Bulwark lib/desktop/new-mail.ts:37-48 notifyingMailbox (a seen or draft message is silent; mail older than a day is an import, not news; the Inbox only, or every folder but junk, trash, sent, drafts and archive; mail in a muted folder is silent unless it is also in an unmuted one),
// :81-93 (a message is told once: the last 500 are remembered), lib/desktop/mail-notifications.ts:117-163 (the notice), :180-200 (do not disturb and "Email notifications" off: nothing). Owner notifications.

using System.Globalization;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Notifications;

/// <summary>Decides which new mail is announced, and how.</summary>
public sealed class NewMailNotifier(TimeProvider time)
{
    private const int SeenLimit = 500;
    private static readonly TimeSpan NewestAge = TimeSpan.FromHours(24);
    private static readonly string[] NotArrivals = ["junk", "trash", "sent", "drafts", "archive"];
    private readonly Lock _lock = new();
    private readonly List<string> _told = [];
    private readonly HashSet<string> _toldSet = new(StringComparer.Ordinal);

    /// <summary>The notice for a batch of new messages of one account, or null when nothing is announced.</summary>
    /// <param name="accountKey">The account.</param>
    /// <param name="mail">The new messages.</param>
    /// <param name="roleIds">The account's role to folder id map (inbox, junk, ...).</param>
    /// <param name="mutedFolders">The folder ids with notifications muted.</param>
    /// <param name="settings">The settings now.</param>
    /// <param name="hideContent">True when sender and subject stay off the notification.</param>
    public NewMailNotice? Decide(string accountKey, IReadOnlyList<EmailSummary> mail, IReadOnlyDictionary<string, string> roleIds, IReadOnlySet<string> mutedFolders, NewMailNotificationSettings settings, bool hideContent = false)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentNullException.ThrowIfNull(mail);
        ArgumentNullException.ThrowIfNull(roleIds);
        ArgumentNullException.ThrowIfNull(mutedFolders);
        ArgumentNullException.ThrowIfNull(settings);
        var fresh = new List<EmailSummary>();
        foreach (var email in mail)
        {
            // told once, even when it is not announced (a muted message does not come back as news when it is moved)
            if (Tell(accountKey, email.Id) && Counts(email, roleIds, mutedFolders, settings))
            {
                fresh.Add(email);
            }
        }

        if (fresh.Count == 0 || !settings.Enabled || settings.DoNotDisturb)
        {
            return null;
        }

        var newest = fresh.OrderByDescending(e => e.ReceivedAt ?? DateTimeOffset.MinValue).First();
        var from = newest.From.Count > 0 ? newest.From[0] : null;
        var sender = !string.IsNullOrEmpty(from?.Name) ? from.Name : from?.Email ?? string.Empty;
        var subject = string.IsNullOrEmpty(newest.Subject) ? "(no subject)" : newest.Subject;
        if (fresh.Count > 1)
        {
            var title = string.Create(CultureInfo.InvariantCulture, $"{fresh.Count} new messages");
            return new NewMailNotice(accountKey, title, hideContent ? string.Empty : $"{sender}: {subject}", fresh.Count, null, null, settings.Sound, settings.SoundChoice);
        }

        return hideContent
            ? new NewMailNotice(accountKey, "New mail", string.Empty, 1, newest.Id, newest.ThreadId, settings.Sound, settings.SoundChoice)
            : new NewMailNotice(accountKey, sender.Length > 0 ? sender : "New mail", string.IsNullOrEmpty(newest.Subject) ? (string.IsNullOrEmpty(newest.Preview) ? subject : newest.Preview) : newest.Subject, 1, newest.Id, newest.ThreadId, settings.Sound, settings.SoundChoice);
    }

    private bool Counts(EmailSummary email, IReadOnlyDictionary<string, string> roleIds, IReadOnlySet<string> muted, NewMailNotificationSettings settings)
    {
        if (email.Keywords.Seen || email.Keywords.IsSet("$draft"))
        {
            return false;
        }

        if (email.ReceivedAt is { } at && time.GetUtcNow() - at > NewestAge)
        {
            return false;
        }

        var here = email.MailboxIds.Where(kv => kv.Value).Select(kv => kv.Key);
        var counting = settings.InboxOnly
            ? here.Where(id => roleIds.TryGetValue("inbox", out var inbox) && string.Equals(id, inbox, StringComparison.Ordinal))
            : here.Where(id => !NotArrivals.Any(role => roleIds.TryGetValue(role, out var skip) && string.Equals(id, skip, StringComparison.Ordinal)));
        // filed in a muted folder and an unmuted one still notifies (new-mail.ts:46)
        return counting.Any(id => !muted.Contains(id));
    }

    private bool Tell(string accountKey, string id)
    {
        var key = accountKey + "/" + id;
        lock (_lock)
        {
            if (!_toldSet.Add(key))
            {
                return false;
            }

            _told.Add(key);
            while (_told.Count > SeenLimit)
            {
                _toldSet.Remove(_told[0]);
                _told.RemoveAt(0);
            }

            return true;
        }
    }
}
