// Mirrors: Bulwark lib/plugin-sandbox/host-api-calendar.ts:969-973 doTasksGet (one task by id, null when there is none) and host-api.ts:316,2425 (tasks:read).
using Barbican.Core.Plugins.Api.Calendar;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Tasks;

internal sealed class PluginTasksGet(IPluginCalendarHost host) : PluginCalendarMethod("tasks.get", "tasks:read", host, false)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(PluginCalendarCall call, PluginValue arguments, CancellationToken ct)
    {
        var snapshot = await PluginCalendarData.SnapshotAsync(Host, call.AccountKey, ct).ConfigureAwait(false);
        if (snapshot is null) return Fail(PluginErrorCode.Unavailable);
        if (!await call.Context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var b = new PluginValueBuilder();
        return Success(b, PluginTaskData.Find(snapshot, arguments.Item(0).StringValue) is { } task ? PluginCalendarProjection.Task(b, task) : b.Null());
    }
}
