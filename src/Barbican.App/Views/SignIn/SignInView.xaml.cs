// Mirrors: Bulwark components/auth/login-form (Enter in a field submits the form); app/login.css:19-76 (the backdrop: the sidebar colour with a primary wash of 8 % from the top to 65 %),
// :97-110 (the card: a 1 px border, radius-lg 8 px, the card colour; WP-10); Fable review 2 R2-03 (the view binds to Presentation's
// SignInViewModel; no async void handler is left: the buttons run the ViewModel's commands, which report their own failures).

using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Accounts;
using Microsoft.UI.Xaml.Input;

namespace Barbican.App.Views.SignIn;

/// <summary>The sign-in screen; the logic is <see cref="SignInViewModel"/>.</summary>
public sealed partial class SignInView : UserControl
{
    public SignInView()
    {
        Model = ViewHost.Get<SignInViewModel>();
        Look = ViewHost.Get<StyleState>();
        InitializeComponent();
        var theme = ViewHost.Find<ThemeController>();
        Loaded += (_, _) =>
        {
            ApplyLook();
            Look.PropertyChanged += OnLookChanged;
            if (theme is not null) theme.CssApplied += OnCssApplied;
        };
        Unloaded += (_, _) =>
        {
            Look.PropertyChanged -= OnLookChanged;
            if (theme is not null) theme.CssApplied -= OnCssApplied;
        };
        ApplyLook();
        StatusText.Foreground = Fmt.Palette("Dim");
        Model.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(SignInViewModel.StatusIsError), StringComparison.Ordinal))
            {
                StatusText.Foreground = Fmt.Palette(Model.StatusIsError ? "RoseFg" : "Dim");
            }
        };
    }

    /// <summary>The colour the backdrop starts with (the title band takes it so the window reads as one surface), or null outside Bulwark.</summary>
    public Windows.UI.Color? WashColor { get; private set; }

    /// <summary>The backdrop was repainted (style or theme change).</summary>
    public event EventHandler? LookApplied;

    // the card is 400 px wide at most (login.css .login-panel max-width) with 16 px of page padding, and stays centred in a window of any height
    private void OnScrollerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        Card.Width = Math.Clamp(Scroller.ActualWidth - 32, 0, 400);
        Stage.MinHeight = Scroller.ActualHeight;
    }

    private void OnLookChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => ApplyLook();

    private void OnCssApplied(object? sender, ThemeCssEventArgs e) => DispatcherQueue.TryEnqueue(ApplyLook);

    // Bulwark: the sidebar colour with a faint primary wash from the top, a card with a hairline border and an 8 px radius; Bento and Nordic keep the plain ground and their pane shape.
    private void ApplyLook()
    {
        // AX-02 (button.tsx:30-37): with large targets every button keeps a 44 px hit area
        var target = Look.MinimumTarget;
        SignInButton.MinHeight = Math.Max(40, target);
        OAuthButton.MinHeight = Math.Max(40, target);
        StopButton.MinHeight = Math.Max(36, target);
        CancelButton.MinHeight = Math.Max(36, target);
        if (!Look.IsBulwark)
        {
            Backdrop.Background = AppResources.Brush("Ground");
            Card.CornerRadius = new CornerRadius(Look.PaneRadius.TopLeft);
            Card.BorderThickness = Look.PaneBorder;
            WashColor = null;
            LookApplied?.Invoke(this, EventArgs.Empty);
            return;
        }

        var side = AppResources.Brush("Side").Color;
        var accent = AppResources.Brush("Accent").Color;
        static byte Mix(byte a, byte b) => (byte)Math.Round((a * 0.92) + (b * 0.08));
        var wash = Windows.UI.Color.FromArgb(side.A, Mix(side.R, accent.R), Mix(side.G, accent.G), Mix(side.B, accent.B));
        var brush = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(0, 1) };
        brush.GradientStops.Add(new GradientStop { Color = wash, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = side, Offset = 0.65 });
        brush.GradientStops.Add(new GradientStop { Color = side, Offset = 1 });
        Backdrop.Background = brush;
        Card.CornerRadius = new CornerRadius(8);
        Card.BorderThickness = new Thickness(1);
        WashColor = wash;
        LookApplied?.Invoke(this, EventArgs.Empty);
    }

    public SignInViewModel Model { get; }

    public StyleState Look { get; }

    // Enter signs in once the form is complete; otherwise the field keeps the key (the shell routes nothing while this screen shows)
    private void OnFieldKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && Model.CanSignIn)
        {
            e.Handled = true;
            Model.Submit();
        }
    }
}
