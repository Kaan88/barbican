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
        // first: every later failure, including one in InitializeComponent or the composition, leaves a crash log (M1 hotfix, sign-in)
        CrashGuard.Install(this);
        InitializeComponent();
    }

#pragma warning disable MA0051 // the start-up sequence reads top to bottom (as before the single-instance hand-off was added)
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

        // a mailto: link on the command line while another Barbican of this profile runs goes to that one, and this process ends without a window (M3 composer-ui, PORT-PLAN 3.9)
        if (!options.IsSmoke && Composition.SingleInstanceStart.ForwardLink(Environment.GetCommandLineArgs().Skip(1).ToArray()))
        {
            Environment.Exit(0);
            return;
        }

        // Select the profile before host composition; each WebView gets explicit environment options when it starts (F-13).
        WebViewEnvironment.Prepare(options);

        _host = AppComposition.Build(options);
        _window = new Window { Title = "Barbican" };
        var shell = new Shell.ShellPage();
        UIElement content = shell;
        var smoke = options.IsSmoke ? new SmokeRun(_window, options, launchedMs) : null;
        if (smoke is not null)
        {
            content = smoke.Wrap(content);
        }

        _window.Content = content;
        // Window activation, visibility and closing reach the shell ViewModel (onWindowFocus/Blur, onVisibilityChange, onBeforeUnload).
        _host.Services.GetRequiredService<Shell.ShellLifecycle>().AttachWindow(_window);
        _host.Services.GetRequiredService<Shell.WindowCloseController>().Attach(_window);
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
                shell.AttachWindow(_window); // the prototype's chrome: content in the title bar, palette caption buttons
                _window.Activate();
                break;
        }

#if AUTOMATION
        StartAutomation(shell, options.AutomationPort);
#endif
        smoke?.Start();
        foreach (var task in _host.Services.GetServices<IStartupTask>())
        {
            _ = task.StartAsync(CancellationToken.None);
        }
    }

#pragma warning restore MA0051
#if AUTOMATION
    private void StartAutomation(Shell.ShellPage shell, int port)
    {
#if WINDOWS
        Views.Compose.Automation.ComposerAutomation.WindowAction = shell.InspectComposerWindow;
        Shell.Automation.CaptionCommand.Use(_window!, shell);
#else
        _ = shell;   // the caption command is Windows only
#endif
        Barbican.Automation.AutomationHost.Start(_window!, _host!.Services, port,
            () => _host.Services.GetRequiredService<Shell.WindowCloseController>().CloseAsync(_window!));
    }
#endif
}
