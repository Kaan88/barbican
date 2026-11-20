// Host-mediated send hooks and final issue fence for a single outgoing message.
using Barbican.Core.Plugins.Payloads;

namespace Barbican.Core.Compose;

/// <summary>Holds the live add-on authority used by one send attempt.</summary>
public interface IOutgoingAddOnSend
{
    Task<bool> CheckAsync(OutgoingEmailPayload outgoing, CancellationToken ct);
    Task<OutgoingEmailPayload> TransformAsync(OutgoingEmailPayload outgoing, CancellationToken ct);

    /// <summary>Starts an operation synchronously under its authority; the operation is awaited after this returns.</summary>
    Task<bool> TryIssueAsync(Func<bool> issue, CancellationToken ct);
}
