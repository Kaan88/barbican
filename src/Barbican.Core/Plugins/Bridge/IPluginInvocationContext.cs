// Mirrors: Bulwark typed host API routing with Barbican grant/account/lifetime fences. No generic service provider or raw JMAP API is exposed.
using Barbican.Core.Common;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Runtime;
namespace Barbican.Core.Plugins.Bridge;
public interface IPluginInvocationContext
{
    PluginRuntimeBinding Binding { get; }
    PluginGrantSnapshot Grant { get; }
    Result<PluginSessionCapture> CaptureActive();
    Result<PluginSessionCapture> CaptureOwner(string localAccountKey);
    Result<Unit> Demand(string permission, string? localAccountKey);
    /// <summary>Revalidate current authority and all captured sessions immediately before mutation and after awaits.</summary>
    ValueTask<bool> CheckCurrentAsync(CancellationToken ct);
    /// <summary>Call immediately before issuing an irreversible mutation, after its last successful current-authority check.</summary>
    void MarkMutationIssued();
    bool HasIssuedMutation { get; }
}
