using Barbican.Core.Compose;
using Barbican.Core.Plugins.Bridge;

namespace Barbican.Core.Plugins.Api.RawSend;

/// <summary>Host result carrying either an accepted receipt or a safe bridge failure.</summary>
public sealed record PluginRawSendOutcome(RawMessageReceipt? Receipt, PluginErrorCode? Error);
