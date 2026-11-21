// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:302,2403-2406 (sieve.isSupported, filters:read): whether the signed-in server has Sieve filters (client.supportsSieve()). Resolves a boolean; no server call.
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailRules;

internal sealed class PluginSieveIsSupported() : PluginScopedMethod("sieve.isSupported", "filters:read")
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => null;

    protected override Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var b = new PluginValueBuilder();
        return Task.FromResult(Success(b, b.Boolean(PluginRulesAccess.SieveAvailable(capture!.Session))));
    }
}
