// M2 hotfix 2 (the user: "forbidden icon never disappears to drop icon"). The drop targets' answer, UI-free: a target may accept only an operation the
// source offers (WinUI DragEventArgs.AcceptedOperation against DataPackage.RequestedOperation), the tag rows used to answer Link to a Move-only drag,
// and every real drag of this app goes through these answers before DropController runs the drop. Owner actions.

#pragma warning disable MA0002 // asserts compare strings ordinally by nature
using Barbican.Core.DragDrop;
using Barbican.Presentation.DragDrop;
using Barbican.Presentation.Tests.Actions;

namespace Barbican.Presentation.Tests.DragDrop;

public sealed class DropAcceptanceTests
{
    [Fact]
    public void The_operation_values_are_the_platforms()
    {
        // Windows.ApplicationModel.DataTransfer.DataPackageOperation: None 0, Copy 1, Move 2, Link 4 (the views cast between them)
        Assert.Equal(0, (int)DragOperations.None);
        Assert.Equal(1, (int)DragOperations.Copy);
        Assert.Equal(2, (int)DragOperations.Move);
        Assert.Equal(4, (int)DragOperations.Link);
    }

    [Fact]
    public void Every_target_answer_is_an_operation_the_source_offers()
    {
        Assert.Equal(DragOperations.Move, DropAcceptance.Pick(DropAcceptance.SourceOffers, DragOperations.Move));
        Assert.Equal(DragOperations.Link, DropAcceptance.Pick(DropAcceptance.SourceOffers, DragOperations.Link, DragOperations.Move));
        // the old tag answer: Link against a Move-only source is refused by the platform; the fallback is the move the source offers
        Assert.Equal(DragOperations.Move, DropAcceptance.Pick(DragOperations.Move, DragOperations.Link, DragOperations.Copy, DragOperations.Move));
        Assert.Equal(DragOperations.None, DropAcceptance.Pick(DragOperations.Copy, DragOperations.Move));
    }

    [Fact]
    public async Task A_mail_drag_is_accepted_by_another_folder_and_a_tag_but_not_by_the_folder_it_is_only_in()
    {
        using var h = await ActionHarness.StartAsync();
        await h.OpenAsync("inbox");
        var dnd = new DropController(h.Actions, h.Bus);
        Assert.Equal(DragOperations.None, DropAcceptance.ForFolder(dnd, h.Folder("archive"), DropAcceptance.SourceOffers));

        dnd.BeginMailDrag(h.Pick(0));
        Assert.Equal(DragOperations.Move, DropAcceptance.ForFolder(dnd, h.Folder("archive"), DropAcceptance.SourceOffers));
        Assert.Equal(DragOperations.None, DropAcceptance.ForFolder(dnd, h.Folder("inbox"), DropAcceptance.SourceOffers));
        Assert.Equal(DragOperations.Link, DropAcceptance.ForTag(dnd, DropAcceptance.SourceOffers));
        Assert.Equal(DragOperations.Move, DropAcceptance.ForTag(dnd, DragOperations.Move));
        Assert.Equal("Move to Archive", DropAcceptance.FolderCaption(dnd, "Archive", DropZone.Inside));
        dnd.CancelDrag();
        Assert.Equal(DragOperations.None, DropAcceptance.ForTag(dnd, DropAcceptance.SourceOffers));
    }

    [Fact]
    public async Task A_folder_drag_is_accepted_by_other_folders_with_its_zone_and_never_by_a_tag_or_itself()
    {
        using var h = await ActionHarness.StartAsync();
        var dnd = new DropController(h.Actions, h.Bus);
        var projects = h.Server.Store.AddMailbox("Projects", null);
        dnd.BeginFolderDrag("acct", projects, "Projects");
        Assert.Equal(DragOperations.Move, DropAcceptance.ForFolder(dnd, h.Folder("archive"), DropAcceptance.SourceOffers));
        Assert.Equal(DragOperations.None, DropAcceptance.ForFolder(dnd, projects, DropAcceptance.SourceOffers));
        Assert.Equal(DragOperations.None, DropAcceptance.ForTag(dnd, DropAcceptance.SourceOffers));
        Assert.Equal("Before Archive", DropAcceptance.FolderCaption(dnd, "Archive", DropController.ZoneAt(2, 32)));
        Assert.Equal("After Archive", DropAcceptance.FolderCaption(dnd, "Archive", DropController.ZoneAt(30, 32)));
        Assert.Equal("Into Archive", DropAcceptance.FolderCaption(dnd, "Archive", DropController.ZoneAt(16, 32)));
    }
}
