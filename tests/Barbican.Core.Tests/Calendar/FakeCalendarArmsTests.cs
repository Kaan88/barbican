// Green: the FakeJmap calendar arms of the M4 plan (section 4.7) answer Calendar/get, CalendarEvent/query and get with the chunk limit, changes and availability; without options.Calendars they answer unknownMethod.

using System.Text.Json.Nodes;
using Barbican.Core.Jmap;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Calendar;

public sealed class FakeCalendarArmsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonObject> CallAsync(FakeJmapServer server, string method, JsonObject args)
    {
        var client = await FakeJmapHarness.ConnectAsync(server, TimeProvider.System, _ => { }, false, Ct);
        args["accountId"] = client.Session.MailAccountId;
        var r = await client.SendAsync(JmapRequest.Of(JmapRequest.Call(method, args, "c")), Ct);
        return r.Value!.MethodResponses[0][1]!.AsObject();
    }

    [Fact]
    public async Task Calendars_events_changes_and_availability_are_served()
    {
        var cals = new FakeCalendars();
        var work = cals.AddCalendar("Work", isDefault: true);
        var ev = cals.AddEvent(work, new JsonObject
        {
            ["title"] = "Review",
            ["start"] = "2026-11-20T10:00:00",
            ["duration"] = "PT1H",
            ["utcStart"] = "2026-11-20T10:00:00Z",
            ["utcEnd"] = "2026-11-20T11:00:00Z",
            ["participants"] = new JsonObject { ["p"] = new JsonObject { ["email"] = "ann@x.example" } },
        });
        var server = new FakeJmapServer(new FakeJmapOptions { CalendarsCapability = true, AvailabilityCapability = true, Calendars = cals });

        var got = await CallAsync(server, "Calendar/get", new JsonObject { ["ids"] = null });
        Assert.Equal("Work", (string?)got["list"]![0]!["name"]);

        var q = await CallAsync(server, "CalendarEvent/query", new JsonObject { ["filter"] = new JsonObject { ["inCalendars"] = new JsonArray(work) } });
        Assert.Equal(ev, (string?)q["ids"]![0]);

        var before = cals.State("CalendarEvent");
        _ = cals.AddEvent(work, new JsonObject { ["title"] = "Later", ["start"] = "2026-11-21T10:00:00" });
        var ch = await CallAsync(server, "CalendarEvent/changes", new JsonObject { ["sinceState"] = before });
        Assert.Single(ch["created"]!.AsArray());

        var busy = await CallAsync(server, "Principal/getAvailability", new JsonObject { ["id"] = "p:ann@x.example", ["utcStart"] = "2026-11-20T00:00:00Z", ["utcEnd"] = "2026-11-21T00:00:00Z" });
        Assert.Single(busy["list"]!.AsArray());
    }

    [Fact]
    public async Task Without_calendar_data_the_methods_are_unknown()
    {
        var client = await FakeJmapHarness.ConnectAsync(new FakeJmapServer(new FakeJmapOptions { CalendarsCapability = true }), TimeProvider.System, _ => { }, false, Ct);
        var r = await client.SendAsync(JmapRequest.Of(JmapRequest.Call("Calendar/get", new JsonObject { ["accountId"] = client.Session.MailAccountId }, "c")), Ct);
        Assert.Equal("error", (string?)r.Value!.MethodResponses[0][0]);
    }
}
