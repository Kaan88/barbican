// Mirrors: Bulwark lib/plugin-sandbox/assistant/c.ts:33-36 and host-api.ts:204-206 (the permission of each app.*Settings method) over PluginScopedMethod's per-call fences. Barbican adds a per-minute budget per add-on (Bulwark has none).
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.App;

internal abstract class PluginAppSettingsMethod(string name, string? permission, PluginSettingsHost host, bool mutates = false, string? rateKind = null, int rateLimit = 60)
    : PluginScopedMethod(name, permission, mutates, slow: mutates, limiter: host.Limiter)
{
    protected PluginSettingsHost Host { get; } = host;

    protected override string? RateKind => rateKind;

    protected override int RateLimit => rateLimit;

    protected static bool IsPlainObject(PluginValue? value) => value is { Kind: PluginValueKind.Object };
}
