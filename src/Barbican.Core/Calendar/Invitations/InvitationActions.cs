// Mirrors: Rust invite.rs:171 importable, :183 rsvp_patch; Bulwark lib/calendar-rsvp.ts, lib/calendar-invitation.ts; RFC 6901 (JSON pointer escaping ~0 ~1).

using System.Text.Json.Nodes;

namespace Barbican.Core.Calendar.Invitations;

/// <summary>The shapes an invitation is stored and answered with.</summary>
public static class InvitationActions
{
    private static readonly string[] ServerSet = ["id", "method", "blobId", "baseEventId", "calendarIds", "isOrigin", "utcStart", "utcEnd"];

    /// <summary>The invitation's event as a stored copy: id, method, blobId, baseEventId, isOrigin, utcStart and utcEnd removed, calendarIds set to the calendar, the uid kept.</summary>
    public static JsonNode Importable(JsonNode ev, string calendarId)
    {
        ArgumentNullException.ThrowIfNull(ev);
        ArgumentNullException.ThrowIfNull(calendarId);
        var copy = (JsonObject)ev.DeepClone();
        foreach (var key in ServerSet)
        {
            copy.Remove(key);
        }

        copy["calendarIds"] = new JsonObject { [calendarId] = true };
        return copy;
    }

    /// <summary>The patch that answers as participant <paramref name="participantId"/>: <c>participants/&lt;id escaped&gt;/participationStatus</c> (the id's "~" and "/" escaped as "~0" and "~1").</summary>
    public static JsonObject RsvpPatch(string participantId, string status)
    {
        ArgumentNullException.ThrowIfNull(participantId);
        var escaped = participantId.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
        return new JsonObject { [$"participants/{escaped}/participationStatus"] = status };
    }
}
