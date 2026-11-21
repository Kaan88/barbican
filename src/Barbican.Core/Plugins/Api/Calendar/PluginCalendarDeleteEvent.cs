// Mirrors: Bulwark lib/plugin-sandbox/assistant/c.calendar.ts:129-163 doCalendarDeleteWithUndo (host-api.ts:313 + c.ts:25,86: calendar:write; one occurrence when given an occurrence's id; never sends
// cancellations; resolves { undoToken } for a single event of the user's own calendars, null for the rest or when another add-on stopped the delete) over host-api-calendar.ts:562-569 doCalendarDeleteEvent.
// Goes through the writer the calendar UI uses (onBeforeEventDelete, onAfterEventDelete run; the open calendar updates) and only when the event's calendars allow it (myRights, see PluginCalendarData.CanChangeAsync).
using System.Text.Json.Nodes;
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Calendar;

internal sealed class PluginCalendarDeleteEvent(IPluginCalendarHost host) : PluginCalendarMethod("calendar.deleteEvent", "calendar:write", host, true)
{
    /// <summary>The undo kind of an event (c.calendar.ts:171).</summary>
    internal const string Kind = "event";

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(PluginCalendarCall call, PluginValue arguments, CancellationToken ct)
    {
        var id = arguments.Item(0).StringValue;
        var snapshot = await PluginCalendarData.SnapshotAsync(Host, call.AccountKey, ct).ConfigureAwait(false);
        if (snapshot is null || PluginCalendarData.FindObject(snapshot, id) is not { } existing || CalendarOccurrences.IsTask(existing)) return Fail(PluginErrorCode.Unavailable);
        if (!await PluginCalendarData.CanChangeAsync(Host, call.Session, snapshot, existing, true, ct).ConfigureAwait(false)) return Fail(PluginErrorCode.PermissionDenied);
        var rid = CalendarIds.RecurrenceIdOf(id);
        var restorable = rid is null && Restorable(existing);
        JsonObject? copy = restorable ? Copy(existing) : null;
        if (!await call.BeginMutationAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var deleted = rid is null
            ? await Host.Writer.DeleteEventAsync(call.AccountKey, id, EditScope.WholeEvent, null, false, ct).ConfigureAwait(false)
            : await Host.Writer.DeleteEventAsync(call.AccountKey, CalendarIds.BaseId(id), EditScope.Occurrence, rid, false, ct).ConfigureAwait(false);
        var b = new PluginValueBuilder();
        if (!deleted.IsOk)
        {
            // another add-on's onBeforeEventDelete said no: nothing was deleted, nothing to undo (c.calendar.ts:159)
            return ReferenceEquals(deleted.Error, CalendarWriter.StoppedByPlugin) ? Success(b, b.Object(("undoToken", b.Null()))) : Fail(PluginErrorCode.Failed);
        }

        var token = copy is null ? null : Host.Undo.Issue(call.PluginId, call.AccountKey, Kind, copy);
        return Success(b, b.Object(("undoToken", token is null ? b.Null() : b.String(token))));
    }

    /// <summary>c.calendar.ts:127-132: a single event of the user's own calendars is the only kind an undo can put back as it was.</summary>
    private static bool Restorable(JsonNode ev) =>
        ev.Str("_acc") is null && !(ev.Str("id") ?? "").Contains('~', StringComparison.Ordinal) && ev.Str("recurrenceId") is null
        && ev.Arr("recurrenceRules") is not { Count: > 0 } && ev.Obj("recurrenceOverrides") is not { Count: > 0 };

    /// <summary>c.calendar.ts:118-124 NOT_RECREATED: what the server sets itself and the app's own fields are left out when the event is created again.</summary>
    private static JsonObject Copy(JsonNode ev)
    {
        var copy = (JsonObject)ev.DeepClone();
        foreach (var key in new[] { "id", "created", "updated", "sequence", "isDraft", "isOrigin", "baseEventId", "blobId", "utcStart", "utcEnd", "originalId", "originalCalendarIds", "accountId", "accountName", "isShared" })
        {
            _ = copy.Remove(key);
        }

        foreach (var key in copy.Where(p => p.Key.StartsWith('_')).Select(p => p.Key).ToList()) _ = copy.Remove(key);
        return copy;
    }
}
