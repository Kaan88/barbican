// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1997-2008 doAppGetInfo (no permission, host-api.ts:202):
// { product, name, version, edition, stalwartFeatures, demoMode }. Deviations: product is "Barbican" (an add-on that gates on Bulwark's product
// string learns it is not running in Bulwark), edition is "full", stalwartFeatures is true (Stalwart is the only server) and demoMode is false.
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.App;

/// <summary>The app.getInfo method.</summary>
public sealed class PluginAppGetInfo(IPluginAppInfo info) : IPluginApiMethod
{
    private readonly IPluginAppInfo _info = info ?? throw new ArgumentNullException(nameof(info));

    public PluginApiDescriptor Descriptor { get; } = new("app.getInfo", [], TimeSpan.FromSeconds(30));

    public PluginErrorCode? ValidateArguments(PluginValue arguments) => null;

    public async Task<PluginCallOutcome> InvokeAsync(IPluginInvocationContext context, PluginValue arguments, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
        var b = new PluginValueBuilder();
        return PluginCallOutcome.Succeeded(b.Build(b.Object(
            ("product", b.String("Barbican")), ("name", b.String(_info.Name)), ("version", b.String(_info.Version)), ("edition", b.String("full")),
            ("stalwartFeatures", b.Boolean(true)), ("demoMode", b.Boolean(false)))));
    }
}
