using System.Collections.Specialized;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Commands;
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
        Panes = ViewHost.Get<PanesViewModel>();
        ApplyStyle();
        Look.PropertyChanged += (_, _) => ApplyStyle();
        Panes.PropertyChanged += (_, _) => ApplyStyle();
        Sidebar.PropertyChanged += OnSidebarChanged;
        Sidebar.Folders.CollectionChanged += OnFoldersChanged;
        Loaded += (_, _) => SyncSelection();
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

#pragma warning disable VSTHRD100 // XAML event handlers return void; a failed command reports itself with a toast
    private async void OnTagClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            await Sidebar.SelectTagAsync(id);
        }
    }

    private async void OnNewFolderClick(object sender, RoutedEventArgs e) => await RunAsync(CommandIds.NewFolder);

    private async void OnNewTagClick(object sender, RoutedEventArgs e) => await RunAsync(CommandIds.NewTag);
#pragma warning restore VSTHRD100

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

        var result = await command.ExecuteAsync(context, CancellationToken.None);
        if (!result.IsOk)
        {
            ViewHost.Find<IToastService>()?.Show(new ToastRequest(result.Error?.Message ?? "That did not work.", IsError: true));
        }
    }

    private void OnFoldersHeaderClick(object sender, RoutedEventArgs e) => Sidebar.FoldersExpanded = !Sidebar.FoldersExpanded;

    private void OnTagsHeaderClick(object sender, RoutedEventArgs e) => Sidebar.TagsExpanded = !Sidebar.TagsExpanded;

    private void OnMeterSizeChanged(object sender, SizeChangedEventArgs e) => SizeMeter();

    private void SizeMeter() => MeterFill.Width = Math.Max(0, Meter.ActualWidth * Math.Clamp(Sidebar.StorageFraction, 0, 1));
}
