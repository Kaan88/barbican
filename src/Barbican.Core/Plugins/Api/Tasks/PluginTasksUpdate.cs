// Mirrors: Bulwark lib/plugin-sandbox/host-api-calendar.ts:1006-1030 doTasksUpdate and host-api.ts:318,2427 (tasks:write): only the fields in the patch change; alerts, links, relatedTo and keywords merge by key (see
// PluginTaskJson); a patch that changes nothing writes nothing; a plugin's writes to one task run one after another (PluginKeyedGate, :923-937). Goes through the writer the Tasks view uses
// (onBeforeTaskUpdate, onTaskUpdate run) and only when the task's calendars allow it (myRights mayWriteAll, or mayWriteOwn for a task the user organises or that has no owner).
// Deviation: Bulwark re-reads the task's maps from the server before merging (withServerMaps); Barbican merges against the store the calendar shows, which the push channel keeps current.
using Barbican.Core.Plugins.Api.Calendar;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Tasks;

internal sealed class PluginTasksUpdate(IPluginCalendarHost host, PluginKeyedGate gate) : PluginCalendarMethod("tasks.update", "tasks:write", host, true)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) && PluginTaskInput.TryParse(PluginMailArgs.At(arguments, 1), false, out _) ? null : PluginErrorCode.InvalidArguments;

    protected override Task<PluginCallOutcome> RunAsync(PluginCalendarCall call, PluginValue arguments, CancellationToken ct)
    {
        var id = arguments.Item(0).StringValue;
        return !PluginTaskInput.TryParse(PluginMailArgs.At(arguments, 1), false, out var input)
            ? Task.FromResult(Fail(PluginErrorCode.InvalidArguments))
            : gate.RunAsync(call.AccountKey + "\n" + id, () => UpdateAsync(call, id, input, ct), ct);
    }

    private async Task<PluginCallOutcome> UpdateAsync(PluginCalendarCall call, string id, PluginTaskInput input, CancellationToken ct)
    {
        var snapshot = await PluginCalendarData.SnapshotAsync(Host, call.AccountKey, ct).ConfigureAwait(false);
        if (snapshot is null || PluginTaskData.Find(snapshot, id) is not { } task) return Fail(PluginErrorCode.Unavailable);
        if (input.CalendarId is not null)
        {
            if (PluginCalendarData.CalendarById(snapshot, input.CalendarId) is not { } target) return Fail(PluginErrorCode.Unavailable);
            var feeds = await PluginCalendarData.FeedCalendarsAsync(Host, call.AccountKey, ct).ConfigureAwait(false);
            if (!PluginCalendarData.CanCreateIn(target, feeds)) return Fail(PluginErrorCode.PermissionDenied);
        }

        if (!await PluginCalendarData.CanChangeAsync(Host, call.Session, snapshot, task, false, ct).ConfigureAwait(false)) return Fail(PluginErrorCode.PermissionDenied);
        if (!PluginTaskJson.TryPatch(input, task, Host.DefaultZoneId, out var patch)) return Fail(PluginErrorCode.InvalidArguments);
        if (patch.Count == 0) return Done();
        if (!await call.BeginMutationAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var written = await Host.Writer.UpdateAsync(call.AccountKey, id, patch, ct).ConfigureAwait(false);
        return written.IsOk ? Done() : Fail(PluginErrorCode.Failed);
    }
}
