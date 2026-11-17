using System.Collections.Specialized;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Shell;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.List;

/// <summary>
/// The message list. It shows <see cref="MessageListViewModel.Rows"/> and keeps the ListView's selection equal to <see cref="MessageListViewModel.Selected"/>
/// (a click selects through the ViewModel, a j/k move or a push delta moves the highlight), asks for the next page when fewer than 300 px remain below the viewport
/// (spec/layout/mail-list.md, I-146) and forwards the search box's Enter.
/// </summary>
public sealed partial class MessageListView : UserControl
{
    private const double LoadMoreDistance = 300;

    private bool _syncing;
    private bool _resyncQueued;
    private ScrollViewer? _scroll;

    public MessageListView()
    {
        List = ViewHost.Get<MessageListViewModel>();
        Glance = ViewHost.Get<GlanceTilesViewModel>();
        Look = ViewHost.Get<StyleState>();
        InitializeComponent();
        ApplyStyle();
        Look.PropertyChanged += (_, _) => ApplyStyle();
        List.PropertyChanged += OnListChanged;
        List.Rows.CollectionChanged += OnRowsChanged;
        SlotHost.Fill(SearchSlot, SlotNames.ListSearch);
        SlotHost.Fill(SelectionBarSlot, SlotNames.ListSelectionBar);
        Rows.Loaded += (_, _) => HookScroll();
        Rows.SizeChanged += (_, _) => CheckLoadMore();
        UpdateEmpty();
    }

    public MessageListViewModel List { get; }

    public GlanceTilesViewModel Glance { get; }

    public StyleState Look { get; }

    private void ApplyStyle() => Rows.ItemContainerStyle = Look.MessageItemStyle;

    private void OnListChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MessageListViewModel.Selected):
                SyncSelection();
                break;
            case nameof(MessageListViewModel.IsLoading):
            case nameof(MessageListViewModel.IsConnecting):
            case nameof(MessageListViewModel.CanLoadMore):
                UpdateEmpty();
                CheckLoadMore();
                break;
            default:
                break;
        }
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateEmpty();
        // the ListView drops its selection when the selected row is replaced or removed; put it back once the change is through
        if (!_resyncQueued)
        {
            _resyncQueued = true;
            DispatcherQueue.TryEnqueue(() =>
            {
                _resyncQueued = false;
                SyncSelection();
                CheckLoadMore();
            });
        }
    }

    // an account on screen without its session yet (no network at start, R2-02) says so instead of "No messages"
    private void UpdateEmpty()
    {
        EmptyText.Text = List.IsConnecting ? "Connecting…" : "No messages";
        EmptyText.Visibility = List.Rows.Count == 0 && !List.IsLoading ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SyncSelection()
    {
        var id = List.Selected?.Id;
        var row = id is null ? null : List.Rows.OfType<MessageRowViewModel>().FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal));
        if (ReferenceEquals(Rows.SelectedItem, row))
        {
            return;
        }

        _syncing = true;
        try
        {
            Rows.SelectedItem = row;
        }
        finally
        {
            _syncing = false;
        }

        if (row is not null)
        {
            Rows.ScrollIntoView(row);
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || e.AddedItems.Count == 0)
        {
            return;
        }

        if (e.AddedItems[0] is MessageRowViewModel row)
        {
            List.Select(row, OpenReason.Click, ListDirection.None);
        }
        else
        {
            SyncSelection();
        }
    }

    // A date-group header is not selectable.
#pragma warning disable CA1822 // XAML event handler
    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        args.ItemContainer.IsEnabled = args.Item is not GroupHeaderViewModel;
        if (args.Item is MessageRowViewModel row)
        {
            AutomationProperties.SetAutomationId(args.ItemContainer, row.Id);
            AutomationProperties.SetName(args.ItemContainer, row.Subject);
        }
    }
#pragma warning restore CA1822

    private void HookScroll()
    {
        if (_scroll is not null)
        {
            return;
        }

        _scroll = FindDescendant<ScrollViewer>(Rows);
        if (_scroll is not null)
        {
            _scroll.ViewChanged += (_, _) => CheckLoadMore();
        }
    }

    private void CheckLoadMore()
    {
        if (!List.CanLoadMore || List.IsLoading || _scroll is null || List.Rows.Count == 0)
        {
            return;
        }

        if (_scroll.ScrollableHeight - _scroll.VerticalOffset < LoadMoreDistance)
        {
            List.LoadMoreCommand.Execute(null);
        }
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } deeper)
            {
                return deeper;
            }
        }

        return default;
    }
}
