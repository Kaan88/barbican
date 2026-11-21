// Mirrors: Bulwark lib/plugin-sandbox/host-api-calendar.ts:50-52,895-921 doTasksList (options includeCompleted, calendarIds of 1 to 100 ids; open tasks only by default; at most 1000) and host-api.ts:315,2424 (tasks:read).
// Deviation: Bulwark asks the server (every object comes, events too, Stalwart has no task filter) and drops the virtual birthday calendar from a calendarIds list; Barbican reads the tasks the calendar store holds.
using Barbican.Core.Calendar;
using Barbican.Core.Plugins.Api.Calendar;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Tasks;

internal sealed class PluginTasksList(IPluginCalendarHost host) : PluginCalendarMethod("tasks.list", "tasks:read", host, false)
{
    internal const int MaxTasks = 1000;
    private static readonly string[] Allowed = ["includeCompleted", "calendarIds"];

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => TryParse(arguments, out _, out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(PluginCalendarCall call, PluginValue arguments, CancellationToken ct)
    {
        _ = TryParse(arguments, out var includeCompleted, out var calendars);
        var snapshot = await PluginCalendarData.SnapshotAsync(Host, call.AccountKey, ct).ConfigureAwait(false);
        if (snapshot is null) return Fail(PluginErrorCode.Unavailable);
        if (!await call.Context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var b = new PluginValueBuilder();
        return Success(b, b.Array([.. snapshot.Events
            .Where(e => CalendarOccurrences.IsTask(e) && (includeCompleted || !PluginTaskData.IsDone(e))
                && (calendars is null || PluginCalendarProjection.TrueKeys(e.Obj("calendarIds")).Any(calendars.Contains)))
            .Take(MaxTasks).Select(t => PluginCalendarProjection.Task(b, t))]));
    }

    private static bool TryParse(PluginValue arguments, out bool includeCompleted, out HashSet<string>? calendars)
    {
        (includeCompleted, calendars) = (false, null);
        if (!PluginMailArgs.TryOptions(PluginMailArgs.At(arguments, 0), Allowed, false, out var options)) return false;
        if (PluginMailArgs.Prop(options, "includeCompleted") is { } flag)
        {
            if (flag.Kind != PluginValueKind.Boolean) return false;
            includeCompleted = flag.BooleanValue;
        }

        if (PluginMailArgs.Prop(options, "calendarIds") is not { } ids) return true;
        if (ids.Kind != PluginValueKind.Array || ids.Count is 0 or > 100) return false;
        calendars = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < ids.Count; i++)
        {
            if (!PluginMailArgs.IsId(ids.Item(i))) return false;
            _ = calendars.Add(ids.Item(i).StringValue);
        }

        return true;
    }
}
