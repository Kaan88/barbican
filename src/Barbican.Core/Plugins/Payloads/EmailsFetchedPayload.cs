// Mirrors: Bulwark onEmailsFetched (components/email/email-list.tsx; lib/plugin-mail-hooks.ts). Owner mailcore.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>A page of the message list, before it is shown (transform).</summary>
public sealed record EmailsFetchedPayload(IReadOnlyList<Barbican.Core.Mail.Model.EmailSummary> Emails, string? MailboxId);
