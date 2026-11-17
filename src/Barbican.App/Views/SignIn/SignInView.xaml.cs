using Barbican.App.Shell;
using Barbican.App.Theme;

namespace Barbican.App.Views.SignIn;

/// <summary>The sign-in screen; the logic is <see cref="SignInFormModel"/>.</summary>
public sealed partial class SignInView : UserControl
{
    public SignInView()
    {
        Form = ViewHost.Get<SignInFormModel>();
        Look = ViewHost.Get<StyleState>();
        InitializeComponent();
        StatusText.Foreground = Fmt.Palette("Dim");
        Form.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(SignInFormModel.IsError), StringComparison.Ordinal))
            {
                StatusText.Foreground = Fmt.Palette(Form.IsError ? "RoseFg" : "Dim");
            }
        };
    }

    public SignInFormModel Form { get; }

    public StyleState Look { get; }

#pragma warning disable VSTHRD100 // XAML event handlers must return void; the awaited call reports its own failure through the ViewModel
    private async void OnSignInClick(object sender, RoutedEventArgs e) => await Form.SignInAsync();
#pragma warning restore VSTHRD100

#pragma warning disable VSTHRD100 // XAML event handlers must return void; the awaited call reports its own failure through the ViewModel
    private async void OnOAuthClick(object sender, RoutedEventArgs e) => await Form.SignInWithOAuthAsync();
#pragma warning restore VSTHRD100
}
