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

public sealed partial class ConversationCardView : UserControl
{
    private readonly StackPanel _layout = new() { Spacing = 4 };
    private ConversationExpansion? _card;
    private BodyShell? _shell;

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
        view.Height = 96;
        AutomationProperties.SetAutomationId(view, "reader.thread.body." + card.EmailId);
        _layout.Children.Add(view);
        shell.Show(new BodyRequest(card.EmailId, BodyRequest.ContentOf(body), true, () => Owned(card)));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var target = MailTarget.OfOpen(card.AccountKey, body);
        if (target.AccountId.Length == 0 && !body.Keywords.IsSet("$draft"))
            actions.Children.Add(Action("Reply", "reader.thread.reply.", card, () => Reply(target)));
        actions.Children.Add(Action("Print", "reader.thread.print.", card, () => _ = ViewHost.Get<IDialogService>().ShowAsync(new ConversationPrintRequest(card), CancellationToken.None)));
        _layout.Children.Add(actions);
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

    private static void Reply(MailTarget target)
    {
        var registry = ViewHost.Get<ICommandRegistry>();
        // The composer reads directly from the login's mail service and takes its server id.
        var source = target with { AccountKey = target.Login, EmailId = target.ServerId };
        var context = new CommandContext(CommandSurface.Toolbar, [source], null, null, target.Login);
        if (registry.TryGet(CommandIds.Reply, out var command) && command.CanExecute(context))
            ViewHost.Get<CommandRunner>().Start(command, context);
    }
}
