// Mirrors: Bulwark components/auth/login-form (Enter in a field submits the form); Fable review 2 R2-03 (the view binds to Presentation's
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
        StatusText.Foreground = Fmt.Palette("Dim");
        Model.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(SignInViewModel.StatusIsError), StringComparison.Ordinal))
            {
                StatusText.Foreground = Fmt.Palette(Model.StatusIsError ? "RoseFg" : "Dim");
            }
        };
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
