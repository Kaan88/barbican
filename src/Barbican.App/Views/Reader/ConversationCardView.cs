// Bulwark components/email/email-viewer.tsx:1035-1145. One independent sandboxed body per visible expansion.
using System.ComponentModel;
using Barbican.App.MailBody;
using Barbican.App.Shell;
using Barbican.Core.Mail.Model;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Input;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Mail.Loading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Reader;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "WinUI owns the control. Unload and data-context replacement both dispose its per-render cancellation source.")]
public sealed partial class ConversationCardView : UserControl
{
    private readonly StackPanel _layout = new() { Spacing = 4 };
    private ConversationExpansion? _card;
    private BodyShell? _shell;
    private CancellationTokenSource? _visible;

    public ConversationCardView()
    {
        Content = _layout;
        DataContextChanged += (_, _) => Bind();
        Loaded += (_, _) => Bind();
        Unloaded += (_, _) => { if (!IsLoaded) Release(); };
    }

    private void Bind()
    {
        var card = DataContext as ConversationExpansion;
        if (!IsLoaded || ReferenceEquals(card, _card)) return;
        Release();
        _card = card;
        if (card is null) return;
        card.PropertyChanged += Changed;
        Draw();
    }

    private void Release()
    {
        if (_card is not null) _card.PropertyChanged -= Changed;
        _card = null;
        _visible?.Cancel();
        _visible?.Dispose();
        _visible = null;
        if (_shell is not null)
        {
            ((IBodyWebViewHost)_shell).Close();
            _shell.Dispose();
            _shell = null;
        }

        _layout.Children.Clear();
    }

    private void Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (string.Equals(args.PropertyName, nameof(ConversationExpansion.Status), StringComparison.Ordinal)) Draw();
    }

    private void Draw()
    {
        if (_card is not { } card || _shell is not null) return;
        _layout.Children.Clear();
        if (card.Body is not { } body)
        {
            _layout.Children.Add(new TextBlock { Text = card.Status, TextWrapping = TextWrapping.Wrap });
            return;
        }

        var shell = ActivatorUtilities.CreateInstance<BodyShell>(ViewHost.Services, new Lazy<IKeyRouter>(ViewHost.Get<IKeyRouter>));
        _shell = shell;
        var view = shell.View;
        view.Height = 48;
        _visible = new CancellationTokenSource();
        var visible = _visible.Token;
        view.SizeChanged += (_, args) =>
        {
            if (Math.Abs(args.NewSize.Width - args.PreviousSize.Width) >= 1 && shell.ShownId is not null)
                _ = FitBodyAsync(shell, view);
        };
        AutomationProperties.SetAutomationId(view, "reader.thread.body." + card.EmailId);
        var to = string.Join(", ", body.To.Select(address => address.Email));
        var cc = string.Join(", ", body.Cc.Select(address => address.Email));
        if (to.Length > 0) _layout.Children.Add(new TextBlock { Text = "To: " + to, TextWrapping = TextWrapping.Wrap });
        if (cc.Length > 0) _layout.Children.Add(new TextBlock { Text = "Cc: " + cc, TextWrapping = TextWrapping.Wrap });
        _layout.Children.Add(view);
        shell.Show(new BodyRequest(card.EmailId, BodyRequest.ContentOf(body), true, () => Owned(card), () =>
        {
            if (visible.IsCancellationRequested) return;
            _ = FitBodyAsync(shell, view);
            _ = ViewHost.Get<MessageLoader>().MarkCardShownAsync(card, visible);
        }));
        var actions = new Barbican.App.Controls.WrapRow { Spacing = 6 };
        if (card.ComposeSource() is { } source)
        {
            actions.Children.Add(Action("Reply", "reader.thread.reply.", card, () => Compose(CommandIds.Reply, source)));
            actions.Children.Add(Action("Reply all", "reader.thread.replyall.", card, () => Compose(CommandIds.ReplyAll, source)));
            actions.Children.Add(Action("Forward", "reader.thread.forward.", card, () => Compose(CommandIds.Forward, source)));
        }

        actions.Children.Add(Action("Print", "reader.thread.print.", card, () => _ = ViewHost.Get<IDialogService>().ShowAsync(new ConversationPrintRequest(card), CancellationToken.None)));
        _layout.Children.Add(actions);
    }

    private async Task FitBodyAsync(BodyShell shell, WebView2 view)
    {
        try
        {
            // The trusted shell owns the iframe. Use DOM prototypes to avoid named elements in a message shadowing a property.
            const string measure = """
                (() => {
                  const frame = document.querySelector('iframe');
                  if (!frame?.contentDocument) return 0;
                  const body = Object.getOwnPropertyDescriptor(Document.prototype, 'body').get.call(frame.contentDocument);
                  if (!body) return 0;
                  const rect = Element.prototype.getBoundingClientRect.call(body);
                  return Math.ceil(rect.height + Math.max(0, rect.top) * 2 + 2);
                })()
                """;
            var raw = await shell.EvalAsync(measure);
            if (ReferenceEquals(_shell, shell) && double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var height) && double.IsFinite(height) && height > 0)
                view.Height = Math.Clamp(height, 48, 1200);
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // A collapsed or recycled card has already closed its WebView.
        }
    }

    private static bool Owned(ConversationExpansion card) =>
        ViewHost.Get<ReaderViewModel>().Owns(card) && string.Equals(ViewHost.Get<IActiveMail>().AccountKey, card.AccountKey, StringComparison.Ordinal);

    private static Button Action(string label, string prefix, ConversationExpansion card, Action run)
    {
        var button = new Button { Content = label };
        AutomationProperties.SetName(button, label);
        AutomationProperties.SetAutomationId(button, prefix + card.EmailId);
        button.Click += (_, _) => { if (Owned(card)) run(); };
        return button;
    }

    private static void Compose(string commandId, MailTarget source)
    {
        var registry = ViewHost.Get<ICommandRegistry>();
        var context = new CommandContext(CommandSurface.Toolbar, [source], null, null, source.AccountKey);
        if (registry.TryGet(commandId, out var command) && command.CanExecute(context))
            ViewHost.Get<CommandRunner>().Start(command, context);
    }
}
