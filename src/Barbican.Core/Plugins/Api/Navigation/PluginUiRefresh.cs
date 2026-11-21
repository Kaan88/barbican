// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:2118 ui.refreshRowBadges (no permission; row-badges.ts:34-41: at most once a second, extra calls dropped), :2310-2325 ui.rerenderEmail and ui.rerenderFetchedEmails (no permission,
// no arguments, resolve undefined; the app re-runs the onRenderEmailBody / onEmailsFetched hooks).
// Deviations: Bulwark does not limit the rerender methods, but each of them re-reads the open message or the shown list from the server, so Barbican allows 6 a minute per add-on (RateLimited past that); refreshRowBadges allows 12
// a minute and drops the rest silently (the per-minute limiter has no one-second window). None needs a signed-in login: with no mail screen the views have nothing to redraw.
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Navigation;

internal sealed class PluginUiRefresh : PluginNavigationMethod
{
    private readonly Kind _kind;

    internal enum Kind
    {
        RowBadges,
        RerenderEmail,
        RerenderFetchedEmails,
    }

    internal PluginUiRefresh(PluginNavigationHost host, Kind kind)
        : base(kind switch { Kind.RowBadges => "ui.refreshRowBadges", Kind.RerenderEmail => "ui.rerenderEmail", _ => "ui.rerenderFetchedEmails" }, [], host, needsLogin: false)
    {
        _kind = kind;
    }

    protected override string RateKind => _kind switch { Kind.RowBadges => "ui-refresh-badges", Kind.RerenderEmail => "ui-rerender-email", _ => "ui-rerender-fetched" };

    protected override int RateLimit => _kind == Kind.RowBadges ? 12 : 6;

    protected override bool DropOverBudget => _kind == Kind.RowBadges;

    // Bulwark ignores whatever arguments come; the call has none
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => null;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        if (Host.Refresh is not { } refresh) return Fail(PluginErrorCode.Unavailable);
        await (_kind switch
        {
            Kind.RowBadges => refresh.RefreshRowBadgesAsync(ct),
            Kind.RerenderEmail => refresh.RerenderEmailAsync(ct),
            _ => refresh.RerenderFetchedEmailsAsync(ct),
        }).ConfigureAwait(false);
        return Done();
    }
}
