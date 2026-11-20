using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.RawSend;

/// <summary>Validated input; the host must authorize the account before reading the binary value.</summary>
public sealed record PluginRawSendRequest(PluginValue Bytes, string IdentityId, IReadOnlyList<string>? EnvelopeRecipients,
    DateTimeOffset? ScheduledAt, string? LabelToken, bool SubmitOnly);
