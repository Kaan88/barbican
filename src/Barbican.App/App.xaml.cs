namespace Barbican.App;

/// <summary>The application: creates the main window with the shell. Composition and DI arrive in M1.</summary>
public partial class BarbicanApp : Application
{
    private Window? _window;

    public BarbicanApp()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new Window { Title = "Barbican", Content = new Shell.ShellPage() };
        _window.Activate();
    }
}
