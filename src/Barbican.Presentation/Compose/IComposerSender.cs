// Mirrors: M3 plan section 4.3 (SendCoordinator: the order of a send and the undo window, owned by send-pipeline); Bulwark components/email/email-composer.tsx handleSend. Added by composer-ui (additive):
// the composer asks this seam, the default wraps IMailSender, and the integrator may register SendCoordinator's adapter instead without touching the composer.

using Barbican.Core.Common;
using Barbican.Core.Compose;

namespace Barbican.Presentation.Compose;

/// <summary>What the composer needs from the send pipeline: submit a prepared draft, undo a held submission, and the longest hold the account allows.</summary>
public interface IComposerSender
{
    /// <summary>The account's <c>maxDelayedSend</c> in seconds (0: no hold); bounds the schedule dialog.</summary>
    long MaxDelayedSeconds(string accountKey);

    /// <summary>The sender shows its own toasts (undo, scheduled); the composer then shows none.</summary>
    bool ShowsToasts => false;

    Task<Result<SendReceipt>> SendAsync(ComposeDraft draft, SendOptions options, CancellationToken ct);

    /// <summary>Cancels the held submission; the seed reopens the message in a composer.</summary>
    Task<Result<ComposeSeed>> UndoAsync(SendReceipt receipt, CancellationToken ct);
}
