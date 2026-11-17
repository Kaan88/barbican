// Mirrors: see IComposerSender. The default: IMailSender as it is (send-pipeline's pipeline runs the plugin send hooks inside it); Stalwart advertises 30 days but the plan clamps the hold to 7 (D-7 of M3 plan section 9).

using Barbican.Core.Common;
using Barbican.Core.Compose;

namespace Barbican.Presentation.Compose;

/// <summary>Sends through <see cref="IMailSender"/>.</summary>
public sealed class DirectComposerSender(IMailSender sender) : IComposerSender
{
    /// <summary>The hold the plan allows when the account does not say (7 days).</summary>
    public const long DefaultMaxDelay = 7 * 24 * 3600;

    public long MaxDelayedSeconds(string accountKey) => DefaultMaxDelay;

    public Task<Result<SendReceipt>> SendAsync(ComposeDraft draft, SendOptions options, CancellationToken ct) => sender.SendAsync(draft, options, ct);

    public Task<Result<ComposeSeed>> UndoAsync(SendReceipt receipt, CancellationToken ct) => sender.UndoAsync(receipt, ct);
}
