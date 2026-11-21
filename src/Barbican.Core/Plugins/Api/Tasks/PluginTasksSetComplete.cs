// Mirrors: Bulwark lib/plugin-sandbox/host-api-calendar.ts:1055-1065 doTasksSetComplete and host-api.ts:319,2428 (tasks:write): a task already in the asked state is left alone; otherwise the same tick the Tasks view's
// checkbox does (progress completed or needs-action through the writer, which fires onBeforeTaskUpdate and onTaskToggleComplete). Deviation: Bulwark's store moves a recurring task to its next date instead of completing it
// (toggleTaskComplete, task-store.ts:306-330); Barbican's Tasks view ticks a recurring task as completed, so this call does too.
using Barbican.Core.Calendar;
using Barbican.Core.Plugins.Api.Calendar;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using System.Text.Json.Nodes;

namespace Barbican.Core.Plugins.Api.Tasks;

internal sealed class PluginTasksSetComplete(IPluginCalendarHost host, PluginKeyedGate gate) : PluginCalendarMethod("tasks.setComplete", "tasks:write", host, true)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) && PluginMailArgs.At(arguments, 1) is { Kind: PluginValueKind.Boolean } ? null : PluginErrorCode.InvalidArguments;

    protected override Task<PluginCallOutcome> RunAsync(PluginCalendarCall call, PluginValue arguments, CancellationToken ct)
    {
        var (id, done) = (arguments.Item(0).StringValue, arguments.Item(1).BooleanValue);
        return gate.RunAsync(call.AccountKey + "\n" + id, () => SetAsync(call, id, done, ct), ct);
    }

    private async Task<PluginCallOutcome> SetAsync(PluginCalendarCall call, string id, bool done, CancellationToken ct)
    {
        var snapshot = await PluginCalendarData.SnapshotAsync(Host, call.AccountKey, ct).ConfigureAwait(false);
        if (snapshot is null || PluginTaskData.Find(snapshot, id) is not { } task) return Fail(PluginErrorCode.Unavailable);
        if (!await PluginCalendarData.CanChangeAsync(Host, call.Session, snapshot, task, false, ct).ConfigureAwait(false)) return Fail(PluginErrorCode.PermissionDenied);
        if (string.Equals(task.Str("progress"), "completed", StringComparison.Ordinal) == done) return Done();
        if (!await call.BeginMutationAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var written = await Host.Writer.UpdateAsync(call.AccountKey, id, new JsonObject { ["progress"] = done ? "completed" : "needs-action" }, ct).ConfigureAwait(false);
        return written.IsOk ? Done() : Fail(PluginErrorCode.Failed);
    }
}
