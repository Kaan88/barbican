// Mirrors: Rust invite.rs:88 my_participant (the (participant id, participationStatus) pair); RFC 8984 section 4.4.6. Stub from stage F of M2; owner reader.

namespace Barbican.Core.Calendar.Invitations;

/// <summary>The recipient's own participant entry in the event.</summary>
public sealed record InviteParticipant(string Id, string Status);
