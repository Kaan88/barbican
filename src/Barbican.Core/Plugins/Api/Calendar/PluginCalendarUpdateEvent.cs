// Mirrors: Bulwark lib/plugin-sandbox/host-api-calendar.ts:515-560 doCalendarUpdateEvent and host-api.ts:312,2421 (calendar:write). Only the fields in the patch change; start, end, duration, timeZone and
// isAllDay are worked out together against the event's current times; never sends updates to participants. An occurrence id ("id|recurrenceId") changes that occurrence only, as an override
// on the series (the calendar's "this event" scope, EventEditor.OccurrenceOverride, RFC 8984 4.3.4); moving a series to another calendar, changing its repeat or its zone is the whole series' call.
// The write goes through the writer the calendar UI uses (hooks run, the open calendar updates) and only when the event's calendars allow it (myRights mayWriteAll, or mayWriteOwn for an event the
// user organises or that has no owner; calendar-editability.ts:54-95): an earlier review found mailbox rights checked here by mistake.
using System.Text.Json.Nodes;
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Calendar.Recurrence;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Calendar;

internal sealed class PluginCalendarUpdateEvent(IPluginCalendarHost host) : PluginCalendarMethod("calendar.updateEvent", "calendar:write", host, true)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) && PluginEventInput.TryParse(PluginMailArgs.At(arguments, 1), false, out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(PluginCalendarCall call, PluginValue arguments, CancellationToken ct)
    {
        var id = arguments.Item(0).StringValue;
        if (!PluginEventInput.TryParse(PluginMailArgs.At(arguments, 1), false, out var input)) return Fail(PluginErrorCode.InvalidArguments);
        var rid = CalendarIds.RecurrenceIdOf(id);
        if (rid is not null && (input.CalendarId is not null || input.HasRepeat || input.TimeZoneGiven)) return Fail(PluginErrorCode.InvalidArguments);
        var snapshot = await PluginCalendarData.SnapshotAsync(Host, call.AccountKey, ct).ConfigureAwait(false);
        if (snapshot is null || PluginCalendarData.FindObject(snapshot, id) is not { } existing || CalendarOccurrences.IsTask(existing)) return Fail(PluginErrorCode.Unavailable);
        if (input.CalendarId is not null && await CheckTargetAsync(call, snapshot, input.CalendarId, ct).ConfigureAwait(false) is { } refused) return Fail(refused);
        if (!await PluginCalendarData.CanChangeAsync(Host, call.Session, snapshot, existing, false, ct).ConfigureAwait(false)) return Fail(PluginErrorCode.PermissionDenied);
        var (patch, error) = await BuildPatchAsync(call, input, existing, rid, ct).ConfigureAwait(false);
        if (patch is null) return Fail(error);
        if (patch.Count == 0) return Done();
        if (!await call.BeginMutationAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var written = await Host.Writer.UpdateAsync(call.AccountKey, CalendarIds.BaseId(id), patch, ct).ConfigureAwait(false);
        return written.IsOk ? Done() : Fail(PluginErrorCode.Failed);
    }

    /// <summary>The calendar the event is to move to must exist, be a place events may go and not be a feed.</summary>
    private async Task<PluginErrorCode?> CheckTargetAsync(PluginCalendarCall call, CalendarSnapshot snapshot, string calendarId, CancellationToken ct)
    {
        if (PluginCalendarData.CalendarById(snapshot, calendarId) is not { } target) return PluginErrorCode.Unavailable;
        var feeds = await PluginCalendarData.FeedCalendarsAsync(Host, call.AccountKey, ct).ConfigureAwait(false);
        return PluginCalendarData.CanCreateIn(target, feeds) ? null : PluginErrorCode.PermissionDenied;
    }

    /// <summary>The patch to write: the fields changed, for a series; the override of one occurrence, for an occurrence id. Null with the error code when the times cannot be worked out.</summary>
    private async Task<(JsonObject? Patch, PluginErrorCode Error)> BuildPatchAsync(PluginCalendarCall call, PluginEventInput input, JsonNode existing, string? rid, CancellationToken ct)
    {
        JsonObject? participants = null;
        if (input.Participants is { } people)
        {
            var own = await PluginCalendarData.OwnAsync(call.Session, ct).ConfigureAwait(false);
            if (people.Count > 0 && own.Organizer is null) return (null, PluginErrorCode.Unavailable);
            participants = EventEditor.ParticipantFields(existing, people, own.Addresses, false, own.Organizer);
        }

        Occurrence? occurrence = null;
        var timingBase = existing;
        if (rid is not null)
        {
            if (RecurrenceEngine.ParseLocal(rid) is not { } day) return (null, PluginErrorCode.Unavailable);
            occurrence = RecurrenceEngine.Occurrences(existing, day.AddDays(-1), day.AddDays(2)).FirstOrDefault(o => string.Equals(o.RecurrenceId, rid, StringComparison.Ordinal));
            if (occurrence is null) return (null, PluginErrorCode.Unavailable);
            timingBase = new JsonObject
            {
                ["start"] = RecurrenceEngine.FormatLocal(occurrence.Start), ["duration"] = RecurrenceEngine.DurationString((long)(occurrence.End - occurrence.Start).TotalSeconds),
                ["showWithoutTime"] = existing.IsTrue("showWithoutTime"), ["timeZone"] = existing.Str("timeZone"),
            };
        }

        if (!PluginEventJson.TryChanges(input, existing, timingBase, Host.DefaultZoneId, participants, out var changes, out var timing)) return (null, PluginErrorCode.InvalidArguments);
        if (input.CalendarId is not null) changes["calendarIds"] = new JsonObject { [input.CalendarId] = true };
        if (occurrence is null || rid is null || changes.Count == 0) return (changes, PluginErrorCode.Failed);
        var newStart = timing is not null && RecurrenceEngine.ParseLocal(timing.Start) is { } moved ? moved : occurrence.Start;
        return (EventEditor.OverridePatch(existing, rid, EventEditor.OccurrenceOverride(existing, rid, changes, newStart)), PluginErrorCode.Failed);
    }
}
