// Ported from Rust src/invite.rs tests (:273-:366) except import_and_patch_shapes (T-127, M4). Red until stream reader implements
// InvitationModel (M2 plan 6.3, early rows T-122..T-126).

using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Invitations;
using Barbican.Core.Mail.AuthResults;

namespace Barbican.Core.Tests.Calendar.Invitations;

public sealed class InvitationModelTests
{
    private static JsonNode J(string json) => JsonNode.Parse(json)!;

    private static JsonNode Ev() => J("""
        {"@type": "Event", "uid": "u1", "title": "Planning", "start": "2026-11-17T10:00:00", "timeZone": "Europe/Berlin", "duration": "PT1H30M",
         "locations": {"l1": {"name": "Room 4"}},
         "participants": {
           "o": {"name": "Olga", "calendarAddress": "mailto:olga@acme.example", "roles": {"owner": true, "attendee": true}, "participationStatus": "accepted"},
           "m": {"calendarAddress": "mailto:Me@x.example", "roles": {"attendee": true}, "participationStatus": "needs-action"}
         }}
        """);

    [Fact]
    [Red("reader")]
    [RustTest("invite::finds_the_calendar_part")]
    public void Finds_the_calendar_part()
    {
        var m = J("""{"attachments": [{"blobId": "b1", "type": "application/pdf", "name": "a.pdf"}, {"blobId": "b2", "type": "text/calendar", "name": "invite.ics"}]}""");
        Assert.Equal("b2", (string)InvitationModel.FindPart(m)!["blobId"]!);
        var inline = J("""{"attachments": [], "textBody": [{"partId": "1", "blobId": "t", "type": "text/plain"}, {"partId": "2", "blobId": "c", "type": "TEXT/CALENDAR; method=REQUEST"}]}""");
        Assert.Equal("c", (string)InvitationModel.FindPart(inline)!["blobId"]!);
        Assert.Equal("x", (string)InvitationModel.FindPart(J("""{"attachments": [{"blobId": "x", "name": "MEETING.ICS", "type": "application/octet-stream"}]}"""))!["blobId"]!);
        Assert.Null(InvitationModel.FindPart(J("""{"attachments": []}""")));
    }

    [Fact]
    [Red("reader")]
    [RustTest("invite::method_order")]
    public void Method_order()
    {
        Assert.Equal(InviteMethod.Request, InvitationModel.MethodFromIcs("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nMETHOD:REQUEST\r\nBEGIN:VEVENT"));
        Assert.Equal(InviteMethod.Cancel, InvitationModel.MethodFromIcs("method;x=1:Cancel\n"));
        Assert.Equal(InviteMethod.Unknown, InvitationModel.MethodFromIcs("METHODICAL:REPLY\nSUMMARY:x"));
        Assert.Equal(InviteMethod.Cancel, InvitationModel.Method(Ev(), "METHOD:CANCEL"));
        Assert.Equal(InviteMethod.Reply, InvitationModel.Method(J("""{"method": "reply"}"""), "METHOD:REQUEST"));
        Assert.Equal(InviteMethod.Request, InvitationModel.Method(Ev(), null));
        Assert.Equal(InviteMethod.Cancel, InvitationModel.Method(J("""{"status": "cancelled"}"""), null));
        var reply = J("""{"participants": {"a": {"roles": {"attendee": true}, "participationStatus": "accepted"}}}""");
        Assert.Equal(InviteMethod.Reply, InvitationModel.Method(reply, null));
    }

    [Fact]
    [Red("reader")]
    [RustTest("invite::people")]
    public void People()
    {
        var e = Ev();
        Assert.Equal(new InviteOrganizer("Olga", "olga@acme.example"), InvitationModel.Organizer(e));
        Assert.Equal(new InviteParticipant("m", "needs-action"), InvitationModel.MyParticipant(e, ["me@x.example"]));
        Assert.NotNull(InvitationModel.MyParticipant(e, ["olga@acme.example"]));
        Assert.True(InvitationModel.IOrganize(e, ["OLGA@acme.example"]));
        var onlyOwner = J("""{"participants": {"o": {"email": "o@x", "roles": {"owner": true}}}}""");
        Assert.Null(InvitationModel.MyParticipant(onlyOwner, ["o@x"]));
        var stalwart = J("""{"organizerCalendarAddress": "mailto:boss@x.example", "participants": {"b": {"calendarAddress": "mailto:boss@x.example", "name": "Boss"}}}""");
        Assert.Equal(new InviteOrganizer("Boss", "boss@x.example"), InvitationModel.Organizer(stalwart));
    }

    [Fact]
    [Red("reader")]
    [RustTest("invite::summary_text")]
    public void Summary_text()
    {
        Assert.Equal(5400L, InvitationModel.DurationSeconds("PT1H30M"));
        Assert.Equal(93_600L, InvitationModel.DurationSeconds("P1DT2H"));
        Assert.Equal(1_209_600L, InvitationModel.DurationSeconds("P2W"));
        Assert.Null(InvitationModel.DurationSeconds("1H"));
        Assert.Equal("Tue 17 Nov 2026, 10:00 – 11:30", InvitationModel.When(Ev(), "Europe/Berlin"));
        Assert.Equal("Tue 17 Nov 2026, 10:00 – 11:30 (Europe/Berlin)", InvitationModel.When(Ev(), "Europe/Istanbul"));
        var allDay = J("""{"start": "2026-11-17T00:00:00", "showWithoutTime": true, "duration": "P1D"}""");
        Assert.Equal("Tue 17 Nov 2026, all day", InvitationModel.When(allDay, "X"));
        var two = J("""{"start": "2026-11-17T00:00:00", "showWithoutTime": true, "duration": "P2D"}""");
        Assert.Equal("Tue 17 Nov 2026 – Wed 18 Nov 2026", InvitationModel.When(two, "X"));
        Assert.Equal("Room 4", InvitationModel.Location(Ev()));
        Assert.Equal("Tentative", InvitationModel.StatusLabel("tentative"));
    }

    [Fact]
    [Red("reader")]
    [RustTest("invite::trust_assessment")]
    public void Trust_assessment()
    {
        var e = Ev();
        var good = AuthenticationResults.Parse("mx; dkim=pass header.d=acme.example; dmarc=pass");
        Assert.Null(InvitationModel.Trust(e, InviteMethod.Request, "olga@acme.example", good, true, []));

        var aligned = InvitationModel.Trust(e, InviteMethod.Request, "olga@evil.example", AuthenticationResults.Parse("mx; spf=pass smtp.mailfrom=x@evil.example"), true, []);
        Assert.NotNull(aligned);
        Assert.False(aligned.Warning, $"an aligned pass for a different sender is a caution: {aligned.Text}");
        Assert.Contains("olga@evil.example", aligned.Text, StringComparison.Ordinal);
        Assert.Contains("olga@acme.example", aligned.Text, StringComparison.Ordinal);

        var spoofed = InvitationModel.Trust(e, InviteMethod.Request, "olga@evil.example", AuthenticationResults.Parse("mx; spf=none"), true, []);
        Assert.NotNull(spoofed);
        Assert.True(spoofed.Warning && spoofed.Text.EndsWith("could not be verified.", StringComparison.Ordinal), "spoofed organizer");

        Assert.True(InvitationModel.Trust(e, InviteMethod.Request, "olga@acme.example", AuthenticationResults.Parse("mx; dmarc=fail"), true, [])!.Warning);
        var none = InvitationModel.Trust(e, InviteMethod.Request, "olga@acme.example", [], false, []);
        Assert.NotNull(none);
        Assert.False(none.Warning, "no authentication at all is a caution");
        Assert.Null(InvitationModel.Trust(e, InviteMethod.Request, "olga@acme.example", [], false, ["me@acme.example"]));
    }
}
