// Mirrors: Bulwark stores/toast-store.ts (a toast: message, optional action, error flag), lib/toast-duration.ts. Owner actions.

namespace Barbican.Presentation.Actions;

/// <summary>One toast on screen.</summary>
/// <param name="Handle">What <see cref="IToastService.Dismiss"/> takes.</param>
/// <param name="Request">What was asked for.</param>
public sealed record ToastItem(ToastHandle Handle, ToastRequest Request)
{
    /// <summary>The text the toast shows (and Narrator announces).</summary>
    public string Message => Request.Message;

    /// <summary>The action button's label (Undo), or null.</summary>
    public string? ActionLabel => Request.ActionLabel;

    /// <summary>An error toast (stays longer, announced as an alert).</summary>
    public bool IsError => Request.IsError;
}
