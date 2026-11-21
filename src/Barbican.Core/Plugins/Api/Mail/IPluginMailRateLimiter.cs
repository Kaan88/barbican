// Mirrors: Bulwark lib/plugin-sandbox/host-api-input.ts:183-196 withinRate (a per-plugin sliding minute per kind).
namespace Barbican.Core.Plugins.Api.Mail;

/// <summary>Host-owned per-plugin budget for one kind of read.</summary>
public interface IPluginMailRateLimiter
{
    bool TryAcquire(string kind, string profile, string stablePluginId, int maxPerMinute);
}
