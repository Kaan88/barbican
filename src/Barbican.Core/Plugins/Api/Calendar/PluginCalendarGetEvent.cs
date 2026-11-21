// Mirrors: Bulwark lib/plugin-sandbox/host-api-calendar.ts:358-375 doCalendarGetEvent (one event by id, null when there is none; an occurrence id "id|recurrenceId" names that occurrence)
// and host-api.ts:310,2419 (calendar:read). Deviation: a task's id answers null here (tasks.get is the task call); the birthday events of the virtual Birthdays calendar do not exist.
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Recurrence;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Calendar;

internal sealed class PluginCalendarGetEvent(IPluginCalendarHost host) : PluginCalendarMethod("calendar.getEvent", "calendar:read", host, false)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(PluginCalendarCall call, PluginValue arguments, CancellationToken ct)
    {
        var id = arguments.Item(0).StringValue;
        var snapshot = await PluginCalendarData.SnapshotAsync(Host, call.AccountKey, ct).ConfigureAwait(false);
        if (snapshot is null) return Fail(PluginErrorCode.Unavailable);
        if (!await call.Context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var b = new PluginValueBuilder();
        if (PluginCalendarData.FindObject(snapshot, id) is not { } ev || CalendarOccurrences.IsTask(ev)) return Success(b, b.Null());
        if (CalendarIds.RecurrenceIdOf(id) is not { } rid) return Success(b, PluginCalendarProjection.Event(b, ev, null, Host.LocalZone));
        if (RecurrenceEngine.ParseLocal(rid) is not { } day) return Success(b, b.Null());
        var occurrence = RecurrenceEngine.Occurrences(ev, day.AddDays(-1), day.AddDays(2)).FirstOrDefault(o => string.Equals(o.RecurrenceId, rid, StringComparison.Ordinal));
        return occurrence is null ? Success(b, b.Null()) : Success(b, PluginCalendarProjection.Event(b, ev, occurrence, Host.LocalZone));
    }
}
