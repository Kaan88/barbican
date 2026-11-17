// Mirrors: Bulwark lib/jmap/types.ts, RFC 8621; Rust jmap.rs / mail.rs value shapes (JSON there, typed here). Model stub from F2 (M1-plan §4.1); owner jmap.

namespace Barbican.Core.Mail.Model;

/// <summary>The mailboxes of an account (the tree is built from <c>parentId</c>).</summary>
public sealed record MailboxTree(IReadOnlyList<Mailbox> All);
