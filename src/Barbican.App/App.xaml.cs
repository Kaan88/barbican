using Barbican.App.Composition;
using Barbican.Presentation.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Barbican.App;

/// <summary>
/// The application. It decides the window mode once (<see cref="HiddenWindowOptions.Decide"/>: an
/// AUTOMATION build or a <c>--smoke</c> run is never shown normally), builds the composition root,
/// creates the main window with the shell and runs the features' startup tasks.
/// </summary>
public partial class BarbicanApp : Application
{
#if AUTOMATION
    private const bool AutomationBuild = true;
#else
    private const bool AutomationBuild = false;
#endif

    private Window? _window;
    private IHost? _host;

    public BarbicanApp()
    {
        InitializeComponent();
        UnhandledException += (_, e) => Console.Error.WriteLine("[barbican] unhandled: " + e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var launchedMs = ProcessClock.SinceStartMs();
        var options = HiddenWindowOptions.Decide(
            Environment.GetCommandLineArgs().Skip(1).ToArray(),
            Environment.GetEnvironmentVariable,
            AutomationBuild,
            OperatingSystem.IsWindows(),
            HiddenWindow.IsSupported);
        if (options.Error is not null)
        {
            Console.Error.WriteLine("[barbican] " + options.Error);
            if (options.IsSmoke)
            {
                SmokeRun.Write(options.SmokeReportPath!, new SmokeReport(false, options.Error, SmokeRun.Configuration(), OperatingSystem.IsWindows() ? "win" : "desktop", launchedMs, null, null, 2));
            }

            Environment.Exit(2);
            return;
        }

        if (options.IsHidden)
        {
            HiddenWebView.Prepare(options);
        }

        _host = AppComposition.Build(options);
        _window = new Window { Title = "Barbican" };
        UIElement content = new Shell.ShellPage();
        var smoke = options.IsSmoke ? new SmokeRun(_window, options, launchedMs) : null;
        if (smoke is not null)
        {
            content = smoke.Wrap(content);
        }

        _window.Content = content;
        // Window activation, visibility and closing reach the shell ViewModel (onWindowFocus/Blur, onVisibilityChange, onBeforeUnload).
        _host.Services.GetRequiredService<Shell.ShellLifecycle>().AttachWindow(_window);
        switch (options.Mode)
        {
            case HiddenWindowMode.OffScreen:
                // Before the first frame; never Activate() here (it takes focus and shows on screen).
                HiddenWindow.Show(_window, HiddenWindowOptions.HiddenWidth, HiddenWindowOptions.HiddenHeight);
                break;
            case HiddenWindowMode.PrivateDisplay:
                // Linux under the VM's private Xvfb only (Decide checked BARBICAN_XVFB and DISPLAY).
                _window.Activate();
                break;
            case HiddenWindowMode.Normal:
                _window.Activate();
                break;
        }

#if AUTOMATION
        Barbican.Automation.AutomationHost.Start(_window, _host.Services, options.AutomationPort);
#endif
        smoke?.Start();
        foreach (var task in _host.Services.GetServices<IStartupTask>())
        {
            _ = task.StartAsync(CancellationToken.None);
        }
    }
}
