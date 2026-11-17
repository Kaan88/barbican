// Mirrors: Rust jmap.rs:1243 submission_envelope; RFC 8621 §7. Stub from F2; owner jmap.

namespace Barbican.Core.Jmap.Submission;

/// <summary>An EmailSubmission envelope.</summary>
public sealed record SubmissionEnvelope(EnvelopeAddress MailFrom, IReadOnlyList<EnvelopeAddress> RcptTo);
