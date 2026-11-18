using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Calendar.Feeds;
using Barbican.Core.Calendar.Recurrence;
using Barbican.Core.Calendar.Reminders;
namespace Barbican.Presentation.Calendar.Editors;

/// <summary>The default rules: the real statics of Core.</summary>
public sealed class CalendarRules(Barbican.Core.Storage.IPrefsStore? prefs = null) : ICalendarRules
{
    // the timeZone setting (S-timeZone), else the machine's zone
    private TimeZoneInfo? Zone() => prefs is null ? null : Barbican.Core.Calendar.CalendarSettings.Read(prefs.Current).Zone();

    public EventData BuildData(EventForm form, JsonNode? ev, (DateTime Start, DateTime End)? occurrence) => EventEditor.BuildData(form, ev, occurrence);

    public CalendarPerson? ParsePerson(string text) => CalendarPeople.Parse(text);

    public string Initials(CalendarPerson person)
    {
        ArgumentNullException.ThrowIfNull(person);
        return CalendarPeople.Initials(person.Name, person.Email);
    }

    public IReadOnlyList<PersonMatch> Matching(IReadOnlyList<JsonNode> cards, string query, IReadOnlyList<string> have, int max) => CalendarPeople.Matching(cards, query, have, max);

    public IReadOnlyList<string> SearchZones(string query, int max) => TimeZones.Search(query, max);

    public (IReadOnlyList<AlertEditRow> Rows, JsonObject Kept) AlertRows(JsonNode? alerts) => ReminderRules.RowsOf(alerts);

    public string RowLabel(ReminderRow row) => ReminderRules.RowLabel(row);

    public JsonNode? CalendarDefaults(JsonNode? calendar, bool allDay) => ReminderRules.CalendarDefaults(calendar, allDay);

    public string AlertsLabel(JsonNode? alerts) => ReminderRules.AlertsLabel(alerts);

    public string Summary(RecurrenceRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return rule.Summary();
    }

    public bool IsSimple(RecurrenceRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return rule.IsSimple;
    }

    public RecurrenceRule ParseRule(JsonNode rule) => RecurrenceRule.Parse(rule);

    public FeedUrlCheck CheckFeed(string url) => FeedRules.Check(url);

    public bool HasAlerts(IReadOnlyList<JsonNode> events) => ImportRules.HasAlerts(events);

    public (string FileName, string Text) EventIcs(JsonNode ev) => IcsWriter.Event(ev);

    public DateTime ZoneToLocal(DateTime wall, string? zone) => TimeZones.ZoneToLocal(wall, zone, Zone());

    public DateTime LocalToZone(DateTime local, string? zone) => TimeZones.LocalToZone(local, zone, Zone());

    public long ZoneToUtcMs(DateTime wall, string? zone) => TimeZones.ZoneToUtcMs(wall, zone, Zone());

    public AvailabilityStatus AvailabilityFor(IReadOnlyList<(long Start, long End, bool Tentative)> periods, long fromMs, long untilMs) => FindATime.AvailabilityFor(periods, fromMs, untilMs);

    public IReadOnlyList<FreeSlot> FreeSlots(DateTime now, long durationMs, IReadOnlyList<(long Start, long End)> busy, int max) =>
        FindATime.FindFreeSlots(now, durationMs, [], 8, 20, busy, max);

    public bool UserOrganizes(JsonNode ev, IReadOnlyList<string> mine) => EventEditor.UserOrganizes(ev, mine);

    public IReadOnlyList<CalendarPerson> AttendeesOf(JsonNode ev) => EventEditor.AttendeesOf(ev);

    public CalendarPerson? Organizer(JsonNode ev) => EventEditor.EventOrganizer(ev);

    public string DescriptionText(JsonNode ev) => EventEditor.DescriptionText(ev);
}
