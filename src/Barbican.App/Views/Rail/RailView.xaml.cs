using Barbican.App.Motion;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Shell;

namespace Barbican.App.Views.Rail;

/// <summary>The app rail. Only displays <see cref="RailViewModel"/> and forwards a click to <see cref="ShellViewModel.TryActivate"/>.</summary>
public sealed partial class RailView : UserControl
{
    private readonly ShellViewModel _shell = ViewHost.Get<ShellViewModel>();

    public RailView()
    {
        Rail = ViewHost.Get<RailViewModel>();
        Look = ViewHost.Get<StyleState>();
        InitializeComponent();
        Popover.Chosen += (_, _) => AccountButton.Flyout?.Hide();
    }

    public RailViewModel Rail { get; }

    public StyleState Look { get; }

    private void OnMailClick(object sender, RoutedEventArgs e) => _shell.TryActivate(AppId.Mail);

    private void OnCalendarClick(object sender, RoutedEventArgs e) => _shell.TryActivate(AppId.Calendar);

    private void OnContactsClick(object sender, RoutedEventArgs e) => _shell.TryActivate(AppId.Contacts);

    private void OnFilesClick(object sender, RoutedEventArgs e) => _shell.TryActivate(AppId.Files);

    // I-023, I-029: the rail's search and shortcuts buttons run the commands by id; a command nobody registered does nothing (search's arrives in wave 2)
    private void OnSearchClick(object sender, RoutedEventArgs e) => Run(CommandIds.FocusSearch);

    private void OnShortcutsClick(object sender, RoutedEventArgs e) => Run(CommandIds.ShowShortcuts);

    private static void Run(string commandId)
    {
        var registry = ViewHost.Find<ICommandRegistry>();
        if (registry is not null && registry.TryGet(commandId, out var command))
        {
            var context = new CommandContext(CommandSurface.Toolbar, [], null, null, null);
            if (command.CanExecute(context))
            {
                _ = command.ExecuteAsync(context, CancellationToken.None);
            }
        }
    }

    // The gear opens nothing yet: the settings pages are M6 (spec I-028).
    private void OnSettingsClick(object sender, RoutedEventArgs e) => _shell.TryActivate(AppId.Settings);

    private void OnAccountFlyoutOpened(object? sender, object e)
    {
        if (Popover.Parent is FrameworkElement presenter)
        {
            PopIn.Run(presenter, new Windows.Foundation.Point(1, 1));
        }
    }
}
