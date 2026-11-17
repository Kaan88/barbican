// Mirrors: Rust compose.rs:797 pre_send_warning (empty subject, placeholders in what the user wrote and not in the quote, attachment reminder with the words attached, attachment, enclosed, anbei, ekte, ek'te, pièce jointe, adjunto);
// Bulwark components/email/send-warnings-dialog.tsx, external-recipient-confirm.tsx (G-054: Rust had no external confirm; Bulwark lists recipients outside the sender's own domains); settings emptySubjectWarningEnabled, placeholderWarningEnabled, attachmentReminderEnabled.
// Decision D-246: one dialog lists all warnings (M3 plan section 9 item 5), so the result is a list, not Rust's "press Send again".

using Barbican.Core.Editor;
using Barbican.Core.Jmap.Submission;

namespace Barbican.Core.Compose;

/// <summary>The checks before a send.</summary>
public static class SendWarnings
{
    private static readonly string[] AttachmentWords = ["attached", "attachment", "enclosed", "anbei", "ekte", "ek'te", "pièce jointe", "adjunto"];

    /// <summary>
    /// The warnings for <paramref name="draft"/>, each only when its setting in <paramref name="settings"/> is on: empty subject; unfilled placeholders (<see cref="ComposeRules.FindPlaceholders"/> over the subject and the body without the quoted
    /// original); a mentioned attachment with none attached; and recipients outside <paramref name="ownDomains"/> (the domains of the account's identities, lower case). Empty when there is nothing to warn about.
    /// </summary>
    public static IReadOnlyList<SendWarning> Check(ComposeDraft draft, ComposeSettings settings, IReadOnlyList<string> ownDomains)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(ownDomains);
        var warnings = new List<SendWarning>();
        if (settings.EmptySubjectWarning && draft.Subject.Trim().Length == 0)
        {
            warnings.Add(new SendWarning(SendWarningKind.EmptySubject, []));
        }

        var written = WrittenText(draft.Body);
        if (settings.PlaceholderWarning && ComposeRules.FindPlaceholders(draft.Subject, written) is { Count: > 0 } found)
        {
            warnings.Add(new SendWarning(SendWarningKind.UnfilledPlaceholders, found));
        }

        if (settings.AttachmentReminder && !draft.Attachments.Any(a => a.Cid is null))
        {
            var lower = written.Replace('\n', ' ').ToLowerInvariant();
            if (AttachmentWords.Any(w => lower.Contains(w, StringComparison.Ordinal)))
            {
                warnings.Add(new SendWarning(SendWarningKind.MissingAttachment, []));
            }
        }

        var own = ownDomains.Select(d => d.Trim().ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        var external = new List<string>();
        foreach (var a in AddressParser.Parse(draft.To).Concat(AddressParser.Parse(draft.Cc)).Concat(AddressParser.Parse(draft.Bcc)))
        {
            var at = a.Email.LastIndexOf('@');
            if (at > 0 && !own.Contains(a.Email[(at + 1)..].ToLowerInvariant()) && !external.Contains(a.Email, StringComparer.OrdinalIgnoreCase))
            {
                external.Add(a.Email);
            }
        }

        if (external.Count > 0)
        {
            warnings.Add(new SendWarning(SendWarningKind.ExternalRecipients, external));
        }

        return warnings;
    }

    /// <summary>What the user wrote: the body without the collapsed quoted original and without <c>&gt;</c> quote lines.</summary>
    internal static string WrittenText(Doc body)
    {
        var lines = new List<string>();
        foreach (var b in body.Blocks)
        {
            if (b.Kind is BlockKind.Quoted or BlockKind.Image or BlockKind.Rule)
            {
                continue;
            }

            var t = b.Text;
            if (!t.StartsWith('>'))
            {
                lines.Add(t);
            }
        }

        return string.Join('\n', lines);
    }
}
