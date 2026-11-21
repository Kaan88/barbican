// Mirrors: Bulwark lib/desktop/types.ts DesktopOsNotification, hooks/use-app-badge.ts:34 (the taskbar badge is the inbox unread count, 0 clears it), lib/notification-sound.ts:8 (the five tones: default, cheerful, involved, swift, relax). Owner notifications.

namespace Barbican.Core.Notifications;

/// <summary>What the operating system shows for the app: notifications, the taskbar badge and the notification tone.</summary>
public interface IDesktopNotifier
{
    /// <summary>Shows a notification (and plays the tone when the notice says so).</summary>
    void Show(NewMailNotice notice);

    /// <summary>Sets the number on the taskbar icon; 0 clears it.</summary>
    void SetBadge(int count);

    /// <summary>Plays the tone (the Settings test button, a reminder).</summary>
    void PlaySound(string choice);

    /// <summary>The user clicked a notification (raised on any thread).</summary>
    event EventHandler<NoticeActivatedEventArgs>? Activated;
}
