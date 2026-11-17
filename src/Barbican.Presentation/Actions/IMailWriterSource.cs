// Mirrors: M2 plan 4.3 (IMailWriterFactory.ForSession: one writer per account session); Bulwark lib/jmap/client.ts (every write takes the account the rows belong to).

using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Write;

namespace Barbican.Presentation.Actions;

/// <summary>The writer of an account, by its key; actions never see sessions.</summary>
public interface IMailWriterSource
{
    /// <summary>The writer of the account, or null when it has no session (offline at start, signed out).</summary>
    IMailWriter? WriterOf(string accountKey);

    /// <summary>False for a read-only account and for one without a session: menus show the write items disabled.</summary>
    bool CanWrite(string accountKey);

    /// <summary>
    /// The folders (id and role) of the account a unified row belongs to, or null when they are not known. Judging "is this message in Trash" by the
    /// active account's tree is wrong for a row of another login (R3-07); a source that routes unified rows answers here, and the default knows none.
    /// </summary>
    Task<IReadOnlyList<(string Id, MailboxRole Role)>?> FoldersOfAsync(MailTarget target, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<(string Id, MailboxRole Role)>?>(null);
}
