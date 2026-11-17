// Mirrors: Rust invite.rs:250 trust (Some((warning, text))); Bulwark lib/calendar-invitation.ts getInvitationTrustAssessment. Stub from stage F of M2; owner reader.

namespace Barbican.Core.Calendar.Invitations;

/// <summary>A caution (Warning false) or a warning (true) about whether the message really comes from who the calendar data names.</summary>
public sealed record InviteTrust(bool Warning, string Text);
