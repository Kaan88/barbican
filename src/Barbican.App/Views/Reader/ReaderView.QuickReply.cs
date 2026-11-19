using Barbican.App.Shell;
using Barbican.Core.Compose;
using Barbican.Core.Mail.Model;
using Barbican.Core.Storage;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Mail;

namespace Barbican.App.Views.Reader;

public sealed partial class ReaderView
{
    private readonly IComposerService _replyComposer = ViewHost.Get<IComposerService>();
    private readonly IActiveMail _replyMail = ViewHost.Get<IActiveMail>();
    private readonly IPrefsStore _replyPrefs = ViewHost.Get<IPrefsStore>();
    private (string ListAccount, string Login, string ServerId)? _replyTarget;
    private CancellationTokenSource? _replyHandoff;
    private bool _replyExpanded;

    private void InitializeQuickReply()
    {
        Loaded += (_, _) =>
        {
            Reader.PropertyChanged += OnQuickReplyReaderChanged;
            _replyComposer.OpenChanged += OnQuickReplyComposerChanged;
            SyncQuickReply();
        };
        Unloaded += (_, _) =>
        {
            Reader.PropertyChanged -= OnQuickReplyReaderChanged;
            _replyComposer.OpenChanged -= OnQuickReplyComposerChanged;
            _replyHandoff?.Cancel();
        };
    }

    private void OnQuickReplyReaderChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(ReaderViewModel.Current), StringComparison.Ordinal)) SyncQuickReply();
    }

    private void OnQuickReplyComposerChanged(object? sender, EventArgs e) => SyncQuickReply();

    private (string ListAccount, string Login, string ServerId)? ReplyTarget()
    {
        var snapshot = Reader.Current;
        if (snapshot is not { ListAccountKey: { } account, Body: { } body, BodyState: BodyState.Ready or BodyState.Truncated }
            || !string.Equals(account, _replyMail.AccountKey, StringComparison.Ordinal)
            || body.Keywords.IsSet("$draft")
            || snapshot.MailboxRoles.Any(role => role is "drafts" or "junk" or "spam" or "scheduled"))
        {
            return null;
        }

        var target = MailTarget.OfOpen(account, body);
        return target.AccountId.Length == 0 && target.Login.Length > 0 && target.ServerId.Length > 0
            ? (account, target.Login, target.ServerId) : null;
    }

    private void SyncQuickReply()
    {
        var target = ReplyTarget();
        if (target != _replyTarget)
        {
            _replyTarget = target;
            _replyHandoff?.Cancel();
            ClearQuickReply();
        }

        QuickReplyBar.Visibility = target is not null && !_replyComposer.IsOpen
            && ComposeSettings.Read(_replyPrefs.Current).QuickReply ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearQuickReply()
    {
        _replyExpanded = false;
        QuickReplyText.Text = string.Empty;
        UpdateQuickReplyActions();
    }

    private void OnQuickReplyFocus(object sender, RoutedEventArgs e)
    {
        _replyExpanded = true;
        UpdateQuickReplyActions();
    }

    private void OnQuickReplyBlur(object sender, RoutedEventArgs e)
    {
        // Moving from the textbox to either action must leave those actions reachable.
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (XamlRoot is { } root && Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root) is DependencyObject focused)
            {
                for (DependencyObject? element = focused; element is not null; element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element))
                {
                    if (ReferenceEquals(element, QuickReplyBar)) return;
                }
            }

            _replyExpanded = false;
            UpdateQuickReplyActions();
        });
    }

    private void OnQuickReplyTextChanged(object sender, TextChangedEventArgs e) => UpdateQuickReplyActions();

    private void UpdateQuickReplyActions()
    {
        if (QuickReplyActions is null) return;
        var expanded = _replyExpanded || QuickReplyText.Text.Length != 0;
        QuickReplyText.MinHeight = expanded ? 76 : 56;
        QuickReplyActions.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnQuickReplyCancel(object sender, RoutedEventArgs e)
    {
        _replyHandoff?.Cancel();
        DetailsButton.Focus(FocusState.Programmatic);
        ClearQuickReply();
    }

    private void OnQuickReplyOpenEditor(object sender, RoutedEventArgs e) => _ = OpenQuickReplyEditorAsync();

    private async Task OpenQuickReplyEditorAsync()
    {
        if (_replyHandoff is not null || QuickReplyBar.Visibility != Visibility.Visible ||
            _replyTarget is not { } target || ReplyTarget() != target)
        {
            return;
        }

        var text = QuickReplyText.Text;
        var previous = _replyComposer.Current;
        using var handoff = new CancellationTokenSource();
        _replyHandoff = handoff;
        try
        {
            await _replyComposer.OpenAsync(new ComposeRequest(target.Login, ComposeKind.Reply)
            {
                SourceEmailId = target.ServerId,
                DraftText = text,
            }, handoff.Token);
            if (!handoff.IsCancellationRequested && ReplyTarget() == target &&
                string.Equals(QuickReplyText.Text, text, StringComparison.Ordinal) &&
                _replyComposer.Current is { Kind: ComposeKind.Reply } opened && !ReferenceEquals(previous, opened) &&
                string.Equals(opened.AccountKey, target.Login, StringComparison.Ordinal) &&
                string.Equals(opened.SourceEmailId, target.ServerId, StringComparison.Ordinal))
            {
                ClearQuickReply();
            }
        }
        catch (OperationCanceledException) when (handoff.IsCancellationRequested)
        {
            // The reader moved to a different message before the editor was prepared.
        }
        catch (Exception)
        {
            _ = ViewHost.Get<IToastService>().Show(new ToastRequest("Could not open the reply editor.", IsError: true));
        }
        finally
        {
            _replyHandoff = null;
        }
    }
}
