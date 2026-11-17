// Mirrors: Rust bodycache.rs on_sync :180 (fresh copies, removed ids, changed threads). Stub from F2; owner jmap.

namespace Barbican.Core.Mail.Model;

/// <summary>Messages changed (keywords, mailboxes) or left; the body cache applies it.</summary>
public sealed record EmailsChangedEvent(
    IReadOnlyDictionary<string, EmailSummary> Fresh,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Threads) : MailSyncEvent;
