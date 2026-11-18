// Ported from Rust src/reminders.rs tests (:543-:637): fetch ahead, offsets and rows, effective alerts, alert patches, due alerts, the missed window (T-247..T-252); the 8-day gap G-005 is covered by the first test. M4 stream cal-data.

using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Reminders;
using static Barbican.Core.Tests.Calendar.CalendarTestData;

namespace Barbican.Core.Tests.Calendar.Reminders;

public sealed class ReminderRulesTests
{
    [Fact]
    [Red("cal-data")]
    [RustTest("reminders::fetch_ahead_covers_long_default_alerts")]
    public void Fetch_ahead_covers_long_default_alerts()
    {
        Assert.Equal(8L * 86_400_000, ReminderRules.FetchAhead([]));
        var cals = new[] { J("""{"defaultAlertsWithoutTime": {"a": {"trigger": {"offset": "-P14D"}}}}""") };
        Assert.Equal(15L * 86_400_000, ReminderRules.FetchAhead(cals));

        // a 2-days-before alert on an event 3 days out is inside the window
        Assert.True(ReminderRules.FetchAhead([]) > 3L * 86_400_000);
    }

    [Fact]
    [Red("cal-data")]
    [RustTest("reminders::offsets_and_rows")]
    public void Offsets_and_rows()
    {
        Assert.Equal(-900_000, ReminderRules.OffsetMs("-PT15M"));
        Assert.Equal((15L, 1), ReminderRules.OffsetToUnit("-PT15M"));
        Assert.Equal((2L, 2), ReminderRules.OffsetToUnit("-PT120M"));
        Assert.Equal((1L, 3), ReminderRules.OffsetToUnit("-PT1440M"));
        Assert.Equal((1L, 4), ReminderRules.OffsetToUnit("-P1W"));
        Assert.Equal((0L, 0), ReminderRules.OffsetToUnit("PT0S"));
        Assert.Null(ReminderRules.OffsetToUnit("PT15M"));
        Assert.Null(ReminderRules.OffsetToUnit("-P1DT2H"));
        Assert.Equal("-PT2H", ReminderRules.RowOffset(new ReminderRow(2, 2, 0)));
        Assert.Null(ReminderRules.RowOffset(new ReminderRow(0, 1, 0)));
        Assert.Equal("1 day before by email", ReminderRules.RowLabel(new ReminderRow(1, 3, 1)));
        var a = J("""{"trigger": {"@type": "OffsetTrigger", "offset": "-PT10M"}, "action": "display"}""");
        Assert.Equal(new ReminderRow(10, 1, 0), ReminderRules.Describe(a));
        Assert.Null(ReminderRules.Describe(J("""{"trigger": {"@type": "AbsoluteTrigger", "when": "2026-01-01T00:00:00Z"}}""")));
        Assert.Equal("10 minutes before", ReminderRules.AlertsLabel(new JsonObject { ["a"] = a.DeepClone() }));
    }

    [Fact]
    [Red("cal-data")]
    [RustTest("reminders::effective_alerts_prefer_the_event_copy")]
    public void Effective_alerts_prefer_the_event_copy()
    {
        var cal = J("""{"defaultAlertsWithTime": {"d1": {"trigger": {"offset": "-PT30M"}}}, "defaultAlertsWithoutTime": {}}""");
        var e = J("""{"useDefaultAlerts": true}""");
        Assert.Equal("-PT30M", (string?)ReminderRules.EffectiveAlerts(e, cal)!["d1"]!["trigger"]!["offset"]);
        e = J("""{"useDefaultAlerts": true, "alerts": {"x": {"trigger": {"offset": "-PT5M"}}}}""");
        Assert.NotNull(ReminderRules.EffectiveAlerts(e, cal)!["x"]);
        e = J("""{"useDefaultAlerts": false}""");
        Assert.Null(ReminderRules.EffectiveAlerts(e, cal));
        Assert.Null(ReminderRules.EffectiveAlerts(J("""{"useDefaultAlerts": true, "showWithoutTime": true}"""), cal));
    }

    [Fact]
    [Red("cal-data")]
    [RustTest("reminders::alerts_patch_keeps_unchanged_and_leaves_defaults")]
    public void Alerts_patch_keeps_unchanged_and_leaves_defaults()
    {
        var ev = J("""{"alerts": {"a1": {"@type": "Alert", "trigger": {"@type": "OffsetTrigger", "offset": "-PT10M"}, "action": "display"}}}""");
        var (rows, kept) = ReminderRules.RowsOf(ev["alerts"]);

        // unchanged: nothing to send
        Assert.Empty(ReminderRules.BuildAlertsPatch(ev, rows, kept, []));

        // one more row: the old alert keeps its id, the new one gets its own
        var more = new List<AlertEditRow>(rows) { new(new ReminderRow(1, 3, 1)) };
        var p = ReminderRules.BuildAlertsPatch(ev, more, kept, []);
        var a = p["alerts"]!.AsObject();
        Assert.True(a.ContainsKey("a1") && a.ContainsKey("alert1"));
        Assert.Equal("-P1D", (string?)a["alert1"]!["trigger"]!["offset"]);
        Assert.Equal("email", (string?)a["alert1"]!["action"]);

        // all rows removed: alerts cleared
        var cleared = ReminderRules.BuildAlertsPatch(ev, [], [], []);
        Assert.True(cleared.ContainsKey("alerts"));
        Assert.Null(cleared["alerts"]);

        // a new event says it leaves the defaults, and sends no empty map
        p = ReminderRules.BuildAlertsPatch(null, [], [], []);
        Assert.False((bool?)p["useDefaultAlerts"]);
        Assert.False(p.ContainsKey("alerts"));

        // an event on defaults gets ids away from the default ids
        ev = J("""{"useDefaultAlerts": true, "alerts": {"alert1": {"trigger": {"offset": "-PT10M"}}}}""");
        p = ReminderRules.BuildAlertsPatch(ev, [new AlertEditRow(new ReminderRow(5, 1, 0))], [], ["alert2"]);
        Assert.True(p["alerts"]!.AsObject().ContainsKey("alert3"));
        Assert.False((bool?)p["useDefaultAlerts"]);
    }

    [Fact]
    [Red("cal-data")]
    [RustTest("reminders::due_alerts_of_series_and_tasks")]
    public void Due_alerts_of_series_and_tasks()
    {
        var cals = new[] { J("""{"id": "c", "name": "Work", "defaultAlertsWithTime": {"d": {"@type": "Alert", "trigger": {"@type": "OffsetTrigger", "offset": "-PT15M"}, "action": "display"}}}""") };
        var ev = J("""
            {"id": "e", "title": "Stand-up", "start": "2026-11-16T10:00:00", "timeZone": "UTC", "duration": "PT15M", "calendarIds": {"c": true},
             "useDefaultAlerts": true, "recurrenceRules": [{"frequency": "daily"}]}
            """);
        var d = ReminderRules.DueBetween([ev], cals, UtcMs("2026-11-17T00:00:00"), UtcMs("2026-11-18T00:00:00"), true);
        Assert.Single(d);
        Assert.Equal(UtcMs("2026-11-17T09:45:00"), d[0].FireMs);
        Assert.Equal("e|2026-11-17T10:00:00", d[0].Event);
        Assert.Equal("Work", d[0].Calendar);
        Assert.Equal("In 15 minutes", ReminderRules.WhenLabel(d[0], UtcMs("2026-11-17T09:45:00")));

        // cancelled and email-only events don't fire
        var c2 = ev.DeepClone();
        c2["status"] = "cancelled";
        Assert.Empty(ReminderRules.DueBetween([c2], cals, UtcMs("2026-11-17T00:00:00"), UtcMs("2026-11-18T00:00:00"), true));
        var task = J("""
            {"id": "t", "@type": "Task", "title": "Pay", "due": "2026-11-20T12:00:00", "timeZone": "UTC", "calendarIds": {"c": true},
             "alerts": {"a": {"@type": "Alert", "trigger": {"@type": "OffsetTrigger", "offset": "-PT1H", "relativeTo": "end"}, "action": "display"}}}
            """);
        d = ReminderRules.DueBetween([task], cals, UtcMs("2026-11-20T00:00:00"), UtcMs("2026-11-21T00:00:00"), true);
        Assert.Single(d);
        Assert.Equal(UtcMs("2026-11-20T11:00:00"), d[0].FireMs);
        Assert.True(d[0].Task);
        Assert.Empty(ReminderRules.DueBetween([task], cals, UtcMs("2026-11-20T00:00:00"), UtcMs("2026-11-21T00:00:00"), false));
    }

    [Fact]
    [Red("cal-data")]
    [RustTest("reminders::missed_window")]
    public void Missed_window()
    {
        static DueReminder Mk(string key, long fire) => new(key, "e", "a", fire, null, "t", string.Empty, false);
        const long now = 10L * 86_400_000;
        var due = new[] { Mk("old", now - (8L * 86_400_000)), Mk("a", now - 3_600_000), Mk("b", now - 60_000), Mk("acked", now - 7_200_000) };
        var acked = new HashSet<string>(StringComparer.Ordinal) { "acked" };
        var m = ReminderRules.Missed(due, acked, now - (9L * 86_400_000), now);
        Assert.Equal(["a"], m.Select(d => d.Key).ToArray());
        Assert.Empty(ReminderRules.Missed(due, acked, null, now));
    }
}
