// Mirrors: Bulwark lib/plugin-sandbox/host-api-calendar.ts:1032-1053 doTasksAcknowledgeAlert and host-api.ts:320,2429 (tasks:write): marks one of a task's alerts dismissed at `when` (UTC; now when left out), or not
// dismissed with null; only that alert's `acknowledged` is written (JSON pointer alerts/<id>/acknowledged, RFC 8984 4.5.2). Refused: a task without that alert, and a task that still uses its calendar's default alerts
// (make them its own with tasks.update(id, { alerts: {} }) first). An alert already in the asked state writes nothing.
using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Calendar;
using Barbican.Core.Plugins.Api.Calendar;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Tasks;

internal sealed class PluginTasksAcknowledgeAlert(IPluginCalendarHost host, PluginKeyedGate gate) : PluginCalendarMethod("tasks.acknowledgeAlert", "tasks:write", host, true)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) && PluginMailArgs.IsId(PluginMailArgs.At(arguments, 1)) && TryWhen(arguments, out _, out _) ? null : PluginErrorCode.InvalidArguments;

    protected override Task<PluginCallOutcome> RunAsync(PluginCalendarCall call, PluginValue arguments, CancellationToken ct)
    {
        var (id, alertId) = (arguments.Item(0).StringValue, arguments.Item(1).StringValue);
        _ = TryWhen(arguments, out var given, out var clear);
        var when = clear ? null : given ?? Host.Time.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        return gate.RunAsync(call.AccountKey + "\n" + id, () => AcknowledgeAsync(call, id, alertId, when, ct), ct);
    }

    private async Task<PluginCallOutcome> AcknowledgeAsync(PluginCalendarCall call, string id, string alertId, string? when, CancellationToken ct)
    {
        var snapshot = await PluginCalendarData.SnapshotAsync(Host, call.AccountKey, ct).ConfigureAwait(false);
        if (snapshot is null || PluginTaskData.Find(snapshot, id) is not { } task) return Fail(PluginErrorCode.Unavailable);
        if (!await PluginCalendarData.CanChangeAsync(Host, call.Session, snapshot, task, false, ct).ConfigureAwait(false)) return Fail(PluginErrorCode.PermissionDenied);
        if (task.Obj("alerts") is not { } alerts || !alerts.TryGetPropertyValue(alertId, out var alert) || alert is null || task.IsTrue("useDefaultAlerts")) return Fail(PluginErrorCode.Failed);
        if (string.Equals(alert.Str("acknowledged"), when, StringComparison.Ordinal)) return Done();
        if (!await call.BeginMutationAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var written = await Host.Writer.UpdateAsync(call.AccountKey, id, new JsonObject { [PluginTaskJson.AcknowledgedPath(alertId)] = when }, ct).ConfigureAwait(false);
        return written.IsOk ? Done() : Fail(PluginErrorCode.Failed);
    }

    /// <summary>The third argument: left out means now, null means not dismissed, else an instant in UTC.</summary>
    private static bool TryWhen(PluginValue arguments, out string? when, out bool clear)
    {
        (when, clear) = (null, false);
        if (PluginMailArgs.At(arguments, 2) is not { } value) return true;
        if (value.Kind == PluginValueKind.Null)
        {
            clear = true;
            return true;
        }

        return PluginMailArgs.TryString(value, 64, out var text) && PluginWhenParser.TryInstant(text, out when);
    }
}
