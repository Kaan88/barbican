// Mirrors: lib/plugin-sandbox/host-api-composer.ts:36-42 and host-api-input.ts:183-196.
using Barbican.Core.Common;
namespace Barbican.Core.Plugins.Api.Composer;
/// <summary>Creates one bounded, host-shared composer edit rate limiter.</summary>
public static class PluginComposerEditRateLimiter
{
    public static IPluginComposerEditRateLimiter Create(TimeProvider time, int maxKeys) => throw new PortPendingException("shared composer edit budget");
}
