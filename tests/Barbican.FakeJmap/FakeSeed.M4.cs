// The M4 calendar seed (M4 plan section 4.7, step 2 of cal-data): calendars Work, Personal and Travel (default alerts 14 days before), a weekday series with an excluded and a moved occurrence, tasks, an event with guests and an all-day event.
// Synthetic, example.org addresses only. Dates are in November 2026 (the plan's "today" is 2026-11-18). A test builds the server with FakeJmapOptions { CalendarsCapability = true, Calendars = FakeSeed.CalendarsM4() }. Owner cal-data.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public static partial class FakeSeed
{
    /// <summary>The weekday series of the Work calendar ("Stand-up", 09:30 UTC, Monday to Friday from 2026-11-02; 11-04 is excluded and 11-05 moves to 11:00).</summary>
    public const string StandUpId = "ev-standup";

    /// <summary>An event with two guests (the organizer is the login).</summary>
    public const string ReviewId = "ev-review";

    /// <summary>An all-day event of the Personal calendar.</summary>
    public const string BirthdayId = "ev-birthday";

    /// <summary>A task (@type Task) due 2026-11-20 with an alert an hour before.</summary>
    public const string TaskId = "task-invoice";

    /// <summary>An event 9 days after 2026-11-18 on the Travel calendar, which alerts 14 days before by default.</summary>
    public const string FlightId = "ev-flight";

    /// <summary>The calendars of the user's own account: Work (default), Personal, Travel.</summary>
    public static FakeCalendars CalendarsM4()
    {
        var cals = new FakeCalendars();
        var work = cals.AddCalendar("Work", isDefault: true, color: "#3b82f6");
        var personal = cals.AddCalendar("Personal", color: "#22c55e");
        var travel = cals.AddCalendar("Travel", color: "#f97316", extra: new JsonObject
        {
            ["defaultAlertsWithTime"] = new JsonObject { ["d1"] = new JsonObject { ["@type"] = "Alert", ["trigger"] = new JsonObject { ["@type"] = "OffsetTrigger", ["offset"] = "-P14D" }, ["action"] = "display" } },
        });
        AddWorkEvents(cals, work);
        cals.AddEvent(personal, new JsonObject
        {
            ["id"] = BirthdayId,
            ["title"] = "Birthday dinner",
            ["start"] = "2026-11-21T00:00:00",
            ["showWithoutTime"] = true,
            ["duration"] = "P1D",
        });
        cals.AddEvent(travel, new JsonObject
        {
            ["id"] = FlightId,
            ["title"] = "Flight",
            ["start"] = "2026-11-27T08:00:00",
            ["timeZone"] = "UTC",
            ["duration"] = "PT2H",
            ["useDefaultAlerts"] = true,
        });
        return cals;
    }

    private static void AddWorkEvents(FakeCalendars cals, string work)
    {
        cals.AddEvent(work, new JsonObject
        {
            ["id"] = StandUpId,
            ["title"] = "Stand-up",
            ["start"] = "2026-11-02T09:30:00",
            ["timeZone"] = "UTC",
            ["duration"] = "PT15M",
            ["recurrenceRules"] = new JsonArray(new JsonObject { ["@type"] = "RecurrenceRule", ["frequency"] = "daily", ["byDay"] = new JsonArray(new JsonObject { ["day"] = "mo" }, new JsonObject { ["day"] = "tu" }, new JsonObject { ["day"] = "we" }, new JsonObject { ["day"] = "th" }, new JsonObject { ["day"] = "fr" }) }),
            ["recurrenceOverrides"] = new JsonObject
            {
                ["2026-11-04T09:30:00"] = new JsonObject { ["excluded"] = true },
                ["2026-11-05T09:30:00"] = new JsonObject { ["start"] = "2026-11-05T11:00:00", ["title"] = "Stand-up (moved)" },
            },
        });
        cals.AddEvent(work, new JsonObject
        {
            ["id"] = ReviewId,
            ["title"] = "Design review",
            ["start"] = "2026-11-19T14:00:00",
            ["timeZone"] = "UTC",
            ["duration"] = "PT1H",
            ["utcStart"] = "2026-11-19T14:00:00Z",
            ["utcEnd"] = "2026-11-19T15:00:00Z",
            ["participants"] = new JsonObject
            {
                ["p1"] = new JsonObject { ["name"] = "Test User", ["email"] = "user@example.org", ["roles"] = new JsonObject { ["owner"] = true }, ["participationStatus"] = "accepted" },
                ["p2"] = new JsonObject { ["name"] = "Ann", ["email"] = "ann@example.org", ["roles"] = new JsonObject { ["attendee"] = true }, ["participationStatus"] = "needs-action" },
            },
        });
        cals.AddEvent(work, new JsonObject
        {
            ["id"] = TaskId,
            ["@type"] = "Task",
            ["title"] = "Pay invoice",
            ["due"] = "2026-11-20T12:00:00",
            ["timeZone"] = "UTC",
            ["progress"] = "needs-action",
            ["alerts"] = new JsonObject { ["a1"] = new JsonObject { ["@type"] = "Alert", ["trigger"] = new JsonObject { ["@type"] = "OffsetTrigger", ["offset"] = "-PT1H", ["relativeTo"] = "end" }, ["action"] = "display" } },
        });
    }

    /// <summary>A calendar shared with the login by another account: one calendar and one event (account id in <see cref="FakeJmapOptions.SharedCalendars"/>).</summary>
    public static FakeCalendars SharedCalendarM4()
    {
        var cals = new FakeCalendars();
        var team = cals.AddCalendar("Team", isDefault: true, color: "#a855f7");
        cals.AddEvent(team, new JsonObject { ["id"] = "ev-offsite", ["title"] = "Offsite", ["start"] = "2026-11-24T09:00:00", ["timeZone"] = "UTC", ["duration"] = "PT8H" });
        return cals;
    }

    /// <summary>Adds <paramref name="count"/> single events to a calendar (the chunking tests: 1200 events with maxObjectsInGet 500), one per hour from 2026-01-01.</summary>
    public static void AddManyEvents(FakeCalendars calendars, string calendarId, int count)
    {
        ArgumentNullException.ThrowIfNull(calendars);
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        for (var i = 0; i < count; i++)
        {
            calendars.AddEvent(calendarId, new JsonObject
            {
                ["title"] = "Event " + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["start"] = start.AddHours(i).ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                ["timeZone"] = "UTC",
                ["duration"] = "PT30M",
            });
        }
    }
}
