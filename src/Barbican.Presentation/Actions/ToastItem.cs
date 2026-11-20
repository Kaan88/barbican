// Mirrors: Bulwark stores/toast-store.ts (a toast: message, optional action, error flag), lib/toast-duration.ts. Owner actions.

namespace Barbican.Presentation.Actions;

/// <summary>One toast on screen.</summary>
/// <param name="Handle">What <see cref="IToastService.Dismiss"/> takes.</param>
/// <param name="Request">What was asked for.</param>
public sealed record ToastItem(ToastHandle Handle, ToastRequest Request)
{
    /// <summary>The text the toast shows (and Narrator announces).</summary>
    public string Message => Request.Message;

    /// <summary>The optional host-supplied notification source.</summary>
    public string? Source => Request.Source;

    /// <summary>The host-framed source, or null for an unattributed notification.</summary>
    public string? DisplaySource => string.IsNullOrWhiteSpace(Source) ? null :
        string.IsNullOrWhiteSpace(Request.SourceId) || string.Equals(Source, Request.SourceId, StringComparison.Ordinal)
            ? "Add-on: " + Source : "Add-on: " + Source + " (" + Request.SourceId + ")";

    /// <summary>Whether this toast represents ongoing work.</summary>
    public bool IsProgress => Request.IsProgress;

    /// <summary>The complete accessible name, including attribution and progress state.</summary>
    public string AccessibleName => (DisplaySource is { } source ? source + ": " + Message : Message) + (IsProgress ? ", in progress" : string.Empty);

    /// <summary>The action button's label (Undo), or null.</summary>
    public string? ActionLabel => Request.ActionLabel;

    /// <summary>The second action button's label (Send now), or null (M3, additive).</summary>
    public string? SecondaryLabel => Request.SecondaryLabel;

    /// <summary>An error toast (stays longer, announced as an alert).</summary>
    public bool IsError => Request.EffectiveSeverity == ToastSeverity.Error;

    /// <summary>The notification severity.</summary>
    public ToastSeverity Severity => Request.EffectiveSeverity;
}
