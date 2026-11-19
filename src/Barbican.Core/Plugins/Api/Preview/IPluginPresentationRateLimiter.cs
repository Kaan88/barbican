// Mirrors: lib/plugin-sandbox/host-api-input.ts:183-196; host-api-print.ts:22-27; host-api-file-viewer.ts:22-24.
namespace Barbican.Core.Plugins.Api.Preview;
/// <summary>Host-shared per-profile, stable-plugin, operation-specific presentation budget.</summary>
public interface IPluginPresentationRateLimiter
{
    bool TryAcquire(string profile, string stablePluginId, PluginPresentationOperation operation);
}
