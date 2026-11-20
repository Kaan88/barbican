// Mirrors: Bulwark lib/plugin-sandbox/host-bridge.ts:327-459; activation requires authenticated ready and init completion.
using Barbican.Core.Common;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Plugins.Packages.Managed;
using Barbican.Core.Jmap.Errors;
namespace Barbican.App.Plugins.Sandbox;
/// <summary>Attaches native handlers, navigates the trusted root, and authenticates activation.</summary>
public interface IPluginSandboxStartup
{
    Task<Result<IPluginRuntimeInstance>> ActivateAsync(PluginSandboxSession session, PluginRuntimeStart trustedRequest, string verifiedCode, CancellationToken ct);
    Task<Result<IPluginRuntimeInstance>> ActivateManagedAsync(PluginSandboxSession session, PluginRuntimeStart trustedRequest, ManagedPluginStartupProof proof, CancellationToken ct) =>
        Task.FromResult(Result<IPluginRuntimeInstance>.Fail(new ValidationError("Managed activation is unavailable.")));
}
