// Mirrors: Bulwark lib/plugin-sandbox/host-api-calendar.ts:985-1004 doTasksCreate and host-api.ts:317,2426 (tasks:write): resolves to the new task, or null when it was not created (another add-on's onBeforeTaskCreate
// stopped it, or the server refused). Goes through the writer the Tasks view uses (ICalendarWriter.CreateAsync: onBeforeTaskCreate, onTaskCreate run; the open views update). The task goes in the requested calendar
// or the login's default one (Barbican has no task-only calendars; the Tasks view also files into the default calendar), and only when that calendar allows writing (myRights, calendar-editability.ts:20-27) and is not a feed.
using System.Text.Json.Nodes;
using Barbican.Core.Calendar;
using Barbican.Core.Plugins.Api.Calendar;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Tasks;

internal sealed class PluginTasksCreate(IPluginCalendarHost host) : PluginCalendarMethod("tasks.create", "tasks:write", host, true)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => PluginTaskInput.TryParse(PluginMailArgs.At(arguments, 0), true, out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(PluginCalendarCall call, PluginValue arguments, CancellationToken ct)
    {
        if (!PluginTaskInput.TryParse(PluginMailArgs.At(arguments, 0), true, out var input)) return Fail(PluginErrorCode.InvalidArguments);
        var snapshot = await PluginCalendarData.SnapshotAsync(Host, call.AccountKey, ct).ConfigureAwait(false);
        if (snapshot is null || PluginCalendarData.PickCalendar(snapshot, input.CalendarId) is not { } calendar) return Fail(PluginErrorCode.Unavailable);
        var feeds = await PluginCalendarData.FeedCalendarsAsync(Host, call.AccountKey, ct).ConfigureAwait(false);
        if (!PluginCalendarData.CanCreateIn(calendar, feeds)) return Fail(PluginErrorCode.PermissionDenied);
        var calendarId = calendar.Str("id") ?? "";
        var data = PluginTaskJson.Create(input, Host.DefaultZoneId);
        if (!await call.BeginMutationAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var created = await Host.Writer.CreateAsync(call.AccountKey, calendarId, data, ct).ConfigureAwait(false);
        if (!created.IsOk) return Null();
        var task = Host.Store.Snapshot(call.AccountKey) is { } fresh ? PluginTaskData.Find(fresh, created.Value!) : null;
        if (task is null)
        {
            data["id"] = created.Value!;
            data["calendarIds"] = new JsonObject { [calendarId] = true };
            task = data;
        }

        var b = new PluginValueBuilder();
        return Success(b, PluginCalendarProjection.Task(b, task));
    }
}
