// Mirrors: Bulwark lib/plugin-sandbox/host-api-calendar.ts:47-50,274-327 doCalendarGetEvents (options from / to / calendarIds; both bounds in UTC or both without a zone; a range of at most 366 days;
// at most 100 calendar ids; at most 1000 events, the earliest win; series expanded, never the master) and host-api.ts:309,2418 (calendar:read). Occurrence overlap is the calendar grid's
// (CalendarOccurrences.In, Rust calendar.rs:373 occurrences_in) so a plugin sees what the user sees. A bound without a zone is the user's clock (JMAP Calendars query bounds are LocalDateTime).
// Deviation: Bulwark asks the server for the range; Barbican reads the store the views show (push-fresh), so a read never moves the calendar and costs no round trip.
using System.Text.Json.Nodes;
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Recurrence;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Calendar;

internal sealed class PluginCalendarGetEvents(IPluginCalendarHost host) : PluginCalendarMethod("calendar.getEvents", "calendar:read", host, false)
{
    internal const int MaxEvents = 1000;
    private const int MaxCalendars = 100;
    private static readonly string[] Allowed = ["from", "to", "calendarIds"];

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => TryParse(arguments, out _, out _, out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(PluginCalendarCall call, PluginValue arguments, CancellationToken ct)
    {
        _ = TryParse(arguments, out var from, out var to, out var calendars);
        var snapshot = await PluginCalendarData.SnapshotAsync(Host, call.AccountKey, ct).ConfigureAwait(false);
        if (snapshot is null) return Fail(PluginErrorCode.Unavailable);
        if (!await call.Context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var local = Host.LocalZone;
        var (f, t) = (OnUserClock(from, local), OnUserClock(to, local));
        var rows = new List<(DateTime Start, Occurrence? Occurrence, JsonNode Event)>();
        foreach (var e in snapshot.Events)
        {
            if (CalendarOccurrences.IsTask(e) || (calendars is not null && !PluginCalendarProjection.TrueKeys(e.Obj("calendarIds")).Any(calendars.Contains))) continue;
            var allDay = e.IsTrue("showWithoutTime");
            var zone = allDay ? null : e.Str("timeZone") is { Length: > 0 } z ? z : null;
            // the window on the event's own clock, a day wider for zones far from the user's
            var (zf, zt) = (TimeZones.LocalToZone(f, zone, local).AddDays(-1), TimeZones.LocalToZone(t, zone, local).AddDays(1));
            foreach (var o in RecurrenceEngine.Occurrences(e, zf, zt))
            {
                var (s, end) = allDay ? (o.Start, o.End) : (TimeZones.ZoneToLocal(o.Start, zone, local), TimeZones.ZoneToLocal(o.End, zone, local));
                if (!(s < t && end > f) && !(s == end && s >= f && s < t)) continue;
                rows.Add((s, o.RecurrenceId is null ? null : o, e));
            }
        }

        var b = new PluginValueBuilder();
        return Success(b, b.Array([.. rows.OrderBy(r => r.Start).Take(MaxEvents).Select(r => PluginCalendarProjection.Event(b, r.Event, r.Occurrence, local))]));
    }

    /// <summary>A bound in UTC becomes the user's wall clock; one without a zone already is.</summary>
    private static DateTime OnUserClock(PluginWhen when, TimeZoneInfo local) =>
        when.Utc ? DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(when.Wall, DateTimeKind.Utc), local), DateTimeKind.Unspecified) : when.Wall;

    private static bool TryParse(PluginValue arguments, out PluginWhen from, out PluginWhen to, out HashSet<string>? calendars)
    {
        (from, to, calendars) = (default, default, null);
        if (!PluginMailArgs.TryOptions(PluginMailArgs.At(arguments, 0), Allowed, true, out var options)) return false;
        if (!PluginMailArgs.TryString(PluginMailArgs.Prop(options, "from"), 64, out var fromText) || !PluginMailArgs.TryString(PluginMailArgs.Prop(options, "to"), 64, out var toText)) return false;
        if (!PluginWhenParser.TryRead(fromText, out from) || !PluginWhenParser.TryRead(toText, out to) || from.Utc != to.Utc) return false;
        var span = to.WallMs - from.WallMs;
        if (span <= 0 || span > PluginTimingBuilder.MaxRangeDays * 86_400_000L) return false;
        if (PluginMailArgs.Prop(options, "calendarIds") is not { } ids) return true;
        if (ids.Kind != PluginValueKind.Array || ids.Count > MaxCalendars) return false;
        calendars = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < ids.Count; i++)
        {
            if (!PluginMailArgs.IsId(ids.Item(i))) return false;
            _ = calendars.Add(ids.Item(i).StringValue);
        }

        return true;
    }
}
