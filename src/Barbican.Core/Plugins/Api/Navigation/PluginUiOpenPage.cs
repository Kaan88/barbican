// Mirrors: Bulwark lib/plugin-sandbox/page-navigation.ts:23-62 pagePathHref / requestPageNavigation and host-api.ts:286,2124 ui.openPage (permission ui:app-page; args pageId and an optional path; only a page the add-on
// set itself, a path of at most 2000 characters without "." or ".." segments; resolves true when the app moved, false when nothing could take it).
// Deviations: a page id the add-on never set is InvalidArguments (Bulwark throws the same refusal as an Error); at most 20 moves a minute per add-on (Bulwark has no limit). The page is looked up by the calling add-on's own
// package, so one add-on can never open another's page.
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Pages;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Navigation;

internal sealed class PluginUiOpenPage(PluginNavigationHost host) : PluginNavigationMethod("ui.openPage", [["ui:app-page"]], host, needsLogin: false)
{
    protected override string RateKind => "ui-open-page";

    protected override int RateLimit => 20;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments)
    {
        if (arguments.Kind != PluginValueKind.Array || arguments.Count is < 1 or > 2) return PluginErrorCode.InvalidArguments;
        var id = PluginMailArgs.At(arguments, 0);
        return id is { Kind: PluginValueKind.String } && PluginPageChecks.IsPageId(id.StringValue) && PluginPageChecks.TryPath(PluginMailArgs.At(arguments, 1), out _)
            ? null : PluginErrorCode.InvalidArguments;
    }

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        if (Host.Pages is not { } registry) return Fail(PluginErrorCode.Unavailable);
        if (!context.Demand("ui:app-page", null).IsOk) return Fail(PluginErrorCode.PermissionDenied);
        var pageId = arguments.Item(0).StringValue;
        // Bulwark pages.ts findPage(plugin.id, pageId): only the caller's own pages
        if (registry.Find(context.Binding.Package.Id, pageId) is null) return Fail(PluginErrorCode.InvalidArguments);
        _ = PluginPageChecks.TryPath(PluginMailArgs.At(arguments, 1), out var path);
        if (Host.PageOpener is not { } opener) return Answer(false);
        return Answer(await opener.OpenPageAsync(context.Binding.Package.Id, pageId, path, ct).ConfigureAwait(false));
    }
}
