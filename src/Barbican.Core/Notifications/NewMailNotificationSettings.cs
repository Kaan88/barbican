// Mirrors: Bulwark stores/settings-store.ts emailNotificationsEnabled, emailNotificationSound, notificationSoundChoice (default involved), pushNotifyInboxOnly, doNotDisturb, faviconUnreadBadge, unreadBadgeAllAccounts; lib/desktop/mail-notifications.ts:180 (MAIL_BATCH_MS 3000). Owner notifications.

using Barbican.Core.Storage;

namespace Barbican.Core.Notifications;

/// <summary>The settings that decide whether and how new mail is announced.</summary>
public sealed record NewMailNotificationSettings
{
    /// <summary>Mail that arrives within this time is told about once (mail-notifications.ts:180).</summary>
    public static readonly TimeSpan Batch = TimeSpan.FromSeconds(3);

    public bool Enabled { get; init; } = true;

    public bool Sound { get; init; } = true;

    public string SoundChoice { get; init; } = "involved";

    public bool InboxOnly { get; init; }

    public bool DoNotDisturb { get; init; }

    public bool BadgeEnabled { get; init; } = true;

    public bool BadgeAllAccounts { get; init; } = true;

    public static NewMailNotificationSettings From(PrefsSnapshot prefs)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        return new NewMailNotificationSettings
        {
            Enabled = prefs.Bool("emailNotificationsEnabled", true),
            Sound = prefs.Bool("emailNotificationSound", true),
            SoundChoice = prefs.Str("notificationSoundChoice", "involved"),
            InboxOnly = prefs.Bool("pushNotifyInboxOnly", false),
            DoNotDisturb = prefs.Bool("doNotDisturb", false),
            BadgeEnabled = prefs.Bool("faviconUnreadBadge", true),
            BadgeAllAccounts = prefs.Bool("unreadBadgeAllAccounts", true),
        };
    }
}
