// Mirrors: Rust jmap.rs apply_object_delta :1494 (Mailbox/changes). Stub from F2; owner jmap.

namespace Barbican.Core.Mail.Model;

/// <summary>The mailbox list or its counts changed; carries the new tree.</summary>
public sealed record MailboxesChangedEvent(MailboxTree Tree) : MailSyncEvent;
