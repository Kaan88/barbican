#if AUTOMATION
// Mirrors: spec/automation-protocol.md `dnd-sim SRC DST`: runs the drop path a real drop runs (DropController), with the Rust rule that a dragged row stands for the checked rows when it is one of them.
// SRC: a message id or #N (0-based); folder:ID|ROLE. DST: folder:ID|ROLE[:before|after|inside] or tag:ID.

using Barbican.Automation;
using Barbican.Core.DragDrop;
using Barbican.Core.Mail.Model;
using Barbican.Presentation.Actions;
using Barbican.Presentation.DragDrop;
using Barbican.Presentation.Mail;

namespace Barbican.App.Views.Actions.Automation;

/// <summary>The <c>dnd-sim</c> command.</summary>
public sealed class DndSimCommand(DropController drops, MessageListViewModel list, IMailboxDirectory folders, IActiveMail active) : IAutomationCommand
{
    public string Name => "dnd-sim";

    public async Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        var parts = (args ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return "error usage: dnd-sim SRC DST";
        }

        var (kind, id, zone) = Split(parts[1]);
        if (parts[0].StartsWith("folder:", StringComparison.Ordinal))
        {
            var z = zone switch { "before" => DropZone.Before, "after" => DropZone.After, _ => DropZone.Inside };
            var moved = await drops.DropFolderAsync(active.AccountKey ?? string.Empty, FolderId(parts[0]["folder:".Length..]) ?? string.Empty, FolderId(id) ?? string.Empty, z, cancellationToken);
            return moved.IsOk ? "ok folder" : "error " + moved.Error!.Message;
        }

        var row = RowRef.Find(list.Messages.ToList(), parts[0]);
        if (row is null)
        {
            return "error no such row " + parts[0];
        }

        var current = list.Current;
        IReadOnlyList<MailTarget> targets = current.Any(t => string.Equals(t.EmailId, row.Id, StringComparison.Ordinal)) ? current : [list.TargetOf(row)];
        drops.BeginMailDrag([.. targets.Select(t => t.EmailId)], targets.Count == 1 ? "1 message" : targets.Count + " messages");
        var result = string.Equals(kind, "tag", StringComparison.Ordinal)
            ? await drops.DropMailOnTagAsync(targets, id, cancellationToken)
            : await drops.DropMailOnFolderAsync(targets, FolderId(id) ?? string.Empty, cancellationToken);
        return result.IsOk ? $"ok {kind} {targets.Count}" : "error " + result.Error!.Message;
    }

    private static (string Kind, string Id, string? Zone) Split(string dst)
    {
        var p = dst.Split(':');
        return p.Length switch
        {
            >= 3 => (p[0], p[1], p[2]),
            2 => (p[0], p[1], null),
            _ => (string.Empty, dst, null),
        };
    }

    /// <summary>A folder id, or the folder with that role name (inbox, archive, trash, junk, sent, drafts) or name.</summary>
    private string? FolderId(string idOrRole)
    {
        var all = folders.Tree.All;
        return all.FirstOrDefault(m => string.Equals(m.Id, idOrRole, StringComparison.Ordinal))?.Id
            ?? all.FirstOrDefault(m => string.Equals(m.Role.ToString(), idOrRole, StringComparison.OrdinalIgnoreCase))?.Id
            ?? all.FirstOrDefault(m => string.Equals(m.Name, idOrRole, StringComparison.OrdinalIgnoreCase))?.Id;
    }
}
#endif
