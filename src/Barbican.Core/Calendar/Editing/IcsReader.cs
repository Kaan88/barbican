// Mirrors: Rust cal_extra.rs:467 parse_ics (the server's CalendarEvent/parse of an uploaded file); Bulwark components/calendar/ical-import-modal.tsx; RFC 5545 3.1 (unfolding), 3.3 (value types), 3.6.1 VEVENT, 3.6.2 VTODO, 3.6.6 VALARM, 3.8.5.3 RRULE; RFC 8984 (JSCalendar) for the result.
// A small reader for what the import and the webcal feeds need (VEVENT and VTODO with their alarms): D-M4-E01 explains why it is not Ical.Net.

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Barbican.Core.Calendar.Editing;

/// <summary>An .ics text as JSCalendar events and tasks.</summary>
public static class IcsReader
{
    private static readonly Dictionary<string, string> NoParams = [];

    private static readonly string[] Days = ["su", "mo", "tu", "we", "th", "fr", "sa"];

    /// <summary>The events and tasks of the text in file order; a component without a start (a task without a due date either) cannot be shown and is skipped.</summary>
    public static IReadOnlyList<JsonNode> Parse(string ics)
    {
        ArgumentNullException.ThrowIfNull(ics);
        var result = new List<JsonNode>();
        Component? current = null;
        Component? alarm = null;
        foreach (var line in Unfold(ics))
        {
            var prop = Property.Read(line);
            if (prop is null)
            {
                continue;
            }

            if (string.Equals(prop.Name, "BEGIN", StringComparison.Ordinal))
            {
                var kind = prop.Value.ToUpperInvariant();
                if (current is not null && string.Equals(kind, "VALARM", StringComparison.Ordinal))
                {
                    alarm = new Component(kind);
                }
                else if (current is null && kind is "VEVENT" or "VTODO")
                {
                    current = new Component(kind);
                }

                continue;
            }

            if (string.Equals(prop.Name, "END", StringComparison.Ordinal))
            {
                var kind = prop.Value.ToUpperInvariant();
                if (alarm is not null && string.Equals(kind, "VALARM", StringComparison.Ordinal))
                {
                    current?.Alarms.Add(alarm);
                    alarm = null;
                }
                else if (current is not null && string.Equals(kind, current.Kind, StringComparison.Ordinal))
                {
                    if (Convert(current) is { } node)
                    {
                        result.Add(node);
                    }

                    current = null;
                }

                continue;
            }

            (alarm ?? current)?.Properties.Add(prop);
        }

        return result;
    }

    private static List<string> Unfold(string text)
    {
        var lines = new List<string>();
        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t') && lines.Count > 0)
            {
                lines[^1] += raw[1..];
            }
            else if (raw.Length > 0)
            {
                lines.Add(raw);
            }
        }

        return lines;
    }

    private static JsonObject? Convert(Component c)
    {
        var task = string.Equals(c.Kind, "VTODO", StringComparison.Ordinal);
        var ev = new JsonObject { ["@type"] = task ? "Task" : "Event" };
        TextFields(c, ev);
        TimeFields(c, ev, task);
        Recurrence(c, ev);
        Participants(c, ev);
        Alarms(c, ev);
        return ev["start"] is null && ev["due"] is null ? null : ev;
    }

    private static void TextFields(Component c, JsonObject ev)
    {
        if (c.First("UID") is { } uid)
        {
            ev["uid"] = uid.Value.Trim();
        }

        ev["title"] = Text(c.First("SUMMARY")?.Value ?? "");
        if (c.First("DESCRIPTION") is { } description && description.Value.Length > 0)
        {
            ev["description"] = Text(description.Value);
        }

        if (c.First("LOCATION") is { Value.Length: > 0 } location)
        {
            ev["locations"] = new JsonObject { ["l1"] = new JsonObject { ["@type"] = "Location", ["name"] = Text(location.Value) } };
        }

        if (c.First("STATUS") is { } status)
        {
            ev["status"] = status.Value.Trim().ToLowerInvariant();
        }

        if (string.Equals(c.First("TRANSP")?.Value.Trim(), "TRANSPARENT", StringComparison.OrdinalIgnoreCase))
        {
            ev["freeBusyStatus"] = "free";
        }

        if (c.First("CLASS") is { } cls)
        {
            var privacy = cls.Value.Trim().ToUpperInvariant() switch { "PRIVATE" => "private", "CONFIDENTIAL" => "secret", _ => null };
            if (privacy is not null)
            {
                ev["privacy"] = privacy;
            }
        }

        if (c.First("CATEGORIES") is { Value.Length: > 0 } categories)
        {
            var keywords = new JsonObject();
            foreach (var k in categories.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                keywords[Text(k)] = true;
            }

            ev["keywords"] = keywords;
        }

    }

    private static void TimeFields(Component c, JsonObject ev, bool task)
    {
        var start = c.First("DTSTART");
        DateValue? s = start is null ? null : DateValue.Read(start);
        if (s is not null)
        {
            ev["start"] = s.Local;
            if (s.AllDay)
            {
                ev["showWithoutTime"] = true;
            }

            if (s.Zone is not null)
            {
                ev["timeZone"] = s.Zone;
            }
        }

        if (task)
        {
            if (c.First("DUE") is { } due && DateValue.Read(due) is { } d)
            {
                ev["due"] = d.Local;
                ev["start"] ??= d.Local;
                if (d.Zone is not null)
                {
                    ev["timeZone"] ??= d.Zone;
                }
            }

            ev["progress"] = TaskProgress(c);
        }
        else
        {
            var duration = c.First("DURATION") is { } dur ? dur.Value.Trim() : null;
            if (duration is null && s is not null && c.First("DTEND") is { } end && DateValue.Read(end) is { } e)
            {
                duration = DurationBetween(s, e);
            }

            if (duration is not null)
            {
                ev["duration"] = duration;
            }
        }

    }

    private static void Recurrence(Component c, JsonObject ev)
    {
        var rules = new JsonArray();
        foreach (var r in c.All("RRULE"))
        {
            if (Rule(r.Value) is { } rule)
            {
                rules.Add((JsonNode)rule);
            }
        }

        if (rules.Count > 0)
        {
            ev["recurrenceRules"] = rules;
        }

        var excluded = new JsonObject();
        foreach (var x in c.All("EXDATE"))
        {
            foreach (var one in x.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (DateValue.Read(new Property("EXDATE", x.Params, one)) is { } d)
                {
                    excluded[d.Local] = new JsonObject { ["excluded"] = true };
                }
            }
        }

        if (excluded.Count > 0)
        {
            ev["recurrenceOverrides"] = excluded;
        }

    }

    private static string TaskProgress(Component c)
    {
        var status = c.First("STATUS")?.Value.Trim().ToUpperInvariant();
        if (string.Equals(status, "COMPLETED", StringComparison.Ordinal) || c.First("COMPLETED") is not null)
        {
            return "completed";
        }

        return status switch { "IN-PROCESS" => "in-process", "CANCELLED" => "failed", _ => "needs-action" };
    }

    private static string DurationBetween(DateValue start, DateValue end)
    {
        if (!DateTime.TryParse(start.Local, CultureInfo.InvariantCulture, DateTimeStyles.None, out var a) || !DateTime.TryParse(end.Local, CultureInfo.InvariantCulture, DateTimeStyles.None, out var b) || b <= a)
        {
            return start.AllDay ? "P1D" : "PT0S";
        }

        var span = b - a;
        if (start.AllDay)
        {
            return string.Create(CultureInfo.InvariantCulture, $"P{(int)span.TotalDays}D");
        }

        var sb = new StringBuilder("P");
        if (span.Days > 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{span.Days}D");
        }

        if (span.Hours + span.Minutes + span.Seconds > 0 || span.Days == 0)
        {
            sb.Append('T');
            if (span.Hours > 0)
            {
                sb.Append(CultureInfo.InvariantCulture, $"{span.Hours}H");
            }

            if (span.Minutes > 0)
            {
                sb.Append(CultureInfo.InvariantCulture, $"{span.Minutes}M");
            }

            if (span.Seconds > 0)
            {
                sb.Append(CultureInfo.InvariantCulture, $"{span.Seconds}S");
            }
        }

        return sb.ToString();
    }

    private static JsonObject? Rule(string text)
    {
        var parts = text.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim().ToUpperInvariant(), p => p[1].Trim(), StringComparer.Ordinal);
        if (!parts.TryGetValue("FREQ", out var freq) || freq.Length == 0)
        {
            return null;
        }

        var rule = new JsonObject { ["@type"] = "RecurrenceRule", ["frequency"] = freq.ToLowerInvariant() };
        if (parts.TryGetValue("INTERVAL", out var interval) && int.TryParse(interval, CultureInfo.InvariantCulture, out var i) && i > 1)
        {
            rule["interval"] = i;
        }

        if (parts.TryGetValue("COUNT", out var count) && int.TryParse(count, CultureInfo.InvariantCulture, out var n))
        {
            rule["count"] = n;
        }

        if (parts.TryGetValue("UNTIL", out var until) && DateValue.Read(new Property("UNTIL", NoParams, until)) is { } u)
        {
            rule["until"] = u.Local;
        }

        if (parts.TryGetValue("BYDAY", out var byDay) && ByDay(byDay) is { Count: > 0 } days)
        {
            rule["byDay"] = days;
        }

        Numbers(parts, "BYMONTHDAY", rule, "byMonthDay");
        Numbers(parts, "BYSETPOS", rule, "bySetPosition");
        Numbers(parts, "BYYEARDAY", rule, "byYearDay");
        Numbers(parts, "BYHOUR", rule, "byHour");
        Numbers(parts, "BYMINUTE", rule, "byMinute");
        if (parts.TryGetValue("BYMONTH", out var months))
        {
            rule["byMonth"] = new JsonArray([.. months.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(m => (JsonNode)JsonValue.Create(m.Trim())!)]);
        }

        if (parts.TryGetValue("WKST", out var wkst) && wkst.Length == 2)
        {
            rule["firstDayOfWeek"] = wkst.ToLowerInvariant();
        }

        return rule;
    }

    private static JsonArray ByDay(string text)
    {
        var days = new JsonArray();
        foreach (var d in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var token = d.Trim();
            if (token.Length < 2 || Array.IndexOf(Days, token[^2..].ToLowerInvariant()) < 0)
            {
                continue;
            }

            var nday = new JsonObject { ["@type"] = "NDay", ["day"] = token[^2..].ToLowerInvariant() };
            if (token.Length > 2 && int.TryParse(token[..^2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var nth) && nth != 0)
            {
                nday["nthOfPeriod"] = nth;
            }

            days.Add((JsonNode)nday);
        }

        return days;
    }

    private static void Numbers(Dictionary<string, string> parts, string key, JsonObject rule, string name)
    {
        if (!parts.TryGetValue(key, out var text))
        {
            return;
        }

        var list = new JsonArray();
        foreach (var t in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(t.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v))
            {
                list.Add((JsonNode)JsonValue.Create(v));
            }
        }

        if (list.Count > 0)
        {
            rule[name] = list;
        }
    }

    private static void Participants(Component c, JsonObject ev)
    {
        var participants = new JsonObject();
        if (c.First("ORGANIZER") is { } org)
        {
            var address = Address(org.Value);
            ev["organizerCalendarAddress"] = "mailto:" + address;
            participants["p1"] = Participant(address, org.Params, organizer: true);
        }

        foreach (var a in c.All("ATTENDEE"))
        {
            var address = Address(a.Value);
            if (participants.Any(p => string.Equals((string?)p.Value?["email"], address, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            participants["p" + (participants.Count + 1).ToString(CultureInfo.InvariantCulture)] = Participant(address, a.Params, organizer: false);
        }

        if (participants.Count > 0)
        {
            ev["participants"] = participants;
        }
    }

    private static string Address(string value) => value.Trim().Replace("mailto:", "", StringComparison.OrdinalIgnoreCase).Trim();

    private static JsonObject Participant(string address, IReadOnlyDictionary<string, string> p, bool organizer)
    {
        var o = new JsonObject { ["@type"] = "Participant", ["email"] = address, ["calendarAddress"] = "mailto:" + address };
        if (p.TryGetValue("CN", out var cn) && cn.Length > 0)
        {
            o["name"] = cn;
        }

        var roles = new JsonObject();
        if (organizer)
        {
            roles["owner"] = true;
        }

        if (!organizer || p.ContainsKey("PARTSTAT"))
        {
            roles["attendee"] = true;
        }

        o["roles"] = roles;
        o["participationStatus"] = p.TryGetValue("PARTSTAT", out var st) ? st.ToLowerInvariant() : organizer ? "accepted" : "needs-action";
        if (p.TryGetValue("RSVP", out var rsvp) && string.Equals(rsvp, "TRUE", StringComparison.OrdinalIgnoreCase))
        {
            o["expectReply"] = true;
        }

        return o;
    }

    private static void Alarms(Component c, JsonObject ev)
    {
        var alerts = new JsonObject();
        foreach (var a in c.Alarms)
        {
            if (a.First("TRIGGER") is not { } trigger)
            {
                continue;
            }

            var value = trigger.Value.Trim();
            JsonObject t;
            if (trigger.Params.TryGetValue("VALUE", out var vt) && string.Equals(vt, "DATE-TIME", StringComparison.OrdinalIgnoreCase))
            {
                if (DateValue.Read(trigger) is not { } at)
                {
                    continue;
                }

                t = new JsonObject { ["@type"] = "AbsoluteTrigger", ["when"] = at.Local + "Z" };
            }
            else
            {
                var negative = value.StartsWith('-');
                var span = value.TrimStart('+', '-');
                var relative = trigger.Params.TryGetValue("RELATED", out var rel) && string.Equals(rel, "END", StringComparison.OrdinalIgnoreCase) ? "end" : "start";
                t = new JsonObject { ["@type"] = "OffsetTrigger", ["offset"] = (negative ? "-" : "") + span, ["relativeTo"] = relative };
            }

            var action = string.Equals(a.First("ACTION")?.Value.Trim(), "EMAIL", StringComparison.OrdinalIgnoreCase) ? "email" : "display";
            alerts["a" + (alerts.Count + 1).ToString(CultureInfo.InvariantCulture)] = new JsonObject { ["@type"] = "Alert", ["trigger"] = t, ["action"] = action };
        }

        if (alerts.Count > 0)
        {
            ev["alerts"] = alerts;
        }
    }

    internal static string Text(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
        {
            return value;
        }

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                i++;
                _ = sb.Append(value[i] is 'n' or 'N' ? '\n' : value[i]);
            }
            else
            {
                _ = sb.Append(value[i]);
            }
        }

        return sb.ToString();
    }

    private sealed class Component(string kind)
    {
        public string Kind { get; } = kind;

        public List<Property> Properties { get; } = [];

        public List<Component> Alarms { get; } = [];

        public Property? First(string name) => Properties.Find(p => string.Equals(p.Name, name, StringComparison.Ordinal));

        public IEnumerable<Property> All(string name) => Properties.Where(p => string.Equals(p.Name, name, StringComparison.Ordinal));
    }

    private sealed record Property(string Name, IReadOnlyDictionary<string, string> Params, string Value)
    {
        public static Property? Read(string line)
        {
            // the first colon outside a quoted parameter value splits name and value
            var quoted = false;
            var colon = -1;
            for (var i = 0; i < line.Length; i++)
            {
                if (line[i] == '"')
                {
                    quoted = !quoted;
                }
                else if (line[i] == ':' && !quoted)
                {
                    colon = i;
                    break;
                }
            }

            if (colon <= 0)
            {
                return null;
            }

            var head = line[..colon].Split(';');
            var ps = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in head.Skip(1))
            {
                var eq = p.IndexOf('=', StringComparison.Ordinal);
                if (eq > 0)
                {
                    ps[p[..eq].Trim().ToUpperInvariant()] = p[(eq + 1)..].Trim().Trim('"');
                }
            }

            return new Property(head[0].Trim().ToUpperInvariant(), ps, line[(colon + 1)..]);
        }
    }

    /// <summary>A DATE or DATE-TIME as the JSCalendar local text, its zone (UTC is "Etc/UTC", floating is none) and whether it is a date only.</summary>
    private sealed record DateValue(string Local, string? Zone, bool AllDay)
    {
        public static DateValue? Read(Property p)
        {
            var text = p.Value.Trim();
            var dateOnly = (p.Params.TryGetValue("VALUE", out var v) && string.Equals(v, "DATE", StringComparison.OrdinalIgnoreCase)) || text.Length == 8;
            var utc = text.EndsWith('Z');
            var core = text.TrimEnd('Z');
            if (dateOnly)
            {
                return DateTime.TryParseExact(core[..Math.Min(8, core.Length)], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                    ? new DateValue(d.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture), null, true)
                    : null;
            }

            if (!DateTime.TryParseExact(core, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            {
                return null;
            }

            var zone = utc ? "Etc/UTC" : p.Params.TryGetValue("TZID", out var tz) && tz.Length > 0 ? tz : null;
            return new DateValue(t.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture), zone, false);
        }
    }
}
