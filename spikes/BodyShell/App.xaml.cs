namespace Barbican.Spike.BodyShell;

/// <summary>
/// Spike S2 app: one window with a native header and the body shell, shown off-screen and never
/// activated (Windows) or on the X display it was given (Linux, Xvfb). Runs <see cref="Scenario"/>,
/// writes the report and exits.
/// </summary>
public partial class SpikeApp : Application
{
    private Window? _window;

    public SpikeApp()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var outDir = Environment.GetEnvironmentVariable("BARBICAN_SPIKE_OUT");
        if (string.IsNullOrEmpty(outDir))
        {
            outDir = Path.Combine(AppContext.BaseDirectory, "spike-out");
        }

        Directory.CreateDirectory(outDir);
        // The spike's own WebView2 profile, never the Rust app's or a dev profile (CLAUDE.md).
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(outDir, "webview2-profile"));
        // An off-screen window counts as occluded for Chromium's occlusion tracker, which would stop
        // painting the page; the hidden automation window needs it to keep rendering (S4 note).
        Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "--disable-features=CalculateNativeWinOcclusion --disable-background-timer-throttling");

        Console.WriteLine("[spike] launched");
        var root = new SpikeRoot();
        _window = new Window { Title = "Barbican body shell spike", Content = root };
#if WINDOWS
        HiddenWindow.ShowOffScreen(_window, SpikeRoot.WindowWidth, SpikeRoot.WindowHeight);
#else
        _window.Activate();
#endif
        var scenario = new Scenario(root, _window, outDir);
        root.Loaded += (_, _) =>
        {
            Console.WriteLine("[spike] root loaded");
            _ = RunAndExitAsync(scenario);
        };
        _ = WatchdogAsync(scenario);
    }

    /// <summary>A probe that never answers must not hang the run: write what is known and exit.</summary>
    private static async Task WatchdogAsync(Scenario scenario)
    {
        await Task.Delay(TimeSpan.FromSeconds(180));
        Console.WriteLine("[spike] watchdog: 180 s passed, writing the partial report");
        await scenario.WriteReportAsync("watchdog: run exceeded 180 s; last step: " + scenario.Step);
        Environment.Exit(3);
    }

    private static async Task RunAndExitAsync(Scenario scenario)
    {
        var code = await scenario.RunAsync();
        Environment.Exit(code);
    }
}
