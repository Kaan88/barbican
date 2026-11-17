// Mirrors: Rust invite.rs:13 Method (iTIP methods, RFC 5546 section 1.4); Bulwark lib/calendar-invitation.ts. Stub from stage F of M2; owner reader (display and trust; RSVP is M4).

namespace Barbican.Core.Calendar.Invitations;

/// <summary>The iTIP method of an invitation message.</summary>
public enum InviteMethod
{
    Publish,
    Request,
    Reply,
    Add,
    Cancel,
    Refresh,
    Counter,
    DeclineCounter,
    Unknown,
}
