// Ported from Rust src/invite.rs test :356 import_and_patch_shapes (T-127). M4 stream cal-edit. (T-122..T-126 were ported with the reader in M2: InvitationModelTests.)

using Barbican.Core.Calendar.Invitations;
using static Barbican.Core.Tests.Calendar.CalendarTestData;

namespace Barbican.Core.Tests.Calendar.Invitations;

public sealed class InvitationActionsTests
{
    [Fact]
    [Red("cal-edit")]
    [RustTest("invite::import_and_patch_shapes")]
    public void Import_and_patch_shapes()
    {
        var e = J("""
            {"@type": "Event", "uid": "u1", "title": "Planning", "start": "2026-11-17T10:00:00", "timeZone": "Europe/Berlin", "duration": "PT1H30M",
             "locations": {"l1": {"name": "Room 4"}},
             "participants": {"o": {"name": "Olga", "calendarAddress": "mailto:olga@acme.example", "roles": {"owner": true, "attendee": true}, "participationStatus": "accepted"},
               "m": {"calendarAddress": "mailto:Me@x.example", "roles": {"attendee": true}, "participationStatus": "needs-action"}}}
            """);
        e["id"] = "x";
        e["method"] = "request";
        var i = InvitationActions.Importable(e, "cal1");
        Assert.Null(i["id"]);
        Assert.Null(i["method"]);
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(J("""{"cal1": true}"""), i["calendarIds"]));
        Assert.Equal("u1", (string?)i["uid"]);
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(J("""{"participants/a~1b~0c/participationStatus": "accepted"}"""), InvitationActions.RsvpPatch("a/b~c", "accepted")));
    }
}
