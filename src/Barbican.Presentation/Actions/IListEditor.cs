// Mirrors: Rust mail.rs patch_local :887 and remove_local :901 (optimistic changes of the loaded list); M2 plan 6.1 (rows leave through ListSync, no reset).

using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Actions;

/// <summary>The optimistic edits an action makes to the open list; the message list implements it. Every edit goes through ListSync: no reset, no flash.</summary>
public interface IListEditor
{
    /// <summary>The open folder's role (None for a search or a tag list).</summary>
    MailboxRole Role { get; }

    /// <summary>The id of the open folder, or null.</summary>
    string? MailboxId { get; }

    /// <summary>The loaded message with this id, or null.</summary>
    EmailSummary? Find(string emailId);

    /// <summary>
    /// Takes messages out of the list at once. When the open message is among them the reader moves on as the returnToListAfterAction setting
    /// says: back to the list, or on to the next message.
    /// </summary>
    ListEdit RemoveRows(IReadOnlyCollection<string> emailIds);

    /// <summary>Puts removed messages back where they were.</summary>
    void RestoreRows(ListEdit edit);

    /// <summary>Changes loaded messages in place (keywords); returns their earlier copies for <see cref="PutRows"/>.</summary>
    IReadOnlyList<EmailSummary> PatchRows(IReadOnlyCollection<string> emailIds, Func<EmailSummary, EmailSummary> change);

    /// <summary>Puts earlier copies back after a failed write.</summary>
    void PutRows(IReadOnlyList<EmailSummary> before);

    /// <summary>
    /// After an Undo put the rows of <paramref name="edit"/> back: opens again the message that was open when the action ran, when the list still shows
    /// the same view and nothing but what the action opened is open now (Bulwark components/mail/mail-app.tsx:4083 reopenAfterUndo).
    /// </summary>
    void Reopen(ListEdit edit);

    /// <summary>Reads the open list again, keeping the rows on screen until it is ready (after a failure nothing else can be trusted).</summary>
    Task ReloadAsync();
}
