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

    // Reply, Reply all and Forward run the compose commands on the message on screen (M3 composer-ui; the commands are registered by the composer)
    private void OnReplyClick(object sender, RoutedEventArgs e) => RunOnOpen(Barbican.Presentation.Commands.CommandIds.Reply);

    private void OnReplyAllClick(object sender, RoutedEventArgs e) => RunOnOpen(Barbican.Presentation.Commands.CommandIds.ReplyAll);

    private void OnForwardClick(object sender, RoutedEventArgs e) => RunOnOpen(Barbican.Presentation.Commands.CommandIds.Forward);

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

    // An attachment card: a click or a right-click opens MENU-ATT at the pointer (spec I-226).
    private void OnAttachmentTapped(object sender, TappedRoutedEventArgs e) => e.Handled = OpenAttachmentMenu(sender, e.GetPosition(XamlRoot?.Content));

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
