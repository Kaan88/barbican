// Mirrors Bulwark components/email/email-composer.tsx onComposeSend and lib/outgoing/compose-takeover.ts.
using Barbican.Core.Plugins.Api.ComposeSend;

namespace Barbican.Core.Compose;

/// <summary>A host-owned send attempt. Only Payload is projected into the add-on frame.</summary>
public sealed record ComposerTakeoverRequest(string ComposerId, string Revision, PluginComposeSendRequest Payload,
    IReadOnlyList<string> EnvelopeRecipients, SendOptions Options, ComposerClassificationDecision? Classification, string? FromSubAddressDelimiter);
