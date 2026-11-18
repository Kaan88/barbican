// New tests for the invitation banner's buttons (M4 stream cal-edit acceptance, I-212): the buttons follow the iTIP method and who the user is, and the handler owns exactly its ids.

using System.Text.Json.Nodes;
using Barbican.Presentation.Reader;

namespace Barbican.Presentation.Tests.Calendar.Invitations;

public sealed class InvitationBannerActionsTests
{
    private const string Me = "user@example.org";

    private static JsonObject Event(string organizer, string guest) => new()
    {
        ["title"] = "Design review",
        ["start"] = "2026-11-20T10:00:00",
        ["timeZone"] = "UTC",
        ["duration"] = "PT1H",
        ["organizerCalendarAddress"] = "mailto:" + organizer,
        ["participants"] = new JsonObject
        {
            ["o"] = new JsonObject { ["email"] = organizer, ["roles"] = new JsonObject { ["owner"] = true } },
            ["g"] = new JsonObject { ["email"] = guest, ["roles"] = new JsonObject { ["attendee"] = true }, ["participationStatus"] = "needs-action" },
        },
    };

    private static string[] Actions(JsonNode ev, string method, bool withActions = true) =>
        [.. InvitationBannerSource.Banner(ev, "BEGIN:VCALENDAR\r\nMETHOD:" + method + "\r\nEND:VCALENDAR", "olga@acme.example", [], [Me], TimeZoneInfo.Utc, withActions).Actions.Select(a => a.ActionId)];

    [Fact]
    public void A_request_to_the_user_offers_the_three_answers_and_adding()
    {
        Assert.Equal(["invite-accept", "invite-maybe", "invite-decline", "invite-add"], Actions(Event("olga@acme.example", Me), "REQUEST"));
        Assert.Equal(["invite-add"], Actions(Event("olga@acme.example", "other@x.example"), "REQUEST"));
    }

    [Fact]
    public void A_cancellation_offers_removal_a_counter_proposal_only_the_organiser_and_a_reply_nothing()
    {
        Assert.Equal(["invite-remove"], Actions(Event("olga@acme.example", Me), "CANCEL"));
        Assert.Equal(["invite-apply-counter"], Actions(Event(Me, "olga@acme.example"), "COUNTER"));
        Assert.Empty(Actions(Event("olga@acme.example", Me), "COUNTER"));
        Assert.Empty(Actions(Event("olga@acme.example", Me), "REPLY"));
    }

    [Fact]
    public void Without_the_service_the_banner_keeps_its_note_and_has_no_buttons()
    {
        var banner = InvitationBannerSource.Banner(Event("olga@acme.example", Me), "BEGIN:VCALENDAR\r\nMETHOD:REQUEST\r\nEND:VCALENDAR", null, [], [Me], TimeZoneInfo.Utc);
        Assert.Empty(banner.Actions);
        Assert.Equal(InvitationBannerSource.RsvpNote, banner.Note);
        Assert.Null(InvitationBannerSource.Banner(Event("olga@acme.example", Me), "BEGIN:VCALENDAR\r\nMETHOD:REQUEST\r\nEND:VCALENDAR", null, [], [Me], TimeZoneInfo.Utc, true).Note);
    }
}
