using System;
using Microsoft.UI.Xaml;

namespace Barbican;

public partial class App : Application
{
    public static MainWindow Main;
    public App()
    {
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER",
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BarbicanWinUI", "WebView2"));
        InitializeComponent();
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Main = new MainWindow();
        Main.Activate();
    }
}
