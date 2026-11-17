// Mirrors: Rust ctxmenu.rs mark_read_query :724 (every unread message that carries the tag), Bulwark components/layout/sidebar.tsx (the tag row menu "Mark all as read");
// RFC 8621 section 4.4.1 (hasKeyword, notKeyword). The select-all cap (10 000) bounds it like the other query-wide actions. Owner actions (m2-close-a).

using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Model;
using Barbican.Presentation.Mail;

namespace Barbican.Presentation.Actions;

public sealed partial class MailActionService
{
    /// <summary>Marks every unread message that carries a tag as read, in every folder.</summary>
    public async Task<Result<Unit>> MarkTagReadAsync(string tagId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(tagId);
        if (active.Mail is not { } mail || active.AccountKey is not { } account)
        {
            return Result<Unit>.Fail(Failed(new ValidationError("There is no account on screen.")));
        }

        var targets = new List<MailTarget>();
        ListCursor? after = null;
        do
        {
            var page = await mail.QueryAsync(new ListQuery { HasKeyword = TagKeyword(tagId), NotKeyword = Seen, PageSize = 100 }, after, ct).ConfigureAwait(true);
            if (!page.IsOk || page.Value is null)
            {
                return Result<Unit>.Fail(Failed(page.Error ?? new ValidationError("Could not read the tag.")));
            }

            targets.AddRange(page.Value.Items.Select(e => new MailTarget(account, string.Empty, e.Id, e.ThreadId ?? string.Empty, e.Id, e.MailboxIds, e.Keywords)));
            after = page.Value.Next;
        }
        while (after is not null && targets.Count < MessageListViewModel.AllInFolderCap);

        if (targets.Count == 0)
        {
            Notify("Nothing to mark as read");
            return Result<Unit>.Ok(default);
        }

        return await SetReadAsync(targets, true, ct).ConfigureAwait(true);
    }
}
