// New tests for the .ics reader, the importer and the availability service (M4 stream cal-edit acceptance, B-CAL-045, G-004, G-009): what a file becomes as JSCalendar, the alerts choice, the import hooks, and Principal/getAvailability over the fake server.

using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Plugins.Hooks;

namespace Barbican.Core.Tests.Calendar.Editing;

public sealed class IcsReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Sample =
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" +
        "BEGIN:VEVENT\r\nUID:a1\r\nSUMMARY:Design\\, review\r\nDTSTART;TZID=Europe/Berlin:20261103T140000\r\nDTEND;TZID=Europe/Berlin:20261103T153000\r\n" +
        "RRULE:FREQ=WEEKLY;COUNT=3;BYDAY=TU\r\nEXDATE;TZID=Europe/Berlin:20261110T140000\r\nLOCATION:Room 4\r\n" +
        "ORGANIZER;CN=Olga:mailto:olga@acme.example\r\nATTENDEE;CN=Me;PARTSTAT=NEEDS-ACTION;RSVP=TRUE:mailto:user@example.org\r\n" +
        "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER:-PT15M\r\nEND:VALARM\r\nEND:VEVENT\r\n" +
        "BEGIN:VEVENT\r\nUID:a2\r\nSUMMARY:Holiday\r\nDTSTART;VALUE=DATE:20261224\r\nDTEND;VALUE=DATE:20261227\r\nEND:VEVENT\r\n" +
        "BEGIN:VTODO\r\nUID:t1\r\nSUMMARY:Pay\r\nDUE:20261105T090000Z\r\nEND:VTODO\r\nEND:VCALENDAR\r\n";

    [Fact]
    public void Events_become_jscalendar_with_zone_duration_rule_guests_and_alarm()
    {
        var all = IcsReader.Parse(Sample);
        Assert.Equal(3, all.Count);
        var ev = all[0];
        Assert.Equal("Design, review", (string?)ev["title"]);
        Assert.Equal("2026-11-03T14:00:00", (string?)ev["start"]);
        Assert.Equal("Europe/Berlin", (string?)ev["timeZone"]);
        Assert.Equal("PT1H30M", (string?)ev["duration"]);
        Assert.Equal("weekly", (string?)ev["recurrenceRules"]![0]!["frequency"]);
        Assert.Equal(3, (int?)ev["recurrenceRules"]![0]!["count"]);
        Assert.Equal("tu", (string?)ev["recurrenceRules"]![0]!["byDay"]![0]!["day"]);
        Assert.True((bool?)ev["recurrenceOverrides"]!["2026-11-10T14:00:00"]!["excluded"]);
        Assert.Equal("Room 4", (string?)ev["locations"]!["l1"]!["name"]);
        Assert.Equal("mailto:olga@acme.example", (string?)ev["organizerCalendarAddress"]);
        Assert.Equal(2, ev["participants"]!.AsObject().Count);
        Assert.Equal("needs-action", (string?)ev["participants"]!["p2"]!["participationStatus"]);
        Assert.Equal("-PT15M", (string?)ev["alerts"]!["a1"]!["trigger"]!["offset"]);
    }

    [Fact]
    public void A_date_event_is_all_day_and_a_task_keeps_its_due_date()
    {
        var all = IcsReader.Parse(Sample);
        Assert.True((bool?)all[1]["showWithoutTime"]);
        Assert.Equal("P3D", (string?)all[1]["duration"]);
        Assert.Equal("Task", (string?)all[2]["@type"]);
        Assert.Equal("2026-11-05T09:00:00", (string?)all[2]["due"]);
        Assert.Equal("needs-action", (string?)all[2]["progress"]);
    }

    [Fact]
    public void Folded_lines_are_joined_and_garbage_is_skipped()
    {
        var one = IcsReader.Parse("BEGIN:VEVENT\r\nUID:x\r\nSUMMARY:A very\r\n  long title\r\nDTSTART:20261103T140000Z\r\nEND:VEVENT\r\nBEGIN:VEVENT\r\nSUMMARY:no start\r\nEND:VEVENT\r\n");
        Assert.Equal("A very long title", (string?)Assert.Single(one)["title"]);
        Assert.Equal("Etc/UTC", (string?)one[0]["timeZone"]);
        Assert.Empty(IcsReader.Parse("not a calendar"));
    }

    [Fact]
    public async Task Import_keeps_or_drops_the_alerts_stores_without_messages_and_tells_the_hooks()
    {
        var rig = await CalendarRig.CreateAsync();
        var importer = new IcsImporter(rig.Accounts, rig.Store, rig.Hooks);
        var parsed = await importer.ParseAsync(CalendarRig.Account, Encoding.UTF8.GetBytes(Sample), Ct);
        Assert.True(parsed.IsOk, parsed.Error?.Message);
        Assert.True(ImportRules.HasAlerts(parsed.Value!));
        var events = parsed.Value!.Where(e => !string.Equals((string?)e["@type"], "Task", StringComparison.Ordinal)).ToList();

        var done = await CalendarRig.HeardAsync<ImportDonePayload>(rig.Hooks, HookNames.OnEventsImport, async () =>
        {
            var r = await importer.ImportAsync(CalendarRig.Account, rig.Work, events, keepAlerts: false, Ct);
            Assert.Equal(2, r.Value);
        });
        Assert.Equal(new ImportDonePayload(rig.Work, 2, 0), done);
        Assert.Equal(2, rig.Cals.Events().Count);
        Assert.All(rig.Cals.Events(), e => Assert.Null(e["alerts"]));
        Assert.All(rig.Sets, s => Assert.False(s.Args.ContainsKey("sendSchedulingMessages")));
        Assert.Empty(rig.Cals.SchedulingSent);

        var kept = await importer.ImportAsync(CalendarRig.Account, rig.Work, [events[0]], keepAlerts: true, Ct);
        Assert.Equal(1, kept.Value);
        Assert.Contains(rig.Cals.Events(), e => e["alerts"] is not null);
    }

    [Fact]
    public async Task An_intercept_hook_stops_the_import_and_a_big_file_is_refused()
    {
        var rig = await CalendarRig.CreateAsync();
        var importer = new IcsImporter(rig.Accounts, rig.Store, rig.Hooks);
        using var stop = rig.Hooks.Intercept<ImportCountPayload>(HookNames.OnBeforeEventsImport, (_, _) => ValueTask.FromResult(false));
        var r = await importer.ImportAsync(CalendarRig.Account, rig.Work, IcsReader.Parse(Sample), true, Ct);
        Assert.False(r.IsOk);
        Assert.Empty(rig.Cals.Events());
        Assert.False((await importer.ParseAsync(CalendarRig.Account, new byte[IcsImporter.MaxBytes + 1], Ct)).IsOk);
        Assert.False((await importer.ParseAsync(CalendarRig.Account, "no events here"u8.ToArray(), Ct)).IsOk);
    }

    [Fact]
    public async Task Availability_reports_busy_time_of_a_guest_and_not_found_for_a_stranger()
    {
        var rig = await CalendarRig.CreateAsync((cals, work) => cals.AddEvent(work, new JsonObject
        {
            ["title"] = "Busy",
            ["start"] = "2026-11-03T10:00:00",
            ["timeZone"] = "UTC",
            ["duration"] = "PT1H",
            ["utcStart"] = "2026-11-03T10:00:00Z",
            ["utcEnd"] = "2026-11-03T11:00:00Z",
            ["participants"] = new JsonObject { ["p"] = new JsonObject { ["email"] = "ann@x.example", ["roles"] = new JsonObject { ["attendee"] = true } } },
        }));
        var service = new AvailabilityService(rig.Accounts);
        Assert.True(service.IsSupported(CalendarRig.Account));
        var from = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var r = await service.BusyAsync(CalendarRig.Account, ["Ann@x.example"], from, from + 86_400_000, Ct);
        var ann = r.Value!["ann@x.example"];
        Assert.True(ann.IsOk, ann.Error?.Message);
        var busy = Assert.Single(ann.Value!);
        Assert.Equal(from + (10 * 3_600_000L), busy.Start);
        Assert.Equal(from + (11 * 3_600_000L), busy.End);
        Assert.False(busy.Tentative);
    }

    [Fact]
    public async Task Cards_are_empty_without_the_contacts_capability()
    {
        var rig = await CalendarRig.CreateAsync();
        var cards = await new ContactCardReader(rig.Accounts).CardsAsync(CalendarRig.Account, Ct);
        Assert.True(cards.IsOk);
        Assert.Empty(cards.Value!);
    }
}
