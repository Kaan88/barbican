// Source: Bulwark lib/plugin-sandbox/host-api-calendar.ts:571-1073 (tasks.list/get/create/update/acknowledgeAlert/setComplete/delete: fields, limits, map merging, default alerts, one write at a time), lib/plugin-projection.ts:163-194
// (taskToView), host-api.ts:315-321 (tasks:read, tasks:write), lib/calendar-editability.ts (the calendar's rights gate a write). The real dispatcher over the real writer and hook bus on the fake JMAP server.
using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Tests.Calendar.Editing;
using Rig = Barbican.Core.Tests.Plugins.Api.PluginCalendarRig;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class PluginTaskMethodsTests
{
    private static readonly string[] TaskKeys = ["alerts", "calendarId", "description", "dueDate", "id", "isComplete", "keywords", "links", "priority", "progress", "relatedTo", "title", "uid", "useDefaultAlerts"];

    private static JsonObject Alert(string offset = "-PT15M", string? acknowledged = null)
    {
        var alert = new JsonObject { ["@type"] = "Alert", ["trigger"] = new JsonObject { ["@type"] = "OffsetTrigger", ["offset"] = offset, ["relativeTo"] = "end" }, ["action"] = "display" };
        if (acknowledged is not null) alert["acknowledged"] = acknowledged;
        return alert;
    }

    private static JsonObject OpenTask(string id, string title, string progress = "needs-action") => new()
    {
        ["@type"] = "Task", ["id"] = id, ["uid"] = "u-" + id, ["title"] = title, ["description"] = "Notes", ["due"] = "2026-11-12T00:00:00", ["showWithoutTime"] = true, ["priority"] = 5, ["progress"] = progress,
    };

    private static Task<Rig> StartAsync(JsonObject? workRights = null, Action<FakeCalendarsFacade>? seed = null) =>
        Rig.StartAsync(Rig.Grant("tasks:read", "tasks:write", "calendar:read"), (cals, work, home) =>
        {
            var rent = OpenTask("t1", "Pay rent");
            rent["keywords"] = new JsonObject { ["money"] = true };
            rent["alerts"] = new JsonObject { ["a1"] = Alert(acknowledged: "2026-11-01T10:00:00Z") };
            rent["links"] = new JsonObject { ["l1"] = new JsonObject { ["@type"] = "Link", ["href"] = "https://bank.example/bill", ["rel"] = "describedby", ["title"] = "Bill" } };
            rent["relatedTo"] = new JsonObject { ["<m1@example.org>"] = new JsonObject { ["@type"] = "Relation", ["relation"] = new JsonObject { ["parent"] = true } } };
            _ = cals.AddEvent(work, rent);
            _ = cals.AddEvent(work, OpenTask("t2", "Old chore", "completed"));
            var defaults = OpenTask("t3", "Defaults");
            defaults["useDefaultAlerts"] = true;
            defaults["alerts"] = new JsonObject { ["d1"] = Alert("-PT1H") };
            _ = cals.AddEvent(home, defaults);
            _ = cals.AddEvent(work, EditingRig.Series());
            seed?.Invoke(new FakeCalendarsFacade(cals, work, home));
        }, workRights);

    [Fact]
    public async Task List_hides_finished_tasks_events_and_other_calendars_unless_asked()
    {
        var rig = await StartAsync();
        var open = (await rig.CallAsync("tasks.list", "[]")).Value!;
        Assert.Equal(["t1", "t3"], Enumerable.Range(0, open.Count).Select(i => Rig.Text(open.Item(i), "id")).Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        Assert.Equal(TaskKeys, Rig.Keys(open.Item(0)), StringComparer.Ordinal);
        Assert.Equal(3, (await rig.CallAsync("tasks.list", """[{"includeCompleted":true}]""")).Value!.Count);
        var home = (await rig.CallAsync("tasks.list", $$"""[{"calendarIds":["{{rig.Home}}"],"includeCompleted":true}]""")).Value!;
        Assert.Equal("t3", Rig.Text(home.Item(0), "id"), StringComparer.Ordinal);
        Assert.Equal(1, home.Count);
        foreach (var bad in new[] { """[{"includeCompleted":"yes"}]""", """[{"calendarIds":[]}]""", """[{"calendarIds":"x"}]""", """[{"other":1}]""", "[5]" })
        {
            Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("tasks.list", bad)).Error);
        }
    }

    [Fact]
    public async Task Get_projects_the_task_with_its_alerts_links_relations_and_keywords()
    {
        var rig = await StartAsync();
        var task = (await rig.CallAsync("tasks.get", """["t1"]""")).Value!;
        Assert.Equal(TaskKeys, Rig.Keys(task), StringComparer.Ordinal);
        Assert.Equal(["Pay rent", "needs-action", "2026-11-12T00:00:00", rig.Work], [Rig.Text(task, "title"), Rig.Text(task, "progress"), Rig.Text(task, "dueDate"), Rig.Text(task, "calendarId")], StringComparer.Ordinal);
        Assert.False(task.Property("isComplete").BooleanValue);
        Assert.Equal(5, Rig.Number(task, "priority"));
        Assert.Equal("money", task.Property("keywords").Item(0).StringValue, StringComparer.Ordinal);
        var a1 = task.Property("alerts").Property("a1");
        Assert.Equal(["acknowledged", "action", "trigger"], Rig.Keys(a1), StringComparer.Ordinal);
        Assert.Equal(["offset", "relativeTo"], Rig.Keys(a1.Property("trigger")), StringComparer.Ordinal);
        Assert.Equal("due", Rig.Text(a1.Property("trigger"), "relativeTo"), StringComparer.Ordinal);
        Assert.Equal("2026-11-01T10:00:00Z", Rig.Text(a1, "acknowledged"), StringComparer.Ordinal);
        var link = task.Property("links").Property("l1");
        Assert.Equal(["contentType", "href", "rel", "title"], Rig.Keys(link), StringComparer.Ordinal);
        Assert.Equal(PluginValueKind.Null, link.Property("contentType").Kind);
        Assert.Equal("parent", task.Property("relatedTo").Property("<m1@example.org>").Property("relation").Item(0).StringValue, StringComparer.Ordinal);
        Assert.Equal(PluginValueKind.Null, (await rig.CallAsync("tasks.get", """["nope"]""")).Value!.Kind);
        Assert.Equal(PluginValueKind.Null, (await rig.CallAsync("tasks.get", """["s"]""")).Value!.Kind);
        Assert.True((await rig.CallAsync("tasks.get", """["t2"]""")).Value!.Property("isComplete").BooleanValue);
    }

    [Fact]
    public async Task A_created_task_is_stored_in_the_default_calendar_with_its_maps_and_runs_the_hooks()
    {
        var rig = await StartAsync();
        var heard = await EditingRig.HeardAsync<EventHookPayload>(rig.Hooks, HookNames.OnTaskCreate, async () =>
        {
            var created = await rig.CallAsync("tasks.create", """
                [{"title":" Call dentist ","description":"Notes","due":"2026-11-20T15:00","priority":3,"keywords":{"home":true},
                  "alerts":{"r":{"trigger":{"offset":"-PT30M","relativeTo":"due"},"action":"email"}},
                  "links":{"l":{"href":"https://x.example/page","title":"Page","rel":"related"}},"relatedTo":{"<m@x.example>":{"relation":["parent","first"]}}}]
                """);
            Assert.True(created.IsSuccess);
            var view = created.Value!;
            Assert.Equal(TaskKeys, Rig.Keys(view), StringComparer.Ordinal);
            Assert.Equal(["Call dentist", "2026-11-20T15:00:00", rig.Work], [Rig.Text(view, "title"), Rig.Text(view, "dueDate"), Rig.Text(view, "calendarId")], StringComparer.Ordinal);
            Assert.Equal("email", Rig.Text(view.Property("alerts").Property("r"), "action"), StringComparer.Ordinal);
        });
        Assert.NotNull(heard.EventId);
        var stored = rig.Cals.Events().Single(e => string.Equals((string?)e["title"], "Call dentist", StringComparison.Ordinal));
        Assert.Equal(["Task", "needs-action", "Europe/Berlin"], [(string)stored["@type"]!, (string)stored["progress"]!, (string)stored["timeZone"]!], StringComparer.Ordinal);
        Assert.False((bool?)stored["showWithoutTime"]);
        Assert.False((bool?)stored["useDefaultAlerts"]);
        Assert.Equal("end", (string?)stored["alerts"]!["r"]!["trigger"]!["relativeTo"]);
        Assert.Equal("OffsetTrigger", (string?)stored["alerts"]!["r"]!["trigger"]!["@type"]);
        Assert.True((bool?)stored["relatedTo"]!["<m@x.example>"]!["relation"]!["first"]);
        Assert.Equal(1, rig.Sets);
    }

    [Fact]
    public async Task A_task_with_only_a_day_is_all_day_and_without_a_due_has_none()
    {
        var rig = await StartAsync();
        var day = (await rig.CallAsync("tasks.create", """[{"title":"Day","due":"2026-11-20"}]""")).Value!;
        var stored = rig.Event(Rig.Text(day, "id"));
        Assert.Equal("2026-11-20T00:00:00", (string?)stored["due"]);
        Assert.True((bool?)stored["showWithoutTime"]);
        var none = (await rig.CallAsync("tasks.create", """[{"title":"No due"}]""")).Value!;
        Assert.Equal(PluginValueKind.Null, none.Property("dueDate").Kind);
        Assert.Equal(0, Rig.Number(none, "priority"));
    }

    [Fact]
    public async Task Bad_tasks_are_refused_before_anything_is_written()
    {
        var rig = await StartAsync();
        string[] bad =
        [
            "[]", "[{}]", """[{"title":""}]""", """[{"title":"x","priority":10}]""", """[{"title":"x","priority":1.5}]""", """[{"title":"x","due":"2026-11-20T10:00:00Z"}]""", """[{"title":"x","due":"later"}]""",
            """[{"title":"x","bogus":1}]""", """[{"title":"x","alerts":{"bad key":{"trigger":{"offset":"-PT5M"}}}}]""", """[{"title":"x","alerts":{"a":{"trigger":{"offset":"-PT5X"}}}}]""",
            """[{"title":"x","alerts":{"a":{"trigger":{"offset":"P400D"}}}}]""", """[{"title":"x","alerts":{"a":{"trigger":{"offset":"PT"}}}}]""", """[{"title":"x","alerts":{"a":{"trigger":{"offset":"-PT5M","extra":1}}}}]""",
            """[{"title":"x","alerts":{"a":{"trigger":{"offset":"-PT5M"},"action":"sms"}}}]""", """[{"title":"x","alerts":{"a":{"trigger":{"at":"2026-11-20T10:00:00"}}}}]""", """[{"title":"x","alerts":{"a":{}}}]""",
            """[{"title":"x","alerts":{"a":{"trigger":{"offset":"-PT5M","relativeTo":"middle"}}}}]""", """[{"title":"x","alerts":[]}]""",
            """[{"title":"x","links":{"l":{"href":"javascript:alert(1)"}}}]""", """[{"title":"x","links":{"l":{"href":"https://user:pw@x.example/"}}}]""", """[{"title":"x","links":{"l":{"href":"/mail/inbox"}}}]""",
            """[{"title":"x","links":{"l":{"href":"mailto:a@b.example?subject=x"}}}]""", """[{"title":"x","links":{"l":{"href":"https://x.example/a b"}}}]""", """[{"title":"x","links":{"l":{"href":"https://x.example","rel":"Bad Rel"}}}]""",
            """[{"title":"x","relatedTo":{"two words":{}}}]""", """[{"title":"x","relatedTo":{"<a@b>":{"relation":["Parent"]}}}]""", """[{"title":"x","relatedTo":{"<a@b>":{"relation":["a","b","c","d","e"]}}}]""",
            """[{"title":"x","keywords":{"k":false}}]""", """[{"title":"x","keywords":{"bad key":true}}]""", """[{"title":"x","keywords":{"a":1}}]""",
        ];
        foreach (var args in bad) Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("tasks.create", args)).Error);
        var many = string.Join(',', Enumerable.Range(0, 6).Select(i => "\"a" + i + "\":{\"trigger\":{\"offset\":\"-PT" + (i + 1) + "M\"}}"));
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("tasks.create", "[{\"title\":\"x\",\"alerts\":{" + many + "}}]")).Error);
        Assert.Equal(0, rig.Sets);
        Assert.Equal(0, rig.Writer.Calls);
    }

    [Fact]
    public async Task Creating_in_a_read_only_calendar_is_refused_and_another_add_on_can_stop_it()
    {
        var locked = await StartAsync(new JsonObject { ["mayReadItems"] = true });
        Assert.Equal(PluginErrorCode.PermissionDenied, (await locked.CallAsync("tasks.create", """[{"title":"x"}]""")).Error);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await locked.CallAsync("tasks.update", """["t1",{"title":"y"}]""")).Error);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await locked.CallAsync("tasks.setComplete", """["t1",true]""")).Error);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await locked.CallAsync("tasks.acknowledgeAlert", """["t1","a1"]""")).Error);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await locked.CallAsync("tasks.delete", """["t1"]""")).Error);
        Assert.Equal(0, locked.Sets);
        Assert.Equal(0, locked.Writer.Calls);
        Assert.True((await locked.CallAsync("tasks.create", $$"""[{"title":"x","calendarId":"{{locked.Home}}"}]""")).IsSuccess);
        var rig = await StartAsync();
        using var stop = rig.Hooks.InterceptAny(HookNames.OnBeforeTaskCreate, (_, _) => ValueTask.FromResult(false));
        Assert.Equal(PluginValueKind.Null, (await rig.CallAsync("tasks.create", """[{"title":"x"}]""")).Value!.Kind);
        Assert.Equal(0, rig.Sets);
    }

    [Fact]
    public async Task An_update_changes_the_fields_named_and_clears_a_due_date_with_null()
    {
        var rig = await StartAsync();
        var result = await rig.CallAsync("tasks.update", """["t1",{"title":"Pay the rent","priority":1,"due":"2026-11-14T09:00","description":"Soon"}]""");
        Assert.Equal(PluginValueKind.Undefined, result.Value!.Kind);
        var stored = rig.Event("t1");
        Assert.Equal(["Pay the rent", "2026-11-14T09:00:00", "Soon", "Europe/Berlin"], [(string)stored["title"]!, (string)stored["due"]!, (string)stored["description"]!, (string)stored["timeZone"]!], StringComparer.Ordinal);
        Assert.Equal(1, (int?)stored["priority"]);
        Assert.False((bool?)stored["showWithoutTime"]);
        Assert.True((await rig.CallAsync("tasks.update", """["t1",{"due":null}]""")).IsSuccess);
        Assert.Null(rig.Event("t1")["due"]);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("tasks.update", """["t1",{}]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("tasks.update", """["nope",{"title":"x"}]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("tasks.update", """["s",{"title":"x"}]""")).Error);
    }

    [Fact]
    public async Task Map_changes_merge_by_key_and_leave_what_the_plugin_did_not_name()
    {
        var rig = await StartAsync();
        Assert.True((await rig.CallAsync("tasks.update", """["t1",{"keywords":{"home":true,"money":null},"links":{"l2":{"href":"mailto:bob@x.example"}},"relatedTo":{"<n@x.example>":{"relation":["child"]}}}]""")).IsSuccess);
        var stored = rig.Event("t1");
        Assert.Equal(["home"], stored["keywords"]!.AsObject().Select(p => p.Key).ToArray(), StringComparer.Ordinal);
        Assert.Equal(["l1", "l2"], stored["links"]!.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        Assert.Equal(2, stored["relatedTo"]!.AsObject().Count);
        Assert.Equal("https://bank.example/bill", (string?)stored["links"]!["l1"]!["href"]);
        Assert.True((await rig.CallAsync("tasks.update", """["t1",{"links":null}]""")).IsSuccess);
        Assert.Null(rig.Event("t1")["links"]);
        Assert.NotNull(rig.Event("t1")["alerts"]);
    }

    [Fact]
    public async Task An_alert_kept_as_it_is_stays_dismissed_and_a_new_trigger_makes_it_fire_again()
    {
        var rig = await StartAsync();
        var sets = rig.Sets;
        Assert.True((await rig.CallAsync("tasks.update", """["t1",{"alerts":{"a1":{"trigger":{"offset":"-PT15M","relativeTo":"due"}}}}]""")).IsSuccess);
        Assert.Equal(sets, rig.Sets);
        Assert.Equal("2026-11-01T10:00:00Z", (string?)rig.Event("t1")["alerts"]!["a1"]!["acknowledged"]);
        Assert.True((await rig.CallAsync("tasks.update", """["t1",{"alerts":{"a1":{"trigger":{"offset":"-PT5M"}},"a2":{"trigger":{"at":"2026-11-11T07:30:00Z"}}}}]""")).IsSuccess);
        var alerts = rig.Event("t1")["alerts"]!;
        Assert.Null(alerts["a1"]!["acknowledged"]);
        Assert.Equal("-PT5M", (string?)alerts["a1"]!["trigger"]!["offset"]);
        Assert.Equal("AbsoluteTrigger", (string?)alerts["a2"]!["trigger"]!["@type"]);
        Assert.Equal("2026-11-11T07:30:00Z", (string?)alerts["a2"]!["trigger"]!["when"]);
        Assert.True((await rig.CallAsync("tasks.update", """["t1",{"alerts":null}]""")).IsSuccess);
        Assert.Null(rig.Event("t1")["alerts"]);
    }

    [Fact]
    public async Task A_task_holds_at_most_five_alerts_counting_those_already_there()
    {
        var rig = await StartAsync();
        Assert.True((await rig.CallAsync("tasks.update", """["t1",{"alerts":{"b":{"trigger":{"offset":"-PT1M"}},"c":{"trigger":{"offset":"-PT2M"}},"d":{"trigger":{"offset":"-PT3M"}},"e":{"trigger":{"offset":"-PT4M"}}}}]""")).IsSuccess);
        Assert.Equal(5, rig.Event("t1")["alerts"]!.AsObject().Count);
        var sets = rig.Sets;
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("tasks.update", """["t1",{"alerts":{"f":{"trigger":{"offset":"-PT5M"}}}}]""")).Error);
        Assert.True((await rig.CallAsync("tasks.update", """["t1",{"alerts":{"f":{"trigger":{"offset":"-PT5M"}},"b":null}}]""")).IsSuccess);
        Assert.Equal(sets + 1, rig.Sets);
    }

    [Fact]
    public async Task A_task_on_its_calendars_default_alerts_gets_its_own_under_new_ids()
    {
        var rig = await StartAsync();
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("tasks.update", """["t3",{"alerts":{"d1":{"trigger":{"offset":"-PT5M"}}}}]""")).Error);
        Assert.True((await rig.CallAsync("tasks.update", """["t3",{"alerts":{"mine":{"trigger":{"offset":"-PT5M"}}}}]""")).IsSuccess);
        var stored = rig.Event("t3");
        Assert.False((bool?)stored["useDefaultAlerts"]);
        var alerts = stored["alerts"]!.AsObject();
        Assert.Equal(2, alerts.Count);
        Assert.False(alerts.ContainsKey("d1"));
        Assert.Equal("-PT1H", (string?)alerts.First(p => !string.Equals(p.Key, "mine", StringComparison.Ordinal)).Value!["trigger"]!["offset"]);
        Assert.Equal(PluginErrorCode.Failed, (await rig.CallAsync("tasks.acknowledgeAlert", """["t3","d1"]""")).Error);
    }

    [Fact]
    public async Task Set_complete_ticks_like_the_tasks_view_runs_the_toggle_hook_and_leaves_a_task_already_there_alone()
    {
        var rig = await StartAsync();
        var heard = await EditingRig.HeardAsync<TaskToggledPayload>(rig.Hooks, HookNames.OnTaskToggleComplete, async () => Assert.True((await rig.CallAsync("tasks.setComplete", """["t1",true]""")).IsSuccess));
        Assert.True(heard.Complete);
        Assert.Equal("completed", (string?)rig.Event("t1")["progress"]);
        var sets = rig.Sets;
        Assert.True((await rig.CallAsync("tasks.setComplete", """["t1",true]""")).IsSuccess);
        Assert.Equal(sets, rig.Sets);
        Assert.True((await rig.CallAsync("tasks.setComplete", """["t1",false]""")).IsSuccess);
        Assert.Equal("needs-action", (string?)rig.Event("t1")["progress"]);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("tasks.setComplete", """["t1","yes"]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("tasks.setComplete", """["nope",true]""")).Error);
    }

    [Fact]
    public async Task An_alert_is_acknowledged_at_a_time_now_or_not_at_all()
    {
        var rig = await StartAsync();
        Assert.True((await rig.CallAsync("tasks.acknowledgeAlert", """["t1","a1","2026-11-10T08:00:00Z"]""")).IsSuccess);
        Assert.Equal("2026-11-10T08:00:00Z", (string?)rig.Event("t1")["alerts"]!["a1"]!["acknowledged"]);
        Assert.Equal("2026-11-10T08:00:00Z", Rig.Text((await rig.CallAsync("tasks.get", """["t1"]""")).Value!.Property("alerts").Property("a1"), "acknowledged"), StringComparer.Ordinal);
        var sets = rig.Sets;
        Assert.True((await rig.CallAsync("tasks.acknowledgeAlert", """["t1","a1","2026-11-10T08:00:00Z"]""")).IsSuccess);
        Assert.Equal(sets, rig.Sets);
        Assert.True((await rig.CallAsync("tasks.acknowledgeAlert", """["t1","a1",null]""")).IsSuccess);
        Assert.Null(rig.Event("t1")["alerts"]!["a1"]!["acknowledged"]);
        Assert.True((await rig.CallAsync("tasks.acknowledgeAlert", """["t1","a1"]""")).IsSuccess);
        Assert.Equal("2026-11-09T08:00:00Z", (string?)rig.Event("t1")["alerts"]!["a1"]!["acknowledged"]);
        Assert.Equal(PluginErrorCode.Failed, (await rig.CallAsync("tasks.acknowledgeAlert", """["t1","nope"]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("tasks.acknowledgeAlert", """["t1","a1","2026-11-10"]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("tasks.acknowledgeAlert", """["t1","a1",5]""")).Error);
    }

    [Fact]
    public async Task Delete_removes_the_task_and_runs_the_hooks()
    {
        var rig = await StartAsync();
        var heard = await EditingRig.HeardAsync<DeleteHookPayload>(rig.Hooks, HookNames.OnTaskDelete, async () => Assert.True((await rig.CallAsync("tasks.delete", """["t1"]""")).IsSuccess));
        Assert.Equal("t1", heard.Id, StringComparer.Ordinal);
        Assert.DoesNotContain(rig.Cals.Events(), e => string.Equals((string?)e["id"], "t1", StringComparison.Ordinal));
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("tasks.delete", """["t1"]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("tasks.delete", """["s"]""")).Error);
        Assert.Equal(1, rig.Sets);
    }

    [Fact]
    public async Task Two_updates_of_one_task_at_once_both_land()
    {
        var rig = await StartAsync();
        var first = rig.CallAsync("tasks.update", """["t1",{"keywords":{"k1":true}}]""");
        var second = rig.CallAsync("tasks.update", """["t1",{"keywords":{"k2":true}}]""");
        var results = await Task.WhenAll(first, second);
        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.Equal(["k1", "k2", "money"], rig.Event("t1")["keywords"]!.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_stale_login_stops_a_write_and_a_change_during_it_reports_an_unknown_outcome()
    {
        var rig = await StartAsync();
        rig.SpyStore.OnSnapshot = () => rig.Sessions.Valid = false;
        Assert.Equal(PluginErrorCode.StaleContext, (await rig.CallAsync("tasks.setComplete", """["t1",true]""")).Error);
        rig.Sessions.Valid = true;
        Assert.Equal(PluginErrorCode.StaleContext, (await rig.CallAsync("tasks.list", "[]")).Error);
        Assert.Equal(0, rig.Sets);
        var during = await StartAsync();
        during.Writer.Before = () => during.Authority.Current = Rig.Grant("tasks:read");
        Assert.Equal(PluginErrorCode.UnknownOutcome, (await during.CallAsync("tasks.delete", """["t1"]""")).Error);
        Assert.Equal(1, during.Sets);
    }

    internal sealed class FakeCalendarsFacade(Barbican.FakeJmap.FakeCalendars cals, string work, string home)
    {
        public string Work { get; } = work;

        public string Home { get; } = home;

        public void Add(string calendar, JsonObject ev) => cals.AddEvent(calendar, ev);
    }
}
