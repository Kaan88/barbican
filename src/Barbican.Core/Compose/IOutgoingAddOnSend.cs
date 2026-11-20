// Host-mediated send hooks and final issue fence for a single outgoing message.
using Barbican.Core.Common;
using Barbican.Core.Plugins.Payloads;

namespace Barbican.Core.Compose;

/// <summary>Holds the live add-on authority used by one send attempt.</summary>
public interface IOutgoingAddOnSend
{
    Task<bool> CheckAsync(OutgoingEmailPayload outgoing, CancellationToken ct);
    Task<OutgoingEmailPayload> TransformAsync(OutgoingEmailPayload outgoing, CancellationToken ct);

    /// <summary>An accepted raw receipt finishes the send. A successful null result permits the standard path.</summary>
    Task<Result<RawMessageReceipt?>> TakeOverAsync(ComposerTakeoverRequest request, CancellationToken ct) =>
        Task.FromResult(Result<RawMessageReceipt?>.Ok(null));

    /// <summary>Reports a completed send without affecting its result.</summary>
    void NotifySent(EmailSentPayload payload) { }

    /// <summary>Starts an operation synchronously under its authority; the operation is awaited after this returns.</summary>
    Task<bool> TryIssueAsync(Func<bool> issue, CancellationToken ct);

    /// <summary>Orders the classification guard before entering the final account and draft fence.</summary>
    Task<bool> TryIssueAsync(Func<Func<bool>, bool> guard, Func<bool> issue, CancellationToken ct) =>
        TryIssueAsync(() => guard(issue), ct);
}
