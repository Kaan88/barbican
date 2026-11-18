// Mirrors: Rust cal_extra.rs:430 sanitize_import, :467 parse_ics; Bulwark components/calendar/ical-import-modal.tsx, lib/calendar-event-normalization.ts; B-CAL-045.

using System.Text.Json.Nodes;

namespace Barbican.Core.Calendar.Editing;

/// <summary>Cleaning an event that came out of an .ics file before it is stored.</summary>
public static class ImportRules
{
    private static readonly string[] Keep =
    [
        "@type", "uid", "title", "description", "descriptionContentType", "start", "timeZone", "showWithoutTime", "status", "freeBusyStatus",
        "privacy", "color", "keywords", "categories", "locale", "locations", "virtualLocations", "links", "recurrenceRules", "recurrenceOverrides",
        "excludedRecurrenceRules", "alerts", "due", "progress",
    ];

    private static readonly string[] ParticipantKeep =
    [
        "name", "email", "description", "kind", "roles", "participationStatus", "participationComment", "expectReply",
        "scheduleAgent", "scheduleForceSend", "scheduleId", "delegatedTo", "delegatedFrom", "memberOf",
    ];

    /// <summary>Drops id and unknown participant fields, sets calendarIds, turns replyTo/sendTo imip into organizerCalendarAddress and calendarAddress, makes a zero duration PT0S (P1D for an all-day event, which also loses its timeZone). Local alerts stay (a feed's do not: <c>FeedRules.SanitizeFeed</c>).</summary>
    public static JsonNode Sanitize(JsonNode source, string calendarId)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(calendarId);
        var d = new JsonObject();
        foreach (var key in Keep)
        {
            if (source[key] is { } v)
            {
                d[key] = v.DeepClone();
            }
        }

        var allDay = (bool?)source["showWithoutTime"] == true;
        var duration = (string?)source["duration"];
        if (!string.Equals((string?)source["@type"], "Task", StringComparison.Ordinal))
        {
            d["duration"] = (allDay, duration) switch
            {
                (true, { } x) when (CalendarTime.DurationSeconds(x) ?? 0) >= 86400 => x,
                (true, _) => "P1D",
                (false, { } x) => x,
                (false, null) => "PT0S",
            };
        }

        if (allDay)
        {
            d.Remove("timeZone");
        }

        if (((string?)source["organizerCalendarAddress"] ?? (string?)source["replyTo"]?["imip"]) is { } organizer)
        {
            d["organizerCalendarAddress"] = organizer;
        }

        if (source["participants"] is JsonObject participants)
        {
            d["participants"] = CleanParticipants(participants);
        }

        d["@type"] ??= "Event";
        d["calendarIds"] = new JsonObject { [calendarId] = true };
        return d;
    }

    private static JsonObject CleanParticipants(JsonObject participants)
    {
        var output = new JsonObject();
        foreach (var (id, p) in participants)
        {
            var q = new JsonObject { ["@type"] = "Participant" };
            if (p is not null)
            {
                foreach (var f in ParticipantKeep)
                {
                    if (p[f] is { } fv)
                    {
                        q[f] = fv.DeepClone();
                    }
                }

                if (((string?)p["calendarAddress"] ?? (string?)p["sendTo"]?["imip"]) is { } address)
                {
                    q["calendarAddress"] = address;
                }
            }

            output[id] = q;
        }

        return output;
    }

    /// <summary>Whether the events carry alerts of their own (the import dialog then asks whether to keep them, G-004; the file's alerts are kept by default as Bulwark keeps them, D-M4-09).</summary>
    public static bool HasAlerts(IReadOnlyList<JsonNode> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return events.Any(e => e["alerts"] is JsonObject { Count: > 0 });
    }
}
