using System.Collections.Specialized;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Shell;
using Barbican.App.Views.Menus;
using Barbican.Presentation.DragDrop;
using Microsoft.UI.Xaml.Automation;
using Windows.ApplicationModel.DataTransfer;

namespace Barbican.App.Views.Sidebar;

/// <summary>The folders column. Selecting a row is <see cref="SidebarViewModel.SelectFolderAsync"/>; the selection shown follows the ViewModel (a g-key jump moves it too).</summary>
public sealed partial class SidebarView : UserControl
{
    private bool _syncing;

    public SidebarView()
    {
        Sidebar = ViewHost.Get<SidebarViewModel>();
        Look = ViewHost.Get<StyleState>();
        InitializeComponent();
        SlotHost.Fill(AccountHeaderSlot, SlotNames.SidebarAccountHeader);
        SlotHost.Fill(UnifiedSectionSlot, SlotNames.SidebarUnified);
        SlotHost.Fill(SharedSectionSlot, SlotNames.SidebarShared);
        Panes = ViewHost.Get<PanesViewModel>();
        ApplyStyle();
        Look.PropertyChanged += (_, _) => ApplyStyle();
        Panes.PropertyChanged += (_, _) => ApplyStyle();
        Sidebar.PropertyChanged += OnSidebarChanged;
        Sidebar.Folders.CollectionChanged += OnFoldersChanged;
        Loaded += (_, _) => SyncSelection();

        // handled events too: the ListView may mark drag events of its items handled
        FolderList.AddHandler(DragOverEvent, new DragEventHandler(OnFolderDragOver), handledEventsToo: true);
        FolderList.AddHandler(DropEvent, new DragEventHandler(OnFolderDrop), handledEventsToo: true);
    }

    public SidebarViewModel Sidebar { get; }

    public StyleState Look { get; }

    /// <summary>The saved pane widths (panes.json, read once at start): 0 keeps the style's own width.</summary>
    public PanesViewModel Panes { get; }

    // The item containers differ per style; they are swapped once per style change, not per row. Tags are rows in both styles (prototype labels).
    private void ApplyStyle()
    {
        var width = Panes.SidebarWidth > 0 ? Panes.SidebarWidth : Look.SidebarWidth;
        if (Math.Abs(Pane.Width - width) > 0.01 || double.IsNaN(Pane.Width))
        {
            Pane.Width = width;
        }

        var style = Look.FolderItemStyle;
        if (!ReferenceEquals(FolderList.ItemContainerStyle, style))
        {
            FolderList.ItemContainerStyle = style;
        }
    }

    private void OnFolderSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && e.AddedItems.Count > 0 && e.AddedItems[0] is FolderRowViewModel row)
        {
            _ = Sidebar.SelectFolderAsync(row.Id);
        }
    }

    private void OnSidebarChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(SidebarViewModel.SelectedFolderId), StringComparison.Ordinal))
        {
            SyncSelection();
        }
        else if (string.Equals(e.PropertyName, nameof(SidebarViewModel.StorageFraction), StringComparison.Ordinal))
        {
            SizeMeter();
        }
    }

    private void OnFoldersChanged(object? sender, NotifyCollectionChangedEventArgs e) => SyncSelection();

    private void SyncSelection()
    {
        var id = Sidebar.SelectedFolderId;
        var row = id is null ? null : Sidebar.Folders.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.Ordinal));
        if (ReferenceEquals(FolderList.SelectedItem, row))
        {
            return;
        }

        _syncing = true;
        try
        {
            FolderList.SelectedItem = row;
        }
        finally
        {
            _syncing = false;
        }
    }

    // The item container is the selectable element UIA and the automation commands see, so it carries the id and the name (the folder's id and name).
#pragma warning disable CA1822 // a XAML event handler
    private void OnFolderContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is FolderRowViewModel row)
        {
            AutomationProperties.SetAutomationId(args.ItemContainer, row.Id);
            AutomationProperties.SetName(args.ItemContainer, row.Name);
        }
    }
#pragma warning restore CA1822

    private void OnChevronClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            Sidebar.ToggleFolder(id);
        }
    }

    // ---- M2 hotfix: real drag and drop (HAND TEST 2 "can't drag"); DropController keeps the drag and runs the drop (the code dnd-sim runs) ----

    private static DropController Drops => ViewHost.Get<DropController>();

    private void OnFolderDragStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.OfType<FolderRowViewModel>().FirstOrDefault() is not { } row || ViewHost.Get<IActiveMail>().AccountKey is not { } account)
        {
            e.Cancel = true;
            return;
        }

        Drops.BeginFolderDrag(account, row.Id, row.Name);
        e.Data.SetText(row.Name);
        e.Data.RequestedOperation = DataPackageOperation.Move;
    }

#pragma warning disable CA1822 // XAML event handler
    private void OnFolderDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) => Drops.CancelDrag();
#pragma warning restore CA1822

    private void OnFolderDragOver(object sender, DragEventArgs e)
    {
        var drops = Drops;
        if (FolderUnder(e) is not { } hit || !drops.CanDropOnFolder(hit.Row.Id))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            drops.Target = null;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Move;
        e.Handled = true;
        drops.Target = hit.Row.Id;
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.Caption = drops.DraggedFolder is null
            ? "Move to " + hit.Row.Name
            : DropController.ZoneAt(e.GetPosition(hit.Container).Y, hit.Container.ActualHeight) switch
            {
                Barbican.Core.DragDrop.DropZone.Before => "Before " + hit.Row.Name,
                Barbican.Core.DragDrop.DropZone.After => "After " + hit.Row.Name,
                _ => "Into " + hit.Row.Name,
            };
    }

    // a failed drop reports itself with a toast (MailActionService); the task is not awaited here
    private void OnFolderDrop(object sender, DragEventArgs e)
    {
        if (FolderUnder(e) is not { } hit || !Drops.CanDropOnFolder(hit.Row.Id))
        {
            return;
        }

        e.Handled = true;
        var zone = DropController.ZoneAt(e.GetPosition(hit.Container).Y, hit.Container.ActualHeight);
        _ = Drops.DropOnFolderAsync(hit.Row.Id, zone, CancellationToken.None);
    }

    private void OnTagDragOver(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TagRowViewModel tag } && Drops.CanDropOnTag)
        {
            e.AcceptedOperation = DataPackageOperation.Link;
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.Caption = "Tag " + tag.Label;
            Drops.Target = tag.Id;
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }

        e.Handled = true;
    }

#pragma warning disable VSTHRD100 // XAML event handler; a failed drop reports itself with a toast (MailActionService)
    private async void OnTagDrop(object sender, DragEventArgs e)
#pragma warning restore VSTHRD100
    {
        if (sender is FrameworkElement { DataContext: TagRowViewModel tag } && Drops.CanDropOnTag)
        {
            e.Handled = true;
            _ = await Drops.DropOnTagAsync(tag.Id, CancellationToken.None);
        }
    }

    // the folder row under the pointer and its item container (the zone is measured on the whole row)
    private static (FolderRowViewModel Row, FrameworkElement Container)? FolderUnder(DragEventArgs e)
    {
        if (VisualWalk.Up<FolderRowViewModel>(e.OriginalSource as DependencyObject) is not { } hit)
        {
            return null;
        }

        for (DependencyObject? node = hit.Element; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ListViewItem item)
            {
                return (hit.Context, item);
            }
        }

        return (hit.Context, hit.Element);
    }

    private void OnFoldersHeaderClick(object sender, RoutedEventArgs e) => Sidebar.FoldersExpanded = !Sidebar.FoldersExpanded;

    private void OnTagsHeaderClick(object sender, RoutedEventArgs e) => Sidebar.TagsExpanded = !Sidebar.TagsExpanded;

    private void OnMeterSizeChanged(object sender, SizeChangedEventArgs e) => SizeMeter();

    private void SizeMeter() => MeterFill.Width = Math.Max(0, Meter.ActualWidth * Math.Clamp(Sidebar.StorageFraction, 0, 1));
}
