// New tests for the invitation service over the fake server (M4 stream cal-edit acceptance, I-212, B-CAL-051, G-001, G-002, D-M4-10): answer a stored copy with a patch that sends the reply, answer an unknown one by adding it with the answer in it (one call),
// add without answering, apply a counter-proposal, remove a cancelled event, and the RSVP hooks.

using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Calendar.Invitations;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Tests.Calendar.Editing;

namespace Barbican.Core.Tests.Calendar.Invitations;

public sealed class InvitationServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonObject Invite(string uid = "inv-1") => new()
    {
        ["@type"] = "Event",
        ["uid"] = uid,
        ["title"] = "Design review",
        ["start"] = "2026-11-20T10:00:00",
        ["timeZone"] = "UTC",
        ["duration"] = "PT1H",
        ["organizerCalendarAddress"] = "mailto:olga@acme.example",
        ["participants"] = new JsonObject
        {
            ["o"] = new JsonObject { ["email"] = "olga@acme.example", ["roles"] = new JsonObject { ["owner"] = true }, ["participationStatus"] = "accepted" },
            ["m"] = new JsonObject { ["email"] = CalendarRig.Me, ["roles"] = new JsonObject { ["attendee"] = true }, ["participationStatus"] = "needs-action" },
        },
    };

    private static InvitationService Service(CalendarRig rig) => new(rig.Accounts, rig.Store, rig.Hooks);

    [Fact]
    public async Task An_event_already_in_the_calendar_is_answered_with_a_patch_that_sends_the_reply()
    {
        var rig = await CalendarRig.CreateAsync((cals, work) => cals.AddEvent(work, Invite()));
        var service = Service(rig);
        var invitation = Invite();
        var heard = await CalendarRig.HeardAsync<RsvpHookPayload>(rig.Hooks, HookNames.OnEventRsvp, async () =>
        {
            var r = await service.AnswerAsync(CalendarRig.Account, invitation, [CalendarRig.Me], "accepted", Ct);
            Assert.True(r.IsOk, r.Error?.Message);
        });
        Assert.Equal("accepted", heard.Status);
        var set = Assert.Single(rig.Sets);
        Assert.True((bool?)set.Args["sendSchedulingMessages"]);
        Assert.Equal("accepted", (string?)set.Args["update"]!.AsObject().First().Value!["participants/m/participationStatus"]);
        Assert.Equal("accepted", (string?)rig.Cals.Events().Single()["participants"]!["m"]!["participationStatus"]);
    }

    [Fact]
    public async Task An_unknown_event_is_added_with_the_answer_already_in_it_in_one_call()
    {
        var rig = await CalendarRig.CreateAsync();
        var r = await Service(rig).AnswerAsync(CalendarRig.Account, Invite(), [CalendarRig.Me], "tentative", Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        var set = Assert.Single(rig.Sets);
        Assert.True((bool?)set.Args["sendSchedulingMessages"]);
        var stored = Assert.Single(rig.Cals.Events());
        Assert.Equal("tentative", (string?)stored["participants"]!["m"]!["participationStatus"]);
        Assert.Equal("inv-1", (string?)stored["uid"]);
        Assert.True((bool?)stored["calendarIds"]![rig.Work]);
        Assert.Null(stored["method"]);
    }

    [Fact]
    public async Task An_intercept_hook_stops_the_answer_and_a_stranger_cannot_answer()
    {
        var rig = await CalendarRig.CreateAsync();
        var service = Service(rig);
        var stranger = await service.AnswerAsync(CalendarRig.Account, Invite(), ["someone@else.example"], "accepted", Ct);
        Assert.False(stranger.IsOk);
        using (rig.Hooks.Intercept<RsvpHookPayload>(HookNames.OnBeforeEventRsvp, (_, _) => ValueTask.FromResult(false)))
        {
            Assert.False((await service.AnswerAsync(CalendarRig.Account, Invite(), [CalendarRig.Me], "accepted", Ct)).IsOk);
        }

        Assert.Empty(rig.Sets);
    }

    [Fact]
    public async Task Add_to_calendar_stores_the_event_without_an_answer_and_without_messages()
    {
        var rig = await CalendarRig.CreateAsync();
        var r = await Service(rig).AddToCalendarAsync(CalendarRig.Account, Invite(), Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        Assert.False(rig.Sets.Single().Args.ContainsKey("sendSchedulingMessages"));
        Assert.Equal("needs-action", (string?)rig.Cals.Events().Single()["participants"]!["m"]!["participationStatus"]);
        var stored = await Service(rig).StoredAsync(CalendarRig.Account, "inv-1", Ct);
        Assert.Equal("Design review", (string?)stored.Value!["title"]);
        Assert.Null((await Service(rig).StoredAsync(CalendarRig.Account, "other", Ct)).Value);
    }

    [Fact]
    public async Task A_counter_proposal_moves_the_event_and_tells_the_guests_and_a_cancellation_removes_it()
    {
        var rig = await CalendarRig.CreateAsync((cals, work) =>
        {
            var mine = Invite();
            mine["organizerCalendarAddress"] = "mailto:" + CalendarRig.Me;
            mine["participants"]!["o"]!["email"] = CalendarRig.Me;
            mine["participants"]!["m"]!["email"] = "olga@acme.example";
            _ = cals.AddEvent(work, mine);
        });
        var service = Service(rig);
        var counter = new JsonObject { ["uid"] = "inv-1", ["start"] = "2026-11-21T15:00:00", ["timeZone"] = "UTC", ["duration"] = "PT30M" };
        var r = await service.ApplyCounterAsync(CalendarRig.Account, counter, Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        Assert.True((bool?)Assert.Single(rig.Sets).Args["sendSchedulingMessages"]);
        var moved = rig.Cals.Events().Single();
        Assert.Equal("2026-11-21T15:00:00", (string?)moved["start"]);
        Assert.Equal("PT30M", (string?)moved["duration"]);

        var gone = await service.RemoveCancelledAsync(CalendarRig.Account, "inv-1", Ct);
        Assert.True(gone.IsOk, gone.Error?.Message);
        Assert.Empty(rig.Cals.Events());
        Assert.True((await service.RemoveCancelledAsync(CalendarRig.Account, "inv-1", Ct)).IsOk);
    }
}
