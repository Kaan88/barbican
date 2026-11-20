namespace Barbican.Presentation.Actions;

/// <summary>A toast removed by dismissal, expiry, replacement, or eviction.</summary>
public sealed class ToastDismissedEventArgs(ToastItem toast) : EventArgs
{
    /// <summary>The removed toast.</summary>
    public ToastItem Toast { get; } = toast;
}
