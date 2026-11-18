// Mirrors: the static pure rules of cal-data and cal-edit (EventEditor, CalendarPeople, TimeZones, ReminderRules, FindATime, FeedRules, ImportRules, IcsWriter, RecurrenceRule) behind one seam, so the editor view models are tested with fakes
// until those streams land (M4 plan section 5, "code against ICalendarWriter/EventEditor/... with fakes"). The default delegates to the real statics.

using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Calendar.Feeds;
using Barbican.Core.Calendar.Recurrence;
using Barbican.Core.Calendar.Reminders;
namespace Barbican.Presentation.Calendar.Editors;

/// <summary>The pure rules the calendar views call.</summary>
public interface ICalendarRules
{
    EventData BuildData(EventForm form, JsonNode? ev, (DateTime Start, DateTime End)? occurrence);

    CalendarPerson? ParsePerson(string text);

    string Initials(CalendarPerson person);

    IReadOnlyList<PersonMatch> Matching(IReadOnlyList<JsonNode> cards, string query, IReadOnlyList<string> have, int max);

    IReadOnlyList<string> SearchZones(string query, int max);

    (IReadOnlyList<AlertEditRow> Rows, JsonObject Kept) AlertRows(JsonNode? alerts);

    string RowLabel(ReminderRow row);

    JsonNode? CalendarDefaults(JsonNode? calendar, bool allDay);

    string AlertsLabel(JsonNode? alerts);

    string Summary(RecurrenceRule rule);

    bool IsSimple(RecurrenceRule rule);

    RecurrenceRule ParseRule(JsonNode rule);

    FeedUrlCheck CheckFeed(string url);

    bool HasAlerts(IReadOnlyList<JsonNode> events);

    (string FileName, string Text) EventIcs(JsonNode ev);

    DateTime ZoneToLocal(DateTime wall, string? zone);

    DateTime LocalToZone(DateTime local, string? zone);

    long ZoneToUtcMs(DateTime wall, string? zone);

    AvailabilityStatus AvailabilityFor(IReadOnlyList<(long Start, long End, bool Tentative)> periods, long fromMs, long untilMs);

    IReadOnlyList<FreeSlot> FreeSlots(DateTime now, long durationMs, IReadOnlyList<(long Start, long End)> busy, int max);

    bool UserOrganizes(JsonNode ev, IReadOnlyList<string> mine);

    IReadOnlyList<CalendarPerson> AttendeesOf(JsonNode ev);

    CalendarPerson? Organizer(JsonNode ev);

    string DescriptionText(JsonNode ev);
}
