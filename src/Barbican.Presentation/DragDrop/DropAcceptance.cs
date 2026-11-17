// Mirrors: WinUI 3 drag and drop (Microsoft.UI.Xaml.UIElement.DragEnter / DragOver: the target says what it accepts in DragEventArgs.AcceptedOperation,
// which must be one of the operations the source allows in DataPackage.RequestedOperation, else the system shows "no drop"); Bulwark
// components/layout/sidebar.tsx:540 (a valid drop target: bg-primary/20 ring-2 ring-primary), hooks/use-mailbox-drop.ts, use-tag-drop.ts. Owner actions.
//
// M2 hotfix 2 (the user: "forbidden icon never disappears to drop icon"): the tag rows accepted Link while the source allowed only Move, which the
// system refuses, and the folder rows looked for the row in DragEventArgs.OriginalSource, which is not reliably the row. This is the one decision the
// views ask, UI-free so it is tested without a window; the views pass the source's operations and the row under the pointer.

namespace Barbican.Presentation.DragDrop;

/// <summary>What a drop target answers while something is dragged over it.</summary>
public static class DropAcceptance
{
    /// <summary>The operations a drag source of this app offers (the message list and the folder list): a move, or a link (a tag).</summary>
    public const DragOperations SourceOffers = DragOperations.Move | DragOperations.Link;

    /// <summary>
    /// The operation to put in AcceptedOperation for a folder row: Move when the drag may land there and the source allows a move, else None
    /// (the "no drop" cursor).
    /// </summary>
    public static DragOperations ForFolder(DropController drops, string folderId, DragOperations offered)
    {
        ArgumentNullException.ThrowIfNull(drops);
        if (string.IsNullOrEmpty(folderId) || !drops.CanDropOnFolder(folderId))
        {
            return DragOperations.None;
        }

        return Pick(offered, DragOperations.Move, DragOperations.Copy);
    }

    /// <summary>The operation for a tag row: a link (the message gets the tag and stays), else whatever the source allows; None when a folder is dragged.</summary>
    public static DragOperations ForTag(DropController drops, DragOperations offered)
    {
        ArgumentNullException.ThrowIfNull(drops);
        return drops.CanDropOnTag ? Pick(offered, DragOperations.Link, DragOperations.Copy, DragOperations.Move) : DragOperations.None;
    }

    /// <summary>The first wanted operation the source offers; None when it offers none of them.</summary>
    public static DragOperations Pick(DragOperations offered, params DragOperations[] wanted)
    {
        ArgumentNullException.ThrowIfNull(wanted);
        foreach (var op in wanted)
        {
            if (op != DragOperations.None && (offered & op) == op)
            {
                return op;
            }
        }

        return DragOperations.None;
    }

    /// <summary>The caption beside the cursor over a folder: where a message or folder drop lands.</summary>
    public static string FolderCaption(DropController drops, string folderName, Barbican.Core.DragDrop.DropZone zone)
    {
        ArgumentNullException.ThrowIfNull(drops);
        if (drops.DraggedFolder is null)
        {
            return "Move to " + folderName;
        }

        return zone switch
        {
            Barbican.Core.DragDrop.DropZone.Before => "Before " + folderName,
            Barbican.Core.DragDrop.DropZone.After => "After " + folderName,
            _ => "Into " + folderName,
        };
    }
}
