// Mirrors: Bulwark lib/plugin-projection.ts:105-132 eventToView, :163-194 taskToView, lib/plugin-sandbox/host-api-calendar.ts:207-216 calendarToView and
// lib/plugin-types.ts:534-573 (CalendarEventView, CalendarView), :739-790 (TaskView and its alert, link and relation views). Every field is named here: nothing is copied by
// reflection, so a field added to a Barbican calendar object never reaches a plugin. Rights, sharing details, colours of other apps and server-only fields stay out.
using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Recurrence;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Calendar;

internal static class PluginCalendarProjection
{
    /// <summary>A year of events is at most a thousand rows of a few hundred characters.</summary>
    internal static readonly PluginValueLimits Limits = new(MaxWireBytes: 24 * 1024 * 1024, MaxNodes: 300_000, MaxStringBytes: 2 * 1024 * 1024);

    internal static IReadOnlyList<string> TrueKeys(JsonNode? map) =>
        map is JsonObject o ? [.. o.Where(x => x.Value is JsonValue v && v.TryGetValue<bool>(out var b) && b).Select(x => x.Key)] : [];

    internal static string FirstTrueCalendar(JsonNode ev) => TrueKeys(ev.Obj("calendarIds")) is { Count: > 0 } keys ? keys[0] : "";

    /// <summary>host-api-calendar.ts:207-216. Barbican has no task-only calendars (a task goes to the login's default calendar, like the Tasks view's), so isTasksOnly is false.</summary>
    internal static int Calendar(PluginValueBuilder b, JsonNode calendar) => b.Object(
        ("id", b.String(calendar.Str("id") ?? "")), ("name", b.String(calendar.Str("name") ?? "")), ("color", b.String(calendar.Str("color") ?? "")),
        ("isVisible", b.Boolean(calendar.Child("isVisible") is not JsonValue v || !v.TryGetValue<bool>(out var visible) || visible)),
        ("isDefault", b.Boolean(calendar.IsTrue("isDefault"))), ("isTasksOnly", b.Boolean(false)));

    /// <summary>plugin-projection.ts:105-132. <paramref name="occurrence"/> is one expanded occurrence of a series (its id is "id|recurrenceId", its start and length are its own); null for a single event.</summary>
    internal static int Event(PluginValueBuilder b, JsonNode ev, Occurrence? occurrence, TimeZoneInfo local)
    {
        var id = ev.Str("id") ?? "";
        var allDay = ev.IsTrue("showWithoutTime");
        var zone = ev.Str("timeZone") is { Length: > 0 } z ? z : null;
        var patch = occurrence?.Patch;
        var location = occurrence is not null && patch is JsonObject po && po.ContainsKey("locations") ? CalendarOccurrences.LocationOf(patch) : CalendarOccurrences.LocationOf(ev);
        var start = occurrence is null ? ev.Str("start") ?? "" : RecurrenceEngine.FormatLocal(occurrence.Start);
        var seconds = occurrence is null ? (long)RecurrenceEngine.EventDuration(ev).TotalSeconds : (long)(occurrence.End - occurrence.Start).TotalSeconds;
        var duration = occurrence is null ? ev.Str("duration") ?? "PT0S" : RecurrenceEngine.DurationString(seconds);
        var (utcStart, utcEnd) = Utc(ev, occurrence, allDay, zone, start, seconds, local);
        var fields = new List<(string, int)>
        {
            ("id", b.String(occurrence?.RecurrenceId is { } r ? id + "|" + r : id)), ("uid", b.String(ev.Str("uid") ?? "")), ("calendarId", b.String(FirstTrueCalendar(ev))),
            ("title", b.String(patch.Str("title") is { Length: > 0 } t ? t : ev.Str("title") ?? "")),
            ("description", b.String(patch.Str("description") is { } pd ? pd : ev.Str("description") ?? "")),
            ("start", b.String(start)), ("duration", b.String(duration)), ("timeZone", zone is null ? b.Null() : b.String(zone)),
            ("utcStart", utcStart is null ? b.Null() : b.String(utcStart)), ("utcEnd", utcEnd is null ? b.Null() : b.String(utcEnd)),
            ("isAllDay", b.Boolean(allDay)), ("location", b.String(location)), ("status", b.String(ev.Str("status") ?? "confirmed")),
            ("participants", b.Array([.. (ev.Obj("participants") ?? []).Select(p => Participant(b, p.Value))])),
            ("isRecurring", b.Boolean(ev.Arr("recurrenceRules") is { Count: > 0 })), ("keywords", Strings(b, TrueKeys(ev.Obj("keywords")))),
        };
        return b.Object([.. fields]);
    }

    /// <summary>plugin-projection.ts:163-194.</summary>
    internal static int Task(PluginValueBuilder b, JsonNode task)
    {
        var progress = task.Str("progress") ?? "needs-action";
        return b.Object(
            ("id", b.String(task.Str("id") ?? "")), ("uid", b.String(task.Str("uid") ?? "")), ("title", b.String(task.Str("title") ?? "")),
            ("description", b.String(task.Str("description") ?? "")), ("isComplete", b.Boolean(string.Equals(progress, "completed", StringComparison.Ordinal))),
            ("progress", b.String(progress)), ("dueDate", task.Str("due") is { } due ? b.String(due) : b.Null()),
            ("priority", b.Number(task.Long("priority") ?? 0)), ("calendarId", b.String(FirstTrueCalendar(task))),
            ("keywords", Strings(b, TrueKeys(task.Obj("keywords")))), ("alerts", Alerts(b, task.Obj("alerts"))),
            ("useDefaultAlerts", b.Boolean(task.IsTrue("useDefaultAlerts"))), ("links", Links(b, task.Obj("links"))), ("relatedTo", Relations(b, task.Obj("relatedTo"))));
    }

    private static int Participant(PluginValueBuilder b, JsonNode? p) => b.Object(
        ("name", b.String(p.Str("name") ?? "")), ("email", b.String(Email(p))),
        ("role", b.String(string.Join(',', TrueKeys(p.Child("roles"))))), ("status", b.String(p.Str("participationStatus") ?? "")));

    private static string Email(JsonNode? p)
    {
        var address = p.Str("email") ?? p.Str("calendarAddress") ?? p.Child("sendTo").Str("imip") ?? "";
        return address.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? address[7..] : address;
    }

    private static int Alerts(PluginValueBuilder b, JsonObject? alerts) => b.Object([.. (alerts ?? []).Where(a => a.Value.Child("trigger") is not null).Select(a =>
    {
        var trigger = a.Value.Child("trigger");
        var view = string.Equals(trigger.Str("@type"), "AbsoluteTrigger", StringComparison.Ordinal)
            ? b.Object(("at", b.String(trigger.Str("when") ?? "")))
            : b.Object(("offset", b.String(trigger.Str("offset") ?? "")), ("relativeTo", b.String(string.Equals(trigger.Str("relativeTo"), "end", StringComparison.Ordinal) ? "due" : "start")));
        return (a.Key, b.Object(("trigger", view), ("action", b.String(string.Equals(a.Value.Str("action"), "email", StringComparison.Ordinal) ? "email" : "display")),
            ("acknowledged", a.Value.Str("acknowledged") is { Length: > 0 } ack ? b.String(ack) : b.Null())));
    })]);

    private static int Links(PluginValueBuilder b, JsonObject? links) => b.Object([.. (links ?? []).Select(l => (l.Key, b.Object(
        ("href", b.String(l.Value.Str("href") ?? "")), ("rel", l.Value.Str("rel") is { Length: > 0 } rel ? b.String(rel) : b.Null()),
        ("title", l.Value.Str("title") is { Length: > 0 } title ? b.String(title) : b.Null()),
        ("contentType", l.Value.Str("contentType") is { Length: > 0 } type ? b.String(type) : b.Null()))))]);

    private static int Relations(PluginValueBuilder b, JsonObject? relations) =>
        b.Object([.. (relations ?? []).Select(r => (r.Key, b.Object(("relation", Strings(b, TrueKeys(r.Value.Child("relation")))))))]);

    private static int Strings(PluginValueBuilder b, IReadOnlyList<string> values) => b.Array([.. values.Select(b.String)]);

    /// <summary>The server's utcStart and utcEnd for a single event; computed from the event's zone for an occurrence. All-day events and floating ones have none to give.</summary>
    private static (string? Start, string? End) Utc(JsonNode ev, Occurrence? occurrence, bool allDay, string? zone, string start, long seconds, TimeZoneInfo local)
    {
        if (occurrence is null && ev.Str("utcStart") is { Length: > 0 } us)
        {
            return (us, ev.Str("utcEnd") is { Length: > 0 } ue ? ue : null);
        }

        if (allDay || zone is null || RecurrenceEngine.ParseLocal(start) is not { } wall) return (null, null);
        var ms = TimeZones.ZoneToUtcMs(wall, zone, local);
        return (FormatUtc(ms), FormatUtc(ms + (seconds * 1000)));
    }

    private static string FormatUtc(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
