namespace Barbican.Spikes.S3;

public sealed partial class App : Microsoft.UI.Xaml.Application
{
    private HarnessWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var argv = Environment.GetCommandLineArgs();
        var outDir = argv.Length > 1 ? argv[1] : Path.Combine(AppContext.BaseDirectory, "s3-out");
        Directory.CreateDirectory(outDir);
        _window = new HarnessWindow(outDir);
        _window.Start();
    }
}
