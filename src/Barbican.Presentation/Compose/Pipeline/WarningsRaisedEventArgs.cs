// Mirrors: Bulwark components/email/send-warnings-dialog.tsx (one dialog lists every warning), M3 plan section 9 item 5; replaces Rust's "press Send again". Owner send-pipeline.

using Barbican.Core.Compose;

namespace Barbican.Presentation.Compose.Pipeline;

/// <summary>The send flow found things to warn about and waits for <see cref="SendCoordinator.Confirm"/> or <see cref="SendCoordinator.Cancel"/>.</summary>
public sealed class WarningsRaisedEventArgs(IReadOnlyList<SendWarning> warnings, SendMode mode) : EventArgs
{
    public IReadOnlyList<SendWarning> Warnings { get; } = warnings;

    /// <summary>Whether the user is sending now or scheduling (the dialog's confirm button says "Send anyway" or "Schedule anyway").</summary>
    public SendMode Mode { get; } = mode;
}
