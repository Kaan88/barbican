using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Calendar.Feeds;
using Barbican.Core.Calendar.Recurrence;
using Barbican.Core.Calendar.Reminders;
using Barbican.Presentation.Calendar.Editors;
namespace Barbican.Presentation.Tests.Calendar.Editors;

internal sealed class FakeRules : ICalendarRules
{
    public EventForm? LastForm { get; private set; }

    public JsonNode? LastEvent { get; private set; }

    public (DateTime Start, DateTime End)? LastOccurrence { get; private set; }

    public List<FreeSlot> Slots { get; set; } = [];

    public EventData BuildData(EventForm form, JsonNode? ev, (DateTime Start, DateTime End)? occurrence)
    {
        LastForm = form;
        LastEvent = ev;
        LastOccurrence = occurrence;
        return new EventData(new JsonObject { ["title"] = form.Title.Trim() }, form.Send);
    }

    public CalendarPerson? ParsePerson(string text)
    {
        var t = text.Trim();
        var lt = t.IndexOf('<', StringComparison.Ordinal);
        var email = lt >= 0 ? t[(lt + 1)..].TrimEnd('>') : t;
        return email.Contains('@', StringComparison.Ordinal) && email.Contains('.', StringComparison.Ordinal) ? new CalendarPerson(lt > 0 ? t[..lt].Trim() : string.Empty, email) : null;
    }

    public string Initials(CalendarPerson person) => (person.Name.Length > 0 ? person.Name : person.Email)[..1].ToUpperInvariant();

    public IReadOnlyList<PersonMatch> Matching(IReadOnlyList<JsonNode> cards, string query, IReadOnlyList<string> have, int max) =>
        [.. cards.Where(c => ((string?)c["name"] ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase)).Select(c => new PersonMatch(new CalendarPerson((string?)c["name"] ?? string.Empty, (string?)c["email"] ?? string.Empty), [])).Take(max)];

    public IReadOnlyList<string> SearchZones(string query, int max) => ["Europe/Berlin", "America/New_York"];

    public (IReadOnlyList<AlertEditRow> Rows, JsonObject Kept) AlertRows(JsonNode? alerts)
    {
        var rows = new List<AlertEditRow>();
        if (alerts is JsonObject o)
        {
            foreach (var (id, a) in o)
            {
                var offset = (string?)a?["trigger"]?["offset"] ?? string.Empty;
                if (offset.StartsWith("-PT", StringComparison.Ordinal) && offset.EndsWith('M'))
                {
                    var row = new ReminderRow(long.Parse(offset[3..^1], System.Globalization.CultureInfo.InvariantCulture), 1, 0);
                    rows.Add(new AlertEditRow(row, new AlertSource(id, row)));
                }
            }
        }

        return (rows, []);
    }

    public string RowLabel(ReminderRow row) => $"{row.Value} before";

    public JsonNode? CalendarDefaults(JsonNode? calendar, bool allDay) => JsonNode.Parse("""{"d1":{"trigger":{"offset":"-PT10M"}}}""");

    public string AlertsLabel(JsonNode? alerts) => alerts is JsonObject { Count: > 0 } ? "10 minutes before" : "None";

    public string Summary(RecurrenceRule rule) => $"{rule.Freq}/{rule.Interval}/{string.Join(',', rule.ByDay.Select(d => d.Day))}";

    public bool IsSimple(RecurrenceRule rule) => rule.Count is null && rule.Until is null && rule.Interval == 1 && rule.ByDay.Count == 0;

    public RecurrenceRule ParseRule(JsonNode rule) => new() { Freq = (string?)rule["frequency"] ?? "daily", Interval = (long?)rule["interval"] ?? 1 };

    public FeedUrlCheck CheckFeed(string url) => url.StartsWith("http://", StringComparison.Ordinal) ? new FeedUrlCheck(url, null, "not encrypted") : new FeedUrlCheck(url, null, null);

    public bool HasAlerts(IReadOnlyList<JsonNode> events) => events.Any(e => e["alerts"] is JsonObject { Count: > 0 });

    public (string FileName, string Text) EventIcs(JsonNode ev) => ("event.ics", "BEGIN:VCALENDAR");

    public DateTime ZoneToLocal(DateTime wall, string? zone) => wall;

    public DateTime LocalToZone(DateTime local, string? zone) => local;

    public long ZoneToUtcMs(DateTime wall, string? zone) => new DateTimeOffset(DateTime.SpecifyKind(wall, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    public AvailabilityStatus AvailabilityFor(IReadOnlyList<(long Start, long End, bool Tentative)> periods, long fromMs, long untilMs) =>
        periods.Count == 0 ? new AvailabilityStatus("Free", 1) : new AvailabilityStatus("Busy", 3);

    public IReadOnlyList<FreeSlot> FreeSlots(DateTime now, long durationMs, IReadOnlyList<(long Start, long End)> busy, int max) => Slots;

    public bool UserOrganizes(JsonNode ev, IReadOnlyList<string> mine) => ev["participants"] is not JsonObject || ev["organizerCalendarAddress"] is null || ((string?)ev["organizerCalendarAddress"] ?? string.Empty).Contains("me@x.example", StringComparison.Ordinal);

    public IReadOnlyList<CalendarPerson> AttendeesOf(JsonNode ev) =>
        ev["participants"] is JsonObject o ? [.. o.Select(p => p.Value!).Where(p => p["roles"]?["owner"] is null).Select(p => new CalendarPerson((string?)p["name"] ?? string.Empty, (string?)p["email"] ?? string.Empty))] : [];

    public CalendarPerson? Organizer(JsonNode ev) => ev["organizerCalendarAddress"] is { } a ? new CalendarPerson("Ann Lee", a.ToString().Replace("mailto:", string.Empty, StringComparison.Ordinal)) : null;

    public string DescriptionText(JsonNode ev) => (string?)ev["description"] ?? string.Empty;
}
