// Mirrors: Rust invite.rs:76 organizer (the (name, address) pair); RFC 8984 section 4.4.6 (participants), JMAP Calendars (organizerCalendarAddress). Stub from stage F of M2; owner reader.

namespace Barbican.Core.Calendar.Invitations;

/// <summary>The organizer of an event as the invitation banner names them.</summary>
public sealed record InviteOrganizer(string Name, string Email);
