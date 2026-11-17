// Mirrors: Bulwark lib/jmap/types.ts, RFC 8621; Rust jmap.rs / mail.rs value shapes (JSON there, typed here). Model stub from F2 (M1-plan §4.1); owner jmap.

namespace Barbican.Core.Mail.Model;

/// <summary>The messages of one conversation, oldest first (Thread/get plus Email/get; the reader's strip).</summary>
public sealed record ConversationThread(string ThreadId, IReadOnlyList<EmailSummary> Emails);
