// Owner notifications. The user clicked a notification.

namespace Barbican.Core.Notifications;

/// <summary>A click on a notification.</summary>
public sealed class NoticeActivatedEventArgs(NewMailNotice notice) : EventArgs
{
    public NewMailNotice Notice { get; } = notice;
}
