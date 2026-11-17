// Mirrors: M3 plan section 4.3 (SendCoordinator.RunAsync(ComposeDraft, SendMode)); composer-ui's IComposerSender seam implemented over send-pipeline's coordinator. The composer asks its one warnings question itself (so the coordinator,
// which asks only when somebody subscribes to WarningsRaised, goes straight on) and leaves the toasts (undo with Undo and Send now, scheduled with Cancel) to the coordinator. Owner composer-ui.

using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Jmap.Errors;
using Barbican.Presentation.Compose.Pipeline;

namespace Barbican.Presentation.Compose;

/// <summary>Sends through the send coordinator.</summary>
public sealed class CoordinatorComposerSender(Lazy<SendCoordinator> coordinator, IScheduledSends scheduled, IMailSender sender) : IComposerSender
{
    public bool ShowsToasts => true;

    public long MaxDelayedSeconds(string accountKey) => scheduled.MaxDelayedSend(accountKey);

    public async Task<Result<SendReceipt>> SendAsync(ComposeDraft draft, SendOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(options);
        var outcome = await coordinator.Value.RunAsync(draft, options.ScheduledAt is { } at ? SendMode.At(at) : SendMode.Now, ct).ConfigureAwait(true);
        return outcome.IsAccepted && outcome.Receipt is { } receipt
            ? Result<SendReceipt>.Ok(receipt)
            : Result<SendReceipt>.Fail(new ValidationError(outcome.Message ?? "The message was not sent."));
    }

    public Task<Result<ComposeSeed>> UndoAsync(SendReceipt receipt, CancellationToken ct) => sender.UndoAsync(receipt, ct);
}
