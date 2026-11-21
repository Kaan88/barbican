// Mirrors: Bulwark lib/move-menu.ts:115-118 isTarget (not Drafts, not Sent, mayAddItems) and hooks/use-message-file-actions.ts:174-179
// (copyTargets drop accounts whose folders are not loaded); G-048. Owner actions.

using Barbican.Core.Mail.Model;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Menus;

namespace Barbican.Presentation.Actions;

/// <summary>Builds the picker's accounts and folders from the folder trees of the other signed-in accounts.</summary>
public static class CopyTargetAccounts
{
    /// <summary>One entry per account with at least one folder a copy may land in, in the order given.</summary>
    public static IReadOnlyList<CopyTargetAccount> Build(IReadOnlyList<(string AccountKey, string Label, MailboxTree Tree)> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        var result = new List<CopyTargetAccount>();
        foreach (var (key, label, tree) in accounts)
        {
            var byId = tree.All.ToDictionary(m => m.Id, StringComparer.Ordinal);
            var folders = FolderRows.Build(tree, new HashSet<string>(StringComparer.Ordinal), new FolderRowOptions())
                .Where(r => IsTarget(r, byId))
                .Select(r => new CopyTargetFolder(r.Id, r.Name, r.Depth))
                .ToList();
            if (folders.Count > 0)
            {
                result.Add(new CopyTargetAccount(key, label, folders));
            }
        }

        return result;
    }

    private static bool IsTarget(FolderRowViewModel row, Dictionary<string, Mailbox> byId) =>
        !row.Id.StartsWith("__", StringComparison.Ordinal)
        && row.Role is not MailboxRole.Drafts and not MailboxRole.Sent
        && (!byId.TryGetValue(row.Id, out var mailbox) || MailboxRights.From(mailbox).MayAddItems);
}
