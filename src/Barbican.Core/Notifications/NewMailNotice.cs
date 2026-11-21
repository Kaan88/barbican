// Mirrors: Bulwark lib/desktop/mail-notifications.ts:117-163 buildMailNotification (one message: sender as the title, subject as the body; several: "N new messages" with the newest as body; content hidden: only "New mail"). Owner notifications.

namespace Barbican.Core.Notifications;

/// <summary>What the user is told when new mail arrives.</summary>
/// <param name="AccountKey">The account the mail is in.</param>
/// <param name="Title">The notification title.</param>
/// <param name="Body">The notification text (empty when the content is hidden).</param>
/// <param name="Count">How many messages it stands for.</param>
/// <param name="EmailId">The message a click opens (null for several messages: a click opens the Inbox, mail-notifications.ts:152).</param>
/// <param name="ThreadId">Its conversation.</param>
/// <param name="PlaySound">Whether the new-mail sound plays (emailNotificationSound).</param>
/// <param name="SoundChoice">The tone chosen in Settings (notificationSoundChoice).</param>
public sealed record NewMailNotice(string AccountKey, string Title, string Body, int Count, string? EmailId, string? ThreadId, bool PlaySound, string SoundChoice);
