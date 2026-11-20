using Barbican.Core.Plugins.Bridge;

namespace Barbican.Core.Plugins.Api.RawSend;

/// <summary>Host authorization and issuance for raw submissions; receives bytes without copying them.</summary>
public interface IPluginRawSendHost
{
    /// <summary>Capture and authorize the active account and policy before copying bytes or issuing anything.</summary>
    Task<PluginRawSendOutcome> SendAsync(IPluginInvocationContext context, PluginRawSendRequest request, CancellationToken ct);
}
