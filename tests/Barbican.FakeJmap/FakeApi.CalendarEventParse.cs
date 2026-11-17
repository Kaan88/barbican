// Mirrors: JMAP Calendars CalendarEvent/parse (blobIds; parsed, notParsable, notFound); Stalwart crates/jmap/src/calendar_event/parse.rs (iCalendar
// to JSCalendar, RFC 8984: title, start + timeZone, duration, locations, participants with calendarAddress and roles); J-072 partial. A small
// reader of the VEVENT lines the seed uses (SUMMARY, DTSTART/DTEND with TZID or Z, LOCATION, ORGANIZER, ATTENDEE with CN and PARTSTAT, UID,
// STATUS); the iTIP METHOD stays in the .ics (Stalwart does not put it on the event). Answers only with FakeJmapOptions.CalendarsCapability. Owner reader.

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private (string? Name, JsonObject Args) CalendarEventParse(JsonObject args)
    {
        if (!options.CalendarsCapability)
        {
            return Error("unknownMethod");
        }

        var parsed = new JsonObject();
        var notParsable = new JsonArray();
        var notFound = new JsonArray();
        foreach (var id in (args["blobIds"] as JsonArray ?? []).Select(x => (string?)x).OfType<string>())
        {
            if (store.Blob(id) is not { } bytes)
            {
                notFound.Add(id);
            }
            else if (ParseVEvent(Encoding.UTF8.GetString(bytes)) is { } ev)
            {
                parsed[id] = new JsonArray(ev);
            }
            else
            {
                notParsable.Add(id);
            }
        }

        return (null, new JsonObject { ["accountId"] = options.AccountId, ["parsed"] = parsed, ["notParsable"] = notParsable, ["notFound"] = notFound });
    }

    private static JsonObject? ParseVEvent(string ics)
    {
        var lines = ics.Replace("\r\n ", string.Empty, StringComparison.Ordinal).Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var begin = lines.IndexOf("BEGIN:VEVENT");
        var end = lines.IndexOf("END:VEVENT");
        if (begin < 0 || end < begin)
        {
            return null;
        }

        var ev = new JsonObject { ["@type"] = "Event" };
        var participants = new JsonObject();
        DateTime? start = null;
        DateTime? stop = null;
        foreach (var line in lines.Skip(begin + 1).Take(end - begin - 1))
        {
            Apply(ev, participants, line, ref start, ref stop);
        }

        if (start is { } s)
        {
            ev["start"] = s.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
            if (stop is { } e && e > s)
            {
                var d = e - s;
                ev["duration"] = d.TotalDays >= 1 && d.Hours == 0 && d.Minutes == 0 ? $"P{(int)d.TotalDays}D" : $"PT{(int)d.TotalHours}H{d.Minutes}M";
            }
        }

        ev["participants"] = participants;
        return ev;
    }

    private static void Apply(JsonObject ev, JsonObject participants, string line, ref DateTime? start, ref DateTime? stop)
    {
        var colon = line.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            return;
        }

        var head = line[..colon].Split(';');
        var value = line[(colon + 1)..];
        var param = head.Skip(1).Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0].ToUpperInvariant(), p => p[1], StringComparer.Ordinal);
        switch (head[0].ToUpperInvariant())
        {
            case "UID":
                ev["uid"] = value;
                break;
            case "SUMMARY":
                ev["title"] = value;
                break;
            case "LOCATION":
                ev["locations"] = new JsonObject { ["l1"] = new JsonObject { ["@type"] = "Location", ["name"] = value } };
                break;
            case "STATUS":
                ev["status"] = value.ToLowerInvariant();
                break;
            case "DTSTART":
                start = IcsTime(value);
                ev["timeZone"] = value.EndsWith('Z') ? "Etc/UTC" : param.GetValueOrDefault("TZID");
                ev["showWithoutTime"] = string.Equals(param.GetValueOrDefault("VALUE"), "DATE", StringComparison.OrdinalIgnoreCase);
                break;
            case "DTEND":
                stop = IcsTime(value);
                break;
            case "ORGANIZER":
                ev["organizerCalendarAddress"] = value;
                participants["p" + (participants.Count + 1).ToString(CultureInfo.InvariantCulture)] = Participant(value, param, organizer: true);
                break;
            case "ATTENDEE":
                participants["p" + (participants.Count + 1).ToString(CultureInfo.InvariantCulture)] = Participant(value, param, organizer: false);
                break;
        }
    }

    private static JsonObject Participant(string address, Dictionary<string, string> param, bool organizer)
    {
        var p = new JsonObject { ["@type"] = "Participant", ["calendarAddress"] = address };
        if (param.TryGetValue("CN", out var cn))
        {
            p["name"] = cn.Trim('"');
        }

        p["roles"] = organizer ? new JsonObject { ["owner"] = true } : new JsonObject { ["attendee"] = true };
        p["participationStatus"] = param.TryGetValue("PARTSTAT", out var st) ? st.ToLowerInvariant() : organizer ? "accepted" : "needs-action";
        return p;
    }

    private static DateTime? IcsTime(string value) =>
        DateTime.TryParseExact(value.TrimEnd('Z'), ["yyyyMMdd'T'HHmmss", "yyyyMMdd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;
}
