// Mirrors: Rust invite.rs:171 importable, :183 rsvp_patch; Bulwark lib/calendar-rsvp.ts, lib/calendar-invitation.ts; RFC 6901 (JSON pointer escaping ~0 ~1). Contract from the M4 plan section 4.3; owner cal-edit replaces the bodies.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Calendar.Invitations;

/// <summary>The shapes an invitation is stored and answered with.</summary>
public static class InvitationActions
{
    /// <summary>The invitation's event as a stored copy: id, method, blobId, baseEventId, isOrigin, utcStart and utcEnd removed, calendarIds set to the calendar, the uid kept.</summary>
    public static JsonNode Importable(JsonNode ev, string calendarId) => throw new PortPendingException("InvitationActions.Importable");

    /// <summary>The patch that answers as participant <paramref name="participantId"/>: <c>participants/&lt;id escaped&gt;/participationStatus</c> (the id's "~" and "/" escaped as "~0" and "~1").</summary>
    public static JsonObject RsvpPatch(string participantId, string status) => throw new PortPendingException("InvitationActions.RsvpPatch");
}
