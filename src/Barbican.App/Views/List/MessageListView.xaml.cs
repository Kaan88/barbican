using System.Collections.Specialized;
using Barbican.App.Motion;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Shell;
using Barbican.Presentation.Menus;
using Barbican.Core.Mail.Model;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.List;

// Mirrors: Bulwark components/email/email-list.tsx:1602-1650 (loading overlay at half opacity with a pill, skeleton on the first load, the empty state), ui/empty-state.tsx:29-45,
// thread-list-item.tsx:733-746,790-806 (hover and selected row, avatar check), mail/mail-app.tsx:6627-6640 (select toggle, active while a selection exists); FM-12: the folder-change rise
// is motion Bulwark does not have (MotionRuntime.DecorationMs); mail/mail-app.tsx:6733-6737 and email/search-result-count.tsx:14-31 (the search results line, SR-04).

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
    private string? _scrolledId;
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
        SkeletonRows.ItemsSource = Enumerable.Range(0, 8).ToArray();
        Panes = ViewHost.Get<PanesViewModel>();
        ApplyStyle();
        Look.PropertyChanged += (_, _) =>
        {
            ApplyStyle();
            UpdateEmpty();
        };
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
    private double? _compactWidth;

    internal void SetCompactWidth(double? width)
    {
        if (_compactWidth == width) return;
        _compactWidth = width;
        ApplyStyle();
    }

    private void ApplyStyle()
    {
        var width = _compactWidth ?? (Panes.ListWidth > 0 ? Panes.ListWidth : Look.ListWidth);
        if (Math.Abs(Pane.Width - width) > 0.01 || double.IsNaN(Pane.Width))
        {
            Pane.Width = width;
        }

        var style = Look.MessageItemStyle;
        if (!ReferenceEquals(Rows.ItemContainerStyle, style))
        {
            Rows.ItemContainerStyle = style;
        }

        ListHeader.Spacing = Look.IsBulwark ? 0 : 12;
        Toolbar.ColumnSpacing = Look.IsBulwark ? 4 : 0;
    }

    // The default Bulwark toolbar clears any existing selection on the next click.
    private void OnToolbarSelectAll(object sender, RoutedEventArgs e)
    {
        if (List.HasChecked) List.ClearChecked();
        else List.SelectAllLoaded();
        // the toggle flipped itself on the click; it shows the selection, which an empty list did not make
        SelectAllToggle.IsChecked = List.HasChecked;
    }

    private void OnFolderActions(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement button) return;
        var sidebar = ViewHost.Get<SidebarViewModel>();
        var id = List.MailboxId;
        var key = ViewHost.Get<IActiveMail>().AccountKey ?? string.Empty;
        var role = sidebar.Folders.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.Ordinal))?.Role ?? MailboxRole.None;
        var mailbox = id is null ? null : new MailboxRef(key, key, id, role == MailboxRole.None ? null : role.ToString().ToLowerInvariant());
        var at = button.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, button.ActualHeight));
        ViewHost.Get<IContextMenuService>().Show(
            new MenuContext(MenuIds.ListActions, [], mailbox, null, null, null, CommandSurface.ContextMenu),
            new MenuAnchor(at.X, at.Y, null));
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
            case nameof(MessageListViewModel.SearchText):
            case nameof(MessageListViewModel.IsLoading):
            case nameof(MessageListViewModel.IsConnecting):
            case nameof(MessageListViewModel.CanLoadMore):
            case nameof(MessageListViewModel.SearchCountText):
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
            Rise.Run(Rows, _rowsShift, scale.Rapid ? 0 : MotionRuntime.DecorationMs(scale.EnterMs), fromOpacity: 0.5, fromY: 10);
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
        var empty = List.Rows.Count == 0 && !List.IsLoading;
        var bulwark = Look.IsBulwark;
        var searching = !string.IsNullOrWhiteSpace(List.SearchText);
        // Bulwark: the empty-state component (no_emails / no_search_results with their one-line descriptions, locales/en/common.json:362-365); the prototypes keep their plain line
        EmptyText.Text = List.IsConnecting ? "Connecting…" : bulwark ? (searching ? "No results found" : "No messages found") : "No messages";
        EmptyHint.Text = List.IsConnecting ? string.Empty : searching ? "Try adjusting your search or filters" : "This mailbox is empty";
        EmptyHint.Visibility = bulwark && !List.IsConnecting ? Visibility.Visible : Visibility.Collapsed;
        EmptyBadge.Visibility = bulwark ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.FontSize = bulwark ? 16 : 13;
        EmptyText.FontWeight = bulwark ? Microsoft.UI.Text.FontWeights.Medium : Microsoft.UI.Text.FontWeights.Normal;
        EmptyText.Foreground = (Brush)Application.Current.Resources[bulwark ? "Text" : "Faint"];
        EmptyState.VerticalAlignment = bulwark ? VerticalAlignment.Center : VerticalAlignment.Top;
        EmptyState.Margin = bulwark ? default : new Thickness(0, 44, 0, 0);
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        // the results line (SR-04, mail-app.tsx:6733): only while a search shows and nothing loads
        var count = bulwark ? List.SearchCountText : null;
        CountText.Text = count ?? string.Empty;
        CountLine.Visibility = count is null ? Visibility.Collapsed : Visibility.Visible;
        // a first page that is on its way: placeholder rows when nothing is shown yet, the old rows at half strength with a pill otherwise (Bulwark only)
        var loading = bulwark && List.IsLoading && !List.IsConnecting;
        LoadingSkeleton.Visibility = loading && List.Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LoadingOverlay.Visibility = loading && List.Rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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

        // R2-12: scroll only when the selected id changed; a row that was replaced by a push delta (same id, new object) keeps the scroll position
        if (row is not null && !string.Equals(_scrolledId, row.Id, StringComparison.Ordinal))
        {
            Rows.ScrollIntoView(row);
        }

        _scrolledId = row?.Id;
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

    // the hover hint of the selectable avatar (selectable-avatar.tsx:50-58): the check shows while the pointer is on the avatar; Bulwark only, one frame
    private void OnAvatarEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => SetAvatarHint(sender, Look.IsBulwark);

    private void OnAvatarExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => SetAvatarHint(sender, false);

    private static void SetAvatarHint(object sender, bool on)
    {
        if (sender is Border { Child: Grid grid })
        {
            foreach (var child in grid.Children)
            {
                if (child is Border { Tag: "hover-check" } hint)
                {
                    hint.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                }
            }
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

    /// <summary>The focus rescue's landing (AX-03, focus-rescue.tsx): the open row (the one that took the deleted row's place), else the list itself.</summary>
    public void ReturnFocus()
    {
        if (Rows.SelectedItem is { } row && Rows.ContainerFromItem(row) is Control container && container.Focus(FocusState.Programmatic))
        {
            return;
        }

        Rows.Focus(FocusState.Programmatic);
    }

    // the date header's toggle folds or opens its group (ML-05, email-list.tsx:1315)
    private void OnGroupToggle(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key }) List.ToggleGroup(key);
    }

#pragma warning disable VSTHRD100 // XAML event handlers must return void; a failed action reports itself with a toast
    private void OnCopyVerificationCode(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MessageRowViewModel row } && List.CopyVerificationCode(row))
        {
            // verification-code-chip.tsx:35-43: a success toast after the copy
            _ = ViewHost.Find<Barbican.Presentation.Actions.IToastService>()?.Show(new Barbican.Presentation.Actions.ToastRequest("Code copied"));
        }
    }

    private async void OnStripClick(object sender, RoutedEventArgs e)
#pragma warning restore VSTHRD100
    {
        if (sender is FrameworkElement { DataContext: HoverButton { OpensTagPicker: true } } tagButton && Menus.VisualWalk.Up<MessageRowViewModel>(tagButton)?.Context is { } tagRow)
        {
            // the tag button opens the picker (the Tag flyout of the row menu) under the button, as Bulwark's hover Tag does (email-hover-actions.tsx:224-228)
            var at = tagButton.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, tagButton.ActualHeight));
            var target = List.TargetOf(tagRow);
            ViewHost.Get<Barbican.Presentation.Menus.IContextMenuService>().Show(
                new Barbican.Presentation.Menus.MenuContext(Barbican.Presentation.Menus.MenuIds.MailTag, [target], null, null, null, null, CommandSurface.HoverStrip),
                new Barbican.Presentation.Menus.MenuAnchor(at.X, at.Y, null));
            return;
        }

        if (sender is FrameworkElement { Tag: string id } button && Menus.VisualWalk.Up<MessageRowViewModel>(button)?.Context is { } row
            && ViewHost.Get<ICommandRegistry>().TryGet(id, out var command))
        {
            var context = new CommandContext(CommandSurface.HoverStrip, [List.TargetOf(row)], null, null, null);
            if (command.CanExecute(context))
            {
                // a thrown exception becomes an error toast and a crash-log line (Fable review 3 R3-05); a failed result toasts itself
                await (ViewHost.Find<CommandRunner>() ?? new CommandRunner(ViewHost.Find<Barbican.Presentation.Actions.IToastService>())).RunAsync(command, context);
            }
        }
    }

    // ---- M2 hotfix: real drag and drop (HAND TEST 2 "can't drag"). The platform's in-app drag; DropController keeps what is dragged and runs the drop
    // (the same code dnd-sim runs). The dragged row stands for the ticked rows when it is one of them (Rust dnd.rs ids_for_mail :47). ----

    private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.OfType<MessageRowViewModel>().FirstOrDefault() is not { } row)
        {
            e.Cancel = true;
            return;
        }

        var drops = ViewHost.Get<Barbican.Presentation.DragDrop.DropController>();
        var current = List.Current;
        IReadOnlyList<Barbican.Core.Mail.Model.MailTarget> targets = current.Any(t => string.Equals(t.EmailId, row.Id, StringComparison.Ordinal)) ? current : [List.TargetOf(row)];
        drops.BeginMailDrag(targets);
        e.Data.SetText(drops.Label);
        // a move onto a folder, a link onto a tag: the targets may accept only what the source offers (M2 hotfix 2: tags answered Link to a Move-only drag)
        e.Data.RequestedOperation = (global::Windows.ApplicationModel.DataTransfer.DataPackageOperation)(int)Barbican.Presentation.DragDrop.DropAcceptance.SourceOffers;
        Barbican.App.Views.Actions.DragBadge.Prepare(drops.Label);
    }

    // after a drop the controller has nothing pending any more; a drag that ended anywhere else fires onDragEnd(dropped: false)
#pragma warning disable CA1822 // XAML event handler
    private void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        Barbican.App.Views.Actions.DropHighlight.Clear();
        ViewHost.Get<Barbican.Presentation.DragDrop.DropController>().CancelDrag();
    }
#pragma warning restore CA1822

    // A date-group header is not selectable.
#pragma warning disable CA1822 // XAML event handler
    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        args.ItemContainer.IsEnabled = args.Item is not GroupHeaderViewModel;
        if (args.Item is MessageRowViewModel row)
        {
            AutomationProperties.SetAutomationId(args.ItemContainer, row.Id);
            AutomationProperties.SetName(args.ItemContainer, row.AccessibleName);
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
