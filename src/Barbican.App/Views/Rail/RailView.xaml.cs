using Barbican.App.Motion;
using Barbican.App.Shell;
using Barbican.App.Theme;
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
    }

    public RailViewModel Rail { get; }

    public StyleState Look { get; }

    private void OnMailClick(object sender, RoutedEventArgs e) => _shell.TryActivate(AppId.Mail);

    private void OnCalendarClick(object sender, RoutedEventArgs e) => _shell.TryActivate(AppId.Calendar);

    private void OnContactsClick(object sender, RoutedEventArgs e) => _shell.TryActivate(AppId.Contacts);

    private void OnFilesClick(object sender, RoutedEventArgs e) => _shell.TryActivate(AppId.Files);

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
