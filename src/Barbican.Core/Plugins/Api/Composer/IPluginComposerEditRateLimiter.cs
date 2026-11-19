// Mirrors: lib/plugin-sandbox/host-api-composer.ts:36-42 and host-api-input.ts:183-196.
namespace Barbican.Core.Plugins.Api.Composer;
/// <summary>Shared host-owned budget across a stable plugin's frames and content generations.</summary>
public interface IPluginComposerEditRateLimiter
{
    bool TryAcquire(string profile, string stablePluginId);
}
