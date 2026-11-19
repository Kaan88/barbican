using Barbican.App.Motion;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Menus;
using Microsoft.UI.Xaml.Input;

namespace Barbican.App.Views.Reader;

/// <summary>The reader chrome. The data is <see cref="ReaderViewModel"/>; the details panel is the only state of its own (it folds again when another message opens).</summary>
public sealed partial class ReaderView : UserControl
{
    private readonly MessageListViewModel _list = ViewHost.Get<MessageListViewModel>();
    private string _detailsFor = string.Empty;

    public ReaderView()
    {
        Reader = ViewHost.Get<ReaderViewModel>();
        Look = ViewHost.Get<StyleState>();
        InitializeComponent();
        InitializeQuickReply();
        Reader.PropertyChanged += (_, e) =>
        {
            if (!string.Equals(e.PropertyName, nameof(ReaderViewModel.EmailId), StringComparison.Ordinal))
            {
                return;
            }

            if (!string.Equals(_detailsFor, Reader.EmailId, StringComparison.Ordinal))
            {
                SetDetails(false);
            }

            RiseHeader();
        };
    }

    public ReaderViewModel Reader { get; }

    public StyleState Look { get; }

    public Barbican.Presentation.Shell.RailViewModel Account { get; } = ViewHost.Get<Barbican.Presentation.Shell.RailViewModel>();

    public event EventHandler? BackRequested;

    public event EventHandler? FullscreenRequested;

    public void SetCompactNavigation(bool showBack, bool phone)
    {
        ReaderBack.Visibility = showBack ? Visibility.Visible : Visibility.Collapsed;
        PhoneActions.Visibility = phone ? Visibility.Visible : Visibility.Collapsed;
        ActionReply.Visibility = ActionReplyAll.Visibility = ActionForward.Visibility = phone ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void OnPreviousClick(object sender, RoutedEventArgs e) => _list.MoveSelection(-1, OpenReason.Click);

    private void OnNextClick(object sender, RoutedEventArgs e) => _list.MoveSelection(1, OpenReason.Click);

    public void SetFullscreen(bool fullscreen)
    {
        var label = fullscreen ? "Exit fullscreen" : "Fullscreen";
        ReaderMaximizeGlyph.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
        ReaderMinimizeGlyph.Visibility = fullscreen ? Visibility.Visible : Visibility.Collapsed;
        Barbican.App.Motion.Tip.SetText(ReaderFullscreenButton, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ReaderFullscreenButton, label);
    }

    private void OnFullscreenClick(object sender, RoutedEventArgs e) => FullscreenRequested?.Invoke(this, EventArgs.Empty);

    private void OnContentSizeChanged(object sender, SizeChangedEventArgs e) =>
        ConversationScroll.MaxHeight = Math.Clamp(e.NewSize.Height * 0.40, 150, 360);

    private void OnShowOlderClick(object sender, RoutedEventArgs e) => Reader.ShowOlderConversationHeaders();

    private void OnConversationHeaderClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            ViewHost.Get<Barbican.Presentation.Mail.Loading.IConversationOpener>().OpenConversationMessage(id);
        }
    }

    // Reply, Reply all and Forward run the compose commands on the message on screen (M3 composer-ui; the commands are registered by the composer)
    private void OnReplyClick(object sender, RoutedEventArgs e) => RunOnOpen(Barbican.Presentation.Commands.CommandIds.Reply);

    private void OnReplyAllClick(object sender, RoutedEventArgs e) => RunOnOpen(Barbican.Presentation.Commands.CommandIds.ReplyAll);

    private void OnForwardClick(object sender, RoutedEventArgs e) => RunOnOpen(Barbican.Presentation.Commands.CommandIds.Forward);
    private void OnArchiveClick(object sender, RoutedEventArgs e) => RunOnOpen(Barbican.Presentation.Commands.CommandIds.Archive);

    private void OnDeleteClick(object sender, RoutedEventArgs e) => RunOnOpen(Barbican.Presentation.Commands.CommandIds.Delete);

    private void RunOnOpen(string commandId)
    {
        var active = ViewHost.Get<Barbican.Presentation.Mail.IActiveMail>();
        if (Reader.Current.Body is not { } body || active.AccountKey is not { } key || !ViewHost.Get<Barbican.Presentation.Commands.ICommandRegistry>().TryGet(commandId, out var command))
        {
            return;
        }

        var context = new Barbican.Presentation.Commands.CommandContext(Barbican.Presentation.Commands.CommandSurface.Toolbar, [Barbican.Core.Mail.Model.MailTarget.OfOpen(key, body)], null, null, key);
        if (command.CanExecute(context))
        {
            ViewHost.Get<Barbican.Presentation.Commands.CommandRunner>().Start(command, context);
        }
    }

    // UI-SWEEP-1 R-05: in a narrow pane the verdict pill drops under the sender line instead of squeezing the name to nothing
    private void OnSenderLineSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < 440;
        Grid.SetRow(VerdictPill, narrow ? 1 : 0);
        Grid.SetColumn(VerdictPill, narrow ? 1 : 2);
        Grid.SetColumnSpan(VerdictPill, narrow ? 3 : 1);
        VerdictPill.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        VerdictPill.Margin = narrow ? new Thickness(0, 6, 0, 0) : default;
    }

    private void OnDetailsClick(object sender, RoutedEventArgs e) => SetDetails(DetailsPanel.Visibility != Visibility.Visible);

    private void SetDetails(bool open)
    {
        _detailsFor = Reader.EmailId;
        DetailsPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        DetailsLabel.Text = open ? "Hide details" : "Details";
        DetailsChevron.Kind = open ? "ChevronDown" : "ChevronRight";
    }

    // The prototype's header transition (a short rise of the new subject and sender line), on the view token. The new text is already set, so the
    // header never blanks; while keys repeat the selection moves faster than any animation and it snaps (design/MOTION.md rule 4).
    private void RiseHeader()
    {
        var scale = MotionRuntime.Scale;
        var ms = scale.Rapid || string.IsNullOrEmpty(Reader.EmailId) ? 0 : scale.ViewMs;
        Rise.Run(SubjectText, SubjectShift, ms, fromOpacity: 0.55, fromY: 6);
        Rise.Run(SenderLine, SenderShift, ms, fromOpacity: 0.55, fromY: 6);
    }

    // "Try again" opens the selected message once more (spec I-230).
    private void OnRetryClick(object sender, RoutedEventArgs e) => _list.OpenSelected(OpenReason.Click);

    // More (the ⋮ button): MENU-READER under the button (spec I-200).
    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement button && XamlRoot?.Content is UIElement root)
        {
            var p = button.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, button.ActualHeight + 4));
            _ = ViewHost.Get<ReaderMenuOpener>().Open(MenuIds.Reader, null, new MenuAnchor(p.X, p.Y, null));
        }
    }

    // The sender's name: a click opens the name popover with the contact card (I-204, M5 con-ui)
    private void OnSenderTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement anchor)
        {
            Contacts.ContactsMailSurfaces.ShowPopover(SenderFlyout, SenderCard, anchor, Reader.Header.Email ?? string.Empty);
            e.Handled = true;
        }
    }

    // Activating opens or saves by preference; right-click still opens MENU-ATT.
    private void OnAttachmentTapped(object sender, TappedRoutedEventArgs e) =>
        e.Handled = sender is FrameworkElement { DataContext: AttachmentCard card } && ViewHost.Get<ReaderMenuOpener>().ActivateAttachment(card.Index);

    private void OnAttachmentRightTapped(object sender, RightTappedRoutedEventArgs e) => e.Handled = OpenAttachmentMenu(sender, e.GetPosition(XamlRoot?.Content));

    private static bool OpenAttachmentMenu(object sender, Windows.Foundation.Point at) =>
        sender is FrameworkElement { DataContext: AttachmentCard card } && ViewHost.Get<ReaderMenuOpener>().Open(MenuIds.Attachment, card.Index, new MenuAnchor(at.X, at.Y, null));

    // A header tag's ×: takes the tag off the open message (Bulwark email-viewer.tsx:5431 onSetTag). The row and the header follow from the list
    // (ReaderRowFollower); a failure reports itself with a toast (MailActionService), so the task is not awaited here.
    private void OnTagRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tagId }
            || _list.Messages.FirstOrDefault(r => string.Equals(r.Id, Reader.EmailId, StringComparison.Ordinal)) is not { } row)
        {
            return;
        }

        _ = ViewHost.Get<Barbican.Presentation.Actions.MailActionService>().SetTagAsync([_list.TargetOf(row)], tagId, on: false, CancellationToken.None);
    }
}
