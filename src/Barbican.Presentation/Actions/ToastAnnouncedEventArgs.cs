// Mirrors: Bulwark lib/announce.ts (a toast is also announced to screen readers); Windows UIA RaiseNotificationEvent. Owner actions.

namespace Barbican.Presentation.Actions;

/// <summary>A toast was shown: the host overlay raises a Narrator notification with its message.</summary>
public sealed class ToastAnnouncedEventArgs(ToastItem toast) : EventArgs
{
    /// <summary>The toast that was shown.</summary>
    public ToastItem Toast { get; } = toast;
}
