// Source: Bulwark lib/plugin-sandbox/host-api-calendar.ts:207-375 (calendarToView, doCalendarGetEvents, doCalendarGetEvent), host-api.ts:308-321 (permissions), lib/plugin-projection.ts:105-132 (eventToView);
// lib/__tests__/plugin-permission-coverage.test.ts. The real PluginApiDispatcher over the real calendar services and the fake JMAP server; Barbican fences (stale login, revoked add-on).
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Tests.Calendar.Editing;
using Rig = Barbican.Core.Tests.Plugins.Api.PluginCalendarRig;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class PluginCalendarReadTests
{
    private static readonly string[] CalendarKeys = ["color", "id", "isDefault", "isTasksOnly", "isVisible", "name"];
    private static readonly string[] EventKeys = ["calendarId", "description", "duration", "id", "isAllDay", "isRecurring", "keywords", "location", "participants", "start", "status", "timeZone", "title", "uid", "utcEnd", "utcStart"];

    internal static System.Text.Json.Nodes.JsonObject Lunch(string id = "e1") => new()
    {
        ["id"] = id,
        ["uid"] = "u-" + id,
        ["title"] = "Lunch",
        ["description"] = "At the usual place",
        ["start"] = "2026-11-10T12:00:00",
        ["timeZone"] = "Europe/Berlin",
        ["duration"] = "PT1H",
        ["locations"] = new System.Text.Json.Nodes.JsonObject { ["l1"] = new System.Text.Json.Nodes.JsonObject { ["name"] = "Cafe" } },
        ["keywords"] = new System.Text.Json.Nodes.JsonObject { ["food"] = true },
    };

    private static Task<Rig> StartAsync(string permission = "calendar:read") =>
        Rig.StartAsync(Rig.Grant(permission), (cals, work, home) =>
        {
            _ = cals.AddEvent(work, EditingRig.Series());
            _ = cals.AddEvent(home, Lunch());
        });

    [Fact]
    public async Task List_calendars_names_six_fields_and_no_rights()
    {
        var rig = await StartAsync();
        var list = (await rig.CallAsync("calendar.listCalendars", "[]")).Value!;
        Assert.Equal(2, list.Count);
        Assert.Equal(CalendarKeys, Rig.Keys(list.Item(0)), StringComparer.Ordinal);
        Assert.Equal("Work", Rig.Text(list.Item(0), "name"), StringComparer.Ordinal);
        Assert.True(list.Item(0).Property("isDefault").BooleanValue);
        Assert.True(list.Item(0).Property("isVisible").BooleanValue);
        Assert.False(list.Item(0).Property("isTasksOnly").BooleanValue);
        Assert.False(list.Item(1).Property("isDefault").BooleanValue);
    }

    [Fact]
    public async Task Events_expand_a_series_into_occurrences_with_exactly_the_named_fields()
    {
        var rig = await StartAsync();
        var events = (await rig.CallAsync("calendar.getEvents", """[{"from":"2026-11-03","to":"2026-11-05"}]""")).Value!;
        Assert.Equal(2, events.Count);
        var first = events.Item(0);
        Assert.Equal(EventKeys, Rig.Keys(first), StringComparer.Ordinal);
        Assert.Equal("s|2026-11-03T09:00:00", Rig.Text(first, "id"), StringComparer.Ordinal);
        Assert.Equal("s|2026-11-04T09:00:00", Rig.Text(events.Item(1), "id"), StringComparer.Ordinal);
        Assert.Equal("u-s", Rig.Text(first, "uid"), StringComparer.Ordinal);
        Assert.Equal("2026-11-03T09:00:00", Rig.Text(first, "start"), StringComparer.Ordinal);
        Assert.Equal("PT15M", Rig.Text(first, "duration"), StringComparer.Ordinal);
        Assert.Equal("UTC", Rig.Text(first, "timeZone"), StringComparer.Ordinal);
        Assert.Equal("2026-11-03T09:00:00Z", Rig.Text(first, "utcStart"), StringComparer.Ordinal);
        Assert.Equal("2026-11-03T09:15:00Z", Rig.Text(first, "utcEnd"), StringComparer.Ordinal);
        Assert.True(first.Property("isRecurring").BooleanValue);
        Assert.False(first.Property("isAllDay").BooleanValue);
        var people = first.Property("participants");
        Assert.Equal(2, people.Count);
        Assert.Equal(["email", "name", "role", "status"], Rig.Keys(people.Item(0)), StringComparer.Ordinal);
        Assert.Equal("owner", Rig.Text(people.Item(0), "role"), StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_range_in_utc_meets_an_event_in_another_zone_by_the_instant()
    {
        var rig = await StartAsync();
        var inside = (await rig.CallAsync("calendar.getEvents", """[{"from":"2026-11-10T11:00:00Z","to":"2026-11-10T11:30:00Z"}]""")).Value!;
        Assert.Equal(1, inside.Count);
        var lunch = inside.Item(0);
        Assert.Equal("e1", Rig.Text(lunch, "id"), StringComparer.Ordinal);
        // 12:00 in Berlin (UTC+1 in November) is 11:00 UTC
        Assert.Equal("2026-11-10T12:00:00", Rig.Text(lunch, "start"), StringComparer.Ordinal);
        Assert.Equal("Europe/Berlin", Rig.Text(lunch, "timeZone"), StringComparer.Ordinal);
        Assert.Equal("2026-11-10T11:00:00Z", Rig.Text(lunch, "utcStart"), StringComparer.Ordinal);
        Assert.Equal("2026-11-10T12:00:00Z", Rig.Text(lunch, "utcEnd"), StringComparer.Ordinal);
        Assert.Equal("Cafe", Rig.Text(lunch, "location"), StringComparer.Ordinal);
        Assert.Equal("food", lunch.Property("keywords").Item(0).StringValue, StringComparer.Ordinal);
        var before = (await rig.CallAsync("calendar.getEvents", """[{"from":"2026-11-10T08:00:00Z","to":"2026-11-10T10:59:00Z"}]""")).Value!;
        Assert.Equal(0, before.Count);
    }

    [Fact]
    public async Task Events_can_be_limited_to_calendars()
    {
        var rig = await StartAsync();
        var home = (await rig.CallAsync("calendar.getEvents", $$"""[{"from":"2026-11-01","to":"2026-11-30","calendarIds":["{{rig.Home}}"]}]""")).Value!;
        Assert.Equal(["e1"], Enumerable.Range(0, home.Count).Select(i => Rig.Text(home.Item(i), "id")).ToArray(), StringComparer.Ordinal);
        var all = (await rig.CallAsync("calendar.getEvents", """[{"from":"2026-11-01","to":"2026-11-30"}]""")).Value!;
        Assert.Equal(6, all.Count);
        Assert.Equal(0, (await rig.CallAsync("calendar.getEvents", """[{"from":"2026-11-01","to":"2026-11-30","calendarIds":[]}]""")).Value!.Count);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""[{"from":"2026-11-01"}]""")]
    [InlineData("""[{"from":"2026-11-05","to":"2026-11-05"}]""")]
    [InlineData("""[{"from":"2026-11-05","to":"2026-11-01"}]""")]
    [InlineData("""[{"from":"2026-11-01","to":"2027-11-04"}]""")]
    [InlineData("""[{"from":"2026-11-01T00:00:00Z","to":"2026-11-02"}]""")]
    [InlineData("""[{"from":"2026-02-30","to":"2026-03-02"}]""")]
    [InlineData("""[{"from":"2026-11-01T25:00","to":"2026-11-02"}]""")]
    [InlineData("""[{"from":"2026-11-01T10:00+02:00","to":"2026-11-02"}]""")]
    [InlineData("""[{"from":"2026-11-01","to":"2026-11-02","extra":1}]""")]
    [InlineData("""[{"from":"2026-11-01","to":"2026-11-02","calendarIds":"cal1"}]""")]
    [InlineData("""[{"from":"2026-11-01","to":"2026-11-02","calendarIds":[1]}]""")]
    public async Task Bad_event_ranges_are_refused_before_any_read(string args)
    {
        var rig = await StartAsync();
        var calls = rig.Server.ApiCalls.Count;
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("calendar.getEvents", args)).Error);
        Assert.Equal(calls, rig.Server.ApiCalls.Count);
    }

    [Fact]
    public async Task A_range_of_366_days_is_allowed_and_367_is_not()
    {
        var rig = await StartAsync();
        Assert.True((await rig.CallAsync("calendar.getEvents", """[{"from":"2026-01-01","to":"2027-01-02"}]""")).IsSuccess);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("calendar.getEvents", """[{"from":"2026-01-01","to":"2027-01-03"}]""")).Error);
    }

    [Fact]
    public async Task One_event_one_occurrence_or_nothing()
    {
        var rig = await StartAsync();
        var one = (await rig.CallAsync("calendar.getEvent", """["e1"]""")).Value!;
        Assert.Equal(EventKeys, Rig.Keys(one), StringComparer.Ordinal);
        Assert.Equal("Lunch", Rig.Text(one, "title"), StringComparer.Ordinal);
        Assert.False(one.Property("isRecurring").BooleanValue);
        var series = (await rig.CallAsync("calendar.getEvent", """["s"]""")).Value!;
        Assert.True(series.Property("isRecurring").BooleanValue);
        var occurrence = (await rig.CallAsync("calendar.getEvent", """["s|2026-11-04T09:00:00"]""")).Value!;
        Assert.Equal("s|2026-11-04T09:00:00", Rig.Text(occurrence, "id"), StringComparer.Ordinal);
        Assert.Equal("2026-11-04T09:00:00", Rig.Text(occurrence, "start"), StringComparer.Ordinal);
        Assert.Equal(PluginValueKind.Null, (await rig.CallAsync("calendar.getEvent", """["nope"]""")).Value!.Kind);
        Assert.Equal(PluginValueKind.Null, (await rig.CallAsync("calendar.getEvent", """["s|2026-11-30T09:00:00"]""")).Value!.Kind);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("calendar.getEvent", "[7]")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("calendar.getEvent", "[]")).Error);
    }

    [Theory]
    [InlineData("calendar.listCalendars", "[]", "calendar:read")]
    [InlineData("calendar.getEvents", """[{"from":"2026-11-01","to":"2026-11-30"}]""", "calendar:read")]
    [InlineData("calendar.getEvent", """["e1"]""", "calendar:read")]
    [InlineData("calendar.createEvent", """[{"title":"x","start":"2026-11-10T10:00"}]""", "calendar:write")]
    [InlineData("calendar.updateEvent", """["e1",{"title":"y"}]""", "calendar:write")]
    [InlineData("calendar.deleteEvent", """["e1"]""", "calendar:write")]
    [InlineData("calendar.undoDelete", """["token"]""", "calendar:write")]
    [InlineData("calendar.respond", """["m1","accepted"]""", "calendar:respond")]
    [InlineData("tasks.list", "[]", "tasks:read")]
    [InlineData("tasks.get", """["t1"]""", "tasks:read")]
    [InlineData("tasks.create", """[{"title":"x"}]""", "tasks:write")]
    [InlineData("tasks.update", """["t1",{"title":"y"}]""", "tasks:write")]
    [InlineData("tasks.delete", """["t1"]""", "tasks:write")]
    [InlineData("tasks.setComplete", """["t1",true]""", "tasks:write")]
    [InlineData("tasks.acknowledgeAlert", """["t1","a1"]""", "tasks:write")]
    public async Task Every_method_needs_its_own_permission_and_a_neighbouring_one_is_not_enough(string method, string args, string needed)
    {
        var neighbour = needed switch
        {
            "calendar:read" => "tasks:read",
            "calendar:write" => "calendar:read",
            "calendar:respond" => "calendar:write",
            "tasks:read" => "calendar:read",
            _ => "tasks:read",
        };
        var rig = await StartAsync(neighbour);
        var calls = rig.Server.ApiCalls.Count;
        var denied = await rig.CallAsync(method, args);
        Assert.Equal(PluginErrorCode.PermissionDenied, denied.Error);
        Assert.Equal(calls, rig.Server.ApiCalls.Count);
        Assert.Equal(0, rig.Sets);
        Assert.Equal(0, rig.Writer.Calls);
        var allowed = await StartAsync(needed);
        // the right permission gets past the gate (the call may still fail on what it names)
        Assert.NotEqual(PluginErrorCode.PermissionDenied, (await allowed.CallAsync(method, args)).Error);
    }

    [Fact]
    public async Task A_grant_that_does_not_name_the_active_login_gets_no_session()
    {
        var grant = new Barbican.Core.Plugins.Permissions.PluginGrantSnapshot(new Barbican.Core.Plugins.Packages.PluginPackageIdentity("probe", "1.0.0", new string('a', 64)), 1, true, ["calendar:read"], ["other"]);
        var rig = await Rig.StartAsync(grant);
        Assert.Equal(PluginErrorCode.NoSession, (await rig.CallAsync("calendar.listCalendars", "[]")).Error);
        Assert.Equal(0, rig.Sets);
    }

    [Fact]
    public async Task A_login_change_while_the_calendar_is_read_drops_the_answer()
    {
        var rig = await StartAsync();
        rig.SpyStore.OnSnapshot = () => rig.Sessions.Valid = false;
        var result = await rig.CallAsync("calendar.getEvents", """[{"from":"2026-11-01","to":"2026-11-30"}]""");
        Assert.Equal(PluginErrorCode.StaleContext, result.Error);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task A_revoked_add_on_while_the_calendar_is_read_drops_the_answer()
    {
        var rig = await StartAsync();
        rig.SpyStore.OnSnapshot = () => rig.Authority.Current = Rig.Grant("tasks:read");
        Assert.Equal(PluginErrorCode.StaleContext, (await rig.CallAsync("calendar.listCalendars", "[]")).Error);
    }

    [Fact]
    public async Task A_generation_change_after_the_call_refuses_delivery()
    {
        var rig = await StartAsync();
        var result = await rig.CallAsync("calendar.listCalendars", "[]");
        Assert.True(result.IsSuccess);
        var wrote = 0;
        Assert.True(result.TryWriteForDelivery(rig.Binding, rig.Authority.Current, () => wrote++));
        rig.Sessions.Valid = false;
        Assert.False(result.TryWriteForDelivery(rig.Binding, rig.Authority.Current, () => wrote++));
        Assert.Equal(1, wrote);
    }
}
