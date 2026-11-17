// Mirrors: Rust invite.rs:36 find_part, :49 method_from_ics, :76 organizer, :88 my_participant, :96 i_organize, :107 method, :118 duration_secs, :141 when,
// :163 location, :168 status_label, :250 trust; RFC 5546 (iTIP), RFC 8984 (JSCalendar); Bulwark lib/calendar-invitation.ts,
// components/email/calendar-invitation-banner.tsx; Stalwart crates/jmap/src/calendar_event/parse.rs (the server parses the .ics, J-072).
// Stub from stage F of M2; owner reader (display and trust only; Accept/Maybe/Decline and add to calendar are M4).

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Mail.AuthResults;

namespace Barbican.Core.Calendar.Invitations;

/// <summary>The pure parts of the invitation banner.</summary>
public static class InvitationModel
{
    /// <summary>The message's calendar part: an attachment first (text/calendar, application/ics, .ics, .ical), then a body part.</summary>
    public static JsonNode? FindPart(JsonNode message)
    {
        throw new PortPendingException("invite.rs:36 find_part");
    }

    /// <summary>The METHOD line of raw iCalendar text; METHODICAL and the like are Unknown.</summary>
    public static InviteMethod MethodFromIcs(string raw)
    {
        throw new PortPendingException("invite.rs:49 method_from_ics");
    }

    /// <summary>The method: the ics METHOD wins, then the event's own method, then status cancelled, then a reply-shaped participant list, else Request.</summary>
    public static InviteMethod Method(JsonNode ev, string? rawIcs)
    {
        throw new PortPendingException("invite.rs:107 method");
    }

    /// <summary>The organizer's name and address (calendarAddress without mailto:, organizerCalendarAddress for Stalwart), or null.</summary>
    public static InviteOrganizer? Organizer(JsonNode ev)
    {
        throw new PortPendingException("invite.rs:76 organizer");
    }

    /// <summary>The participant entry for one of the user's addresses; an organizer who also attends answers; an owner-only entry is not one.</summary>
    public static InviteParticipant? MyParticipant(JsonNode ev, IReadOnlyList<string> mine)
    {
        throw new PortPendingException("invite.rs:88 my_participant");
    }

    /// <summary>Whether one of the user's addresses (case-insensitive) is the organizer.</summary>
    public static bool IOrganize(JsonNode ev, IReadOnlyList<string> mine)
    {
        throw new PortPendingException("invite.rs:96 i_organize");
    }

    /// <summary>Seconds of an ISO 8601 duration (PT1H30M, P1DT2H, P2W), or null for an invalid one.</summary>
    public static long? DurationSeconds(string duration)
    {
        throw new PortPendingException("invite.rs:118 duration_secs");
    }

    /// <summary>"Tue 17 Nov 2026, 10:00 - 11:30" in the event's zone, with the zone name when it differs from the local one; all-day events say so.</summary>
    public static string When(JsonNode ev, string localZone)
    {
        throw new PortPendingException("invite.rs:141 when");
    }

    /// <summary>The first location's name, or empty.</summary>
    public static string Location(JsonNode ev)
    {
        throw new PortPendingException("invite.rs:163 location");
    }

    /// <summary>A display label for an event status ("tentative" gives "Tentative").</summary>
    public static string StatusLabel(string status)
    {
        throw new PortPendingException("invite.rs:168 status_label");
    }

    /// <summary>
    /// How far the message can be trusted to come from who the calendar data names: a warning, a caution, or null when trusted.
    /// <paramref name="auth"/> is the topmost Authentication-Results of the account's own server (D-9), <paramref name="hasAuth"/> whether there was one,
    /// <paramref name="mine"/> the account's own addresses (mail between them is internal).
    /// </summary>
    public static InviteTrust? Trust(JsonNode ev, InviteMethod method, string? from, IReadOnlyList<AuthResult> auth, bool hasAuth, IReadOnlyList<string> mine)
    {
        throw new PortPendingException("invite.rs:250 trust");
    }
}
