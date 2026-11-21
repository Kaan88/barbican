// Source: Bulwark lib/plugin-sandbox/host-api-calendar.ts:396-569 (createEvent, updateEvent, deleteEvent: fields, times, repeat, participants, no invitations), assistant/c.calendar.ts:118-181 (delete with undo token,
// undoDelete), assistant/c.undo.ts (token life), lib/calendar-editability.ts (the CALENDAR's rights gate a write; mailbox rights never do). The writes run through the real calendar writer (hooks, store patches) on
// the fake JMAP server; Barbican fences: stale login / revoked add-on before and during a write.
using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Feeds;
using Barbican.Core.Common;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Tests.Calendar.Editing;
using Rig = Barbican.Core.Tests.Plugins.Api.PluginCalendarRig;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class PluginCalendarWriteTests
{
    /// <summary>The namespace Bulwark gives an add-on's own uids (host-api-calendar.ts:410-412), after the at sign.</summary>
    private const string UidSuffix = "@probe.plugin.bulwark";

    private static string[] Both => ["calendar:read", "calendar:write"];

    private static Task<Rig> StartAsync(Action<FakeCalendarsFacade>? seed = null, JsonObject? workRights = null) =>
        Rig.StartAsync(Rig.Grant(Both), (cals, work, home) =>
        {
            _ = cals.AddEvent(work, EditingRig.Series());
            _ = cals.AddEvent(home, PluginCalendarReadTests.Lunch());
            seed?.Invoke(new FakeCalendarsFacade(cals, work, home));
        }, workRights);

    private static string IdOf(Rig rig, string uid) => (string)rig.Stored(uid)!["id"]!;

    [Fact]
    public async Task A_created_event_has_the_zone_the_instant_and_the_plugins_uid_and_sends_nothing()
    {
        var rig = await StartAsync();
        var result = await rig.CallAsync("calendar.createEvent", """[{"title":"  Planning  ","start":"2026-11-10T09:30","description":"Agenda","location":"Room 4","uid":"plan-1"}]""");
        Assert.True(result.IsSuccess);
        var view = result.Value!;
        Assert.Equal("Planning", Rig.Text(view, "title"), StringComparer.Ordinal);
        Assert.Equal("2026-11-10T09:30:00", Rig.Text(view, "start"), StringComparer.Ordinal);
        Assert.Equal("PT1H", Rig.Text(view, "duration"), StringComparer.Ordinal);
        Assert.Equal("Europe/Berlin", Rig.Text(view, "timeZone"), StringComparer.Ordinal);
        // 09:30 in Berlin in November is 08:30 UTC
        Assert.Equal("2026-11-10T08:30:00Z", Rig.Text(view, "utcStart"), StringComparer.Ordinal);
        Assert.Equal("plan-1" + UidSuffix, Rig.Text(view, "uid"), StringComparer.Ordinal);
        Assert.Equal(rig.Work, Rig.Text(view, "calendarId"), StringComparer.Ordinal);
        Assert.Equal("Room 4", Rig.Text(view, "location"), StringComparer.Ordinal);
        var stored = rig.Stored("plan-1" + UidSuffix)!;
        Assert.Equal(["confirmed", "busy", "public"], [(string)stored["status"]!, (string)stored["freeBusyStatus"]!, (string)stored["privacy"]!], StringComparer.Ordinal);
        Assert.Equal(1, rig.Sets);
        Assert.False(rig.Server.Api.SetRequests[0].Args.ContainsKey("sendSchedulingMessages"));
        Assert.Empty(rig.Cals.SchedulingSent);
        Assert.True(rig.Store.Refreshes >= 1);
    }

    [Fact]
    public async Task Times_are_stored_as_the_plugin_wrote_them_in_utc_in_a_zone_or_as_a_day()
    {
        var rig = await StartAsync();
        var utc = (await rig.CallAsync("calendar.createEvent", """[{"title":"Call","start":"2026-11-10T07:30:00Z","end":"2026-11-10T08:15:00Z"}]""")).Value!;
        Assert.Equal("Etc/UTC", Rig.Text(utc, "timeZone"), StringComparer.Ordinal);
        Assert.Equal("PT45M", Rig.Text(utc, "duration"), StringComparer.Ordinal);
        Assert.Equal("2026-11-10T07:30:00Z", Rig.Text(utc, "utcStart"), StringComparer.Ordinal);
        var day = (await rig.CallAsync("calendar.createEvent", """[{"title":"Trip","start":"2026-12-01","end":"2026-12-04"}]""")).Value!;
        Assert.True(day.Property("isAllDay").BooleanValue);
        Assert.Equal("P3D", Rig.Text(day, "duration"), StringComparer.Ordinal);
        Assert.Equal(PluginValueKind.Null, day.Property("timeZone").Kind);
        Assert.Equal("2026-12-01T00:00:00", Rig.Text(day, "start"), StringComparer.Ordinal);
        var york = (await rig.CallAsync("calendar.createEvent", """[{"title":"NY","start":"2026-11-10T09:30","timeZone":"America/New_York","duration":"PT90M"}]""")).Value!;
        Assert.Equal("PT1H30M", Rig.Text(york, "duration"), StringComparer.Ordinal);
        Assert.Equal("2026-11-10T14:30:00Z", Rig.Text(york, "utcStart"), StringComparer.Ordinal);
        Assert.Equal(3, rig.Sets);
    }

    [Fact]
    public async Task A_repeating_event_keeps_its_wall_clock_across_the_clock_change()
    {
        var rig = await Rig.StartAsync(Rig.Grant(Both));
        var made = await rig.CallAsync("calendar.createEvent", """[{"title":"Weekly","start":"2026-10-20T09:30","repeat":{"frequency":"weekly","count":3},"uid":"w"}]""");
        Assert.True(made.Value!.Property("isRecurring").BooleanValue);
        var rule = rig.Stored("w" + UidSuffix)!["recurrenceRules"]![0]!;
        Assert.Equal("weekly", (string?)rule["frequency"]);
        Assert.Equal(3, (int?)rule["count"]);
        var events = (await rig.CallAsync("calendar.getEvents", """[{"from":"2026-10-19","to":"2026-11-04"}]""")).Value!;
        Assert.Equal(3, events.Count);
        // 20 October is summer time in Berlin (UTC+2), 27 October is after the change (UTC+1)
        Assert.Equal("2026-10-20T07:30:00Z", Rig.Text(events.Item(0), "utcStart"), StringComparer.Ordinal);
        Assert.Equal("2026-10-27T08:30:00Z", Rig.Text(events.Item(1), "utcStart"), StringComparer.Ordinal);
        Assert.Equal("2026-10-27T09:30:00", Rig.Text(events.Item(1), "start"), StringComparer.Ordinal);
        Assert.Equal("2026-11-03T08:30:00Z", Rig.Text(events.Item(2), "utcStart"), StringComparer.Ordinal);
    }

    [Fact]
    public async Task Participants_are_stored_with_the_user_as_organizer_and_nobody_is_invited()
    {
        var rig = await StartAsync();
        var view = (await rig.CallAsync("calendar.createEvent", """[{"title":"Sync","start":"2026-11-11T10:00","participants":[{"email":"ann@x.example","name":"Ann"}]}]""")).Value!;
        var people = view.Property("participants");
        Assert.Equal(2, people.Count);
        var stored = rig.Event(Rig.Text(view, "id"));
        Assert.Equal("mailto:" + EditingRig.Me, (string?)stored["organizerCalendarAddress"]);
        Assert.Empty(rig.Cals.SchedulingSent);
        Assert.False(rig.Server.Api.SetRequests[0].Args.ContainsKey("sendSchedulingMessages"));
    }

    [Fact]
    public async Task Bad_events_are_refused_before_anything_is_written()
    {
        var rig = await StartAsync();
        var long501 = new string('a', 501);
        string[] bad =
        [
            "[]", "[{}]", """[{"title":""}]""", """[{"title":"   ","start":"2026-11-10T10:00"}]""", """[{"title":"x"}]""", """[{"title":"x","start":"soon"}]""",
            """[{"title":"x","start":"2026-11-10T10:00","bogus":1}]""", """[{"title":"x","start":"2026-11-10T10:00","end":"2026-11-10T11:00","duration":"PT1H"}]""",
            """[{"title":"x","start":"2026-11-10T10:00","end":"2026-11-10T09:00"}]""", """[{"title":"x","start":"2026-11-10","timeZone":"Europe/Berlin"}]""",
            """[{"title":"x","start":"2026-11-10","isAllDay":false}]""", """[{"title":"x","start":"2026-11-10T10:00Z","timeZone":"Europe/Berlin"}]""",
            """[{"title":"x","start":"2026-11-10T10:00","timeZone":"Mars/Base"}]""", """[{"title":"x","start":"2026-11-10T10:00","duration":"bogus"}]""",
            """[{"title":"x","start":"2026-11-10T10:00","duration":"PT0S"}]""", """[{"title":"x","start":"2026-11-10","duration":"PT5H"}]""",
            """[{"title":"x","start":"2026-11-10","end":"2026-11-10"}]""", """[{"title":"x","start":"2026-11-10T10:00","duration":"P400D"}]""",
            """[{"title":"x","start":"2026-11-10T10:00","participants":[{"email":"nobody"}]}]""", """[{"title":"x","start":"2026-11-10T10:00","participants":[{"email":"a@b.c","phone":"1"}]}]""",
            """[{"title":"x","start":"2026-11-10T10:00","repeat":{"frequency":"hourly"}}]""", """[{"title":"x","start":"2026-11-10T10:00","repeat":{"frequency":"daily","count":2,"until":"2026-12-01"}}]""",
            """[{"title":"x","start":"2026-11-10T10:00","repeat":{"frequency":"daily","interval":100}}]""", """[{"title":"x","start":"2026-11-10T10:00","repeat":{"frequency":"daily","until":"2026-13-01"}}]""",
            """[{"title":"x","start":"2026-11-10T10:00","uid":"has space"}]""", """[{"title":"x","start":"2026-11-10T10:00","isAllDay":"yes"}]""", """[{"title":"x","start":"2026-11-10T10:00","calendarId":""}]""",
            "[{\"title\":\"" + long501 + "\",\"start\":\"2026-11-10T10:00\"}]", "[{\"title\":\"x\",\"start\":\"2026-11-10T10:00\",\"location\":\"" + new string('b', 501) + "\"}]",
            "[{\"title\":\"x\",\"start\":\"2026-11-10T10:00\",\"description\":\"" + new string('c', 10_001) + "\"}]",
        ];
        foreach (var args in bad) Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("calendar.createEvent", args)).Error);
        var fifty1 = string.Join(',', Enumerable.Range(0, 51).Select(i => $$"""{"email":"p{{i}}@x.example"}"""));
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("calendar.createEvent", $$"""[{"title":"x","start":"2026-11-10T10:00","participants":[{{fifty1}}]}]""")).Error);
        Assert.Equal(0, rig.Sets);
        Assert.Equal(0, rig.Writer.Calls);
    }

    [Fact]
    public async Task A_read_only_calendar_is_refused_by_its_own_rights_and_a_writable_one_still_works()
    {
        var rig = await StartAsync(workRights: new JsonObject { ["mayReadItems"] = true });
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("calendar.createEvent", """[{"title":"x","start":"2026-11-10T10:00"}]""")).Error);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("calendar.createEvent", $$"""[{"title":"x","start":"2026-11-10T10:00","calendarId":"{{rig.Work}}"}]""")).Error);
        Assert.Equal(0, rig.Sets);
        Assert.Equal(0, rig.Writer.Calls);
        Assert.True((await rig.CallAsync("calendar.createEvent", $$"""[{"title":"x","start":"2026-11-10T10:00","calendarId":"{{rig.Home}}"}]""")).IsSuccess);
        Assert.Equal(1, rig.Sets);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("calendar.createEvent", """[{"title":"x","start":"2026-11-10T10:00","calendarId":"nope"}]""")).Error);
        var own = await StartAsync(workRights: new JsonObject { ["mayWriteOwn"] = true });
        Assert.True((await own.CallAsync("calendar.createEvent", """[{"title":"x","start":"2026-11-10T10:00"}]""")).IsSuccess);
    }

    [Fact]
    public async Task A_feed_calendar_is_read_only_whatever_its_rights_say()
    {
        var rig = await StartAsync();
        rig.Host.Subscriptions = new Feeds(rig.Work);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("calendar.createEvent", """[{"title":"x","start":"2026-11-10T10:00"}]""")).Error);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("calendar.updateEvent", """["s",{"title":"y"}]""")).Error);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("calendar.deleteEvent", """["s"]""")).Error);
        Assert.Equal(0, rig.Sets);
    }

    [Fact]
    public async Task Another_add_on_stopping_the_create_gives_null_and_writes_nothing()
    {
        var rig = await StartAsync();
        using var stop = rig.Hooks.InterceptAny(HookNames.OnBeforeEventCreate, (_, _) => ValueTask.FromResult(false));
        var result = await rig.CallAsync("calendar.createEvent", """[{"title":"x","start":"2026-11-10T10:00"}]""");
        Assert.Equal(PluginValueKind.Null, result.Value!.Kind);
        Assert.Equal(0, rig.Sets);
    }

    [Fact]
    public async Task An_update_changes_only_what_is_named_and_keeps_the_length_and_zone()
    {
        var rig = await StartAsync();
        var result = await rig.CallAsync("calendar.updateEvent", """["e1",{"title":"Brunch","start":"2026-11-10T13:15"}]""");
        Assert.True(result.IsSuccess);
        Assert.Equal(PluginValueKind.Undefined, result.Value!.Kind);
        var stored = rig.Event("e1");
        Assert.Equal(["Brunch", "2026-11-10T13:15:00", "PT1H", "Europe/Berlin"], [(string)stored["title"]!, (string)stored["start"]!, (string)stored["duration"]!, (string)stored["timeZone"]!], StringComparer.Ordinal);
        Assert.Equal("At the usual place", (string?)stored["description"]);
        Assert.Equal(1, rig.Sets);
        Assert.False(rig.Server.Api.SetRequests[0].Args.ContainsKey("sendSchedulingMessages"));
        Assert.Equal(PluginValueKind.String, (await rig.CallAsync("calendar.getEvent", """["e1"]""")).Value!.Property("title").Kind);
    }

    [Fact]
    public async Task Time_changes_are_worked_out_together_against_the_stored_event()
    {
        var rig = await StartAsync();
        Assert.True((await rig.CallAsync("calendar.updateEvent", """["e1",{"timeZone":"Asia/Tokyo"}]""")).IsSuccess);
        var tokyo = rig.Event("e1");
        Assert.Equal(["2026-11-10T12:00:00", "Asia/Tokyo", "PT1H"], [(string)tokyo["start"]!, (string)tokyo["timeZone"]!, (string)tokyo["duration"]!], StringComparer.Ordinal);
        Assert.True((await rig.CallAsync("calendar.updateEvent", """["e1",{"duration":"PT2H"}]""")).IsSuccess);
        Assert.Equal("PT2H", (string?)rig.Event("e1")["duration"]);
        Assert.True((await rig.CallAsync("calendar.updateEvent", """["e1",{"isAllDay":true}]""")).IsSuccess);
        var day = rig.Event("e1");
        Assert.Equal(["2026-11-10T00:00:00", "P1D"], [(string)day["start"]!, (string)day["duration"]!], StringComparer.Ordinal);
        Assert.True((bool?)day["showWithoutTime"]);
        Assert.Null(day["timeZone"]);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("calendar.updateEvent", """["e1",{"timeZone":"Europe/Berlin"}]""")).Error);
        Assert.Equal(3, rig.Sets);
    }

    [Fact]
    public async Task Location_description_participants_and_repeat_can_be_removed_or_replaced()
    {
        var rig = await StartAsync(f => f.Add(f.Home, new JsonObject { ["id"] = "html", ["uid"] = "u-html", ["title"] = "Rich", ["start"] = "2026-11-12T10:00:00", ["timeZone"] = "UTC", ["duration"] = "PT1H", ["description"] = "<p>Hi</p>", ["descriptionContentType"] = "text/html" }));
        Assert.True((await rig.CallAsync("calendar.updateEvent", """["e1",{"location":""}]""")).IsSuccess);
        Assert.Null(rig.Event("e1")["locations"]);
        Assert.True((await rig.CallAsync("calendar.updateEvent", """["html",{"description":"Plain"}]""")).IsSuccess);
        Assert.Equal(["Plain", "text/plain"], [(string)rig.Event("html")["description"]!, (string)rig.Event("html")["descriptionContentType"]!], StringComparer.Ordinal);
        Assert.True((await rig.CallAsync("calendar.updateEvent", """["s",{"participants":[]}]""")).IsSuccess);
        Assert.Null(rig.Event("s")["participants"]);
        Assert.True((await rig.CallAsync("calendar.updateEvent", """["s",{"repeat":null}]""")).IsSuccess);
        Assert.Null(rig.Event("s")["recurrenceRules"]);
        Assert.True((await rig.CallAsync("calendar.updateEvent", """["e1",{"participants":[{"email":"bob@y.example"}]}]""")).IsSuccess);
        Assert.Equal(2, rig.Event("e1")["participants"]!.AsObject().Count);
        Assert.Empty(rig.Cals.SchedulingSent);
    }

    [Fact]
    public async Task An_occurrence_id_changes_that_occurrence_as_an_override_on_the_series()
    {
        var rig = await StartAsync();
        Assert.True((await rig.CallAsync("calendar.updateEvent", """["s|2026-11-04T09:00:00",{"title":"Stand-up moved","start":"2026-11-04T10:00"}]""")).IsSuccess);
        var stored = rig.Event("s");
        Assert.Equal("Stand-up", (string?)stored["title"]);
        var over = stored["recurrenceOverrides"]!["2026-11-04T09:00:00"]!;
        Assert.Equal(["Stand-up moved", "2026-11-04T10:00:00"], [(string)over["title"]!, (string)over["start"]!], StringComparer.Ordinal);
        var occurrence = (await rig.CallAsync("calendar.getEvent", """["s|2026-11-04T09:00:00"]""")).Value!;
        Assert.Equal("Stand-up moved", Rig.Text(occurrence, "title"), StringComparer.Ordinal);
        Assert.Equal("2026-11-04T10:00:00", Rig.Text(occurrence, "start"), StringComparer.Ordinal);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("calendar.updateEvent", $$"""["s|2026-11-04T09:00:00",{"calendarId":"{{rig.Home}}"}]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("calendar.updateEvent", """["s|2026-11-04T09:00:00",{"repeat":null}]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("calendar.updateEvent", """["s|2026-12-30T09:00:00",{"title":"x"}]""")).Error);
    }

    [Fact]
    public async Task An_event_can_move_to_another_writable_calendar_and_not_into_a_read_only_one()
    {
        var rig = await StartAsync(f => f.Add(f.AddCalendar("Locked", new JsonObject { ["mayReadItems"] = true }), new JsonObject { ["id"] = "x", ["uid"] = "u-x", ["title"] = "X", ["start"] = "2026-11-13T10:00:00", ["timeZone"] = "UTC", ["duration"] = "PT1H" }));
        Assert.True((await rig.CallAsync("calendar.updateEvent", $$"""["s",{"calendarId":"{{rig.Home}}"}]""")).IsSuccess);
        Assert.True((bool?)rig.Event("s")["calendarIds"]![rig.Home]);
        var locked = (string)rig.Cals.Calendars().First(c => string.Equals((string?)c["name"], "Locked", StringComparison.Ordinal))["id"]!;
        var before = rig.Sets;
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("calendar.updateEvent", $$"""["e1",{"calendarId":"{{locked}}"}]""")).Error);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("calendar.updateEvent", """["x",{"title":"changed"}]""")).Error);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("calendar.deleteEvent", """["x"]""")).Error);
        Assert.Equal(before, rig.Sets);
        Assert.Equal("X", (string?)rig.Event("x")["title"]);
    }

    [Fact]
    public async Task Write_own_lets_an_organizer_edit_and_not_an_event_someone_else_organizes()
    {
        var rig = await StartAsync(f =>
        {
            var own = f.AddCalendar("Own", new JsonObject { ["mayReadItems"] = true, ["mayWriteOwn"] = true });
            var mine = EditingRig.Series("mine");
            f.Add(own, mine);
            f.Add(own, new JsonObject
            {
                ["id"] = "theirs", ["uid"] = "u-theirs", ["title"] = "Theirs", ["start"] = "2026-11-12T10:00:00", ["timeZone"] = "UTC", ["duration"] = "PT1H", ["organizerCalendarAddress"] = "mailto:olga@acme.example",
                ["participants"] = new JsonObject
                {
                    ["o"] = new JsonObject { ["email"] = "olga@acme.example", ["roles"] = new JsonObject { ["owner"] = true } },
                    ["m"] = new JsonObject { ["email"] = EditingRig.Me, ["roles"] = new JsonObject { ["attendee"] = true } },
                },
            });
        });
        Assert.True((await rig.CallAsync("calendar.updateEvent", """["mine",{"title":"Mine"}]""")).IsSuccess);
        Assert.Equal("Mine", (string?)rig.Event("mine")["title"]);
        var sets = rig.Sets;
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("calendar.updateEvent", """["theirs",{"title":"Hijacked"}]""")).Error);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("calendar.deleteEvent", """["theirs"]""")).Error);
        Assert.Equal(sets, rig.Sets);
    }

    [Fact]
    public async Task Bad_updates_and_unknown_events_are_refused()
    {
        var rig = await StartAsync();
        foreach (var bad in new[] { """["e1",{}]""", """["e1",{"uid":"x"}]""", """["e1",{"title":""}]""", """["e1",{"start":"never"}]""", """["e1","title"]""", """[7,{"title":"x"}]""", """["e1",{"end":"2026-11-10T13:00","duration":"PT1H"}]""" })
        {
            Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("calendar.updateEvent", bad)).Error);
        }

        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("calendar.updateEvent", """["nope",{"title":"x"}]""")).Error);
        Assert.Equal(0, rig.Sets);
    }

    [Fact]
    public async Task Delete_returns_a_token_that_puts_the_event_back_once_with_the_same_uid()
    {
        var rig = await StartAsync();
        var deleted = await rig.CallAsync("calendar.deleteEvent", """["e1"]""");
        Assert.Equal(["undoToken"], Rig.Keys(deleted.Value!), StringComparer.Ordinal);
        var token = Rig.Text(deleted.Value!, "undoToken");
        Assert.Equal(32, token.Length);
        Assert.Null(rig.Stored("u-e1"));
        Assert.Empty(rig.Cals.SchedulingSent);
        Assert.Equal(1, rig.Sets);
        var undone = await rig.CallAsync("calendar.undoDelete", $$"""["{{token}}"]""");
        Assert.Equal(["id"], Rig.Keys(undone.Value!), StringComparer.Ordinal);
        var back = rig.Stored("u-e1")!;
        Assert.Equal(["Lunch", rig.Home], [(string)back["title"]!, back["calendarIds"]!.AsObject().First().Key], StringComparer.Ordinal);
        Assert.Equal((string)back["id"]!, Rig.Text(undone.Value!, "id"), StringComparer.Ordinal);
        Assert.Equal(2, rig.Sets);
        Assert.Empty(rig.Cals.SchedulingSent);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("calendar.undoDelete", $$"""["{{token}}"]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("calendar.undoDelete", """["made-up"]""")).Error);
        Assert.Equal(2, rig.Sets);
    }

    [Fact]
    public async Task A_token_lasts_thirty_minutes_and_a_failed_undo_keeps_it()
    {
        var rig = await StartAsync();
        var token = Rig.Text((await rig.CallAsync("calendar.deleteEvent", """["e1"]""")).Value!, "undoToken");
        using var stop = rig.Hooks.InterceptAny(HookNames.OnBeforeEventCreate, (_, _) => ValueTask.FromResult(false));
        Assert.Equal(PluginErrorCode.Failed, (await rig.CallAsync("calendar.undoDelete", $$"""["{{token}}"]""")).Error);
        Assert.Null(rig.Stored("u-e1"));
        stop.Dispose();
        rig.Time.Advance(TimeSpan.FromMinutes(29));
        Assert.True((await rig.CallAsync("calendar.undoDelete", $$"""["{{token}}"]""")).IsSuccess);
        var late = Rig.Text((await rig.CallAsync("calendar.deleteEvent", $$"""["{{IdOf(rig, "u-e1")}}"]""")).Value!, "undoToken");
        rig.Time.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("calendar.undoDelete", $$"""["{{late}}"]""")).Error);
    }

    [Fact]
    public async Task A_series_or_one_occurrence_is_deleted_without_a_token()
    {
        var rig = await StartAsync();
        var one = await rig.CallAsync("calendar.deleteEvent", """["s|2026-11-03T09:00:00"]""");
        Assert.Equal(PluginValueKind.Null, one.Value!.Property("undoToken").Kind);
        Assert.True((bool?)rig.Event("s")["recurrenceOverrides"]!["2026-11-03T09:00:00"]!["excluded"]);
        Assert.Equal(PluginValueKind.Null, (await rig.CallAsync("calendar.getEvent", """["s|2026-11-03T09:00:00"]""")).Value!.Kind);
        var all = await rig.CallAsync("calendar.deleteEvent", """["s"]""");
        Assert.Equal(PluginValueKind.Null, all.Value!.Property("undoToken").Kind);
        Assert.Null(rig.Stored("u-s"));
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("calendar.deleteEvent", """["s"]""")).Error);
    }

    [Fact]
    public async Task Another_add_on_stopping_the_delete_gives_a_null_token_and_keeps_the_event()
    {
        var rig = await StartAsync();
        using var stop = rig.Hooks.InterceptAny(HookNames.OnBeforeEventDelete, (_, _) => ValueTask.FromResult(false));
        var result = await rig.CallAsync("calendar.deleteEvent", """["e1"]""");
        Assert.Equal(PluginValueKind.Null, result.Value!.Property("undoToken").Kind);
        Assert.NotNull(rig.Stored("u-e1"));
        Assert.Equal(0, rig.Sets);
    }

    [Fact]
    public async Task A_login_that_went_stale_before_the_write_stops_it()
    {
        var rig = await StartAsync();
        rig.SpyStore.OnSnapshot = () => rig.Sessions.Valid = false;
        Assert.Equal(PluginErrorCode.StaleContext, (await rig.CallAsync("calendar.createEvent", """[{"title":"x","start":"2026-11-10T10:00"}]""")).Error);
        rig.Sessions.Valid = true;
        Assert.Equal(PluginErrorCode.StaleContext, (await rig.CallAsync("calendar.deleteEvent", """["e1"]""")).Error);
        Assert.Equal(0, rig.Sets);
        Assert.Equal(0, rig.Writer.Calls);
        Assert.NotNull(rig.Stored("u-e1"));
    }

    [Fact]
    public async Task A_login_or_grant_that_changed_during_the_write_reports_an_unknown_outcome()
    {
        var rig = await StartAsync();
        rig.Writer.Before = () => rig.Sessions.Valid = false;
        var result = await rig.CallAsync("calendar.createEvent", """[{"title":"x","start":"2026-11-10T10:00"}]""");
        Assert.Equal(PluginErrorCode.UnknownOutcome, result.Error);
        Assert.Null(result.Value);
        var revoked = await StartAsync();
        revoked.Writer.Before = () => revoked.Authority.Current = Rig.Grant("calendar:read");
        Assert.Equal(PluginErrorCode.UnknownOutcome, (await revoked.CallAsync("calendar.deleteEvent", """["e1"]""")).Error);
    }

    internal sealed class FakeCalendarsFacade(Barbican.FakeJmap.FakeCalendars cals, string work, string home)
    {
        public string Work { get; } = work;

        public string Home { get; } = home;

        public string AddCalendar(string name, JsonObject rights) => cals.AddCalendar(name, false, "#3b82f6", new JsonObject { ["myRights"] = rights });

        public void Add(string calendar, JsonObject ev) => cals.AddEvent(calendar, ev);
    }

    private sealed class Feeds(string calendarId) : ICalendarSubscriptions
    {
        public Task<IReadOnlyList<CalendarSubscription>> ListAsync(string accountKey, CancellationToken ct) => Task.FromResult<IReadOnlyList<CalendarSubscription>>([new CalendarSubscription(calendarId, "https://feed.example/a.ics", 60)]);

        public Task<Result<string?>> SaveAsync(string accountKey, CalendarSubscription subscription, string name, CancellationToken ct) => throw new NotSupportedException();

        public Task<Result<int>> RefreshAsync(string accountKey, string calendarId, CancellationToken ct) => throw new NotSupportedException();

        public Task<Result<Unit>> RemoveAsync(string accountKey, string calendarId, bool deleteCalendar, CancellationToken ct) => throw new NotSupportedException();
    }
}
