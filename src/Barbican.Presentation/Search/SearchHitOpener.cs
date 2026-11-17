// Mirrors: Rust palette.rs:550 open_hit (a mail hit: go to Mail, show its folder, open the message once the folder's list has it); Bulwark lib/global-search/open-hit.ts. T-102 (the folder of a hit).

using Barbican.Presentation.Mail;
using Barbican.Presentation.Shell;

namespace Barbican.Presentation.Search;

/// <summary>Opens a search hit in its folder.</summary>
public sealed class SearchHitOpener(MessageListViewModel list, SidebarViewModel sidebar)
{
    /// <summary>Shows the hit's folder (unless it is the open one) and selects the message once the list has it; false when the message is not among the rows loaded (beyond the first page).</summary>
    public async Task<bool> OpenMailAsync(string emailId, string folderId)
    {
        ArgumentException.ThrowIfNullOrEmpty(emailId);
        if (folderId.Length > 0 && !string.Equals(list.MailboxId, folderId, StringComparison.Ordinal))
        {
            await sidebar.SelectFolderAsync(folderId).ConfigureAwait(true);
        }

        var row = list.Messages.FirstOrDefault(m => string.Equals(m.Id, emailId, StringComparison.Ordinal));
        if (row is null)
        {
            return false;
        }

        list.Select(row, OpenReason.Search, ListDirection.None);
        return true;
    }
}
