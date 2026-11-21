using System.Collections.Specialized;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Commands;
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
    private bool _folderSyncQueued;

    public SidebarView()
    {
        Sidebar = ViewHost.Get<SidebarViewModel>();
        Look = ViewHost.Get<StyleState>();
        InitializeComponent();
        ViewHost.Get<Barbican.App.Views.Accounts.AccountFlyoutKeys>().Track(HeaderAccountButton.Flyout!);
        HeaderPopover.Chosen += (_, _) => HeaderAccountButton.Flyout?.Hide();
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

        // M2 hotfix 2: every folder row container is a drop target of its own (OnFolderContainerChanging); the list itself only catches what no row
        // took (handled events too: the ListView marks the drag events of its items handled)
        FolderList.AddHandler(DragEnterEvent, new DragEventHandler(OnFolderListDragOver), handledEventsToo: true);
        FolderList.AddHandler(DragOverEvent, new DragEventHandler(OnFolderListDragOver), handledEventsToo: true);
        FolderList.AddHandler(DragLeaveEvent, new DragEventHandler(OnFolderListDragLeave), handledEventsToo: true);
        FolderList.AddHandler(DropEvent, new DragEventHandler(OnFolderListDrop), handledEventsToo: true);
    }

    public event EventHandler? MessagesRequested;

    internal void SetCompactBack(bool visible) => CompactBack.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private void OnCompactBack(object sender, RoutedEventArgs e) => MessagesRequested?.Invoke(this, EventArgs.Empty);

    public SidebarViewModel Sidebar { get; }

    public RailViewModel Account { get; } = ViewHost.Get<RailViewModel>();

    public StyleState Look { get; }

    /// <summary>The saved pane widths (panes.json, read once at start): 0 keeps the style's own width.</summary>
    public PanesViewModel Panes { get; }

    // The item containers differ per style; they are swapped once per style change, not per row. Tags are rows in both styles (prototype labels).
    private void ApplyStyle()
    {
        // Only the active style owns the public automation id.
        AutomationProperties.SetAutomationId(ComposeButton, Look.IsBulwark ? "sidebar.compose.legacy" : "sidebar.compose");
        AutomationProperties.SetAutomationId(BulwarkComposeButton, Look.IsBulwark ? "sidebar.compose" : "sidebar.compose.bulwark");
        var width = Panes.SidebarWidth > 0 ? Panes.SidebarWidth : Look.SidebarWidth;
        if (Math.Abs(Pane.Width - width) > 0.01 || double.IsNaN(Pane.Width))
        {
            Pane.Width = width;
        }

        // Bulwark rows: the local square row with the accent selection and its 2 px bar (SidebarRows.xaml); the legacy rows keep the shared pill styles
        var style = Look.IsBulwark ? (Style)Resources["ListItem.Sidebar.Bulwark"] : Look.FolderItemStyle;
        if (TagRepeater.Layout is StackLayout tagLayout)
        {
            tagLayout.Spacing = Look.IsBulwark ? 0 : 2;
        }

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

    private void OnFoldersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_folderSyncQueued) return;
        _folderSyncQueued = DispatcherQueue.TryEnqueue(() =>
        {
            _folderSyncQueued = false;
            SyncSelection();
        });
    }

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

        WireDropTarget(args.ItemContainer);
    }
#pragma warning restore CA1822

    private void OnChevronClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            Sidebar.ToggleFolder(id);
        }
    }

#pragma warning disable VSTHRD100 // XAML event handlers return void; a failed command reports itself with a toast
    private async void OnTagClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            await Sidebar.SelectTagAsync(id);
        }
    }

    private async void OnComposeClick(object sender, RoutedEventArgs e) => await RunAsync(CommandIds.Compose);

    private async void OnNewFolderClick(object sender, RoutedEventArgs e) => await RunAsync(CommandIds.NewFolder);

    private async void OnNewTagClick(object sender, RoutedEventArgs e) => await RunAsync(CommandIds.NewTag);
#pragma warning restore VSTHRD100

    // the gears open Settings > Folders & tags (Bulwark sidebar.tsx:2430-2431 buildSettingsPath('organize', 'folders' | 'tags'))
    private void OnFolderSettingsClick(object sender, RoutedEventArgs e) => OpenOrganize("folders");

    private void OnTagSettingsClick(object sender, RoutedEventArgs e) => OpenOrganize("tags");

    private static void OpenOrganize(string anchor)
    {
        if (ViewHost.Get<ShellViewModel>().TryActivate(AppId.Settings))
        {
            ViewHost.Get<Barbican.Presentation.Settings.ISettingsNavigation>().Open(Barbican.Presentation.Settings.SettingsPageIds.Organize, anchor);
        }
    }

    private void OnTagChevronClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            Sidebar.ToggleTag(id);
        }
    }

    // the header buttons run the same commands as the menus; the prompt dialog asks for the name, and an error comes back as a toast
    private static async Task RunAsync(string commandId)
    {
        var registry = ViewHost.Get<ICommandRegistry>();
        if (!registry.TryGet(commandId, out var command))
        {
            return;
        }

        var context = new CommandContext(CommandSurface.Toolbar, [], null, null, ViewHost.Get<IActiveMail>().AccountKey);
        if (!command.CanExecute(context))
        {
            return;
        }

        // a failed result and a thrown exception both end in an error toast (Fable review 3 R3-05)
        _ = await (ViewHost.Find<CommandRunner>() ?? new CommandRunner(ViewHost.Find<Barbican.Presentation.Actions.IToastService>())).RunAsync(command, context, toastFailure: true);
    }

    // ArrowRight and ArrowLeft unfold and fold the selected folder (I-060)
    private void OnFolderKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var handled = e.Key switch
        {
            Windows.System.VirtualKey.Right => Sidebar.SetSelectedExpanded(true),
            Windows.System.VirtualKey.Left => Sidebar.SetSelectedExpanded(false),
            _ => false,
        };
        e.Handled = handled;
    }

    // ---- Drag and drop. M2 hotfix: real drag sources and targets; DropController keeps the drag and runs the drop (the code dnd-sim runs).
    // M2 hotfix 2 (the user: "forbidden icon never disappears"): every row is its own drop target (AllowDrop on the ListViewItem, handlers on it, so
    // the row is the sender and not guessed from OriginalSource), DragEnter answers as DragOver does, the answer comes from DropAcceptance (only an
    // operation the source offers), the row under the pointer is marked (DropHighlight) and the drag shows the small badge (DragBadge). ----

    private static readonly HashSet<ListViewItem> Wired = [];
    private static DragEventArgs? _taken;

    private static DropController Drops => ViewHost.Get<DropController>();

    private static DragOperations Offered(DragEventArgs e) => (DragOperations)(int)e.DataView.RequestedOperation;

    private static void WireDropTarget(Microsoft.UI.Xaml.Controls.Primitives.SelectorItem container)
    {
        if (container is not ListViewItem item || !Wired.Add(item))
        {
            return;
        }

        item.AllowDrop = true;
        item.AddHandler(DragEnterEvent, new DragEventHandler(OnFolderRowDragOver), handledEventsToo: true);
        item.AddHandler(DragOverEvent, new DragEventHandler(OnFolderRowDragOver), handledEventsToo: true);
        item.AddHandler(DragLeaveEvent, new DragEventHandler(OnFolderRowDragLeave), handledEventsToo: true);
        item.AddHandler(DropEvent, new DragEventHandler(OnFolderRowDrop), handledEventsToo: true);
    }

    private void OnFolderDragStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.OfType<FolderRowViewModel>().FirstOrDefault() is not { } row || ViewHost.Get<IActiveMail>().AccountKey is not { } account)
        {
            e.Cancel = true;
            return;
        }

        Drops.BeginFolderDrag(account, row.Id, row.Name);
        e.Data.SetText(row.Name);
        e.Data.RequestedOperation = (DataPackageOperation)(int)DropAcceptance.SourceOffers;
        Actions.DragBadge.Prepare(row.Name);
    }

#pragma warning disable CA1822 // XAML event handler
    private void OnFolderDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        Actions.DropHighlight.Clear();
        Drops.CancelDrag();
    }
#pragma warning restore CA1822

    private static void OnFolderRowDragOver(object sender, DragEventArgs e)
    {
        if (sender is ListViewItem { Content: FolderRowViewModel row } item)
        {
            AnswerFolder(e, row, item);
        }
    }

    private static void OnFolderRowDragLeave(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement item)
        {
            Actions.DropHighlight.Clear(item);
        }
    }

    private static void OnFolderRowDrop(object sender, DragEventArgs e)
    {
        if (sender is ListViewItem { Content: FolderRowViewModel row } item)
        {
            DropOnFolder(e, row, item);
        }
    }

    // the list catches what no row took: the row is found under the pointer
    private void OnFolderListDragOver(object sender, DragEventArgs e)
    {
        if (ReferenceEquals(e, _taken))
        {
            return;
        }

        if (FolderUnder(e) is { } hit)
        {
            AnswerFolder(e, hit.Row, hit.Container);
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
            Actions.DragBadge.Apply(e.DragUIOverride);
            Actions.DropHighlight.Clear();
            Drops.Target = null;
        }
    }

#pragma warning disable CA1822 // XAML event handler
    private void OnFolderListDragLeave(object sender, DragEventArgs e)
    {
        Actions.DropHighlight.Clear();
        Drops.Target = null;
    }
#pragma warning restore CA1822

    private void OnFolderListDrop(object sender, DragEventArgs e)
    {
        if (!ReferenceEquals(e, _taken) && FolderUnder(e) is { } hit)
        {
            DropOnFolder(e, hit.Row, hit.Container);
        }
    }

    private static void AnswerFolder(DragEventArgs e, FolderRowViewModel row, FrameworkElement container)
    {
        var drops = Drops;
        _taken = e;
        e.Handled = true;
        Actions.DragBadge.Apply(e.DragUIOverride);
        var accepted = DropAcceptance.ForFolder(drops, row.Id, Offered(e));
        e.AcceptedOperation = (DataPackageOperation)(int)accepted;
        if (accepted == DragOperations.None)
        {
            Actions.DropHighlight.Clear(container);
            drops.Target = null;
            e.DragUIOverride.IsCaptionVisible = false;
            return;
        }

        var zone = DropController.ZoneAt(e.GetPosition(container).Y, container.ActualHeight);
        drops.Target = row.Id;
        Actions.DropHighlight.Show(container, zone, line: drops.DraggedFolder is not null);
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.Caption = DropAcceptance.FolderCaption(drops, row.Name, zone);
    }

    // a failed drop reports itself with a toast (MailActionService); the task is not awaited here
    private static void DropOnFolder(DragEventArgs e, FolderRowViewModel row, FrameworkElement container)
    {
        _taken = e;
        Actions.DropHighlight.Clear();
        if (!Drops.CanDropOnFolder(row.Id))
        {
            return;
        }

        e.Handled = true;
        e.AcceptedOperation = DataPackageOperation.Move;
        var zone = DropController.ZoneAt(e.GetPosition(container).Y, container.ActualHeight);
        _ = Drops.DropOnFolderAsync(row.Id, zone, CancellationToken.None);
    }

#pragma warning disable CA1822 // XAML event handler
    private void OnTagDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        Actions.DragBadge.Apply(e.DragUIOverride);
        if (sender is not FrameworkElement { DataContext: TagRowViewModel tag } row)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        var accepted = DropAcceptance.ForTag(Drops, Offered(e));
        e.AcceptedOperation = (DataPackageOperation)(int)accepted;
        if (accepted == DragOperations.None)
        {
            Actions.DropHighlight.Clear(row);
            e.DragUIOverride.IsCaptionVisible = false;
            return;
        }

        Drops.Target = tag.Id;
        Actions.DropHighlight.Show(row, Barbican.Core.DragDrop.DropZone.Inside, line: false);
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.Caption = "Tag " + tag.Label;
    }

    private void OnTagDragLeave(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement row)
        {
            Actions.DropHighlight.Clear(row);
        }

        Drops.Target = null;
    }
#pragma warning restore CA1822

#pragma warning disable VSTHRD100, CA1822 // XAML event handler; a failed drop reports itself with a toast (MailActionService)
    private async void OnTagDrop(object sender, DragEventArgs e)
#pragma warning restore VSTHRD100, CA1822
    {
        Actions.DropHighlight.Clear();
        if (sender is FrameworkElement { DataContext: TagRowViewModel tag } && Drops.CanDropOnTag)
        {
            e.Handled = true;
            e.AcceptedOperation = (DataPackageOperation)(int)DropAcceptance.ForTag(Drops, Offered(e));
            _ = await Drops.DropOnTagAsync(tag.Id, CancellationToken.None);
        }
    }

    // the folder row under the pointer and its item container: from the event's source, else by hit-testing the pointer position
    private (FolderRowViewModel Row, FrameworkElement Container)? FolderUnder(DragEventArgs e)
    {
        if (VisualWalk.Up<FolderRowViewModel>(e.OriginalSource as DependencyObject) is { } hit)
        {
            return (hit.Context, ContainerOf(hit.Element) ?? hit.Element);
        }

        // host coordinates: the position relative to the window's root content (Uno's GetPosition does not take null)
        if (FolderList.XamlRoot?.Content is not { } root)
        {
            return null;
        }

        foreach (var element in VisualTreeHelper.FindElementsInHostCoordinates(e.GetPosition(root), FolderList))
        {
            if (element is ListViewItem { Content: FolderRowViewModel row } item)
            {
                return (row, item);
            }
        }

        return null;
    }

    private static ListViewItem? ContainerOf(DependencyObject element)
    {
        for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ListViewItem item)
            {
                return item;
            }
        }

        return null;
    }

    private void OnFoldersHeaderClick(object sender, RoutedEventArgs e) => Sidebar.FoldersExpanded = !Sidebar.FoldersExpanded;

    private void OnTagsHeaderClick(object sender, RoutedEventArgs e) => Sidebar.TagsExpanded = !Sidebar.TagsExpanded;

    private void OnMeterSizeChanged(object sender, SizeChangedEventArgs e) => SizeMeter();

    private void SizeMeter() => MeterFill.Width = Math.Max(0, Meter.ActualWidth * Math.Clamp(Sidebar.StorageFraction, 0, 1));
}
