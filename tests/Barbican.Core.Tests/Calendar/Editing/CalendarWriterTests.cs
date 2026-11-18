// New tests for the event and task writes (M4 stream cal-edit acceptance): scheduling messages, the three scopes with their patches, the two-step "following" with its restore, drag with rollback, task ticks and their hooks, shared accounts.

using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Plugins.Hooks;
using static Barbican.Core.Tests.Calendar.CalendarTestData;

namespace Barbican.Core.Tests.Calendar.Editing;

public sealed class CalendarWriterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static EventForm Form(EditingRig rig) => new()
    {
        Title = "Review",
        Start = Dt("2026-11-20T10:00:00"),
        End = Dt("2026-11-20T11:00:00"),
        TimeZone = "UTC",
        Calendar = rig.Work,
        Send = true,
        Me = [EditingRig.Me],
        Attendees = [new CalendarPerson("Ann", "ann@x.example"), new CalendarPerson("Bob", "bob@y.example")],
    };

    private static async Task<EditingRig> SeriesRigAsync() => await EditingRig.CreateAsync((cals, work) => cals.AddEvent(work, EditingRig.Series()));

    [Fact]
    public async Task A_new_event_with_two_guests_is_one_call_with_scheduling_messages()
    {
        var rig = await EditingRig.CreateAsync();
        var (data, send) = EventEditor.BuildData(Form(rig), null, null);
        var id = await rig.Writer.SaveEventAsync(new EventWrite(EditingRig.Account, null, data, EditScope.WholeEvent, null, send), Ct);
        Assert.True(id.IsOk, id.Error?.Message);
        var set = Assert.Single(rig.Sets);
        Assert.Equal("CalendarEvent", set.Type);
        Assert.True((bool?)set.Args["sendSchedulingMessages"]);
        Assert.Equal([("create", id.Value!)], rig.Cals.SchedulingSent);
        Assert.Equal(3, rig.Cals.Events().Single()["participants"]!.AsObject().Count);
        Assert.Equal(1, rig.Store.Refreshes);
    }

    [Fact]
    public async Task Without_guests_or_without_the_notify_choice_no_scheduling_messages_are_asked_for()
    {
        var rig = await EditingRig.CreateAsync();
        var (data, send) = EventEditor.BuildData(Form(rig) with { Attendees = [] }, null, null);
        _ = await rig.Writer.SaveEventAsync(new EventWrite(EditingRig.Account, null, data, EditScope.WholeEvent, null, send), Ct);
        (data, send) = EventEditor.BuildData(Form(rig) with { Send = false }, null, null);
        _ = await rig.Writer.SaveEventAsync(new EventWrite(EditingRig.Account, null, data, EditScope.WholeEvent, null, send), Ct);
        Assert.All(rig.Sets, s => Assert.False(s.Args.ContainsKey("sendSchedulingMessages")));
        Assert.Empty(rig.Cals.SchedulingSent);
    }

    [Fact]
    public async Task One_occurrence_is_an_override_on_the_master_and_nothing_else()
    {
        var rig = await SeriesRigAsync();
        var master = rig.Store.Event("s")!;
        var form = new EventForm { Title = "Stand-up (moved)", Start = Dt("2026-11-04T09:00:00"), End = Dt("2026-11-04T09:15:00"), TimeZone = "UTC", Calendar = rig.Work, Repeat = 1, Me = [EditingRig.Me], Attendees = EventEditor.AttendeesOf(master) };
        var (data, _) = EventEditor.BuildData(form, master, (Dt("2026-11-04T09:00:00"), Dt("2026-11-04T09:15:00")));
        var r = await rig.Writer.SaveEventAsync(new EventWrite(EditingRig.Account, "s", data, EditScope.Occurrence, "2026-11-04T09:00:00", false), Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        var update = Assert.Single(rig.Sets).Args["update"]!["s"]!.AsObject();
        var over = Assert.Single(update);
        Assert.Equal("recurrenceOverrides", over.Key);
        Assert.Equal("Stand-up (moved)", (string?)over.Value!["2026-11-04T09:00:00"]!["title"]);

        // a second override goes in as a pointer into the map
        data["title"] = "Stand-up (again)";
        var again = await rig.Writer.SaveEventAsync(new EventWrite(EditingRig.Account, "s", data, EditScope.Occurrence, "2026-11-05T09:00:00", false), Ct);
        Assert.True(again.IsOk, again.Error?.Message);
        Assert.Equal(["recurrenceOverrides/2026-11-05T09:00:00"], rig.Sets[^1].Args["update"]!["s"]!.AsObject().Select(kv => kv.Key).ToArray());
    }

    [Fact]
    public async Task This_and_following_is_two_calls_and_a_failing_second_restores_the_series()
    {
        var rig = await SeriesRigAsync();
        var master = rig.Store.Event("s")!;
        var form = new EventForm { Title = "Stand-up (new)", Start = Dt("2026-11-04T09:00:00"), End = Dt("2026-11-04T09:15:00"), TimeZone = "UTC", Calendar = rig.Work, Repeat = 1, Me = [EditingRig.Me], Attendees = EventEditor.AttendeesOf(master) };
        var (data, _) = EventEditor.BuildData(form, master, (Dt("2026-11-04T09:00:00"), Dt("2026-11-04T09:15:00")));
        var ok = await rig.Writer.SaveEventAsync(new EventWrite(EditingRig.Account, "s", data, EditScope.Following, "2026-11-04T09:00:00", false), Ct);
        Assert.True(ok.IsOk, ok.Error?.Message);
        Assert.Equal(2, rig.Sets.Count);
        Assert.Equal("2026-11-04T08:59:59", (string?)rig.Cals.Events().First(e => string.Equals((string?)e["id"], "s", StringComparison.Ordinal))["recurrenceRules"]![0]!["until"]);
        Assert.Equal(2, rig.Cals.Events().Count);

        var rig2 = await SeriesRigAsync();
        rig2.Server.Api.FailNextSet("forbidden", skip: 1);
        var failed = await rig2.Writer.SaveEventAsync(new EventWrite(EditingRig.Account, "s", data, EditScope.Following, "2026-11-04T09:00:00", false), Ct);
        Assert.False(failed.IsOk);
        Assert.Equal(3, rig2.Sets.Count);
        var restored = rig2.Cals.Events().Single();
        Assert.Equal(5, (int?)restored["recurrenceRules"]![0]!["count"]);
        Assert.Null(restored["recurrenceRules"]![0]!["until"]);
        Assert.Equal(["s"], rig2.Store.Restores);
    }

    [Fact]
    public async Task All_moves_the_series_as_far_as_the_occurrence_did()
    {
        var rig = await SeriesRigAsync();
        var master = rig.Store.Event("s")!;
        var form = new EventForm { Title = "Stand-up", Start = Dt("2026-11-04T09:30:00"), End = Dt("2026-11-04T09:45:00"), TimeZone = "UTC", Calendar = rig.Work, Repeat = 1, Me = [EditingRig.Me], Attendees = EventEditor.AttendeesOf(master) };
        var (data, _) = EventEditor.BuildData(form, master, (Dt("2026-11-04T09:00:00"), Dt("2026-11-04T09:15:00")));
        var r = await rig.Writer.SaveEventAsync(new EventWrite(EditingRig.Account, "s", data, EditScope.All, "2026-11-04T09:00:00", false), Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        Assert.Equal("2026-11-02T09:30:00", (string?)rig.Cals.Events().Single()["start"]);
    }

    [Fact]
    public async Task Deleting_one_occurrence_adds_an_excluded_override_and_deleting_following_cuts_the_series()
    {
        var rig = await SeriesRigAsync();
        var r = await rig.Writer.DeleteEventAsync(EditingRig.Account, "s", EditScope.Occurrence, "2026-11-03T09:00:00", true, Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        Assert.True((bool?)rig.Cals.Events().Single()["recurrenceOverrides"]!["2026-11-03T09:00:00"]!["excluded"]);
        Assert.True((bool?)rig.Sets[0].Args["sendSchedulingMessages"]);
        var f = await rig.Writer.DeleteEventAsync(EditingRig.Account, "s", EditScope.Following, "2026-11-05T09:00:00", false, Ct);
        Assert.True(f.IsOk, f.Error?.Message);
        Assert.Equal("2026-11-05T08:59:59", (string?)rig.Cals.Events().Single()["recurrenceRules"]![0]!["until"]);
        var all = await rig.Writer.DeleteEventAsync(EditingRig.Account, "s", EditScope.All, null, false, Ct);
        Assert.True(all.IsOk, all.Error?.Message);
        Assert.Empty(rig.Cals.Events());
    }

    [Fact]
    public async Task Deleting_following_from_the_first_occurrence_deletes_the_whole_series()
    {
        var rig = await SeriesRigAsync();
        var r = await rig.Writer.DeleteEventAsync(EditingRig.Account, "s", EditScope.Following, "2026-11-02T09:00:00", false, Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        Assert.Empty(rig.Cals.Events());
    }

    [Fact]
    public async Task A_drag_patches_start_and_duration_notifies_guests_and_a_drag_that_changes_nothing_is_no_call()
    {
        var rig = await EditingRig.CreateAsync((cals, work) =>
        {
            var e = EditingRig.Series("single");
            e.Remove("recurrenceRules");
            e["timeZone"] = null;
            _ = cals.AddEvent(work, e);
        });
        var same = await rig.Writer.MoveAsync(EditingRig.Account, "single", Dt("2026-11-02T09:00:00"), TimeSpan.FromMinutes(15), Ct);
        Assert.True(same.IsOk);
        Assert.Empty(rig.Sets);

        var moved = await rig.Writer.MoveAsync(EditingRig.Account, "single", Dt("2026-11-02T10:15:00"), TimeSpan.FromMinutes(45), Ct);
        Assert.True(moved.IsOk, moved.Error?.Message);
        var set = Assert.Single(rig.Sets);
        Assert.True((bool?)set.Args["sendSchedulingMessages"]);
        var patch = set.Args["update"]!["single"]!.AsObject();
        Assert.Equal("2026-11-02T10:15:00", (string?)patch["start"]);
        Assert.Equal("PT45M", (string?)patch["duration"]);
    }

    [Fact]
    public async Task A_failed_drag_restores_the_store_and_returns_the_error()
    {
        var rig = await EditingRig.CreateAsync((cals, work) =>
        {
            var e = EditingRig.Series("single");
            e.Remove("recurrenceRules");
            e["timeZone"] = null;
            _ = cals.AddEvent(work, e);
        });
        rig.Server.Api.FailNextSet("forbidden");
        var r = await rig.Writer.MoveAsync(EditingRig.Account, "single", Dt("2026-11-03T09:00:00"), TimeSpan.FromMinutes(15), Ct);
        Assert.False(r.IsOk);
        Assert.Contains("permission", r.Error!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("2026-11-02T09:00:00", (string?)rig.Store.Event("single")!["start"]);
        Assert.Equal(["single"], rig.Store.Restores);
        Assert.Equal("2026-11-02T09:00:00", (string?)rig.Cals.Events().Single()["start"]);
    }

    [Fact]
    public async Task Dragging_an_occurrence_of_a_series_writes_its_override()
    {
        var rig = await EditingRig.CreateAsync((cals, work) =>
        {
            var e = EditingRig.Series();
            e["timeZone"] = null;
            _ = cals.AddEvent(work, e);
        });
        var r = await rig.Writer.MoveAsync(EditingRig.Account, "s|2026-11-04T09:00:00", Dt("2026-11-04T11:00:00"), TimeSpan.FromMinutes(15), Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        var over = rig.Cals.Events().Single()["recurrenceOverrides"]!["2026-11-04T09:00:00"]!;
        Assert.Equal("2026-11-04T11:00:00", (string?)over["start"]);
        Assert.Null(over["duration"]);
    }

    [Fact]
    public async Task A_drag_is_written_on_the_events_own_clock()
    {
        var rig = await SeriesRigAsync();
        var local = Dt("2026-11-02T12:00:00");
        var r = await rig.Writer.MoveAsync(EditingRig.Account, "s", local, TimeSpan.FromMinutes(15), Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        var expected = TimeZoneInfo.ConvertTimeToUtc(local, TimeZoneInfo.Local).ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, (string?)rig.Cals.Events().Single()["recurrenceOverrides"]?["2026-11-02T09:00:00"]?["start"] ?? (string?)rig.Cals.Events().Single()["start"]);
    }

    [Fact]
    public async Task A_guest_event_the_user_does_not_organise_moves_without_scheduling_messages()
    {
        var rig = await EditingRig.CreateAsync((cals, work) =>
        {
            var e = EditingRig.Series("theirs");
            e.Remove("recurrenceRules");
            e["timeZone"] = null;
            e["organizerCalendarAddress"] = "mailto:boss@y.example";
            e["participants"]!["o"]!["email"] = "boss@y.example";
            e["participants"]!["o"]!["calendarAddress"] = "mailto:boss@y.example";
            _ = cals.AddEvent(work, e);
        });
        var r = await rig.Writer.MoveAsync(EditingRig.Account, "theirs", Dt("2026-11-02T12:00:00"), TimeSpan.FromMinutes(15), Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        Assert.False(rig.Sets[0].Args.ContainsKey("sendSchedulingMessages"));
    }

    [Fact]
    public async Task A_task_tick_sends_progress_and_fires_the_toggle_hook_once_per_flip()
    {
        var rig = await EditingRig.CreateAsync((cals, work) => cals.AddEvent(work, new JsonObject { ["id"] = "t1", ["@type"] = "Task", ["title"] = "Pay", ["progress"] = "needs-action" }));
        var heard = await EditingRig.HeardAsync<TaskToggledPayload>(rig.Hooks, HookNames.OnTaskToggleComplete, async () =>
        {
            var r = await rig.Writer.UpdateAsync(EditingRig.Account, "t1", new JsonObject { ["progress"] = "completed" }, Ct);
            Assert.True(r.IsOk, r.Error?.Message);
        });
        Assert.Equal(new TaskToggledPayload("t1", true), heard);
        Assert.Equal("completed", (string?)rig.Cals.Events().Single()["progress"]);
        Assert.Equal("completed", (string?)rig.Store.Event("t1")!["progress"]);
    }

    [Fact]
    public async Task Tasks_are_created_updated_and_destroyed_with_the_task_hooks()
    {
        var rig = await EditingRig.CreateAsync();
        var created = await EditingRig.HeardAsync<EventHookPayload>(rig.Hooks, HookNames.OnTaskCreate, async () =>
        {
            var r = await rig.Writer.CreateAsync(EditingRig.Account, rig.Work, new JsonObject { ["@type"] = "Task", ["title"] = "Call", ["start"] = "2026-11-20T00:00:00" }, Ct);
            Assert.True(r.IsOk, r.Error?.Message);
        });
        var id = created.EventId!;
        Assert.Equal("Task", (string?)rig.Cals.Events().Single()["@type"]);
        var deleted = await EditingRig.HeardAsync<DeleteHookPayload>(rig.Hooks, HookNames.OnTaskDelete, async () =>
        {
            var r = await rig.Writer.DestroyAsync(EditingRig.Account, id, Ct);
            Assert.True(r.IsOk, r.Error?.Message);
        });
        Assert.Equal(new DeleteHookPayload(id, "Call"), deleted);
        Assert.Empty(rig.Cals.Events());
    }

    [Fact]
    public async Task An_intercept_hook_stops_a_create_and_a_transform_changes_what_the_server_gets()
    {
        var rig = await EditingRig.CreateAsync();
        using (rig.Hooks.Intercept<EventHookPayload>(HookNames.OnBeforeEventCreate, (_, _) => ValueTask.FromResult(false)))
        {
            var (data, send) = EventEditor.BuildData(Form(rig), null, null);
            var stopped = await rig.Writer.SaveEventAsync(new EventWrite(EditingRig.Account, null, data, EditScope.WholeEvent, null, send), Ct);
            Assert.False(stopped.IsOk);
            Assert.Empty(rig.Sets);
        }

        using var shape = rig.Hooks.Transform<CreatingEventPayload>(HookNames.OnCreatingEvent, (p, _) =>
        {
            var server = (JsonObject)p.ServerEvent.DeepClone();
            server["title"] = "Renamed by a plugin";
            return ValueTask.FromResult(p with { ServerEvent = server });
        });
        var (d2, s2) = EventEditor.BuildData(Form(rig), null, null);
        _ = await rig.Writer.SaveEventAsync(new EventWrite(EditingRig.Account, null, d2, EditScope.WholeEvent, null, s2), Ct);
        Assert.Equal("Renamed by a plugin", (string?)rig.Cals.Events().Single()["title"]);
    }

    [Fact]
    public async Task A_refused_update_rolls_the_store_back_and_names_the_reason()
    {
        var rig = await SeriesRigAsync();
        rig.Server.Api.FailNextSet("forbidden");
        var r = await rig.Writer.UpdateAsync(EditingRig.Account, "s", new JsonObject { ["title"] = "Nope" }, Ct);
        Assert.False(r.IsOk);
        Assert.Equal("Stand-up", (string?)rig.Store.Event("s")!["title"]);
        Assert.Equal([("s", "Nope")], rig.Store.Patches.Select(p => (p.Id, (string)p.Patch["title"]!)).ToArray());
    }

    [Fact]
    public async Task A_conflict_check_asks_the_plugins()
    {
        var rig = await EditingRig.CreateAsync();
        Assert.Empty(await rig.Writer.CheckConflictsAsync(new JsonObject(), Ct));
        using var h = rig.Hooks.Transform<ConflictCheckPayload>(HookNames.OnCheckEventConflicts, (p, _) => ValueTask.FromResult(p with { Warnings = [.. p.Warnings, "Overlaps Lunch"] }));
        Assert.Equal(["Overlaps Lunch"], await rig.Writer.CheckConflictsAsync(new JsonObject(), Ct));
    }

    [Fact]
    public void A_shared_accounts_event_is_written_to_its_own_account()
    {
        var ev = JsonNode.Parse("""{"id": "b2~e9", "_rid": "e9", "_acc": "b2"}""")!;
        Assert.Equal(("b2", "e9"), CalendarWriter.ServerRef("primary", ev, "b2~e9"));
        Assert.Equal(("b2", "e9"), CalendarWriter.ServerRef("primary", null, "b2~e9"));
        Assert.Equal(("primary", "e9"), CalendarWriter.ServerRef("primary", null, "e9|2026-11-04T09:00:00"));
    }
}
