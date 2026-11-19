// Mirrors: spec/layout/shell.md (regions, breakpoints); Bulwark hooks/use-keyboard-shortcuts.ts (shortcuts are ignored while typing in a field); M1-plan §6.4.
// Fable review 2: R2-05 (no shortcut behind the sign-in screen; Enter and Space belong to a focused button, toggle, combo box or a list item
// outside the message list, KeyGate); R2-01/R2-02 (the sign-in form shows for an account the server refused, with its server and login filled
// in, and never for a remembered account that only cannot reach its server: that one stays on the mail screen, "connecting"); R2-03 (the
// sign-in view and the popover are bound to Presentation's SignInViewModel and AccountsViewModel).

using Barbican.App.Motion;
using Barbican.App.Theme;
using Barbican.Core.Accounts;
using Barbican.Presentation.Accounts;
using Barbican.Presentation.Calendar.Grid;
using Barbican.Presentation.Input;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Shell;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Shell;

/// <summary>The shell page; it only composes the regions and routes real key presses to the KeyRouter (PORT-PLAN.md §3.2).</summary>
public sealed partial class ShellPage : Page
{
    private readonly StyleState _look;
    private readonly ThemeController _theme;
    private readonly IKeyRouter? _keys;
    private readonly IAccountManager? _accounts;
    private IAccountOverview? _overview;
    private SignInViewModel? _signIn;
    private readonly Task? _restore;
    private bool _adding;
    private AccountStatus? _requested;
    private string? _offered;
    private string? _dismissed;
    private bool? _appliedNordic;
    private readonly Barbican.Presentation.Compose.IComposerService? _composer;
    private bool _composing;
    private object? _dockSession;
    private bool _showDockMessage;
    private bool _readerFullscreen;
    private bool _composerMaximized;
    private Views.Calendar.CalendarScreen? _calendar;
    private readonly ShellViewModel? _shell;
    private readonly IReadOnlyList<IAppScreenFactory> _factories;
    private readonly Dictionary<AppId, UIElement> _appScreens = [];

    public ShellPage()
    {
        AppResources.EnsureStyles();
        _look = ViewHost.Get<StyleState>();
        _theme = ViewHost.Get<ThemeController>();
        _keys = ViewHost.Find<IKeyRouter>();
        _accounts = ViewHost.Find<IAccountManager>();
        _composer = ViewHost.Find<Barbican.Presentation.Compose.IComposerService>();
        _shell = ViewHost.Find<ShellViewModel>();
        _factories = ViewHost.Find<IEnumerable<IAppScreenFactory>>()?.ToList() ?? [];
#if AUTOMATION
        // a sample-data run (BARBICAN_SAMPLE=1) shows FakeJmap's seed through IActiveMail without signing in: no sign-in screen there
        if (ViewHost.Find<Barbican.Presentation.Mail.IActiveMail>() is Automation.SampleActiveMail)
        {
            _accounts = null;
        }
#endif

        // while the remembered accounts are restored the mail screen stays (no sign-in screen flashing up before the account is back)
        _restore = _accounts is null ? null : ViewHost.Find<AccountsStartup>()?.Completion;
        InitializeComponent();
        WireFullscreen();
        Tip.SetHost(Overlay);

        // M2 hotfix 2: an in-app drag shows the small badge everywhere in the window, not the platform's snapshot of the row; outside a drop target
        // nothing is accepted (the targets answer first and mark the event handled)
        Views.Actions.DragBadge.Attach(Overlay);
        Root.AllowDrop = true;
        Root.DragEnter += OnRootDragOver;
        Root.DragOver += OnRootDragOver;
        ShellOverlays.Fill(OverlayLayer);
        _theme.Attach(Root);
        ApplyLook();
        _look.PropertyChanged += (_, _) => ApplyLook();
        SidebarRegion.Panes.PropertyChanged += (_, _) =>
        {
            if (_composing && _look.IsBulwark) ApplyLook();
        };
        SizeChanged += (_, e) => _look.SetWindowWidth(e.NewSize.Width);
        ReaderColumn.SizeChanged += (_, _) => ApplyDockLayout();
        PreviewKeyDown += OnPreviewKeyDown;
        if (_composer is not null)
        {
            _composer.OpenChanged += (_, _) => DispatcherQueue.TryEnqueue(ApplyComposer);
        }

        CharacterReceived += OnCharacterReceived;
        if (_accounts is not null)
        {
            WireAccounts(_accounts);
        }

        WireCalendar();
        ApplyAppAvailability();
        ShowScreen();
        if (_restore is { IsCompleted: false } restoring)
        {
            _ = ShowWhenRestoredAsync(restoring);
        }

        Loaded += (_, _) => ViewHost.Get<ShellLifecycle>().Start(DispatcherQueue, _accounts?.Active?.Key);
    }

    public StyleState Look => _look;

    private void OnFloatingComposeClick(object sender, RoutedEventArgs e)
    {
        var registry = ViewHost.Find<ICommandRegistry>();
        if (registry is null || !registry.TryGet(CommandIds.Compose, out var command)) return;
        var context = new CommandContext(CommandSurface.Toolbar, [], null, null, ViewHost.Find<IActiveMail>()?.AccountKey);
        if (command.CanExecute(context))
        {
            (ViewHost.Find<CommandRunner>() ?? new CommandRunner(ViewHost.Find<Barbican.Presentation.Actions.IToastService>())).Start(command, context, toastFailure: true);
        }
    }

    // the sign-in screen follows the account rows (a refusal, R2-01), the active account and the popover's requests (R2-03)
    private void WireAccounts(IAccountManager accounts)
    {
        _overview = ViewHost.Find<IAccountOverview>();
        var signIn = SignInRegion.Model;
        _signIn = signIn;
        var switcher = ViewHost.Get<AccountsViewModel>();
        accounts.ActiveChanged += (_, _) => DispatcherQueue.TryEnqueue(() => { ApplyAppAvailability(); ShowScreen(); });
        if (_overview is not null)
        {
            _overview.StatusesChanged += (_, _) => DispatcherQueue.TryEnqueue(() => { ApplyAppAvailability(); ShowScreen(); });
        }

        signIn.SignedIn += (_, _) => Back(signedIn: true);
        signIn.Left += (_, _) => Back(signedIn: false);
        switcher.AddAccountRequested += (_, _) =>
        {
            _adding = true;
            _requested = null;
            signIn.Reset(AccountsViewModel.AddAccountStatus);
            ShowScreen();
        };
        switcher.SignInRequested += (_, e) =>
        {
            var status = e.Status;
            _adding = false;
            _requested = status;
            _offered = status.Account.Key;
            signIn.Prefill(status.Account, status.Error);
            ShowScreen();
        };
    }

    // The region frame follows the style and the window width: one pass, nothing is rebuilt. A style change also gets the prototype's short rise of the
    // mail screen (ApplyStyle animate: true), shortened to the enter token; the new layout is complete on the first frame and nothing waits for it.
    private void ApplyLook()
    {
        var layout = _look.Layout;
        var band = HasTitleBand;
        TitleRow.Height = new GridLength(band ? _look.TitleBarHeight : 0);
        var pad = _look.WindowPadding;
        MailScreen.Padding = band ? pad : new Thickness(pad.Left, pad.Bottom, pad.Right, pad.Bottom);
        MailScreen.ColumnSpacing = _look.Gap;
        if (!_look.IsBulwark) _composerMaximized = false;
        var fullscreen = _readerFullscreen && _look.IsBulwark;
        var hideMailPanes = fullscreen || _composerMaximized;
        RailRegion.Visibility = _composerMaximized ? Visibility.Collapsed : Visibility.Visible;
        ComposerRegion.SetMaximized(_composerMaximized);
        // Bulwark keeps the mailbox beside an in-page composer. Fold only when the
        // saved pane widths would leave too little room for the editor controls.
        var panes = SidebarRegion.Panes;
        var editorSpace = layout.WindowWidth - _look.RailWidth
            - (panes.SidebarWidth > 0 ? panes.SidebarWidth : _look.SidebarWidth)
            - (panes.ListWidth > 0 ? panes.ListWidth : layout.ListWidth);
        SidebarRegion.Visibility = layout.SidebarShown && !hideMailPanes
            && (!_composing || (_look.IsBulwark && editorSpace >= 480)) ? Visibility.Visible : Visibility.Collapsed;
        SidebarHandle.Visibility = SidebarRegion.Visibility;
        ListColumn.Visibility = hideMailPanes ? Visibility.Collapsed : Visibility.Visible;
        ListHandle.Visibility = ListColumn.Visibility;
        ReaderRegion.SetFullscreen(fullscreen);
        Surface.Background = _look.SurfaceBrush;
        Surface.BorderBrush = AppResources.Brush("Line");
        Surface.BorderThickness = _look.SurfaceBorder;
        Surface.CornerRadius = _look.SurfaceRadius;
        SurfaceGrid.ColumnSpacing = _look.Gap;
        ReaderColumn.RowSpacing = _look.Gap;
        GlanceRegion.Visibility = layout.SummaryTilesShown && _look.IsBento && !_composing ? Visibility.Visible : Visibility.Collapsed;
        ContextRegion.Visibility = layout.ContextShown && !_composing ? Visibility.Visible : Visibility.Collapsed;
        ApplyDockLayout();
        if (_appliedNordic is { } before && before != _look.IsNordic)
        {
            Rise.Run(MailScreen, MailShift, MotionRuntime.Scale.EnterMs);
        }

        _appliedNordic = _look.IsNordic;
    }

    private void WireFullscreen()
    {
        ComposerRegion.MaximizeRequested += (_, _) =>
        {
            if (!_composing || !_look.IsBulwark) return;
            _composerMaximized = !_composerMaximized;
            ApplyLook();
        };
        ReaderRegion.FullscreenRequested += (_, _) => SetReaderFullscreen(!_readerFullscreen);
        ReaderRegion.Reader.PropertyChanged += (_, e) =>
        {
            if (_readerFullscreen && ReaderRegion.Reader.IsEmpty &&
                string.Equals(e.PropertyName, nameof(ReaderViewModel.IsEmpty), StringComparison.Ordinal))
            {
                SetReaderFullscreen(false);
            }
        };
    }

    private void SetReaderFullscreen(bool enabled)
    {
        _readerFullscreen = enabled && _look.IsBulwark && !ReaderRegion.Reader.IsEmpty;
        ApplyLook();
    }

    // M3 composer-ui: the composer replaces the reader (the folders column collapses while it is open, the list stays; the summary tiles and the sender panel give way too); a reply that docks
    // keeps the reader above it. No app key runs while it is open: the composer has its own keys (Ctrl+Enter, Ctrl+S, Ctrl+Shift+C and B, Escape).
    private void ApplyComposer()
    {
        var composer = _composer?.Current;
        _composing = composer is not null;
        if (!ReferenceEquals(_dockSession, composer))
        {
            _dockSession = composer;
            _composerMaximized = false;
            _showDockMessage = false;
        }

        ComposerDockHost.Visibility = _composing ? Visibility.Visible : Visibility.Collapsed;
        ApplyLook();
    }

    // Bulwark shares 60% of a tall reader; a short reader alternates between
    // the message and the still-mounted composer (reply-dock.tsx:40-70, 148-210).
    private void ApplyDockLayout()
    {
        var docked = !_composerMaximized && _composer?.Current?.IsDocked == true;
        Grid.SetRow(ComposerDockHost, docked ? 2 : 0);
        Grid.SetRowSpan(ComposerDockHost, docked ? 1 : 3);
        var adaptive = _composing && docked && _look.IsBulwark;
        var layout = adaptive ? ReplyDockLayout.Compute(ReaderColumn.ActualHeight, _dockShare) : default;
        var takeover = adaptive && layout.Takeover;
        var showingMessage = takeover && _showDockMessage;
        DockResize.Visibility = adaptive && !takeover && layout.Height > 0 ? Visibility.Visible : Visibility.Collapsed;
        DockDisclosure.Visibility = takeover ? Visibility.Visible : Visibility.Collapsed;
        DockDisclosure.Content = showingMessage ? "Back to reply" : "Show the message";
        AutomationProperties.SetName(DockDisclosure, (string)DockDisclosure.Content);
        ComposerRegion.Visibility = showingMessage ? Visibility.Collapsed : Visibility.Visible;
        ReaderRegion.Visibility = _composing && (!docked || (takeover && !showingMessage)) ? Visibility.Collapsed : Visibility.Visible;
        ComposerDockHost.Height = !docked || showingMessage ? double.NaN
            : !adaptive ? 400
            : layout.Height > 0 ? layout.Height : double.NaN;
    }

    private void OnDockDisclosureClick(object sender, RoutedEventArgs e)
    {
        _showDockMessage = !_showDockMessage;
        ApplyDockLayout();
    }

    // The title band exists where the window's content is extended into its title bar: the WinUI head. The Linux head keeps the window manager's
    // title bar, so the band would be dead space there.
#if WINDOWS
    private static bool HasTitleBand => true;
#else
    private static bool HasTitleBand => false;
#endif

    /// <summary>
    /// Extends the content into the title bar (the prototype's look: no system title bar, the title band is the drag area) and keeps the caption
    /// buttons in the palette across theme changes. Only for a window shown normally; a hidden window has no title bar at all.
    /// </summary>
    public void AttachWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        ActiveWindow.Set(window);
#if WINDOWS
        window.ExtendsContentIntoTitleBar = true;
        window.SetTitleBar(TitleBand);
        void Caption()
        {
            var bar = window.AppWindow.TitleBar;
            var clear = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            bar.ButtonBackgroundColor = clear;
            bar.ButtonInactiveBackgroundColor = clear;
            bar.ButtonForegroundColor = AppResources.Brush("Dim").Color;
            bar.ButtonHoverBackgroundColor = AppResources.Brush("Hover").Color;
            bar.ButtonHoverForegroundColor = AppResources.Brush("Text").Color;
            bar.ButtonPressedBackgroundColor = AppResources.Brush("Pressed").Color;
            bar.ButtonPressedForegroundColor = AppResources.Brush("Text").Color;
            bar.ButtonInactiveForegroundColor = AppResources.Brush("Faint").Color;
        }

        Caption();
        _theme.CssApplied += (_, _) => Caption();
#else
        // the window manager draws the title bar (HasTitleBand)
        TitleBand.Visibility = Visibility.Collapsed;
#endif
    }

    // Sign-in shows when the accounts exist as a service and, once the remembered ones are restored: none is active; or the account on screen was
    // refused by its server (filled in, once per refusal; Cancel returns to the mail screen while another account exists); or the user asked for it
    // ("Add account", I-010, or a refused row in the popover). A remembered account that only cannot reach its server keeps the mail screen
    // ("connecting", R2-02). Without the accounts stream (sample data) the mail screen shows.
    private void ShowScreen()
    {
        var signedOut = false;
        if (_accounts is not null && _signIn is not null)
        {
            var restoring = _restore is { IsCompleted: false };
            var statuses = _overview?.Statuses ?? [];
            var active = _accounts.Active;
            var refused = statuses.FirstOrDefault(s => s.IsActive && s.NeedsSignIn) ?? (active is null ? statuses.FirstOrDefault(s => s.NeedsSignIn) : null);
            var offer = !restoring && refused is not null && !string.Equals(refused.Account.Key, _dismissed, StringComparison.Ordinal);
            if (offer && !_adding && _requested is null && !string.Equals(_offered, refused!.Account.Key, StringComparison.Ordinal))
            {
                _offered = refused.Account.Key;
                _signIn.Prefill(refused.Account, refused.Error);
            }

            signedOut = _adding || _requested is not null || offer || (!restoring && active is null);
            _signIn.CanLeave = _adding || _requested is not null || (offer && statuses.Count > 1);
        }

        SignInRegion.Visibility = signedOut ? Visibility.Visible : Visibility.Collapsed;
        var calendar = !signedOut && _shell?.ActiveApp == AppId.Calendar && _shell.AvailableApps.Contains(AppId.Calendar);
        var other = signedOut || _shell is null || calendar ? null : _factories.FirstOrDefault(f => f.App == _shell.ActiveApp && _shell.AvailableApps.Contains(f.App));
        MailScreen.Visibility = signedOut || calendar || other is not null ? Visibility.Collapsed : Visibility.Visible;
        ShowCalendar(calendar);
        ShowAppScreen(other);
    }

    // M5 S0: an app of the rail other than Mail and Calendar shows the screen of the factory registered for it (built on the first visit, then only hidden)
    private void ShowAppScreen(IAppScreenFactory? active)
    {
        if (active is not null && !_appScreens.ContainsKey(active.App))
        {
            var screen = active.Create();
            _appScreens[active.App] = screen;
            AppHost.Children.Add(screen);
        }

        foreach (var (app, screen) in _appScreens)
        {
            screen.Visibility = active is not null && active.App == app ? Visibility.Visible : Visibility.Collapsed;
        }

        AppHost.Visibility = active is null ? Visibility.Collapsed : Visibility.Visible;
        active?.OnShown();
    }

    // M5 S0: the rail enables an app while its factory says the login offers it; additive, so Calendar's own availability (CalendarAppAvailability) is left alone
    private void ApplyAppAvailability()
    {
        if (_shell is null)
        {
            return;
        }

        var apps = _shell.AvailableApps.ToList();
        var changed = false;
        foreach (var factory in _factories)
        {
            var has = factory.IsAvailable();
            if (has && !apps.Contains(factory.App))
            {
                apps.Add(factory.App);
                changed = true;
            }
            else if (!has && apps.Remove(factory.App))
            {
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        _shell.SetAvailableApps(apps.OrderBy(a => (int)a).ToList());
        if (_shell.ActiveApp != AppId.Mail && !_shell.AvailableApps.Contains(_shell.ActiveApp))
        {
            _ = _shell.TryActivate(AppId.Mail);
        }
    }

    // M4 cal-views: Calendar is in the rail's available apps while the login advertises calendars; opening it swaps the screen
    private void WireCalendar()
    {
        ViewHost.Find<CalendarAppAvailability>()?.Apply();
        if (_shell is null)
        {
            return;
        }

        _shell.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ShellViewModel.ActiveApp), StringComparison.Ordinal))
            {
                ShowScreen();
            }
        };
    }

    // the Calendar screen is built on the first visit (no cost at start-up) and then only hidden
    private void ShowCalendar(bool show)
    {
        if (show && _calendar is null)
        {
            _calendar = new Views.Calendar.CalendarScreen();
            CalendarHost.Children.Add(_calendar);
        }

        CalendarHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            _calendar?.OnShown();
        }
    }

    private void Back(bool signedIn)
    {
        if (signedIn)
        {
            _dismissed = null;
            _offered = null;
        }
        else if (!_adding && _requested is null)
        {
            // Cancel on the form offered for a refused account: keep the mail screen until it is refused again
            _dismissed = _offered;
        }

        _adding = false;
        _requested = null;
        ShowScreen();
    }

    private async Task ShowWhenRestoredAsync(Task restore)
    {
        // WhenAny never throws: a failed or cancelled restore also ends the wait (the sign-in screen then shows)
#pragma warning disable VSTHRD003 // AccountsStartup.Completion is a TaskCompletionSource with asynchronous continuations; waiting on it cannot deadlock the UI thread
        _ = await Task.WhenAny(restore).ConfigureAwait(true);
#pragma warning restore VSTHRD003
        DispatcherQueue.TryEnqueue(() => ShowScreen());
    }

    private static void OnRootDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
        Views.Actions.DragBadge.Apply(e.DragUIOverride);
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_readerFullscreen && _look.IsBulwark && !_composing &&
            e.Key == Windows.System.VirtualKey.Escape && !IsTyping(e.OriginalSource))
        {
            SetReaderFullscreen(false);
            e.Handled = true;
            return;
        }

        if (_keys is null || _composing || IsTyping(e.OriginalSource) || e.OriginalSource is DependencyObject source && (IsInside(source, SidebarHandle) || IsInside(source, ListHandle)))
        {
            return;
        }

        var chord = KeyChordMapper.MapKeyDown(
            (int)e.Key,
            IsDown(Windows.System.VirtualKey.Control),
            IsDown(Windows.System.VirtualKey.Shift),
            IsDown(Windows.System.VirtualKey.Menu));
        if (chord is { } c
            && (!e.KeyStatus.WasKeyDown || KeyGate.RepeatRoutes(c))
            && KeyGate.Routes(c, SignInRegion.Visibility == Visibility.Visible, Activates(e.OriginalSource))
            && _keys.Handle(c, KeySource.Keyboard))
        {
            e.Handled = true;
        }
    }

    // R2-11: punctuation ("/", "?", "!", "#") is the typed character, whatever the layout (a German "/" is Shift+7); the virtual key path leaves it out (KeyChordMapper.MapKeyDown)
    private void OnCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        if (_keys is null || _composing || IsTyping(e.OriginalSource))
        {
            return;
        }

        var chord = KeyChordMapper.MapCharacter(e.Character, IsDown(Windows.System.VirtualKey.Control), IsDown(Windows.System.VirtualKey.Menu));
        if (chord is { } c && KeyGate.Routes(c, SignInRegion.Visibility == Visibility.Visible, focusActivates: false) && _keys.Handle(c, KeySource.Keyboard))
        {
            e.Handled = true;
        }
    }

    // A printable key typed into a field never reaches the router (mailcore note: KeyRouter.Handle ignores KeySource).
    private static bool IsTyping(object? source) => source is TextBox or PasswordBox or RichEditBox;

    // The focused element takes Enter and Space itself: a button (also a toggle, check box or link button), a combo box, a toggle switch, or a
    // list item that is not a row of the message list (R2-05; WCAG 2.1.1). Enter on a message row still opens it.
    private bool Activates(object? source) => source switch
    {
        ButtonBase or ComboBox or ToggleSwitch => true,
        SelectorItem item => !IsInside(item, ListRegion),
        _ => false,
    };

    private static bool IsInside(DependencyObject element, DependencyObject ancestor)
    {
        for (var node = element; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDown(Windows.System.VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
}
