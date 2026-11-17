// Mirrors: Rust main.rs:561 check_new_mail (unread Inbox mail that arrived since the last check; the first check only seeds);
// Bulwark plugin hook onNewEmailReceived (lib/plugin-hook-catalog.ts:157). Owner jmap.

namespace Barbican.Core.Mail.Model;

/// <summary>New unread mail arrived (newest first); notifications (M8) and the onNewEmailReceived hook follow it.</summary>
public sealed record NewMailEvent(IReadOnlyList<EmailSummary> Emails) : MailSyncEvent;
