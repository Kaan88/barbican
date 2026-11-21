// Source: Bulwark lib/plugin-sandbox/assistant/c.calendar.ts:42-116 doCalendarRespond (calendar:respond; refuses a message that is no invitation, an event the user organises, a sender that fails the trust check, invitation reading
// switched off; answers like the reader's banner), lib/__tests__/plugin-host-calendar-respond.test.ts, lib/calendar-rsvp.ts. The seed invitation of the fake JMAP server (CalendarEvent/parse of the attached .ics); the answer's effect is
// read back from the fake calendar and its scheduling messages.
using System.Text.Json.Nodes;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Tests.Calendar.Editing;
using Barbican.FakeJmap;
using Rig = Barbican.Core.Tests.Plugins.Api.PluginCalendarRig;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class PluginCalendarRespondTests
{
    private const string Uid = "design-review-1@example.org";
    private static readonly string[] Respond = ["calendar:respond"];

    private static Task<Rig> StartAsync(bool parsing = true, bool organizer = false, params string[] permissions) =>
        Rig.StartAsync(Rig.Grant(permissions.Length == 0 ? Respond : permissions), organizer ? (cals, work, _) => cals.AddEvent(work, Mine()) : null, invitations: true, parsing: parsing);

    private static JsonObject Mine() => new()
    {
        ["uid"] = Uid, ["title"] = "Design review", ["start"] = "2026-11-17T09:00:00", ["timeZone"] = "UTC", ["duration"] = "PT1H", ["organizerCalendarAddress"] = "mailto:" + EditingRig.Me,
        ["participants"] = new JsonObject
        {
            ["o"] = new JsonObject { ["email"] = EditingRig.Me, ["calendarAddress"] = "mailto:" + EditingRig.Me, ["roles"] = new JsonObject { ["owner"] = true }, ["participationStatus"] = "accepted" },
            ["a"] = new JsonObject { ["email"] = "olga@acme.example", ["calendarAddress"] = "mailto:olga@acme.example", ["roles"] = new JsonObject { ["attendee"] = true }, ["participationStatus"] = "needs-action" },
        },
    };

    private static JsonNode MeOf(JsonObject stored) => stored["participants"]!.AsObject().Select(p => p.Value!).Single(p => ((string?)p["email"] ?? (string?)p["calendarAddress"] ?? "").Contains(EditingRig.Me, StringComparison.OrdinalIgnoreCase));

    private static string Invitation(Rig rig) => (string)rig.Server.Store.EmailIds().Select(i => rig.Server.Store.Email(i)!).First(e => string.Equals((string?)e["subject"], FakeSeed.InvitationSubject, StringComparison.Ordinal))["id"]!;

    private static string Plain(Rig rig) => (string)rig.Server.Store.EmailIds().Select(i => rig.Server.Store.Email(i)!).First(e => !string.Equals((string?)e["subject"], FakeSeed.InvitationSubject, StringComparison.Ordinal))["id"]!;

    [Fact]
    public async Task Accepting_answers_the_invitation_adds_it_with_the_answer_and_lets_the_server_tell_the_organizer()
    {
        var rig = await StartAsync();
        var result = await rig.CallAsync("calendar.respond", $$"""["{{Invitation(rig)}}","accepted"]""");
        Assert.True(result.IsSuccess);
        var view = result.Value!;
        Assert.Equal(["organizer", "start", "status", "title"], Rig.Keys(view), StringComparer.Ordinal);
        Assert.Equal(["accepted", "Design review", "olga@acme.example"], [Rig.Text(view, "status"), Rig.Text(view, "title"), Rig.Text(view, "organizer")], StringComparer.Ordinal);
        Assert.Equal("2026-11-17T10:00:00", Rig.Text(view, "start"), StringComparer.Ordinal);
        var stored = rig.Stored(Uid)!;
        var me = MeOf(stored);
        Assert.Equal("accepted", (string?)me["participationStatus"]);
        Assert.Equal(1, rig.Sets);
        Assert.True((bool?)rig.Server.Api.SetRequests[0].Args["sendSchedulingMessages"]);
        Assert.Single(rig.Cals.SchedulingSent);
    }

    [Fact]
    public async Task A_second_answer_updates_the_stored_copy_and_sends_the_new_reply()
    {
        var rig = await StartAsync();
        Assert.True((await rig.CallAsync("calendar.respond", $$"""["{{Invitation(rig)}}","tentative"]""")).IsSuccess);
        var declined = await rig.CallAsync("calendar.respond", $$"""["{{Invitation(rig)}}","declined"]""");
        Assert.Equal("declined", Rig.Text(declined.Value!, "status"), StringComparer.Ordinal);
        var me = MeOf(rig.Stored(Uid)!);
        Assert.Equal("declined", (string?)me["participationStatus"]);
        Assert.Equal(2, rig.Sets);
        Assert.Single(rig.Cals.Events());
        Assert.Equal(2, rig.Cals.SchedulingSent.Count);
    }

    [Fact]
    public async Task Without_calendar_respond_nothing_is_read_or_written()
    {
        var rig = await StartAsync(true, false, "calendar:write", "calendar:read");
        var calls = rig.Server.ApiCalls.Count;
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("calendar.respond", $$"""["{{Invitation(rig)}}","accepted"]""")).Error);
        Assert.Equal(calls, rig.Server.ApiCalls.Count);
        Assert.Equal(0, rig.Sets);
    }

    [Fact]
    public async Task Switching_invitation_reading_off_in_settings_refuses_the_call()
    {
        var rig = await StartAsync(parsing: false);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("calendar.respond", $$"""["{{Invitation(rig)}}","accepted"]""")).Error);
        Assert.Equal(0, rig.Sets);
    }

    [Fact]
    public async Task A_message_without_an_invitation_or_an_unknown_one_is_refused()
    {
        var rig = await StartAsync();
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("calendar.respond", $$"""["{{Plain(rig)}}","accepted"]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("calendar.respond", """["no-such-message","accepted"]""")).Error);
        Assert.Equal(0, rig.Sets);
    }

    [Fact]
    public async Task Bad_answers_are_refused_before_any_read()
    {
        var rig = await StartAsync();
        var calls = rig.Server.ApiCalls.Count;
        foreach (var bad in new[] { "[]", """["m"]""", """["m","maybe"]""", """["m","Accepted"]""", """["","accepted"]""", """[1,"accepted"]""", """["m",true]""" })
        {
            Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("calendar.respond", bad)).Error);
        }

        Assert.Equal(calls, rig.Server.ApiCalls.Count);
    }

    [Fact]
    public async Task An_event_the_user_organizes_is_not_answered()
    {
        var rig = await StartAsync(organizer: true);
        Assert.Equal(PluginErrorCode.Failed, (await rig.CallAsync("calendar.respond", $$"""["{{Invitation(rig)}}","accepted"]""")).Error);
        Assert.Equal(0, rig.Sets);
    }

    [Fact]
    public async Task Another_add_on_stopping_the_answer_gives_null_and_sends_nothing()
    {
        var rig = await StartAsync();
        using var stop = rig.Hooks.InterceptAny(HookNames.OnBeforeEventRsvp, (_, _) => ValueTask.FromResult(false));
        var result = await rig.CallAsync("calendar.respond", $$"""["{{Invitation(rig)}}","accepted"]""");
        Assert.Equal(PluginValueKind.Null, result.Value!.Kind);
        Assert.Equal(0, rig.Sets);
        Assert.Empty(rig.Cals.SchedulingSent);
    }

    [Fact]
    public async Task A_stale_login_before_the_answer_stops_it()
    {
        var rig = await StartAsync();
        rig.SpyStore.OnSnapshot = () => rig.Sessions.Valid = false;
        Assert.Equal(PluginErrorCode.StaleContext, (await rig.CallAsync("calendar.respond", $$"""["{{Invitation(rig)}}","accepted"]""")).Error);
        Assert.Equal(0, rig.Sets);
    }
}
