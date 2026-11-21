// Mirrors: Bulwark lib/plugin-sandbox/host-api-calendar.ts:470-513 doCalendarCreateEvent and host-api.ts:311,2420 (calendar:write). Never sends invitations (the writer is called without scheduling
// messages), runs the same hooks as the calendar's own create (ICalendarWriter.SaveEventAsync: onBeforeEventCreate, onCreatingEvent, onEventCreate) so the open calendar shows the event,
// and resolves to the new event or null when it was not created (another add-on stopped it, or the server refused). The calendar must allow writing (myRights mayWriteAll or mayWriteOwn, calendar-editability.ts:20-27)
// and not be a feed; the grant is the add-on's, the rights are the user's.
using System.Text.Json.Nodes;
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Calendar;

internal sealed class PluginCalendarCreateEvent(IPluginCalendarHost host) : PluginCalendarMethod("calendar.createEvent", "calendar:write", host, true)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => PluginEventInput.TryParse(PluginMailArgs.At(arguments, 0), true, out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(PluginCalendarCall call, PluginValue arguments, CancellationToken ct)
    {
        if (!PluginEventInput.TryParse(PluginMailArgs.At(arguments, 0), true, out var input)) return Fail(PluginErrorCode.InvalidArguments);
        var snapshot = await PluginCalendarData.SnapshotAsync(Host, call.AccountKey, ct).ConfigureAwait(false);
        if (snapshot is null || PluginCalendarData.PickCalendar(snapshot, input.CalendarId) is not { } calendar) return Fail(PluginErrorCode.Unavailable);
        var feeds = await PluginCalendarData.FeedCalendarsAsync(Host, call.AccountKey, ct).ConfigureAwait(false);
        if (!PluginCalendarData.CanCreateIn(calendar, feeds)) return Fail(PluginErrorCode.PermissionDenied);
        if (!PluginTimingBuilder.TryBuild(input.Timing, Host.DefaultZoneId, out var timing)) return Fail(PluginErrorCode.InvalidArguments);
        JsonObject? participants = null;
        if (input.Participants is { Count: > 0 })
        {
            var own = await PluginCalendarData.OwnAsync(call.Session, ct).ConfigureAwait(false);
            if (own.Organizer is null) return Fail(PluginErrorCode.Unavailable);
            participants = EventEditor.ParticipantFields(null, input.Participants, own.Addresses, false, own.Organizer);
        }

        var uid = input.Uid is { } given ? PluginEventJson.PluginUid(call.PluginId, given) : EventEditor.NewUid();
        var data = PluginEventJson.Create(input, timing, calendar.Str("id") ?? "", uid, participants);
        if (!await call.BeginMutationAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var saved = await Host.Writer.SaveEventAsync(new EventWrite(call.AccountKey, null, data, EditScope.WholeEvent, null, false), ct).ConfigureAwait(false);
        if (!saved.IsOk) return Null();
        var b = new PluginValueBuilder();
        var created = Host.Store.Snapshot(call.AccountKey) is { } fresh ? PluginCalendarData.CreatedObject(fresh, saved.Value!) : null;
        if (created is null)
        {
            data["id"] = saved.Value!;
            created = data;
        }

        return Success(b, PluginCalendarProjection.Event(b, created, null, Host.LocalZone));
    }
}
