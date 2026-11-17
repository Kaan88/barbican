// Mirrors: Rust mail.rs mailbox_role :1040 and move_to_role (the folder of a role), tags.rs; Bulwark lib/move-targets.ts. Owner actions.

using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Actions;

/// <summary>The folders of the active account as the sidebar last read them.</summary>
public interface IMailboxDirectory
{
    /// <summary>All folders.</summary>
    MailboxTree Tree { get; }
}
