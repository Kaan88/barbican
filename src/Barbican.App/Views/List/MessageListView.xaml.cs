using System.Collections.Specialized;
using Barbican.App.Motion;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Commands;
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
    private bool _modifierClick;
    private bool _resyncQueued;
    private ScrollViewer? _scroll;
    private readonly TranslateTransform _rowsShift = new();
    private bool _riseNext;

    public MessageListView()
    {
        List = ViewHost.Get<MessageListViewModel>();
        Glance = ViewHost.Get<GlanceTilesViewModel>();
        Look = ViewHost.Get<StyleState>();
        InitializeComponent();
        Rows.RenderTransform = _rowsShift;
        Panes = ViewHost.Get<PanesViewModel>();
        ApplyStyle();
        Look.PropertyChanged += (_, _) => ApplyStyle();
        Panes.PropertyChanged += (_, _) => ApplyStyle();
        List.PropertyChanged += OnListChanged;
        List.Rows.CollectionChanged += OnRowsChanged;
        SlotHost.Fill(SearchSlot, SlotNames.ListSearch);
        SlotHost.Fill(SelectionBarSlot, SlotNames.ListSelectionBar);
        Rows.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(OnRowsPointerPressed), true);
        Rows.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => _modifierClick = false), true);
        Rows.Loaded += (_, _) => HookScroll();
        Rows.SizeChanged += (_, _) => CheckLoadMore();
        UpdateEmpty();
    }

    public MessageListViewModel List { get; }

    public GlanceTilesViewModel Glance { get; }

    public StyleState Look { get; }

    /// <summary>The saved pane widths (panes.json, read once at start): 0 keeps the style's own width.</summary>
    public PanesViewModel Panes { get; }

    // The window width also raises StyleState changes; the containers are swapped only when the style really changed.
    private void ApplyStyle()
    {
        var width = Panes.ListWidth > 0 ? Panes.ListWidth : Look.ListWidth;
        if (Math.Abs(Pane.Width - width) > 0.01 || double.IsNaN(Pane.Width))
        {
            Pane.Width = width;
        }

        var style = Look.MessageItemStyle;
        if (!ReferenceEquals(Rows.ItemContainerStyle, style))
        {
            Rows.ItemContainerStyle = style;
        }
    }

    private void OnListChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MessageListViewModel.Selected):
                SyncSelection();
                break;
            case nameof(MessageListViewModel.MailboxId):
                _riseNext = true;
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
        // Another folder's rows are in: they lift once as a block (the prototype's list entrance without its per-row stagger, design/MOTION.md rule 3).
        // The old rows stayed until now, so nothing blanks; the new ones are fully there on the first frame.
        if (_riseNext && List.Rows.Count > 0)
        {
            _riseNext = false;
            var scale = MotionRuntime.Scale;
            Rise.Run(Rows, _rowsShift, scale.Rapid ? 0 : scale.EnterMs, fromOpacity: 0.5, fromY: 10);
        }

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

        if (_modifierClick)
        {
            // Ctrl or Shift was down: the press ticked rows (OnRowsPointerPressed); it must not also open the message
            _modifierClick = false;
            SyncSelection();
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

    private static MessageRowViewModel? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as MessageRowViewModel;

    // ---- M2 actions: the selectable avatar, Ctrl and Shift clicks, the hover strip (real pointer input is a hand test; the VM state is driven by hover-sim and select in automation) ----

    // Ctrl-click ticks one row, Shift-click ticks the range (email-list.tsx:1221); the modifiers come from the pointer event itself, no key state is read
    private void OnRowsPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var modifiers = e.KeyModifiers;
        var ctrl = modifiers.HasFlag(global::Windows.System.VirtualKeyModifiers.Control);
        var shift = modifiers.HasFlag(global::Windows.System.VirtualKeyModifiers.Shift);
        if ((!ctrl && !shift) || RowOf(e.OriginalSource) is not { } row)
        {
            return;
        }

        _modifierClick = true;
        if (shift)
        {
            List.ExtendTo(row);
        }
        else
        {
            List.ToggleCheck(row);
        }
    }

    private void OnAvatarTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (RowOf(sender) is { } row)
        {
            e.Handled = true;
            List.ToggleCheck(row);
        }
    }

    private void OnRowEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (RowOf(sender) is { } row)
        {
            List.SetHover(row.Id);
        }
    }

    private void OnRowExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (RowOf(sender) is { } row && string.Equals(List.HoverId, row.Id, StringComparison.Ordinal))
        {
            List.SetHover(null);
        }
    }

    // the strip also shows while keyboard focus is inside the row (I-136)
    private void OnRowFocused(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row)
        {
            List.SetHover(row.Id);
        }
    }

#pragma warning disable VSTHRD100 // XAML event handlers must return void; a failed action reports itself with a toast
    private async void OnStripClick(object sender, RoutedEventArgs e)
#pragma warning restore VSTHRD100
    {
        if (sender is FrameworkElement { Tag: string id } button && RowOf(button) is { } row
            && ViewHost.Get<ICommandRegistry>().TryGet(id, out var command))
        {
            var context = new CommandContext(CommandSurface.HoverStrip, [List.TargetOf(row)], null, null, null);
            if (command.CanExecute(context))
            {
                await command.ExecuteAsync(context, CancellationToken.None);
            }
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
