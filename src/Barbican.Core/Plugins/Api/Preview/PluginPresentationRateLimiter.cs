// Mirrors: lib/plugin-sandbox/host-api-input.ts:183-196; host-api-print.ts:22-27; host-api-file-viewer.ts:22-24.
using Barbican.Core.Common;
namespace Barbican.Core.Plugins.Api.Preview;
public static class PluginPresentationRateLimiter
{
    public static IPluginPresentationRateLimiter Create(TimeProvider time, int maxKeys) => throw new PortPendingException("shared presentation rate limits");
}
