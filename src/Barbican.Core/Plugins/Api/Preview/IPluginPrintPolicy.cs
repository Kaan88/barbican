// Mirrors: lib/plugin-sandbox/host-api-print.ts:49-67; lib/device-policy.ts (strictest signed-in policy).
namespace Barbican.Core.Plugins.Api.Preview;
/// <summary>Host-owned aggregate print policy covering every signed-in account.</summary>
public interface IPluginPrintPolicy
{
    Task<bool> CanPrintAsync(CancellationToken ct);
}
