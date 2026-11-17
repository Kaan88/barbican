// Mirrors: spec/layout/shell.md (regions, breakpoints); Bulwark hooks/use-keyboard-shortcuts.ts (shortcuts are ignored while typing in a field); M1-plan §6.4.

using Barbican.App.Theme;
using Barbican.Core.Accounts;
using Barbican.Presentation.Input;
using Microsoft.UI.Xaml.Input;

namespace Barbican.App.Shell;

/// <summary>The shell page; it only composes the regions and routes real key presses to the KeyRouter (PORT-PLAN.md §3.2).</summary>
public sealed partial class ShellPage : Page
{
    private readonly StyleState _look;
    private readonly ThemeController _theme;
    private readonly IKeyRouter? _keys;
    private readonly IAccountManager? _accounts;

    public ShellPage()
    {
        AppResources.EnsureStyles();
        _look = ViewHost.Get<StyleState>();
        _theme = ViewHost.Get<ThemeController>();
        _keys = ViewHost.Find<IKeyRouter>();
        _accounts = ViewHost.Find<IAccountManager>();
        InitializeComponent();
        _theme.Attach(Root);
        ApplyLook();
        _look.PropertyChanged += (_, _) => ApplyLook();
        SizeChanged += (_, e) => _look.SetWindowWidth(e.NewSize.Width);
        PreviewKeyDown += OnPreviewKeyDown;
        if (_accounts is not null)
        {
            _accounts.ActiveChanged += (_, _) => DispatcherQueue.TryEnqueue(ShowScreen);
            SignInRegion.Form.SignedIn += (_, _) => ShowScreen();
        }

        ShowScreen();
    }

    // The region frame follows the style and the window width: one pass, nothing is rebuilt.
    private void ApplyLook()
    {
        var layout = _look.Layout;
        MailScreen.Padding = _look.WindowPadding;
        MailScreen.ColumnSpacing = _look.Gap;
        SidebarRegion.Visibility = layout.SidebarShown ? Visibility.Visible : Visibility.Collapsed;
        GlanceRegion.Visibility = layout.SummaryTilesShown && _look.IsBento ? Visibility.Visible : Visibility.Collapsed;
    }

    // Sign-in shows when the accounts exist as a service and none is active; without the accounts stream (sample data) the mail screen shows.
    private void ShowScreen()
    {
        var signedOut = _accounts is not null && _accounts.Active is null;
        SignInRegion.Visibility = signedOut ? Visibility.Visible : Visibility.Collapsed;
        MailScreen.Visibility = signedOut ? Visibility.Collapsed : Visibility.Visible;
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
        if (chord is { } c && _keys.Handle(c, KeySource.Keyboard))
        {
            e.Handled = true;
        }
    }

    // A printable key typed into a field never reaches the router (mailcore note: KeyRouter.Handle ignores KeySource).
    private static bool IsTyping(object? source) => source is TextBox or PasswordBox or RichEditBox;

    private static bool IsDown(Windows.System.VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
}
