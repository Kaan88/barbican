// Mirrors: Bulwark components/email/selection-view.tsx (the bar's buttons run the same commands as keys and menus); spec I-138..I-142. Owner actions.

using Barbican.App.Shell;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Mail;

namespace Barbican.App.Views.Actions;

/// <summary>The selection bar in the list's slot.</summary>
public sealed partial class SelectionBarView : UserControl
{
    private readonly ICommandRegistry _commands;

    public SelectionBarView()
    {
        List = ViewHost.Get<MessageListViewModel>();
        _commands = ViewHost.Get<ICommandRegistry>();
        InitializeComponent();
    }

    public MessageListViewModel List { get; }

    private void OnToggleAll(object sender, RoutedEventArgs e) => List.ToggleSelectAll();

#pragma warning disable VSTHRD100 // XAML event handlers must return void; a failed action reports itself with a toast
    private async void OnCommand(object sender, RoutedEventArgs e)
#pragma warning restore VSTHRD100
    {
        if (sender is FrameworkElement { Tag: string id } && _commands.TryGet(id, out var command))
        {
            var context = new CommandContext(CommandSurface.SelectionBar, [], null, null, null);
            if (command.CanExecute(context))
            {
                await command.ExecuteAsync(context, CancellationToken.None);
            }
        }
    }
}
