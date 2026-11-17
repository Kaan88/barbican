// Mirrors: spec/layout/shell.md (regions, breakpoints); Bulwark hooks/use-keyboard-shortcuts.ts (shortcuts are ignored while typing in a field); M1-plan §6.4.
// Fable review 2: R2-05 (no shortcut behind the sign-in screen; Enter and Space belong to a focused button, toggle, combo box or a list item
// outside the message list, KeyGate); R2-01/R2-02 (the sign-in form shows for an account the server refused, with its server and login filled
// in, and never for a remembered account that only cannot reach its server: that one stays on the mail screen, "connecting"); R2-03 (the
// sign-in view and the popover are bound to Presentation's SignInViewModel and AccountsViewModel).

using Barbican.App.Motion;
using Barbican.App.Theme;
using Barbican.Core.Accounts;
using Barbican.Presentation.Accounts;
using Barbican.Presentation.Input;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

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

    public ShellPage()
    {
        AppResources.EnsureStyles();
        _look = ViewHost.Get<StyleState>();
        _theme = ViewHost.Get<ThemeController>();
        _keys = ViewHost.Find<IKeyRouter>();
        _accounts = ViewHost.Find<IAccountManager>();
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
        Tip.SetHost(Overlay);
        ShellOverlays.Fill(OverlayLayer);
        _theme.Attach(Root);
        ApplyLook();
        _look.PropertyChanged += (_, _) => ApplyLook();
        SizeChanged += (_, e) => _look.SetWindowWidth(e.NewSize.Width);
        PreviewKeyDown += OnPreviewKeyDown;
        if (_accounts is not null)
        {
            WireAccounts(_accounts);
        }

        ShowScreen();
        if (_restore is { IsCompleted: false } restoring)
        {
            _ = ShowWhenRestoredAsync(restoring);
        }

        Loaded += (_, _) => ViewHost.Get<ShellLifecycle>().Start(DispatcherQueue, _accounts?.Active?.Key);
    }

    // the sign-in screen follows the account rows (a refusal, R2-01), the active account and the popover's requests (R2-03)
    private void WireAccounts(IAccountManager accounts)
    {
        _overview = ViewHost.Find<IAccountOverview>();
        var signIn = SignInRegion.Model;
        _signIn = signIn;
        var switcher = ViewHost.Get<AccountsViewModel>();
        accounts.ActiveChanged += (_, _) => DispatcherQueue.TryEnqueue(() => ShowScreen());
        if (_overview is not null)
        {
            _overview.StatusesChanged += (_, _) => DispatcherQueue.TryEnqueue(() => ShowScreen());
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
        SidebarRegion.Visibility = layout.SidebarShown ? Visibility.Visible : Visibility.Collapsed;
        Surface.Background = _look.SurfaceBrush;
        Surface.BorderBrush = AppResources.Brush("Line");
        Surface.BorderThickness = _look.SurfaceBorder;
        Surface.CornerRadius = _look.SurfaceRadius;
        SurfaceGrid.ColumnSpacing = _look.Gap;
        ReaderColumn.RowSpacing = _look.Gap;
        GlanceRegion.Visibility = layout.SummaryTilesShown && _look.IsBento ? Visibility.Visible : Visibility.Collapsed;
        ContextRegion.Visibility = layout.ContextShown ? Visibility.Visible : Visibility.Collapsed;
        if (_appliedNordic is { } before && before != _look.IsNordic)
        {
            Rise.Run(MailScreen, MailShift, MotionRuntime.Scale.EnterMs);
        }

        _appliedNordic = _look.IsNordic;
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
        MailScreen.Visibility = signedOut ? Visibility.Collapsed : Visibility.Visible;
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

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_keys is null || IsTyping(e.OriginalSource))
        {
            return;
        }

        var chord = KeyChordMapper.Map(
            (int)e.Key,
            IsDown(Windows.System.VirtualKey.Control),
            IsDown(Windows.System.VirtualKey.Shift),
            IsDown(Windows.System.VirtualKey.Menu));
        if (chord is { } c
            && KeyGate.Routes(c, SignInRegion.Visibility == Visibility.Visible, Activates(e.OriginalSource))
            && _keys.Handle(c, KeySource.Keyboard))
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
