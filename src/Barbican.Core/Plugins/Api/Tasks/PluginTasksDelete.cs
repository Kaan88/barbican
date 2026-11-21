// Mirrors: Bulwark lib/plugin-sandbox/host-api-calendar.ts:1067-1073 doTasksDelete and host-api.ts:321,2430 (tasks:write). Goes through the writer the Tasks view uses (ICalendarWriter.DestroyAsync:
// onBeforeTaskDelete, onTaskDelete run) and only when the task's calendars allow a delete (mayWriteAll or mayWriteOwn, and mayDelete when the calendar states it). A task another add-on's hook kept is
// reported as it is in Bulwark (the call resolves; nothing is deleted).
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Plugins.Api.Calendar;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Tasks;

internal sealed class PluginTasksDelete(IPluginCalendarHost host) : PluginCalendarMethod("tasks.delete", "tasks:write", host, true)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(PluginCalendarCall call, PluginValue arguments, CancellationToken ct)
    {
        var id = arguments.Item(0).StringValue;
        var snapshot = await PluginCalendarData.SnapshotAsync(Host, call.AccountKey, ct).ConfigureAwait(false);
        if (snapshot is null || PluginTaskData.Find(snapshot, id) is not { } task) return Fail(PluginErrorCode.Unavailable);
        if (!await PluginCalendarData.CanChangeAsync(Host, call.Session, snapshot, task, true, ct).ConfigureAwait(false)) return Fail(PluginErrorCode.PermissionDenied);
        if (!await call.BeginMutationAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var deleted = await Host.Writer.DestroyAsync(call.AccountKey, id, ct).ConfigureAwait(false);
        return deleted.IsOk || ReferenceEquals(deleted.Error, CalendarWriter.StoppedByPlugin) ? Done() : Fail(PluginErrorCode.Failed);
    }
}
