// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:217,2233 (plugin.getConfig, no static gate: NO_STATIC_GATE) and 2018-2022 publicConfig: the administrator's non-secret settings for THIS add-on as a flat object. The add-on id is the one the host
// bound the call to, never an argument (the method takes none), so an add-on reads only its own settings, never another add-on's and never a secret. Barbican has no administrator configuration store before M7: with the default
// source (PluginPublicConfigNone) every add-on gets {}, which is also what Bulwark answers when its server has no config. Values are projected as JSON values, bounded by the add-on value limits.
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.AccountSettings;

internal sealed class PluginGetConfig(IPluginPublicConfig source) : PluginScopedMethod("plugin.getConfig", null)
{
    private const int MaxEntries = 256;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => null;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var config = await source.ReadAsync(context.Binding.Package.Id, ct).ConfigureAwait(false);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        if (config.Count > MaxEntries) return PluginCallOutcome.Failed(PluginErrorCode.Failed);
        var b = new PluginValueBuilder();
        try
        {
            var fields = config.Select(e => (e.Key, e.Value is null ? b.Null() : b.Import(PluginAccountData.ToPluginValue(e.Value)))).ToArray();
            return Success(b, b.Object(fields));
        }
        catch (PluginValueFormatException)
        {
            return PluginCallOutcome.Failed(PluginErrorCode.Failed);
        }
    }
}
