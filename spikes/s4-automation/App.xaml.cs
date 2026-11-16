using Microsoft.UI.Xaml;

namespace S4;

public partial class App : Application
{
    public static MainWindow Main;
    public App()
    {
        var profile = Environment.GetEnvironmentVariable("BARBICAN_PROFILE") ?? "s4";
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BarbicanSpikeS4", profile, "WebView2"));
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Main = new MainWindow();
        if (Automation.Enabled) Hidden.ShowOffScreen(Main);   // never Activate(): that takes focus
        else Main.Activate();
        Automation.Start(Main);
    }
}
