// Mirrors: Bulwark lib/plugin-hook-catalog.ts:110 onComposeSend (ComposeSendRequest, takeover). The bus has no takeover kind: a handler that sent the message itself returns false (decision D-M3A-02). Owner send-pipeline.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The request at the start of sending: the message and the hold; a handler that sends it itself returns false.</summary>
public sealed record ComposeSendRequestPayload(OutgoingEmailPayload Email, int UndoSeconds, DateTimeOffset? ScheduledAt);
