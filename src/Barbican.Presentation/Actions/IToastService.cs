// Mirrors: Bulwark lib/email-action-toast.ts, lib/toast-duration.ts; M2 plan section 4.3 (a toast host overlay, in-window, PopIn, RaiseNotificationEvent for Narrator). Contract from stage F of M2; owner actions.

namespace Barbican.Presentation.Actions;

/// <summary>Shows toasts in the window.</summary>
public interface IToastService
{
    ToastHandle Show(ToastRequest toast);

    void Dismiss(ToastHandle handle);
}
