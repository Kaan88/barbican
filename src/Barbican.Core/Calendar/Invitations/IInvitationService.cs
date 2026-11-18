// Mirrors: Rust invite.rs:195 parse, :210 stored, :230 answer; Bulwark lib/calendar-rsvp.ts, calendar-invitation.ts, calendar-cancellation.ts, components/email/calendar-invitation-banner.tsx; RFC 5546 (REQUEST, REPLY, COUNTER, CANCEL), JMAP Calendars CalendarEvent/parse and
// sendSchedulingMessages; B-CAL-051; G-001 (apply a counter-proposal), G-002 (remove a cancelled event). Answers run as the login that owns the message (the account the invitation arrived in). Contract from the M4 plan section 4.3; owner cal-edit.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Calendar.Invitations;

/// <summary>What the invitation banner does: read the event, answer it, add it, apply a counter-proposal, remove a cancelled one.</summary>
public interface IInvitationService
{
    /// <summary>CalendarEvent/parse of the message's blob plus the METHOD of the raw .ics (<c>InvitationModel.Method</c>).</summary>
    Task<Result<ParsedInvitation>> ParseAsync(string accountKey, string blobId, CancellationToken ct);

    /// <summary>The stored copy with this uid (the master, not an occurrence), or null.</summary>
    Task<Result<JsonNode?>> StoredAsync(string accountKey, string uid, CancellationToken ct);

    /// <summary>Answers on the stored copy; else adds the invitation to the default calendar with the answer already in it and the server sends the reply, in one call, so a failure leaves nothing behind. Returns the event id answered.</summary>
    Task<Result<string>> AnswerAsync(string accountKey, JsonNode invitation, IReadOnlyList<string> mine, string status, CancellationToken ct);

    /// <summary>Adds the event to the default calendar without answering.</summary>
    Task<Result<string>> AddToCalendarAsync(string accountKey, JsonNode invitation, CancellationToken ct);

    /// <summary>G-001: the organiser accepts an attendee's counter-proposal: the stored event takes the proposed start, end and duration and sends the update to the guests.</summary>
    Task<Result<Unit>> ApplyCounterAsync(string accountKey, JsonNode counter, CancellationToken ct);

    /// <summary>G-002: the organiser cancelled; the stored copy (if any) is removed from the calendar.</summary>
    Task<Result<Unit>> RemoveCancelledAsync(string accountKey, string uid, CancellationToken ct);
}
