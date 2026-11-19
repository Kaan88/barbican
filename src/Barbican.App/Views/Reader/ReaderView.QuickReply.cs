using Barbican.App.Shell;
using Barbican.App.Theme;
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
    private CancellationTokenSource? _inlineSend;
    private int _hintVersion;
    private bool _replyExpanded;

    private void InitializeQuickReply()
    {
        Loaded += (_, _) =>
        {
            Reader.PropertyChanged += OnQuickReplyReaderChanged;
            _replyComposer.OpenChanged += OnQuickReplyComposerChanged;
            Look.PropertyChanged += OnQuickReplyStyleChanged;
            ApplyHeaderLayout();
            ApplyQuickReplyStyle();
            SyncQuickReply();
        };
        Unloaded += (_, _) =>
        {
            Reader.PropertyChanged -= OnQuickReplyReaderChanged;
            _replyComposer.OpenChanged -= OnQuickReplyComposerChanged;
            Look.PropertyChanged -= OnQuickReplyStyleChanged;
            _replyHandoff?.Cancel();
            _inlineSend?.Cancel();
            _replyTarget = null;
            _hintVersion++;
        };
    }

    private void OnQuickReplyReaderChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(ReaderViewModel.Current), StringComparison.Ordinal)) SyncQuickReply();
    }

    private void OnQuickReplyComposerChanged(object? sender, EventArgs e)
    {
        if (_replyComposer.IsOpen) _inlineSend?.Cancel();
        SyncQuickReply();
    }

    private void OnQuickReplyStyleChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        ApplyHeaderLayout();
        ApplyQuickReplyStyle();
    }

    // Bulwark 1.21.2 email-viewer.tsx:6652-6684: a ruled footer with 24 px
    // gutters, regular-density 12 px spacing and a two/three-row textarea.
    private void ApplyQuickReplyStyle()
    {
        var bulwark = Look.IsBulwark;
        var inset = Look.ReaderPadding;
        QuickReplyBar.Margin = bulwark ? new Thickness(-inset.Left, 0, -inset.Right, -inset.Bottom) : new Thickness(0);
        QuickReplyBar.BorderThickness = bulwark ? new Thickness(0, 1, 0, 0) : new Thickness(1);
        QuickReplyBar.CornerRadius = bulwark ? new CornerRadius(0) : Look.ReplyRadius;
        QuickReplyBar.Padding = bulwark ? new Thickness(24, 12, 24, 12) : new Thickness(8, 4, 8, 4);
        QuickReplyBar.Background = AppResources.Brush(bulwark ? "Ground" : "Tile");
        QuickReplyLayout.Spacing = bulwark ? 12 : 6;
        if (bulwark)
        {
            QuickReplyText.BorderBrush = AppResources.Brush("Line");
            QuickReplyText.Background = AppResources.Brush("Ground");
            QuickReplyText.Foreground = AppResources.Brush("Text");
            QuickReplyText.Padding = new Thickness(12, 8, 12, 8);
            QuickReplyText.CornerRadius = new CornerRadius(8);
        }
        else
        {
            QuickReplyText.ClearValue(BorderBrushProperty);
            QuickReplyText.Background = AppResources.Brush("Tile");
            QuickReplyText.ClearValue(ForegroundProperty);
            QuickReplyText.ClearValue(PaddingProperty);
            QuickReplyText.CornerRadius = new CornerRadius(18);
        }
        UpdateQuickReplyActions();
    }

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
            _inlineSend?.Cancel();
            _replyTarget = target;
            _replyHandoff?.Cancel();
            ClearQuickReply();
            QuickReplyText.PlaceholderText = Fmt.ReplyHint(string.Empty);
            var version = ++_hintVersion;
            if (target is { } recipientTarget && Reader.Current.Body is { } body)
            {
                _ = UpdateReplyHintAsync(recipientTarget.Login, body, version);
            }
        }

        QuickReplyBar.Visibility = target is not null && !_replyComposer.IsOpen
            && ComposeSettings.Read(_replyPrefs.Current).QuickReply ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task UpdateReplyHintAsync(string login, EmailBody body, int version)
    {
        try
        {
            var identities = await ViewHost.Get<IIdentityService>().ListAsync(login, CancellationToken.None);
            if (version != _hintVersion || !identities.IsOk) return;

            var username = ViewHost.Find<Barbican.Core.Accounts.IAccountManager>()?.SessionFor(login)?.Client.Session.Username;
            if (string.Equals(login, _replyMail.AccountKey, StringComparison.Ordinal)) username ??= _replyMail.AccountUser;
            var own = identities.Value!.Select(i => i.Identity.Email).ToList();
            if (!string.IsNullOrEmpty(username)) own.Add(username);
            var settings = ComposeSettings.Read(_replyPrefs.Current);
            var (to, _) = ReplyRecipients.Build(body, false, own, settings.SubAddressDelimiter);
            QuickReplyText.PlaceholderText = Fmt.ReplyHint(string.Join(", ", to.Select(a => string.IsNullOrEmpty(a.Name) ? a.Email : a.Name)));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or Barbican.Core.Common.PortPendingException)
        {
            // The neutral prompt stays available if identities cannot be loaded.
        }
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
        if (QuickReplyActions is null || QuickReplySend is null) return;
        QuickReplySend.IsEnabled = _inlineSend is null && !string.IsNullOrWhiteSpace(QuickReplyText.Text);
        QuickReplyText.IsReadOnly = _inlineSend is not null;
        var expanded = _replyExpanded || QuickReplyText.Text.Length != 0;
        QuickReplyText.MinHeight = expanded ? (Look.IsBulwark ? 78 : 76) : Look.IsBulwark ? 58 : Math.Max(40, Look.MinimumTarget);
        QuickReplyActions.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnQuickReplySend(object sender, RoutedEventArgs e) => _ = SendQuickReplyAsync();

    private void OnQuickReplySendKey(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs e)
    {
        e.Handled = true;
        _ = SendQuickReplyAsync();
    }

    private async Task SendQuickReplyAsync()
    {
        if (_inlineSend is not null || _replyHandoff is not null || _replyComposer.IsOpen ||
            QuickReplyBar.Visibility != Visibility.Visible || _replyTarget is not { } target || ReplyTarget() != target) return;
        var text = QuickReplyText.Text;
        if (string.IsNullOrWhiteSpace(text)) return;
        using var sending = new CancellationTokenSource();
        _inlineSend = sending;
        UpdateQuickReplyActions();
        try
        {
            var request = new ComposeRequest(target.Login, ComposeKind.Reply) { InlineReply = true, DraftText = text, SourceEmailId = target.ServerId };
            if (await _replyComposer.SendInlineReplyAsync(request, AskInlineAsync, sending.Token) && ReplyTarget() == target && string.Equals(QuickReplyText.Text, text, StringComparison.Ordinal))
            {
                DetailsButton.Focus(FocusState.Programmatic);
                ClearQuickReply();
            }
        }
        catch (OperationCanceledException) when (sending.IsCancellationRequested) { }
        catch (Exception)
        {
            _ = ViewHost.Get<IToastService>().Show(new ToastRequest("Could not send the quick reply.", IsError: true));
        }
        finally
        {
            _inlineSend = null;
            UpdateQuickReplyActions();
        }
    }

    private static async Task<string?> AskInlineAsync(ComposerDialog dialog, CancellationToken ct)
    {
        var choice = dialog.Choices[0];
        var description = string.Join(Environment.NewLine, new[] { dialog.Message }.Concat(dialog.Lines));
        var request = new Barbican.Presentation.Dialogs.ConfirmDialogRequest(dialog.Title, description, choice.Label);
        return await ViewHost.Get<Barbican.Presentation.Dialogs.IDialogService>().ShowAsync(request, ct) ? choice.Id : null;
    }

    private void OnQuickReplyCancel(object sender, RoutedEventArgs e)
    {
        if (_inlineSend is not null) return;
        _replyHandoff?.Cancel();
        DetailsButton.Focus(FocusState.Programmatic);
        ClearQuickReply();
    }

    private void OnQuickReplyOpenEditor(object sender, RoutedEventArgs e) => _ = OpenQuickReplyEditorAsync();

    private async Task OpenQuickReplyEditorAsync()
    {
        if (_inlineSend is not null || _replyHandoff is not null || QuickReplyBar.Visibility != Visibility.Visible ||
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
