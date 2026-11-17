// Mirrors: Rust compose.rs:795 pre_send_warning (empty subject, placeholders in what the user wrote and not in the quote, attachment reminder with the words attached, attachment, enclosed, anbei, ekte, ek'te, pièce jointe, adjunto);
// Bulwark components/email/send-warnings-dialog.tsx, external-recipient-confirm.tsx; settings emptySubjectWarningEnabled, placeholderWarningEnabled, attachmentReminderEnabled. Contract stub from the M3 plan section 4.3; owner send-pipeline replaces the body.

using Barbican.Core.Common;

namespace Barbican.Core.Compose;

/// <summary>The checks before a send (stub until stream send-pipeline lands).</summary>
public static class SendWarnings
{
    /// <summary>
    /// The warnings for <paramref name="draft"/>, each only when its setting in <paramref name="settings"/> is on: empty subject; unfilled placeholders (<see cref="ComposeRules.FindPlaceholders"/> over the subject and the body without the quoted
    /// original); a mentioned attachment with none attached; and recipients outside <paramref name="ownDomains"/> (the domains of the account's identities, lower case). Empty when there is nothing to warn about.
    /// </summary>
    public static IReadOnlyList<SendWarning> Check(ComposeDraft draft, ComposeSettings settings, IReadOnlyList<string> ownDomains) => throw new PortPendingException("SendWarnings.Check");
}
