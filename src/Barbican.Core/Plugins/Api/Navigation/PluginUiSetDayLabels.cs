// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:279,2120 ui.setDayLabels (permission ui:calendar-action; checkDayLabels, day-labels.ts:24-44; replaces the add-on's earlier labels; resolves undefined).
// The labels are the calling add-on's own (the registry keys them by the runtime's package, never by anything the add-on passes). Deviation: Bulwark does not limit the calls; a call redraws the calendar, so Barbican
// allows 20 a minute per add-on. No signed-in login is needed: day labels do not depend on the account.
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Calendar;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Navigation;

internal sealed class PluginUiSetDayLabels(PluginNavigationHost host) : PluginNavigationMethod("ui.setDayLabels", [["ui:calendar-action"]], host, needsLogin: false)
{
    protected override string RateKind => "ui-set-day-labels";

    protected override int RateLimit => 20;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        arguments.Kind == PluginValueKind.Array && arguments.Count == 1 && PluginDayLabelChecks.TryRead(arguments.Item(0), out _) ? null : PluginErrorCode.InvalidArguments;

    protected override Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        if (Host.DayLabels is not { } registry) return Task.FromResult(Fail(PluginErrorCode.Unavailable));
        if (!context.Demand("ui:calendar-action", null).IsOk) return Task.FromResult(Fail(PluginErrorCode.PermissionDenied));
        _ = PluginDayLabelChecks.TryRead(arguments.Item(0), out var labels);
        return Task.FromResult(registry.Set(context.Binding, labels) ? Done() : Stale(context));
    }
}
