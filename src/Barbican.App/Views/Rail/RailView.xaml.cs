// Mirrors: Bulwark components/layout/navigation-rail.tsx:164-221 (storage button + popover), :790-870 (account circles behind showRailAccountList, Add account, Sign out menu),
// components/layout/account-switcher.tsx:310-372 (the rail variant: a 40 px button, a 32 px avatar, a dot when other accounts have unread mail).

using Barbican.App.Motion;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Core.Storage;
using Barbican.Presentation.Accounts;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Shell;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Barbican.App.Views.Rail;

/// <summary>The app rail. Only displays <see cref="RailViewModel"/> and forwards a click to <see cref="ShellViewModel.TryActivate"/>.</summary>
public sealed partial class RailView : UserControl
{
    private readonly ShellViewModel _shell = ViewHost.Get<ShellViewModel>();
    private readonly IPrefsStore? _prefs = ViewHost.Find<IPrefsStore>();

    public RailView()
    {
        Rail = ViewHost.Get<RailViewModel>();
        Sidebar = ViewHost.Get<SidebarViewModel>();
        Bell = ViewHost.Get<Barbican.Presentation.Notifications.NotificationCenterViewModel>();
        Accounts = ViewHost.Get<AccountsViewModel>();
        Look = ViewHost.Get<StyleState>();
        InitializeComponent();
        ViewHost.Get<Barbican.App.Views.Accounts.AccountFlyoutKeys>().Track(AccountButton.Flyout!);
        Popover.Chosen += (_, _) => AccountButton.Flyout?.Hide();
        Look.PropertyChanged += (_, _) => ApplyAccountLook();
        Accounts.Rows.CollectionChanged += (_, _) => ApplyAccountLook();
        Accounts.PropertyChanged += (_, _) => ApplyAccountLook();
        Sidebar.PropertyChanged += OnSidebarChanged;
        Rail.PluginPages.CollectionChanged += (_, _) => BuildPluginPages();
        Look.PropertyChanged += (_, _) => ResizePluginPages();
        Rail.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(RailViewModel.ActiveApp), StringComparison.Ordinal) || string.Equals(e.PropertyName, nameof(RailViewModel.HasActivePluginPage), StringComparison.Ordinal))
            {
                SyncActive();
            }
        };
        if (_prefs is not null)
        {
            _prefs.Changed += (_, _) => DispatcherQueue.TryEnqueue(ApplyAccountLook);
        }

        Loaded += (_, _) => ApplyAccountLook();
        StyleSignOutMenu();
        BuildPluginPages();
        SyncActive();
        ApplyAccountLook();
    }

    public RailViewModel Rail { get; }

    public SidebarViewModel Sidebar { get; }

    public Barbican.Presentation.Notifications.NotificationCenterViewModel Bell { get; }

    public AccountsViewModel Accounts { get; }

    public StyleState Look { get; }

    // notification-center.tsx:68-75 (mark all read, clear) and :54-59 (an entry is read when opened; a mail entry opens its message)
    private void OnBellMarkAllRead(object sender, RoutedEventArgs e) => Bell.MarkAllRead();

    private void OnBellClear(object sender, RoutedEventArgs e) => Bell.Clear();

    private void OnBellEntryClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            BellButton.Flyout?.Hide();
            Bell.OpenEntry(id);
        }
    }

    private void OnComposeClick(object sender, RoutedEventArgs e) => Run(CommandIds.Compose);

    private void OnMailClick(object sender, RoutedEventArgs e) => Activate(AppId.Mail);

    private void OnCalendarClick(object sender, RoutedEventArgs e) => Activate(AppId.Calendar);

    private void OnContactsClick(object sender, RoutedEventArgs e) => Activate(AppId.Contacts);

    private void OnFilesClick(object sender, RoutedEventArgs e) => Activate(AppId.Files);

    // I-023, I-029: the rail's search and shortcuts buttons run the commands by id; a command nobody registered does nothing (search's arrives in wave 2)
    private void OnSearchClick(object sender, RoutedEventArgs e) => Run(CommandIds.GlobalSearch);

    private void OnShortcutsClick(object sender, RoutedEventArgs e) => Run(CommandIds.ShowShortcuts);

    private void OnAccessibilityClick(object sender, RoutedEventArgs e) => Run(CommandIds.OpenA11y);

    private static void Run(string commandId)
    {
        var registry = ViewHost.Find<ICommandRegistry>();
        if (registry is not null && registry.TryGet(commandId, out var command))
        {
            var context = new CommandContext(CommandSurface.Toolbar, [], null, null, null);
            if (command.CanExecute(context))
            {
                // not awaited, but what it throws still ends in an error toast and the crash log (Fable review 3 R3-05)
                (ViewHost.Find<CommandRunner>() ?? new CommandRunner(ViewHost.Find<Barbican.Presentation.Actions.IToastService>())).Start(command, context);
            }
        }
    }

    // The gear opens nothing yet: the settings pages are M6 (spec I-028).
    private void OnSettingsClick(object sender, RoutedEventArgs e) => Activate(AppId.Settings);

    // The nav buttons are toggles that only mirror the active app (navigation-rail.tsx:640-652 aria-current): a click never leaves one checked on its own.
    private void Activate(AppId app)
    {
        _shell.TryActivate(app);
        SyncActive();
    }

    // SH-10 (navigation-rail.tsx:459-512,700-720): one toggle per add-on page, built like the apps' (same size, style, icon size and in-window tooltip), checked while its page is on screen
    private void BuildPluginPages()
    {
        PluginPageList.Children.Clear();
        foreach (var item in Rail.PluginPages)
        {
            var button = new ToggleButton
            {
                Style = (Style)Resources["Rail.Toggle"],
                Width = Look.RailButton,
                Height = Look.RailButton,
                CornerRadius = Look.RailButtonRadius,
                Tag = item,
                IsChecked = item.IsActive,
                Content = new Controls.SIcon { Kind = item.IconKind, Size = Look.RailIcon },
            };
            button.Click += OnPluginPageClick;
            AutomationProperties.SetAutomationId(button, "rail.plugin." + item.Key);
            AutomationProperties.SetName(button, item.Title);
            Tip.SetText(button, item.Tooltip);
            item.PropertyChanged += (_, e) =>
            {
                if (string.Equals(e.PropertyName, nameof(PluginRailItem.IsActive), StringComparison.Ordinal))
                {
                    button.IsChecked = item.IsActive;
                }
            };
            PluginPageList.Children.Add(button);
        }
    }

    // the style (Bento, Nordic, Bulwark) changes the button, radius and icon sizes of every app: the page entries follow
    private void ResizePluginPages()
    {
        foreach (var child in PluginPageList.Children)
        {
            if (child is ToggleButton { Content: Controls.SIcon icon } button)
            {
                button.Width = Look.RailButton;
                button.Height = Look.RailButton;
                button.CornerRadius = Look.RailButtonRadius;
                icon.Size = Look.RailIcon;
            }
        }
    }

    private void OnPluginPageClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: PluginRailItem item } button)
        {
            Rail.OpenPluginPage(item);
            button.IsChecked = item.IsActive;
        }
    }

    private void SyncActive()
    {
        var page = Rail.HasActivePluginPage;
        MailButton.IsChecked = !page && Rail.ActiveApp == AppId.Mail;
        CalendarButton.IsChecked = !page && Rail.ActiveApp == AppId.Calendar;
        ContactsButton.IsChecked = !page && Rail.ActiveApp == AppId.Contacts;
        FilesButton.IsChecked = !page && Rail.ActiveApp == AppId.Files;
        SettingsButton.IsChecked = !page && Rail.ActiveApp == AppId.Settings;
    }

    // Flyouts are a Menu kind: Bulwark's popovers have no entrance animation (FM-14), and the radius is the shared one (navigation-rail.tsx:182 rounded-lg).
    private void OnAccountFlyoutOpened(object? sender, object e) => Present(Popover.Parent as FrameworkElement, Look.DialogRadius);

    private void OnStorageFlyoutOpened(object? sender, object e)
    {
        if (StorageButton.Flyout is Flyout { Content: FrameworkElement content })
        {
            Present(content.Parent as FrameworkElement, Look.DialogRadius);
        }

        SizeStorageBar();
    }

    private void OnSignOutFlyoutOpened(object? sender, object e)
    {
        SignOutAll.Visibility = Accounts.CanSignOutAll ? Visibility.Visible : Visibility.Collapsed;
        Present(SignOutMenu.Parent as FrameworkElement, Look.MenuRadius);
    }

    private static void Present(FrameworkElement? presenter, CornerRadius radius)
    {
        if (presenter is null)
        {
            return;
        }

        if (presenter is Control control)
        {
            control.CornerRadius = radius;
        }

        PopIn.Run(presenter, new Windows.Foundation.Point(1, 1), PopInKind.Menu);
    }

    private void OnStorageBarSizeChanged(object sender, SizeChangedEventArgs e) => SizeStorageBar();

    // the bar under the popover's figures: the arc's level colour, the used share of the track (navigation-rail.tsx:208-219)
    private void SizeStorageBar()
    {
        StorageBarFill.Width = Math.Max(0, StorageBar.ActualWidth * Math.Clamp(Rail.StorageFraction, 0, 1));
        StorageBarFill.Background = StyleState.RingBrush(Rail.StorageFraction);
    }

    private void OnSidebarChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(SidebarViewModel.StorageFraction), StringComparison.Ordinal))
        {
            SizeStorageBar();
        }
    }

    private void OnCircleClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AccountRowViewModel { IsActive: false } row })
        {
            Accounts.SwitchCommand.Execute(row);
        }
    }

    // hover:opacity-100 on a dimmed circle (navigation-rail.tsx:809)
    private void OnCircleEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            element.Opacity = 1;
        }
    }

    private void OnCircleExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AccountRowViewModel row } element)
        {
            element.Opacity = StyleState.AccountDim(row.IsActive);
        }
    }

    private void OnAddAccountClick(object sender, RoutedEventArgs e) => Accounts.AddAccountCommand.Execute(null);

    // both sign-outs ask first (the command opens the confirmation); the menu closes at once
    // the sign-out menu (navigation-rail.tsx:872-890) is declared in RailView.xaml (popup lint, ADR 0007); the item style and radius are applied here
    private void StyleSignOutMenu()
    {
        foreach (var item in new[] { SignOutOne, SignOutAll })
        {
            item.Style = Look.MenuItemStyle;
            item.CornerRadius = Look.MenuItemRadius;
        }
    }

    private void OnSignOutOne(object sender, RoutedEventArgs e)
    {
        SignOutButton.Flyout?.Hide();
        Accounts.SignOutCommand.Execute(null);
    }

    private void OnSignOutAll(object sender, RoutedEventArgs e)
    {
        SignOutButton.Flyout?.Hide();
        Accounts.SignOutAllCommand.Execute(null);
    }

    // Bulwark: a 32 px tinted avatar in a 40 px button, a dot for other accounts' unread mail, the account circles instead behind showRailAccountList.
    // Bento and Nordic keep the prototype's selection-filled account button inside the storage ring.
    private void ApplyAccountLook()
    {
        var bulwark = Look.IsBulwark;
        var active = Accounts.Rows.FirstOrDefault(r => r.IsActive);
        var list = bulwark && (_prefs?.Current.Bool("showRailAccountList", false) ?? false) && !(_prefs?.Current.Bool("simpleMode", false) ?? false) && Accounts.Rows.Count > 0;
        AccountStack.Visibility = list ? Visibility.Visible : Visibility.Collapsed;
        AccountSingle.Visibility = list ? Visibility.Collapsed : Visibility.Visible;
        AccountButton.Width = AccountButton.Height = bulwark ? Look.RailButton : Look.AccountSize;
        AccountButton.CornerRadius = bulwark ? Look.RailButtonRadius : Look.AccountRadius;
        AccountButton.Background = bulwark ? new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)) : AppResources.Brush("Sel");
        AccountAvatar.Width = AccountAvatar.Height = bulwark ? 32 : double.NaN;
        AccountAvatar.CornerRadius = new CornerRadius(bulwark ? 16 : 0);
        AccountAvatar.Background = bulwark ? Fmt.TintBg(active?.TintIndex ?? 0) : null;
        AccountInitials.Foreground = bulwark ? Fmt.TintFg(active?.TintIndex ?? 0) : AppResources.Brush("SelText");
        AccountInitials.FontSize = bulwark ? 12 : 13;
        OthersDot.Visibility = bulwark && Accounts.HasOthersUnread ? Visibility.Visible : Visibility.Collapsed;
    }
}
