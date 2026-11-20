// Mirrors: Bulwark lib/email-action-toast.ts, lib/selection-toast.ts, lib/toast-duration.ts; M2 plan section 4.3. Contract from stage F of M2; owner actions.

namespace Barbican.Presentation.Actions;

/// <summary>A toast to show: the text, whether it is an error, and an optional action button (Undo).</summary>
public sealed record ToastRequest(string Message, bool IsError = false, string? ActionLabel = null, Func<CancellationToken, Task>? Action = null, TimeSpan? Duration = null)
{
    /// <summary>Optional explicit severity; existing error requests retain their behavior.</summary>
    public ToastSeverity? Severity { get; init; }

    /// <summary>The requested severity or the legacy error flag.</summary>
    public ToastSeverity EffectiveSeverity => Severity ?? (IsError ? ToastSeverity.Error : ToastSeverity.Info);

    /// <summary>Stable host-supplied source identity, used only for notification replacement.</summary>
    public string? SourceId { get; init; }

    /// <summary>Optional host-supplied source shown with the notification.</summary>
    public string? Source { get; init; }

    /// <summary>A second action button (M3, additive: the undo-send toast has Undo and Send now).</summary>
    public string? SecondaryLabel { get; init; }

    /// <summary>What the second button does.</summary>
    public Func<CancellationToken, Task>? SecondaryAction { get; init; }
}
