// Green: the FakeJmap calendar set arm (M4 plan section 4.7): Calendar/set and CalendarEvent/set create, update with pointer patches, destroy, scheduling messages recorded, invalidProperties, notFound, ifInState, invalidPatch.

using System.Text.Json.Nodes;
using Barbican.Core.Jmap;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Calendar;

public sealed class FakeCalendarSetTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (FakeJmapServer Server, FakeCalendars Cals, string Work) Setup()
    {
        var cals = new FakeCalendars();
        var work = cals.AddCalendar("Work", isDefault: true);
        return (new FakeJmapServer(new FakeJmapOptions { CalendarsCapability = true, AvailabilityCapability = true, Calendars = cals }), cals, work);
    }

    private static async Task<JsonObject> CallAsync(FakeJmapServer server, string method, JsonObject args)
    {
        var client = await FakeJmapHarness.ConnectAsync(server, TimeProvider.System, _ => { }, false, Ct);
        args["accountId"] = client.Session.MailAccountId;
        var r = await client.SendAsync(JmapRequest.Of(JmapRequest.Call(method, args, "c")), Ct);
        return r.Value!.MethodResponses[0][1]!.AsObject();
    }

    private static JsonObject Event(string calendar, string title, bool guests = false)
    {
        var e = new JsonObject
        {
            ["title"] = title,
            ["start"] = "2026-11-20T10:00:00",
            ["timeZone"] = "UTC",
            ["duration"] = "PT1H",
            ["calendarIds"] = new JsonObject { [calendar] = true },
        };
        if (guests)
        {
            e["participants"] = new JsonObject { ["p"] = new JsonObject { ["email"] = "ann@x.example", ["roles"] = new JsonObject { ["attendee"] = true } } };
        }

        return e;
    }

    [Fact]
    public async Task An_event_is_created_patched_and_destroyed()
    {
        var (server, cals, work) = Setup();
        var made = await CallAsync(server, "CalendarEvent/set", new JsonObject { ["create"] = new JsonObject { ["e"] = Event(work, "Review") } });
        var id = (string)made["created"]!["e"]!["id"]!;
        Assert.Equal("2026-11-20T11:00:00Z", (string?)made["created"]!["e"]!["utcEnd"]);
        Assert.Equal("Review", (string?)cals.Events().Single()["title"]);

        var patched = await CallAsync(server, "CalendarEvent/set", new JsonObject { ["update"] = new JsonObject { [id] = new JsonObject { ["title"] = "Review 2", ["duration"] = "PT2H" } } });
        Assert.True(patched["updated"]!.AsObject().ContainsKey(id));
        Assert.Equal("Review 2", (string?)cals.Events().Single()["title"]);
        Assert.Equal("2026-11-20T12:00:00Z", (string?)cals.Events().Single()["utcEnd"]);

        var gone = await CallAsync(server, "CalendarEvent/set", new JsonObject { ["destroy"] = new JsonArray(id) });
        Assert.Equal(id, (string?)gone["destroyed"]![0]);
        Assert.Empty(cals.Events());
        Assert.Equal(3, server.Api.SetRequests.Count);
    }

    [Fact]
    public async Task Scheduling_messages_are_recorded_only_when_asked_and_there_are_guests()
    {
        var (server, cals, work) = Setup();
        _ = await CallAsync(server, "CalendarEvent/set", new JsonObject { ["create"] = new JsonObject { ["a"] = Event(work, "Alone") }, ["sendSchedulingMessages"] = true });
        Assert.Empty(cals.SchedulingSent);
        var made = await CallAsync(server, "CalendarEvent/set", new JsonObject { ["create"] = new JsonObject { ["b"] = Event(work, "With Ann", true) }, ["sendSchedulingMessages"] = true });
        Assert.Equal([("create", (string)made["created"]!["b"]!["id"]!)], cals.SchedulingSent);
        _ = await CallAsync(server, "CalendarEvent/set", new JsonObject { ["create"] = new JsonObject { ["c"] = Event(work, "Quiet", true) } });
        Assert.Single(cals.SchedulingSent);
    }

    [Fact]
    public async Task Errors_are_per_object_and_a_pointer_into_a_missing_map_is_refused()
    {
        var (server, cals, work) = Setup();
        var bad = await CallAsync(server, "CalendarEvent/set", new JsonObject { ["create"] = new JsonObject { ["e"] = new JsonObject { ["title"] = "No calendar" } } });
        Assert.Equal("invalidProperties", (string?)bad["notCreated"]!["e"]!["type"]);
        var id = cals.AddEvent(work, new JsonObject { ["title"] = "Series", ["start"] = "2026-11-20T10:00:00" });
        var missing = await CallAsync(server, "CalendarEvent/set", new JsonObject { ["update"] = new JsonObject { [id] = new JsonObject { ["recurrenceOverrides/2026-11-21T10:00:00"] = new JsonObject { ["excluded"] = true } } } });
        Assert.Equal("invalidPatch", (string?)missing["notUpdated"]![id]!["type"]);
        var none = await CallAsync(server, "CalendarEvent/set", new JsonObject { ["update"] = new JsonObject { ["nope"] = new JsonObject { ["title"] = "x" } }, ["destroy"] = new JsonArray("nope2") });
        Assert.Equal("notFound", (string?)none["notUpdated"]!["nope"]!["type"]);
        Assert.Equal("notFound", (string?)none["notDestroyed"]!["nope2"]!["type"]);
        var stale = await CallAsync(server, "CalendarEvent/set", new JsonObject { ["ifInState"] = "CalendarEvent-0", ["create"] = new JsonObject { ["e"] = Event(work, "x") } });
        Assert.Equal("stateMismatch", (string?)stale["type"]);
        server.Api.FailNextSet("forbidden");
        var refused = await CallAsync(server, "CalendarEvent/set", new JsonObject { ["update"] = new JsonObject { [id] = new JsonObject { ["title"] = "x" } } });
        Assert.Equal("forbidden", (string?)refused["notUpdated"]![id]!["type"]);
        Assert.Equal("Series", (string?)cals.Events().Single()["title"]);
    }

    [Fact]
    public async Task Calendars_are_created_made_default_and_destroyed_with_their_events()
    {
        var (server, cals, work) = Setup();
        var made = await CallAsync(server, "Calendar/set", new JsonObject { ["create"] = new JsonObject { ["c"] = new JsonObject { ["name"] = "Home", ["color"] = "#ff0000" } }, ["onSuccessSetIsDefault"] = "#c" });
        var home = (string)made["created"]!["c"]!["id"]!;
        Assert.Equal(home, (string?)cals.Calendars().Single(c => (bool?)c["isDefault"] == true)["id"]);
        _ = cals.AddEvent(work, new JsonObject { ["title"] = "Old", ["start"] = "2026-11-20T10:00:00" });

        var refused = await CallAsync(server, "Calendar/set", new JsonObject { ["destroy"] = new JsonArray(home) });
        Assert.Equal("forbidden", (string?)refused["notDestroyed"]![home]!["type"]);
        var kept = await CallAsync(server, "Calendar/set", new JsonObject { ["destroy"] = new JsonArray(work) });
        Assert.Equal("calendarHasEvent", (string?)kept["notDestroyed"]![work]!["type"]);
        var gone = await CallAsync(server, "Calendar/set", new JsonObject { ["destroy"] = new JsonArray(work), ["onDestroyRemoveEvents"] = true });
        Assert.Equal(work, (string?)gone["destroyed"]![0]);
        Assert.Empty(cals.Events());
    }
}
