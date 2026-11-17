using System.Collections.Specialized;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Shell;
using Microsoft.UI.Xaml.Automation;

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
        ApplyStyle();
        Look.PropertyChanged += (_, _) => ApplyStyle();
        Sidebar.PropertyChanged += OnSidebarChanged;
        Sidebar.Folders.CollectionChanged += OnFoldersChanged;
        Loaded += (_, _) => SyncSelection();
    }

    public SidebarViewModel Sidebar { get; }

    public StyleState Look { get; }

    // The item containers and the tag layout differ per style; they are swapped once per style change, not per row.
    private void ApplyStyle()
    {
        FolderList.ItemContainerStyle = Look.FolderItemStyle;
        TagRepeater.Layout = Look.IsBento
            ? new UniformGridLayout { MinItemWidth = 78, MinColumnSpacing = 8, MinRowSpacing = 8, ItemsStretch = UniformGridLayoutItemsStretch.Fill }
            : new StackLayout { Orientation = Orientation.Vertical, Spacing = 2 };
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

    private void OnFoldersHeaderClick(object sender, RoutedEventArgs e) => Sidebar.FoldersExpanded = !Sidebar.FoldersExpanded;

    private void OnTagsHeaderClick(object sender, RoutedEventArgs e) => Sidebar.TagsExpanded = !Sidebar.TagsExpanded;

    private void OnMeterSizeChanged(object sender, SizeChangedEventArgs e) => SizeMeter();

    private void SizeMeter() => MeterFill.Width = Math.Max(0, Meter.ActualWidth * Math.Clamp(Sidebar.StorageFraction, 0, 1));
}
