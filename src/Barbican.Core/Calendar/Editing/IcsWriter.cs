// Mirrors: Rust calendar.rs:981 ics_escape, :986 ics_fold, :999 ics_local, :1002 event_ics; Bulwark lib/calendar-ics-export.ts; RFC 5545 3.1 (folding), 3.3.11 (escaping). I-328 "Export as .ics".

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Barbican.Core.Calendar.Editing;

/// <summary>One event as an .ics file.</summary>
public static class IcsWriter
{
    /// <summary>(file name, text) of a VCALENDAR with one VEVENT: UID, DTSTAMP, SUMMARY, DTSTART and DTEND (TZID or floating, VALUE=DATE for all-day), LOCATION, DESCRIPTION, RRULE, lines folded at 75 octets, CRLF.</summary>
    public static (string FileName, string Text) Event(JsonNode ev) => Event(ev, TimeProvider.System.GetUtcNow());

    /// <summary>As <see cref="Event(JsonNode)"/> with the stamp given (tests).</summary>
    internal static (string FileName, string Text) Event(JsonNode ev, DateTimeOffset stamp)
    {
        ArgumentNullException.ThrowIfNull(ev);
        var lines = new List<string> { "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//Barbican//EN", "CALSCALE:GREGORIAN", "BEGIN:VEVENT" };
        lines.Add("UID:" + ((string?)ev["uid"] ?? (string?)ev["id"] ?? ""));
        lines.Add("DTSTAMP:" + stamp.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture));
        var start = (string?)ev["start"] ?? "";
        var tz = (string?)ev["timeZone"] is { Length: > 0 } z ? z : null;
        var utc = tz is "Etc/UTC" or "UTC";
        lines.AddRange(TimeLines(ev, start, tz, utc));

        lines.Add("SUMMARY:" + Escape((string?)ev["title"] ?? ""));
        if ((string?)ev["description"] is { Length: > 0 } description)
        {
            lines.Add("DESCRIPTION:" + Escape(description));
        }

        if ((ev["locations"] as JsonObject)?.Select(kv => (string?)kv.Value?["name"]).FirstOrDefault() is { } location)
        {
            lines.Add("LOCATION:" + Escape(location));
        }

        if ((string?)ev["status"] is { } status)
        {
            lines.Add("STATUS:" + status.ToUpperInvariant());
        }

        if (ev["recurrenceRules"] is JsonArray rules)
        {
            foreach (var rule in rules)
            {
                if (rule is not null)
                {
                    lines.Add("RRULE:" + Rule(rule, utc));
                }
            }
        }

        lines.AddRange(PeopleLines(ev["participants"] as JsonObject));

        lines.Add("END:VEVENT");
        lines.Add("END:VCALENDAR");
        var title = (string?)ev["title"] is { Length: > 0 } t ? t : "event";
        return (FileName(title) + ".ics", string.Concat(lines.Select(Fold)));
    }

    private static IEnumerable<string> TimeLines(JsonNode ev, string start, string? tz, bool utc)
    {
        if ((bool?)ev["showWithoutTime"] == true)
        {
            yield return "DTSTART;VALUE=DATE:" + Local(start)[..Math.Min(8, Local(start).Length)];
            if (CalendarTime.ParseLocal(start) is { } st)
            {
                var d = CalendarTime.EventDuration(ev);
                var end = (st + (d.TotalDays < 1 ? TimeSpan.FromDays(1) : d)).Date;
                yield return "DTEND;VALUE=DATE:" + end.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            }

            yield break;
        }

        yield return tz switch
        {
            null => "DTSTART:" + Local(start),
            _ when utc => "DTSTART:" + Local(start) + "Z",
            _ => $"DTSTART;TZID={tz}:{Local(start)}",
        };
        if ((string?)ev["duration"] is { } duration)
        {
            yield return "DURATION:" + duration;
        }
    }

    private static IEnumerable<string> PeopleLines(JsonObject? participants)
    {
        foreach (var (_, p) in participants ?? [])
        {
            if (p is null || Address(p) is not { Length: > 0 } address)
            {
                continue;
            }

            var mailto = "mailto:" + address;
            var cn = (string?)p["name"] is { Length: > 0 } name ? $";CN=\"{name.Replace("\"", "", StringComparison.Ordinal)}\"" : "";
            var owner = (bool?)p["roles"]?["owner"] == true;
            if (owner)
            {
                yield return $"ORGANIZER{cn}:{mailto}";
            }

            if ((bool?)p["roles"]?["attendee"] == true || !owner)
            {
                yield return $"ATTENDEE{cn};PARTSTAT={((string?)p["participationStatus"] ?? "needs-action").ToUpperInvariant()}:{mailto}";
            }
        }
    }

    private static string Rule(JsonNode r, bool utc)
    {
        var parts = new List<string> { "FREQ=" + ((string?)r["frequency"] ?? "daily").ToUpperInvariant() };
        if ((long?)r["interval"] is > 1 and var interval)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"INTERVAL={interval}"));
        }

        if ((long?)r["count"] is { } count)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"COUNT={count}"));
        }

        if ((string?)r["until"] is { } until)
        {
            parts.Add("UNTIL=" + Local(until) + (utc ? "Z" : ""));
        }

        if (r["byDay"] is JsonArray days)
        {
            var d = days.Select(x => (long?)x?["nthOfPeriod"] is { } n ? n.ToString(CultureInfo.InvariantCulture) + ((string?)x["day"] ?? "").ToUpperInvariant() : ((string?)x?["day"] ?? "").ToUpperInvariant())
                .Where(x => x.Length > 0).ToList();
            if (d.Count > 0)
            {
                parts.Add("BYDAY=" + string.Join(',', d));
            }
        }

        if (r["byMonthDay"] is JsonArray monthDays)
        {
            parts.Add("BYMONTHDAY=" + string.Join(',', monthDays.Select(x => x?.ToJsonString() ?? "")));
        }

        if (r["byMonth"] is JsonArray months)
        {
            parts.Add("BYMONTH=" + string.Join(',', months.Select(x => (string?)x ?? x?.ToJsonString()).OfType<string>()));
        }

        if (r["bySetPosition"] is JsonArray positions)
        {
            parts.Add("BYSETPOS=" + string.Join(',', positions.Select(x => x?.ToJsonString() ?? "")));
        }

        return string.Join(';', parts);
    }

    private static string? Address(JsonNode p) =>
        ((string?)p["calendarAddress"] ?? (string?)p["sendTo"]?["imip"] ?? (string?)p["email"])?.Trim().Replace("mailto:", "", StringComparison.OrdinalIgnoreCase);

    internal static string Escape(string s) =>
        s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(";", "\\;", StringComparison.Ordinal).Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("\r\n", "\\n", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    /// <summary>Folds a content line at 75 octets, never inside a UTF-8 sequence (RFC 5545 3.1); the continuation space counts.</summary>
    internal static string Fold(string line)
    {
        var o = new StringBuilder();
        var n = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var l = rune.Utf8SequenceLength;
            if (n + l > 75)
            {
                o.Append("\r\n ");
                n = 1;
            }

            o.Append(rune.ToString());
            n += l;
        }

        return o.Append("\r\n").ToString();
    }

    private static string Local(string s) => new(s.Where(c => c is not ('-' or ':')).Take(15).ToArray());

    private static string FileName(string title)
    {
        var bad = Path.GetInvalidFileNameChars();
        var cleaned = new string(title.Select(c => bad.Contains(c) || c is '/' or '\\' ? '_' : c).ToArray()).Trim(' ', '.');
        return cleaned.Length == 0 ? "event" : cleaned.Length > 80 ? cleaned[..80] : cleaned;
    }
}
