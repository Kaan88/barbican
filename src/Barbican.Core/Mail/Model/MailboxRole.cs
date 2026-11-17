// Mirrors: Bulwark lib/jmap/types.ts, RFC 8621; Rust jmap.rs / mail.rs value shapes (JSON there, typed here). Model stub from F2 (M1-plan §4.1); owner jmap.

namespace Barbican.Core.Mail.Model;

/// <summary>The role of a mailbox (RFC 8621 §2, IANA registry); the jmap stream maps the strings.</summary>
public enum MailboxRole
{
    None,
    Inbox,
    Drafts,
    Sent,
    Trash,
    Junk,
    Archive,
    Important,
    All,
    Flagged,
    Subscribed,
}
