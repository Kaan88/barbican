// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:284,2123 ui.setPages (permission ui:app-page; checkPages, pages.ts:19-45: a list of at most 3 {id, title, icon?}; resolves undefined) and pages.ts:57-85 setPluginPages.
// The pages are the calling add-on's own (the registry keys them by the runtime's package, never by anything the add-on passes). Deviation: Bulwark does not limit the calls; a call replaces the add-on's list and redraws the
// rail, so Barbican allows 20 a minute per add-on. No signed-in login is needed: the rail entries do not depend on the account.
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Pages;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Navigation;

internal sealed class PluginUiSetPages(PluginNavigationHost host) : PluginNavigationMethod("ui.setPages", [["ui:app-page"]], host, needsLogin: false)
{
    protected override string RateKind => "ui-set-pages";

    protected override int RateLimit => 20;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        arguments.Kind == PluginValueKind.Array && arguments.Count == 1 && PluginPageChecks.TryRead(arguments.Item(0), out _) ? null : PluginErrorCode.InvalidArguments;

    protected override Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        if (Host.Pages is not { } registry) return Task.FromResult(Fail(PluginErrorCode.Unavailable));
        if (!context.Demand("ui:app-page", null).IsOk) return Task.FromResult(Fail(PluginErrorCode.PermissionDenied));
        _ = PluginPageChecks.TryRead(arguments.Item(0), out var pages);
        return Task.FromResult(registry.Set(context.Binding, pages) ? Done() : Stale(context));
    }
}
