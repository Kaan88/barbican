// Mirrors: Rust jmap.rs:721 scheduled (each message with receivedAt replaced by the submission sendAt and its submissionId). Owner send-pipeline.

using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Compose.Pipeline;

/// <summary>A row of the Scheduled folder: the message as the list shows it (its date is the release time) and its submission.</summary>
public sealed record ScheduledItem(EmailSummary Summary, string SubmissionId, DateTimeOffset At);
